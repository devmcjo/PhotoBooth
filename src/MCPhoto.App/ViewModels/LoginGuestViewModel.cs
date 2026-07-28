using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MCPhoto.Core.Accounts;
using Microsoft.Extensions.Logging;

namespace MCPhoto.App.ViewModels;

/// <summary>
/// 로그인 전용 화면. 촬영 게스트 직행(it2 §5)이라 "게스트로 계속" 버튼은 폐지.
/// 상단 바 로그인·프레임 선택의 커스텀 유도로 진입하며, 성공 시 직전 화면으로 복귀. (it2 §3.3)
/// it15 §6.1: 자격증명이 Google SSO 단독으로 축소되어 id/pw·회원가입·비밀번호 찾기가 전부 사라졌다.
/// </summary>
public sealed partial class LoginGuestViewModel : ViewModelBase
{
    private readonly AppShellViewModel _shell;
    private readonly IAccountService _accounts;
    private readonly IGoogleSignInService _googleSignIn;
    private readonly ILogger<LoginGuestViewModel>? _logger;

    [ObservableProperty] private string _errorMessage = string.Empty;
    [ObservableProperty] private bool _isBusy;

    /// <summary>
    /// "Google로 로그인" 버튼 노출 게이트(item1b §7.1). GoogleClientId가 설정돼 있어야 authorize URL을 조립할 수 있다.
    /// 빈 값이면 SSO opt-out으로 버튼을 숨긴다(브라우저 봉쇄 키오스크 배려).
    /// 네트워크 상태로는 숨기지 않는다 — 도달 실패는 로그인 시도 시 인라인 오류로 안내(it15 §6.1).
    /// GoogleClientId는 시작 시 고정되므로 진입 시 1회 평가로 충분.
    /// </summary>
    public bool IsGoogleSignInAvailable =>
        !string.IsNullOrWhiteSpace(_shell.Settings.Current.GoogleClientId);

    public LoginGuestViewModel(AppShellViewModel shell, IAccountService accounts,
        IGoogleSignInService googleSignIn, ILogger<LoginGuestViewModel>? logger = null)
    {
        _shell = shell;
        _accounts = accounts;
        _googleSignIn = googleSignIn;
        _logger = logger;
    }

    /// <summary>
    /// Google SSO 로그인(item1b §7.7). 시스템 브라우저 + loopback으로 authorization code를 받아 백엔드로 전달하고,
    /// 성공 시 계정을 세션에 반영한 뒤 직전 화면으로 복귀한다. IsBusy 재진입 가드.
    /// 자동가입(temp_user)은 서버 책임이라 클라 커맨드는 신규/기존 구분 없이 동일 User를 받는다.
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
                // 계정은 자동 생성/매핑이라 정상 검증 email은 거의 여기 오지 않는다.
                // Google 검증 실패(도메인·미검증 등)를 서버가 401로 일반화한 경우 — 열거 방지(§6.4).
                ErrorMessage = "이 Google 계정으로는 로그인할 수 없습니다. 허용된 계정·도메인인지 확인해 주세요.";
                return;
            }

            _shell.Session.Login(user);       // 단일 소스 로그인 + CurrentUserChanged 통지(상단 바 자동 갱신)
            await _shell.ReturnFromOverlay();  // 직전 화면 복귀
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
}
