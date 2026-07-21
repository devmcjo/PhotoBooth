using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;

namespace MCPhoto.App.ViewModels;

/// <summary>완료/감사 화면. 잠시 후 자동으로 대기(홈) 복귀(키오스크 1회 세션, §F8).</summary>
public sealed partial class DoneViewModel : ViewModelBase
{
    private readonly AppShellViewModel _shell;
    private DispatcherTimer? _autoReturn;

    public DoneViewModel(AppShellViewModel shell) => _shell = shell;

    public override Task OnEnterAsync()
    {
        // 6초 후 자동 홈 복귀(무인 키오스크)
        _autoReturn = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
        _autoReturn.Tick += (_, _) =>
        {
            _autoReturn?.Stop();
            _shell.ReturnHome("세션 완료", clearUser: true); // 다음 손님 위해 로그아웃(it3 §2.3)
        };
        _autoReturn.Start();
        return Task.CompletedTask;
    }

    public override Task OnLeaveAsync()
    {
        _autoReturn?.Stop();
        _autoReturn = null;
        return Task.CompletedTask;
    }

    [RelayCommand]
    private void GoHome() => _shell.ReturnHome("완료 확인", clearUser: true); // 세션 완료 → 다음 손님(it3 §2.3)
}
