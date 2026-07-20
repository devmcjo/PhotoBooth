using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MCPhoto.App.Services;
using MCPhoto.Core.Models;
using MCPhoto.Core.Navigation;

namespace MCPhoto.App.ViewModels;

/// <summary>프레임 선택. 게스트=기본만, 로그인=기본+커스텀. 촬영 전 선택·이후 고정. (PRD §F2, §9 #28)</summary>
public sealed partial class FrameSelectViewModel : ViewModelBase
{
    private readonly AppShellViewModel _shell;
    private readonly FrameCatalogService _catalog;

    public ObservableCollection<FrameTemplate> Frames { get; } = new();

    [ObservableProperty] private FrameTemplate? _selectedFrame;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isLoggedIn;

    public FrameSelectViewModel(AppShellViewModel shell, FrameCatalogService catalog)
    {
        _shell = shell;
        _catalog = catalog;
    }

    public override async Task OnEnterAsync()
    {
        IsLoading = true;
        Frames.Clear();
        try
        {
            var user = _shell.Session.CurrentUser;
            IsLoggedIn = user is not null;

            foreach (var f in await _catalog.GetDefaultFramesAsync())
                Frames.Add(f);

            if (user is not null)
                foreach (var f in await _catalog.GetUserFramesAsync(user.Id))
                    Frames.Add(f);

            SelectedFrame = Frames.FirstOrDefault();
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    private async Task Next()
    {
        if (SelectedFrame is null) return;
        _shell.Session.SelectedFrame = SelectedFrame;
        _shell.Session.Capture.Begin(SelectedFrame, _shell.Settings.Current.CutCount);
        await _shell.NavigateAsync(AppState.Guide);
    }

    /// <summary>프레임 편집기 진입(로그인 사용자만).</summary>
    [RelayCommand]
    private async Task CreateFrame()
    {
        if (!IsLoggedIn) return;
        await _shell.NavigateAsync(AppState.FrameEditor);
    }

    [RelayCommand]
    private void Cancel() => _shell.ReturnHome("프레임 선택 취소");
}
