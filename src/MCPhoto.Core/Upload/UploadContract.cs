using MCPhoto.Core.Settings;

namespace MCPhoto.Core.Upload;

/// <summary>
/// Firebase 업로드 계약 조립(순수 로직, 테스트 대상). Storage 경로·토큰 URL·downloadPageUrl·expiresAt.
/// firebase-contract §3·§4 규약을 정확히 준수해야 웹이 읽을 수 있다.
/// </summary>
public static class UploadContract
{
    /// <summary>새 세션 토큰(UUIDv4). 문서 ID이자 접근 열쇠(추측 불가). firebase-contract §3.3.</summary>
    public static string NewSessionToken() => Guid.NewGuid().ToString();

    /// <summary>최종 이미지 Storage 경로: results/{sessionId}/final.{ext}. §4.2</summary>
    public static string FinalImagePath(string sessionId, OutputFormat format)
        => $"results/{sessionId}/final.{(format == OutputFormat.Png ? "png" : "jpg")}";

    /// <summary>타임랩스 Storage 경로: results/{sessionId}/timelapse.mp4. §4.2</summary>
    public static string TimelapsePath(string sessionId)
        => $"results/{sessionId}/timelapse.mp4";

    /// <summary>
    /// Firebase 다운로드 토큰 URL 조립. §4.3
    /// https://firebasestorage.googleapis.com/v0/b/{bucket}/o/{urlEncodedPath}?alt=media&amp;token={downloadToken}
    /// </summary>
    public static string TokenDownloadUrl(string bucket, string storagePath, string downloadToken)
    {
        var encoded = Uri.EscapeDataString(storagePath); // 슬래시 → %2F 포함
        return $"https://firebasestorage.googleapis.com/v0/b/{bucket}/o/{encoded}?alt=media&token={downloadToken}";
    }

    /// <summary>
    /// downloadPageUrl 조립(쿼리형 기본안). §3.5
    /// {hostingBaseUrl}/?s={token} — 트레일링 슬래시 제거 후 조립.
    /// </summary>
    public static string DownloadPageUrl(string hostingBaseUrl, string token)
    {
        var baseUrl = (hostingBaseUrl ?? string.Empty).TrimEnd('/');
        return $"{baseUrl}/?s={token}";
    }

    /// <summary>expiresAt = createdAt + retentionHours. §2.3</summary>
    public static DateTime ComputeExpiresAt(DateTime createdAt, int retentionHours)
        => createdAt.AddHours(retentionHours);
}
