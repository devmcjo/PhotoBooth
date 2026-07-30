using MCPhoto.Core.Capture;
using MCPhoto.Core.Models;
using MCPhoto.Core.Settings;

namespace MCPhoto.Tests;

/// <summary>WBS Step 7: 촬영 세션 컷 버퍼·선택 규칙(정확히 슬롯 수)·재촬영 검증.</summary>
public class CaptureSessionTests
{
    private static FrameTemplate MakeFrame(int slots)
    {
        var f = new FrameTemplate { Id = "f1", Name = "test" };
        for (int i = 0; i < slots; i++)
            f.Slots.Add(new Slot { Index = i, X = i * 10, Y = 0, Width = 100, Height = 133 });
        return f;
    }

    private static CapturedStill MakeStill() => new() { Width = 100, Height = 133, Pixels = new byte[100 * 133 * 3] };

    [Fact]
    public void Begin_Sets_Frame_And_CutCount()
    {
        var s = new CaptureSession();
        s.Begin(MakeFrame(4), 6);
        Assert.Equal(4, s.SlotCount);
        Assert.Equal(6, s.CutCount);
    }

    [Fact]
    public void CutCount_Never_Below_SlotCount()
    {
        var s = new CaptureSession();
        s.Begin(MakeFrame(6), 4); // 컷수<슬롯: VF-5 위반 방어
        Assert.Equal(6, s.CutCount);
    }

    [Fact]
    public void AddCut_Caps_At_CutCount()
    {
        var s = new CaptureSession();
        s.Begin(MakeFrame(4), 6);
        for (int i = 0; i < 10; i++) s.AddCut(MakeStill());
        Assert.Equal(6, s.Cuts.Count);
        Assert.True(s.IsCaptureComplete);
    }

    [Fact]
    public void Selection_Exactly_SlotCount()
    {
        var s = new CaptureSession();
        s.Begin(MakeFrame(4), 6);
        for (int i = 0; i < 6; i++) s.AddCut(MakeStill());

        // 4개 선택 성공
        Assert.True(s.ToggleSelection(0));
        Assert.True(s.ToggleSelection(1));
        Assert.True(s.ToggleSelection(2));
        Assert.True(s.ToggleSelection(3));
        Assert.True(s.IsSelectionComplete);

        // 5번째 선택 거부(슬롯 수 초과 불가)
        Assert.False(s.ToggleSelection(4));
        Assert.Equal(4, s.Selection.Count);
    }

    [Fact]
    public void Toggle_Deselects()
    {
        var s = new CaptureSession();
        s.Begin(MakeFrame(2), 6);
        for (int i = 0; i < 6; i++) s.AddCut(MakeStill());

        s.ToggleSelection(0);
        s.ToggleSelection(1);
        Assert.True(s.IsSelectionComplete);

        s.ToggleSelection(0); // 해제
        Assert.False(s.IsSelectionComplete);
        Assert.Single(s.Selection);
        Assert.Equal(1, s.Selection[0]);
    }

    [Fact]
    public void Selection_Order_Preserved()
    {
        var s = new CaptureSession();
        s.Begin(MakeFrame(3), 6);
        for (int i = 0; i < 6; i++) s.AddCut(MakeStill());

        s.ToggleSelection(5);
        s.ToggleSelection(2);
        s.ToggleSelection(0);

        Assert.Equal(new[] { 5, 2, 0 }, s.Selection.ToArray());
    }

    [Fact]
    public void Retake_Clears_Cuts_Keeps_Frame()
    {
        var s = new CaptureSession();
        var frame = MakeFrame(4);
        s.Begin(frame, 6);
        for (int i = 0; i < 6; i++) s.AddCut(MakeStill());
        s.ToggleSelection(0);

        s.ResetForRetake();
        Assert.Empty(s.Cuts);
        Assert.Empty(s.Selection);
        Assert.Same(frame, s.Frame); // 프레임 유지(촬영 전 선택 고정)
    }

    [Fact]
    public void Discard_Clears_Everything()
    {
        var s = new CaptureSession();
        s.Begin(MakeFrame(4), 6);
        s.AddCut(MakeStill());
        s.Discard();
        Assert.Null(s.Frame);
        Assert.Empty(s.Cuts);
        Assert.Equal(0, s.CutCount);
    }

    // ── it11 #13: 전체 재촬영 카운터(컷별 재촬영은 후속) ──

    [Fact]
    public void BeginFullRetake_Increments_And_Clears()
    {
        var s = new CaptureSession();
        var frame = MakeFrame(4);
        s.Begin(frame, 6);
        for (int i = 0; i < 6; i++) s.AddCut(MakeStill());
        s.ToggleSelection(0);

        Assert.Equal(0, s.FullRetakeCount);
        Assert.False(s.HasFullRetaken);

        s.BeginFullRetake();

        Assert.Equal(1, s.FullRetakeCount);
        Assert.True(s.HasFullRetaken);
        Assert.Empty(s.Cuts);          // 컷 폐기
        Assert.Empty(s.Selection);     // 선택 폐기
        Assert.Same(frame, s.Frame);   // 프레임 유지(촬영 전 선택 고정)
    }

