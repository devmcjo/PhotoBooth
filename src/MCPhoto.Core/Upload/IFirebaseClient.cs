using MCPhoto.Core.Models;

namespace MCPhoto.Core.Upload;

/// <summary>
/// 백엔드 저장소 접근 추상화(Firestore + Storage 게이트웨이). it15부터 유일한 구현은 HTTP 경유
/// <c>HttpFirebaseClient</c>다(레거시 Admin SDK 직결 경로 폐기).
/// ⚠️ 이름이 실체(백엔드 게이트웨이)와 어긋나지만 이번 범위에서 리네임하지 않는다(설계 §4.2 — 백로그).
/// (architecture §6.4)
/// </summary>
public interface IFirebaseClient
{
    /// <summary>구성 완료 여부(백엔드 base URL 설정됨). 서버 도달 성공을 뜻하지는 않는다.</summary>
    bool IsInitialized { get; }

    /// <summary>Storage 버킷 이름(토큰 URL 조립용).</summary>
    string Bucket { get; }

    /// <summary>
    /// 파일 업로드. 다운로드 토큰(UUID)을 부여하고 그 토큰을 반환. (§4.3)
    /// </summary>
    /// <param name="fileProgress">
    /// 파일 단위 진행 보고(선택, 하위호환). 0.0~1.0. null이면 진행 보고 없이 기존 동작. (it11 #16 §3.16.3)
    /// </param>
    Task<string> UploadFileAsync(string storagePath, string localFilePath, string contentType, IProgress<double>? fileProgress = null, CancellationToken ct = default);

    /// <summary>Storage 경로(폴더) 삭제.</summary>
    Task DeleteStoragePrefixAsync(string prefix, CancellationToken ct = default);

    /// <summary>ResultSession 문서 생성(문서 ID = session.Id). (§2.3)</summary>
    Task CreateResultSessionAsync(ResultSession session, CancellationToken ct = default);

    /// <summary>만료(expiresAt &lt; now) ResultSession 문서 조회.</summary>
    Task<IReadOnlyList<ResultSession>> QueryExpiredSessionsAsync(DateTime now, CancellationToken ct = default);

    /// <summary>ResultSession 문서 삭제.</summary>
    Task DeleteResultSessionAsync(string sessionId, CancellationToken ct = default);
}
