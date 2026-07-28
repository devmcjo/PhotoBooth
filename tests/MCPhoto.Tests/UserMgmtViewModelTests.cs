using System.IO;
using System.Linq;
using MCPhoto.App;
using MCPhoto.App.ViewModels;
using MCPhoto.Core.Accounts;
using MCPhoto.Core.Models;
using MCPhoto.Core.Navigation;
using MCPhoto.Core.Settings;

namespace MCPhoto.Tests;

/// <summary>
/// it13 §9.5: 역할 변경 콤보+Apply(§8.7 매트릭스). 행별 지정 가능 역할 필터, Apply의 SetRole 호출·무변경 no-op·
/// 권한 밖 차단·서버 403 우아 처리(안내+목록 원복)를 단위 검증.
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
        public int ReloadCount { get; private set; }
        /// <summary>설정 시 SetRoleAsync가 이 예외를 던진다(서버 403 모사 등).</summary>
        public Exception? SetRoleThrows { get; set; }

        public Task SetRoleAsync(string id, UserRole role, CancellationToken ct = default)
        {
            SetRoleCalled = true;
            SetRoleId = id;
            SetRoleValue = role;
            if (SetRoleThrows is not null) throw SetRoleThrows;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<User>> GetAllAsync(CancellationToken ct = default)
        {
            ReloadCount++;
            return Task.FromResult(Accounts);
        }

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

    private static async Task<(UserMgmtViewModel vm, SpyAccountService accounts)> MakeVmAsync(
        UserRole actorRole, string actorId, IReadOnlyList<User> accountList)
    {
        var settings = new IniSettingsService(iniPath: Path.Combine(Path.GetTempPath(), $"umvm_{Guid.NewGuid():N}.ini"));
        settings.Load();
        var session = new SessionContext();
        session.Login(new User { Id = actorId, Role = actorRole });
        var shell = new AppShellViewModel(new IdleWatchdog(), settings, new EmptyServiceProvider(), session);
        var accounts = new SpyAccountService { Accounts = accountList };
        var vm = new UserMgmtViewModel(shell, accounts);
        await vm.OnEnterAsync();
        return (vm, accounts);
    }

    private static UserRowViewModel Row(UserMgmtViewModel vm, string id) => vm.Rows.First(r => r.User.Id == id);

    // ── 행별 지정 가능 역할(콤보 옵션) 필터 ──

    [Fact]
    public async Task Admin_Rows_Offer_All_Except_Admin_And_Self()
    {
        var list = new[]
        {
            new User { Id = "admin", Role = UserRole.Admin },   // 자기 계정
            new User { Id = "u1", Role = UserRole.User },
            new User { Id = "m1", Role = UserRole.Manager },
            new User { Id = "t1", Role = UserRole.TempUser },
            new User { Id = "otherAdmin", Role = UserRole.Admin },
        };
        var (vm, _) = await MakeVmAsync(UserRole.Admin, "admin", list);

        Assert.False(Row(vm, "admin").CanChangeRole);        // 자기 계정 미노출
        Assert.False(Row(vm, "otherAdmin").CanChangeRole);   // admin 대상 미노출
        var all = new[] { UserRole.TempUser, UserRole.User, UserRole.Manager };
        Assert.Equal(all, Row(vm, "u1").AssignableRoles);
        Assert.Equal(all, Row(vm, "m1").AssignableRoles);
        Assert.Equal(all, Row(vm, "t1").AssignableRoles);
    }

    [Fact]
    public async Task Manager_Rows_Only_User_Target_Offers_Demote()
    {
        var list = new[]
        {
            new User { Id = "u1", Role = UserRole.User },
            new User { Id = "t1", Role = UserRole.TempUser },
            new User { Id = "m2", Role = UserRole.Manager },
        };
        var (vm, _) = await MakeVmAsync(UserRole.Manager, "mgrSelf", list);

        Assert.Equal(new[] { UserRole.User, UserRole.TempUser }, Row(vm, "u1").AssignableRoles); // user→temp_user 강등
        Assert.False(Row(vm, "t1").CanChangeRole);   // manager는 temp_user 대상 미노출(승격 불가)
        Assert.False(Row(vm, "m2").CanChangeRole);    // manager는 manager 대상 미노출
    }

    // ── Apply 동작 ──

    [Fact]
    public async Task Apply_User_To_TempUser_Calls_SetRole()
    {
        var list = new[] { new User { Id = "u1", Role = UserRole.User } };
        var (vm, accounts) = await MakeVmAsync(UserRole.Admin, "admin", list);
        var row = Row(vm, "u1");
        row.SelectedRole = UserRole.TempUser;

        await vm.ApplyRoleChangeCommand.ExecuteAsync(row);

        Assert.True(accounts.SetRoleCalled);
        Assert.Equal("u1", accounts.SetRoleId);
        Assert.Equal(UserRole.TempUser, accounts.SetRoleValue);
    }

    [Fact]
    public async Task Apply_No_Change_Is_NoOp()
    {
        var list = new[] { new User { Id = "u1", Role = UserRole.User } };
        var (vm, accounts) = await MakeVmAsync(UserRole.Admin, "admin", list);
        var row = Row(vm, "u1");
        // SelectedRole == 현재 역할(User) → 무변경 no-op.
        await vm.ApplyRoleChangeCommand.ExecuteAsync(row);
        Assert.False(accounts.SetRoleCalled);
    }

    [Fact]
    public async Task Apply_Beyond_Matrix_Blocked_Client_Side()
    {
        // manager가 temp_user를 user로 승격 시도(매트릭스 밖) → 클라 차단, SetRole 미호출.
        var list = new[] { new User { Id = "t1", Role = UserRole.TempUser } };
        var (vm, accounts) = await MakeVmAsync(UserRole.Manager, "mgrSelf", list);
        var row = Row(vm, "t1");
        row.SelectedRole = UserRole.User;   // 승격(manager 불가)

        await vm.ApplyRoleChangeCommand.ExecuteAsync(row);

        Assert.False(accounts.SetRoleCalled);
        Assert.Equal("해당 역할로 변경할 권한이 없습니다.", vm.StatusMessage);
    }

    [Fact]
    public async Task Apply_Server_403_Handled_Gracefully_And_Reloads()
    {
        var list = new[] { new User { Id = "u1", Role = UserRole.User } };
        var (vm, accounts) = await MakeVmAsync(UserRole.Admin, "admin", list);
        accounts.SetRoleThrows = new UnauthorizedAccessException("forbidden");
        var reloadsBefore = accounts.ReloadCount;

        var row = Row(vm, "u1");
        row.SelectedRole = UserRole.Manager;
        await vm.ApplyRoleChangeCommand.ExecuteAsync(row);

        Assert.True(accounts.SetRoleCalled);
        Assert.Equal("역할을 변경할 권한이 없습니다.", vm.StatusMessage);
        Assert.True(accounts.ReloadCount > reloadsBefore);   // 목록 원복(재로드)
    }

    [Fact]
    public async Task Apply_Self_Row_Blocked()
    {
        // 자기 계정은 행 래퍼가 빈 목록이라 UI 미노출이지만, 커맨드 직접 호출 시에도 이중 방어.
        var list = new[] { new User { Id = "admin", Role = UserRole.Admin } };
        var (vm, accounts) = await MakeVmAsync(UserRole.Admin, "admin", list);
        var row = Row(vm, "admin");
        row.SelectedRole = UserRole.Manager;

        await vm.ApplyRoleChangeCommand.ExecuteAsync(row);
        Assert.False(accounts.SetRoleCalled);
    }
}
