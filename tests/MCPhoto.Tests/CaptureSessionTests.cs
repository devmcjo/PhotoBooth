using MCPhoto.Core.Capture;
using MCPhoto.Core.Models;

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
}
