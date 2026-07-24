using System.IO;
using MCPhoto.App;
using MCPhoto.App.ViewModels;
using MCPhoto.Core.Accounts;
using MCPhoto.Core.Models;
using MCPhoto.Core.Navigation;
using MCPhoto.Core.Settings;

namespace MCPhoto.Tests;

/// <summary>
/// item1a §9.4: 비밀번호 찾기(비로그인 재설정) 2단계 플로우 단위 검증.
/// - 1단계 요청: 서비스 호출 + 열거 방지(존재 무관 2단계 진행).
/// - 2단계 확인: 비번 불일치 사전 차단, 코드/새 비번 서비스 전달.
/// 셸 복귀(ReturnFromOverlay) 부수효과는 최소 ServiceProvider(HomeViewModel 제공)로 안전하게 처리.
/// </summary>
public class PasswordResetViewModelTests
{
    /// <summary>ReturnFromOverlay가 기본 복귀 지점(Home) VM을 요청하므로 HomeViewModel만 제공.</summary>
    private sealed class ShellServiceProvider : IServiceProvider
    {
        public AppShellViewModel? Shell { get; set; }
        public object? GetService(Type serviceType)
            => serviceType == typeof(HomeViewModel) && Shell is not null
                ? new HomeViewModel(Shell)
                : null;
    }

    /// <summary>재설정 호출을 기록하는 fake. 예외를 주입해 실패 경로도 검증한다.</summary>
    private sealed class RecordingAccountService : IAccountService
    {
        public string? RequestedIdOrEmail { get; private set; }
        public (string idOrEmail, string code, string newPassword)? ConfirmByCode { get; private set; }
        public Exception? ConfirmThrows { get; set; }

        public Task<User?> LoginAsync(string id, string password, CancellationToken ct = default)
            => Task.FromResult<User?>(null);
        public Task<User> CreateAsync(string id, string password, UserRole role, string? email, UserRole actingRole, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task ChangePasswordAsync(string id, string newPassword, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<User>> GetAllAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<User>>(Array.Empty<User>());
        public Task DeleteAsync(string id, CancellationToken ct = default) => Task.CompletedTask;
        public Task SetRoleAsync(string id, UserRole role, CancellationToken ct = default) => Task.CompletedTask;
        public Task EnsureSeedAccountAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task SetEmailAsync(string id, string email, CancellationToken ct = default) => Task.CompletedTask;

        public Task RequestPasswordResetAsync(string idOrEmail, CancellationToken ct = default)
        {
            RequestedIdOrEmail = idOrEmail;
            return Task.CompletedTask;
        }
        public Task ConfirmPasswordResetAsync(string id, string token, string newPassword, CancellationToken ct = default)
            => Task.CompletedTask;
        public Task ConfirmPasswordResetByCodeAsync(string idOrEmail, string code, string newPassword, CancellationToken ct = default)
        {
            if (ConfirmThrows is not null) throw ConfirmThrows;
            ConfirmByCode = (idOrEmail, code, newPassword);
            return Task.CompletedTask;
        }
        public Task RequestEmailVerificationAsync(string idOrEmail, CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> ConfirmEmailVerificationAsync(string id, string code, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> ConfirmEmailVerificationByTokenAsync(string id, string token, CancellationToken ct = default) => Task.FromResult(true);
    }

    private static (PasswordResetViewModel vm, RecordingAccountService accounts) MakeVm()
    {
        var settings = new IniSettingsService(iniPath: Path.Combine(Path.GetTempPath(), $"prvm_{Guid.NewGuid():N}.ini"));
        settings.Load();
        var provider = new ShellServiceProvider();
        var session = new SessionContext();
        var shell = new AppShellViewModel(new IdleWatchdog(), settings, provider, session);
        provider.Shell = shell;
        var accounts = new RecordingAccountService();
        return (new PasswordResetViewModel(shell, accounts), accounts);
    }

    [Fact]
    public async Task Enter_Starts_On_Request_Step()
    {
        var (vm, _) = MakeVm();
        await vm.OnEnterAsync();
        Assert.True(vm.IsRequestStep);
        Assert.False(vm.IsConfirmStep);
    }

    [Fact]
    public async Task Request_Empty_Input_Shows_Error_And_Skips_Service()
    {
        var (vm, accounts) = MakeVm();
        vm.IdOrEmail = "   ";
        await vm.RequestResetCommand.ExecuteAsync(null);

        Assert.True(vm.MessageIsError);
        Assert.Null(accounts.RequestedIdOrEmail); // 서비스 미호출
        Assert.False(vm.IsConfirmStep);            // 단계 전환 없음
    }

    [Fact]
    public async Task Request_Calls_Service_And_Advances_To_Confirm_Step()
    {
        var (vm, accounts) = MakeVm();
        vm.IdOrEmail = "someone@x.com";
        await vm.RequestResetCommand.ExecuteAsync(null);

        Assert.Equal("someone@x.com", accounts.RequestedIdOrEmail);
        Assert.True(vm.IsConfirmStep);   // 열거 방지: 존재 무관 2단계 진행
        Assert.False(vm.MessageIsError);
    }

    [Fact]
    public async Task Confirm_Password_Mismatch_Blocks_Before_Service()
    {
        var (vm, accounts) = MakeVm();
        vm.IsConfirmStep = true;
        vm.Code = "123456";
        vm.NewPassword = "aaa";
        vm.ConfirmPassword = "bbb"; // 불일치

        await vm.ConfirmResetCommand.ExecuteAsync(null);

        Assert.True(vm.MessageIsError);
        Assert.Null(accounts.ConfirmByCode); // 서비스 미호출
    }

    [Fact]
    public async Task Confirm_Passes_Code_And_Password_To_Service()
    {
        var (vm, accounts) = MakeVm();
        vm.IdOrEmail = "u1";
        vm.IsConfirmStep = true;
        vm.Code = "654321";
        vm.NewPassword = "newpw";
        vm.ConfirmPassword = "newpw";

        await vm.ConfirmResetCommand.ExecuteAsync(null);

        Assert.NotNull(accounts.ConfirmByCode);
        Assert.Equal(("u1", "654321", "newpw"), accounts.ConfirmByCode!.Value);
    }

    [Fact]
    public async Task Confirm_Invalid_Code_Shows_Error()
    {
        var (vm, accounts) = MakeVm();
        accounts.ConfirmThrows = new InvalidOperationException("코드 불일치"); // 401 매핑 상당
        vm.IdOrEmail = "u1";
        vm.IsConfirmStep = true;
        vm.Code = "000000";
        vm.NewPassword = "newpw";
        vm.ConfirmPassword = "newpw";

        await vm.ConfirmResetCommand.ExecuteAsync(null);

        Assert.True(vm.MessageIsError);
        Assert.True(vm.IsConfirmStep); // 실패 시 단계 유지(재시도 가능)
    }
}
