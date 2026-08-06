using MCPhoto.Core.Navigation;

namespace MCPhoto.Tests;

/// <summary>WBS Step 4: 상태 전이 규칙·불법 전이 거부·유휴 감시·IdleWatchdog 검증.</summary>
public class AppStateTests
{
    [Fact]
    public void Normal_Flow_Is_Legal()
    {
        // Home→Login→FrameSelect→Guide→Capture→CutSelect→Result→Qr→Home
        // (완료 화면 폐지: 세션 완료는 상태 전이가 아니라 홈 복귀 + 완료 토스트다)
        Assert.True(SessionStateMachine.CanTransition(AppState.Home, AppState.Login));
        Assert.True(SessionStateMachine.CanTransition(AppState.Login, AppState.FrameSelect));
        Assert.True(SessionStateMachine.CanTransition(AppState.FrameSelect, AppState.Guide));
        Assert.True(SessionStateMachine.CanTransition(AppState.Guide, AppState.Capture));
        Assert.True(SessionStateMachine.CanTransition(AppState.Capture, AppState.CutSelect));
        Assert.True(SessionStateMachine.CanTransition(AppState.CutSelect, AppState.Result));
        Assert.True(SessionStateMachine.CanTransition(AppState.Result, AppState.Qr));
        Assert.True(SessionStateMachine.CanTransition(AppState.Qr, AppState.Home));      // QR [완료] → 홈
        Assert.True(SessionStateMachine.CanTransition(AppState.Result, AppState.Home));  // QR 미사용 즉시 완료 → 홈
    }

    /// <summary>
    /// 완료 화면 폐지 회귀: 세션 완료는 상태가 아니라 홈 복귀 + 완료 토스트다
    /// (<c>AppShellViewModel.CompleteSession</c>). 상태가 되살아나면 화면 하나가 다시 끼어든다.
    /// </summary>
    [Fact]
    public void Done_State_Is_Retired()
    {
        Assert.DoesNotContain("Done", Enum.GetNames<AppState>());
    }

    [Fact]
    public void Home_Return_Always_Legal()
    {
        foreach (AppState from in Enum.GetValues<AppState>())
            Assert.True(SessionStateMachine.CanTransition(from, AppState.Home),
                $"{from}→Home 은 항상 합법이어야 함");
    }

    [Fact]
    public void Illegal_Transitions_Rejected()
    {
        // 홈에서 바로 촬영으로 건너뛰기 불가
        Assert.False(SessionStateMachine.CanTransition(AppState.Home, AppState.Capture));
        // 결과에서 촬영으로 되돌아가기 불가(재촬영은 CutSelect→Guide)
        Assert.False(SessionStateMachine.CanTransition(AppState.Result, AppState.Capture));
        // 카운트다운 중 결과로 점프 불가
        Assert.False(SessionStateMachine.CanTransition(AppState.Capture, AppState.Result));
        // QR에서 프레임 선택으로 불가
        Assert.False(SessionStateMachine.CanTransition(AppState.Qr, AppState.FrameSelect));
    }

    [Fact]
    public void Retake_From_CutSelect_To_Guide_Is_Legal()
    {
        // 재촬영 = 세션 전체(CutSelect→Guide)
        Assert.True(SessionStateMachine.CanTransition(AppState.CutSelect, AppState.Guide));
    }

    [Fact]
    public void Self_Transition_Rejected()
    {
        Assert.False(SessionStateMachine.CanTransition(AppState.Capture, AppState.Capture));
    }

    [Fact]
    public void Session_Active_States_Identified()
    {
        Assert.True(SessionStateMachine.IsSessionActive(AppState.Capture));
        Assert.True(SessionStateMachine.IsSessionActive(AppState.CutSelect));
        Assert.True(SessionStateMachine.IsSessionActive(AppState.Result));

        // Home은 유휴 감시 비대상
        Assert.False(SessionStateMachine.IsSessionActive(AppState.Home));
        // it2: Settings·Login도 유휴 감시 비대상(설정 조작 중 홈복귀 방지, §5.2)
        Assert.False(SessionStateMachine.IsSessionActive(AppState.Settings));
        Assert.False(SessionStateMachine.IsSessionActive(AppState.Login));
    }

    // ── it4 §4 (B5): 편집기 유휴 타임아웃 제외 ──

    [Fact]
    public void FrameEditor_Excluded_From_Idle_Watch()
    {
        // 편집기는 로그인 필수 능동 작업 → 촬영용 유휴 타임아웃 대상 아님(홈복귀·로그아웃 방지).
        Assert.False(SessionStateMachine.IsSessionActive(AppState.FrameEditor));
    }

