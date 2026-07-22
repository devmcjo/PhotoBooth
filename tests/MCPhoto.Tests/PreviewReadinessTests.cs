using MCPhoto.Core.Capture;

namespace MCPhoto.Tests;

/// <summary>it8 Step 7 (A7): 안정적 프리뷰 판정 — N프레임 + 최소경과 + fps>0 모두 충족 시 Ready.</summary>
public class PreviewReadinessTests
{
    [Fact]
    public void Not_Ready_Before_Enough_Frames()
    {
        var r = new PreviewReadiness(requiredFrames: 8, minElapsedMs: 500);
        // 경과·fps는 충분하지만 프레임 수 부족.
        for (int i = 0; i < 7; i++) Assert.False(r.OnFrame(600, 15));
        Assert.False(r.IsReady);
    }

    [Fact]
    public void Not_Ready_Before_Min_Elapsed()
    {
        var r = new PreviewReadiness(requiredFrames: 3, minElapsedMs: 500);
        // 프레임 수·fps 충분하지만 경과 미달(400ms).
        Assert.False(r.OnFrame(400, 15));
        Assert.False(r.OnFrame(420, 15));
        Assert.False(r.OnFrame(450, 15));
        Assert.False(r.IsReady);
    }

    [Fact]
    public void Not_Ready_When_Fps_Zero()
    {
        var r = new PreviewReadiness(requiredFrames: 2, minElapsedMs: 100);
        // 프레임·경과 충족이나 fps=0(스트림 미흐름).
        Assert.False(r.OnFrame(200, 0));
        Assert.False(r.OnFrame(300, 0));
        Assert.False(r.IsReady);
    }

    [Fact]
    public void Ready_When_All_Conditions_Met()
    {
        var r = new PreviewReadiness(requiredFrames: 3, minElapsedMs: 500);
        Assert.False(r.OnFrame(200, 15)); // 1: 경과 미달
        Assert.False(r.OnFrame(400, 15)); // 2: 경과 미달
        Assert.True(r.OnFrame(600, 15));  // 3: 프레임·경과·fps 모두 충족 → Ready 전이
        Assert.True(r.IsReady);
    }

    [Fact]
    public void Ready_Transition_Signals_Once()
    {
        var r = new PreviewReadiness(requiredFrames: 1, minElapsedMs: 0);
        Assert.True(r.OnFrame(10, 15));   // 즉시 충족(전이 true)
        Assert.False(r.OnFrame(20, 15));  // 이미 Ready — 중복 false
    }
}
