using MCPhoto.App;
using MCPhoto.App.ViewModels;
using MCPhoto.Core.Accounts;
using MCPhoto.Core.Models;
using MCPhoto.Core.Navigation;
using MCPhoto.Core.Settings;

namespace MCPhoto.Tests;

/// <summary>
/// it13 §7.6·§7.7·§9.4: 계정 페이지 TempUser 지원 — 생성 콤보에 TempUser 자동 등장,
/// Admin 전역 한도 로드/저장(Admin·백엔드 전용). 순수 라벨 매핑은 RoleManagementTests.ToLabel_Korean.
/// </summary>
public class AccountViewModelTempUserTests
{
    private sealed class StubSettingsService : ISettingsService
    {
        private readonly AppSettings _settings;
        public StubSettingsService(AppSettings settings) => _settings = settings;
        public AppSettings Current => _settings;
        public AppSettings Load() => _settings;
        public bool Save() => true;
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    /// <summary>최소 계정 서비스(생성 호출만 기록).</summary>
    private sealed class StubAccountService : IAccountService
    {
        public (string id, UserRole role, UserRole acting)? Created { get; private set; }
        public Task<User?> LoginAsync(string id, string password, CancellationToken ct = default) => Task.FromResult<User?>(null);
        public Task<bool> VerifyPasswordAsync(string id, string password, CancellationToken ct = default) => Task.FromResult(true);
        public Task<User?> LoginWithGoogleAsync(string code, string codeVerifier, string redirectUri, string? nonce = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<User?> RegisterAsync(string id, string password, string? email, CancellationToken ct = default) => Task.FromResult<User?>(null);
        public Task<User> CreateAsync(string id, string password, UserRole role, string? email, UserRole actingRole, CancellationToken ct = default)
        {
            Created = (id, role, actingRole);
            return Task.FromResult(new User { Id = id, Role = role });
        }
        public Task ChangePasswordAsync(string id, string newPassword, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<User>> GetAllAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<User>>(Array.Empty<User>());
        public Task DeleteAsync(string id, CancellationToken ct = default) => Task.CompletedTask;
        public Task SetRoleAsync(string id, UserRole role, CancellationToken ct = default) => Task.CompletedTask;
        public Task EnsureSeedAccountAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task SetEmailAsync(string id, string email, CancellationToken ct = default) => Task.CompletedTask;
        public Task RequestPasswordResetAsync(string idOrEmail, CancellationToken ct = default) => Task.CompletedTask;
        public Task ConfirmPasswordResetAsync(string id, string token, string newPassword, CancellationToken ct = default) => Task.CompletedTask;
        public Task ConfirmPasswordResetByCodeAsync(string idOrEmail, string code, string newPassword, CancellationToken ct = default) => Task.CompletedTask;
        public Task RequestEmailVerificationAsync(string idOrEmail, CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> ConfirmEmailVerificationAsync(string id, string code, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> ConfirmEmailVerificationByTokenAsync(string id, string token, CancellationToken ct = default) => Task.FromResult(true);
    }

    /// <summary>전역 한도 서비스 fake — 조회값 반환 + 저장 호출 기록.</summary>
    private sealed class RecordingLimitsService : ITempUserLimitsService
    {
        public TempUserLimits Stored;
        public TempUserLimits? Saved { get; private set; }
        public bool ThrowUnauthorizedOnSet { get; set; }
        public RecordingLimitsService(TempUserLimits initial) => Stored = initial;

        public Task<TempUserLimits> GetLimitsAsync(CancellationToken ct = default) => Task.FromResult(Stored);
        public Task SetLimitsAsync(TempUserLimits limits, CancellationToken ct = default)
        {
            if (ThrowUnauthorizedOnSet) throw new UnauthorizedAccessException("no");
            Saved = limits;
            Stored = limits;
            return Task.CompletedTask;
        }
    }

    private static AppSettings BackendOn()
    {
        var s = new AppSettings { UseBackend = true, BackendBaseUrl = "https://backend.test/api", BackendApiKey = "key" };
        s.Clamp();
        return s;
    }

    private static (AccountViewModel vm, StubAccountService accounts, RecordingLimitsService limits) MakeVm(
        UserRole? loginRole, AccountMode mode, RecordingLimitsService? limits = null, bool backend = true)
    {
        var settings = new StubSettingsService(backend ? BackendOn() : new AppSettings { UseBackend = false });
        var session = new SessionContext();
        if (loginRole is { } r) session.Login(new User { Id = "actor", Role = r });
        var shell = new AppShellViewModel(new IdleWatchdog(), settings, new EmptyServiceProvider(), session);
        var accounts = new StubAccountService();
        limits ??= new RecordingLimitsService(new TempUserLimits(48, 30));
        var vm = new AccountViewModel(shell, accounts, limits) { Mode = mode };
        return (vm, accounts, limits);
    }

    // ── §9.4: 생성 콤보에 TempUser 등장 ──

    [Fact]
    public async Task Admin_Create_Combo_Includes_TempUser_First()
    {
        var (vm, _, _) = MakeVm(UserRole.Admin, AccountMode.AccountCreate);
        await vm.OnEnterAsync();

        Assert.Equal(new[] { UserRole.TempUser, UserRole.User, UserRole.Manager }, vm.CreatableRoles);
        Assert.Equal(UserRole.TempUser, vm.SelectedNewRole);   // 첫 항목이 기본 선택
    }

    [Fact]
    public async Task Manager_Create_Combo_Includes_TempUser()
    {
        var (vm, _, _) = MakeVm(UserRole.Manager, AccountMode.AccountCreate);
        await vm.OnEnterAsync();
        Assert.Equal(new[] { UserRole.TempUser, UserRole.User }, vm.CreatableRoles);
    }

    [Fact]
    public async Task Create_TempUser_Passes_Role_To_Service()
    {
        var (vm, accounts, _) = MakeVm(UserRole.Admin, AccountMode.AccountCreate);
        await vm.OnEnterAsync();
        vm.NewAccountId = "temp1";
        vm.NewAccountPassword = "pw";
        vm.SelectedNewRole = UserRole.TempUser;
        await vm.CreateAccountCommand.ExecuteAsync(null);

        Assert.NotNull(accounts.Created);
        Assert.Equal(UserRole.TempUser, accounts.Created!.Value.role);
        Assert.Equal(UserRole.Admin, accounts.Created!.Value.acting);
    }

    // ── §7.7: Admin 전역 한도 로드/저장 ──

    [Fact]
    public async Task Admin_Enter_Loads_Current_Limits()
    {
        var limits = new RecordingLimitsService(new TempUserLimits(72, 50));
        var (vm, _, _) = MakeVm(UserRole.Admin, AccountMode.Admin, limits);
        await vm.OnEnterAsync();

        Assert.True(vm.CanEditTempUserLimits);
        Assert.Equal(72, vm.TempUserQrHours);
        Assert.Equal(50, vm.TempUserQrCount);
    }

    [Fact]
    public async Task Admin_Save_Limits_Persists()
    {
        var limits = new RecordingLimitsService(new TempUserLimits(48, 30));
        var (vm, _, _) = MakeVm(UserRole.Admin, AccountMode.Admin, limits);
        await vm.OnEnterAsync();

        vm.TempUserQrHours = 24;
        vm.TempUserQrCount = 10;
        await vm.SaveTempUserLimitsCommand.ExecuteAsync(null);

        Assert.NotNull(limits.Saved);
        Assert.Equal(24, limits.Saved!.QrHours);
        Assert.Equal(10, limits.Saved!.QrCount);
        Assert.False(vm.TempUserLimitsMessageIsError);
    }

    [Fact]
    public async Task Admin_Save_Invalid_Range_Rejected_Client_Side()
    {
        var limits = new RecordingLimitsService(new TempUserLimits(48, 30));
        var (vm, _, _) = MakeVm(UserRole.Admin, AccountMode.Admin, limits);
        await vm.OnEnterAsync();

        vm.TempUserQrHours = 0;   // 1 미만 → 클라 거부(서버 왕복 전)
        await vm.SaveTempUserLimitsCommand.ExecuteAsync(null);

        Assert.Null(limits.Saved);
        Assert.True(vm.TempUserLimitsMessageIsError);
    }

    [Fact]
    public async Task NonAdmin_Cannot_Edit_Limits()
    {
        // Manager(power지만 Admin 아님) → 한도 섹션 미노출.
        var (vm, _, _) = MakeVm(UserRole.Manager, AccountMode.Admin);
        await vm.OnEnterAsync();
        Assert.False(vm.CanEditTempUserLimits);
    }

    [Fact]
    public async Task Legacy_Mode_Cannot_Edit_Limits()
    {
        // 백엔드 off(레거시) → Admin이라도 한도 섹션 미노출(강제 인프라 없음).
        var (vm, _, _) = MakeVm(UserRole.Admin, AccountMode.Admin, backend: false);
        await vm.OnEnterAsync();
        Assert.False(vm.CanEditTempUserLimits);
    }
}
