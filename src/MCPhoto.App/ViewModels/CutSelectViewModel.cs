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

    /// <summary>대표 슬롯 종횡비(가로/세로). 썸네일 컨테이너 비율 = 컷 크롭 비율(WYSIWYG, it5 §3 B7). 기본 3:4.</summary>
    [ObservableProperty] private double _slotAspectRatio = 3.0 / 4.0;

    public CutSelectViewModel(AppShellViewModel shell) => _shell = shell;

    public override Task OnEnterAsync()
    {
        Cuts.Clear();
        var session = _shell.Session.Capture;
        SlotCount = session.SlotCount;

        // 컷은 이미 대표 슬롯 종횡비로 중앙 크롭됨(VF-4). 썸네일 컨테이너도 같은 비율로 → Uniform 표시 시 왜곡·잘림 0.
        var slots = session.Frame?.Slots;
        if (slots is { Count: > 0 } && slots[0].AspectRatio > 0)
            SlotAspectRatio = slots[0].AspectRatio;

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
