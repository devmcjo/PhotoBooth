using System.Linq;
using MCPhoto.Core.Capture;
using MCPhoto.Core.Models;
using Xunit;

namespace MCPhoto.Tests;

/// <summary>
/// 컷 선택 → 슬롯 배치 계획(순수 로직). 컷 선택 화면의 배치 프리뷰와 합성이 같은 규칙을 쓰는지 고정한다
/// — 규칙이 갈리면 "미리보기와 결과물이 다르다"는 최악의 버그가 된다.
/// </summary>
public class SlotFillPlanTests
{
    private static List<Slot> Slots(int count, bool reverseOrder = false)
    {
        var list = new List<Slot>();
        for (int i = 0; i < count; i++)
            list.Add(new Slot { Index = i, X = i * 100, Y = 0, Width = 90, Height = 120 });
        if (reverseOrder) list.Reverse();   // 저장 순서가 Index 순이 아닌 경우
        return list;
    }

    [Fact]
    public void No_Selection_Leaves_Every_Slot_Empty()
    {
        var plan = SlotFillPlan.Build(Slots(3), Array.Empty<int>());

        Assert.Equal(3, plan.Count);
        Assert.All(plan, f => Assert.False(f.IsFilled));
        Assert.Equal(new[] { 1, 2, 3 }, plan.Select(f => f.SlotNumber).ToArray()); // 순번은 1부터
    }

    [Fact]
    public void Selection_Fills_Slots_In_Selection_Order()
    {
        // 컷 4→1 순으로 골랐다면 첫 슬롯에 컷4, 둘째 슬롯에 컷1이 들어간다(합성과 동일).
        var plan = SlotFillPlan.Build(Slots(3), new[] { 4, 1 });

        Assert.Equal(4, plan[0].CutIndex);
        Assert.Equal(1, plan[1].CutIndex);
        Assert.Null(plan[2].CutIndex);      // 남은 슬롯은 빈 칸
        Assert.True(plan[0].IsFilled);
        Assert.False(plan[2].IsFilled);
    }

    [Fact]
    public void Slots_Are_Ordered_By_Index_Not_By_List_Order()
    {
        // 합성(CompositionService)이 Slot.Index로 정렬하므로 프리뷰도 같아야 한다.
        var plan = SlotFillPlan.Build(Slots(3, reverseOrder: true), new[] { 7 });

        Assert.Equal(new[] { 0, 1, 2 }, plan.Select(f => f.Slot.Index).ToArray());
        Assert.Equal(7, plan[0].CutIndex);   // 첫 선택은 Index 0 슬롯에
    }

    [Fact]
    public void Deselecting_Pulls_Later_Cuts_Forward()
    {
        // 선택 목록이 곧 슬롯 순서다 — 가운데를 해제하면 뒤 컷이 앞 슬롯으로 당겨진다.
        var before = SlotFillPlan.Build(Slots(3), new[] { 0, 1, 2 });
        Assert.Equal(new int?[] { 0, 1, 2 }, before.Select(f => f.CutIndex).ToArray());

        var after = SlotFillPlan.Build(Slots(3), new[] { 0, 2 });   // 컷1 해제 후의 선택 목록
        Assert.Equal(new int?[] { 0, 2, null }, after.Select(f => f.CutIndex).ToArray());
    }

    [Fact]
    public void Extra_Selection_Beyond_Slot_Count_Is_Ignored()
    {
        // CaptureSession.ToggleSelection이 이미 상한을 강제하지만, 이상 입력에도 슬롯 수를 넘기지 않는다.
        var plan = SlotFillPlan.Build(Slots(2), new[] { 5, 6, 7 });

        Assert.Equal(2, plan.Count);
        Assert.Equal(new int?[] { 5, 6 }, plan.Select(f => f.CutIndex).ToArray());
    }

    [Fact]
    public void Empty_Slots_Yields_Empty_Plan()
    {
        Assert.Empty(SlotFillPlan.Build(Array.Empty<Slot>(), new[] { 0 }));
    }

    /// <summary>
    /// 자동 컷 수(it17)로 컷이 슬롯보다 많은 정상 상황: 슬롯 5 + 7컷 촬영 → 계획은 항상 슬롯 수(5)다.
    /// </summary>
    [Fact]
    public void Plan_Length_Always_Equals_Slot_Count()
    {
        var plan = SlotFillPlan.Build(Slots(5), new[] { 6, 5, 4, 3, 2 });

        Assert.Equal(5, plan.Count);
        Assert.All(plan, f => Assert.True(f.IsFilled));
    }
}
