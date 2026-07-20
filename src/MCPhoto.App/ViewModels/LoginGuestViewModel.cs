using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MCPhoto.Core.Accounts;
using MCPhoto.Core.Navigation;
using Microsoft.Extensions.Logging;

namespace MCPhoto.App.ViewModels;

/// <summary>로그인/게스트 선택. 게스트=기본 프레임만, 로그인=기본+커스텀. (PRD §F8)</summary>
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

    /// <summary>게스트로 진행(기본 프레임만).</summary>
    [RelayCommand]
    private async Task ContinueAsGuest()
    {
        _shell.Session.CurrentUser = null;
        await _shell.NavigateAsync(AppState.FrameSelect);
    }

    /// <summary>id/pw 로그인.</summary>
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
            _shell.Session.CurrentUser = user;
            await _shell.NavigateAsync(AppState.FrameSelect);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "로그인 실패(네트워크?)");
            ErrorMessage = "로그인할 수 없습니다. 네트워크를 확인해 주세요.";
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private void Cancel() => _shell.ReturnHome("로그인 취소");
}