    [Fact]
    public void Capture_Flow_Idle_Watch_Not_Regressed()
    {
        // 촬영 흐름(FrameSelect~Qr)은 여전히 유휴 감시 대상(무인 키오스크 보호 유지).
        Assert.True(SessionStateMachine.IsSessionActive(AppState.FrameSelect));
        Assert.True(SessionStateMachine.IsSessionActive(AppState.Guide));
        Assert.True(SessionStateMachine.IsSessionActive(AppState.Capture));
        Assert.True(SessionStateMachine.IsSessionActive(AppState.CutSelect));
        Assert.True(SessionStateMachine.IsSessionActive(AppState.Result));
        Assert.True(SessionStateMachine.IsSessionActive(AppState.Qr));
    }

    // ── it2 §5.2: Settings/Login 오버레이 특례 + 촬영 게스트 직행 ──

    [Fact]
    public void Settings_Reachable_From_Anywhere()
    {
        foreach (AppState from in Enum.GetValues<AppState>())
        {
            if (from == AppState.Settings) continue;
            Assert.True(SessionStateMachine.CanTransition(from, AppState.Settings),
                $"{from}→Settings 는 어디서든 합법이어야 함");
        }
    }

    [Fact]
    public void Login_Reachable_From_Anywhere()
    {
        foreach (AppState from in Enum.GetValues<AppState>())
        {
            if (from == AppState.Login) continue;
            Assert.True(SessionStateMachine.CanTransition(from, AppState.Login),
                $"{from}→Login 은 어디서든 합법이어야 함");
        }
    }

    [Fact]
    public void Home_To_FrameSelect_Legal()
    {
        // 게스트 촬영 직행(홈 [촬영하기] → 프레임 선택)
        Assert.True(SessionStateMachine.CanTransition(AppState.Home, AppState.FrameSelect));
    }

    [Fact]
    public void UserMgmt_Back_To_Account_Legal()
    {
        // it5 C2: 사용자 관리는 관리자 도구(Account)에서 진입·복귀(설정에서 계정 분리).
        Assert.True(SessionStateMachine.CanTransition(AppState.UserMgmt, AppState.Account));
    }

    // ── it5 §5 C2: 계정 전용 페이지(Account) 오버레이 ──

    [Fact]
    public void Account_Reachable_From_Anywhere()
    {
        foreach (AppState from in Enum.GetValues<AppState>())
        {
            if (from == AppState.Account) continue;
            Assert.True(SessionStateMachine.CanTransition(from, AppState.Account),
                $"{from}→Account 는 어디서든 합법이어야 함(오버레이 진입)");
        }
    }

    [Fact]
    public void Account_Not_Idle_Watched_And_TopBar_Visible()
    {
        // 계정 페이지는 유휴 감시 비대상(능동 작업) + 상단바 표시(몰입 화면 아님).
        Assert.False(SessionStateMachine.IsSessionActive(AppState.Account));
        Assert.True(SessionStateMachine.IsTopBarVisible(AppState.Account));
    }

    [Fact]
    public void Account_To_UserMgmt_Legal()
    {
        // 관리자 도구(Account) → 사용자 관리 진입.
        Assert.True(SessionStateMachine.CanTransition(AppState.Account, AppState.UserMgmt));
    }

    // ── it19: 오버레이 화면 분류(복귀 지점 저장 제외 집합) ──

    [Theory]
    [InlineData(AppState.Settings)]
    [InlineData(AppState.Login)]
    [InlineData(AppState.Account)]
    [InlineData(AppState.UserMgmt)]
    public void Overlay_Screens_Excluded_From_Return_Point(AppState state)
    {
        // 이 화면들에서 오버레이로 전환할 때 복귀 지점을 덮어쓰면 [닫기]가 자기 자신으로 복귀한다.
        Assert.True(SessionStateMachine.IsOverlayScreen(state), $"{state}는 오버레이 화면이어야 함");
    }

    [Theory]
    [InlineData(AppState.Home)]
    [InlineData(AppState.FrameSelect)]
    [InlineData(AppState.Guide)]
    [InlineData(AppState.Capture)]
    [InlineData(AppState.CutSelect)]
    [InlineData(AppState.Result)]
    [InlineData(AppState.Qr)]
    [InlineData(AppState.FrameEditor)]
    public void Non_Overlay_Screens_Are_Valid_Return_Points(AppState state)
    {
        // 촬영 흐름·홈·편집기는 복귀 지점이 되어야 한다(오버레이를 닫으면 여기로 돌아온다).
        Assert.False(SessionStateMachine.IsOverlayScreen(state), $"{state}는 오버레이 화면이 아니어야 함");
    }

    // ── it2 리뷰 사이클1 Major 회귀: 오버레이 복귀 방향 ──
    // 버그: ReturnFromOverlay가 CanTransition(Settings, _returnState)에 막혀 세션 화면 복귀 실패.
    // 근본 원인은 복귀 방향(Settings→세션화면)이 전이표에 없다는 것 — 이는 의도된 설계다
    // (진입만 특례, 복귀는 저장된 상태로 검증 면제 복귀). 아래 테스트는 그 전제를 고정한다.

