using System.IO;
using Google.Cloud.Firestore;
using MCPhoto.Core.Frames;
using MCPhoto.Core.Models;
using MCPhoto.Firebase.Dto;
using Microsoft.Extensions.Logging;

namespace MCPhoto.Firebase;

/// <summary>
/// FrameTemplate CRUD. Firestore frameTemplates + Storage frames/{userId}/. (firebase-contract §2.2/§4.1)
/// Firebase 미초기화 시 빈 목록(오프라인 게스트+번들 모드).
/// </summary>
public sealed class FrameRepository : IFrameRepository
{
    private const string Collection = "frameTemplates";
    private const int MaxPerUser = 10;

    private readonly FirebaseClient _client;
    private readonly ILogger<FrameRepository>? _logger;

    public FrameRepository(FirebaseClient client, ILogger<FrameRepository>? logger = null)
    {
        _client = client;
        _logger = logger;
    }

    private FirestoreDb? Db => _client.Firestore;

    public async Task<IReadOnlyList<FrameTemplate>> GetDefaultFramesAsync(CancellationToken ct = default)
    {
        if (Db is null) return Array.Empty<FrameTemplate>();
        var snap = await Db.Collection(Collection).WhereEqualTo("isDefault", true).GetSnapshotAsync(ct);
        return snap.Documents.Select(d => ToTemplate(d.ConvertTo<FrameTemplateDoc>())).ToList();
    }

    public async Task<IReadOnlyList<FrameTemplate>> GetUserFramesAsync(string userId, CancellationToken ct = default)
    {
        if (Db is null) return Array.Empty<FrameTemplate>();
        var snap = await Db.Collection(Collection).WhereEqualTo("userId", userId).GetSnapshotAsync(ct);
        return snap.Documents.Select(d => ToTemplate(d.ConvertTo<FrameTemplateDoc>())).ToList();
    }

    public async Task<FrameTemplate> SaveAsync(FrameTemplate frame, byte[] imageBytes, CancellationToken ct = default)
    {
        EnsureInit();

        // 계정당 10개 제한(§9 #6). userId 있을 때만.
        if (!string.IsNullOrEmpty(frame.UserId))
        {
            var existing = await GetUserFramesAsync(frame.UserId!, ct);
            if (existing.Count >= MaxPerUser && existing.All(f => f.Id != frame.Id))
                throw new InvalidOperationException($"프레임은 계정당 최대 {MaxPerUser}개까지 저장할 수 있습니다.");
        }

        if (string.IsNullOrEmpty(frame.Id))
            frame.Id = Guid.NewGuid().ToString();

        // 이미지 업로드: frames/{userId}/{frameId}.png (TTL 비대상)
        var owner = frame.UserId ?? "default";
        var storagePath = $"frames/{owner}/{frame.Id}.png";
        var tmp = Path.Combine(Path.GetTempPath(), $"mcphoto_frame_{frame.Id}.png");
        await File.WriteAllBytesAsync(tmp, imageBytes, ct);
        try
        {
            var token = await _client.UploadFileAsync(storagePath, tmp, "image/png", ct: ct);
            frame.ImageUrl = MCPhoto.Core.Upload.UploadContract.TokenDownloadUrl(_client.Bucket, storagePath, token);
        }
        finally { try { File.Delete(tmp); } catch { /* 무시 */ } }

        var doc = ToDoc(frame);
        await Db!.Collection(Collection).Document(frame.Id).SetAsync(doc, cancellationToken: ct);
        return frame;
    }

    /// <summary>레거시(Admin)는 SetAsync(frame.Id)로 같은 문서 덮어쓰기가 성립 → update 지원. (item2 §5.1)</summary>
    public bool SupportsUpdateById => true;

    /// <summary>
    /// 기존 공용 기본 프레임 업데이트(같은 frame.Id). name·slots·imageSize 갱신, id·userId(null)·isDefault(true)·
    /// createdAt은 기존 문서에서 보존. replaceImage=true면 같은 Storage 키에 이미지 재업로드(덮어쓰기). (item2 §5.1)
    /// 서버 updateFrame(services/frames.ts)의 보존 규칙과 정합.
    /// </summary>
    public async Task<FrameTemplate> UpdateAsync(FrameTemplate frame, byte[] imageBytes, bool replaceImage, CancellationToken ct = default)
    {
        EnsureInit();
        if (string.IsNullOrEmpty(frame.Id))
            throw new InvalidOperationException("업데이트할 프레임 id가 없습니다.");

        var docRef = Db!.Collection(Collection).Document(frame.Id);
        var snap = await docRef.GetSnapshotAsync(ct);
        if (!snap.Exists)
            throw new InvalidOperationException("프레임을 찾을 수 없습니다.");

        var current = snap.ConvertTo<FrameTemplateDoc>();
        // 공용 기본 프레임만 업데이트 대상(서버 규칙과 동일: userId=null·isDefault=true 보존).
        if (!string.IsNullOrEmpty(current.UserId) || !current.IsDefault)
            throw new InvalidOperationException("공용 기본 프레임만 업데이트할 수 있습니다.");

        // 보존 필드: id·userId(null)·isDefault(true)·createdAt. 갱신 필드: name·slots·imageSize·imageUrl.
        frame.UserId = null;
        frame.IsDefault = true;
        frame.CreatedAt = current.CreatedAt.ToDateTime();
        frame.ImageUrl = current.ImageUrl; // 기본은 기존 이미지 URL 유지

        if (replaceImage)
        {
            // 같은 Storage 키(frames/default/{id}.png) 덮어쓰기 → 새 다운로드 토큰 URL로 갱신.
            var storagePath = $"frames/default/{frame.Id}.png";
            var tmp = Path.Combine(Path.GetTempPath(), $"mcphoto_frame_{frame.Id}.png");
            await File.WriteAllBytesAsync(tmp, imageBytes, ct);
            try
            {
                var token = await _client.UploadFileAsync(storagePath, tmp, "image/png", ct: ct);
                frame.ImageUrl = MCPhoto.Core.Upload.UploadContract.TokenDownloadUrl(_client.Bucket, storagePath, token);
            }
            finally { try { File.Delete(tmp); } catch { /* 무시 */ } }
        }

        var doc = ToDoc(frame);
        await docRef.SetAsync(doc, cancellationToken: ct);
        _logger?.LogInformation("프레임 서버 업데이트 완료 id={Id} (이미지교체={Replace})", frame.Id, replaceImage);
        return frame;
    }

