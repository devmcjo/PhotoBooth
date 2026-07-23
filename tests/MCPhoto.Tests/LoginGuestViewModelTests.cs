using System.IO;
using MCPhoto.App;
using MCPhoto.App.ViewModels;
using MCPhoto.Core.Accounts;
using MCPhoto.Core.Models;
using MCPhoto.Core.Navigation;
using MCPhoto.Core.Settings;
using MCPhoto.Core.Upload;

namespace MCPhoto.Tests;

/// <summary>
/// it10 S2-1: 서버 미연결(Firebase 미초기화) 시 로그인 UX.
/// - 비시드 계정 로그인 실패는 "아이디/비밀번호 불일치"가 아니라 오프라인 메시지로 분기.
/// - 초기화된 상태(온라인)에서는 기존 메시지 유지.
/// 성공 경로는 shell 오버레이 복귀 부수효과가 있어 여기선 다루지 않음(로그인 실패 분기만 단위 검증).
/// </summary>
public class LoginGuestViewModelTests
{
    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    /// <summary>항상 null(로그인 실패)을 반환하는 계정 서비스 — 실패 메시지 분기만 검증.</summary>
    private sealed class NullAccountService : IAccountService
    {
        public Task<User?> LoginAsync(string id, string password, CancellationToken ct = default)
            => Task.FromResult<User?>(null);
        public Task<User> CreateAsync(string id, string password, UserRole role, UserRole actingRole, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task ChangePasswordAsync(string id, string newPassword, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<User>> GetAllAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<User>>(Array.Empty<User>());
        public Task DeleteAsync(string id, CancellationToken ct = default) => Task.CompletedTask;
        public Task SetRoleAsync(string id, UserRole role, CancellationToken ct = default) => Task.CompletedTask;
        public Task EnsureSeedAccountAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private static LoginGuestViewModel MakeVm(bool serverInitialized)
    {
        var settings = new IniSettingsService(iniPath: Path.Combine(Path.GetTempPath(), $"lgvm_{Guid.NewGuid():N}.ini"));
        settings.Load();
        var session = new SessionContext();
        var shell = new AppShellViewModel(new IdleWatchdog(), settings, new EmptyServiceProvider(), session);
        var firebase = new FakeFirebaseClient { IsInitialized = serverInitialized };
        return new LoginGuestViewModel(shell, new NullAccountService(), firebase);
    }

    [Fact]
    public void Offline_Exposes_IsServerOffline_True()
    {
        var vm = MakeVm(serverInitialized: false);
        Assert.True(vm.IsServerOffline);
    }

    [Fact]
    public void Online_Exposes_IsServerOffline_False()
    {
        var vm = MakeVm(serverInitialized: true);
        Assert.False(vm.IsServerOffline);
    }

    [Fact]
    public async Task Offline_NonSeed_Login_Shows_Offline_Message()
    {
        var vm = MakeVm(serverInitialized: false);
        vm.LoginId = "manager";     // 비시드 계정
        vm.Password = "whatever";

        await vm.LoginCommand.ExecuteAsync(null);

        Assert.Equal("서버 미연결 상태에서는 이 계정으로 로그인할 수 없습니다.", vm.ErrorMessage);
    }

    [Fact]
    public async Task Offline_Seed_Wrong_Password_Keeps_Credential_Message()
    {
        // 시드(devmcjo) 계정은 오프라인이라도 유효 → 실패는 자격증명 문제로 안내(오프라인 메시지 금지).
        var vm = MakeVm(serverInitialized: false);
        vm.LoginId = "devmcjo";
        vm.Password = "wrong";

        await vm.LoginCommand.ExecuteAsync(null);

        Assert.Equal("아이디 또는 비밀번호가 올바르지 않습니다.", vm.ErrorMessage);
    }

    [Fact]
    public async Task Online_Login_Failure_Keeps_Credential_Message()
    {
        // 온라인(초기화됨)에서 실패는 기존 자격증명 메시지 유지(오프라인 분기 발동 금지).
        var vm = MakeVm(serverInitialized: true);
        vm.LoginId = "manager";
        vm.Password = "whatever";

        await vm.LoginCommand.ExecuteAsync(null);

        Assert.Equal("아이디 또는 비밀번호가 올바르지 않습니다.", vm.ErrorMessage);
    }
}
