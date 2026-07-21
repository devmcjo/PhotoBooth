namespace MCPhoto.Core.Navigation;

/// <summary>
/// 키오스크 세션 상태 전이 규칙(순수 로직, 테스트 대상). (architecture §4.1)
/// 정상 흐름: Home→Login→FrameSelect→Guide→Capture→CutSelect→Result→Qr→Done→Home.
/// 어느 상태에서든 Home 복귀 허용(유휴 만료·예외·취소·세션 완료).
/// </summary>
public static class SessionStateMachine
{
    // 각 상태에서 사용자 액션으로 진행 가능한 다음 상태들.
    // Home·Settings·Login으로의 전이는 별도 특례로 항상 허용(오버레이성 진입, it2 §5.2).
    private static readonly Dictionary<AppState, AppState[]> Forward = new()
    {
        [AppState.Home] = new[] { AppState.FrameSelect, AppState.Login, AppState.Settings },
        [AppState.Login] = new[] { AppState.FrameSelect, AppState.FrameEditor, AppState.Settings },
        [AppState.FrameSelect] = new[] { AppState.Guide, AppState.FrameEditor },
        [AppState.Guide] = new[] { AppState.Capture },
        [AppState.Capture] = new[] { AppState.CutSelect },
        [AppState.CutSelect] = new[] { AppState.Result, AppState.Guide }, // Guide=재촬영(세션 전체)
        [AppState.Result] = new[] { AppState.Qr, AppState.Done },
        [AppState.Qr] = new[] { AppState.Done },
        [AppState.Done] = new[] { AppState.Home },
        [AppState.Settings] = new[] { AppState.Login, AppState.UserMgmt, AppState.FrameEditor },
        [AppState.UserMgmt] = new[] { AppState.Settings },
        [AppState.FrameEditor] = new[] { AppState.FrameSelect, AppState.Settings, AppState.Login }
    };

    /// <summary>
    /// from → to 전이가 합법인지. Home·Settings·Login으로의 전이는 어디서든 허용(오버레이 진입).
    /// (it2 §5.2 — 특례는 이 3개로 한정)
    /// </summary>
    public static bool CanTransition(AppState from, AppState to)
    {
        // 오버레이성 진입/복귀는 어디서든 허용(취소·유휴·예외·완료·설정·로그인).
        // 자기 자신으로의 오버레이 전이도 무해 허용(예: Home→Home 복귀). 기존 동작 보존.
        if (to is AppState.Home or AppState.Settings or AppState.Login) return true;
        if (from == to) return false; // 그 외 자기 자신 전이는 무의미
        return Forward.TryGetValue(from, out var next) && Array.IndexOf(next, to) >= 0;
    }

    /// <summary>세션 진행(촬영 흐름) 중 상태인지 — 유휴 감시 대상. Settings·Login은 비대상(it2 §5.2).</summary>
    public static bool IsSessionActive(AppState state) => state
        is AppState.FrameSelect
        or AppState.Guide
        or AppState.Capture
        or AppState.CutSelect
        or AppState.Result
        or AppState.Qr
        or AppState.FrameEditor;

    /// <summary>
    /// 상단 바(로그인·설정 버튼)를 표시할 상태인지. 몰입/모달 화면(촬영·카운트다운·QR 팝업)에서는 숨김.
    /// (it2 §3.1 — 오조작·산만 방지)
    /// </summary>
    public static bool IsTopBarVisible(AppState state) => state
        is not (AppState.Capture or AppState.Qr);
}
