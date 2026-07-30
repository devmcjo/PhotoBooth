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
        [AppState.Settings] = new[] { AppState.Login, AppState.FrameEditor },
        [AppState.UserMgmt] = new[] { AppState.Account }, // 관리자 도구(Account) 복귀
        [AppState.FrameEditor] = new[] { AppState.FrameSelect, AppState.Settings, AppState.Login },
        [AppState.Account] = new[] { AppState.UserMgmt } // 관리자 도구 → 사용자 관리
    };

    /// <summary>
    /// from → to 전이가 합법인지. Home·Settings·Login·Account로의 전이는 어디서든 허용(오버레이 진입).
    /// (it2 §5.2 특례 + it5 §5 C2에서 Account 추가)
    /// </summary>
    public static bool CanTransition(AppState from, AppState to)
    {
        // 오버레이성 진입/복귀는 어디서든 허용(취소·유휴·예외·완료·설정·로그인·계정).
        // 자기 자신으로의 오버레이 전이도 무해 허용(예: Home→Home 복귀). 기존 동작 보존.
        if (to is AppState.Home or AppState.Settings or AppState.Login or AppState.Account) return true;
        if (from == to) return false; // 그 외 자기 자신 전이는 무의미
        return Forward.TryGetValue(from, out var next) && Array.IndexOf(next, to) >= 0;
    }

    /// <summary>
    /// 세션 진행(촬영 흐름) 중 상태인지 — 유휴 감시 대상. Settings·Login은 비대상(it2 §5.2).
    /// FrameEditor는 로그인 필수 능동 작업(관리/커스텀)이라 촬영용 유휴 타임아웃 대상이 아니다(it4 §4 B5).
    /// </summary>
    public static bool IsSessionActive(AppState state) => state
        is AppState.FrameSelect
        or AppState.Guide
        or AppState.Capture
        or AppState.CutSelect
        or AppState.Result
        or AppState.Qr;

    /// <summary>
    /// 오버레이성 화면(촬영 흐름 밖의 설정·로그인·계정 계열)인지 — 복귀 지점 저장 제외 판정. (it19)
    /// 오버레이끼리 전환할 때 복귀 지점을 덮어쓰면 [닫기]가 자기 자신으로 복귀해 아무 일도 하지 않는다.
    /// UserMgmt는 관리자 도구(Account)의 하위 페이지라 같은 묶음이다(복귀 지점이 되면 Account↔UserMgmt를 벗어날 수 없다).
    /// </summary>
    public static bool IsOverlayScreen(AppState state) => state
        is AppState.Settings or AppState.Login or AppState.Account or AppState.UserMgmt;

    /// <summary>
    /// 상단 바(로그인·설정 버튼)를 표시할 상태인지. 몰입/모달 화면(촬영·카운트다운·QR 팝업)에서는 숨김.
    /// (it2 §3.1 — 오조작·산만 방지)
    /// </summary>
    public static bool IsTopBarVisible(AppState state) => state
        is not (AppState.Capture or AppState.Qr);
}
