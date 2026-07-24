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
/// it10 S2-1: 서버 미연결(Firebase 미초기화) 시 로그인 UX + item1b: Google SSO 게이트·커맨드 분기.
/// - 비시드 계정 로그인 실패는 "아이디/비밀번호 불일치"가 아니라 오프라인 메시지로 분기.
/// - 초기화된 상태(온라인)에서는 기존 메시지 유지.
/// - Google SSO: 노출 게이트(UseBackend×GoogleClientId)·취소·매핑 실패·미구성 안내 분기.
/// 성공 경로는 shell 오버레이 복귀 부수효과가 있어 여기선 실패/취소 분기만 단위 검증.
/// </summary>
public class LoginGuestViewModelTests
{
    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    /// <summary>로그인 결과를 주입 가능한 계정 서비스(SSO 분기 검증용). null=실패, 예외 주입 가능.</summary>
    private sealed class StubAccountService : IAccountService
    {
        /// <summary>LoginWithGoogleAsync가 반환할 값(null=매핑 실패). GoogleException이 있으면 그것을 던진다.</summary>
        public User? GoogleResult { get; set; }
        public Exception? GoogleException { get; set; }
        public bool GoogleCalled { get; private set; }

        public Task<User?> LoginAsync(string id, string password, CancellationToken ct = default)
            => Task.FromResult<User?>(null);

        public Task<User?> LoginWithGoogleAsync(string code, string codeVerifier, string redirectUri,
            string? nonce = null, CancellationToken ct = default)
        {
            GoogleCalled = true;
            if (GoogleException is not null) throw GoogleException;
            return Task.FromResult(GoogleResult);
        }

