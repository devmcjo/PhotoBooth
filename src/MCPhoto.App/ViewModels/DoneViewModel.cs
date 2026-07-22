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
            // 촬영 후 로그인 유지(it5 §4 B8, PRD 원안 갱신). 촬영 데이터는 Reset이 항상 폐기.
            // 로그아웃은 계정 메뉴 수동 또는 유휴 타임아웃(무인 보호)만.
            _shell.ReturnHome("세션 완료", clearUser: false);
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
    private void GoHome() => _shell.ReturnHome("완료 확인", clearUser: false); // 촬영 후 로그인 유지(it5 §4 B8)
}
