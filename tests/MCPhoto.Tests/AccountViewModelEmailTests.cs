using System.IO;
using MCPhoto.App;
using MCPhoto.App.ViewModels;
using MCPhoto.Core.Accounts;
using MCPhoto.Core.Models;
using MCPhoto.Core.Navigation;
using MCPhoto.Core.Settings;

namespace MCPhoto.Tests;

/// <summary>
/// item1a §9.3: 계정 페이지 이메일 인증 섹션 + 백엔드 모드 게이트 단위 검증.
/// - IsBackendMode 게이트: UseBackend 값에 따라 UI 노출/활성이 결정된다.
/// - OnEnterAsync가 세션 User의 Email/EmailVerified를 섹션 상태로 로드한다.
/// - CreateAsync에 email 전달(백엔드 모드에서만), SetEmail/Verify가 세션·상태에 반영된다.
/// </summary>
public class AccountViewModelEmailTests
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

    /// <summary>계정 서비스 호출을 기록하는 fake.</summary>
    private sealed class RecordingAccountService : IAccountService
    {
        public (string id, string pw, UserRole role, string? email, UserRole acting)? Created { get; private set; }
        public (string id, string email)? SetEmailCall { get; private set; }
        public string? VerifiedId { get; private set; }
        public bool VerifyResult { get; set; } = true;
        /// <summary>설정 시 ConfirmEmailVerificationAsync가 이 메시지로 InvalidOperationException(409 taken)을 던진다.</summary>
        public string? VerifyThrowsMessage { get; set; }

        public Task<User?> LoginAsync(string id, string password, CancellationToken ct = default)
            => Task.FromResult<User?>(null);
        public Task<bool> VerifyPasswordAsync(string id, string password, CancellationToken ct = default)
        {
            VerifiedId = id;
            return Task.FromResult(VerifyResult);
        }
        public Task<User?> LoginWithGoogleAsync(string code, string codeVerifier, string redirectUri, string? nonce = null, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<User?> RegisterAsync(string id, string password, string? email, CancellationToken ct = default)
            => Task.FromResult<User?>(null);
        public Task<User> CreateAsync(string id, string password, UserRole role, string? email, UserRole actingRole, CancellationToken ct = default)
        {
            Created = (id, password, role, email, actingRole);
            return Task.FromResult(new User { Id = id, Role = role, Email = email });
        }
        public Task ChangePasswordAsync(string id, string newPassword, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<User>> GetAllAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<User>>(Array.Empty<User>());
        public Task DeleteAsync(string id, CancellationToken ct = default) => Task.CompletedTask;
        public Task SetRoleAsync(string id, UserRole role, CancellationToken ct = default) => Task.CompletedTask;
        public Task EnsureSeedAccountAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task SetEmailAsync(string id, string email, CancellationToken ct = default)
        {
            SetEmailCall = (id, email);
            return Task.CompletedTask;
        }
        public Task RequestPasswordResetAsync(string idOrEmail, CancellationToken ct = default) => Task.CompletedTask;
        public Task ConfirmPasswordResetAsync(string id, string token, string newPassword, CancellationToken ct = default) => Task.CompletedTask;
        public Task ConfirmPasswordResetByCodeAsync(string idOrEmail, string code, string newPassword, CancellationToken ct = default) => Task.CompletedTask;
        public Task RequestEmailVerificationAsync(string idOrEmail, CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> ConfirmEmailVerificationAsync(string id, string code, CancellationToken ct = default)
        {
            VerifiedId = id;
            if (VerifyThrowsMessage is not null)
                throw new InvalidOperationException(VerifyThrowsMessage);
            return Task.FromResult(VerifyResult);
        }
        public Task<bool> ConfirmEmailVerificationByTokenAsync(string id, string token, CancellationToken ct = default) => Task.FromResult(true);
    }

    private static AppSettings BackendSettings(bool useBackend)
    {
        var s = new AppSettings();
        if (useBackend)
        {
            s.UseBackend = true;
            s.BackendBaseUrl = "https://backend.test/api";
            s.BackendApiKey = "key";
        }
        s.Clamp(); // 유효 URL이면 UseBackend 유지, 아니면 off
        return s;
    }

    private static (AccountViewModel vm, RecordingAccountService accounts, SessionContext session) MakeVm(
        bool useBackend, User? loginUser)
    {
        var settings = new StubSettingsService(BackendSettings(useBackend));
        var session = new SessionContext();
        if (loginUser is not null) session.Login(loginUser);
        var shell = new AppShellViewModel(new IdleWatchdog(), settings, new EmptyServiceProvider(), session);
        var accounts = new RecordingAccountService();
        var vm = new AccountViewModel(shell, accounts) { Mode = AccountMode.PasswordChange };
        return (vm, accounts, session);
    }

    [Fact]
    public void IsBackendMode_Reflects_Setting_On()
    {
        var (vm, _, _) = MakeVm(useBackend: true, loginUser: new User { Id = "u1", Role = UserRole.User });
        Assert.True(vm.IsBackendMode);
    }

    [Fact]
    public void IsBackendMode_Reflects_Setting_Off()
    {
        var (vm, _, _) = MakeVm(useBackend: false, loginUser: new User { Id = "u1", Role = UserRole.User });
        Assert.False(vm.IsBackendMode);
    }

    [Fact]
    public async Task OnEnter_Loads_Email_State_From_Session()
    {
        var user = new User { Id = "u1", Role = UserRole.User, Email = "u@x.com", EmailVerified = true };
        var (vm, _, _) = MakeVm(useBackend: true, loginUser: user);

        await vm.OnEnterAsync();

        Assert.Equal("u@x.com", vm.CurrentEmail);
        Assert.True(vm.HasEmail);
        Assert.True(vm.IsEmailVerified);
        Assert.Equal("u@x.com", vm.EmailInput);
    }

    [Fact]
    public async Task OnEnter_No_Email_Sets_HasEmail_False()
    {
        var user = new User { Id = "u1", Role = UserRole.User };
        var (vm, _, _) = MakeVm(useBackend: true, loginUser: user);

        await vm.OnEnterAsync();

        Assert.Null(vm.CurrentEmail);
        Assert.False(vm.HasEmail);
        Assert.False(vm.IsEmailVerified);
    }

    [Fact]
    public async Task CreateAccount_Backend_Passes_Email()
    {
        var admin = new User { Id = "boss", Role = UserRole.Admin };
        var (vm, accounts, _) = MakeVm(useBackend: true, loginUser: admin);
        vm.Mode = AccountMode.AccountCreate;
        await vm.OnEnterAsync();
        vm.NewAccountId = "newuser";
        vm.NewAccountPassword = "pw";
        vm.NewAccountEmail = "new@x.com";
        vm.SelectedNewRole = UserRole.User;

        await vm.CreateAccountCommand.ExecuteAsync(null);

        Assert.NotNull(accounts.Created);
        Assert.Equal("new@x.com", accounts.Created!.Value.email);
    }

    [Fact]
    public async Task CreateAccount_NonBackend_Does_Not_Pass_Email()
    {
        // 백엔드 모드가 아니면 email 필드가 있어도 전달하지 않는다(레거시 경로 무영향).
        var admin = new User { Id = "boss", Role = UserRole.Admin };
        var (vm, accounts, _) = MakeVm(useBackend: false, loginUser: admin);
        vm.Mode = AccountMode.AccountCreate;
        await vm.OnEnterAsync();
        vm.NewAccountId = "newuser";
        vm.NewAccountPassword = "pw";
        vm.NewAccountEmail = "leak@x.com"; // 게이트 밖 값
        vm.SelectedNewRole = UserRole.User;

        await vm.CreateAccountCommand.ExecuteAsync(null);

        Assert.NotNull(accounts.Created);
        Assert.Null(accounts.Created!.Value.email);
    }

    [Fact]
    public async Task RegisterEmail_Calls_Service_And_Resets_Verified()
    {
        var user = new User { Id = "u1", Role = UserRole.User, Email = "old@x.com", EmailVerified = true };
        var (vm, accounts, session) = MakeVm(useBackend: true, loginUser: user);
        await vm.OnEnterAsync();
        vm.EmailInput = "new@x.com";

        await vm.RegisterEmailCommand.ExecuteAsync(null);

        Assert.Equal(("u1", "new@x.com"), accounts.SetEmailCall);
        Assert.Equal("new@x.com", vm.CurrentEmail);
        Assert.False(vm.IsEmailVerified);                 // 변경 시 미인증으로 리셋
        Assert.Equal("new@x.com", session.CurrentUser!.Email); // 세션 반영
        Assert.False(session.CurrentUser!.EmailVerified);
    }

    [Fact]
    public async Task VerifyEmail_Success_Marks_Verified()
    {
        var user = new User { Id = "u1", Role = UserRole.User, Email = "u@x.com", EmailVerified = false };
        var (vm, accounts, session) = MakeVm(useBackend: true, loginUser: user);
        accounts.VerifyResult = true;
        await vm.OnEnterAsync();
        vm.EmailVerifyCode = "123456";

        await vm.VerifyEmailCommand.ExecuteAsync(null);

        Assert.Equal("u1", accounts.VerifiedId);
        Assert.True(vm.IsEmailVerified);
        Assert.True(session.CurrentUser!.EmailVerified);
    }

    [Fact]
    public async Task VerifyEmail_Failure_Shows_Error_And_Stays_Unverified()
    {
        var user = new User { Id = "u1", Role = UserRole.User, Email = "u@x.com", EmailVerified = false };
        var (vm, accounts, _) = MakeVm(useBackend: true, loginUser: user);
        accounts.VerifyResult = false; // 코드 불일치·만료
        await vm.OnEnterAsync();
        vm.EmailVerifyCode = "000000";

        await vm.VerifyEmailCommand.ExecuteAsync(null);

        Assert.False(vm.IsEmailVerified);
        Assert.True(vm.EmailMessageIsError);
    }

    // ── W-4 (§3.3): 인증 코드 5분 카운트다운 시작/정지·누수 방지 ──

    [Fact]
    public async Task RegisterEmail_Success_Starts_Countdown()
    {
        var user = new User { Id = "u1", Role = UserRole.User, Email = "old@x.com", EmailVerified = true };
        var (vm, _, _) = MakeVm(useBackend: true, loginUser: user);
        await vm.OnEnterAsync();
        vm.EmailInput = "new@x.com";

        await vm.RegisterEmailCommand.ExecuteAsync(null);

        Assert.True(vm.IsVerifyCountdownActive);
        // 발송 직후 즉시 mm:ss 표시. 형식 검증: 정확히 mm:ss.
        Assert.Matches(@"^\d{2}:\d{2}$", vm.VerifyCountdownText);
        // 5분 시작 직후는 05:00 또는 04:59(마이크로초 절삭). 남은 시간이 4:50~5:00 범위인지 확인.
        var parts = vm.VerifyCountdownText.Split(':');
        var remaining = TimeSpan.FromMinutes(int.Parse(parts[0])) + TimeSpan.FromSeconds(int.Parse(parts[1]));
        Assert.InRange(remaining, TimeSpan.FromSeconds(290), TimeSpan.FromSeconds(300));
    }

    [Fact]
    public async Task ResendVerification_Success_Starts_Countdown()
    {
        var user = new User { Id = "u1", Role = UserRole.User, Email = "u@x.com", EmailVerified = false };
        var (vm, _, _) = MakeVm(useBackend: true, loginUser: user);
        await vm.OnEnterAsync();

        await vm.ResendEmailVerificationCommand.ExecuteAsync(null);

        Assert.True(vm.IsVerifyCountdownActive);
        Assert.Matches(@"^\d{2}:\d{2}$", vm.VerifyCountdownText);
    }

    [Fact]
    public async Task Countdown_Not_Started_When_NonBackend()
    {
        // 비백엔드 모드는 이메일 기능 자체가 막혀 코드가 나가지 않으므로 카운트다운도 시작 안 함.
        var user = new User { Id = "u1", Role = UserRole.User, Email = "u@x.com", EmailVerified = false };
        var (vm, _, _) = MakeVm(useBackend: false, loginUser: user);
        await vm.OnEnterAsync();

        await vm.ResendEmailVerificationCommand.ExecuteAsync(null);

        Assert.False(vm.IsVerifyCountdownActive);
    }

    [Fact]
    public async Task OnEnter_Reentry_Stops_Countdown_No_Leak()
    {
        // 오버레이 재사용 VM: 재진입 시 기존 카운트다운을 정지·해제해야 한다(G6 누수 방지).
        var user = new User { Id = "u1", Role = UserRole.User, Email = "u@x.com", EmailVerified = false };
        var (vm, _, _) = MakeVm(useBackend: true, loginUser: user);
        await vm.OnEnterAsync();
        await vm.ResendEmailVerificationCommand.ExecuteAsync(null);
        Assert.True(vm.IsVerifyCountdownActive); // 진행 중 확인

        await vm.OnEnterAsync(); // 재진입

        Assert.False(vm.IsVerifyCountdownActive);
        Assert.Equal(string.Empty, vm.VerifyCountdownText);
    }

    [Fact]
    public async Task Close_Overlay_Exit_Stops_Countdown_No_Leak()
    {
        // 오버레이 이탈(닫기→복귀 시 셸이 OnLeaveAsync 호출)에서 카운트다운 정지·해제.
        var user = new User { Id = "u1", Role = UserRole.User, Email = "u@x.com", EmailVerified = false };
        var (vm, _, _) = MakeVm(useBackend: true, loginUser: user);
        await vm.OnEnterAsync();
        await vm.ResendEmailVerificationCommand.ExecuteAsync(null);
        Assert.True(vm.IsVerifyCountdownActive);

        await vm.OnLeaveAsync(); // 셸의 이탈 훅(ReturnFromOverlay 경로)

        Assert.False(vm.IsVerifyCountdownActive);
        Assert.Equal(string.Empty, vm.VerifyCountdownText);
    }

    [Fact]
    public async Task VerifyEmail_Success_Stops_Countdown()
    {
        var user = new User { Id = "u1", Role = UserRole.User, Email = "u@x.com", EmailVerified = false };
        var (vm, accounts, _) = MakeVm(useBackend: true, loginUser: user);
        accounts.VerifyResult = true;
        await vm.OnEnterAsync();
        await vm.ResendEmailVerificationCommand.ExecuteAsync(null);
        Assert.True(vm.IsVerifyCountdownActive);
        vm.EmailVerifyCode = "123456";

        await vm.VerifyEmailCommand.ExecuteAsync(null);

        Assert.True(vm.IsEmailVerified);
        Assert.False(vm.IsVerifyCountdownActive); // 인증 완료 시 카운트다운 정지
    }

    // ── W-4 (§3.4 C4): verify 초과(409, InvalidOperationException) 메시지 표시 ──

    [Fact]
    public async Task VerifyEmail_Taken_409_Shows_Server_Message()
    {
        const string takenMsg = "해당 이메일로 생성 가능한 계정 수를 초과하였습니다.";
        var user = new User { Id = "u1", Role = UserRole.User, Email = "u@x.com", EmailVerified = false };
        var (vm, accounts, _) = MakeVm(useBackend: true, loginUser: user);
        accounts.VerifyThrowsMessage = takenMsg; // 409 taken 전파
        await vm.OnEnterAsync();
        vm.EmailVerifyCode = "123456";

        await vm.VerifyEmailCommand.ExecuteAsync(null);

        Assert.False(vm.IsEmailVerified);      // 인증 안 됨
        Assert.True(vm.EmailMessageIsError);
        Assert.Equal(takenMsg, vm.EmailMessage); // 서버 메시지 그대로 노출
    }
}
