using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MCPhoto.App.Imaging;
using MCPhoto.Core.Frames;
using MCPhoto.Core.Models;
using MCPhoto.Core.Navigation;
using Microsoft.Extensions.Logging;

namespace MCPhoto.App.ViewModels;

/// <summary>
/// 프레임 편집기. 이미지 업로드 → 슬롯 개수 지정(자동 배치) → 드래그 조절 → 저장. (PRD §F2)
/// 편집 범위=슬롯 배치만(텍스트/스티커/배경 제외). 경계 클램프·겹침 방지·10개 제한.
/// </summary>
public sealed partial class FrameEditorViewModel : ViewModelBase
{
    private readonly AppShellViewModel _shell;
    private readonly IFrameRepository _repository;
    private readonly ILogger<FrameEditorViewModel>? _logger;

    private byte[]? _imageBytes;

    [ObservableProperty] private ImageSource? _frameImage;
    [ObservableProperty] private int _frameWidth;
    [ObservableProperty] private int _frameHeight;
    [ObservableProperty] private int _slotCount = 4;
    [ObservableProperty] private string _frameName = "새 프레임";
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _canSave;

    /// <summary>슬롯 종횡비(편집기 전역, MVP). 변경 시 재배치. (it4 §3)</summary>
    [ObservableProperty] private SlotAspect _slotAspect = SlotAspect.Ratio3x4;

    /// <summary>종횡비 선택 옵션(4:3 / 3:4 / 1:1).</summary>
    public IReadOnlyList<SlotAspect> AspectOptions { get; } =
        new[] { SlotAspect.Ratio4x3, SlotAspect.Ratio3x4, SlotAspect.Ratio1x1 };

    /// <summary>슬롯 개수 옵션(1~6). 값 기반 바인딩(SelectedValue)으로 초기화 clobber 차단. (it7 B9)</summary>
    public IReadOnlyList<int> SlotCountOptions { get; } = new[] { 1, 2, 3, 4, 5, 6 };

    /// <summary>슬롯 크기 일괄 스케일(%, 70~130). 기본 100. (it5 §8 F1)</summary>
    [ObservableProperty] private double _slotScalePercent = 100;

    /// <summary>스케일 기준(100% 원본) 슬롯. _baseSlots에서 매번 스케일해 누적 오차 방지. (it5 §8)</summary>
    private readonly List<Slot> _baseSlots = new();

    /// <summary>편집 중 슬롯(드래그 대상).</summary>
    public ObservableCollection<Slot> Slots { get; } = new();

    public FrameEditorViewModel(AppShellViewModel shell, IFrameRepository repository, ILogger<FrameEditorViewModel>? logger = null)
    {
        _shell = shell;
        _repository = repository;
        _logger = logger;
    }