    [Fact]
    public void CanFullRetake_Respects_Limit_Boundary()
    {
        var s = new CaptureSession();
        s.Begin(MakeFrame(4), 6);

        // limit=1: 0회 때 가능, 1회 후 불가.
        Assert.True(s.CanFullRetake(1));
        s.BeginFullRetake();
        Assert.False(s.CanFullRetake(1));

        // limit=3: 1회 소진 상태여도 3회까지 여유.
        Assert.True(s.CanFullRetake(3));
        s.BeginFullRetake();
        s.BeginFullRetake();
        Assert.Equal(3, s.FullRetakeCount);
        Assert.False(s.CanFullRetake(3)); // 3회 도달 → 초과 차단
    }

    [Fact]
    public void CanFullRetake_Zero_Limit_Always_False()
    {
        // 경계: limit 0이면 0회 때도 불가(방어).
        var s = new CaptureSession();
        s.Begin(MakeFrame(4), 6);
        Assert.False(s.CanFullRetake(0));
    }

    [Fact]
    public void Discard_Resets_Retake_Counter()
    {
        var s = new CaptureSession();
        s.Begin(MakeFrame(4), 6);
        s.BeginFullRetake();
        Assert.Equal(1, s.FullRetakeCount);

        s.Discard();
        Assert.Equal(0, s.FullRetakeCount);
        Assert.False(s.HasFullRetaken);
    }

    [Fact]
    public void ResetForRetake_Does_Not_Touch_Counter()
    {
        // 레거시 경로는 카운터를 건드리지 않음(회귀 방지). 컷·선택만 폐기.
        var s = new CaptureSession();
        s.Begin(MakeFrame(4), 6);
        for (int i = 0; i < 6; i++) s.AddCut(MakeStill());

        s.ResetForRetake();
        Assert.Empty(s.Cuts);
        Assert.Equal(0, s.FullRetakeCount); // 카운터 불변
    }

    // ── it17: 자동 컷 수 해석(Begin이 유일한 해석 지점 — 설계 §0.4) ──

    [Fact]
    public void Begin_Auto_Resolves_Slots_Plus_Two()
    {
        var s = new CaptureSession();
        s.Begin(MakeFrame(5), CutCountPolicy.AutoCutCount);
        Assert.Equal(7, s.CutCount);        // 허용 집합({6,8,10})에 없는 7이 실효값으로 정상 동작
        Assert.True(s.IsAutoCutCount);
    }

    [Fact]
    public void Begin_Auto_Six_Slots_Gives_Eight()
    {
        // 피드백 시나리오: 슬롯 6개면 8장을 찍어 6칸을 고른다 → 버릴 2장이 생긴다(설계 §0.2).
        var s = new CaptureSession();
        s.Begin(MakeFrame(6), CutCountPolicy.AutoCutCount);
        Assert.Equal(8, s.CutCount);
        Assert.Equal(6, s.SlotCount);
    }

    [Fact]
    public void Begin_Auto_Respects_Minimum()
    {
        // 슬롯 3개면 3+2=5지만 최소 6이 우선.
        var s = new CaptureSession();
        s.Begin(MakeFrame(3), CutCountPolicy.AutoCutCount);
        Assert.Equal(6, s.CutCount);
    }

    [Fact]
    public void Begin_Fixed_Sets_IsAuto_False()
    {
        // 고정 설정은 종전 동작 그대로 + 배지 미노출.
        var s = new CaptureSession();
        s.Begin(MakeFrame(6), 6);
        Assert.Equal(6, s.CutCount);
        Assert.False(s.IsAutoCutCount);
    }

    [Fact]
    public void FullRetake_Preserves_Resolved_CutCount()
    {
        // 재촬영은 컷·선택만 폐기 — 해석값 재계산 없음(VF-10).
        var s = new CaptureSession();
        s.Begin(MakeFrame(5), CutCountPolicy.AutoCutCount);
        for (int i = 0; i < 7; i++) s.AddCut(MakeStill());

        s.BeginFullRetake();

        Assert.Equal(7, s.CutCount);
        Assert.True(s.IsAutoCutCount);
        Assert.Empty(s.Cuts);
    }

    [Fact]
    public void Discard_Resets_IsAutoCutCount()
    {
        var s = new CaptureSession();
        s.Begin(MakeFrame(5), CutCountPolicy.AutoCutCount);
        Assert.True(s.IsAutoCutCount);

        s.Discard();

        Assert.Equal(0, s.CutCount);        // "세션 없음"(자동 sentinel과 무관)
        Assert.False(s.IsAutoCutCount);
    }
}
