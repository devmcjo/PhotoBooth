namespace MCPhoto.Core.Models;

/// <summary>
/// 업로드된 결과 세션 메타데이터. 문서 ID = 추측 불가 UUIDv4 토큰. (firebase-contract §2.3)
/// </summary>
public sealed class ResultSession
{
    /// <summary>세션 ID = 문서 ID = URL 토큰(UUIDv4).</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>프레임 포함 최종 이미지 다운로드 토큰 URL.</summary>
    public string FinalImageUrl { get; set; } = string.Empty;

    /// <summary>타임랩스 영상 URL. 생성 실패/미포함 시 null.</summary>
    public string? TimelapseUrl { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>createdAt + retentionHours. 자동 삭제 기준.</summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>모바일 다운로드 페이지 URL(QR 인코딩 대상).</summary>
    public string DownloadPageUrl { get; set; } = string.Empty;
}
