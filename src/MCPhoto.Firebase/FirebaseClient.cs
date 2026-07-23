using System.IO;
using Google.Apis.Auth.OAuth2;
using Google.Cloud.Firestore;
using Google.Cloud.Storage.V1;
using MCPhoto.Core.Models;
using MCPhoto.Core.Upload;
using MCPhoto.Firebase.Dto;
using Microsoft.Extensions.Logging;
using GcsObject = Google.Apis.Storage.v1.Data.Object;

namespace MCPhoto.Firebase;

/// <summary>
/// Firebase 접근(Admin SDK/서비스 계정) — MVP 1차. 규칙 우회 쓰기. (architecture §6.4)
/// ⚠️ 서비스 계정 키는 실행경로 우선, 없으면 %ProgramData%\MCPhoto\ 폴백에서 로드(it6 #2). git·인스톨러 포함 금지.
/// 키가 없으면 IsInitialized=false로 안전 동작(오프라인/QR off 완화 경로).
/// </summary>
public sealed class FirebaseClient : IFirebaseClient
{
    private const string DownloadTokenMetaKey = "firebaseStorageDownloadTokens";

    private readonly ILogger<FirebaseClient>? _logger;
    private FirestoreDb? _firestore;
    private StorageClient? _storage;

    public bool IsInitialized { get; private set; }
    public string Bucket { get; private set; } = string.Empty;

    /// <summary>Firestore 접근(FrameRepository·AccountService 공유). 미초기화 시 null.</summary>
    internal FirestoreDb? Firestore => _firestore;

    /// <summary>Storage 접근(FrameRepository 공유). 미초기화 시 null.</summary>
    internal StorageClient? Storage => _storage;

    /// <param name="serviceAccountKeyPath">서비스 계정 JSON 경로. null이면 기본 보호 위치.</param>
    /// <param name="projectId">Firebase 프로젝트 ID. null이면 키에서 추론.</param>
    /// <param name="bucket">Storage 버킷(예: {project}.firebasestorage.app). null이면 {projectId}.appspot.com.</param>
    public FirebaseClient(
        ILogger<FirebaseClient>? logger = null,
        string? serviceAccountKeyPath = null,
        string? projectId = null,
        string? bucket = null)
    {
        _logger = logger;
        try
        {
            var keyPath = serviceAccountKeyPath ?? DefaultKeyPath();
            if (!File.Exists(keyPath))
            {
                // it10 S4-1: 후보 전부를 존재 여부와 함께 로그 — QA가 "키를 어디에 두어야 하는지" 파악 가능.
                // 명시 경로가 주어졌으면 그 경로만, 아니면 기본 후보 2경로를 나열.
                var candidates = serviceAccountKeyPath is not null
                    ? new[] { serviceAccountKeyPath }
                    : KeyCandidatePaths();
                var detail = string.Join(", ", candidates.Select(p => $"[{p}]={(File.Exists(p) ? "있음" : "없음")}"));
                _logger?.LogWarning(
                    "서비스 계정 키 없음 — Firebase 미초기화(QR off/오프라인 완화 경로). 탐색: {Candidates}. " +
                    "서버 기능 비활성(오프라인 모드).", detail);
                return;
            }

            var credential = GoogleCredential.FromFile(keyPath);
            var resolvedProject = projectId ?? ProjectIdFromKey(keyPath);

            _firestore = new FirestoreDbBuilder
            {
                ProjectId = resolvedProject,
                Credential = credential
            }.Build();

            _storage = StorageClient.Create(credential);
            if (!string.IsNullOrWhiteSpace(bucket))
            {
                Bucket = bucket!;
            }
            else
            {
                // 미지정 시 레거시 규약으로 유도. 신규 프로젝트는 {project}.firebasestorage.app이라
                // 불일치 소지가 있으므로 설정(AppSettings.StorageBucket)으로 명시 권장.
                Bucket = $"{resolvedProject}.appspot.com";
                _logger?.LogWarning(
                    "Storage 버킷 미지정 — 레거시 규약 '{Bucket}'으로 유도함. 신규 프로젝트(*.firebasestorage.app)면 " +
                    "업로드 실패 가능. AppSettings.StorageBucket에 실제 버킷을 지정하세요.", Bucket);
            }
            IsInitialized = true;
            // it10 S4-1: 진단 — 실제 사용한 키 경로를 로그에 포함(QA가 어떤 키로 붙었는지 확인).
            _logger?.LogInformation(
                "Firebase 초기화 완료: project={Project}, bucket={Bucket}, key={KeyPath}",
                resolvedProject, Bucket, keyPath);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Firebase 초기화 실패 — 미초기화로 진행");
            IsInitialized = false;
        }
    }

