using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MCPhoto.Core.Accounts;
using MCPhoto.Core.Navigation;
using MCPhoto.Core.Upload;
using Microsoft.Extensions.Logging;

namespace MCPhoto.App.ViewModels;

/// <summary>
/// 로그인 전용 화면. 촬영 게스트 직행(it2 §5)으로 "게스트로 계속" 버튼은 폐지.
/// 상단 바 로그인·프레임 선택의 커스텀 유도로 진입하며, 성공 시 직전 화면으로 복귀. (it2 §3.3)
/// </summary>
public sealed partial class LoginGuestViewModel : ViewModelBase
{
    /// <summary>오프라인 시드 계정 id(미초기화 시 인메모리로만 로그인 허용 — AccountService와 동일 규약).</summary>
    private const string OfflineSeedId = "devmcjo";

    private readonly AppShellViewModel _shell;
    private readonly IAccountService _accounts;
    private readonly IFirebaseClient _firebase;
    private readonly IGoogleSignInService _googleSignIn;
    private readonly ILogger<LoginGuestViewModel>? _logger;

    [ObservableProperty] private string _loginId = string.Empty;
    [ObservableProperty] private string _password = string.Empty;
    [ObservableProperty] private string _errorMessage = string.Empty;
    [ObservableProperty] private bool _isBusy;

    /// <summary>
    /// 서버 미연결(서비스 계정 키 없음 → Firebase 미초기화) 여부. 키는 시작 시 결정되고 런타임에 변하지 않으므로
    /// 진입 시 1회 평가로 충분. 배너 표시·로그인 메시지 분기에 사용. (it10 S2-1)
    /// </summary>
    public bool IsServerOffline => !_firebase.IsInitialized;

    /// <summary>
    /// 백엔드 모드 여부(item1a §9.4 게이트). "비밀번호 찾기" 링크는 이메일 인프라가 있는
    /// 백엔드 모드에서만 노출한다(레거시 Firebase 경로엔 재설정 인프라 없음).
    /// </summary>
    public bool IsBackendMode => _shell.Settings.Current.UseBackend;

    /// <summary>
    /// "Google로 로그인" 버튼 노출 게이트(item1b §7.1). 백엔드 모드(UseBackend) AND GoogleClientId 설정됨
    /// (SSO opt-in). 미설정이면 버튼을 숨긴다 — 브라우저 봉쇄 키오스크 배려 + client_id 없이는 authorize URL
    /// 조립 불가. UseBackend·GoogleClientId는 시작 시 고정되므로 진입 시 1회 평가로 충분.
    /// </summary>
    public bool IsGoogleSignInAvailable =>
        _shell.Settings.Current.UseBackend
        && !string.IsNullOrWhiteSpace(_shell.Settings.Current.GoogleClientId);

    public LoginGuestViewModel(AppShellViewModel shell, IAccountService accounts, IFirebaseClient firebase,
        IGoogleSignInService googleSignIn, ILogger<LoginGuestViewModel>? logger = null)
    {
        _shell = shell;
        _accounts = accounts;
        _firebase = firebase;
        _googleSignIn = googleSignIn;
        _logger = logger;
    }

    /// <summary>id/pw 로그인. 성공 시 계정 반영 후 직전 화면 복귀(오버레이).</summary>
    [RelayCommand]
    private async Task Login()
    {
        if (IsBusy) return;
        ErrorMessage = string.Empty;
        IsBusy = true;
        try
        {
            var user = await _accounts.LoginAsync(LoginId.Trim(), Password);
            if (user is null)
            {
                // it10 S2-1: 서버 미연결 상태에서 비시드 계정 로그인 실패는 "아이디/비밀번호 불일치"로 오도하지 않고
                // 실제 원인(오프라인)을 노출. 시드(devmcjo) 오입력은 기존 메시지 유지(계정 자체는 유효하므로).
                ErrorMessage = IsServerOffline && LoginId.Trim() != OfflineSeedId
                    ? "서버 미연결 상태에서는 이 계정으로 로그인할 수 없습니다."
                    : "아이디 또는 비밀번호가 올바르지 않습니다.";
                return;
            }
            _shell.Session.Login(user); // 단일 소스 로그인 + CurrentUserChanged 통지(상단 바 자동 갱신)
            // 상단 바 진입 시 원 화면, 프레임 선택 유도 시 FrameSelect 재진입(커스텀 프레임 로드)으로 복귀
            await _shell.ReturnFromOverlay();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "로그인 실패(네트워크?)");
            ErrorMessage = "로그인할 수 없습니다. 네트워크를 확인해 주세요.";
        }
        finally { IsBusy = false; }
    }

    /// <summary>
    /// Google SSO 로그인(item1b §7.7). 시스템 브라우저 + loopback으로 authorization code를 받아 백엔드로 전달하고,
    /// 성공 시 id/pw 로그인과 동일하게 계정 반영 후 직전 화면으로 복귀한다. IsBusy 재진입 가드.
    /// </summary>
    [RelayCommand]
    private async Task LoginWithGoogle()
    {
        if (IsBusy) return;
        ErrorMessage = string.Empty;
        IsBusy = true;
        try
        {
            var codeResult = await _googleSignIn.AcquireAuthorizationCodeAsync();
            if (codeResult is null)
            {
                // 사용자 취소·타임아웃·state 불일치·인가 거부(서비스가 null로 신호).
                ErrorMessage = "Google 로그인이 취소되었습니다.";
                return;
            }

            var user = await _accounts.LoginWithGoogleAsync(
                codeResult.Code, codeResult.CodeVerifier, codeResult.RedirectUri, codeResult.Nonce);
            if (user is null)
            {
                // 매핑 실패(등록 안 됨/미검증/Google 검증 실패) — 서버 401 일반화(열거 방지, §6.4).
                ErrorMessage = "이 Google 계정으로 로그인할 수 없습니다. 관리자에게 등록을 요청하세요.";
                return;
            }

            _shell.Session.Login(user);       // id/pw 로그인과 동일 경로(단일 소스 + 상단 바 자동 갱신)
            await _shell.ReturnFromOverlay();  // 직전 화면 복귀(동일)
        }
        catch (GoogleSsoNotConfiguredException ex)
        {
            // 서버 SSO 미구성(501) 전용. 자격 문제(null)·네트워크 오류와 구분되는 명확 안내.
            _logger?.LogWarning(ex, "Google 로그인: 서버 SSO 미구성");
            ErrorMessage = "Google 로그인이 구성되지 않았습니다. 관리자에게 문의하세요.";
        }
        catch (Exception ex)
        {
            // 네트워크·기타 오류. 토큰류는 로그에 없음.
            _logger?.LogWarning(ex, "Google 로그인 실패(네트워크?)");
            ErrorMessage = "Google 로그인 중 오류가 발생했습니다. 네트워크를 확인해 주세요.";
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task Cancel() => await _shell.ReturnFromOverlay();

    /// <summary>비밀번호 찾기 화면으로 진입(백엔드 모드 전용, item1a §9.4).</summary>
    [RelayCommand]
    private async Task ForgotPassword() => await _shell.OpenPasswordReset();
}
