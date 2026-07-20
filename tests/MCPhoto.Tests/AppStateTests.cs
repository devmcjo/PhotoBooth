using MCPhoto.Core.Navigation;

namespace MCPhoto.Tests;

/// <summary>WBS Step 4: 상태 전이 규칙·불법 전이 거부·유휴 감시·IdleWatchdog 검증.</summary>
public class AppStateTests
{
    [Fact]
    public void Normal_Flow_Is_Legal()
    {
        // Home→Login→FrameSelect→Guide→Capture→CutSelect→Result→Qr→Done→Home
        Assert.True(SessionStateMachine.CanTransition(AppState.Home, AppState.Login));
        Assert.True(SessionStateMachine.CanTransition(AppState.Login, AppState.FrameSelect));
        Assert.True(SessionStateMachine.CanTransition(AppState.FrameSelect, AppState.Guide));
        Assert.True(SessionStateMachine.CanTransition(AppState.Guide, AppState.Capture));
        Assert.True(SessionStateMachine.CanTransition(AppState.Capture, AppState.CutSelect));
        Assert.True(SessionStateMachine.CanTransition(AppState.CutSelect, AppState.Result));
        Assert.True(SessionStateMachine.CanTransition(AppState.Result, AppState.Qr));
        Assert.True(SessionStateMachine.CanTransition(AppState.Qr, AppState.Done));
        Assert.True(SessionStateMachine.CanTransition(AppState.Done, AppState.Home));
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
        Assert.True(SessionStateMachine.IsSessionActive(AppState.FrameEditor));

        // Home·Done·Admin은 유휴 감시 비대상
        Assert.False(SessionStateMachine.IsSessionActive(AppState.Home));
        Assert.False(SessionStateMachine.IsSessionActive(AppState.Done));
        Assert.False(SessionStateMachine.IsSessionActive(AppState.Admin));
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
