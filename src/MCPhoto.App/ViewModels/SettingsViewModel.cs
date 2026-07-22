using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MCPhoto.Core.Models;
using MCPhoto.Core.Settings;
using Microsoft.Extensions.Logging;

namespace MCPhoto.App.ViewModels;

/// <summary>
/// 설정 페이지 VM. [앱 설정](AppSettings)만 담당. 계정·관리자 기능은 AccountViewModel로 분리(it5 §5 C1).
/// AppSettings 전 항목 편집(OutputFormat/DisplayMode/StorageBucket 포함) + 저장 신뢰성(it3 §3).
/// </summary>
public sealed partial class SettingsViewModel : ViewModelBase
{
    private readonly AppShellViewModel _shell;
    private readonly ISettingsService _settings;
    private readonly ILogger<SettingsViewModel>? _logger;

    private DispatcherTimer? _noticeTimer;

    // ── [앱 설정] 필드 (AppSettings 전 항목, it2 §4.2) ──
    [ObservableProperty] private int _cutCount;
    [ObservableProperty] private int _countdownSec;
    [ObservableProperty] private bool _mirrorMode;
    [ObservableProperty] private bool _flashMode;
    [ObservableProperty] private bool _enableQrDelivery;
    [ObservableProperty] private bool _sendPhoto;       // QR 하위: 사진 전송 (it7 F2)
    [ObservableProperty] private bool _sendTimelapse;   // QR 하위: 타임랩스 전송 (it7 F2)
    [ObservableProperty] private bool _filterGrayscale; // 필터 노출 (it8 A6)
    [ObservableProperty] private bool _filterBrightness;
    [ObservableProperty] private bool _filterBeauty;
    [ObservableProperty] private bool _saveLocalCopy;
    [ObservableProperty] private int _retentionHours;
    [ObservableProperty] private string _localSavePath = string.Empty;
    [ObservableProperty] private string _hostingBaseUrl = string.Empty;
    [ObservableProperty] private int _cameraDevice;
    [ObservableProperty] private OutputFormat _outputFormat;
    [ObservableProperty] private DisplayMode _displayMode;
    [ObservableProperty] private string _storageBucket = string.Empty;

    [ObservableProperty] private string _savedNotice = string.Empty;
    [ObservableProperty] private bool _savedNoticeIsError; // 성공=false(민트/성공색), 실패=true(로즈/danger)

    /// <summary>컷수 옵션(세그먼트 바인딩).</summary>
    public IReadOnlyList<int> CutCountOptions { get; } = AppSettings.AllowedCutCounts;
    /// <summary>카운트다운 옵션.</summary>
    public IReadOnlyList<int> CountdownOptions { get; } = AppSettings.AllowedCountdownSecs;
    /// <summary>출력 포맷 옵션.</summary>
    public IReadOnlyList<OutputFormat> OutputFormatOptions { get; } = new[] { OutputFormat.Jpg, OutputFormat.Png };
    /// <summary>표시 모드 옵션.</summary>
    public IReadOnlyList<DisplayMode> DisplayModeOptions { get; } = new[] { DisplayMode.Fullscreen, DisplayMode.Windowed };

    public SettingsViewModel(AppShellViewModel shell, ISettingsService settings, ILogger<SettingsViewModel>? logger = null)
    {
        _shell = shell;
        _settings = settings;
        _logger = logger;
    }

    public override Task OnEnterAsync()
    {
        LoadSettings();
        return Task.CompletedTask;
    }

    private void LoadSettings()
    {
        var s = _settings.Current;
        // 로드 중에는 QR 연동 콜백 억제(저장값을 그대로 반영, off→on 강제·정규화 발동 방지).
        _normalizing = true;
        try
        {
            CutCount = s.CutCount;
            CountdownSec = s.CountdownSec;
            MirrorMode = s.MirrorMode;
            FlashMode = s.FlashMode;
            EnableQrDelivery = s.EnableQrDelivery;
            SendPhoto = s.SendPhoto;
            SendTimelapse = s.SendTimelapse;
            FilterGrayscale = s.FilterGrayscale;
            FilterBrightness = s.FilterBrightness;
            FilterBeauty = s.FilterBeauty;
            SaveLocalCopy = s.SaveLocalCopy;
            RetentionHours = s.RetentionHours;
            LocalSavePath = s.LocalSavePath;
            HostingBaseUrl = s.HostingBaseUrl;
            CameraDevice = s.CameraDevice;
            OutputFormat = s.OutputFormat;
            DisplayMode = s.DisplayMode;
            StorageBucket = s.StorageBucket;
        }
        finally { _normalizing = false; }
    }

