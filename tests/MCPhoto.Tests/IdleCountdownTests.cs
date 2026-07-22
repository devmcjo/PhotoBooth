using MCPhoto.Core.Navigation;

namespace MCPhoto.Tests;

/// <summary>it8 Step 1 (A1): 유휴 경고 카운트다운 순수 로직 — 감소·완료·리셋.</summary>
public class IdleCountdownTests
{
    [Fact]
    public void Starts_At_Given_Seconds()
    {
        var c = new IdleCountdown(10);
        Assert.Equal(10, c.Remaining);
        Assert.False(c.IsExpired);
    }

    [Fact]
    public void Tick_Decrements_By_One()
    {
        var c = new IdleCountdown(10);
        Assert.False(c.Tick()); // 10→9, 아직 만료 아님
        Assert.Equal(9, c.Remaining);
    }

    [Fact]
    public void Reaches_Zero_And_Signals_Expiry_Once()
    {
        var c = new IdleCountdown(3);
        Assert.False(c.Tick()); // 3→2
        Assert.False(c.Tick()); // 2→1
        Assert.True(c.Tick());  // 1→0 (만료 전이, true 1회)
        Assert.True(c.IsExpired);
        Assert.Equal(0, c.Remaining);
        Assert.False(c.Tick()); // 이미 0 — 중복 완료 없음
    }

    [Fact]
    public void Reset_Restores_Start_Value()
    {
        var c = new IdleCountdown(10);
        c.Tick(); c.Tick(); c.Tick(); // 10→7
        Assert.Equal(7, c.Remaining);

        c.Reset();
        Assert.Equal(10, c.Remaining);
        Assert.False(c.IsExpired);
    }

    [Fact]
    public void Start_Value_Has_Lower_Bound_One()
    {
        var c = new IdleCountdown(0);
        Assert.Equal(1, c.Remaining); // 최소 1(즉시 만료 방지)
    }
}
