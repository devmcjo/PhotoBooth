using MCPhoto.App;
using MCPhoto.App.ViewModels;
using MCPhoto.Core.Accounts;
using MCPhoto.Core.Models;
using MCPhoto.Core.Navigation;
using MCPhoto.Core.Settings;

namespace MCPhoto.Tests;

/// <summary>
/// it14 §6.1: 본인 설정 진입 PIN 설정/변경(AccountView PasswordChange 모드, SSO 계정 전용).
/// 노출 조건(CanChangePin)·최초 설정(현재 PIN 불요)·변경(현재 PIN 필요)·형식/일치 검증·서버 호출을 단위 검증.
/// </summary>
public class AccountViewModelPinTests
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

    /// <summary>SetOwnPinAsync 호출 인자를 기록하는 계정 서비스. 예외 주입으로 실패 경로도 검증.</summary>
    private sealed class RecordingAccountService : IAccountService
    {
        public (string id, string? currentPin, string newPin)? SetOwnPinCall { get; private set; }
        public Exception? SetOwnPinThrows { get; set; }

        public Task SetOwnPinAsync(string id, string? currentPin, string newPin, CancellationToken ct = default)
        {
            if (SetOwnPinThrows is not null) throw SetOwnPinThrows;
            SetOwnPinCall = (id, currentPin, newPin);
            return Task.CompletedTask;
        }

        public Task<User?> LoginAsync(string id, string password, CancellationToken ct = default) => Task.FromResult<User?>(null);
        public Task<bool> VerifyPasswordAsync(string id, string password, CancellationToken ct = default) => Task.FromResult(true);
        public Task<User?> LoginWithGoogleAsync(string code, string codeVerifier, string redirectUri, string? nonce = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<User?> RegisterAsync(string id, string password, string? email, CancellationToken ct = default) => Task.FromResult<User?>(null);
        public Task<User> CreateAsync(string id, string password, UserRole role, string? email, UserRole actingRole, CancellationToken ct = default) => throw new NotSupportedException();
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
        public Task<bool> VerifyPinAsync(string id, string pin, CancellationToken ct = default) => Task.FromResult(true);
        public Task ResetPinAsync(string targetId, string newPin, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class NullTempUserLimitsService : ITempUserLimitsService
    {
        public Task<TempUserLimits> GetLimitsAsync(CancellationToken ct = default) => Task.FromResult(TempUserLimits.Default);
        public Task SetLimitsAsync(TempUserLimits limits, CancellationToken ct = default) => Task.CompletedTask;
    }

    private static AppSettings BackendOn()
    {
        var s = new AppSettings { UseBackend = true, BackendBaseUrl = "https://backend.test/api", BackendApiKey = "key" };
        s.Clamp();
        return s;
    }

    private static (AccountViewModel vm, RecordingAccountService accounts) MakeVm(User? loginUser, bool backend = true)
    {
        var settings = new StubSettingsService(backend ? BackendOn() : new AppSettings { UseBackend = false });
        var session = new SessionContext();
        if (loginUser is not null) session.Login(loginUser);
        var shell = new AppShellViewModel(new IdleWatchdog(), settings, new EmptyServiceProvider(), session);
        var accounts = new RecordingAccountService();
        var vm = new AccountViewModel(shell, accounts, new NullTempUserLimitsService()) { Mode = AccountMode.PasswordChange };
        return (vm, accounts);
    }

    private static User Sso(bool hasPin) =>
        new() { Id = "g", Role = UserRole.User, AuthMethod = AuthMethod.Sso, HasPin = hasPin };

    // ── 노출 조건(CanChangePin) ──

    [Fact]
    public async Task Sso_Backend_Shows_Pin_Section()
    {
        var (vm, _) = MakeVm(Sso(hasPin: true));
        await vm.OnEnterAsync();
        Assert.True(vm.CanChangePin);
        Assert.True(vm.HasPin);
    }

    [Fact]
    public async Task Password_Account_Hides_Pin_Section()
    {
        var (vm, _) = MakeVm(new User { Id = "p", Role = UserRole.User, AuthMethod = AuthMethod.Password });
        await vm.OnEnterAsync();
        Assert.False(vm.CanChangePin);
    }

    [Fact]
    public async Task Sso_But_Legacy_Mode_Hides_Pin_Section()
    {
        // 비백엔드(레거시)엔 PIN 인프라 없음 → SSO여도 미노출.
        var (vm, _) = MakeVm(Sso(hasPin: false), backend: false);
        await vm.OnEnterAsync();
        Assert.False(vm.CanChangePin);
    }

    // ── 최초 설정(HasPin=false): 현재 PIN 불요 ──

    [Fact]
    public async Task Initial_Setup_Sends_Null_CurrentPin()
    {
        var (vm, accounts) = MakeVm(Sso(hasPin: false));
        await vm.OnEnterAsync();
        vm.NewPin = "1234";
        vm.ConfirmPin = "1234";
        await vm.ChangePinCommand.ExecuteAsync(null);

        Assert.NotNull(accounts.SetOwnPinCall);
        Assert.Null(accounts.SetOwnPinCall!.Value.currentPin);   // 최초 설정 → 현재 PIN null
        Assert.Equal("1234", accounts.SetOwnPinCall!.Value.newPin);
        Assert.False(vm.PinMessageIsError);
    }

    [Fact]
    public async Task Initial_Setup_Flips_HasPin_True()
    {
        var user = Sso(hasPin: false);
        var (vm, _) = MakeVm(user);
        await vm.OnEnterAsync();
        vm.NewPin = "1234";
        vm.ConfirmPin = "1234";
        await vm.ChangePinCommand.ExecuteAsync(null);

        Assert.True(user.HasPin);   // 로컬 세션 반영(변경 모드로 전환)
        Assert.True(vm.HasPin);
    }

    // ── 변경(HasPin=true): 현재 PIN 전달 ──

    [Fact]
    public async Task Change_Sends_CurrentPin()
    {
        var (vm, accounts) = MakeVm(Sso(hasPin: true));
        await vm.OnEnterAsync();
        vm.CurrentPin = "1111";
        vm.NewPin = "2222";
        vm.ConfirmPin = "2222";
        await vm.ChangePinCommand.ExecuteAsync(null);

        Assert.NotNull(accounts.SetOwnPinCall);
        Assert.Equal("1111", accounts.SetOwnPinCall!.Value.currentPin);
        Assert.Equal("2222", accounts.SetOwnPinCall!.Value.newPin);
    }

    // ── 형식/일치 검증(서버 왕복 전 차단) ──

    [Fact]
    public async Task Invalid_Format_Blocks_Service()
    {
        var (vm, accounts) = MakeVm(Sso(hasPin: false));
        await vm.OnEnterAsync();
        vm.NewPin = "12a";       // 비숫자/길이 위반
        vm.ConfirmPin = "12a";
        await vm.ChangePinCommand.ExecuteAsync(null);

        Assert.Null(accounts.SetOwnPinCall); // 서비스 미호출
        Assert.True(vm.PinMessageIsError);
    }

    [Fact]
    public async Task Mismatch_Blocks_Service()
    {
        var (vm, accounts) = MakeVm(Sso(hasPin: false));
        await vm.OnEnterAsync();
        vm.NewPin = "1234";
        vm.ConfirmPin = "5678";
        await vm.ChangePinCommand.ExecuteAsync(null);

        Assert.Null(accounts.SetOwnPinCall);
        Assert.True(vm.PinMessageIsError);
    }

    [Fact]
    public async Task Change_Invalid_CurrentPin_Blocks_Service()
    {
        var (vm, accounts) = MakeVm(Sso(hasPin: true));
        await vm.OnEnterAsync();
        vm.CurrentPin = "11";    // 형식 위반
        vm.NewPin = "2222";
        vm.ConfirmPin = "2222";
        await vm.ChangePinCommand.ExecuteAsync(null);

        Assert.Null(accounts.SetOwnPinCall);
        Assert.True(vm.PinMessageIsError);
    }

    // ── 서버 거부(현재 PIN 불일치) 우아 처리 ──

    [Fact]
    public async Task Server_Rejects_WrongCurrent_Shows_Error()
    {
        var (vm, accounts) = MakeVm(Sso(hasPin: true));
        accounts.SetOwnPinThrows = new InvalidOperationException("현재 PIN이 올바르지 않습니다.");
        await vm.OnEnterAsync();
        vm.CurrentPin = "0000";
        vm.NewPin = "2222";
        vm.ConfirmPin = "2222";
        await vm.ChangePinCommand.ExecuteAsync(null);

        Assert.True(vm.PinMessageIsError);
    }
}
