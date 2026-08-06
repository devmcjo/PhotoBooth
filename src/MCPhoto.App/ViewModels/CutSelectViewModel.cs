using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MCPhoto.App.Imaging;
using MCPhoto.Core.Capture;
using MCPhoto.Core.Models;
using MCPhoto.Core.Navigation;
using Microsoft.Extensions.Logging;

namespace MCPhoto.App.ViewModels;

/// <summary>촬영된 컷을 썸네일로 표시하고 정확히 슬롯 수만큼 선택. (BM③, §9 #29)</summary>
public sealed partial class CutSelectViewModel : ViewModelBase
{
    private readonly AppShellViewModel _shell;
    private readonly ILogger<CutSelectViewModel>? _logger;

    public ObservableCollection<CutThumbnail> Cuts { get; } = new();

    [ObservableProperty] private int _slotCount;
    [ObservableProperty] private int _selectedCount;
    [ObservableProperty] private bool _canProceed;

    /// <summary>대표 슬롯 종횡비(가로/세로). 썸네일 컨테이너 비율 = 컷 크롭 비율(WYSIWYG, it5 §3 B7). 기본 3:4.</summary>
    [ObservableProperty] private double _slotAspectRatio = 3.0 / 4.0;

    // ── 배치 프리뷰: 선택한 컷이 어느 슬롯에 어떻게 들어가는지 실시간 표시 ──

    /// <summary>프리뷰 배경 = 프레임 이미지(로컬 파일). 로드 실패 시 null → 슬롯 사각형만 그린다.</summary>
    [ObservableProperty] private ImageSource? _frameImage;

    /// <summary>
    /// 프리뷰 캔버스 좌표계 크기(= 프레임 원본 픽셀). 슬롯 좌표를 그대로 쓰고 표시 축소는 Viewbox가 맡는다
    /// — 좌표 변환 코드를 두지 않는 것이 이 프리뷰의 정합성 근거다.
    /// </summary>
    [ObservableProperty] private double _canvasWidth = 1200;
    [ObservableProperty] private double _canvasHeight = 1600;

    /// <summary>슬롯별 프리뷰 칸(슬롯 수만큼, 순서 = 슬롯 Index 오름차순).</summary>
    public ObservableCollection<SlotPreviewItem> SlotPreviews { get; } = new();

    /// <summary>프리뷰를 그릴 수 있는가(프레임·슬롯 확정). false면 프리뷰 카드 미노출.</summary>
    [ObservableProperty] private bool _hasSlotPreview;

    /// <summary>재촬영 UI 노출 여부(설정 on). off면 "다시 촬영" 버튼 미노출. (it11 #13)</summary>
    public bool RetakeEnabled => _shell.Settings.Current.RetakeEnabled;

    /// <summary>전체 재촬영 가능(설정 on AND 횟수 제한 미도달). "다시 촬영" 버튼 IsEnabled. (it11 #13)</summary>
    public bool CanFullRetake =>
        RetakeEnabled && _shell.Session.Capture.CanFullRetake(_shell.Settings.Current.RetakeLimit);

    public CutSelectViewModel(AppShellViewModel shell, ILogger<CutSelectViewModel>? logger = null)
    {
        _shell = shell;
        _logger = logger;
    }

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

        // 배치 프리뷰(프레임 + 빈 슬롯 칸) 구성 → 선택 상태 반영. 전체 재촬영 재진입 시에도 다시 구성된다.
        BuildSlotPreviews(session.Frame);
        LoadFramePreviewImage(session.Frame);
        UpdateSlotFills();

