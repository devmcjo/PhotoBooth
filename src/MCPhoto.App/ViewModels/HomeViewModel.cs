using CommunityToolkit.Mvvm.Input;
using MCPhoto.Core.Navigation;

namespace MCPhoto.App.ViewModels;

/// <summary>대기/홈 화면. [촬영하기]로 세션 시작. (BM①)</summary>
public sealed partial class HomeViewModel : ViewModelBase
{
    private readonly AppShellViewModel _shell;

    public HomeViewModel(AppShellViewModel shell) => _shell = shell;

    /// <summary>
    /// [촬영하기]: 프레임 선택으로 직행(게스트 자동 진행). 로그인/게스트 선택 화면을 거치지 않음. (it2 §5)
    /// 촬영 데이터만 초기화하고 로그인은 보존(clearUser:false) — 로그인 사용자는 커스텀 프레임 사용. (it3 §2.3)
    /// </summary>
    [RelayCommand]
    private async Task Start()
    {
        _shell.Session.Reset(clearUser: false);
        await _shell.NavigateAsync(AppState.FrameSelect);
    }
}
