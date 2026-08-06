using CommunityToolkit.Mvvm.Input;
using MCPhoto.Core.Models;      // UserRole.CanWriteFrames 확장 메서드
using MCPhoto.Core.Navigation;

namespace MCPhoto.App.ViewModels;

/// <summary>대기/홈 화면. [촬영하기]로 세션 시작. (BM①)</summary>
public sealed partial class HomeViewModel : ViewModelBase
{
    private readonly AppShellViewModel _shell;

    public HomeViewModel(AppShellViewModel shell) => _shell = shell;

    /// <summary>
    /// 게스트 여부(진입 시점 스냅샷). (it21 §7.4)
    /// 통지가 없어도 되는 이유: 홈 진입마다 이 VM이 새로 생성되므로(AppShellViewModel.CreateViewModel)
    /// 로그인 후 복귀하면 새 인스턴스가 최신 상태를 읽는다. 셸 구독은 해제 책임만 늘린다.
    /// </summary>
    public bool IsGuest => _shell.IsGuest;

    /// <summary>
    /// 흐름 안내 1번 칸("프레임 선택")의 보조 문구. 빈 문자열이면 화면에서 숨긴다. (it21 §7.4)
    ///
    /// ⚠️ 문구는 **현재 권한에서 반드시 참**이어야 한다. 프레임 만들기는 CanWriteFrames
    /// (AdvancedUser·Manager·Admin)만 가능하고 게스트는 물론 일반 user·temp_user도 못 만든다 —
    /// "직접 만들 수도 있어요"를 무조건 노출하면 대다수에게 거짓이 되고, 프레임 선택 화면에서
    /// [프레임 만들기]를 찾지 못한 손님에게 실망만 남긴다.
    /// 로그인했는데 쓰기 권한이 없는 경우는 **할 말이 없으므로 아무 말도 하지 않는다**.
    /// </summary>
    public string FrameStepHint =>
        _shell.Session.CurrentUser?.Role.CanWriteFrames() == true ? "직접 만들 수도 있어요"
        : IsGuest ? "로그인하면 내 프레임을 쓸 수 있어요"
        : string.Empty;

    /// <summary>보조 문구 노출 여부(바인딩 편의).</summary>
    public bool HasFrameStepHint => FrameStepHint.Length > 0;

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
