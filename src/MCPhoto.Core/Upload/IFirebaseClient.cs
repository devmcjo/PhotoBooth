using MCPhoto.Core.Models;

namespace MCPhoto.Core.Upload;

/// <summary>
/// Firebase 접근 추상화(Firestore + Storage). MVP는 Admin SDK 구현, 배포 시 규칙 준수 경로로 교체.
/// (architecture §6.4 — 전환 비용 최소화)
/// </summary>
public interface IFirebaseClient
{
    /// <summary>초기화 성공 여부(서비스 계정 키 로드됨).</summary>
    bool IsInitialized { get; }

    /// <summary>Storage 버킷 이름(토큰 URL 조립용).</summary>
    string Bucket { get; }

    /// <summary>파일 업로드. 다운로드 토큰(UUID)을 부여하고 그 토큰을 반환. (§4.3)</summary>
    Task<string> UploadFileAsync(string storagePath, string localFilePath, string contentType, CancellationToken ct = default);

    /// <summary>Storage 경로(폴더) 삭제.</summary>
    Task DeleteStoragePrefixAsync(string prefix, CancellationToken ct = default);

    /// <summary>ResultSession 문서 생성(문서 ID = session.Id). (§2.3)</summary>
    Task CreateResultSessionAsync(ResultSession session, CancellationToken ct = default);

    /// <summary>만료(expiresAt &lt; now) ResultSession 문서 조회.</summary>
    Task<IReadOnlyList<ResultSession>> QueryExpiredSessionsAsync(DateTime now, CancellationToken ct = default);

    /// <summary>ResultSession 문서 삭제.</summary>
    Task DeleteResultSessionAsync(string sessionId, CancellationToken ct = default);
}
