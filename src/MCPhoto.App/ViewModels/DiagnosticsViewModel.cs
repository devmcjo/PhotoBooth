using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MCPhoto.App.Services;
using MCPhoto.Capture;
using MCPhoto.Core.Build;
using MCPhoto.Core.Capture;
using MCPhoto.Core.Models;
using MCPhoto.Core.Settings;
using MCPhoto.Core.Upload;
using Microsoft.Extensions.Logging;

namespace MCPhoto.App.ViewModels;

/// <summary>
/// 진단/상태 화면 VM(관리자 트러블슈팅용). 모달 다이얼로그 전용 — 별도 AppState 미추가(회귀 표면 0). (it11 §3.14)
/// 카메라(연결·목록)·ffmpeg(가용·경로)·서버 연결(백엔드 구성·버킷·주소·키 내장·로그인 계정) 헬스체크
/// + 로그 폴더 경로·열기 + 개발자 문의(연락처·버전·빌드 시각·웹 배포일).
/// it15 §6.6: 레거시 서비스 계정 키 탐색 경로 섹션은 직결 경로 폐기로 삭제.
/// UI 타입(Visibility/Brush) 미의존 — 상태는 bool/int/string, 색은 View의 DataTrigger가 담당(§1.3).
/// 진단 화면은 라이브 프리뷰(StartAsync)를 켜지 않는다 → 카메라 점유 없음(열거만). (§3.14.4)
/// </summary>
public sealed partial class DiagnosticsViewModel : ObservableObject
{
    /// <summary>값을 얻지 못했을 때의 공통 표기(빈칸으로 두면 "표시 누락"과 구분되지 않는다).</summary>
    private const string Unknown = "(확인 불가)";

    /// <summary>
    /// 웹 배포일 조회 타임아웃. 백엔드 HttpClient의 기본 타임아웃은 100초(업로드 기준)라서 그대로 쓰면
    /// 서버 미도달 시 진단 창이 100초간 뜨지 않는다 — 진단 표기용 조회는 짧게 끊고 "확인 불가"로 넘어간다.
    /// </summary>
    private static readonly TimeSpan WebDeployProbeTimeout = TimeSpan.FromSeconds(5);

    private readonly ICameraService _camera;
    private readonly FfmpegRunner _ffmpeg;
    private readonly IFirebaseClient _firebase;
    private readonly ILogFolderService _logFolder;
    private readonly ISettingsService _settings;
    private readonly SessionContext _session;
    private readonly IBuildInfoService _buildInfo;
    private readonly IServerDeployInfoService _serverDeploy;
    private readonly IClipboardService _clipboard;
    private readonly ILicenseFolderService _licenseFolder;
    private readonly ILogger<DiagnosticsViewModel>? _logger;

    public DiagnosticsViewModel(ICameraService camera, FfmpegRunner ffmpeg, IFirebaseClient firebase,
        ILogFolderService logFolder, ISettingsService settings, SessionContext session,
        IBuildInfoService buildInfo, IServerDeployInfoService serverDeploy, IClipboardService clipboard,
        ILicenseFolderService licenseFolder,
        ILogger<DiagnosticsViewModel>? logger = null)
    {
        _licenseFolder = licenseFolder;
        _camera = camera;
        _ffmpeg = ffmpeg;
        _firebase = firebase;
        _logFolder = logFolder;
        _settings = settings;
        _session = session;
        _buildInfo = buildInfo;
        _serverDeploy = serverDeploy;
        _clipboard = clipboard;
        _logger = logger;
    }

    // ── 카메라 ──
    /// <summary>카메라 열거 진행 중(로딩 표시·재검사 버튼 비활성).</summary>
    [ObservableProperty] private bool _isCheckingCamera;

    /// <summary>연결된 카메라 대수.</summary>
    [ObservableProperty] private int _cameraCount;

    /// <summary>카메라 연결 여부(색상 트리거용). 0대=false.</summary>
    [ObservableProperty] private bool _hasCamera;

    /// <summary>카메라 요약 문구(예: "2대 연결됨" / "미연결").</summary>
    [ObservableProperty] private string _cameraSummary = string.Empty;

