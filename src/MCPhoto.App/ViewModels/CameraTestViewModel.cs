using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MCPhoto.App.Services;
using MCPhoto.Core.Capture;
using MCPhoto.Core.Devices;
using MCPhoto.Core.Settings;
using Microsoft.Extensions.Logging;

namespace MCPhoto.App.ViewModels;

/// <summary>
/// 카메라 설정 테스트 모달 VM. 실제 촬영과 동일한 프리뷰·플래시·셔터를 재현하되 저장은 하지 않는다. (it9 §2.2 C1)
/// 카메라(ICameraService)는 DI Singleton 공유 — 오픈 시 StopAsync→StartAsync(선택 인덱스), 닫기 시 StopAsync.
/// <para>
/// it23: 모달 상단에 <b>장치 목록</b>이 생겼다(요구 4). 웹캠 항목은 "타임랩스·프리뷰 확인", 외부 카메라 항목은
/// "카메라 세팅 확인 + 셔터 동작 테스트"다 — 같은 창에서 둘을 오가며 비교할 수 있다.
/// </para>
/// </summary>
public sealed partial class CameraTestViewModel : ObservableObject
{
    /// <summary>첫 안정 프레임 대기 타임아웃(무한 로딩 방지, CaptureViewModel과 동일).</summary>
    private const int CameraReadyTimeoutMs = 8000;

    /// <summary>셔터 테스트 결과를 화면에 남겨 두는 시간(이후 폐기 — 저장하지 않는다).</summary>
    private const int ShotPreviewMs = 3000;

    private readonly ICameraService _camera;
    private readonly ISettingsService _settings;
    private readonly IExternalCamera _external;
    private readonly ILogger? _logger;
    private bool _shooting;
    private bool _switching;

    /// <summary>View가 프리뷰 렌더(CameraFramePresenter)를 위해 구독.</summary>
    public ICameraService Camera => _camera;

    [ObservableProperty] private bool _flashActive;
    [ObservableProperty] private bool _isLoading = true;
    [ObservableProperty] private string _loadingMessage = "카메라 준비 중…";
    /// <summary>셔터 후 잠깐 노출되는 안내(저장되지 않음 재확인).</summary>
    [ObservableProperty] private string _shotNotice = string.Empty;

    // ── it23: 장치 목록 + 외부 카메라 정보 패널 ──

    /// <summary>장치 목록(웹캠 + 외부 카메라 1항목). 외부 항목은 설정이 on일 때만 들어간다(§9.3).</summary>
    public ObservableCollection<CameraTestTarget> Targets { get; } = new();

    /// <summary>
    /// 현재 선택 항목. 값 기반 선택이다 — <c>SelectedIndex</c>로 바인딩하면 목록이 채워지는 순간
    /// 초기 선택이 0으로 덮인다(it7 B9 사고 이력).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsExternalSelected), nameof(IsWebcamSelected), nameof(PurposeLabel))]
    private CameraTestTarget? _selectedTarget;

    /// <summary>외부 카메라 항목이 선택됐는지(정보 패널·셔터 테스트 노출 조건).</summary>
    public bool IsExternalSelected => SelectedTarget?.IsExternal == true;

    /// <summary>웹캠 항목이 선택됐는지(프리뷰·테스트 셔터 노출 조건).</summary>
    public bool IsWebcamSelected => SelectedTarget is { IsExternal: false };

    /// <summary>선택 항목의 확인 목적 라벨(§9.3 — 두 항목의 목적이 다르다).</summary>
    public string PurposeLabel => IsExternalSelected
        ? "외부 카메라 — 카메라 세팅 확인 · 셔터 동작 테스트"
        : "웹캠 — 타임랩스·프리뷰 확인";

    /// <summary>외부 카메라 모델 표시명(미연결이면 빈 문자열).</summary>
    [ObservableProperty] private string _externalModelName = string.Empty;

    /// <summary>배터리 표시(조회 실패·미지원이면 "확인 불가").</summary>
    [ObservableProperty] private string _externalBatteryText = string.Empty;

    /// <summary>capability 요약(항목별 지원 상태·사유 문구 포함).</summary>
    public ObservableCollection<string> ExternalCapabilityLines { get; } = new();

    /// <summary>외부 카메라 상태 문구(미연결 사유 등). 빈 문자열이면 정상.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasExternalStatus))]
    private string _externalStatus = string.Empty;

    /// <summary>외부 카메라 상태 문구 표시 여부.</summary>
    public bool HasExternalStatus => !string.IsNullOrEmpty(ExternalStatus);

