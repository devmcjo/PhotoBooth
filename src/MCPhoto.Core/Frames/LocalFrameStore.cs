using System.Text;
using MCPhoto.Core.Models;

namespace MCPhoto.Core.Frames;

/// <summary>
/// 로컬 프레임 파일 저장소. 루트 = 실행 폴더 Frame\ (번들+파워캐시+user 공존, it8 §3 정정).
/// 레이아웃(단일 폴더, 접두 규칙으로 구분):
///   공용(번들·파워캐시) = `{이름}.png` (접두 없음) → 게스트 포함 노출.
///   user 전용          = `{계정}_{이름}.png` (`{계정}_` 접두) → 본인 로그인 시만.
/// 이름 원문 그대로 저장(sanitize 없음). 파일시스템 금지문자만 저장 거부.
/// .slots: 첫 줄 "#imagesize=W,H" + 이후 "index,x,y,w,h"(하위호환 5필드).
/// </summary>
public sealed class LocalFrameStore : ILocalFrameStore
{
    private readonly string _root;

    /// <param name="rootFolder">실행 폴더 Frame\ (AppContext.BaseDirectory\Frame). BundleFolder와 동일.</param>
    public LocalFrameStore(string rootFolder) => _root = rootFolder;

    public FrameTemplate SaveLocal(FrameTemplate frame, byte[] png, string? ownerName)
        => WriteFrame(frame, png, ownerName, dbId: ownerName is null ? frame.Id : null);

    public FrameTemplate CacheFromDb(FrameTemplate frame, byte[] png)
        => WriteFrame(frame, png, ownerName: null, dbId: frame.Id); // 공용 캐시(접두 없음), DB id 보존

    private FrameTemplate WriteFrame(FrameTemplate frame, byte[] png, string? ownerName, string? dbId)
    {
        // 이름·계정에 파일시스템 금지문자가 있으면 저장 거부(sanitize 아님 — 원문 그대로 규칙).
        EnsureFileNameSafe(frame.Name);
        string baseName;
        if (!string.IsNullOrWhiteSpace(ownerName))
        {
            EnsureFileNameSafe(ownerName);
            baseName = $"{ownerName}_{frame.Name}"; // user 전용 접두
        }
        else
        {
            baseName = frame.Name; // 공용(접두 없음)
        }

        Directory.CreateDirectory(_root);
        var pngPath = Path.Combine(_root, baseName + ".png");
        var slotsPath = Path.Combine(_root, baseName + ".slots");

        File.WriteAllBytes(pngPath, png);
        // 공용 캐시는 DB 문서 id를 메타로 보존(삭제 시 서버 매칭용). user는 dbId 없음(로컬 전용).
        File.WriteAllText(slotsPath, SerializeSlots(frame, dbId), Encoding.UTF8);

        frame.ImageUrl = pngPath;   // 로컬 경로 반영(로딩·표시용)
        frame.IsDefault = ownerName is null; // 공용/캐시 = 기본, user = 비기본
        return frame;
    }

    public IReadOnlyList<FrameTemplate> LoadPublic()
    {
        // 공용 = 접두 없는 파일(번들 + 파워 캐시). 접두 판별: 파일명에 '_'가 없으면 공용.
        // ('{계정}_{이름}' user 파일은 '_'를 포함 → 공용에서 제외. 이름 자체의 '_'는 모호성 수용, §3.1.1.)
        return EnumerateFrames(name => !name.Contains('_'), ownerId: null);
    }

    public IReadOnlyList<FrameTemplate> LoadUser(string ownerName)
    {
        if (string.IsNullOrWhiteSpace(ownerName)) return Array.Empty<FrameTemplate>();
        var prefix = ownerName + "_";
        // user = '{계정}_' 접두 파일. displayName은 접두 제거.
        return EnumerateFrames(name => name.StartsWith(prefix, StringComparison.Ordinal), ownerId: ownerName);
    }

