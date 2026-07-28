using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MCPhoto.Core.Accounts;
using MCPhoto.Core.Navigation;
using MCPhoto.Core.Upload;
using Microsoft.Extensions.Logging;

namespace MCPhoto.App.ViewModels;

/// <summary>로그인/회원가입 화면의 모드(§2.4). 탭 전환으로 섹션을 스왑한다.</summary>
public enum AuthMode
{
    /// <summary>id/pw · Google 로그인.</summary>
    SignIn,
    /// <summary>self-signup(비로그인 회원가입).</summary>
    SignUp,
}

/// <summary>
/// 로그인/회원가입 전용 화면. 촬영 게스트 직행(it2 §5)으로 "게스트로 계속" 버튼은 폐지.
/// 상단 바 로그인·프레임 선택의 커스텀 유도로 진입하며, 성공 시 직전 화면으로 복귀. (it2 §3.3)
/// W-3(§2.4): 로그인/회원가입 탭 + Google 강조 + 인라인 검증의 상용 UX.
/// </summary>
public sealed partial class LoginGuestViewModel : ViewModelBase
{
    /// <summary>오프라인 시드 계정 id(미초기화 시 인메모리로만 로그인 허용 — AccountService와 동일 규약).</summary>
    private const string OfflineSeedId = "devmcjo";

    /// <summary>클라 UX 비번 최소 길이(D-B5). 하드 차단은 서버 규칙 준수, 클라는 4자 이상 안내로 UX 개선.</summary>
    private const int MinPasswordLength = 4;

    private readonly AppShellViewModel _shell;
    private readonly IAccountService _accounts;
    private readonly IFirebaseClient _firebase;
    private readonly IGoogleSignInService _googleSignIn;
    private readonly ILogger<LoginGuestViewModel>? _logger;

    [ObservableProperty] private string _loginId = string.Empty;
    [ObservableProperty] private string _password = string.Empty;
    [ObservableProperty] private string _errorMessage = string.Empty;
    [ObservableProperty] private bool _isBusy;

    /// <summary>현재 인증 모드(기본 로그인). 탭 전환으로 SignIn↔SignUp 스왑. (§2.4)</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSignIn))]
    [NotifyPropertyChangedFor(nameof(IsSignUp))]
    private AuthMode _mode = AuthMode.SignIn;

    /// <summary>로그인 섹션 노출 여부(Visibility 바인딩).</summary>
    public bool IsSignIn => Mode == AuthMode.SignIn;

    /// <summary>회원가입 섹션 노출 여부(Visibility 바인딩).</summary>
    public bool IsSignUp => Mode == AuthMode.SignUp;