    /// <summary>외부 카메라가 연결돼 셔터 테스트·노출 조정이 가능한지.</summary>
    [ObservableProperty] private bool _isExternalConnected;

    /// <summary>
    /// 셔터 테스트로 수신한 이미지(인코딩 바이트). <b>정규화하지 않은 원본</b>이다 —
    /// 이 화면의 목적은 카메라 자체 확인이지 합성 미리보기가 아니다(§9.3).
    /// 3초 후 null로 되돌려 폐기한다(저장 없음 — 현행 모달 원칙 유지).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasShotImage))]
    private byte[]? _shotImageBytes;

    /// <summary>셔터 테스트 결과 이미지 표시 여부.</summary>
    public bool HasShotImage => ShotImageBytes is { Length: > 0 };

    /// <summary>노출 3요소 편집 행(설정 화면과 같은 VM 타입 재사용 — 규칙 복제 금지).</summary>
    public IReadOnlyList<ExposureParameterViewModel> ExposureParameters { get; }

    private readonly ExposureParameterViewModel _shutterSpeed = new(ExposureParameter.ShutterSpeed, "셔터 속도");
    private readonly ExposureParameterViewModel _aperture = new(ExposureParameter.Aperture, "조리개");
    private readonly ExposureParameterViewModel _iso = new(ExposureParameter.Iso, "ISO");

    /// <summary>View(Window)가 구독해 실제 창을 닫는다(VM은 Window 미참조).</summary>
    public event Action? RequestClose;

    public CameraTestViewModel(ICameraService camera, ISettingsService settings,
        IExternalCamera external, CameraTestTarget initialTarget, ILogger? logger = null)
    {
        _camera = camera;
        _settings = settings;
        _external = external;
        _logger = logger;
        _initialTarget = initialTarget;
        ExposureParameters = new[] { _shutterSpeed, _aperture, _iso };

        // ⚠️ 임의 스레드에서 오는 탈락 통지를 받아 화면을 갱신한다(§11 E5: 모달이 열려 있는 동안 USB가
        //    뽑히면 "연결됨"이 그대로 남아 셔터 테스트가 무한 실패한다). 해제는 StopAsync가 담당한다.
        _dispatcher = Dispatcher.CurrentDispatcher;
        _external.ConnectionChanged += OnExternalConnectionChanged;
    }

    private readonly CameraTestTarget _initialTarget;
    private readonly Dispatcher _dispatcher;
    private bool _connectionSubscribed = true;

    /// <summary>
    /// 모달 오픈 시 호출: 장치 목록 구성 → 초기 선택 적용.
    /// 목록에 외부 항목이 있어도 <b>선택하지 않으면 연결을 시도하지 않는다</b>(§9.3 trigger).
    /// </summary>
    public async Task StartAsync()
    {
        BuildTargets();

        // 초기 선택은 값 기반 매칭(같은 인덱스의 웹캠 또는 외부 항목). 없으면 첫 항목.
        var initial = Targets.FirstOrDefault(t => t == _initialTarget)
                      ?? Targets.FirstOrDefault(t => !t.IsExternal && t.DeviceIndex == _initialTarget.DeviceIndex)
                      ?? Targets.FirstOrDefault();

        if (initial is null)
        {
            IsLoading = true;
            LoadingMessage = "사용할 수 있는 카메라가 없습니다.";
            return;
        }

        // SelectedTarget 대입이 전환 처리를 유발하므로 여기서 await로 완료를 기다린다.
        SelectedTarget = initial;
        await ApplyTargetAsync(initial);
    }

    /// <summary>
    /// 장치 목록 구성: 웹캠 열거 결과 + (설정 on일 때만) 외부 카메라 1항목.
    /// ⚠️ 설정이 off면 외부 항목이 <b>존재하지 않는다</b> — 목록·동작이 현행과 완전히 동일해야 한다(회귀 0).
    /// </summary>
    private void BuildTargets()
    {
        Targets.Clear();
        try
        {
            foreach (var d in _camera.EnumerateDevices())
                Targets.Add(CameraTestTarget.Webcam(d));
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "카메라 테스트: 장치 열거 실패(외부 항목만 표시될 수 있음)");
        }

