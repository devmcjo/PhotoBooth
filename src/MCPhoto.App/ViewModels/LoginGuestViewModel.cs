using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MCPhoto.Core.Accounts;
using MCPhoto.Core.Navigation;
using Microsoft.Extensions.Logging;

namespace MCPhoto.App.ViewModels;

/// <summary>
/// 로그인 전용 화면. 촬영 게스트 직행(it2 §5)으로 "게스트로 계속" 버튼은 폐지.
/// 상단 바 로그인·프레임 선택의 커스텀 유도로 진입하며, 성공 시 직전 화면으로 복귀. (it2 §3.3)
/// </summary>
public sealed partial class LoginGuestViewModel : ViewModelBase
{
    private readonly AppShellViewModel _shell;
    private readonly IAccountService _accounts;
    private readonly ILogger<LoginGuestViewModel>? _logger;

    [ObservableProperty] private string _loginId = string.Empty;
    [ObservableProperty] private string _password = string.Empty;
    [ObservableProperty] private string _errorMessage = string.Empty;
    [ObservableProperty] private bool _isBusy;

    public LoginGuestViewModel(AppShellViewModel shell, IAccountService accounts, ILogger<LoginGuestViewModel>? logger = null)
    {
        _shell = shell;
        _accounts = accounts;
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
                ErrorMessage = "아이디 또는 비밀번호가 올바르지 않습니다.";
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

    [RelayCommand]
    private async Task Cancel() => await _shell.ReturnFromOverlay();
}
