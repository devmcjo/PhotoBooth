using CommunityToolkit.Mvvm.Input;
using MCPhoto.Core.Navigation;

namespace MCPhoto.App.ViewModels;

/// <summary>대기/홈 화면. [촬영하기]로 세션 시작. (BM①)</summary>
public sealed partial class HomeViewModel : ViewModelBase
{
    private readonly AppShellViewModel _shell;

    public HomeViewModel(AppShellViewModel shell) => _shell = shell;

    /// <summary>
    /// 게스트 여부(진입 시점 스냅샷). 게스트에게만 "로그인하고 내 프레임 쓰기" 진입점을 노출한다. (it21 §7.4)
    /// 통지가 없어도 되는 이유: 홈 진입마다 이 VM이 새로 생성되므로(AppShellViewModel.CreateViewModel)
    /// 로그인 후 복귀하면 새 인스턴스가 최신 상태를 읽는다. 셸 구독은 해제 책임만 늘린다.
    /// </summary>
    public bool IsGuest => _shell.IsGuest;

    /// <summary>
    /// [로그인하고 내 프레임 쓰기]: 로그인 페이지로(오버레이 진입 → 복귀 지점 보존).
    /// 상단 바 계정 버튼이 아이콘 전용이 되면서 잃은 발견성을 Home에서 회복한다 —
    /// 터치 화면에는 hover 툴팁이 없다(NN/g, 설계 §4.3).
    /// </summary>
    [RelayCommand]
    private Task Login() => _shell.NavigateToOverlayAsync(AppState.Login);

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
