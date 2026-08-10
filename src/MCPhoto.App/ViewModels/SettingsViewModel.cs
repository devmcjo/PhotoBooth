using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MCPhoto.App.Services;
using MCPhoto.Core.Accounts;
using MCPhoto.Core.Capture;
using MCPhoto.Core.Devices;
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
    /// <summary>
    /// 외부 카메라(DSLR). 이 화면은 <b>연결을 시도하지 않는다</b> — 노출 도메인 캐시만 조회한다(§9.1).
    /// 설정은 열람 빈도가 높아, 진입만으로 USB 장치를 건드리는 부수효과를 만들지 않는다.
    /// </summary>
    private readonly IExternalCamera _external;
    /// <summary>
    /// 오픈소스 라이선스 고지 열거·읽기(it23 C부). 미주입(null)이면 뷰어가 안내 문구로 축퇴한다 —
    /// 기존 테스트 호출부를 그대로 두기 위해 생성자 마지막 선택 파라미터로 받는다.
    /// </summary>
    private readonly ILicenseNoticeService? _licenseNotice;
    private readonly ILogger<SettingsViewModel>? _logger;

    private DispatcherTimer? _noticeTimer;

    // ── [앱 설정] 필드 (AppSettings 전 항목, it2 §4.2) ──
    // it17: 컷 수는 "자동"(sentinel 0) 선택이 가능하다. 실제 컷 수는 프레임 확정 후 산출된다.
    [ObservableProperty] private int _cutCount;
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
    // it23: 외부 카메라는 실배선(촬영 세션이 이 값을 읽는다). 프린터는 여전히 placeholder(범위 밖).
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasExposureDomain), nameof(CanOpenCameraTest))]
    private bool _externalCameraEnabled;
    [ObservableProperty] private string _externalCameraModel = ExternalCameraModels.Default.Id;
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

    // ── it23 §8.3: 외부 장치 편집 게이트(3지점 패턴 — 기존 게스트 게이트와 같은 메커니즘) ──

    /// <summary>
    /// 외부 카메라 설정 편집 권한. 로그인 + User 이상(TempUser 제외). 설정 진입 중 불변이라 INPC 불요.
    /// <para>
    /// ⚠️ 이것은 <b>편집</b> 게이트다. 촬영 세션이 DSLR을 쓰는지는 ini의 <c>ExternalCameraEnabled</c>가
    /// 결정하며 게스트(손님) 세션에도 적용된다 — 손님이 장비 구성을 바꿀 수는 없지만 그 장비로 찍히는 것은
    /// 당연하다는 키오스크 모델 그대로다(설계 §8.2).
    /// </para>
    /// </summary>
    public bool CanEditExternalCamera
        => IsLoggedIn && (_shell.CurrentUser?.Role.CanConfigureExternalCamera() ?? false);

    /// <summary>연동 가능 모델 목록(콤보 바인딩). 모델 추가는 Core 레지스트리 표 한 줄이다(§3.3).</summary>
    public IReadOnlyList<ExternalCameraModel> ExternalCameraModelOptions { get; } = ExternalCameraModels.All;

    /// <summary>노출 3요소 편집 행(슬라이더 + 직접 입력). 순서 = 화면 표시 순서.</summary>
    public IReadOnlyList<ExposureParameterViewModel> ExposureParameters { get; }

    private readonly ExposureParameterViewModel _shutterSpeed = new(ExposureParameter.ShutterSpeed, "셔터 속도");
    private readonly ExposureParameterViewModel _aperture = new(ExposureParameter.Aperture, "조리개");
    private readonly ExposureParameterViewModel _iso = new(ExposureParameter.Iso, "ISO");

    /// <summary>
    /// 노출 도메인(카메라가 준 이산 목록)을 하나라도 확보했는지. false면 슬라이더가 비활성이고
    /// "노출 목록은 카메라 연결 시 확인됩니다"(W3) 캡션이 뜬다(§10.3).
    /// </summary>
    public bool HasExposureDomain
        => ExternalCameraEnabled && ExposureParameters.Any(p => p.IsDomainAvailable);

    // ── it9 C1: 카메라 장치(ComboBox) ──
    /// <summary>연결된 카메라 목록(설정 진입 시 백그라운드 열거). 빈 목록이면 ComboBox Disable.</summary>
    public ObservableCollection<CameraDevice> CameraDevices { get; } = new();
    /// <summary>카메라 연결 여부(ComboBox IsEnabled). 빈 목록=false.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanOpenCameraTest))]
    private bool _hasCamera;

    /// <summary>
    /// 테스트 모달을 열 수 있는지. 웹캠이 있거나 외부 카메라가 켜져 있으면 확인할 대상이 있다(it23 §9.3).
    /// 종전에는 <see cref="HasCamera"/> 단독이라, 웹캠 없이 DSLR만 붙인 부스에서는 셔터 테스트조차 못 했다.
    /// </summary>
    public bool CanOpenCameraTest => HasCamera || ExternalCameraEnabled;
    /// <summary>카메라 열거 진행 중(로딩 표시·재열거 버튼 비활성).</summary>
    [ObservableProperty] private bool _isEnumeratingCameras;

    /// <summary>컷수 옵션(콤보 바인딩). "자동"(sentinel 0) 최상단 + 고정 6/8/10. (it17)</summary>
    public IReadOnlyList<CutCountOption> CutCountOptions { get; } = BuildCutCountOptions();

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
        IFirebaseClient firebase, IExternalCamera external,
        ILogger<SettingsViewModel>? logger = null,
        ILicenseNoticeService? licenseNotice = null)
    {
        _shell = shell;
        _settings = settings;
        _camera = camera;
        _cameraTestDialog = cameraTestDialog;
        _diagnostics = diagnostics;
        _firebase = firebase;
        _external = external;
        _logger = logger;
        _licenseNotice = licenseNotice;

        ExposureParameters = new[] { _shutterSpeed, _aperture, _iso };
        foreach (var p in ExposureParameters)
            p.DomainAvailabilityChanged += OnExposureDomainAvailabilityChanged;
    }

    /// <summary>
    /// 도메인 확보 여부가 바뀌면 W3 캡션 표시가 달라진다 → 파생 속성 알림 연쇄.
    /// 구독 해제는 <see cref="OnLeaveAsync"/>가 담당한다(VM은 Transient — 진입마다 새 인스턴스).
    /// </summary>
    private void OnExposureDomainAvailabilityChanged(object? sender, EventArgs e)
        => OnPropertyChanged(nameof(HasExposureDomain));

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
        await RefreshExposureDomainAsync();
        await RefreshCamerasAsync();
    }

    public override Task OnLeaveAsync()
    {
        // 구독 해제(생성자 구독의 대칭). VM은 Transient지만 해제 경로 없는 구독은 규칙 위반이다.
        foreach (var p in ExposureParameters)
            p.DomainAvailabilityChanged -= OnExposureDomainAvailabilityChanged;
        return Task.CompletedTask;
    }

    /// <summary>
    /// 노출 도메인 갱신. 외부 카메라가 <b>이미 연결돼 있을 때만</b> 값이 온다(어댑터의 세션 캐시 조회 —
    /// 파일 I/O·SDK 왕복 없음). 미연결이면 도메인이 비어 슬라이더가 잠기고 W3 캡션이 뜬다(§10.3).
    /// </summary>
    private async Task RefreshExposureDomainAsync()
    {
        ExposureDomain? domain = null;
        if (ExternalCameraEnabled)
        {
            try { domain = await _external.GetExposureDomainAsync(); }
            catch (Exception ex) { _logger?.LogWarning(ex, "노출 도메인 조회 실패(슬라이더 비활성으로 진행)"); }
        }

        var s = _settings.Current;
        _shutterSpeed.SetDomain(domain?.ShutterSpeed, s.ExternalShutterSpeed);
        _aperture.SetDomain(domain?.Aperture, s.ExternalAperture);
        _iso.SetDomain(domain?.Iso, s.ExternalIso);
        OnPropertyChanged(nameof(HasExposureDomain));
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

    /// <summary>
    /// 실촬영 동일 테스트 모달 열기(저장 없음). (it9 §2.2 C1)
    /// <para>
    /// it23: 초기 선택만 넘긴다 — 모달 안에서 웹캠↔외부 카메라를 오갈 수 있다(§9.3). 그래서
    /// 웹캠이 0대여도 외부 카메라 설정이 켜져 있으면 모달을 열 수 있어야 한다(외부 항목이 목록에 있다).
    /// </para>
    /// </summary>
    [RelayCommand]
    private async Task OpenCameraTest()
    {
        if (!HasCamera && !ExternalCameraEnabled) return;
        var target = HasCamera
            ? CameraTestTarget.Webcam(CameraDevice, CameraDevices.FirstOrDefault(d => d.Index == CameraDevice)?.Name)
            : CameraTestTarget.External(ExternalCameraModels.Resolve(ExternalCameraModel));
        try { await _cameraTestDialog.ShowAsync(target); }
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
            // it23 §8.3-1: 외부 카메라는 편집 불가 세션(TempUser)에서도 **강제 off 하지 않는다** —
            //   섹션이 읽기 전용으로 보이므로 ini 원값을 그대로 표시해야 운영 상태가 정직하게 드러난다.
            //   (게스트는 섹션 자체가 Collapsed라 표시 문제가 없다.)
            ExternalCameraEnabled = s.ExternalCameraEnabled;
            ExternalCameraModel = s.ExternalCameraModel;
            ApplySavedExposureText(s);
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

    /// <summary>저장된 노출 문자열을 3행에 반영(도메인은 건드리지 않는다 — 저장 직후 재로드용).</summary>
    private void ApplySavedExposureText(AppSettings s)
    {
        _shutterSpeed.SetSavedText(s.ExternalShutterSpeed);
        _aperture.SetSavedText(s.ExternalAperture);
        _iso.SetSavedText(s.ExternalIso);
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
        // it23 §8.3-2: 외부 카메라 4필드는 **편집 권한이 있을 때만** 기록한다.
        // ⚠️ TempUser·게스트 세션이 이 값을 기록하면 관리자가 맞춰 둔 장비 구성·노출이 클로버된다
        //    (읽기 전용으로 보여 준 값을 저장해 버리는 형태). 미기록 = ini 원값 보존.
        if (CanEditExternalCamera)
        {
            s.ExternalCameraEnabled = ExternalCameraEnabled;
            s.ExternalCameraModel = ExternalCameraModel;
            s.ExternalShutterSpeed = _shutterSpeed.Text;
            s.ExternalAperture = _aperture.Text;
            s.ExternalIso = _iso.Text;
        }
        // 프린터는 여전히 placeholder(범위 밖) — 기존 게스트 게이트를 그대로 유지한다.
        if (!IsGuest)
            s.PhotoPrinterEnabled = PhotoPrinterEnabled;
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

    // ══════════════════════════════════════════════════════════════════════════════════
    // [license-viewer:begin] it23 C부 — 오픈소스 라이선스 전문 열람 (설계 §C5·§C7.2)
    //
    // ⚠️ 이 구역은 **계정·역할·테스트 모드를 읽지 않는다**(수락 기준 AC-C2, 정적 검사 C-T14b가 고정).
    //    고지 접근은 로그인 여부와 무관해야 하며(GPLv3 §4 — 게스트도 전문을 볼 수 있어야 한다),
    //    뷰어가 세션을 전혀 읽지 않으면 "어떤 로그인 상태에서 못 보이나"라는 질문이 구조적으로 성립하지 않는다.
    //    도달성은 전부 상위 진입 게이트(설정 화면 진입)가 결정한다.
    // ⚠️ UI 어디에도 폴더 경로를 노출하지 않는다(요구: "경로를 적어주지 말고") — 경로는 로그에만 남는다.
    // ══════════════════════════════════════════════════════════════════════════════════

    /// <summary>고지 폴더 자체가 없을 때(F1). 배포 산출물 누락이므로 감추지 않고 알린다.</summary>
    public const string LicenseFolderMissingMessage =
        "라이선스 고지 폴더를 찾을 수 없습니다. 배포 산출물에 licenses 폴더가 누락된 상태이므로 개발자에게 알려주세요.";

    /// <summary>폴더는 있으나 고지 파일이 0건일 때(F2).</summary>
    public const string LicenseFilesMissingMessage =
        "라이선스 고지 파일을 찾을 수 없습니다. 배포 산출물이 불완전하므로 개발자에게 알려주세요.";

    /// <summary>열거 자체가 불가능할 때(F6 — 서비스 미주입·경로 권한).</summary>
    public const string LicenseUnavailableMessage =
        "라이선스 고지를 불러올 수 없습니다. 개발자에게 알려주세요.";

    /// <summary>오버레이 표시 여부. [오픈소스 라이선스] 버튼으로만 열리고 [닫기]로만 닫힌다(자동 닫힘 없음).</summary>
    [ObservableProperty] private bool _isLicenseViewerOpen;

    /// <summary>고지 문서 목록(열 때마다 재열거 — 파일 교체·삭제를 반영한다. 비용은 디렉터리 열거 1회).</summary>
    public ObservableCollection<LicenseDocument> LicenseDocuments { get; } = new();

    /// <summary>선택된 문서. 변경되면 본문을 다시 읽는다(캐시하지 않는다 — 파일 교체 반영 + 메모리 상주 회피).</summary>
    [ObservableProperty] private LicenseDocument? _selectedLicenseDocument;

    /// <summary>본문 전문. 개행(CRLF)·탭을 변환하지 않은 원문이다("그대로 노출" 요구).</summary>
    [ObservableProperty] private string _licenseText = string.Empty;

    /// <summary>실패 안내(§C6). 빈 문자열이면 미노출.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLicenseError))]
    private string _licenseErrorMessage = string.Empty;

    /// <summary>실패 안내 노출 여부(문구가 있을 때만).</summary>
    public bool HasLicenseError => !string.IsNullOrEmpty(LicenseErrorMessage);

    /// <summary>본문 읽는 중(`불러오는 중…` 표시). 파일이 느린 저장소에 있을 수 있다.</summary>
    [ObservableProperty] private bool _isLicenseLoading;

    /// <summary>하단 요약 <c>{파일명} · {크기}</c> — 전문 여부를 사용자가 가늠할 수 있게 한다.</summary>
    [ObservableProperty] private string _licenseSelectionSummary = string.Empty;

    /// <summary>
    /// 진행 중인 본문 읽기(테스트가 결정적으로 대기하는 이음새 — <c>UserMgmtViewModel.FrameCountLoadTask</c> 선례).
    /// </summary>
    public Task? LicenseLoadTask { get; private set; }

    /// <summary>
    /// 뷰어 열기: 재열거 → 첫 항목(색인) 자동 선택 → 본문 표시. 열자마자 빈 화면을 보여주지 않는다.
    /// 실패는 예외가 아니라 안내 문구로 끝난다 — 여기서 예외가 새면 설정 화면이 통째로 닫힌다(홈 복귀 사고).
    /// </summary>
    [RelayCommand]
    private async Task OpenLicenseViewer()
    {
        IsLicenseViewerOpen = true;
        LicenseDocuments.Clear();
        LicenseText = string.Empty;
        LicenseSelectionSummary = string.Empty;
        LicenseErrorMessage = string.Empty;
        SelectedLicenseDocument = null;

        IReadOnlyList<LicenseDocument> docs;
        if (_licenseNotice is null)
        {
            LicenseErrorMessage = LicenseUnavailableMessage;
            return;
        }

        try { docs = _licenseNotice.ListDocuments(); }
        catch (Exception ex)
        {
            // 서비스 계약상 예외를 던지지 않지만, 다른 구현이 던져도 화면은 열려 있어야 한다.
            _logger?.LogWarning(ex, "라이선스 고지 열거 실패");
            LicenseErrorMessage = LicenseUnavailableMessage;
            return;
        }

        if (docs.Count == 0)
        {
            // 폴더 부재(F1)와 파일 0건(F2)을 구분한다 — 조치가 다르고, 뭉개면 배포 사고의 형태를 알 수 없다.
            LicenseErrorMessage = _licenseNotice.Exists ? LicenseFilesMissingMessage : LicenseFolderMissingMessage;
            return;
        }

        foreach (var doc in docs) LicenseDocuments.Add(doc);
        SelectedLicenseDocument = LicenseDocuments[0];   // 변경 콜백이 본문 로드를 시작한다
        if (LicenseLoadTask is { } load) await load;     // 첫 로드는 기다린다(열자마자 본문이 보여야 한다)
    }

    /// <summary>뷰어 닫기: 본문(최대 수십 KB)과 목록을 놓아준다. 오버레이가 닫히면 설정 편집이 다시 가능해진다.</summary>
    [RelayCommand]
    private void CloseLicenseViewer()
    {
        IsLicenseViewerOpen = false;
        SelectedLicenseDocument = null;   // 진행 중 로드 결과는 stale 판정으로 버려진다
        LicenseDocuments.Clear();
        LicenseText = string.Empty;
        LicenseSelectionSummary = string.Empty;
        LicenseErrorMessage = string.Empty;
        IsLicenseLoading = false;
    }

    /// <summary>목록 선택 변경 → 본문 재로드(선택마다 파일을 다시 읽는다).</summary>
    partial void OnSelectedLicenseDocumentChanged(LicenseDocument? value)
        => LicenseLoadTask = LoadLicenseTextAsync(value);

    /// <summary>
    /// 본문 읽기. <c>Task.Run</c>으로 오프로드하는 이유: 파일이 네트워크 드라이브·느린 디스크에 있을 수 있고
    /// UI 스레드 동기 읽기는 키오스크를 멈춘다(리포 규약 — 로컬 I/O는 <c>Task.Run</c>).
    /// 선택 스냅샷을 비교해 <b>stale 결과를 버린다</b>(다른 파일을 고르거나 오버레이를 닫은 뒤 도착한 결과).
    /// </summary>
    private async Task LoadLicenseTextAsync(LicenseDocument? document)
    {
        if (document is null)
        {
            LicenseText = string.Empty;
            LicenseSelectionSummary = string.Empty;
            IsLicenseLoading = false;
            return;
        }

        IsLicenseLoading = true;
        LicenseErrorMessage = string.Empty;
        LicenseText = string.Empty;
        LicenseSelectionSummary = $"{document.DisplayName} · {document.SizeText}";
        try
        {
            var service = _licenseNotice;
            if (service is null)
            {
                LicenseErrorMessage = LicenseUnavailableMessage;
                return;
            }

            // ConfigureAwait(true): 아래 대입(PropertyChanged)이 UI 스레드에서 일어나야 한다.
            var result = await Task.Run(() => service.ReadText(document)).ConfigureAwait(true);
            if (!ReferenceEquals(document, SelectedLicenseDocument)) return;   // stale 폐기

            if (result.IsSuccess) LicenseText = result.Text!;
            else LicenseErrorMessage = result.ErrorMessage ?? LicenseUnavailableMessage;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "라이선스 고지 본문 읽기 실패");
            if (ReferenceEquals(document, SelectedLicenseDocument))
                LicenseErrorMessage = LicenseUnavailableMessage;
        }
        finally
        {
            if (ReferenceEquals(document, SelectedLicenseDocument)) IsLicenseLoading = false;
        }
    }

    // [license-viewer:end]

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

/// <summary>
/// 노출 파라미터 1개의 편집 표면 — <b>슬라이더 + 직접 입력 병행</b>(it23 §10.2, 요구 5).
/// <para>
/// 왜 슬라이더가 "값"이 아니라 "인덱스"인가: 셔터 속도·조리개는 연속량이 아니라 카메라가 허용하는
/// 이산 목록이다(<c>1/125</c>, <c>f/5.6</c>). WPF Slider는 연속 double이므로 도메인 인덱스를 값으로 쓰고
/// 표시는 항상 문자열로 한다 — 이러면 SDK 표기 관례를 파싱하지 않아도 정확한 값만 선택된다.
/// </para>
/// <para>
/// ⚠️ 직접 입력은 <b>정확 일치만</b> 통과한다. 근사 매칭(<c>1/100</c>→<c>1/125</c>)은 하지 않는다 —
/// 운영자 몰래 노출을 바꾸는 동작이기 때문이다(§17.2 비목표).
/// </para>
/// </summary>
public sealed partial class ExposureParameterViewModel : ObservableObject
{
    /// <summary>도메인 미확보 상태에서 입력한 값이 카메라 목록에 없을 때의 힌트(§10.2).</summary>
    public const string UnsupportedValueHint = "카메라가 지원하지 않는 값";

    /// <summary>슬라이더↔TextBox 상호 갱신이 서로를 다시 호출하는 루프 차단(SettingsViewModel의 _normalizing 관례).</summary>
    private bool _syncing;

    public ExposureParameterViewModel(ExposureParameter parameter, string label)
    {
        Parameter = parameter;
        Label = label;
    }

    /// <summary>어떤 노출 요소인지(적용 시 SDK로 전달되는 식별자).</summary>
    public ExposureParameter Parameter { get; }

    /// <summary>행 라벨(한글).</summary>
    public string Label { get; }

    /// <summary>카메라가 준 이산 목록(순서 보존). 비어 있으면 도메인 미확보.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDomainAvailable), nameof(MaxIndex))]
    private IReadOnlyList<string> _values = Array.Empty<string>();

    /// <summary>선택된 도메인 인덱스(슬라이더 값). -1 = 미선택.</summary>
    [ObservableProperty] private int _selectedIndex = -1;

    /// <summary>직접 입력·표시 문자열. 빈 문자열 = 미지정(카메라 현재값 유지) — ini에 그대로 저장된다.</summary>
    [ObservableProperty] private string _text = string.Empty;

    /// <summary>입력 검증 힌트(불일치 시). 빈 문자열이면 미표시.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHint))]
    private string _hint = string.Empty;

    /// <summary>카메라 도메인을 확보했는지(슬라이더 활성 조건).</summary>
    public bool IsDomainAvailable => Values.Count > 0;

    /// <summary>슬라이더 Maximum(= 마지막 인덱스). 도메인이 없으면 0.</summary>
    public double MaxIndex => Math.Max(0, Values.Count - 1);

    /// <summary>힌트 표시 여부.</summary>
    public bool HasHint => !string.IsNullOrEmpty(Hint);

    /// <summary>도메인 확보 여부가 바뀌었다(상위 VM이 W3 캡션 표시를 갱신한다).</summary>
    public event EventHandler? DomainAvailabilityChanged;

    /// <summary>
    /// 카메라 도메인 + 저장값을 반영. 저장값이 도메인에 있으면 슬라이더를 그 위치로 맞추고,
    /// 없으면 저장값을 그대로 보여 주며 힌트를 띄운다(값을 버리지 않는다 — 운영자가 장비 없이 미리
    /// 값을 준비해 두는 워크플로를 막을 이유가 없고, 적용 시점 검증이 안전망이다, §10.3).
    /// </summary>
    public void SetDomain(ExposureDomainEntry? entry, string? savedValue)
    {
        bool wasAvailable = IsDomainAvailable;
        _syncing = true;
        try
        {
            Values = entry?.Values ?? (IReadOnlyList<string>)Array.Empty<string>();
            Text = (savedValue ?? string.Empty).Trim();

            int index = entry?.IndexOf(Text) ?? -1;
            // 저장값이 목록에 없으면 카메라 현재값 위치를 보여 준다(둘 다 없으면 -1).
            SelectedIndex = index >= 0 ? index : entry?.CurrentIndex ?? -1;
            Hint = ComputeHint(entry, Text);
        }
        finally { _syncing = false; }

        if (wasAvailable != IsDomainAvailable)
            DomainAvailabilityChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>저장된 문자열만 다시 반영(도메인 유지 — 저장 직후 재로드용).</summary>
    public void SetSavedText(string? savedValue)
    {
        _syncing = true;
        try
        {
            Text = (savedValue ?? string.Empty).Trim();
            int index = IndexOfInDomain(Text);
            if (index >= 0) SelectedIndex = index;
            Hint = IsDomainAvailable && Text.Length > 0 && index < 0 ? UnsupportedValueHint : string.Empty;
        }
        finally { _syncing = false; }
    }

    private string ComputeHint(ExposureDomainEntry? entry, string text)
    {
        if (entry is null) return string.Empty;              // 도메인 미확보 → 자유 입력(검증 불가)
        if (text.Length == 0) return string.Empty;           // 미지정은 정상 상태
        return entry.IndexOf(text) >= 0 ? string.Empty : UnsupportedValueHint;
    }

    private int IndexOfInDomain(string text)
    {
        if (Values.Count == 0 || string.IsNullOrWhiteSpace(text)) return -1;
        var needle = text.Trim();
        for (int i = 0; i < Values.Count; i++)
        {
            if (string.Equals(Values[i]?.Trim(), needle, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return -1;
    }

    /// <summary>슬라이더 이동 → 표시 문자열 동기(선택된 값은 항상 도메인 안이라 힌트가 사라진다).</summary>
    partial void OnSelectedIndexChanged(int value)
    {
        if (_syncing) return;
        if (value < 0 || value >= Values.Count) return;

        _syncing = true;
        try
        {
            Text = Values[value];
            Hint = string.Empty;
        }
        finally { _syncing = false; }
    }

    /// <summary>직접 입력 → 도메인 정확 일치 시 슬라이더 동기, 불일치면 적용하지 않고 힌트만 띄운다.</summary>
    partial void OnTextChanged(string value)
    {
        if (_syncing) return;

        _syncing = true;
        try
        {
            int index = IndexOfInDomain(value);
            if (index >= 0)
            {
                SelectedIndex = index;
                Hint = string.Empty;
            }
            else
            {
                // 도메인을 모르면 검증할 수 없다 → 자유 입력 허용(저장만). 알면서 불일치면 힌트.
                Hint = IsDomainAvailable && !string.IsNullOrWhiteSpace(value)
                    ? UnsupportedValueHint
                    : string.Empty;
            }
        }
        finally { _syncing = false; }
    }
}
