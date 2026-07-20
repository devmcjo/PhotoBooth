namespace MCPhoto.Core.Capture;

using MCPhoto.Core.Models;

/// <summary>필터 종류. 전체 컷 일괄 적용. (PRD §F4)</summary>
public enum FilterKind
{
    None,
    Grayscale,
    Brightness,
    Beauty
}

/// <summary>
/// 배경형 합성 서비스. 프레임 이미지 = 배경, 필터 적용 컷 = 슬롯 위. (architecture §3.4)
/// 캡처가 이미 슬롯 종횡비라 uniform 스케일만(왜곡 없음).
/// </summary>
public interface ICompositionService
{
    /// <summary>
    /// 선택 컷들을 프레임 슬롯에 배치해 최종 이미지 파일 생성.
    /// cuts[i] → frame.Slots[i](선택 순서 = 슬롯 순서). 정확히 슬롯 수만큼.
    /// </summary>
    /// <returns>저장된 최종 이미지 경로.</returns>
    Task<string> ComposeAsync(
        FrameTemplate frame,
        IReadOnlyList<CapturedStill> cuts,
        FilterKind filter,
        string outputPath,
        CancellationToken ct = default);
}