        // 재촬영 UI 상태는 진입마다 최신 설정·카운터를 반영(계산 속성이라 명시 통지). (it11 #13)
        OnPropertyChanged(nameof(RetakeEnabled));
        OnPropertyChanged(nameof(CanFullRetake));
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
            UpdateSlotFills();   // 배치 프리뷰도 같은 토글로 갱신(선택 해제 시 뒤 컷이 앞 슬롯으로 당겨진다)
        }
    }

    private void UpdateState()
    {
        SelectedCount = _shell.Session.Capture.Selection.Count;
        CanProceed = _shell.Session.Capture.IsSelectionComplete;
    }

    // ── 배치 프리뷰 구성 ──

    /// <summary>프레임의 슬롯 사각형으로 빈 프리뷰 칸을 만든다(이미지는 <see cref="UpdateSlotFills"/>가 채운다).</summary>
    private void BuildSlotPreviews(FrameTemplate? frame)
    {
        SlotPreviews.Clear();
        HasSlotPreview = false;
        if (frame is null || frame.Slots.Count == 0) return;

        var (w, h) = PreviewCanvasSize(frame);
        CanvasWidth = w;
        CanvasHeight = h;

        foreach (var fill in SlotFillPlan.Build(frame.Slots, Array.Empty<int>()))
        {
            // 합성과 동일한 경계 클램프를 거쳐야 프리뷰 좌표가 결과물과 일치한다(SlotPlacement 공유).
            var rect = SlotPlacement.ClampSlotToFrame(fill.Slot, (int)w, (int)h);
            SlotPreviews.Add(new SlotPreviewItem(fill.SlotNumber, rect.X, rect.Y, rect.Width, rect.Height));
        }
        HasSlotPreview = SlotPreviews.Count > 0;
    }

    /// <summary>
    /// 프리뷰 좌표계 크기. 프레임 원본 픽셀이 정답이지만, 기록이 없는 이상 데이터는
    /// 슬롯 bounding box로 대체한다(비율만 유지되면 "어느 슬롯에 들어가는가"는 전달된다).
    /// </summary>
    private static (double w, double h) PreviewCanvasSize(FrameTemplate frame)
    {
        if (frame.ImageSize.Width > 0 && frame.ImageSize.Height > 0)
            return (frame.ImageSize.Width, frame.ImageSize.Height);

        int maxX = frame.Slots.Max(s => s.X + s.Width);
        int maxY = frame.Slots.Max(s => s.Y + s.Height);
        return (Math.Max(1, maxX), Math.Max(1, maxY));
    }

    /// <summary>프레임 배경 이미지 로드(best-effort). 실패해도 슬롯 칸 프리뷰는 그대로 동작한다.</summary>
    private void LoadFramePreviewImage(FrameTemplate? frame)
    {
        FrameImage = null;
        var path = frame?.ImageUrl;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
        try
        {
            FrameImage = StillImageConverter.FromFile(path);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "배치 프리뷰 프레임 이미지 로드 실패: {Path}", path);
        }
    }

    /// <summary>현재 선택을 슬롯 칸에 반영. 규칙은 합성과 공유(<see cref="SlotFillPlan"/>).</summary>
    private void UpdateSlotFills()
    {
        var session = _shell.Session.Capture;
        var frame = session.Frame;
        if (frame is null || SlotPreviews.Count == 0) return;

        var plan = SlotFillPlan.Build(frame.Slots, session.Selection);
        for (int i = 0; i < SlotPreviews.Count && i < plan.Count; i++)
        {
            SlotPreviews[i].Image = plan[i].CutIndex is int ci && ci >= 0 && ci < Cuts.Count
                ? Cuts[ci].Image
                : null;
        }
    }

    [RelayCommand]
    private async Task Next()
    {
        if (!CanProceed) return;
        await _shell.NavigateAsync(AppState.Result);
    }

    /// <summary>재촬영(세션 전체). 횟수 제한 미도달 시에만. CutSelect→Guide(기존 전이 재사용). (it11 #13)</summary>
    [RelayCommand]
    private async Task Retake()
    {
        if (!CanFullRetake) return;               // 방어(버튼 비활성이어도 이중 확인)
        _shell.Session.Capture.BeginFullRetake(); // 컷·선택 폐기 + 카운터 증가
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

/// <summary>
/// 배치 프리뷰의 슬롯 한 칸. 좌표·크기는 <b>프레임 픽셀 좌표계</b>이며 화면 축소는 Viewbox가 맡는다.
/// </summary>
public sealed partial class SlotPreviewItem : ObservableObject
{
    public SlotPreviewItem(int number, double x, double y, double width, double height)
    {
        Number = number;
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    /// <summary>슬롯 순번(1부터). 빈 칸에 표시해 "몇 번째로 고른 컷이 여기 들어간다"를 알린다.</summary>
    public int Number { get; }
    public double X { get; }
    public double Y { get; }
    public double Width { get; }
    public double Height { get; }

    /// <summary>빈 칸 순번 글자 크기. 프레임 픽셀 좌표계이므로 슬롯 크기에 비례해야 축소 후에도 읽힌다.</summary>
    public double NumberFontSize => Math.Max(12, Math.Min(Width, Height) * 0.42);

    /// <summary>이 슬롯에 들어갈 컷 썸네일(미선택이면 null).</summary>
    [ObservableProperty] private ImageSource? _image;

    /// <summary>컷이 배정됐는가(빈 칸 표시 전환용).</summary>
    public bool IsFilled => Image is not null;

    partial void OnImageChanged(ImageSource? value) => OnPropertyChanged(nameof(IsFilled));
}
