using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MCPhoto.Core.Navigation;
using MCPhoto.Core.Settings;

namespace MCPhoto.App.ViewModels;

/// <summary>촬영 안내(타이머·컷수·선택 방식). (BM②)</summary>
public sealed partial class GuideViewModel : ViewModelBase
{
    private readonly AppShellViewModel _shell;

    [ObservableProperty] private int _cutCount;
    [ObservableProperty] private int _countdownSec;
    [ObservableProperty] private int _slotCount;
    [ObservableProperty] private bool _mirrorMode;

    /// <summary>이 세션의 컷 수가 자동 모드로 산출됐는지("(자동)" 배지). 설정이 아니라 세션에서 읽는다
    /// — 세션 시작 시점의 의도가 기준(설계 §3.3). (it17)</summary>
    [ObservableProperty] private bool _isAutoCutCount;

    public GuideViewModel(AppShellViewModel shell) => _shell = shell;

    public override Task OnEnterAsync()
    {
        var s = _shell.Settings.Current;
        CutCount = _shell.Session.Capture.CutCount;
        CountdownSec = s.CountdownSec;
        SlotCount = _shell.Session.Capture.SlotCount;
        IsAutoCutCount = _shell.Session.Capture.IsAutoCutCount;
        MirrorMode = s.MirrorMode;
        return Task.CompletedTask;
    }

    [RelayCommand]
    private async Task StartCapture() => await _shell.NavigateAsync(AppState.Capture);

    [RelayCommand]
    private void Cancel() => _shell.ReturnHome("안내 취소");
}