    [Theory]
    [InlineData(AppState.Guide)]
    [InlineData(AppState.CutSelect)]
    [InlineData(AppState.Result)]
    [InlineData(AppState.Qr)]
    public void Overlay_Return_Direction_Not_Forward_Legal(AppState sessionState)
    {
        // 복귀 방향(Settings→세션화면)은 전이표상 불법이다(특례 아님).
        // 따라서 ReturnFromOverlay는 반드시 검증을 면제해야 복귀가 성립한다.
        // 이 값이 True로 바뀌면(전이표에 세션 화면이 Settings.forward로 추가되면) 설계 위반이므로 실패로 잡는다.
        // (FrameSelect는 Login.forward에 정상 포함되므로 이 케이스에서 제외 — Settings 기준만 검증)
        Assert.False(SessionStateMachine.CanTransition(AppState.Settings, sessionState),
            $"Settings→{sessionState}는 전이표상 불법이어야 하며, 복귀는 검증 면제 경로로만 이뤄져야 한다");
    }

    [Fact]
    public void Overlay_Return_To_FrameSelect_Needs_Bypass_From_Settings()
    {
        // 프레임 선택 복귀도 Settings 기준으론 전이표상 불법(Settings.forward에 없음) → 검증 면제 필요.
        Assert.False(SessionStateMachine.CanTransition(AppState.Settings, AppState.FrameSelect));
    }

    [Fact]
    public void Overlay_Entry_Direction_Is_Legal_From_Session_States()
    {
        // 진입 방향(세션화면→Settings/Login)은 특례로 항상 합법이어야 한다.
        foreach (var s in new[] { AppState.FrameSelect, AppState.Guide, AppState.CutSelect, AppState.Result })
        {
            Assert.True(SessionStateMachine.CanTransition(s, AppState.Settings), $"{s}→Settings 합법");
            Assert.True(SessionStateMachine.CanTransition(s, AppState.Login), $"{s}→Login 합법");
        }
    }

    [Fact]
    public void Capture_Flow_Unchanged()
    {
        // 촬영 흐름은 변경되지 않음(기존 유지)
        Assert.True(SessionStateMachine.CanTransition(AppState.Guide, AppState.Capture));
        Assert.True(SessionStateMachine.CanTransition(AppState.Capture, AppState.CutSelect));
        Assert.True(SessionStateMachine.CanTransition(AppState.CutSelect, AppState.Result));
        // Home→Capture는 여전히 불가(특례 아님)
        Assert.False(SessionStateMachine.CanTransition(AppState.Home, AppState.Capture));
    }

    // ── it2 §3.1: 상단 바 가시성 ──

    [Fact]
    public void TopBar_Hidden_On_Immersive_States()
    {
        // 촬영·QR 팝업에서 숨김
        Assert.False(SessionStateMachine.IsTopBarVisible(AppState.Capture));
        Assert.False(SessionStateMachine.IsTopBarVisible(AppState.Qr));
    }

    [Fact]
    public void TopBar_Visible_On_Static_States()
    {
        Assert.True(SessionStateMachine.IsTopBarVisible(AppState.Home));
        Assert.True(SessionStateMachine.IsTopBarVisible(AppState.FrameSelect));
        Assert.True(SessionStateMachine.IsTopBarVisible(AppState.Settings));
        Assert.True(SessionStateMachine.IsTopBarVisible(AppState.Result));
    }

    [Fact]
    public async Task IdleWatchdog_Fires_After_Timeout()
    {
        using var wd = new IdleWatchdog();
        var fired = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        wd.IdleTimeout += (_, _) => fired.TrySetResult(true);

        wd.Start(1); // 1초

        var completed = await Task.WhenAny(fired.Task, Task.Delay(3000));
        Assert.Equal(fired.Task, completed);
        Assert.True(await fired.Task);
    }

    [Fact]
    public async Task IdleWatchdog_Reset_Prevents_Timeout()
    {
        using var wd = new IdleWatchdog();
        int fireCount = 0;
        wd.IdleTimeout += (_, _) => Interlocked.Increment(ref fireCount);

        wd.Start(1);
        // 0.5초마다 리셋을 3회 → 1초 타임아웃 도달 방지
        for (int i = 0; i < 3; i++)
        {
            await Task.Delay(500);
            wd.Reset();
        }
        Assert.Equal(0, fireCount); // 아직 안 터짐
    }

    [Fact]
    public async Task IdleWatchdog_Stop_Prevents_Timeout()
    {
        using var wd = new IdleWatchdog();
        int fireCount = 0;
        wd.IdleTimeout += (_, _) => Interlocked.Increment(ref fireCount);

        wd.Start(1);
        wd.Stop();
        await Task.Delay(1500);
        Assert.Equal(0, fireCount);
    }
}