    public IReadOnlySet<string> PublicFrameNames()
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (!Directory.Exists(_root)) return set;
        foreach (var png in Directory.EnumerateFiles(_root, "*.png"))
        {
            var name = Path.GetFileNameWithoutExtension(png);
            if (!name.Contains('_')) set.Add(name); // 공용만
        }
        return set;
    }

    public bool DeleteLocal(FrameTemplate frame)
    {
        var pngPath = !string.IsNullOrEmpty(frame.ImageUrl) && File.Exists(frame.ImageUrl)
            ? frame.ImageUrl
            : null;
        if (pngPath is null) return false;

        var slotsPath = Path.ChangeExtension(pngPath, ".slots");
        try { if (File.Exists(slotsPath)) File.Delete(slotsPath); } catch { /* 무시 */ }
        try { File.Delete(pngPath); } catch { /* 잠김 등 — 아래에서 존재 여부로 판정 */ }
        // png가 실제로 사라졌는지로 성공을 판정(잠금·부분 삭제를 정직하게 반환).
        return !File.Exists(pngPath);
    }

    // ── 내부 ──

    private List<FrameTemplate> EnumerateFrames(Func<string, bool> nameFilter, string? ownerId)
    {
        var list = new List<FrameTemplate>();
        if (!Directory.Exists(_root)) return list;

        foreach (var png in Directory.EnumerateFiles(_root, "*.png"))
        {
            var fileName = Path.GetFileNameWithoutExtension(png);
            if (!nameFilter(fileName)) continue;

            var slotsPath = Path.ChangeExtension(png, ".slots");
            if (!File.Exists(slotsPath)) continue; // slots 없으면 로컬 프레임 아님(스킵)

            // displayName: user면 접두 제거, 공용이면 파일명 그대로.
            var displayName = ownerId is not null && fileName.StartsWith(ownerId + "_", StringComparison.Ordinal)
                ? fileName.Substring(ownerId.Length + 1)
                : fileName;

            var (imageSize, slots, dbId) = ParseSlots(File.ReadAllLines(slotsPath));
            // Id: 공용 캐시는 DB 문서 id 보존(서버 삭제 매칭), 없으면 로컬 식별자.
            var id = !string.IsNullOrEmpty(dbId) ? dbId! : $"local:{fileName}";
            list.Add(new FrameTemplate
            {
                Id = id,
                Name = displayName,
                UserId = ownerId,
                IsDefault = ownerId is null,
                ImageUrl = png,
                ImageSize = imageSize,
                Slots = slots
            });
        }
        return list;
    }

    /// <summary>
    /// 파일시스템 금지문자(\ / : * ? " &lt; &gt; |) 포함 시 저장 거부. sanitize 아님(원문 유지).
    /// 판정은 <see cref="FrameNaming.IsFileNameSafe"/>(순수 함수)에 위임한다 — 저장 전 선검증(VM)과
    /// 같은 기준을 쓰기 위한 단일 출처. 빈 이름/금지문자 두 갈래 예외 메시지는 그대로 유지한다.
    /// </summary>
    private static void EnsureFileNameSafe(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new IOException("이름이 비어 있습니다.");
        if (!FrameNaming.IsFileNameSafe(value))
            throw new IOException($"이름에 사용할 수 없는 문자가 있습니다: {value}");
    }

    /// <summary>슬롯 직렬화: #imagesize 메타 (+ 공용 캐시면 #dbid) + 5필드 슬롯들.</summary>
    private static string SerializeSlots(FrameTemplate frame, string? dbId)
    {
        var sb = new StringBuilder();
        sb.Append("#imagesize=").Append(frame.ImageSize.Width).Append(',').Append(frame.ImageSize.Height).Append('\n');
        if (!string.IsNullOrEmpty(dbId))
            sb.Append("#dbid=").Append(dbId).Append('\n'); // 서버 삭제 매칭용(공용 캐시/생성)
        foreach (var s in frame.Slots)
            sb.Append(s.Index).Append(',').Append(s.X).Append(',').Append(s.Y).Append(',')
              .Append(s.Width).Append(',').Append(s.Height).Append('\n');
        return sb.ToString();
    }

    /// <summary>슬롯 파싱: #imagesize·#dbid 메타(있으면) + "index,x,y,w,h" 줄들(하위호환).</summary>
    private static (ImageSize size, List<Slot> slots, string? dbId) ParseSlots(string[] lines)
    {
        var size = new ImageSize();
        var slots = new List<Slot>();
        string? dbId = null;
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith("#imagesize=", StringComparison.OrdinalIgnoreCase))
            {
                var wh = line.Substring("#imagesize=".Length).Split(',');
                if (wh.Length == 2 && int.TryParse(wh[0], out var w) && int.TryParse(wh[1], out var h))
                {
                    size.Width = w;
                    size.Height = h;
                }
                continue;
            }
            if (line.StartsWith("#dbid=", StringComparison.OrdinalIgnoreCase))
            {
                dbId = line.Substring("#dbid=".Length).Trim();
                continue;
            }
            if (line.StartsWith("#", StringComparison.Ordinal)) continue; // 기타 주석 무시

            var p = line.Split(',');
            if (p.Length == 5
                && int.TryParse(p[0], out var idx) && int.TryParse(p[1], out var x)
                && int.TryParse(p[2], out var y) && int.TryParse(p[3], out var sw)
                && int.TryParse(p[4], out var sh))
            {
                slots.Add(new Slot { Index = idx, X = x, Y = y, Width = sw, Height = sh });
            }
        }
        return (size, slots, dbId);
    }
}
