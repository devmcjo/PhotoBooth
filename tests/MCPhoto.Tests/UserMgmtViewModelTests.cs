using System.IO;
using MCPhoto.App;
using MCPhoto.App.ViewModels;
using MCPhoto.Core.Accounts;
using MCPhoto.Core.Models;
using MCPhoto.Core.Navigation;
using MCPhoto.Core.Settings;

namespace MCPhoto.Tests;

/// <summary>
/// W-2: 역할 양방향 변경 UI(admin: manager↔user). 승격(PromoteToManager)과 대칭인 강등(DemoteToUser)의
/// 가드·SetRole 호출·no-op 분기를 단위 검증. (설계 §1.3, §8 W-2)
/// </summary>
public class UserMgmtViewModelTests
{
    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    /// <summary>SetRoleAsync 호출을 기록하는 계정 서비스. GetAllAsync는 주입 목록을 반환(Reload 검증용).</summary>
    private sealed class SpyAccountService : IAccountService
    {
        public IReadOnlyList<User> Accounts { get; set; } = Array.Empty<User>();
        public bool SetRoleCalled { get; private set; }
        public string? SetRoleId { get; private set; }
        public UserRole? SetRoleValue { get; private set; }

        public Task SetRoleAsync(string id, UserRole role, CancellationToken ct = default)
        {
            SetRoleCalled = true;
            SetRoleId = id;
            SetRoleValue = role;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<User>> GetAllAsync(CancellationToken ct = default) => Task.FromResult(Accounts);

        public Task<User?> LoginAsync(string id, string password, CancellationToken ct = default) => Task.FromResult<User?>(null);
        public Task<bool> VerifyPasswordAsync(string id, string password, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<User?> LoginWithGoogleAsync(string code, string codeVerifier, string redirectUri, string? nonce = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<User?> RegisterAsync(string id, string password, string? email, CancellationToken ct = default) => Task.FromResult<User?>(null);
        public Task<User> CreateAsync(string id, string password, UserRole role, string? email, UserRole actingRole, CancellationToken ct = default) => throw new NotSupportedException();
        public Task ChangePasswordAsync(string id, string newPassword, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteAsync(string id, CancellationToken ct = default) => Task.CompletedTask;
        public Task EnsureSeedAccountAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task SetEmailAsync(string id, string email, CancellationToken ct = default) => Task.CompletedTask;
        public Task RequestPasswordResetAsync(string idOrEmail, CancellationToken ct = default) => Task.CompletedTask;
        public Task ConfirmPasswordResetAsync(string id, string token, string newPassword, CancellationToken ct = default) => Task.CompletedTask;
        public Task ConfirmPasswordResetByCodeAsync(string idOrEmail, string code, string newPassword, CancellationToken ct = default) => Task.CompletedTask;
        public Task RequestEmailVerificationAsync(string idOrEmail, CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> ConfirmEmailVerificationAsync(string id, string code, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> ConfirmEmailVerificationByTokenAsync(string id, string token, CancellationToken ct = default) => Task.FromResult(true);
    }

    /// <summary>admin 세션으로 진입한 UserMgmtViewModel과 spy를 구성한다.</summary>
    private static async Task<(UserMgmtViewModel vm, SpyAccountService accounts, SessionContext session)>
        MakeAdminVmAsync(User? adminSessionUser = null, IReadOnlyList<User>? accountList = null)
    {
        var iniPath = Path.Combine(Path.GetTempPath(), $"umvm_{Guid.NewGuid():N}.ini");
        var settings = new IniSettingsService(iniPath: iniPath);
        settings.Load();

        var session = new SessionContext();
        session.Login(adminSessionUser ?? new User { Id = "devmcjo", Role = UserRole.Admin });

        var shell = new AppShellViewModel(new IdleWatchdog(), settings, new EmptyServiceProvider(), session);
        var accounts = new SpyAccountService { Accounts = accountList ?? Array.Empty<User>() };
        var vm = new UserMgmtViewModel(shell, accounts);
        await vm.OnEnterAsync(); // ActorRole=Admin, IsAdmin=true, 목록 로드
        return (vm, accounts, session);
    }

    [Fact]
    public async Task Demote_ManagerToUser_CallsSetRole()
    {
        var target = new User { Id = "mgr1", Role = UserRole.Manager };
        var (vm, accounts, _) = await MakeAdminVmAsync();

        await vm.DemoteToUserCommand.ExecuteAsync(target);

        Assert.True(accounts.SetRoleCalled);
        Assert.Equal("mgr1", accounts.SetRoleId);
        Assert.Equal(UserRole.User, accounts.SetRoleValue);
    }

    [Fact]
    public async Task Demote_NonManager_NoOp()
    {
        // 대상이 manager가 아니면(user) 강등 no-op.
        var target = new User { Id = "user1", Role = UserRole.User };
        var (vm, accounts, _) = await MakeAdminVmAsync();

        await vm.DemoteToUserCommand.ExecuteAsync(target);

        Assert.False(accounts.SetRoleCalled);
    }

    [Fact]
    public async Task Demote_Self_NoOp()
    {
        // 자기 자신(세션 사용자)의 역할은 변경 금지 — manager 세션이 자기를 강등 시도해도 no-op.
        var self = new User { Id = "selfmgr", Role = UserRole.Manager };
        // admin 권한이어야 강등 가드를 통과하므로, 자기 방지 가드만 격리 검증하려면 대상=세션사용자.
        var (vm, accounts, session) = await MakeAdminVmAsync(
            adminSessionUser: new User { Id = "selfmgr", Role = UserRole.Admin });

        // 세션 사용자와 같은 Id + Manager 역할(대상). IsAdmin·Role 가드는 통과, 자기 방지 가드에서 차단.
        var target = new User { Id = "selfmgr", Role = UserRole.Manager };
        await vm.DemoteToUserCommand.ExecuteAsync(target);

        Assert.False(accounts.SetRoleCalled);
        Assert.Equal("자기 계정의 역할은 변경할 수 없습니다.", vm.StatusMessage);
    }
}