    // ── self-signup 입력 (§2.4) ──
    // 아이디/이메일은 바인딩. 비밀번호 2개는 PasswordBox라 code-behind에서 전달(바인딩 금지).

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSubmitSignUp))]
    private string _signUpId = string.Empty;

    [ObservableProperty] private string _signUpEmail = string.Empty;

    /// <summary>회원가입 비밀번호(code-behind 전달). 바인딩하지 않는다(PasswordBox 보안).</summary>
    public string SignUpPassword { get; set; } = string.Empty;

    /// <summary>회원가입 비밀번호 확인(code-behind 전달). 바인딩하지 않는다.</summary>
    public string SignUpPasswordConfirm { get; set; } = string.Empty;

    /// <summary>가입 성공 노티(예: 이메일 인증 안내). 오류와 구분해 표시.</summary>
    [ObservableProperty] private string _signUpNotice = string.Empty;

    // ── 인라인 검증 파생 (§2.4) ──
    // code-behind가 비번 변경 시 RefreshSignUpValidation()으로 갱신을 트리거한다.

    /// <summary>두 비번이 비어있지 않고 동일한지(불일치 인라인 경고용).</summary>
    public bool PasswordsMatch =>
        SignUpPassword.Length > 0 && SignUpPassword == SignUpPasswordConfirm;

    /// <summary>회원가입 버튼 활성 조건: id 비어있지 않음 && 비번 4자 이상 && 두 비번 일치.</summary>
    public bool CanSubmitSignUp =>
        !string.IsNullOrWhiteSpace(SignUpId)
        && SignUpPassword.Length >= MinPasswordLength
        && PasswordsMatch;

    /// <summary>비밀번호 규칙 안내 문구(정적 캡션).</summary>
    public string PasswordRuleText => $"비밀번호는 {MinPasswordLength}자 이상이어야 합니다.";

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

    /// <summary>
    /// code-behind가 SignUp PasswordBox 변경 시 호출: 비번 값을 반영한 뒤 인라인 검증 파생을 갱신한다.
    /// (PasswordBox는 바인딩 불가라 값 전달과 알림을 뷰가 트리거)
    /// </summary>
    public void RefreshSignUpValidation()
    {
        OnPropertyChanged(nameof(PasswordsMatch));
        OnPropertyChanged(nameof(CanSubmitSignUp));
    }

    /// <summary>탭 전환(로그인↔회원가입). 모드 오염 방지를 위해 오류·성공·입력을 초기화한다. (§2.4)</summary>
    [RelayCommand]
    private void SwitchMode(object? target)
    {
        var next = target switch
        {
            AuthMode m => m,
            string s when Enum.TryParse<AuthMode>(s, ignoreCase: true, out var m) => m,
            _ => Mode,
        };
        if (next == Mode) return;

        Mode = next;
        // 모드 전환 시 양쪽 섹션의 오류·성공·입력 잔재 제거(오염 방지).
        ErrorMessage = string.Empty;
        SignUpNotice = string.Empty;
        SignUpId = string.Empty;
        SignUpEmail = string.Empty;
        SignUpPassword = string.Empty;
        SignUpPasswordConfirm = string.Empty;
        RefreshSignUpValidation();
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
    /// 이메일/비번 self-signup(§2.2 B-BE-2, W-1). RegisterAsync가 서버에서 user 계정 생성 + 즉시 로그인(JWT)하고
    /// User를 반환하면 세션 로그인 후 직전 화면 복귀. 실패(id 중복 등)는 예외 메시지를 인라인 표시.
    /// </summary>
    [RelayCommand]
    private async Task SignUp()
    {
        if (IsBusy) return;
        if (!CanSubmitSignUp) return; // 버튼 IsEnabled와 이중 가드(Enter 등 우회 방지)

        ErrorMessage = string.Empty;
        SignUpNotice = string.Empty;
        IsBusy = true;
        try
        {
            var email = string.IsNullOrWhiteSpace(SignUpEmail) ? null : SignUpEmail.Trim();
            var user = await _accounts.RegisterAsync(SignUpId.Trim(), SignUpPassword, email);
            if (user is null)
            {
                // 계약상 성공 시 User 반환. null은 예외 상황(서버가 토큰/유저 미반환).
                ErrorMessage = "회원가입에 실패했습니다. 잠시 후 다시 시도해 주세요.";
                return;
            }
            _shell.Session.Login(user); // 가입 즉시 로그인(D-B3) — id/pw 로그인과 동일 경로
            await _shell.ReturnFromOverlay();
        }
        catch (Exception ex)
        {
            // id 중복(409) 등 서버 사유는 그대로 노출(가입 UX 필수, 열거 방지 대상 아님).
            _logger?.LogWarning(ex, "회원가입 실패");
            ErrorMessage = ex.Message;
        }
        finally { IsBusy = false; }
    }

    /// <summary>
    /// Google SSO 로그인(item1b §7.7). 시스템 브라우저 + loopback으로 authorization code를 받아 백엔드로 전달하고,
    /// 성공 시 id/pw 로그인과 동일하게 계정 반영 후 직전 화면으로 복귀한다. IsBusy 재진입 가드.
    /// 자동가입은 서버(BE-2) 책임이라 클라 커맨드는 무변경(신규/기존 구분 없이 동일 User 반환).
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
                // 계정은 자동 생성/승격(BE-2)이라 정상 검증 email은 거의 여기 오지 않는다.
                // Google 검증 실패(도메인·미검증 등)를 서버가 401로 일반화한 경우 — 열거 방지(§6.4).
                ErrorMessage = "이 Google 계정으로는 로그인할 수 없습니다. 허용된 계정·도메인인지 확인해 주세요.";
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
