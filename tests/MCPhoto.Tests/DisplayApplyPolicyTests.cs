using MCPhoto.Core.Settings;

namespace MCPhoto.Tests;

/// <summary>
/// it16 §7.3: 표시 모드 적용 판정(순수). 시작 복원(appliedMode=null)과 런타임 모드 변경을 한 규칙으로 통합한다.
/// 창(Window) 없이 검증 가능한 유일한 지점이라 결정 표 6조합을 전수 고정한다.
/// </summary>
public class DisplayApplyPolicyTests
{
    [Theory]
    // 시작(appliedMode=null): 설정된 모드를 그대로 적용 — 창모드면 ini 기하 복원(현행 동작 보존).
    [InlineData(DisplayMode.Fullscreen, null, DisplayApplyAction.Fullscreen)]
    [InlineData(DisplayMode.Windowed, null, DisplayApplyAction.WindowedRestoreGeometry)]
    // 동일 모드: 무동작(버그 수정 지점 — 저장해도 창이 움직이지 않는다).
    [InlineData(DisplayMode.Windowed, DisplayMode.Windowed, DisplayApplyAction.None)]
    [InlineData(DisplayMode.Fullscreen, DisplayMode.Fullscreen, DisplayApplyAction.None)]
    // 모드 전환: it9 후속(재시작 없이 즉시 전환)을 유지해야 한다.
    [InlineData(DisplayMode.Fullscreen, DisplayMode.Windowed, DisplayApplyAction.Fullscreen)]
    [InlineData(DisplayMode.Windowed, DisplayMode.Fullscreen, DisplayApplyAction.WindowedRestoreGeometry)]
    public void Decide_Covers_Decision_Table(DisplayMode target, DisplayMode? appliedMode, DisplayApplyAction expected)
        => Assert.Equal(expected, DisplayApplyPolicy.Decide(target, appliedMode));

    /// <summary>
    /// it16 §8.3-29 회귀 방지: **설정 저장 시 창 위치 점프 방지**.
    /// 창모드에서 창모드로 저장하는 것은 표시 모드 변경이 아니므로 창 기하·상태를 절대 재적용하지 않는다
    /// (종전에는 OnClosing에서만 갱신되는 과거 WindowBounds로 창이 되돌아갔다).
    /// </summary>
    [Fact]
    public void Decide_Same_Windowed_Mode_Is_None_So_Save_Does_Not_Jump_Window()
        => Assert.Equal(DisplayApplyAction.None,
            DisplayApplyPolicy.Decide(DisplayMode.Windowed, DisplayMode.Windowed));

    /// <summary>반복 저장(같은 모드 연속 적용)도 계속 무동작이어야 한다.</summary>
    [Fact]
    public void Decide_Is_Idempotent_For_Repeated_Saves()
    {
        DisplayMode? applied = null;
        var first = DisplayApplyPolicy.Decide(DisplayMode.Windowed, applied);
        Assert.Equal(DisplayApplyAction.WindowedRestoreGeometry, first);

        applied = DisplayMode.Windowed;   // 창이 실제로 창모드로 적용된 뒤
        Assert.Equal(DisplayApplyAction.None, DisplayApplyPolicy.Decide(DisplayMode.Windowed, applied));
        Assert.Equal(DisplayApplyAction.None, DisplayApplyPolicy.Decide(DisplayMode.Windowed, applied));
    }
}
