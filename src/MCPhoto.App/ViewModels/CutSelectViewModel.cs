using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MCPhoto.App.Imaging;
using MCPhoto.Core.Navigation;

namespace MCPhoto.App.ViewModels;

/// <summary>촬영된 컷을 썸네일로 표시하고 정확히 슬롯 수만큼 선택. (BM③, §9 #29)</summary>
public sealed partial class CutSelectViewModel : ViewModelBase
{
    private readonly AppShellViewModel _shell;

    public ObservableCollection<CutThumbnail> Cuts { get; } = new();

    [ObservableProperty] private int _slotCount;
    [ObservableProperty] private int _selectedCount;
    [ObservableProperty] private bool _canProceed;

    public CutSelectViewModel(AppShellViewModel shell) => _shell = shell;

    public override Task OnEnterAsync()
    {
        Cuts.Clear();
        var session = _shell.Session.Capture;
        SlotCount = session.SlotCount;

        for (int i = 0; i < session.Cuts.Count; i++)
        {
            var thumb = new CutThumbnail(i, StillImageConverter.ToBitmapSource(session.Cuts[i]));
            Cuts.Add(thumb);
        }
        UpdateState();
        return Task.CompletedTask;
    }

    [RelayCommand]
    private void ToggleCut(CutThumbnail? thumb)
    {
        if (thumb is null) return;
        var session = _shell.Session.Capture;
        if (session.ToggleSelection(thumb.Index))
        {
            // 선택 순서 번호 갱신
            var selection = session.Selection;
            for (int i = 0; i < Cuts.Count; i++)
            {
                int order = -1;
                for (int j = 0; j < selection.Count; j++)
                    if (selection[j] == Cuts[i].Index) { order = j; break; }
                Cuts[i].SelectionOrder = order >= 0 ? order + 1 : 0;
            }
            UpdateState();
        }
    }

    private void UpdateState()
    {
        SelectedCount = _shell.Session.Capture.Selection.Count;
        CanProceed = _shell.Session.Capture.IsSelectionComplete;
    }

    [RelayCommand]
    private async Task Next()
    {
        if (!CanProceed) return;
        await _shell.NavigateAsync(AppState.Result);
    }

    /// <summary>재촬영(세션 전체). CutSelect→Guide.</summary>
    [RelayCommand]
    private async Task Retake()
    {
        _shell.Session.Capture.ResetForRetake();
        await _shell.NavigateAsync(AppState.Guide);
    }

    [RelayCommand]
    private void Cancel() => _shell.ReturnHome("컷 선택 취소");
}

/// <summary>컷 썸네일 항목(선택 상태 포함).</summary>
public sealed partial class CutThumbnail : ObservableObject
{
    public int Index { get; }
    public ImageSource Image { get; }

    /// <summary>선택 순서(1부터, 0=미선택).</summary>
    [ObservableProperty] private int _selectionOrder;

    public bool IsSelected => SelectionOrder > 0;

    partial void OnSelectionOrderChanged(int value) => OnPropertyChanged(nameof(IsSelected));

    public CutThumbnail(int index, ImageSource image)
    {
        Index = index;
        Image = image;
    }
}