        var s = _settings.Current;
        if (s.ExternalCameraEnabled)
            Targets.Add(CameraTestTarget.External(ExternalCameraModels.Resolve(s.ExternalCameraModel)));
    }

    /// <summary>목록에서 항목을 고르면 호출(View의 SelectionChanged → 커맨드).</summary>
    [RelayCommand]
    private async Task SelectTarget(CameraTestTarget? target)
    {
        if (target is null || _switching) return;
        await ApplyTargetAsync(target);
    }

    private async Task ApplyTargetAsync(CameraTestTarget target)
    {
        if (_switching) return;
        _switching = true;
        try
        {
            // ⚠️ 선택 상태를 여기서 확정한다. ComboBox 두방향 바인딩에만 맡기면 커맨드 경로(코드·테스트)로
            //    전환했을 때 화면 분기(IsExternalSelected 등)가 이전 항목에 머문다.
            SelectedTarget = target;
            ShotImageBytes = null;
            ShotNotice = string.Empty;
            if (target.IsExternal) await EnterExternalAsync();
            else await EnterWebcamAsync(target.DeviceIndex);
        }
        finally { _switching = false; }
    }

    /// <summary>웹캠 항목: 현행 동작 그대로(Stop→Start(인덱스) → 안정 프리뷰 대기).</summary>
    private async Task EnterWebcamAsync(int deviceIndex)
    {
        IsLoading = true;
        LoadingMessage = "카메라 준비 중…";
        try
        {
            // ⚠️ StartAsync는 running이면 파라미터를 무시한다 → 전환은 반드시 Stop 선행(F13).
            await _camera.StopAsync();
            var s = _settings.Current;
            bool ok = await _camera.StartAsync(deviceIndex, 3.0 / 4.0, s.MirrorMode);
            if (!ok)
            {
                LoadingMessage = "카메라를 열 수 없습니다.";
                _logger?.LogWarning("카메라 테스트: 장치 {Index} 열기 실패", deviceIndex);
                return; // IsLoading=true 유지(오버레이에 실패 문구)
            }

            bool ready = await WaitForStablePreviewAsync(CameraReadyTimeoutMs);
            if (!ready)
            {
                LoadingMessage = "카메라 준비에 실패했습니다.";
                _logger?.LogWarning("카메라 테스트: 안정적 프리뷰 타임아웃(장치 {Index})", deviceIndex);
                return;
            }
            IsLoading = false;
        }
        catch (Exception ex)
        {
            LoadingMessage = "카메라 준비 중 오류가 발생했습니다.";
            _logger?.LogError(ex, "카메라 테스트 시작 오류");
        }
    }

    /// <summary>
    /// 외부 카메라 항목: 웹캠 프리뷰를 <b>먼저 정지</b>(Singleton 반납, F13)한 뒤 연결한다.
    /// 순서가 뒤바뀌면 두 장치를 동시에 열려는 시도가 되어(설계 A8 미검증) 실패 원인을 특정할 수 없다.
    /// </summary>
    private async Task EnterExternalAsync()
    {
        IsLoading = true;
        LoadingMessage = "외부 카메라 연결 중…";
        IsExternalConnected = false;
        ExternalCapabilityLines.Clear();
        ExternalModelName = string.Empty;
        ExternalBatteryText = string.Empty;
        ExternalStatus = string.Empty;

        try
        {
            await _camera.StopAsync();

            bool connected = await _external.ConnectAsync();
            if (!connected)
            {
                ExternalStatus = _external.UnavailableReason ?? "카메라가 연결되지 않았습니다 (USB·전원 확인)";
                LoadingMessage = ExternalStatus;
                _logger?.LogInformation("카메라 테스트: 외부 카메라 연결 실패 — {Reason}", ExternalStatus);
                // 정보 패널을 보여 주기 위해 오버레이는 내린다([다시 연결]로 재시도 가능).
                IsLoading = false;
                return;
            }

            IsExternalConnected = true;
            ExternalModelName = _external.ModelName ?? string.Empty;

            var caps = await _external.GetCapabilitiesAsync();
            BuildCapabilityLines(caps);
            ExternalBatteryText = caps?.BatteryLevelPercent is { } pct ? $"{pct}%" : "확인 불가";

            await RefreshExposureDomainAsync();
            IsLoading = false;
        }
        catch (Exception ex)
        {
            // 장치 계층은 예외를 던지지 않도록 만들어져 있지만, 모달이 죽으면 설정 화면째 얼어붙는다 → 최후 방어.
            _logger?.LogWarning(ex, "카메라 테스트: 외부 카메라 준비 중 예외");
            ExternalStatus = "외부 카메라 준비 중 오류가 발생했습니다.";
            IsLoading = false;
        }
    }

    /// <summary>[다시 연결]: 외부 카메라 연결 재시도(§11 E4의 탈출 경로).</summary>
    [RelayCommand]
    private async Task ReconnectExternal()
    {
        if (_switching || !IsExternalSelected) return;
        _switching = true;
        try { await EnterExternalAsync(); }
        finally { _switching = false; }
    }

    /// <summary>capability 요약 줄 구성 — Supported/Unsupported/Unknown의 사유 문구가 다르다(§4.1).</summary>
    private void BuildCapabilityLines(ExternalCameraCapabilities? caps)
    {
        ExternalCapabilityLines.Clear();
        if (caps is null)
        {
            ExternalCapabilityLines.Add("기능 지원 여부를 확인하지 못했습니다");
            return;
        }

        void Add(string label, CapabilityState state)
        {
            var suffix = ExternalCapturePolicy.IsOpen(state)
                ? "지원"
                : ExternalCapturePolicy.DescribeClosed(state);
            ExternalCapabilityLines.Add($"{label}: {suffix}");
        }

        Add("스틸 촬영", caps.StillCapture);
        Add("노출 제어", caps.ExposureControl);
        Add("물리 플래시", caps.PhysicalFlash);
        // LiveView·동영상은 이번 이터레이션의 비목표지만 진단 목적으로 값만 노출한다(§4.2).
        Add("LiveView", caps.LiveView);
        Add("동영상 녹화", caps.VideoRecord);
    }

    private async Task RefreshExposureDomainAsync()
    {
        ExposureDomain? domain = null;
        try { domain = await _external.GetExposureDomainAsync(); }
        catch (Exception ex) { _logger?.LogWarning(ex, "카메라 테스트: 노출 도메인 조회 실패"); }

        var s = _settings.Current;
        _shutterSpeed.SetDomain(domain?.ShutterSpeed, s.ExternalShutterSpeed);
        _aperture.SetDomain(domain?.Aperture, s.ExternalAperture);
        _iso.SetDomain(domain?.Iso, s.ExternalIso);
    }

    /// <summary>
    /// [노출 적용]: 3행의 현재 값을 카메라에 쓴다.
    /// <para>
    /// ⚠️ 슬라이더를 움직이는 즉시 쓰지 않는 이유: 드래그 한 번에 SDK 쓰기가 수십 번 발생하고,
    /// 그 왕복이 실패하면 어느 값이 적용됐는지 알 수 없게 된다. 명시 적용이 관측 가능하다.
    /// </para>
    /// 실패(도메인 불일치·쓰기 거부)는 해당 행 힌트로만 알린다 — 카메라 현재값이 유지되고 테스트는 계속된다(§11 E9).
    /// </summary>
    [RelayCommand]
    private async Task ApplyExposure()
    {
        if (!IsExternalConnected) return;

        var failed = new List<ExposureParameterViewModel>();
        foreach (var p in ExposureParameters)
        {
            if (string.IsNullOrWhiteSpace(p.Text)) continue;   // 미지정은 건너뛴다
            try
            {
                if (!await _external.SetExposureAsync(p.Parameter, p.Text))
                    failed.Add(p);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "카메라 테스트: 노출 적용 실패({Parameter})", p.Parameter);
                failed.Add(p);
            }
        }

        // 적용 후 카메라가 보고하는 현재값으로 갱신(카메라가 근처 값으로 스냅했을 수 있다).
        await RefreshExposureDomainAsync();

        // ⚠️ 실패 힌트는 도메인 갱신 **뒤에** 다시 세운다. 갱신이 힌트를 도메인 기준으로 재계산하므로
        //    (값이 목록에 있으면 힌트 없음) 순서를 바꾸면 쓰기 실패가 화면에서 조용히 사라진다 —
        //    운영자는 적용된 줄 알고 넘어가게 된다(§11 E9).
        foreach (var p in failed)
            p.Hint = ExposureParameterViewModel.UnsupportedValueHint;
    }

    /// <summary>테스트 셔터: 플래시 옵션 확인 후 재현, 스틸은 캡처하되 저장/합성 없이 폐기. (요구 §1)</summary>
    [RelayCommand]
    private async Task ShootTest()
    {
        if (_shooting || IsLoading) return;
        _shooting = true;
        try
        {
            if (_settings.Current.FlashMode)
            {
                FlashActive = true;
                // it23 §4.3: 이중 발광 경로를 실촬영과 동일하게 재현한다(게이트가 닫혀 있으면 false로 무해).
                if (IsExternalSelected && IsExternalConnected)
                    await _external.TrySetPhysicalFlashAsync(true);
                await Task.Delay(120);
            }
            if (_settings.Current.ShutterSound) SoundEffects.PlayShutter(); // 실촬영과 동일 재현

            if (IsExternalSelected)
            {
                if (!IsExternalConnected)
                {
                    FlashActive = false;
                    ShotNotice = "외부 카메라가 연결되지 않았습니다";
                    return;
                }

                var bytes = await _external.CaptureAsync();
                FlashActive = false;
                if (bytes is null || bytes.Length == 0)
                {
                    ShotNotice = "셔터 테스트 실패 — 카메라 상태를 확인해 주세요";
                    await Task.Delay(ShotPreviewMs);
                    ShotNotice = string.Empty;
                    return;
                }

                // 원본 비율 그대로 보여 준 뒤 폐기(저장 없음).
                ShotImageBytes = bytes;
                ShotNotice = "셔터 테스트 완료 · 저장되지 않았습니다";
                await Task.Delay(ShotPreviewMs);
                ShotImageBytes = null;
                ShotNotice = string.Empty;
                return;
            }

            var still = await _camera.CaptureStillAsync(); // 결과 폐기(저장 안 함)
            _ = still;
            FlashActive = false;

            ShotNotice = "테스트 촬영 완료 · 저장되지 않았습니다";
            await Task.Delay(1500);
            ShotNotice = string.Empty;
        }
        catch (Exception ex)
        {
            FlashActive = false;
            _logger?.LogWarning(ex, "카메라 테스트 촬영 오류(무시)");
        }
        finally { _shooting = false; }
    }

    [RelayCommand]
    private void Close() => RequestClose?.Invoke();

    /// <summary>
    /// 모달 닫힘 시 호출: 웹캠 정지 + 외부 카메라 해제.
    /// <para>
    /// ⚠️ 외부→웹캠 <b>전환</b>에서는 해제하지 않는다(재연결 비용 회피, §9.3). 닫을 때만 끊는다.
    /// </para>
    /// </summary>
    public async Task StopAsync()
    {
        // 구독 해제(생성자 구독의 대칭). 외부 카메라는 Singleton이라 해제하지 않으면 닫힌 모달의 VM이
        // 계속 붙잡혀 있는다 — 모달을 여닫을수록 죽은 구독자가 쌓인다.
        if (_connectionSubscribed)
        {
            _external.ConnectionChanged -= OnExternalConnectionChanged;
            _connectionSubscribed = false;
        }

        try { await _camera.StopAsync(); }
        catch (Exception ex) { _logger?.LogWarning(ex, "카메라 테스트 정지 오류(무시)"); }

        try { await _external.DisconnectAsync(); }
        catch (Exception ex) { _logger?.LogWarning(ex, "카메라 테스트 외부 카메라 해제 오류(무시)"); }
    }

    /// <summary>
    /// 외부 카메라 연결 상태 변화(임의 스레드) → UI 갱신.
    /// <para>
    /// ⚠️ Dispatcher 마샬링이 필수다: 이 이벤트는 SDK 콜백 스레드에서 올라온다는 것이 계약이고,
    /// <c>ObservableCollection</c>·바인딩 대상 속성을 다른 스레드에서 만지면 런타임 예외가 난다.
    /// 같은 스레드(테스트·UI 스레드 발화)면 인라인 실행해 관측 가능성을 유지한다.
    /// </para>
    /// </summary>
    private void OnExternalConnectionChanged(object? sender, ExternalCameraConnectionChange change)
    {
        if (_dispatcher.CheckAccess()) Apply();
        else _dispatcher.InvokeAsync(Apply);

        void Apply()
        {
            if (change.IsConnected) return;   // 연결 성립은 EnterExternalAsync가 이미 반영했다

            IsExternalConnected = false;
            ExternalCapabilityLines.Clear();
            ExternalBatteryText = string.Empty;
            ExternalStatus = change.Reason ?? "카메라가 연결되지 않았습니다 (USB·전원 확인)";
            _logger?.LogInformation("카메라 테스트: 외부 카메라 탈락 — {Reason}", ExternalStatus);
        }
    }

    /// <summary>안정적 프리뷰(연속 N프레임 + 최소 경과) 대기. CaptureViewModel과 동일 규칙(PreviewReadiness 재사용).</summary>
    private async Task<bool> WaitForStablePreviewAsync(int timeoutMs)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var readiness = new PreviewReadiness();
        var sw = System.Diagnostics.Stopwatch.StartNew();

        void OnFrame(object? s, CameraFrame f)
        {
            if (readiness.OnFrame(sw.Elapsed.TotalMilliseconds, _camera.CurrentFps))
                tcs.TrySetResult(true);
        }
        _camera.FrameReady += OnFrame;
        try
        {
            var completed = await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs));
            return completed == tcs.Task;
        }
        finally { _camera.FrameReady -= OnFrame; }
    }
}
