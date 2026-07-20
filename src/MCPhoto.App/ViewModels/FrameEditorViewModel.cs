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

    private void ArrangeSlots()
    {
        if (FrameWidth <= 0 || FrameHeight <= 0) return;
        Slots.Clear();
        foreach (var s in SlotLayout.AutoArrange(SlotCount, FrameWidth, FrameHeight))
            Slots.Add(s);
        UpdateCanSave();
    }

    /// <summary>드래그 후 슬롯 위치·크기 반영(경계 클램프).</summary>
    public void UpdateSlot(int index, int x, int y, int width, int height)
    {
        if (index < 0 || index >= Slots.Count) return;
        var clamped = SlotLayout.ClampToFrame(
            new Slot { Index = index, X = x, Y = y, Width = width, Height = height },
            FrameWidth, FrameHeight);
        Slots[index] = clamped;
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