    /// <summary>연결된 카메라 목록(Index·Name). #15 FriendlyName이 있으면 실제 장치명 표시.</summary>
    public ObservableCollection<CameraDevice> Cameras { get; } = new();

    // ── ffmpeg ──
    /// <summary>ffmpeg 실행 파일 존재 여부(실연동 가능).</summary>
    public bool FfmpegAvailable => _ffmpeg.IsAvailable;
    /// <summary>ffmpeg 해석 경로(번들/PATH).</summary>
    public string FfmpegPath => _ffmpeg.FfmpegPath;

    // ── 서버 연결(백엔드) it15 §6.6 ──
    /// <summary>백엔드 구성 여부(base URL 설정됨). 도달 성공을 뜻하지는 않는다.</summary>
    public bool IsBackendConfigured => _firebase.IsInitialized;

    /// <summary>구성 시 스토리지 버킷명, 미구성 시 안내.</summary>
    public string FirebaseBucket => _firebase.IsInitialized ? _firebase.Bucket : "(미구성)";

    /// <summary>백엔드 base URL(공개값). 미설정이면 안내 문구.</summary>
    public string BackendBaseUrl =>
        _settings.Current.BackendBaseUrl is { Length: > 0 } url ? url : "(미설정)";

    /// <summary>
    /// 백엔드 게이트 키 내장 여부. ⚠️ 키 값 자체는 절대 표시하지 않는다(반비밀) — 부울 표기만.
    /// </summary>
    public string BackendApiKeyState =>
        string.IsNullOrEmpty(_settings.Current.BackendApiKey) ? "미설정" : "설정됨";

    /// <summary>현재 로그인 계정 요약({Id} · {로그인방식} · {역할} · PIN {상태}). 게스트면 "게스트".</summary>
    public string SignedInAccount =>
        _session.CurrentUser is { } u
            ? $"{u.Id} · {u.AuthMethod.ToLabel()} · {u.Role.ToLabel()} · PIN {(u.HasPin ? "설정됨" : "미설정")}"
            : "게스트";

    // ── 로그 ──
    /// <summary>로그 폴더 절대 경로(표시·수동 탐색용).</summary>
    public string LogFolderPath => _logFolder.LogFolderPath;

    // ── 오픈소스 라이선스 (it22 §5.1 1-6) ──
    /// <summary>라이선스 고지 폴더 절대 경로(열기 실패 시 수동 탐색용).</summary>
    public string LicenseFolderPath => _licenseFolder.LicenseFolderPath;

    /// <summary>
    /// 고지 폴더가 배포물에 실제로 있는지. false면 **라이선스 위반 상태로 배포된 것**이므로
    /// 경로만 보여주고 끝내지 않고 화면에 경고를 띄운다(운영자가 즉시 알아야 한다).
    /// </summary>
    public bool HasLicenseFolder => _licenseFolder.Exists;

    /// <summary>고지 폴더 누락 경고 노출 여부(바인딩 편의 — HasLicenseFolder의 반전).</summary>
    public bool IsLicenseFolderMissing => !_licenseFolder.Exists;

    // ── 개발자 문의 ──
    /// <summary>개발자 연락처(고정값). 문의 메일에 아래 버전·빌드일·웹 배포일을 함께 적도록 안내한다.</summary>
    public const string DeveloperEmailAddress = "devmcjo@gmail.com";

    /// <summary>바인딩용 개발자 메일 주소.</summary>
    public string DeveloperEmail => DeveloperEmailAddress;

    /// <summary>앱 버전(어셈블리 버전 리소스, 예: "1.1.6"). 확인 불가 시 "0.0.0". (it18)</summary>
    public string AppVersion => _buildInfo.Version;

    /// <summary>
    /// 앱 빌드 시각(exe 최종 수정 시각, 예: "2026-07-30 16:42" — 로컬). 값이 없으면 <see cref="Unknown"/>.
    /// it18: 종전 bldinfo.ini의 날짜 문자열(수동 관리)에서 exe 타임스탬프로 바꿔 시각까지 표기한다.
    /// </summary>
    public string AppBuildDate =>
        string.IsNullOrWhiteSpace(_buildInfo.BuildDate) ? Unknown : _buildInfo.BuildDate;

