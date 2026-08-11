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
    /// <summary>
    /// 테스트 모드 판정(it25 §5.5). <b>시뮬레이션 분기의 유일한 입력</b>이며 이 화면의 검색 시퀀스
    /// 한 곳에서만 읽는다(불변식 TS1). 미주입(null)이면 시뮬레이션이 아예 성립하지 않는다.
    /// <para>
    /// ⚠️ 분기 조건은 <c>IsTestUser</c>(참조 동일성)를 통과해야 한다(TM3·TS2). <c>IsEnabled</c> 단독으로
    /// 분기하면 테스트 ini를 켜 둔 채 <b>실계정으로 로그인한 운영자</b>가 가짜 "연결 확인됨"을 보고
    /// 실장비 진단을 그르친다.
    /// </para>
    /// </summary>
    private readonly ITestModeService? _testMode;
    /// <summary>
    /// PnP 휴대용 장치 이름 조회 이음새(it24 §5.1 ③). 기본값은 실제 WMI 프로브다.
    /// <para>
    /// ⚠️ 왜 델리게이트인가: WMI는 이 머신에 실제로 꽂힌 장치를 돌려주므로, 주입 지점이 없으면
    /// 검색 상태 전수표 테스트가 머신 구성에 따라 다른 결과를 본다(참고 라인 유무·매칭 여부).
    /// 판정은 순수 함수 뒤에 있지만, <b>관측을 테스트가 지정할 수 있어야</b> 표 전체가 headless로 고정된다.
    /// </para>
    /// </summary>
    private readonly Func<IReadOnlyList<string>> _probePortableDevices;
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
    // it23: 외부 카메라는 실배선(촬영 세션이 이 값을 읽는다).
    // it25: 프린터는 placeholder로 **환원**됐다 — 토글은 IsEnabled="False" + "추후 지원 예정" 캡션이고
    //       VM은 표시값만 로드한다(저장 미기록 = ini 원값 보존, §4.1·§4.3).
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

    /// <summary>
    /// "권한 없음" 캡션 표시 조건(it24 §4.3): <b>로그인했으나 편집 불가</b>(= TempUser)일 때만.
    /// 설정 진입 중 불변이라 INPC 불요.
    /// <para>
    /// 게스트에게는 이 캡션 대신 <c>GuestGateNote</c>("로그인 필요")가 뜬다. 게스트에게 "권한 없음"을 보여 주면
    /// "로그인하면 되는가?"라는 질문에 답하지 못하는 문구가 된다 — 두 상태의 조치가 다르므로 문구도 갈라야 한다.
    /// </para>
    /// </summary>
    public bool IsExternalEditDenied => IsLoggedIn && !CanEditExternalCamera;

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
        ILicenseNoticeService? licenseNotice = null,
        Func<IReadOnlyList<string>>? probePortableDevices = null,
        ITestModeService? testMode = null)
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
        _testMode = testMode;
        // 네임스페이스를 들이지 않고 정규화 이름으로 호출한다(MCPhoto.Capture와 MCPhoto.Core.Capture 혼동 회피).
        _probePortableDevices = probePortableDevices
            ?? (() => MCPhoto.Capture.PortableDeviceProbe.TryGetPortableDeviceNames(_logger));

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
        // it25 §4.1: 프린터 표면이 "추후 지원 예정" placeholder로 환원되어 **진입 시 열거가 없다**.
        //            설정은 열람 빈도가 높은 화면이고, 편집·표시할 프린터 표면이 없는데 스풀러를 왕복할
        //            이유가 사라졌다(열거자는 소비자 0 스캐폴드로 남는다 — §4.2).
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
            // it24 §4.2: 게스트도 같다(섹션이 이제 보인다). 외부 장치 토글은 **편집 게이트이지 동작 게이트가 아니므로**
            //   관리자가 켜 둔 DSLR은 게스트 세션에서도 동작한다 — off로 보여 주는 것이 오히려 거짓 표시다.
            //   그래서 아래 게스트 강제 off 블록에 외부 장치 필드를 넣지 않는다.
            ExternalCameraEnabled = s.ExternalCameraEnabled;
            ExternalCameraModel = s.ExternalCameraModel;
            ApplySavedExposureText(s);
            // it25 §4.3: 토글 표시값만 로드한다(편집 불가). PhotoPrinterName은 UI 표면이 없는
            //            잔존 키이므로 VM이 읽지도 쓰지도 않는다 — 저장 시 Clone 원값이 그대로 재기록된다.
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
        // it25 §4.1: 프린터 2키(PhotoPrinterEnabled·PhotoPrinterName)는 **어느 역할에서도 기록하지 않는다** —
        // ⚠️ 표면을 환원했으므로 VM이 가진 값은 "편집 불가 컨트롤의 표시값"뿐이다. 그것을 되기록하면
        //    ini 원값과 다를 이유가 없는 값을 매 저장마다 덮어쓰는 셈이고, 키 의미가 바뀔 때(인쇄 이터레이션)
        //    조용한 클로버 경로가 된다. 미기록 = Clone 원값 그대로 재기록 = 라운드트립 보존(§4.3).
        if (CanEditExternalCamera)
        {
            s.ExternalCameraEnabled = ExternalCameraEnabled;
            s.ExternalCameraModel = ExternalCameraModel;
            s.ExternalShutterSpeed = _shutterSpeed.Text;
            s.ExternalAperture = _aperture.Text;
            s.ExternalIso = _iso.Text;
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

    // ══════════════════════════════════════════════════════════════════════════════════
    // [external-discovery:begin] it24 — 외부 장치 검색 (설계 §5) / it25 — 인식된 카메라 · 프린터 환원
    //
    // ⚠️ 이 구역의 전 문구는 **두 명제를 섞지 않는다**(설계 §3):
    //    "연결 가능한 장치를 찾지 못했습니다"(부재 단정)는 SDK 제어 스택이 갖춰졌을 때만 말할 수 있고,
    //    스택이 없으면 "장치 연결 여부를 확인할 수 없습니다"(판정 불가) + 사유만 말한다.
    //    합치면 SDK 미탑재 배포본에서 화면이 부재를 단정하고, 운영자는 케이블·전원을 헛되이 점검한다.
    // ⚠️ USB 관측(WMI)은 **양성 신호 전용**이다: "감지되었습니다"는 말해도 미감지를 "없음"의 근거로 쓰지 않는다.
    // ══════════════════════════════════════════════════════════════════════════════════

    // ── 동결 문구(it24 §8.2 W16~W23 · it25 §8.3 W34·W38). 상수로 모으는 이유는 NikonCameraReasons와 같다 —
    //    같은 상태가 화면·테스트·운영 문서에서 다르게 설명되는 것을 막는다. ──

    /// <summary>W16 — S0(검색 전).</summary>
    public const string DiscoveryNotSearchedText = "장치를 검색하지 않았습니다. [장치 검색]으로 연결 상태를 확인하세요.";
    /// <summary>W17 — S1(검색 중).</summary>
    public const string DiscoverySearchingText = "장치 검색 중…";
    /// <summary>W18 — S2 헤드라인. ★ "없습니다"가 아니다(판정 불가).</summary>
    public const string DiscoveryUndeterminedText = "장치 연결 여부를 확인할 수 없습니다";
    /// <summary>W19 — S4 헤드라인. 부재 단정도 완화형으로 쓴다("찾지 못했다"는 어느 경우에도 참이다).</summary>
    public const string DiscoveryNotFoundText = "연결 가능한 장치를 찾지 못했습니다 (USB·전원·PTP 모드 확인)";
    /// <summary>W20a — S3 부연(스택 미비인데 USB 후보가 보이는 상태).</summary>
    public const string DiscoveryUncontrollableText = "SDK 모듈이 없어 제어할 수 없습니다";
    /// <summary>W20b — S5 부연(스택 정상인데 연결 실패 + USB 후보 있음).</summary>
    public const string DiscoveryConnectFailedText =
        "SDK 연결에 실패했습니다 — 다른 프로그램의 점유(웹캠 유틸리티 등)·케이블을 확인하세요";
    /// <summary>W21a — S6 부연.</summary>
    public const string DiscoveryTestHintText = "세부 확인·셔터 테스트는 [카메라 테스트]에서 할 수 있습니다";
    /// <summary>W22 — S7(검색 시퀀스 예외).</summary>
    public const string DiscoveryFailedText = "장치 검색에 실패했습니다. 다시 시도해 주세요.";
    /// <summary>
    /// W38 — 시뮬레이션 명시 라인(it25 §8.3, 불변식 TS4). 시뮬레이션이 만든 결과에는 <b>항상</b> 붙는다.
    /// 이 한 줄이 없으면 스크린샷 단위에서 실관측과 구분할 수단이 사라진다.
    /// </summary>
    public const string DiscoverySimulatedText = "테스트 모드 시뮬레이션 결과입니다 — 실제 장치 관측이 아닙니다.";
    /// <summary>W34 — 인식 콤보의 sentinel 항목 표시명(선택 안 한 상태).</summary>
    public const string RecognizedCameraNoneDisplay = "- 선택안함 -";

    /// <summary>W20 — S3·S5 감지 라인. 관측된 이름 원문을 그대로 노출한다(운영자가 육안으로 대조한다).</summary>
    public static string DiscoveryDetectedText(string names) => $"USB에서 장치가 감지되었습니다: {names}";
    /// <summary>W21 — S6 헤드라인. "연결됨"이 아니라 "확인됨"인 이유: 표시 시점엔 이미 해제되어 있다(§5.5).</summary>
    public static string DiscoveryConnectedText(string model) => $"{model} — 연결 확인됨";
    /// <summary>W21b — S6 배터리(조회 성공 시에만).</summary>
    public static string DiscoveryBatteryText(int percent) => $"배터리 {percent}%";
    /// <summary>W23 — 비매칭 휴대용 장치 참고 라인(제네릭 이름으로 뜬 카메라를 운영자가 알아볼 유일한 단서).</summary>
    public static string DiscoveryOtherDevicesText(string names)
        => $"참고: 감지된 휴대용 장치(카메라가 아닐 수 있음): {names}";

    /// <summary>W23 참고 라인에 나열할 최대 개수(그 이상은 화면을 덮는다).</summary>
    private const int OtherDeviceNoteLimit = 4;

    // ── 카메라 검색 상태 ──

    /// <summary>
    /// 검색 진행 중(S1). 단일 비행 플래그 겸 버튼 비활성 조건이다.
    /// ⚠️ 해제는 항상 <c>finally</c>에서 한다 — 예외 경로에서 true로 남으면 버튼이 영구 잠긴다(it20 교훈).
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DiscoverExternalCameraCommand))]
    private bool _isDiscovering;

    /// <summary>검색 결과 헤드라인(S0~S7). 초기값은 W16 — 검색하지 않은 상태를 정직하게 말한다.</summary>
    [ObservableProperty] private string _discoveryHeadline = DiscoveryNotSearchedText;

    // ── 인식된 카메라 콤보(it25 §6) ──
    //
    // it24까지 이 콤보는 **지원 모델 목록**이었다. it25에서 의미가 "연결이 인식된 카메라"로 바뀌었고,
    // 지원 목록은 [지원 카메라 목록] 오버레이로 완전히 분리됐다(R5 강화).
    //
    // ⚠️ "인식됨"은 **SDK 연결 확인(S6)만**이다(§6.1). WMI 관측 양성(S3·S5)은 장치명 문자열 우연에
    //    기대는 best-effort라 "그 지원 모델이 맞다"를 보장하지 못하는데, 콤보는 저장으로 이어지는
    //    조작 표면이다 — 제어 불가 항목을 올리면 운영자가 "선택했는데 촬영이 안 되는" 상태를 스스로
    //    만들 수 있다. 감지 사실은 검색 결과 라인(W20)이 이미 말한다.
    // ⚠️ 현 프로덕션(SDK 미동봉)에서 실경로의 인식 목록은 **항상 비어 있다** — 결함이 아니라
    //    사용자가 기술한 기본 상태이며(빈 콤보 = sentinel 단독), 채워진 콤보의 확인 수단이 시뮬레이션이다.

    /// <summary>
    /// 인식 콤보 목록. 초기값은 sentinel 단독 — S0은 "검색 전"이지 "없음 단정"이 아니다(W16·W33이 안내한다).
    /// ini에 저장되지 않는 <b>화면 세션 상태</b>이며 [장치 검색] 1회마다 재구성된다.
    /// </summary>
    public ObservableCollection<RecognizedCameraOption> RecognizedCameraOptions { get; } =
        new() { RecognizedCameraOption.None };

    /// <summary>
    /// 인식 콤보의 선택(<c>SelectedValue</c> 바인딩 대상). <c>""</c> = sentinel(선택 안 함).
    /// <para>
    /// ⚠️ 이 값을 ini 미러(<see cref="ExternalCameraModel"/>)에 <b>직접 바인딩하지 않는 이유</b>:
    /// 인식 목록이 비는 순간 WPF ComboBox가 매칭 실패한 <c>SelectedValue</c>를 null로 되써서
    /// 운영자가 맞춰 둔 저장값이 저장 한 번에 소멸한다(it24 P5·it7 B9 계열 함정). 사용자가
    /// "빈 목록에는 선택안함만"을 명시했으므로 합성 행 해법을 쓸 수 없어 <b>선택을 분리</b>한다.
    /// </para>
    /// </summary>
    [ObservableProperty] private string _recognizedCameraSelection = string.Empty;

    /// <summary>
    /// 목록 재구성이 만든 프로그램적 선택 변경을 ini 미러 갱신과 구분하는 가드
    /// (<c>_normalizing</c>·<c>ExposureParameterViewModel._syncing</c>과 같은 관례).
    /// </summary>
    private bool _syncingRecognizedSelection;

    /// <summary>
    /// 콤보 선택 → ini 미러 반영. <b>사용자가 인식 항목을 명시 선택했을 때만</b> 갱신한다(§6.3).
    /// <list type="bullet">
    /// <item>null 되쓰기(E25) → <c>""</c>로 정규화하고 저장값은 건드리지 않는다.</item>
    /// <item>sentinel 선택 → 저장값 불변(검색 결과가 설정을 지우지 않는다).</item>
    /// <item>목록 재구성 중의 선택 → 가드로 억제(자동 변경 금지).</item>
    /// </list>
    /// </summary>
    partial void OnRecognizedCameraSelectionChanged(string value)
    {
        // WPF는 목록에 없는 SelectedValue를 null로 되쓴다 — 표시상 sentinel로 정규화한다.
        if (value is null) { RecognizedCameraSelection = string.Empty; return; }
        if (_syncingRecognizedSelection || value.Length == 0) return;
        if (ExternalCameraModels.Find(value) is { } model) ExternalCameraModel = model.Id;
    }

    /// <summary>
    /// 인식 목록 재구성. <paramref name="recognized"/>가 null이면 sentinel 단독(S0~S5·S7)이고,
    /// 값이 있으면(S6만) sentinel + 인식 1행이다(§6.4 전수표).
    /// <para>
    /// ⚠️ 어떤 검색 상태도 <see cref="ExternalCameraModel"/>(ini 미러)을 바꾸지 않는다. 인식 Id가
    /// 저장 Id와 <b>일치할 때만</b> 그 행을 자동 선택하고, 다르면 sentinel에 둔다 — 자동 선택이
    /// 저장값을 따라가는 것이 아니라, 저장값이 이미 그것일 때 화면이 그 사실을 반영하는 것이다.
    /// </para>
    /// </summary>
    private void ApplyRecognizedCamera(ExternalCameraModel? recognized)
    {
        _syncingRecognizedSelection = true;
        try
        {
            RecognizedCameraOptions.Clear();
            RecognizedCameraOptions.Add(RecognizedCameraOption.None);

            if (recognized is null)
            {
                RecognizedCameraSelection = string.Empty;
                return;
            }

            RecognizedCameraOptions.Add(new RecognizedCameraOption(recognized.Id, recognized.DisplayName));
            RecognizedCameraSelection =
                string.Equals(recognized.Id, ExternalCameraModel, StringComparison.OrdinalIgnoreCase)
                    ? recognized.Id
                    : string.Empty;
        }
        finally { _syncingRecognizedSelection = false; }
    }

    /// <summary>
    /// 상세 라인(사유 원문·감지·배터리·참고). 사유는 <c>NikonCameraReasons</c> 원문을 그대로 흘린다 —
    /// 여기서 다시 문장을 만들면 같은 원인이 화면마다 다르게 설명된다.
    /// </summary>
    public ObservableCollection<string> DiscoveryDetailLines { get; } = new();

    /// <summary>
    /// [장치 검색]. 게이트는 <see cref="IsLoggedIn"/>(진단·상태 모달과 같은 눈높이 — TempUser 포함,
    /// 게스트 제외)이며 검색은 상태를 바꾸지 않는 진단 액션이다(§4.3).
    /// </summary>
    private bool CanDiscoverExternalCamera() => IsLoggedIn && !IsDiscovering;

    /// <summary>
    /// 장치 검색 1회(§5.2). 관측 3원 → Core 순수 판정 → 문구.
    /// <list type="bullet">
    /// <item>① 전제 검사 + ③ WMI 관측은 <c>Task.Run</c>에서 — 둘 다 UI 스레드를 막을 수 있는 로컬 I/O다.</item>
    /// <item>② SDK 연결은 <b>①이 참일 때만</b> 시도한다. 판정할 수 없는 상태의 연결 실패는 아무것도 증명하지 않으므로,
    ///       그 시도를 아예 하지 않는 편이 상태표를 단순하게 유지한다(USB도 건드리지 않는다).</item>
    /// <item>성공 시 스냅샷(모델명·배터리)만 채취하고 <b>즉시 해제</b>한다 — 설정 화면이 USB를 점유한 채
    ///       방치되면 화면 이탈·예외 경로마다 해제 설계를 새로 해야 한다(§5.5).</item>
    /// </list>
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanDiscoverExternalCamera))]
    private async Task DiscoverExternalCameraAsync()
    {
        if (IsDiscovering) return;   // CanExecute와 이중 방어(커맨드 밖 호출·연타)
        IsDiscovering = true;
        DiscoveryHeadline = DiscoverySearchingText;
        DiscoveryDetailLines.Clear();

        try
        {
            // ══ it25 §5.5: 시뮬레이션 판정 **단일 지점**(불변식 TS1) ══
            // 조건이 이 두 줄 외에 존재하지 않는다. IExternalCamera·INikonSdkShim·CaptureViewModel·
            // CameraTestViewModel·DI 등록 어디에도 시뮬레이션 구현을 주입하거나 데코레이트하지 않는다.
            // ⚠️ 금지: _testMode?.IsEnabled 단독 분기(TS2 — 테스트 ini를 켠 채 실계정으로 일하는 운영자에게
            //          가짜 "연결 확인됨"을 보여 실장비 진단을 그르친다) / IExternalCamera 데코레이터(TS1 —
            //          ConnectAsync는 촬영도 쓰는 멤버라 촬영 경로가 오염된다).
            var plan = ExternalCameraSimulation.Plan(_testMode?.Options ?? TestModeOptions.Disabled);
            if (plan is not null && _testMode!.IsTestUser(_shell.CurrentUser))
            {
                // 관측 I/O(CheckReadiness·WMI 프로브·ConnectAsync)를 **전부 건너뛴다** — 관측 위조가 아니라
                // 관측 생략 + 대체 입력이며(R3 유지), 그 사실을 W38 라인이 화면에서 명시한다(TS4).
                var simulated = ExternalDiscoveryJudge.Judge(plan.Readiness, usbCandidateSeen: false, plan.Connected);
                ApplyDiscoveryResult(simulated, plan.Readiness,
                    candidates: Array.Empty<string>(), allNames: Array.Empty<string>(),
                    modelName: plan.Model?.DisplayName, battery: null,
                    unavailableReason: null, recognized: plan.Model);
                DiscoveryDetailLines.Add(DiscoverySimulatedText);
                return;
            }

            var keywords = ModelKeywords();

            var probed = await Task.Run(() => (
                Readiness: _external.CheckReadiness(),
                Names: _probePortableDevices()));

            var candidates = MCPhoto.Capture.PortableDeviceProbe.MatchCandidates(probed.Names, keywords);

            bool connected = false;
            string? modelName = null;
            int? battery = null;

            if (probed.Readiness.CanControl)
            {
                connected = await _external.ConnectAsync();   // ConnectTimeout 5s 내장(어댑터)
                if (connected)
                {
                    modelName = _external.ModelName;
                    try
                    {
                        var caps = await _external.GetCapabilitiesAsync();
                        battery = caps?.BatteryLevelPercent;
                    }
                    catch (Exception ex)
                    {
                        // E15: 배터리·capability 조회 실패는 검색 성공 판정을 바꾸지 않는다(라인만 생략).
                        _logger?.LogWarning(ex, "검색 중 capability 조회 실패(배터리 표시 생략)");
                    }

                    await _external.DisconnectAsync();   // §5.5 연결 잔류 금지(어댑터가 예외를 삼킨다 — E16)
                }
            }

            var state = ExternalDiscoveryJudge.Judge(probed.Readiness, candidates.Count > 0, connected);
            ApplyDiscoveryResult(state, probed.Readiness, candidates, probed.Names, modelName, battery,
                unavailableReason: _external.UnavailableReason,
                // 실경로의 인식 모델은 **구성된 모델**이다 — ConnectAsync가 그 모델의 md3로 연결을 시도했고
                // 성공했으므로, 확인된 것은 그 1종이다(§6.5의 정직한 한계).
                recognized: connected ? ExternalCameraModels.Resolve(ExternalCameraModel) : null);
        }
        catch (Exception ex)
        {
            // E13: 예상 밖 예외도 크래시가 아니라 S7 문구로 끝난다(키오스크에서 설정 화면이 죽으면 홈으로 튕긴다).
            _logger?.LogWarning(ex, "외부 카메라 검색 실패");
            DiscoveryDetailLines.Clear();
            DiscoveryHeadline = DiscoveryFailedText;
            ApplyRecognizedCamera(null);   // S7도 인식 0 상태다(§6.4) — 직전 검색의 인식 행을 남겨 두지 않는다
        }
        finally { IsDiscovering = false; }
    }

    /// <summary>
    /// 모델 표시명에서 USB 관측 키워드를 유도한다(예 <c>"Nikon D5300"</c> → <c>["Nikon","D5300"]</c>).
    /// 레지스트리 스키마를 늘리지 않는 것이 요점 — 모델 추가는 여전히 Core 표 한 줄이다(it23 §3.3).
    /// </summary>
    private IReadOnlyList<string> ModelKeywords()
        => ExternalCameraModels.Resolve(ExternalCameraModel).DisplayName
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>
    /// 판정 결과 → 화면 문구 + 인식 콤보(it24 §5.3 표 · it25 §6.4 표). 문구 조립이 이 한 곳에만 있어야
    /// 상태별 명제가 어긋나지 않으며, 시뮬레이션도 같은 곳을 지난다(§5.4).
    /// </summary>
    /// <param name="unavailableReason">
    /// S4·S5의 상세 라인이 될 사유. <b>파라미터로 받는 이유</b>: 여기서 <c>_external.UnavailableReason</c>을
    /// 직접 읽으면 시뮬레이션 결과에 <b>실장비 관측 한 줄이 섞여</b> 들어간다(시뮬레이션 산출물은 계획이
    /// 말한 것만이어야 한다 — TS3·TS4). 실경로가 넘기고 시뮬레이션은 null을 넘긴다.
    /// </param>
    /// <param name="recognized">인식 콤보에 올릴 모델(S6에서만 non-null). §6.4 전수표.</param>
    private void ApplyDiscoveryResult(
        ExternalCameraDiscoveryState state,
        ExternalCameraReadiness readiness,
        IReadOnlyList<string> candidates,
        IReadOnlyList<string> allNames,
        string? modelName,
        int? battery,
        string? unavailableReason,
        ExternalCameraModel? recognized)
    {
        DiscoveryDetailLines.Clear();
        ApplyRecognizedCamera(recognized);

        switch (state)
        {
            case ExternalCameraDiscoveryState.UndeterminedStackMissing:      // S2
                DiscoveryHeadline = DiscoveryUndeterminedText;
                AddDetailLine(readiness.Reason);
                AddOtherDeviceNote(allNames, candidates);
                break;

            case ExternalCameraDiscoveryState.DetectedUncontrollable:        // S3
                DiscoveryHeadline = DiscoveryDetectedText(string.Join(", ", candidates));
                DiscoveryDetailLines.Add(DiscoveryUncontrollableText);
                AddDetailLine(readiness.Reason);
                break;

            case ExternalCameraDiscoveryState.NotFound:                      // S4
                DiscoveryHeadline = DiscoveryNotFoundText;
                AddDetailLine(unavailableReason);
                AddOtherDeviceNote(allNames, candidates);
                break;

            case ExternalCameraDiscoveryState.DetectedConnectFailed:         // S5
                DiscoveryHeadline = DiscoveryDetectedText(string.Join(", ", candidates));
                DiscoveryDetailLines.Add(DiscoveryConnectFailedText);
                AddDetailLine(unavailableReason);
                break;

            case ExternalCameraDiscoveryState.Connected:                     // S6
                DiscoveryHeadline = DiscoveryConnectedText(
                    string.IsNullOrWhiteSpace(modelName)
                        ? (recognized ?? ExternalCameraModels.Resolve(ExternalCameraModel)).DisplayName
                        : modelName!);
                if (battery is int percent) DiscoveryDetailLines.Add(DiscoveryBatteryText(percent));
                DiscoveryDetailLines.Add(DiscoveryTestHintText);
                break;

            default:
                // Judge는 S0·S1·S7을 반환하지 않는다. 여기 오면 판정 분기가 늘어난 것이므로 검색 전 상태로 되돌린다.
                DiscoveryHeadline = DiscoveryNotSearchedText;
                break;
        }
    }

    private void AddDetailLine(string? line)
    {
        if (!string.IsNullOrWhiteSpace(line)) DiscoveryDetailLines.Add(line!);
    }

    /// <summary>
    /// W23 참고 라인: 키워드에 걸리지 않은 휴대용 장치 이름을 원문으로 나열한다(최대 4개).
    /// <para>
    /// 왜 필요한가: Nikon 바디가 제네릭 "MTP Portable Device"로 뜨면 키워드 매칭은 miss난다(U2).
    /// 그때 이 라인이 없으면 화면은 "확인할 수 없다"만 말하고, 운영자는 카메라가 PC에 보이는지조차 알 수 없다.
    /// </para>
    /// 매칭이 하나라도 있으면 감지 라인(W20)이 이미 그 역할을 하므로 붙이지 않는다.
    /// </summary>
    private void AddOtherDeviceNote(IReadOnlyList<string> allNames, IReadOnlyList<string> candidates)
    {
        if (candidates.Count > 0 || allNames.Count == 0) return;
        var listed = allNames.Take(OtherDeviceNoteLimit);
        DiscoveryDetailLines.Add(DiscoveryOtherDevicesText(string.Join(", ", listed)));
    }

    // ── 지원 카메라 목록 오버레이(it25 §7) ──
    //
    // 콤보가 "인식된 카메라"가 된 뒤로 **이 오버레이가 지원 목록의 유일한 자리**다.
    // ⚠️ 별도 Window로 만들지 않는다: headless 테스트가 Window를 인스턴스화할 수 없어(B-T9 함정)
    //    XAML 회귀(바인딩 오타·리소스 키)를 잡을 수 없다 — 라이선스 고지가 같은 이유로 오버레이다.
    // ⚠️ 권한 게이트를 걸지 않는다: 지원 모델 목록은 비밀이 아니고 열람은 편집이 아니다.

    /// <summary>오버레이 표시 여부. 정적 데이터만 보여 주므로 열림이 어떤 장치·파일 I/O도 유발하지 않는다.</summary>
    [ObservableProperty] private bool _isSupportedCameraListOpen;

    /// <summary>
    /// 제조사별로 묶은 지원 모델(오버레이 바인딩). 레지스트리 파생 <b>불변</b> 목록이라 INPC가 불요하다.
    /// <para>
    /// <c>CollectionViewSource</c> 그룹핑을 쓰지 않는 이유: 정적 소량 데이터에 뷰 계층 그룹핑을 얹으면
    /// 테스트 불가능한 XAML 로직만 늘어난다. 순수 LINQ면 headless로 정렬·묶음을 단정할 수 있다.
    /// </para>
    /// </summary>
    public IReadOnlyList<SupportedCameraGroup> SupportedCameraGroups { get; } =
        ExternalCameraModels.All
            .GroupBy(m => m.Manufacturer, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => new SupportedCameraGroup(
                g.Key,
                g.Select(m => m.ModelName).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray()))
            .ToArray();

    /// <summary>[지원 카메라 목록] — 오버레이 열기(게이트 없음).</summary>
    [RelayCommand]
    private void OpenSupportedCameraList() => IsSupportedCameraListOpen = true;

    /// <summary>[닫기] — 오버레이 닫기. 구독·타이머·자원이 없어 정리할 것도 없다(E27).</summary>
    [RelayCommand]
    private void CloseSupportedCameraList() => IsSupportedCameraListOpen = false;

    // ── 프린터 ──
    //
    // it25 §4: 사용자 지시("아직 지원되는 항목이 하나도 없으니까 추후 제공으로 남겨놔줘")로 프린터 표면을
    // placeholder로 환원했다. VM에 남는 것은 토글 표시값(PhotoPrinterEnabled) 하나뿐이며 열거·선택·상태
    // 문구·[다시 검색]은 전부 사라졌다(it24 §7의 판정 (b)를 it25 §4가 대체).
    //
    // ⚠️ 열거자(IPrinterEnumerator·SystemPrinterEnumerator)는 **삭제하지 않았다** — 소비자 0인 의도된
    //    스캐폴드이며 인쇄 기능 이터레이션이 재배선한다(IPhotoPrinter/NullPhotoPrinter와 같은 지위, §4.2).
    // ⚠️ ini 2키(PhotoPrinterEnabled·PhotoPrinterName)도 유지한다. WriteFrom에서 빼면 기존 ini의 값이
    //    **첫 저장에서 소멸**하므로(외래 섹션 보존과 같은 계열의 함정) AppSettings는 한 줄도 건드리지 않는다.

    // [external-discovery:end]

    // ══════════════════════════════════════════════════════════════════════════════════
    // [license-viewer:begin] it24 — 프로젝트 라이선스 고지 (설계 §2·§3, it23 C부 재설계)
    //
    // ⚠️ 이 구역은 **계정·권한·시험 세션 종류를 읽지 않는다**(수락 기준 AC-C2, 정적 검사 C-T14b가 고정).
    //    고지 접근은 로그인 여부와 무관해야 하며(GPLv3 §4 — 손님 세션도 전문을 볼 수 있어야 한다),
    //    이 구역이 세션을 전혀 읽지 않으면 "어떤 상태에서 못 보이나"라는 질문이 구조적으로 성립하지 않는다.
    //    도달성은 전부 상위 진입 게이트(설정 화면 진입)가 결정한다.
    // ⚠️ UI 어디에도 폴더 경로·파일명을 노출하지 않는다 — 경로는 로그에만 남는다. 파일명 노출이 허용되는
    //    유일한 지점은 강등 폴백 목록과 미참조 문서 섹션이며(우리가 아는 정보가 파일명뿐이다),
    //    정상 배포물에서는 둘 다 렌더링되지 않는다(설계 §2.6).
    // ⚠️ 화면은 2단이다: Level 1 = 요약 카드(기본), Level 2 = 전문/상세 1건. 같은 오버레이 안에서
    //    Visibility로 전환하며, 새 창·새 화면 상태를 만들지 않는다.
    // ══════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// 고지 화면의 2단 구조. 새 <c>AppState</c>·새 <c>Window</c>를 만들지 않는 이유는 촬영 상태 기계를
    /// 라이선스 표시 때문에 건드리지 않기 위해서다.
    /// </summary>
    public enum LicenseViewerPage
    {
        /// <summary>Level 1 — 컴포넌트 요약 카드.</summary>
        Summary,
        /// <summary>Level 2 — 라이선스 전문 또는 상세 고지 1건.</summary>
        FullText,
    }

    /// <summary>고지 폴더 자체가 없을 때(F1). 배포 산출물 누락이므로 감추지 않고 알린다.</summary>
    public const string LicenseFolderMissingMessage =
        "라이선스 고지 폴더를 찾을 수 없습니다. 배포 산출물에 licenses 폴더가 누락된 상태이므로 개발자에게 알려주세요.";

    /// <summary>폴더는 있으나 고지 파일이 0건일 때(F2).</summary>
    public const string LicenseFilesMissingMessage =
        "라이선스 고지 파일을 찾을 수 없습니다. 배포 산출물이 불완전하므로 개발자에게 알려주세요.";

    /// <summary>열거 자체가 불가능할 때(F6 — 서비스 미주입·경로 권한).</summary>
    public const string LicenseUnavailableMessage =
        "라이선스 고지를 불러올 수 없습니다. 개발자에게 알려주세요.";

    /// <summary>Level 2 부제 — 라이선스 전문을 보고 있을 때.</summary>
    public const string LicenseFullTextSubtitleText = "라이선스 전문";

    /// <summary>Level 2 부제 — 상세 고지(소스 코드 제공 안내)를 보고 있을 때.</summary>
    public const string LicenseNoticeSubtitleText = "소스 코드 제공 안내";

    /// <summary>오버레이 표시 여부. [프로젝트 라이선스 고지] 버튼으로만 열리고 [닫기]·Esc로만 닫힌다.</summary>
    [ObservableProperty] private bool _isLicenseViewerOpen;

    /// <summary>현재 단계. XAML은 아래 두 bool만 보고 <c>BoolToVis</c>로 전환한다(신규 컨버터 0개).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLicenseSummaryPage))]
    [NotifyPropertyChangedFor(nameof(IsLicenseFullTextPage))]
    private LicenseViewerPage _licensePage = LicenseViewerPage.Summary;

    public bool IsLicenseSummaryPage => LicensePage == LicenseViewerPage.Summary;
    public bool IsLicenseFullTextPage => LicensePage == LicenseViewerPage.FullText;

    /// <summary>
    /// 요약 카드 — 이 소프트웨어 본체. 카드 소스를 종류별 2개 컬렉션으로 나눈 이유: 섹션 머리를
    /// "항목이 있을 때만" 띄우려면 그룹별 존재 여부가 필요하고, <c>CollectionViewSource</c> 그룹 헤더에서
    /// 그룹 키(bool)를 문구로 바꾸려면 신규 컨버터나 <c>object</c> 대상 DataTrigger가 필요해진다
    /// (설계 §3.6의 "신규 컨버터 0개" 제약과 충돌).
    /// </summary>
    public ObservableCollection<LicenseComponent> LicenseSelfComponents { get; } = new();

    /// <summary>요약 카드 — 동봉된 오픈소스.</summary>
    public ObservableCollection<LicenseComponent> LicenseBundledComponents { get; } = new();

    public bool HasLicenseSelfComponents => LicenseSelfComponents.Count > 0;
    public bool HasLicenseBundledComponents => LicenseBundledComponents.Count > 0;

    /// <summary>카드가 하나라도 있는지. 0개면 카드 영역 전체를 접고 배너만 남긴다.</summary>
    public bool HasLicenseComponents => HasLicenseSelfComponents || HasLicenseBundledComponents;

    /// <summary>
    /// 미참조 고지 문서(정상 배포물에서는 0건) + 강등 시의 폴백 목록.
    /// ⚠️ it24에서 <b>의미가 바뀌었다</b> — 더 이상 화면의 기본 목록이 아니다.
    /// </summary>
    public ObservableCollection<LicenseDocument> LicenseDocuments { get; } = new();

    /// <summary>폴백·미참조 목록 섹션 표시 여부.</summary>
    public bool HasLicenseDocuments => LicenseDocuments.Count > 0;

    /// <summary>폴백·미참조 목록의 선택. 선택되면 그 문서의 본문으로 Level 2에 진입한다.</summary>
    [ObservableProperty] private LicenseDocument? _selectedLicenseDocument;

    /// <summary>본문 전문. 개행(CRLF)·탭을 변환하지 않은 원문이다("그대로 노출" 요구).</summary>
    [ObservableProperty] private string _licenseText = string.Empty;

    /// <summary>강등 배너(D1·D2). 닫을 수 없다 — 배포 사고를 현장에서 드러낸다.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLicenseDegraded))]
    private string _licenseDegradedMessage = string.Empty;

    public bool HasLicenseDegraded => !string.IsNullOrEmpty(LicenseDegradedMessage);

    /// <summary>실패 안내(F1~F6). 빈 문자열이면 미노출.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLicenseError))]
    private string _licenseErrorMessage = string.Empty;

    /// <summary>실패 안내 노출 여부(문구가 있을 때만).</summary>
    public bool HasLicenseError => !string.IsNullOrEmpty(LicenseErrorMessage);

    /// <summary>본문 읽는 중(`불러오는 중…` 표시). 파일이 느린 저장소에 있을 수 있다.</summary>
    [ObservableProperty] private bool _isLicenseLoading;

    /// <summary>
    /// Level 2 헤더. 정상 경로는 <c>{구성 요소} · {SPDX}</c>이며 <b>파일명이 들어가지 않는다</b>.
    /// 폴백 문서에서 진입한 경우에만 <c>{파일명} · {크기}</c>가 된다(그 목록은 파일명이 유일한 정보다).
    /// </summary>
    [ObservableProperty] private string _licenseFullTextCaption = string.Empty;

    /// <summary>Level 2 부제(전문인지 상세 고지인지).</summary>
    [ObservableProperty] private string _licenseFullTextSubtitle = string.Empty;

    /// <summary>Level 1 푸터 우측의 고지 기준일 표기. 값이 없으면 빈 문자열(미표시).</summary>
    [ObservableProperty] private string _licenseNoticeAsOfText = string.Empty;

    /// <summary>
    /// 진행 중인 본문 읽기(테스트가 결정적으로 대기하는 이음새 — <c>UserMgmtViewModel.FrameCountLoadTask</c> 선례).
    /// </summary>
    public Task? LicenseLoadTask { get; private set; }

    /// <summary>
    /// 단조 증가 요청 ID. 본문 요청 출처가 3개(카드 전문·카드 상세 고지·폴백 문서 선택)로 늘어나
    /// "선택 객체 비교"만으로는 stale 판정이 불가능해졌다 — 도착 시 ID가 최신인 결과만 반영한다.
    /// </summary>
    private int _licenseRequestId;

    /// <summary>
    /// 고지 열기: 요약 재구성 → Level 1. <b>전문을 읽지 않는다</b>(종전에는 열자마자 색인 본문을 읽었다).
    /// 실패는 예외가 아니라 안내 문구로 끝난다 — 여기서 예외가 새면 설정 화면이 통째로 닫힌다(홈 복귀 사고).
    /// </summary>
    [RelayCommand]
    private async Task OpenLicenseViewer()
    {
        IsLicenseViewerOpen = true;
        ResetLicenseViewerState();

        var service = _licenseNotice;
        if (service is null)
        {
            LicenseErrorMessage = LicenseUnavailableMessage;
            return;
        }

        LicenseSummary summary;
        try
        {
            // 매니페스트 읽기 + 파일 존재 검사 N회 = 디스크 접근. 느린·네트워크 저장소에서 UI를 멈추지 않는다.
            // ConfigureAwait(true): 아래 대입(PropertyChanged)이 UI 스레드에서 일어나야 한다.
            summary = await Task.Run(service.ReadSummary).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            // 서비스 계약상 예외를 던지지 않지만, 다른 구현이 던져도 화면은 열려 있어야 한다.
            _logger?.LogWarning(ex, "라이선스 고지 요약 산출 실패");
            LicenseErrorMessage = LicenseUnavailableMessage;
            return;
        }

        foreach (var component in summary.Components)
        {
            if (component.IsSelf) LicenseSelfComponents.Add(component);
            else LicenseBundledComponents.Add(component);
        }
        foreach (var document in summary.UnlistedDocuments) LicenseDocuments.Add(document);
        NotifyLicenseCollectionsChanged();

        LicenseNoticeAsOfText = string.IsNullOrEmpty(summary.UpdatedOn)
            ? string.Empty
            : $"{summary.UpdatedOn} 기준";

        if (!string.IsNullOrEmpty(summary.DegradedMessage))
        {
            // 강등이어도 폴더에 문서가 있으면 그것을 그대로 보여준다(전문 도달 경로 유지 = GPLv3 §4의 마지막 그물).
            // 문서조차 없으면 강등이 아니라 배포 누락이므로 폴더 부재(F1)와 파일 0건(F2)을 구분해 알린다.
            if (HasLicenseDocuments) LicenseDegradedMessage = summary.DegradedMessage!;
            else LicenseErrorMessage = service.Exists ? LicenseFilesMissingMessage : LicenseFolderMissingMessage;
        }
        else if (!HasLicenseComponents)
        {
            // 서비스는 항목 0개를 강등으로 판정하므로 여기까지 오지 않는다. 다른 구현 대비 방어.
            LicenseErrorMessage = service.Exists ? LicenseFilesMissingMessage : LicenseFolderMissingMessage;
        }
    }

    /// <summary>고지 닫기: 본문(최대 수십 KB)과 컬렉션을 놓아준다. 닫히면 설정 편집이 다시 가능해진다.</summary>
    [RelayCommand]
    private void CloseLicenseViewer()
    {
        IsLicenseViewerOpen = false;
        ResetLicenseViewerState();
    }

    /// <summary>[라이선스 전문 보기] — 해당 구성 요소의 전문 파일로 Level 2 진입.</summary>
    [RelayCommand]
    private async Task ShowLicenseFullText(LicenseComponent? component)
    {
        if (component is null) return;
        LicenseLoadTask = LoadLicenseBodyAsync(
            CaptionFor(component), LicenseFullTextSubtitleText,
            service => service.ReadText(component.FullTextFile));
        await LicenseLoadTask;
    }

    /// <summary>[소스 코드 제공 안내] — 해당 구성 요소의 상세 고지 파일로 Level 2 진입.</summary>
    [RelayCommand]
    private async Task ShowLicenseNotice(LicenseComponent? component)
    {
        if (component?.NoticeFile is not { Length: > 0 } noticeFile) return;
        LicenseLoadTask = LoadLicenseBodyAsync(
            CaptionFor(component), LicenseNoticeSubtitleText,
            service => service.ReadText(noticeFile));
        await LicenseLoadTask;
    }

    /// <summary>[← 뒤로] — Level 2 → Level 1. 본문(수십 KB)을 즉시 놓아준다.</summary>
    [RelayCommand]
    private void BackToLicenseSummary()
    {
        unchecked { _licenseRequestId++; }   // 진행 중 로드 결과를 stale로 만든다
        LicensePage = LicenseViewerPage.Summary;
        SelectedLicenseDocument = null;
        LicenseText = string.Empty;
        LicenseFullTextCaption = string.Empty;
        LicenseFullTextSubtitle = string.Empty;
        LicenseErrorMessage = string.Empty;
        IsLicenseLoading = false;
    }

    /// <summary>
    /// Esc 1키의 3분기: Level 2 → Level 1 / Level 1 → 닫기 / 닫힌 상태 → <b>아무 것도 하지 않는다</b>.
    /// <c>KeyBinding</c>이 커맨드 하나만 지목할 수 있어 분기를 VM에 두었다 — 덕분에 3분기를 단위 테스트로 검증한다
    /// (설정 화면을 Esc로 닫는 동작을 새로 만들지 않는다).
    /// </summary>
    [RelayCommand]
    private void EscapeLicenseViewer()
    {
        if (!IsLicenseViewerOpen) return;
        if (LicensePage == LicenseViewerPage.FullText) BackToLicenseSummary();
        else CloseLicenseViewer();
    }

    /// <summary>
    /// 폴백·미참조 목록의 선택 변경 → 그 문서 본문으로 Level 2 진입.
    /// ⚠️ <c>null</c>은 초기화 경로(컬렉션 비움·뒤로)이므로 페이지를 전환하지 않는다 — 하지 않으면
    /// 닫기·뒤로가 곧바로 Level 2를 다시 열어 버린다.
    /// </summary>
    partial void OnSelectedLicenseDocumentChanged(LicenseDocument? value)
    {
        if (value is null) return;
        LicenseLoadTask = LoadLicenseBodyAsync(
            $"{value.DisplayName} · {value.SizeText}", LicenseFullTextSubtitleText,
            service => service.ReadText(value));
    }

    /// <summary>Level 2 헤더 문구. 파일명을 쓰지 않는다(요구 R1).</summary>
    private static string CaptionFor(LicenseComponent component) => $"{component.Name} · {component.SpdxId}";

    /// <summary>
    /// 본문 읽기(3경로 공용). <c>Task.Run</c>으로 오프로드하는 이유: 파일이 네트워크 드라이브·느린 디스크에
    /// 있을 수 있고 UI 스레드 동기 읽기는 키오스크를 멈춘다(리포 규약 — 로컬 I/O는 <c>Task.Run</c>).
    /// 요청 ID를 비교해 <b>stale 결과를 버린다</b>(다른 항목을 누르거나 닫은 뒤 도착한 결과).
    /// </summary>
    private async Task LoadLicenseBodyAsync(
        string caption, string subtitle, Func<ILicenseNoticeService, LicenseTextResult> read)
    {
        int id;
        unchecked { id = ++_licenseRequestId; }

        LicensePage = LicenseViewerPage.FullText;
        LicenseFullTextCaption = caption;
        LicenseFullTextSubtitle = subtitle;
        LicenseText = string.Empty;
        LicenseErrorMessage = string.Empty;
        IsLicenseLoading = true;
        try
        {
            var service = _licenseNotice;
            if (service is null)
            {
                LicenseErrorMessage = LicenseUnavailableMessage;
                return;
            }

            var result = await Task.Run(() => read(service)).ConfigureAwait(true);
            if (id != _licenseRequestId) return;   // stale 폐기

            if (result.IsSuccess) LicenseText = result.Text!;
            else LicenseErrorMessage = result.ErrorMessage ?? LicenseUnavailableMessage;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "라이선스 고지 본문 읽기 실패");
            if (id == _licenseRequestId) LicenseErrorMessage = LicenseUnavailableMessage;
        }
        finally
        {
            if (id == _licenseRequestId) IsLicenseLoading = false;
        }
    }

    /// <summary>열기·닫기 공용 초기화. 열 때마다 재구성하는 이유는 파일 교체·삭제를 반영하기 위해서다.</summary>
    private void ResetLicenseViewerState()
    {
        unchecked { _licenseRequestId++; }   // 진행 중 로드 결과 폐기
        LicensePage = LicenseViewerPage.Summary;
        LicenseSelfComponents.Clear();
        LicenseBundledComponents.Clear();
        LicenseDocuments.Clear();
        NotifyLicenseCollectionsChanged();
        SelectedLicenseDocument = null;
        LicenseText = string.Empty;
        LicenseFullTextCaption = string.Empty;
        LicenseFullTextSubtitle = string.Empty;
        LicenseErrorMessage = string.Empty;
        LicenseDegradedMessage = string.Empty;
        LicenseNoticeAsOfText = string.Empty;
        IsLicenseLoading = false;
    }

    /// <summary>컬렉션 개수 기반 <c>Has*</c>는 소스 생성기가 알림을 만들어 주지 않으므로 직접 올린다.</summary>
    private void NotifyLicenseCollectionsChanged()
    {
        OnPropertyChanged(nameof(HasLicenseSelfComponents));
        OnPropertyChanged(nameof(HasLicenseBundledComponents));
        OnPropertyChanged(nameof(HasLicenseComponents));
        OnPropertyChanged(nameof(HasLicenseDocuments));
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

/// <summary>
/// 인식된 카메라 콤보 항목(it25 §6.2). <paramref name="Value"/>가 값 기반 선택 키(레지스트리 Id 또는
/// sentinel의 빈 문자열)이고 <paramref name="Display"/>는 표시 문자열이다.
/// <para>
/// ⚠️ 콤보는 <c>SelectedValuePath="Value"</c>로 값 기반 선택을 쓴다(it7 B9 — 인덱스 바인딩은 목록이
/// 채워지는 순간 선택을 0번으로 덮는다). sentinel의 <c>Value</c>가 <c>""</c>인 것이 요점 —
/// "선택 안 함"이 유효한 값으로 표현되므로 인식 0 상태에서도 콤보를 열 수 있다.
/// </para>
/// </summary>
public sealed record RecognizedCameraOption(string Value, string Display)
{
    /// <summary>sentinel 항목(W34). 인식 결과와 무관하게 항상 목록의 첫 행이다.</summary>
    public static RecognizedCameraOption None { get; } =
        new(string.Empty, SettingsViewModel.RecognizedCameraNoneDisplay);

    public override string ToString() => Display;
}

/// <summary>
/// 지원 카메라 오버레이의 제조사 그룹 1개(it25 §7.3). 제조사가 헤더, 제품명이 하위 행이다 —
/// 사용자 요구("제조사, 제품명 별로 정리")의 화면 구조를 그대로 데이터로 만든다.
/// </summary>
public sealed record SupportedCameraGroup(string Manufacturer, IReadOnlyList<string> Models);

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
