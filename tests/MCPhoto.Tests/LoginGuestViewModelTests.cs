using System.IO;
using MCPhoto.App;
using MCPhoto.App.ViewModels;
using MCPhoto.Core.Accounts;
using MCPhoto.Core.Models;
using MCPhoto.Core.Navigation;
using MCPhoto.Core.Settings;

namespace MCPhoto.Tests;

/// <summary>
/// item1b Google SSO 게이트·커맨드 분기. it15 §6.1: 자격증명이 Google SSO 단독으로 축소되어
/// id/pw 로그인·회원가입·비밀번호 찾기 케이스와 IsServerOffline 배너가 전부 사라졌다.
/// - 노출 게이트는 GoogleClientId 단독(네트워크 상태로 숨기지 않는다).
/// - 취소·매핑 실패·미구성(501)·네트워크 4분기 오류 문구 유지.
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

        public Task<User?> LoginWithGoogleAsync(string code, string codeVerifier, string redirectUri,
            string? nonce = null, CancellationToken ct = default)
        {
            GoogleCalled = true;
            if (GoogleException is not null) throw GoogleException;
            return Task.FromResult(GoogleResult);
        }

        public Task<IReadOnlyList<User>> GetAllAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<User>>(Array.Empty<User>());
        public Task DeleteAsync(string id, CancellationToken ct = default) => Task.CompletedTask;
        public Task SetRoleAsync(string id, UserRole role, CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> VerifyPinAsync(string id, string pin, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SetOwnPinAsync(string id, string? currentPin, string newPin, CancellationToken ct = default) => Task.CompletedTask;
        public Task ResetPinAsync(string targetId, string newPin, CancellationToken ct = default) => Task.CompletedTask;
    }

    /// <summary>결과를 주입 가능한 가짜 Google SSO 서비스(브라우저·loopback 미실행).</summary>
    private sealed class StubGoogleSignInService : IGoogleSignInService
    {
        public GoogleAuthCodeResult? Result { get; set; }
        public Task<GoogleAuthCodeResult?> AcquireAuthorizationCodeAsync(CancellationToken ct = default)
            => Task.FromResult(Result);
    }

    private static (LoginGuestViewModel vm, StubAccountService accounts, StubGoogleSignInService google, SessionContext session)
        MakeVm(string googleClientId = "cid.apps.googleusercontent.com")
    {
        var iniPath = Path.Combine(Path.GetTempPath(), $"lgvm_{Guid.NewGuid():N}.ini");
        var settings = new IniSettingsService(iniPath: iniPath);
        var loaded = settings.Load();
        loaded.BackendBaseUrl = "https://x.test/api/";
        loaded.GoogleClientId = googleClientId;
        loaded.Clamp();

        var session = new SessionContext();
        var shell = new AppShellViewModel(new IdleWatchdog(), settings, new EmptyServiceProvider(), session);
        var accounts = new StubAccountService();
        var google = new StubGoogleSignInService();
        var vm = new LoginGuestViewModel(shell, accounts, google);
        return (vm, accounts, google, session);
    }

    private static GoogleAuthCodeResult Code() => new()
    {
        Code = "c", CodeVerifier = "v", RedirectUri = "http://127.0.0.1:1/", Nonce = "n"
    };

    // ── it15 §6.1: Google SSO 노출 게이트는 GoogleClientId 단독 ──

    [Fact]
    public void GoogleSignIn_Hidden_When_ClientId_Empty()
    {
        // client_id 미설정 → SSO opt-out(버튼 숨김, 키오스크 배려).
        var (vm, _, _, _) = MakeVm(googleClientId: "");
        Assert.False(vm.IsGoogleSignInAvailable);
    }

    [Fact]
    public void GoogleSignIn_Visible_When_ClientId_Set()
    {
        var (vm, _, _, _) = MakeVm();
        Assert.True(vm.IsGoogleSignInAvailable);
    }

    // ── item1b: Google SSO 커맨드 분기 (§7.7) ──

    [Fact]
    public async Task LoginWithGoogle_Cancel_Shows_Cancelled_Message()
    {
        var (vm, accounts, google, _) = MakeVm();
        google.Result = null; // 사용자 취소·타임아웃(서비스가 null 반환)

        await vm.LoginWithGoogleCommand.ExecuteAsync(null);

        Assert.Equal("Google 로그인이 취소되었습니다.", vm.ErrorMessage);
        Assert.False(accounts.GoogleCalled); // code 없으면 백엔드 호출 안 함
        Assert.False(vm.IsBusy);             // 재진입 가드 해제됨
    }

    [Fact]
    public async Task LoginWithGoogle_Mapping_Failure_Shows_General_Message()
    {
        var (vm, accounts, google, _) = MakeVm();
        google.Result = Code();
        accounts.GoogleResult = null; // 서버 401 → LoginWithGoogleAsync가 null(매핑 실패)

        await vm.LoginWithGoogleCommand.ExecuteAsync(null);

        Assert.True(accounts.GoogleCalled);
        Assert.Equal("이 Google 계정으로는 로그인할 수 없습니다. 허용된 계정·도메인인지 확인해 주세요.", vm.ErrorMessage);
    }

    [Fact]
    public async Task LoginWithGoogle_Not_Configured_501_Shows_Config_Message()
    {
        var (vm, accounts, google, _) = MakeVm();
        google.Result = Code();
        // 서버 SSO 미구성 → HttpAccountService가 전용 예외로 매핑.
        accounts.GoogleException = new GoogleSsoNotConfiguredException("Google 로그인이 구성되지 않았습니다.");

        await vm.LoginWithGoogleCommand.ExecuteAsync(null);

        Assert.Equal("Google 로그인이 구성되지 않았습니다. 관리자에게 문의하세요.", vm.ErrorMessage);
    }

    [Fact]
    public async Task LoginWithGoogle_Network_Error_Shows_Network_Message()
    {
        var (vm, accounts, google, _) = MakeVm();
        google.Result = Code();
        // 네트워크 오류(HttpBackendClient가 던지는 InvalidOperationException) → 미구성과 구분되는 네트워크 안내.
        accounts.GoogleException = new InvalidOperationException("백엔드에 연결할 수 없습니다.");

        await vm.LoginWithGoogleCommand.ExecuteAsync(null);

        Assert.Equal("Google 로그인 중 오류가 발생했습니다. 네트워크를 확인해 주세요.", vm.ErrorMessage);
    }

    [Fact]
    public async Task LoginWithGoogle_Success_Signs_In_Session()
    {
        var (vm, accounts, google, session) = MakeVm();
        google.Result = Code();
        accounts.GoogleResult = new User
        {
            Id = "devmcjo", Role = UserRole.TempUser, AuthMethod = AuthMethod.Google, Email = "devmcjo@gmail.com"
        };

        // 성공 시 ReturnFromOverlay(EmptyServiceProvider→DI 예외)가 뒤따르지만, Session.Login은 그 이전에 반영된다.
        await Record.ExceptionAsync(() => vm.LoginWithGoogleCommand.ExecuteAsync(null));

        Assert.True(accounts.GoogleCalled);
        Assert.NotNull(session.CurrentUser);
        Assert.Equal("devmcjo", session.CurrentUser!.Id);
    }
}
