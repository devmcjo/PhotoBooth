using MCPhoto.Core.Models;

namespace MCPhoto.Core.Capture;

/// <summary>
/// 슬롯 한 칸의 배치 결과. <see cref="CutIndex"/>가 null이면 아직 채워지지 않은 슬롯이다.
/// </summary>
/// <param name="Slot">슬롯(프레임 픽셀 좌표계).</param>
/// <param name="SlotNumber">사람에게 보이는 순번(1부터). 슬롯 Index 오름차순.</param>
/// <param name="CutIndex">이 슬롯에 들어갈 컷의 인덱스(세션 컷 버퍼 기준). 미선택이면 null.</param>
public sealed record SlotFill(Slot Slot, int SlotNumber, int? CutIndex)
{
    /// <summary>이 슬롯에 들어갈 컷이 정해졌는가.</summary>
    public bool IsFilled => CutIndex.HasValue;
}

/// <summary>
/// "선택한 컷이 어느 슬롯에 들어가는가"를 산출하는 순수 로직(UI 무의존).
/// ⚠️ 규칙은 합성(<c>CompositionService.Compose</c>)과 반드시 같아야 한다 —
/// 슬롯을 <see cref="Slot.Index"/> 오름차순으로 정렬한 뒤 <b>선택 순서대로 1:1 대응</b>시킨다.
/// 선택을 해제하면 뒤 선택이 앞 슬롯으로 당겨지는 것도 합성과 같은 결과다(선택 목록이 곧 슬롯 순서).
/// 컷 선택 화면의 배치 프리뷰가 이 계획을 그린다.
/// </summary>
public static class SlotFillPlan
{
    /// <summary>
    /// 슬롯 배치 계획 산출. 결과 길이 = 슬롯 수(항상). 선택이 슬롯보다 적으면 남은 슬롯은 빈 칸으로 남는다.
    /// 선택이 슬롯보다 많은 이상 입력은 앞에서 슬롯 수만큼만 쓴다(<c>CaptureSession.ToggleSelection</c>이 이미 상한을 강제).
    /// </summary>
    public static IReadOnlyList<SlotFill> Build(IReadOnlyList<Slot> slots, IReadOnlyList<int> selection)
    {
        var ordered = slots.OrderBy(s => s.Index).ToList();
        var result = new List<SlotFill>(ordered.Count);
        for (int i = 0; i < ordered.Count; i++)
            result.Add(new SlotFill(ordered[i], i + 1, i < selection.Count ? selection[i] : null));
        return result;
    }
}
