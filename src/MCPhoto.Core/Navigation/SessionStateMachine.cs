namespace MCPhoto.Core.Navigation;

/// <summary>
/// 키오스크 세션 상태 전이 규칙(순수 로직, 테스트 대상). (architecture §4.1)
/// 정상 흐름: Home→Login→FrameSelect→Guide→Capture→CutSelect→Result→Qr→Done→Home.
/// 어느 상태에서든 Home 복귀 허용(유휴 만료·예외·취소·세션 완료).
/// </summary>
public static class SessionStateMachine
{
    // 각 상태에서 사용자 액션으로 진행 가능한 다음 상태들(Home 복귀는 별도 항상 허용).
    private static readonly Dictionary<AppState, AppState[]> Forward = new()
    {
        [AppState.Home] = new[] { AppState.Login, AppState.FrameSelect, AppState.Admin },
        [AppState.Login] = new[] { AppState.FrameSelect, AppState.Admin, AppState.FrameEditor },
        [AppState.FrameSelect] = new[] { AppState.Guide, AppState.FrameEditor },
        [AppState.Guide] = new[] { AppState.Capture },
        [AppState.Capture] = new[] { AppState.CutSelect },
        [AppState.CutSelect] = new[] { AppState.Result, AppState.Guide }, // Guide=재촬영(세션 전체)
        [AppState.Result] = new[] { AppState.Qr, AppState.Done },
        [AppState.Qr] = new[] { AppState.Done },
        [AppState.Done] = new[] { AppState.Home },
        [AppState.Admin] = new[] { AppState.UserMgmt, AppState.FrameEditor },
        [AppState.UserMgmt] = new[] { AppState.Admin },
        [AppState.FrameEditor] = new[] { AppState.FrameSelect, AppState.Admin, AppState.Login }
    };

    /// <summary>from → to 전이가 합법인지. Home 복귀는 항상 합법.</summary>
    public static bool CanTransition(AppState from, AppState to)
    {
        if (to == AppState.Home) return true; // 취소·유휴·예외·완료 복귀는 어디서든 허용
        if (from == to) return false;         // 자기 자신 전이 무의미
        return Forward.TryGetValue(from, out var next) && Array.IndexOf(next, to) >= 0;
    }

    /// <summary>세션 진행(촬영 흐름) 중 상태인지 — 유휴 감시 대상.</summary>
    public static bool IsSessionActive(AppState state) => state
        is AppState.FrameSelect
        or AppState.Guide
        or AppState.Capture
        or AppState.CutSelect
        or AppState.Result
        or AppState.Qr
        or AppState.FrameEditor;
}
