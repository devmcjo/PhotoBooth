namespace MCPhoto.Core.Models;

/// <summary>
/// 업로드된 결과 세션 메타데이터. 문서 ID = 추측 불가 UUIDv4 토큰. (firebase-contract §2.3)
/// </summary>
public sealed class ResultSession
{
    /// <summary>세션 ID = 문서 ID = URL 토큰(UUIDv4).</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 프레임 포함 최종 이미지 다운로드 토큰 URL. 사진 전송 옵션(SendPhoto) off면 null. (it7 F2 계약)
    /// 미만료 문서에서 null = "사진 전송 옵션 꺼짐"(의도적 제외, 실패·만료 아님).
    /// </summary>
    public string? FinalImageUrl { get; set; }

    /// <summary>타임랩스 영상 URL. 옵션 off·생성 실패·미포함 시 null. (it7 F2)</summary>
    public string? TimelapseUrl { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>createdAt + retentionHours. 자동 삭제 기준.</summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>모바일 다운로드 페이지 URL(QR 인코딩 대상).</summary>
    public string DownloadPageUrl { get; set; } = string.Empty;
}