    // QR 하위 토글 변경 연동(it7 F2): 둘 다 off면 QR 전송 자체 off(단일 정규화 지점).
    partial void OnSendPhotoChanged(bool value) => NormalizeQrToggles();
    partial void OnSendTimelapseChanged(bool value) => NormalizeQrToggles();

    // QR off→on 재활성 시 하위 토글 둘 다 on 강제(it8 A5). LoadSettings 중에는 _normalizing으로 억제.
    partial void OnEnableQrDeliveryChanged(bool oldValue, bool newValue)
    {
        if (_normalizing) return;
        if (!oldValue && newValue) // false→true 전환
        {
            _normalizing = true;
            try
            {
                var (sp, st) = QrDeliveryPolicy.OnReEnabled();
                SendPhoto = sp;
                SendTimelapse = st;
            }
            finally { _normalizing = false; }
        }
    }

    private bool _normalizing;
    private void NormalizeQrToggles()
    {
        if (_normalizing) return; // 재진입 방지(정규화가 프로퍼티를 다시 바꿀 때)
        _normalizing = true;
        try
        {
            var (enableQr, _, _) = QrDeliveryPolicy.Normalize(EnableQrDelivery, SendPhoto, SendTimelapse);
            if (EnableQrDelivery != enableQr) EnableQrDelivery = enableQr; // 둘 다 off → QR off
        }
        finally { _normalizing = false; }
    }

    /// <summary>[앱 설정] 저장: 필드 → AppSettings → Clamp → INI flush. (it2 §4.2, it3 §3)</summary>
    [RelayCommand]
    private void SaveSettings()
    {
        var s = _settings.Current;
        s.CutCount = CutCount;
        s.CountdownSec = CountdownSec;
        s.MirrorMode = MirrorMode;
        s.FlashMode = FlashMode;
        s.EnableQrDelivery = EnableQrDelivery;
        s.SendPhoto = SendPhoto;
        s.SendTimelapse = SendTimelapse;
        s.FilterGrayscale = FilterGrayscale;
        s.FilterBrightness = FilterBrightness;
        s.FilterBeauty = FilterBeauty;
        s.SaveLocalCopy = SaveLocalCopy;
        s.RetentionHours = RetentionHours;
        s.LocalSavePath = LocalSavePath;
        s.HostingBaseUrl = HostingBaseUrl;
        s.CameraDevice = CameraDevice;
        s.OutputFormat = OutputFormat;
        s.DisplayMode = DisplayMode;
        s.StorageBucket = StorageBucket;

        var ok = _settings.Save(); // bool 반환(폴백 체인). 내부에서 Clamp() 호출
        LoadSettings();            // 클램프된 값 반영
        if (ok)
        {
            ShowNotice("저장되었습니다.", isError: false);
            _logger?.LogInformation("AppSettings 저장 성공(설정 페이지)");
        }
        else
        {
            // 성공 오인 금지(it3 §3): 쓰기 실패 시 오류 토스트
            ShowNotice("저장 위치에 쓸 수 없습니다. 관리자에게 문의하세요.", isError: true);
            _logger?.LogWarning("AppSettings 저장 실패(모든 경로 쓰기 불가)");
        }
    }

    /// <summary>[닫기]: 오버레이 복귀(직전 화면). 세션 보존.</summary>
    [RelayCommand]
    private async Task Close() => await _shell.ReturnFromOverlay();

    private void ShowNotice(string text, bool isError = false)
    {
        SavedNotice = text;
        SavedNoticeIsError = isError;
        _noticeTimer?.Stop();
        // 오류는 사용자가 읽을 시간을 더 준다(성공 3초, 실패 6초)
        _noticeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(isError ? 6 : 3) };
        _noticeTimer.Tick += (_, _) =>
        {
            _noticeTimer?.Stop();
            SavedNotice = string.Empty;
        };
        _noticeTimer.Start();
    }
}
