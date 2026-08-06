using System.IO;
using MCPhoto.App;
using MCPhoto.App.ViewModels;
using MCPhoto.Core.Accounts;
using MCPhoto.Core.Models;
using MCPhoto.Core.Navigation;
using MCPhoto.Core.Settings;
using MCPhoto.Tests.Fakes;

namespace MCPhoto.Tests;

/// <summary>
/// 완료 화면(종전 <c>AppState.Done</c>: "감사합니다" + [처음으로] + 6초 자동 복귀) 폐지 회귀.
/// 세션 완료는 이제 화면 전이가 아니라 <b>홈 복귀 + 완료 토스트</b>다(<c>CompleteSession</c>).
/// 실제 창은 띄우지 않는다(headless — 홈 VM만 테스트 팩토리로 등록).
/// </summary>
public class AppShellSessionCompleteTests
{
    private static AppShellViewModel MakeShell(SessionContext session)
    {
        var settings = new IniSettingsService(
            iniPath: Path.Combine(Path.GetTempPath(), $"shellcomplete_{Guid.NewGuid():N}.ini"));
        settings.Load();

        var services = new MapServiceProvider();
        var shell = new AppShellViewModel(new IdleWatchdog(), settings, services, session);
        services.AddFactory<HomeViewModel>(() => new HomeViewModel(shell));   // 셸 순환 의존 → 지연 생성
        return shell;
    }

    private static FrameTemplate MakeFrame()
    {
        var f = new FrameTemplate { Id = "f1", Name = "t" };
        f.Slots.Add(new Slot { Index = 0, X = 0, Y = 0, Width = 100, Height = 133 });
        return f;
    }

    [Fact]
    public async Task CompleteSession_Goes_Home_With_Toast()
    {
        var session = new SessionContext();
        var shell = MakeShell(session);
        await shell.NavigateAsync(AppState.Home);
        Assert.False(shell.HasToast);                 // 평소엔 토스트가 없다

        shell.CompleteSession("테스트 완료");

        Assert.Equal(AppState.Home, shell.CurrentState);
        Assert.True(shell.HasToast);
        Assert.Equal(AppShellViewModel.SessionCompleteMessage, shell.ToastMessage);
    }

    [Fact]
    public async Task CompleteSession_Discards_Capture_But_Keeps_Login()
    {
        // 촬영 후 로그인 유지(it5 §4 B8) + 촬영 데이터는 항상 폐기 — 완료 화면이 하던 규칙을 그대로 승계한다.
        var session = new SessionContext();
        session.Login(new User { Id = "u1", Role = UserRole.User, AuthMethod = AuthMethod.Google });
        session.Capture.Begin(MakeFrame(), 6);

        var shell = MakeShell(session);
        await shell.NavigateAsync(AppState.Home);

        shell.CompleteSession("테스트 완료");

        Assert.NotNull(session.CurrentUser);          // 로그아웃하지 않는다
        Assert.Null(session.Capture.Frame);           // 촬영 세션은 폐기
    }

    [Fact]
    public async Task DismissToast_Hides_Immediately()
    {
        var shell = MakeShell(new SessionContext());
        await shell.NavigateAsync(AppState.Home);
        shell.CompleteSession("테스트 완료");
        Assert.True(shell.HasToast);

        shell.DismissToastCommand.Execute(null);      // [확인] 버튼

        Assert.False(shell.HasToast);
        Assert.Equal(string.Empty, shell.ToastMessage);
    }

    [Fact]
    public async Task ShowToast_Replaces_Previous_Message()
    {
        var shell = MakeShell(new SessionContext());
        await shell.NavigateAsync(AppState.Home);

        shell.ShowToast("첫 번째");
        shell.ShowToast("두 번째");

        Assert.Equal("두 번째", shell.ToastMessage);  // 겹쳐 쌓이지 않는다(타이머도 교체)
    }

    [Fact]
    public async Task ShowToast_Empty_Message_Is_Hidden()
    {
        var shell = MakeShell(new SessionContext());
        await shell.NavigateAsync(AppState.Home);

        shell.ShowToast(string.Empty);

        Assert.False(shell.HasToast);
    }
}