    /// <summary>웹 배포일 조회 진행 중(진입 시 1회 + 사용자가 다시 확인).</summary>
    [ObservableProperty] private bool _isCheckingWebDeploy;

    /// <summary>
    /// 최종 웹 배포 시각 표기(로컬 시간 "yyyy-MM-dd HH:mm"). 서버(GET /health deployedAt) 조회 결과이며
    /// 미구성·미도달·미제공이면 <see cref="Unknown"/>. 앱과 웹은 따로 배포되므로 빌드일과 다를 수 있다.
    /// </summary>
    [ObservableProperty] private string _webDeployDate = Unknown;

    /// <summary>복사 결과 안내(모달은 진입마다 새 VM이라 잔존 없음).</summary>
    [ObservableProperty] private string _copyNotice = string.Empty;

    /// <summary>카메라 재검사(백그라운드 열거 — UI 블로킹 방지). 진입 시 다이얼로그 서비스가 1회 호출.</summary>
    [RelayCommand]
    private async Task RefreshCameras()
    {
        if (IsCheckingCamera) return;
        IsCheckingCamera = true;
        try
        {
            // EnumerateDevices()는 장치 0~7 open/close(수백 ms~초) → Task.Run 백그라운드(SettingsViewModel과 동일 패턴).
            var devices = await Task.Run(() => _camera.EnumerateDevices());
            Cameras.Clear();
            foreach (var d in devices) Cameras.Add(d);
            CameraCount = Cameras.Count;
            HasCamera = CameraCount > 0;
            CameraSummary = HasCamera ? $"{CameraCount}대 연결됨" : "미연결";
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "진단 카메라 열거 실패");
            Cameras.Clear();
            CameraCount = 0;
            HasCamera = false;
            CameraSummary = "미연결";
        }
        finally { IsCheckingCamera = false; }
    }

    /// <summary>로그 폴더를 탐색기로 열기(best-effort, 실패해도 크래시 없음).</summary>
    [RelayCommand]
    private void OpenLogFolder() => _logFolder.OpenLogFolder();

    /// <summary>라이선스 고지 폴더를 탐색기로 열기(best-effort). 폴더가 없으면 아무 일도 하지 않는다.</summary>
    [RelayCommand]
    private void OpenLicenseFolder() => _licenseFolder.OpenLicenseFolder();

    /// <summary>
    /// 최종 웹 배포일 조회(서버 GET /health). 진입 시 다이얼로그 서비스가 1회 호출하고,
    /// 카드의 "다시 확인" 버튼이 재호출한다. 실패는 예외가 아니라 <see cref="Unknown"/> 표기로 끝난다.
    /// </summary>
    [RelayCommand]
    private async Task RefreshWebDeployDate()
    {
        if (IsCheckingWebDeploy) return;
        IsCheckingWebDeploy = true;
        try
        {
            using var cts = new CancellationTokenSource(WebDeployProbeTimeout);
            var deployedAt = await _serverDeploy.GetWebDeployedAtAsync(cts.Token).ConfigureAwait(true);
            // 서버는 UTC로 준다 → 운영자가 읽는 로컬 시간으로 변환(숫자 포맷이라 invariant 안전).
            WebDeployDate = deployedAt is { } utc
                ? utc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
                : Unknown;
        }
        catch (Exception ex)
        {
            // HTTP 구현은 자체 폴백하지만, 다른 구현이 던져도 진단 화면은 열려 있어야 한다.
            _logger?.LogWarning(ex, "웹 배포일 조회 실패");
            WebDeployDate = Unknown;
        }
        finally { IsCheckingWebDeploy = false; }
    }

    /// <summary>개발자 메일 주소를 클립보드에 복사(best-effort). 실패 시 직접 선택·복사하도록 안내.</summary>
    [RelayCommand]
    private void CopyDeveloperEmail()
        => CopyNotice = _clipboard.TrySetText(DeveloperEmailAddress)
            ? "메일 주소를 복사했습니다."
            : "복사에 실패했습니다. 위 주소를 직접 선택해 복사하세요.";
}