    /// <summary>
    /// 서비스 계정 키 탐색 후보(우선순위 순): ①실행경로\serviceAccountKey.json ②%ProgramData%\MCPhoto\serviceAccountKey.json. (it6 #2, it10 S4-1)
    /// 진단 로그·기본 경로 결정 모두 이 목록을 단일 소스로 사용한다.
    /// ⚠️ 키는 비밀: .gitignore가 serviceAccountKey.json 커버, 인스톨러 미포함. 실행경로 배치는 포터블 편의.
    /// </summary>
    public static string[] KeyCandidatePaths()
    {
        const string fileName = "serviceAccountKey.json";

        var exePath = Path.Combine(AppContext.BaseDirectory, fileName);
        var programDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "MCPhoto", fileName);
        return new[] { exePath, programDataPath };
    }

    /// <summary>
    /// 기본 키 경로: <see cref="KeyCandidatePaths"/> 중 첫 존재 파일. 둘 다 없으면 마지막 후보(ProgramData) 반환
    /// (존재하지 않음 → 호출측 오프라인 완화). 동작 불변(실행경로 우선).
    /// </summary>
    public static string DefaultKeyPath()
    {
        var candidates = KeyCandidatePaths();
        foreach (var path in candidates)
            if (File.Exists(path)) return path;
        return candidates[^1];
    }

    private static string ProjectIdFromKey(string keyPath)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(keyPath));
            if (doc.RootElement.TryGetProperty("project_id", out var pid))
                return pid.GetString() ?? string.Empty;
        }
        catch { /* 무시 */ }
        return string.Empty;
    }

    public async Task<string> UploadFileAsync(string storagePath, string localFilePath, string contentType, CancellationToken ct = default)
    {
        EnsureInit();
        var downloadToken = Guid.NewGuid().ToString();

        var obj = new GcsObject
        {
            Bucket = Bucket,
            Name = storagePath,
            ContentType = contentType,
            // Firebase 다운로드 토큰 URL이 동작하려면 이 메타데이터 필수(§4.3)
            Metadata = new Dictionary<string, string> { [DownloadTokenMetaKey] = downloadToken }
        };

        await using var stream = File.OpenRead(localFilePath);
        await _storage!.UploadObjectAsync(obj, stream, cancellationToken: ct);
        _logger?.LogInformation("업로드: {Path}", storagePath);
        return downloadToken;
    }

    public async Task DeleteStoragePrefixAsync(string prefix, CancellationToken ct = default)
    {
        EnsureInit();
        var toDelete = new List<string>();
        await foreach (var obj in _storage!.ListObjectsAsync(Bucket, prefix).WithCancellation(ct))
            toDelete.Add(obj.Name);

        foreach (var name in toDelete)
        {
            try { await _storage.DeleteObjectAsync(Bucket, name, cancellationToken: ct); }
            catch (Exception ex) { _logger?.LogWarning(ex, "Storage 삭제 실패: {Name}", name); }
        }
    }

    public async Task CreateResultSessionAsync(ResultSession session, CancellationToken ct = default)
    {
        EnsureInit();
        var doc = new ResultSessionDoc
        {
            Id = session.Id,
            FinalImageUrl = session.FinalImageUrl,
            TimelapseUrl = session.TimelapseUrl,
            CreatedAt = Timestamp.FromDateTime(session.CreatedAt.ToUniversalTime()),
            ExpiresAt = Timestamp.FromDateTime(session.ExpiresAt.ToUniversalTime()),
            DownloadPageUrl = session.DownloadPageUrl
        };
        await _firestore!.Collection("resultSessions").Document(session.Id).SetAsync(doc, cancellationToken: ct);
    }

    public async Task<IReadOnlyList<ResultSession>> QueryExpiredSessionsAsync(DateTime now, CancellationToken ct = default)
    {
        EnsureInit();
        var snapshot = await _firestore!.Collection("resultSessions")
            .WhereLessThan("expiresAt", Timestamp.FromDateTime(now.ToUniversalTime()))
            .GetSnapshotAsync(ct);

        var result = new List<ResultSession>();
        foreach (var d in snapshot.Documents)
        {
            var dto = d.ConvertTo<ResultSessionDoc>();
            result.Add(new ResultSession
            {
                Id = dto.Id,
                FinalImageUrl = dto.FinalImageUrl,
                TimelapseUrl = dto.TimelapseUrl,
                CreatedAt = dto.CreatedAt.ToDateTime(),
                ExpiresAt = dto.ExpiresAt.ToDateTime(),
                DownloadPageUrl = dto.DownloadPageUrl
            });
        }
        return result;
    }

    public async Task DeleteResultSessionAsync(string sessionId, CancellationToken ct = default)
    {
        EnsureInit();
        await _firestore!.Collection("resultSessions").Document(sessionId).DeleteAsync(cancellationToken: ct);
    }

    private void EnsureInit()
    {
        if (!IsInitialized)
            throw new InvalidOperationException("Firebase가 초기화되지 않았습니다(서비스 계정 키 필요).");
    }
}
