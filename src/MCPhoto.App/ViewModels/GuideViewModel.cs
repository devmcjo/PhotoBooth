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

    public GuideViewModel(AppShellViewModel shell) => _shell = shell;

    public override Task OnEnterAsync()
    {
        var s = _shell.Settings.Current;
        CutCount = _shell.Session.Capture.CutCount;
        CountdownSec = s.CountdownSec;
        SlotCount = _shell.Session.Capture.SlotCount;
        MirrorMode = s.MirrorMode;
        return Task.CompletedTask;
    }

    [RelayCommand]
    private async Task StartCapture() => await _shell.NavigateAsync(AppState.Capture);

    [RelayCommand]
    private void Cancel() => _shell.ReturnHome("안내 취소");
}
