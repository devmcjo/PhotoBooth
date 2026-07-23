namespace MCPhoto.Core.Upload;

/// <summary>
/// 업로드 진행 단계. QR 업로드(사진→타임랩스→문서 생성)의 단계 구분. (it11 #16 §3.16.2)
/// </summary>
public enum UploadStage
{
    /// <summary>최종 이미지 업로드 단계.</summary>
    Photo,
    /// <summary>타임랩스 영상 업로드 단계.</summary>
    Timelapse,
    /// <summary>ResultSession 문서 생성 등 마무리 단계.</summary>
    Finalizing
}

/// <summary>
/// 업로드 진행 상황(단계 + 해당 단계 내 진행 비율 + 표시 라벨).
/// System.Windows 무의존(Core에 위치) — UI 계층이 IProgress&lt;UploadProgress&gt;로 소비. (it11 #16 §3.16.2)
/// </summary>
/// <param name="Stage">현재 진행 단계.</param>
/// <param name="Fraction">해당 단계 내 진행 비율(0.0~1.0). 세밀 진행 불가 시 0 또는 1.</param>
/// <param name="Label">표시용 라벨(null이면 소비 측이 단계 기본 문구 사용).</param>
public sealed record UploadProgress(UploadStage Stage, double Fraction, string? Label = null);