    public async Task<bool> DeleteAsync(string frameId, CancellationToken ct = default)
    {
        EnsureInit();
        var docRef = Db!.Collection(Collection).Document(frameId);

        // 문서 존재 확인(Firestore는 없는 문서 삭제 시 예외 없이 no-op이므로, 성공 오인 방지용으로 먼저 확인).
        var snap = await docRef.GetSnapshotAsync(ct);
        if (!snap.Exists)
        {
            _logger?.LogWarning("프레임 서버 삭제: 문서 없음 id={Id} (로컬 id와 서버 문서 id 불일치 가능)", frameId);
            return false;
        }

        // owner를 읽어 Storage 경로 재구성(저장 규약 frames/{owner}/{frameId}.png). 문서 삭제 전에 읽어야 경로 확인(고아 이미지 방지).
        try
        {
            var dto = snap.ConvertTo<FrameTemplateDoc>();
            var owner = string.IsNullOrEmpty(dto.UserId) ? "default" : dto.UserId!;
            await _client.DeleteStoragePrefixAsync($"frames/{owner}/{frameId}.png", ct);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "프레임 Storage 이미지 삭제 실패(문서는 계속 삭제): {Id}", frameId);
        }

        await docRef.DeleteAsync(cancellationToken: ct);
        _logger?.LogInformation("프레임 서버 삭제 완료 id={Id}", frameId);
        return true;
    }

    public async Task DeleteAllByUserAsync(string userId, CancellationToken ct = default)
    {
        if (Db is null) return;
        var snap = await Db.Collection(Collection).WhereEqualTo("userId", userId).GetSnapshotAsync(ct);
        foreach (var d in snap.Documents)
        {
            try { await d.Reference.DeleteAsync(cancellationToken: ct); }
            catch (Exception ex) { _logger?.LogWarning(ex, "프레임 문서 삭제 실패: {Id}", d.Id); }
        }
        // Storage frames/{userId}/ 전체 삭제(§F8 cascade)
        try { await _client.DeleteStoragePrefixAsync($"frames/{userId}/", ct); }
        catch (Exception ex) { _logger?.LogWarning(ex, "프레임 Storage 삭제 실패: {User}", userId); }
    }

    private static FrameTemplate ToTemplate(FrameTemplateDoc d)
    {
        var t = new FrameTemplate
        {
            Id = d.Id,
            UserId = d.UserId,
            IsDefault = d.IsDefault,
            Name = d.Name,
            ImageUrl = d.ImageUrl,
            ImageSize = new ImageSize
            {
                Width = ToInt(d.ImageSize.GetValueOrDefault("width")),
                Height = ToInt(d.ImageSize.GetValueOrDefault("height"))
            },
            CreatedAt = d.CreatedAt.ToDateTime()
        };
        foreach (var s in d.Slots)
        {
            t.Slots.Add(new Slot
            {
                Index = ToInt(s.GetValueOrDefault("index")),
                X = ToInt(s.GetValueOrDefault("x")),
                Y = ToInt(s.GetValueOrDefault("y")),
                Width = ToInt(s.GetValueOrDefault("width")),
                Height = ToInt(s.GetValueOrDefault("height"))
            });
        }
        return t;
    }

    private static FrameTemplateDoc ToDoc(FrameTemplate t) => new()
    {
        Id = t.Id,
        UserId = t.UserId,
        IsDefault = t.IsDefault,
        Name = t.Name,
        ImageUrl = t.ImageUrl,
        ImageSize = new Dictionary<string, object> { ["width"] = t.ImageSize.Width, ["height"] = t.ImageSize.Height },
        Slots = t.Slots.Select(s => new Dictionary<string, object>
        {
            ["index"] = s.Index, ["x"] = s.X, ["y"] = s.Y, ["width"] = s.Width, ["height"] = s.Height
        }).ToList(),
        CreatedAt = Timestamp.FromDateTime(t.CreatedAt.ToUniversalTime())
    };

    private static int ToInt(object? v) => v switch
    {
        long l => (int)l,
        int i => i,
        double d => (int)d,
        _ => 0
    };

    private void EnsureInit()
    {
        if (Db is null)
            throw new InvalidOperationException("Firebase 미초기화 — 프레임 저장 불가(서비스 계정 키 필요).");
    }
}
