namespace MCPhoto.Core.Settings;

/// <summary>표시 모드 적용 시 창에 무엇을 할지. (it16 §7)</summary>
public enum DisplayApplyAction
{
    /// <summary>무동작 — 이미 같은 모드다. 창 스타일·상태·기하 전부 건드리지 않는다(위치 점프 방지).</summary>
    None,

    /// <summary>전체화면 적용(WindowStyle.None + NoResize + Maximized). 기하 미적용.</summary>
    Fullscreen,

    /// <summary>창모드 적용 + WindowBounds로 기하 복원(시작 복원, 전체화면→창모드 복귀).</summary>
    WindowedRestoreGeometry
}

/// <summary>
/// 표시 모드 적용 판정(순수). ① 시작 복원과 ② 런타임 모드 변경을 하나의 규칙으로 통합한다.
/// appliedMode=null이 "아직 한 번도 적용하지 않음"(=시작)이라는 유일한 신호다.
/// 목적: 설정 "저장" 시 표시 모드가 그대로인데도 창 기하를 재적용해 창모드 창이 옛 위치·크기로
/// 점프하던 버그를 없앤다(it16 §7.1). 모드가 실제로 바뀔 때만 창에 손댄다.
/// </summary>
public static class DisplayApplyPolicy
{
    public static DisplayApplyAction Decide(DisplayMode target, DisplayMode? appliedMode)
        => appliedMode == target
            ? DisplayApplyAction.None
            : target == DisplayMode.Fullscreen
                ? DisplayApplyAction.Fullscreen
                : DisplayApplyAction.WindowedRestoreGeometry;
}