        public Task<User> CreateAsync(string id, string password, UserRole role, string? email, UserRole actingRole, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task ChangePasswordAsync(string id, string newPassword, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<User>> GetAllAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<User>>(Array.Empty<User>());
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

    /// <summary>결과를 주입 가능한 가짜 Google SSO 서비스(브라우저·loopback 미실행).</summary>
    private sealed class StubGoogleSignInService : IGoogleSignInService
    {
        public GoogleAuthCodeResult? Result { get; set; }
        public Task<GoogleAuthCodeResult?> AcquireAuthorizationCodeAsync(CancellationToken ct = default)
            => Task.FromResult(Result);
    }

    private static (LoginGuestViewModel vm, StubAccountService accounts, StubGoogleSignInService google)
        MakeVm(bool serverInitialized, bool useBackend = false, string googleClientId = "")
    {
        var iniPath = Path.Combine(Path.GetTempPath(), $"lgvm_{Guid.NewGuid():N}.ini");
        var settings = new IniSettingsService(iniPath: iniPath);
        var loaded = settings.Load();
        loaded.UseBackend = useBackend;
        loaded.BackendBaseUrl = useBackend ? "https://x.test/api/" : string.Empty;
        loaded.GoogleClientId = googleClientId;
        loaded.Clamp(); // UseBackend 불변식(빈 URL이면 off) 적용 — 게이트 검증이 실제 규칙을 반영하도록.

        var session = new SessionContext();
        var shell = new AppShellViewModel(new IdleWatchdog(), settings, new EmptyServiceProvider(), session);
        var firebase = new FakeFirebaseClient { IsInitialized = serverInitialized };
        var accounts = new StubAccountService();
        var google = new StubGoogleSignInService();
        var vm = new LoginGuestViewModel(shell, accounts, firebase, google);
        return (vm, accounts, google);
    }

    [Fact]
    public void Offline_Exposes_IsServerOffline_True()
    {
        var (vm, _, _) = MakeVm(serverInitialized: false);
        Assert.True(vm.IsServerOffline);
    }

    [Fact]
    public void Online_Exposes_IsServerOffline_False()
    {
        var (vm, _, _) = MakeVm(serverInitialized: true);
        Assert.False(vm.IsServerOffline);
    }

    [Fact]
    public async Task Offline_NonSeed_Login_Shows_Offline_Message()
    {
        var (vm, _, _) = MakeVm(serverInitialized: false);
        vm.LoginId = "manager";     // 비시드 계정
        vm.Password = "whatever";

        await vm.LoginCommand.ExecuteAsync(null);

        Assert.Equal("서버 미연결 상태에서는 이 계정으로 로그인할 수 없습니다.", vm.ErrorMessage);
    }

    [Fact]
    public async Task Offline_Seed_Wrong_Password_Keeps_Credential_Message()
    {
        // 시드(devmcjo) 계정은 오프라인이라도 유효 → 실패는 자격증명 문제로 안내(오프라인 메시지 금지).
        var (vm, _, _) = MakeVm(serverInitialized: false);
        vm.LoginId = "devmcjo";
        vm.Password = "wrong";

        await vm.LoginCommand.ExecuteAsync(null);

        Assert.Equal("아이디 또는 비밀번호가 올바르지 않습니다.", vm.ErrorMessage);
    }

    [Fact]
    public async Task Online_Login_Failure_Keeps_Credential_Message()
    {
        // 온라인(초기화됨)에서 실패는 기존 자격증명 메시지 유지(오프라인 분기 발동 금지).
        var (vm, _, _) = MakeVm(serverInitialized: true);
        vm.LoginId = "manager";
        vm.Password = "whatever";

        await vm.LoginCommand.ExecuteAsync(null);

        Assert.Equal("아이디 또는 비밀번호가 올바르지 않습니다.", vm.ErrorMessage);
    }

    // ── item1b: Google SSO 노출 게이트 (§7.1) ──

    [Fact]
    public void GoogleSignIn_Hidden_When_Not_Backend_Mode()
    {
        // 레거시 Firebase 모드: client_id가 있어도 게이트 off(백엔드에 /auth/google 없음).
        var (vm, _, _) = MakeVm(serverInitialized: true, useBackend: false, googleClientId: "cid");
        Assert.False(vm.IsGoogleSignInAvailable);
    }

    [Fact]
    public void GoogleSignIn_Hidden_When_ClientId_Empty()
    {
        // 백엔드 모드지만 client_id 미설정 → SSO opt-out(버튼 숨김, 키오스크 배려).
        var (vm, _, _) = MakeVm(serverInitialized: true, useBackend: true, googleClientId: "");
        Assert.False(vm.IsGoogleSignInAvailable);
    }

    [Fact]
    public void GoogleSignIn_Visible_When_Backend_And_ClientId_Set()
    {
        var (vm, _, _) = MakeVm(serverInitialized: true, useBackend: true, googleClientId: "cid.apps.googleusercontent.com");
        Assert.True(vm.IsGoogleSignInAvailable);
    }

    // ── item1b: Google SSO 커맨드 분기 (§7.7) ──

    [Fact]
    public async Task LoginWithGoogle_Cancel_Shows_Cancelled_Message()
    {
        var (vm, accounts, google) = MakeVm(serverInitialized: true, useBackend: true, googleClientId: "cid");
        google.Result = null; // 사용자 취소·타임아웃(서비스가 null 반환)

        await vm.LoginWithGoogleCommand.ExecuteAsync(null);

        Assert.Equal("Google 로그인이 취소되었습니다.", vm.ErrorMessage);
        Assert.False(accounts.GoogleCalled); // code 없으면 백엔드 호출 안 함
        Assert.False(vm.IsBusy);             // 재진입 가드 해제됨
    }

    [Fact]
    public async Task LoginWithGoogle_Mapping_Failure_Shows_General_Message()
    {
        var (vm, accounts, google) = MakeVm(serverInitialized: true, useBackend: true, googleClientId: "cid");
        google.Result = new GoogleAuthCodeResult
        {
            Code = "c", CodeVerifier = "v", RedirectUri = "http://127.0.0.1:1/", Nonce = "n"
        };
        accounts.GoogleResult = null; // 서버 401 → LoginWithGoogleAsync가 null(매핑 실패)

        await vm.LoginWithGoogleCommand.ExecuteAsync(null);

        Assert.True(accounts.GoogleCalled);
        Assert.Equal("이 Google 계정으로 로그인할 수 없습니다. 관리자에게 등록을 요청하세요.", vm.ErrorMessage);
    }

    [Fact]
    public async Task LoginWithGoogle_Not_Configured_501_Shows_Config_Message()
    {
        var (vm, accounts, google) = MakeVm(serverInitialized: true, useBackend: true, googleClientId: "cid");
        google.Result = new GoogleAuthCodeResult
        {
            Code = "c", CodeVerifier = "v", RedirectUri = "http://127.0.0.1:1/", Nonce = "n"
        };
        // 서버 SSO 미구성 → HttpAccountService가 전용 예외로 매핑.
        accounts.GoogleException = new GoogleSsoNotConfiguredException("Google 로그인이 구성되지 않았습니다.");

        await vm.LoginWithGoogleCommand.ExecuteAsync(null);

        Assert.Equal("Google 로그인이 구성되지 않았습니다. 관리자에게 문의하세요.", vm.ErrorMessage);
    }

    [Fact]
    public async Task LoginWithGoogle_Network_Error_Shows_Network_Message()
    {
        var (vm, accounts, google) = MakeVm(serverInitialized: true, useBackend: true, googleClientId: "cid");
        google.Result = new GoogleAuthCodeResult
        {
            Code = "c", CodeVerifier = "v", RedirectUri = "http://127.0.0.1:1/", Nonce = "n"
        };
        // 네트워크 오류(HttpBackendClient가 던지는 InvalidOperationException) → 미구성과 구분되는 네트워크 안내.
        accounts.GoogleException = new InvalidOperationException("백엔드에 연결할 수 없습니다.");

        await vm.LoginWithGoogleCommand.ExecuteAsync(null);

        Assert.Equal("Google 로그인 중 오류가 발생했습니다. 네트워크를 확인해 주세요.", vm.ErrorMessage);
    }
}
