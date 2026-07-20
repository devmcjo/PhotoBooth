namespace MCPhoto.Core.Upload;

using MCPhoto.Core.Models;

/// <summary>
/// Firebase 업로드 + ResultSession 생성 + 다운로드 토큰 URL 산출. (architecture §6, firebase-contract §4)
/// 추상화로 배포 시 규칙 준수 경로 교체 가능.
/// </summary>
public interface IUploadService
{
    /// <summary>
    /// 최종 이미지·타임랩스를 results/{sessionId}/에 업로드하고 ResultSession 문서 생성.
    /// downloadPageUrl 조립(§3.5) 후 반환. 업로드 실패 시 예외(QR 노출 전 확인).
    /// </summary>
    Task<ResultSession> UploadResultAsync(
        string finalImagePath,
        string? timelapsePath,
        int retentionHours,
        string hostingBaseUrl,
        CancellationToken ct = default);

    /// <summary>만료(expiresAt &lt; now) 세션 정리: Storage results/ + Firestore 문서 삭제.</summary>
    Task<int> PurgeExpiredAsync(CancellationToken ct = default);
}
