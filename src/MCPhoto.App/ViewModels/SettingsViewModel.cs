using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MCPhoto.App.Services;
using MCPhoto.Core.Accounts;
using MCPhoto.Core.Capture;
using MCPhoto.Core.Models;
using MCPhoto.Core.Settings;
using MCPhoto.Core.Upload;
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
    private readonly ICameraService _camera;
    private readonly ICameraTestDialogService _cameraTestDialog;
    private readonly IDiagnosticsDialogService _diagnostics;
    private readonly IFirebaseClient _firebase;
    private readonly ILogger<SettingsViewModel>? _logger;

    private DispatcherTimer? _noticeTimer;

    // ── [앱 설정] 필드 (AppSettings 전 항목, it2 §4.2) ──
    // it17: 컷 수는 "자동"(sentinel 0) 선택이 가능해 규칙 캡션 노출 조건이 함께 갱신되어야 한다.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAutoCutCount))]
    private int _cutCount;
    [ObservableProperty] private int _countdownSec;
    [ObservableProperty] private bool _mirrorMode;
    [ObservableProperty] private bool _flashMode;
    [ObservableProperty] private bool _shutterSound;   // 기능#7
    [ObservableProperty] private bool _retakeEnabled;  // it11 #13: 재촬영 사용(상위 토글)
    [ObservableProperty] private int _retakeLimit;     // it11 #13: 재촬영 횟수 제한(1~3)
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
    // item3 스캐폴드: 외부 장치 placeholder(로그인 전용 UI에 노출, 값은 저장만·실기능 미배선).
    [ObservableProperty] private bool _externalCameraEnabled;
    [ObservableProperty] private bool _photoPrinterEnabled;
    [ObservableProperty] private OutputFormat _outputFormat;
    [ObservableProperty] private DisplayMode _displayMode;
    [ObservableProperty] private string _storageBucket = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSavedNotice))]
    private string _savedNotice = string.Empty;
    [ObservableProperty] private bool _savedNoticeIsError; // 성공=false(민트/성공색), 실패=true(로즈/danger)

    /// <summary>저장 안내 토스트 표시 여부(문구가 있을 때만).</summary>
    public bool HasSavedNotice => !string.IsNullOrEmpty(SavedNotice);

    // ── 보완#1: 권한 게이트 ──
    /// <summary>로그인 여부(게스트=false). QR/Firebase 편집 가능 여부. 설정 진입 중 불변.</summary>
    public bool IsLoggedIn => _shell.IsLoggedIn;
    /// <summary>게스트 여부. QR/Firebase는 소스단에서 off 표시·저장 제외(ini 원값 보존).</summary>
    public bool IsGuest => !_shell.IsLoggedIn;

    // ── it13 §7.3: TempUser QR 한도 게이트(게스트 3지점 패턴 확장, 셸에서 파생 — 설정 진입 중 불변) ──
    /// <summary>로그인 계정이 TempUser인지(역할 판별). 설정 진입 중 불변.</summary>
    public bool IsTempUser => _shell.CurrentUser?.Role == UserRole.TempUser;
    /// <summary>TempUser이고 QR 한도 초과인지(셸 합성). true면 QR 3토글 표시 off·disabled + 사유 문구.</summary>
    public bool IsTempUserBlocked => _shell.IsTempUserQrBlocked;
    /// <summary>QR 토글 편집 가능 여부. 로그인 + TempUser 미초과일 때만(게스트·초과 TempUser는 disabled).</summary>
    public bool CanEditQr => IsLoggedIn && !IsTempUserBlocked;
    /// <summary>TempUser 한도 초과 사유 문구(§0 정확 문구, 시간 우선). 미초과·비TempUser면 빈 문자열.</summary>
    public string QrLimitNotice => _shell.TempUserQrReason switch
    {
        QrGateReason.Time => "무료 사용 시간이 지났습니다. 관리자에게 문의해주세요.",
        QrGateReason.Count => "무료 사용 횟수가 소진되었습니다. 관리자에게 문의해주세요.",
        _ => string.Empty
    };
    /// <summary>한도 초과 노티 표시 여부(문구가 있을 때만 — 초과 TempUser 전용).</summary>
    public bool HasQrLimitNotice => !string.IsNullOrEmpty(QrLimitNotice);

    // ── it9 C1: 카메라 장치(ComboBox) ──
    /// <summary>연결된 카메라 목록(설정 진입 시 백그라운드 열거). 빈 목록이면 ComboBox Disable.</summary>
    public ObservableCollection<CameraDevice> CameraDevices { get; } = new();
    /// <summary>카메라 연결 여부(ComboBox·테스트 버튼 IsEnabled). 빈 목록=false.</summary>
    [ObservableProperty] private bool _hasCamera;
    /// <summary>카메라 열거 진행 중(로딩 표시·재열거 버튼 비활성).</summary>
    [ObservableProperty] private bool _isEnumeratingCameras;

    /// <summary>컷수 옵션(콤보 바인딩). "자동"(sentinel 0) 최상단 + 고정 6/8/10. (it17)</summary>
    public IReadOnlyList<CutCountOption> CutCountOptions { get; } = BuildCutCountOptions();

    /// <summary>자동 모드 선택 여부. 설정 화면의 규칙 캡션 노출 조건(실제 컷 수는 프레임 확정 후에만
    /// 알 수 있어 여기선 숫자를 표시하지 않는다 — 설계 §6.2). (it17)</summary>
    public bool IsAutoCutCount => CutCountPolicy.IsAuto(CutCount);

    private static CutCountOption[] BuildCutCountOptions()
    {
        var list = new List<CutCountOption>(AppSettings.AllowedCutCounts.Length + 1)
        {
            new(CutCountPolicy.AutoCutCount, "자동")
        };
        foreach (var n in AppSettings.AllowedCutCounts)
            list.Add(new CutCountOption(n, $"{n}컷"));
        return list.ToArray();
    }

    /// <summary>카운트다운 옵션.</summary>
    public IReadOnlyList<int> CountdownOptions { get; } = AppSettings.AllowedCountdownSecs;
    /// <summary>재촬영 횟수 제한 옵션(1~3). (it11 #13)</summary>
    public IReadOnlyList<int> RetakeLimitOptions { get; } = AppSettings.AllowedRetakeLimits;
    /// <summary>출력 포맷 옵션.</summary>
    public IReadOnlyList<OutputFormat> OutputFormatOptions { get; } = new[] { OutputFormat.Jpg, OutputFormat.Png };
    /// <summary>표시 모드 옵션(한글 라벨). 값=DisplayMode, 표시=전체화면/창모드. (it9 후속)</summary>
    public IReadOnlyList<DisplayModeOption> DisplayModeOptions { get; } = new[]
    {
        new DisplayModeOption(DisplayMode.Fullscreen, "전체화면"),
        new DisplayModeOption(DisplayMode.Windowed, "창모드"),
    };

    public SettingsViewModel(AppShellViewModel shell, ISettingsService settings,
        ICameraService camera, ICameraTestDialogService cameraTestDialog,
        IDiagnosticsDialogService diagnostics,
        IFirebaseClient firebase, ILogger<SettingsViewModel>? logger = null)
    {
        _shell = shell;
        _settings = settings;
        _camera = camera;
        _cameraTestDialog = cameraTestDialog;
        _diagnostics = diagnostics;
        _firebase = firebase;
        _logger = logger;
    }

    // ── it10 S4-2: 서버 연결 상태(읽기 전용, 표시 전용) ──
    /// <summary>
    /// 백엔드 구성(서버 연결) 여부. 색상 트리거·상태 문구 판단용. 설정 진입 중 불변(base URL은 시작 시 결정).
    /// it15: 레거시 직결 경로 폐기로 "구성됨"은 백엔드 base URL이 설정됐다는 뜻이며 도달 성공을 보장하지 않는다.
    /// </summary>
    public bool IsServerConnected => _firebase.IsInitialized;

    /// <summary>서버 연결 상태 안내 문구. 구성 시 버킷 표기, 미구성 시 백엔드 주소 부재 안내.</summary>
    public string ServerStatusText => _firebase.IsInitialized
        ? $"연결됨 — {_firebase.Bucket}"
        : "미구성 — 백엔드 주소가 설정되지 않았습니다(로그 참조)";

    public override async Task OnEnterAsync()
    {
        LoadSettings();
        await RefreshCamerasAsync();
    }

    /// <summary>연결된 카메라 열거(백그라운드 — UI 블로킹 방지). 저장 인덱스가 없으면 첫 장치로 보정. (it9 §2.1)</summary>
    [RelayCommand]
    private async Task RefreshCamerasAsync()
    {
        if (IsEnumeratingCameras) return;
        IsEnumeratingCameras = true;
        try
        {
            // EnumerateDevices()는 장치 0~7 open/close(수백 ms~초) → Task.Run 백그라운드.
            var devices = await Task.Run(() => _camera.EnumerateDevices());
            CameraDevices.Clear();
            foreach (var d in devices) CameraDevices.Add(d);
            HasCamera = CameraDevices.Count > 0;

            // 저장된 인덱스가 목록에 없으면 첫 장치로 보정(연결분 있을 때만). 없으면 값 유지(재연결 대비).
            if (HasCamera && CameraDevices.All(d => d.Index != CameraDevice))
                CameraDevice = CameraDevices[0].Index;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "카메라 열거 실패");
            HasCamera = false;
        }
        finally { IsEnumeratingCameras = false; }
    }

    /// <summary>선택된 카메라로 실촬영 동일 테스트 모달 열기(저장 없음). (it9 §2.2 C1)</summary>
    [RelayCommand]
    private async Task OpenCameraTest()
    {
        if (!HasCamera) return;
        try { await _cameraTestDialog.ShowAsync(CameraDevice); }
        catch (Exception ex) { _logger?.LogError(ex, "카메라 테스트 모달 오류"); }
    }

    /// <summary>진단·상태 모달 열기(관리자 트러블슈팅). 로그인 상태에서만(게스트 no-op). (it11 §3.14.7)</summary>
    [RelayCommand]
    private async Task OpenDiagnostics()
    {
        if (!IsLoggedIn) return;
        try { await _diagnostics.ShowAsync(); }
        catch (Exception ex) { _logger?.LogError(ex, "진단 다이얼로그 오류"); }
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
            ShutterSound = s.ShutterSound;
            RetakeEnabled = s.RetakeEnabled;
            RetakeLimit = s.RetakeLimit;
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
            // item3 스캐폴드: 외부 장치 placeholder 로드(현재 미지원 — 저장값 표시만).
            ExternalCameraEnabled = s.ExternalCameraEnabled;
            PhotoPrinterEnabled = s.PhotoPrinterEnabled;
            OutputFormat = s.OutputFormat;
            DisplayMode = s.DisplayMode;
            StorageBucket = s.StorageBucket;

            // 게스트: QR/Firebase는 소스단에서 off로 표시(ini는 그대로 — 저장 시 원값 보존). (보완#1)
            if (IsGuest)
            {
                EnableQrDelivery = false;
                SendPhoto = false;
                SendTimelapse = false;
                // it12 R1: 편집 권한 게이트 확대(표시 전용 off, ini 원값은 SaveSettings에서 보존).
                //          RetakeLimit은 int·재촬영 하위(상위 off 시 숨김)라 강제하지 않음(로드값 유지, 무해).
                MirrorMode = false;
                RetakeEnabled = false;
                FilterGrayscale = false;
                FilterBrightness = false;
                FilterBeauty = false;
            }

            // it13 §7.3: TempUser 한도 초과 — QR 3필드만 표시 전용 off(게스트와 별개, 로그인 상태 유지).
            //            ini 원값은 SaveSettings에서 보존(&& !IsTempUserBlocked 가드). QR 외 필드는 User와 동일(편집 가능).
            if (IsTempUserBlocked)
            {
                EnableQrDelivery = false;
                SendPhoto = false;
                SendTimelapse = false;
            }
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
        // it16 §7: 저장 직전 현재 창 기하를 반영 → ini에 실제 위치가 남고, 저장 후 재적용이 점프를 만들지 않는다.
        // ⚠️ 반드시 s.DisplayMode를 갱신하기 **전에** 호출한다(창은 아직 이전 모드로 떠 있다).
        //    순서가 뒤바뀌면 창모드→전체화면 저장 시 직전 창 위치를 잃는다(§8.3 테스트 31이 이 순서를 고정한다).
        _shell.RequestCaptureWindowBounds();

        var s = _settings.Current;
        s.CutCount = CutCount;
        s.CountdownSec = CountdownSec;
        s.FlashMode = FlashMode;
        s.ShutterSound = ShutterSound;
        // 게스트는 게이트 대상 필드를 저장하지 않음(ini 원값 보존 → 관리자 값 클로버 방지). (보완#1, it12 R1)
        // it12 R1: 거울모드·재촬영(횟수 포함)·필터 3종도 QR/Firebase와 동일하게 로그인 전용 편집으로 확대.
        if (!IsGuest) s.MirrorMode = MirrorMode;
        if (!IsGuest)
        {
            s.RetakeEnabled = RetakeEnabled;
            s.RetakeLimit = RetakeLimit;
        }
        // it13 §7.3: QR 3필드는 로그인 + TempUser 미초과일 때만 기록(초과 시 표시 off를 ini에 반영하지 않아
        //            관리자 원값 보존 → 한도 해제 시 원복). 게스트 `!IsGuest`와 동형으로 `&& !IsTempUserBlocked` 추가.
        if (!IsGuest && !IsTempUserBlocked)
        {
            s.EnableQrDelivery = EnableQrDelivery;
            s.SendPhoto = SendPhoto;
            s.SendTimelapse = SendTimelapse;
        }
        if (!IsGuest)
        {
            s.FilterGrayscale = FilterGrayscale;
            s.FilterBrightness = FilterBrightness;
            s.FilterBeauty = FilterBeauty;
        }
        s.SaveLocalCopy = SaveLocalCopy;
        s.RetentionHours = RetentionHours;
        s.LocalSavePath = LocalSavePath;
        if (!IsGuest) s.HostingBaseUrl = HostingBaseUrl;   // Firebase 관련: 게스트 미저장 (보완#1)
        s.CameraDevice = CameraDevice;
        // item3 스캐폴드: 외부 장치 placeholder 저장(로그인 전용 섹션 — 게스트는 미노출·미기록으로 ini 원값 보존).
        // ⚠️ 값은 저장만 하고 실기능에 배선하지 않는다(미지원 골격). 실제 연동은 장비 확정 후.
        if (!IsGuest)
        {
            s.ExternalCameraEnabled = ExternalCameraEnabled;
            s.PhotoPrinterEnabled = PhotoPrinterEnabled;
        }
        s.OutputFormat = OutputFormat;
        s.DisplayMode = DisplayMode;
        if (!IsGuest) s.StorageBucket = StorageBucket;     // Firebase 관련: 게스트 미저장 (보완#1)

        var ok = _settings.Save(); // bool 반환(폴백 체인). 내부에서 Clamp() 호출
        LoadSettings();            // 클램프된 값 반영
        if (ok)
        {
            ShowNotice("저장되었습니다.", isError: false);
            _logger?.LogInformation("AppSettings 저장 성공(설정 페이지)");
            // 표시 모드(전체화면/창모드)를 재시작 없이 즉시 반영. (it9 후속)
            _shell.RequestApplyDisplayMode();
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

/// <summary>표시 모드 콤보 항목(값 + 한글 라벨). ToString=라벨(닫힌 박스 폴백 대비). (it9 후속)</summary>
public sealed record DisplayModeOption(DisplayMode Value, string Label)
{
    public override string ToString() => Label;
}

/// <summary>촬영 컷 수 콤보 항목(값 + 한글 라벨). Value=0은 자동(CutCountPolicy.AutoCutCount).
/// ToString=라벨(닫힌 박스 폴백 대비). (it17)</summary>
public sealed record CutCountOption(int Value, string Label)
{
    public override string ToString() => Label;
}
