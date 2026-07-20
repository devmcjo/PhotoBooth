using CommunityToolkit.Mvvm.Input;
using MCPhoto.Core.Navigation;

namespace MCPhoto.App.ViewModels;

/// <summary>대기/홈 화면. [촬영하기]로 세션 시작. (BM①)</summary>
public sealed partial class HomeViewModel : ViewModelBase
{
    private readonly AppShellViewModel _shell;

    public HomeViewModel(AppShellViewModel shell) => _shell = shell;

    [RelayCommand]
    private async Task Start()
    {
        _shell.Session.Reset();
        await _shell.NavigateAsync(AppState.Login);
    }
}