    /// <summary>이미지 파일 로드(업로드). 제한 검사 후 프레임 크기·미리보기 설정.</summary>
    public bool LoadImage(string path)
    {
        if (!FrameImageValidator.IsSupportedExtension(path))
        {
            StatusMessage = "PNG/JPG/JPEG만 지원합니다.";
            return false;
        }
        var info = new FileInfo(path);
        if (!FrameImageValidator.IsSizeWithinLimit(info.Length))
        {
            StatusMessage = "이미지가 10MB를 초과합니다.";
            return false;
        }

        try
        {
            using var mat = OpenCvSharp.Cv2.ImRead(path, OpenCvSharp.ImreadModes.Color);
            if (mat.Empty()) { StatusMessage = "이미지를 읽을 수 없습니다."; return false; }

            var (w, h) = FrameImageValidator.ScaledSize(mat.Width, mat.Height);
            // 장변 4000 초과 시 축소
            using var resized = new OpenCvSharp.Mat();
            if (w != mat.Width || h != mat.Height)
                OpenCvSharp.Cv2.Resize(mat, resized, new OpenCvSharp.Size(w, h));
            else
                mat.CopyTo(resized);

            OpenCvSharp.Cv2.ImEncode(".png", resized, out var buf);
            _imageBytes = buf;

            FrameWidth = w;
            FrameHeight = h;
            FrameImage = StillImageConverter.FromPngBytes(_imageBytes);

            ArrangeSlots();
            StatusMessage = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "이미지 로드 실패: {Path}", path);
            StatusMessage = "이미지 로드 실패";
            return false;
        }
    }

    /// <summary>슬롯 개수 변경 → 자동 배치.</summary>
    partial void OnSlotCountChanged(int value) => ArrangeSlots();

    /// <summary>종횡비 변경 → 선택 비율로 재배치. (it4 §3)</summary>
    partial void OnSlotAspectChanged(SlotAspect value) => ArrangeSlots();

    /// <summary>슬롯 크기 슬라이더(%) 변경 → _baseSlots 기준 일괄 스케일. 70~130 클램프. (it5 §8 F1)</summary>
    partial void OnSlotScalePercentChanged(double value)
    {
        var clamped = Math.Clamp(value, 70, 130);
        if (Math.Abs(clamped - value) > 0.0001)
        {
            SlotScalePercent = clamped; // 재진입(클램프 값으로) — 아래 ApplyScale는 다음 호출에서 수행
            return;
        }
        ApplyScale();
    }

    private void ArrangeSlots()
    {
        if (FrameWidth <= 0 || FrameHeight <= 0) return;
        // 자동 배치 원본을 스케일 기준으로 보관, 현재 배율을 재적용(개수·종횡비 변경 시).
        _baseSlots.Clear();
        _baseSlots.AddRange(SlotLayout.AutoArrange(SlotCount, FrameWidth, FrameHeight, SlotAspect.ToRatio()));
        ApplyScale();
    }

    /// <summary>_baseSlots에서 현재 배율로 스케일해 Slots 갱신(누적 오차 없음).</summary>
    private void ApplyScale()
    {
        if (_baseSlots.Count == 0) return;
        Slots.Clear();
        foreach (var s in SlotLayout.ScaleSlots(_baseSlots, SlotScalePercent / 100.0, FrameWidth, FrameHeight))
            Slots.Add(s);
        UpdateCanSave();
    }

    /// <summary>드래그 후 슬롯 위치·크기 반영(경계 클램프). 스케일 기준(_baseSlots) 위치도 중심 맞춰 갱신.</summary>
    public void UpdateSlot(int index, int x, int y, int width, int height)
    {
        if (index < 0 || index >= Slots.Count) return;
        var clamped = SlotLayout.ClampToFrame(
            new Slot { Index = index, X = x, Y = y, Width = width, Height = height },
            FrameWidth, FrameHeight);
        Slots[index] = clamped;

        // 드래그로 옮긴 중심을 _baseSlots에도 반영(원본 크기 유지) → 이후 스케일 기준 위치 일치.
        if (index < _baseSlots.Count)
        {
            var b = _baseSlots[index];
            double cx = clamped.X + clamped.Width / 2.0;
            double cy = clamped.Y + clamped.Height / 2.0;
            _baseSlots[index] = SlotLayout.ClampToFrame(
                new Slot
                {
                    Index = b.Index,
                    X = (int)Math.Round(cx - b.Width / 2.0),
                    Y = (int)Math.Round(cy - b.Height / 2.0),
                    Width = b.Width,
                    Height = b.Height
                }, FrameWidth, FrameHeight);
        }
        UpdateCanSave();
    }

    private void UpdateCanSave()
        => CanSave = _imageBytes is not null && SlotLayout.IsValid(Slots, FrameWidth, FrameHeight);

    [RelayCommand]
    private async Task Save()
    {
        var user = _shell.Session.CurrentUser;
        if (user is null) { StatusMessage = "로그인이 필요합니다."; return; }
        if (_imageBytes is null || !SlotLayout.IsValid(Slots, FrameWidth, FrameHeight))
        {
            StatusMessage = "슬롯이 겹치거나 프레임을 벗어났습니다.";
            return;
        }

        try
        {
            StatusMessage = "저장 중...";
            var frame = new FrameTemplate
            {
                UserId = user.Id,
                IsDefault = false,
                Name = FrameName,
                ImageSize = new ImageSize { Width = FrameWidth, Height = FrameHeight },
                Slots = Slots.ToList()
            };
            await _repository.SaveAsync(frame, _imageBytes);
            await _shell.NavigateAsync(AppState.FrameSelect);
        }
        catch (InvalidOperationException ex)
        {
            // 10개 초과 등
            StatusMessage = ex.Message;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "프레임 저장 실패");
            StatusMessage = "저장에 실패했습니다.";
        }
    }

    [RelayCommand]
    private async Task Cancel() => await _shell.NavigateAsync(AppState.FrameSelect);
}
