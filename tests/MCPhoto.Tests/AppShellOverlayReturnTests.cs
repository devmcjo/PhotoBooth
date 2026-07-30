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
/// it19: 오버레이 복귀 지점(<c>_returnState</c>) 회귀 — 계정관리/관리자도구의 [닫기] 무반응 버그.
/// 근본 원인: <c>NavigateToOverlayAsync</c>가 복귀 지점 저장에서 Settings·Login만 제외해
/// 계정 페이지에서 상단바 팝오버로 계정 페이지에 재진입하면 <c>_returnState = Account</c>가 되고,
/// 이후 [닫기]가 Account → Account 복귀를 시도해 화면이 그대로 남았다.
/// 실제 창은 띄우지 않는다(headless — 화면 VM을 테스트 팩토리로 직접 등록).
/// </summary>
public class AppShellOverlayReturnTests
{
    /// <summary>목록 조회만 의미 있는 계정 서비스 스텁(사용자 관리 진입용). 나머지는 미지원.</summary>
    private sealed class StubAccountService : IAccountService
    {
        public Task<IReadOnlyList<User>> GetAllAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<User>>(Array.Empty<User>());

        public Task<User?> LoginWithGoogleAsync(string code, string codeVerifier, string redirectUri, string? nonce = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteAsync(string id, CancellationToken ct = default) => Task.CompletedTask;
        public Task SetRoleAsync(string id, UserRole role, CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> VerifyPinAsync(string id, string pin, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SetOwnPinAsync(string id, string? currentPin, string newPin, CancellationToken ct = default) => Task.CompletedTask;
        public Task ResetPinAsync(string targetId, string newPin, CancellationToken ct = default) => Task.CompletedTask;
    }

    /// <summary>
    /// 홈에서 출발한 셸. 계정 페이지 진입 PIN 게이트는 HasPin=true로 통과 상태로 두고(게이트가 아니라
    /// 복귀 지점이 검증 대상), Manager 역할로 관리자 도구·사용자 관리에 접근한다(Admin 전역 한도 로드 회피).
    /// </summary>
    private static async Task<AppShellViewModel> MakeShellAtHomeAsync()
    {
        var settings = new IniSettingsService(iniPath: Path.Combine(Path.GetTempPath(), $"shellret_{Guid.NewGuid():N}.ini"));
        settings.Load();

        var session = new SessionContext();
        session.Login(new User { Id = "m1", Role = UserRole.Manager, AuthMethod = AuthMethod.Google, HasPin = true });

        var accounts = new StubAccountService();
        var services = new MapServiceProvider().Add<IAccountService>(accounts);
        var shell = new AppShellViewModel(new IdleWatchdog(), settings, services, session);
        // 셸 순환 의존 → 지연 생성. 이 테스트가 지나는 상태(Home·Account·UserMgmt)만 등록한다.
        services.AddFactory<HomeViewModel>(() => new HomeViewModel(shell));
        services.AddFactory<AccountViewModel>(() => new AccountViewModel(shell, accounts, new NullTempUserLimitsService()));
        services.AddFactory<UserMgmtViewModel>(() => new UserMgmtViewModel(shell, accounts));

        await shell.NavigateAsync(AppState.Home);
        return shell;
    }

    /// <summary>현재 화면이 계정 페이지임을 확인하고 [닫기]를 실행.</summary>
    private static async Task CloseAccountPageAsync(AppShellViewModel shell)
    {
        var vm = Assert.IsType<AccountViewModel>(shell.CurrentViewModel);
        await vm.CloseCommand.ExecuteAsync(null);
    }

    [Fact]
    public async Task Account_To_Account_ReEntry_Keeps_Return_Point_And_Close_Goes_Back()
    {
        var shell = await MakeShellAtHomeAsync();

        // 홈 → 팝오버 [계정 관리]
        await shell.OpenAccountManageCommand.ExecuteAsync(null);
        Assert.Equal(AppState.Account, shell.CurrentState);

        // 계정 페이지에서 상단바 팝오버로 [관리자 도구] — 오버레이끼리의 전환.
        await shell.OpenAdminToolsCommand.ExecuteAsync(null);
        Assert.Equal(AppState.Account, shell.CurrentState);
        Assert.Equal(AccountMode.Admin, Assert.IsType<AccountViewModel>(shell.CurrentViewModel).Mode);

        // [닫기] → 오버레이 진입 전 화면(Home). 복귀 지점이 Account로 덮였다면 화면이 그대로 남는다.
        await CloseAccountPageAsync(shell);
        Assert.Equal(AppState.Home, shell.CurrentState);
    }

    [Fact]
    public async Task Account_Same_Mode_ReEntry_Keeps_Return_Point()
    {
        var shell = await MakeShellAtHomeAsync();

        await shell.OpenAccountManageCommand.ExecuteAsync(null);
        await shell.OpenAccountManageCommand.ExecuteAsync(null);   // 같은 모드로 재진입(팝오버 오조작)
        Assert.Equal(AppState.Account, shell.CurrentState);

        await CloseAccountPageAsync(shell);
        Assert.Equal(AppState.Home, shell.CurrentState);
    }

    [Fact]
    public async Task UserMgmt_Popover_Entry_Keeps_Return_Point()
    {
        var shell = await MakeShellAtHomeAsync();

        await shell.OpenAdminToolsCommand.ExecuteAsync(null);
        var admin = Assert.IsType<AccountViewModel>(shell.CurrentViewModel);
        await admin.OpenUserManagementCommand.ExecuteAsync(null);
        Assert.Equal(AppState.UserMgmt, shell.CurrentState);

        // 사용자 관리에서 상단바 팝오버로 [계정 관리] — UserMgmt는 관리자 도구의 하위 페이지이므로
        // 복귀 지점이 되어선 안 된다(되면 Account ↔ UserMgmt를 벗어날 수 없다).
        await shell.OpenAccountManageCommand.ExecuteAsync(null);
        Assert.Equal(AppState.Account, shell.CurrentState);

        await CloseAccountPageAsync(shell);
        Assert.Equal(AppState.Home, shell.CurrentState);
    }

    [Fact]
    public async Task UserMgmt_Back_Button_Returns_To_Admin_Tools()
    {
        // 기존 동작 보존: 사용자 관리 [뒤로]는 복귀 지점을 쓰지 않고 관리자 도구로 직행한다.
        var shell = await MakeShellAtHomeAsync();

        await shell.OpenAdminToolsCommand.ExecuteAsync(null);
        await Assert.IsType<AccountViewModel>(shell.CurrentViewModel).OpenUserManagementCommand.ExecuteAsync(null);
        var mgmt = Assert.IsType<UserMgmtViewModel>(shell.CurrentViewModel);

        await mgmt.BackCommand.ExecuteAsync(null);

        Assert.Equal(AppState.Account, shell.CurrentState);
        Assert.Equal(AccountMode.Admin, Assert.IsType<AccountViewModel>(shell.CurrentViewModel).Mode);

        // 이어서 [닫기]는 여전히 진입 전 화면으로.
        await CloseAccountPageAsync(shell);
        Assert.Equal(AppState.Home, shell.CurrentState);
    }
}
