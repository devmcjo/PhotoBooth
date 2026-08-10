using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MCPhoto.Capture;
using MCPhoto.Core.Capture;
using MCPhoto.Core.Devices;
using MCPhoto.Core.Navigation;
using Microsoft.Extensions.Logging;

namespace MCPhoto.App.ViewModels;

/// <summary>카메라 준비 상태. (it3 §7 U4)</summary>
public enum CameraLoadState
{
    Initializing, // StartAsync 호출~첫 프레임 대기(로딩 표시)
    Ready,        // 첫 프레임 수신(프리뷰 준비 완료)
    Failed        // 장치 없음/열기 실패/타임아웃
}

/// <summary>
/// 촬영/카운트다운. N컷 연속 촬영(컷별 카운트다운 → 자동 셔터), [바로촬영], 플래시, 세션 녹화. (PRD §F1/F3)
/// 카운트다운 타이머는 async 루프로 구동, [바로촬영]은 남은 시간 스킵.
/// 카메라 준비 전 로딩 표시 + 카운트다운 Ready 게이트. (it3 §7)
/// <para>
/// it23: 외부 카메라(DSLR) 스틸 소스를 지원한다. 프리뷰·타임랩스는 <b>항상 웹캠 전담</b>이고
/// 스틸만 DSLR로 갈아탄다(설계 §6). 소스는 세션 시작 시 1회 확정되며 실패 강등에서만 바뀐다.
/// </para>
/// ⚠️ <c>ExternalCameraEnabled=false</c>(기본값)에서는 외부 카메라를 <b>단 한 번도 만지지 않는다</b> —
/// 기존 웹캠 경로의 회귀 0이 이 이터레이션의 최우선 제약이다.
/// </summary>
public sealed partial class CaptureViewModel : ViewModelBase
{
    /// <summary>첫 프레임 대기 타임아웃(무한 로딩 방지, R5).</summary>
    private const int CameraReadyTimeoutMs = 8000;

    /// <summary>이 세션의 스틸 소스. 세션 시작 시 1회 확정 — 컷마다 재판정하지 않는다(설계 §6.1).</summary>
    private enum StillSource
    {
        /// <summary>웹캠 프리뷰 프레임이 곧 스틸(현행 WYSIWYG).</summary>
        Webcam,

        /// <summary>DSLR 셔터 → JPEG 수신 → 웹캠과 동일 규칙 정규화.</summary>
        External
    }

    private readonly AppShellViewModel _shell;
    private readonly ICameraService _camera;
    private readonly IExternalCamera _external;
    private readonly ExternalStillDecoder _decoder;
    private readonly ILogger<CaptureViewModel>? _logger;

    private CancellationTokenSource? _countdownCts;
    private CancellationTokenSource? _sessionCts;

    // ── it23 세션 확정값(컷 루프가 읽는다) ──
    private StillSource _source = StillSource.Webcam;
    private double _slotAspect = 3.0 / 4.0;
    private bool _mirror;
    /// <summary>웹캠이 실제로 열렸는지 — 외부 카메라 실패 시 강등 대상이 있는지의 판정 기준(§6.4).</summary>
    private bool _webcamRunning;
    /// <summary>물리 플래시 이중 발광 게이트(capability가 Supported일 때만 열린다, §4.3).</summary>
    private bool _physicalFlashOpen;

    [ObservableProperty] private int _currentCut;
    [ObservableProperty] private int _totalCuts;
    [ObservableProperty] private int _remainingSeconds;
    [ObservableProperty] private bool _cameraAvailable = true;
    [ObservableProperty] private bool _flashActive;
    [ObservableProperty] private string _statusMessage = string.Empty;

    /// <summary>카메라 준비 상태(로딩 오버레이·시퀀스 게이트).</summary>
    [ObservableProperty] private CameraLoadState _cameraState = CameraLoadState.Initializing;

    // ── it23 UI 상태 ──

    /// <summary>
    /// 외부 카메라로 진행되는 세션인지(상시 배지 W4). 프리뷰와 결과물이 다른 광학계에서 온다는 사실은
    /// 세션 내내 참이므로, 촬영 순간마다 안내하지 않고 배지를 상시 노출한다(§5.4 고지 ②).
    /// </summary>
    [ObservableProperty] private bool _isExternalSource;

    /// <summary>DSLR 셔터 후 이미지 수신 대기 중(오버레이 W5). 카운트다운 숫자 대신 표시된다.</summary>
    [ObservableProperty] private bool _isReceiving;

    /// <summary>
    /// 프리뷰 부재 모드(W8). 외부 소스 + 웹캠 없음 — 촬영은 정상 진행되고 타임랩스만 없다(§6.5).
    /// <c>CameraLoadState</c>에 새 국면을 추가하지 않는 이유: Failed는 "촬영 불가"를 뜻하는데
    /// 이 상태는 촬영이 가능하다. 국면을 늘리면 기존 게이트 전부를 재검토해야 한다.
    /// </summary>
    [ObservableProperty] private bool _previewAbsent;

    /// <summary>
    /// 강등 배너 문구(W6/W7). 빈 문자열이면 미표시.
    /// 세션 잔여 기간 내내 유지한다 — 앞 컷과 뒤 컷의 화질이 달라진 사실의 고지다(§6.4).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDegradeBanner))]
    private string _degradeBanner = string.Empty;

    /// <summary>강등 배너 표시 여부(문구가 있을 때만).</summary>
    public bool HasDegradeBanner => !string.IsNullOrEmpty(DegradeBanner);

    /// <summary>View가 프리뷰 렌더를 위해 구독.</summary>
    public ICameraService Camera => _camera;

    public CaptureViewModel(AppShellViewModel shell, ICameraService camera,
        IExternalCamera external, ExternalStillDecoder decoder,
        ILogger<CaptureViewModel>? logger = null)
    {
        _shell = shell;
        _camera = camera;
        _external = external;
        _decoder = decoder;
        _logger = logger;
    }

    public override async Task OnEnterAsync()
    {
        var session = _shell.Session;
        var settings = _shell.Settings.Current;
        var frame = session.SelectedFrame;
        if (frame is null) { _shell.ReturnHome("프레임 없음"); return; }

        TotalCuts = session.Capture.CutCount;
        CameraState = CameraLoadState.Initializing;

        // 대표 슬롯 종횡비로 크롭
        double aspect = frame.Slots.Count > 0 ? frame.Slots[0].AspectRatio : 3.0 / 4.0;
        _slotAspect = aspect;
        _mirror = settings.MirrorMode;

        // ── it23 §6.1: 세션 스틸 소스 확정(1회). off면 외부 카메라를 만지지 않는다. ──
        _source = StillSource.Webcam;
        IsExternalSource = false;
        _physicalFlashOpen = false;
        DegradeBanner = string.Empty;
        PreviewAbsent = false;
        if (settings.ExternalCameraEnabled)
            await ResolveExternalSourceAsync();

        bool ok = await _camera.StartAsync(settings.CameraDevice, aspect, settings.MirrorMode);
        _webcamRunning = ok;

        if (_source == StillSource.Webcam)
        {
            // ── 현행 경로 그대로(회귀 0) ──
            CameraAvailable = ok;
            if (!ok)
            {
                CameraState = CameraLoadState.Failed;
                StatusMessage = "카메라를 찾을 수 없습니다.";
                _logger?.LogWarning("촬영 화면: 카메라 미연결");
                return;
            }

            // 안정적 프리뷰(연속 N프레임+최소 경과) 대기 → Ready. 타임아웃 시 Failed(무한 로딩 방지). (it8 §7 A7)
            bool ready = await WaitForStablePreviewAsync(CameraReadyTimeoutMs);
            if (!ready)
            {
                CameraState = CameraLoadState.Failed;
                StatusMessage = "카메라 준비에 실패했습니다.";
                _logger?.LogWarning("촬영 화면: 안정적 프리뷰 타임아웃");
                return;
            }
            CameraState = CameraLoadState.Ready;
        }
        else
        {
            // ── 외부 소스(§6.5): Ready 게이트는 DSLR 연결 성공으로 이미 충족됐다.
            //    웹캠은 프리뷰·타임랩스 담당이므로 실패해도 세션을 막지 않는다. ──
            CameraAvailable = true;
            if (ok)
            {
                // 프리뷰 안정화를 기다리되, 실패는 "프리뷰 없음"으로만 강등한다(촬영은 DSLR이 한다).
                PreviewAbsent = !await WaitForStablePreviewAsync(CameraReadyTimeoutMs);
                if (PreviewAbsent)
                    _logger?.LogWarning("촬영 화면: 외부 소스 — 웹캠 프리뷰 준비 실패(프리뷰 없이 진행)");
            }
            else
            {
                PreviewAbsent = true;
                _webcamRunning = false;
                _logger?.LogInformation("촬영 화면: 외부 소스 — 웹캠 없음(프리뷰·타임랩스 없이 진행)");
            }
            CameraState = CameraLoadState.Ready;
        }

        // 세션 작업 폴더 준비(sessions\{guid} — 종료 시 Reset이 삭제, 시작 시 잔재 정리, it6 #3)
        session.WorkFolder = Path.Combine(
            Core.Capture.SessionWorkspace.SessionsRoot(App.DataFolder), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(session.WorkFolder);
        // 프리뷰가 없으면 녹화할 프레임 자체가 없다 → 경로를 만들지 않는다(§6.5).
        // ⚠️ 존재하지 않는 파일 경로를 남기면 결과 화면이 타임랩스 생성을 시도한다 —
        //    null이면 기존 "타임랩스 없는 세션" 경로가 그대로 적용된다(it7 F3).
        session.SessionVideoPath = PreviewAbsent
            ? null
            : Path.Combine(session.WorkFolder, "session.mp4");
        session.SessionTime = DateTime.Now;

        // 시퀀스는 Ready 이후에만 시작(로딩 중 카운트다운 방지, it3 §7.3)
        _sessionCts = new CancellationTokenSource();
        _ = RunCaptureSequenceAsync(_sessionCts.Token);
    }

    /// <summary>
    /// 외부 카메라 연결 시도 → 소스 확정(§6.1). 실패·미지원이면 웹캠 강등 + 사유 토스트(W7)를 띄우고
    /// 소스를 Webcam으로 남긴다 — <b>세션은 계속된다</b>(키오스크 UX: 게스트 세션을 죽이지 않는다).
    /// </summary>
    private async Task ResolveExternalSourceAsync()
    {
        try
        {
            bool connected = await _external.ConnectAsync();
            var caps = connected ? await _external.GetCapabilitiesAsync() : null;

            if (connected && ExternalCapturePolicy.IsOpen(caps?.StillCapture ?? CapabilityState.Unknown))
            {
                _source = StillSource.External;
                IsExternalSource = true;
                // 물리 플래시는 Supported로 확인된 경우에만 열린다 — 현재 프로덕션에서는 항상 닫혀 있고
                // 화면 플래시가 유일한 활성 경로다(§4.3).
                _physicalFlashOpen = ExternalCapturePolicy.IsOpen(caps?.PhysicalFlash ?? CapabilityState.Unknown);
                _logger?.LogInformation("촬영 세션 스틸 소스 = 외부 카메라({Model})", _external.ModelName);
                return;
            }

            // 사유 우선순위: 어댑터가 확정한 사용 불가 사유 → capability 게이트 사유 → 미연결.
            var reason = _external.UnavailableReason
                         ?? ExternalCapturePolicy.DescribeClosed(caps?.StillCapture ?? CapabilityState.Unknown)
                         ?? "외부 카메라를 사용할 수 없습니다";
            _shell.ShowToast($"외부 카메라를 사용할 수 없어 웹캠으로 촬영합니다 ({reason})");
            _logger?.LogInformation("외부 카메라 강등 → 웹캠 단독: {Reason}", reason);
        }
        catch (Exception ex)
        {
            // 장치 계층은 예외를 던지지 않도록 만들어져 있지만, 여기서 새면 손님 세션이 죽는다 → 최후 방어.
            _logger?.LogWarning(ex, "외부 카메라 소스 확정 중 예외 — 웹캠 단독으로 진행");
            _shell.ShowToast("외부 카메라를 사용할 수 없어 웹캠으로 촬영합니다 (초기화 오류)");
        }
    }

    /// <summary>
    /// 안정적 프리뷰(연속 N프레임 + 최소 경과 + fps>0)까지 대기. (it8 §7 A7)
    /// 첫 프레임 1회로 끝내던 것을 강화 — 실사용 가능 시점까지 waiting 유지. 타임아웃 내 미충족 시 false.
    /// </summary>
    private async Task<bool> WaitForStablePreviewAsync(int timeoutMs)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var readiness = new PreviewReadiness(); // 기본 8프레임 + 500ms
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
        finally
        {
            _camera.FrameReady -= OnFrame;
        }
    }

    private async Task RunCaptureSequenceAsync(CancellationToken ct)
    {
        var session = _shell.Session;
        var settings = _shell.Settings.Current;

        try
        {
            // 세션 전체 녹화 시작(첫 컷 카운트다운 시작 시점)
            if (session.SessionVideoPath is not null)
                _camera.StartRecording(session.SessionVideoPath);

            for (int cut = 1; cut <= TotalCuts; cut++)
            {
                if (ct.IsCancellationRequested) return;
                CurrentCut = cut;

                await CountdownAsync(settings.CountdownSec, ct);
                if (ct.IsCancellationRequested) return;

                // 플래시(셔터 직전 하양 오버레이)
                if (settings.FlashMode)
                {
                    FlashActive = true;
                    // it23 §4.3: 물리 플래시가 Supported로 확인된 경우에만 이중 발광을 시도한다.
                    // 실패(false)해도 시퀀스는 계속된다 — 화면 플래시는 이미 켜져 있다.
                    if (_physicalFlashOpen)
                        await _external.TrySetPhysicalFlashAsync(true, ct);
                    await Task.Delay(120, ct);
                }

                // 셔터음(옵션). 저장 흐름 방해 금지 — 비동기 재생·실패 무시. (기능#7)
                if (settings.ShutterSound) SoundEffects.PlayShutter();

                var still = await CaptureCutAsync(ct);
                if (still is null)
                {
                    // 강등할 웹캠도 없다(§11 E7) — 완성 불가 세션을 컷선택으로 보내지 않는다.
                    FlashActive = false;
                    IsReceiving = false;
                    _shell.ShowToast("촬영을 계속할 수 없습니다 — 카메라를 확인해 주세요");
                    _shell.ReturnHome("외부 카메라 오류");
                    return;
                }

                session.Capture.AddCut(still);
                FlashActive = false;

                await Task.Delay(300, ct); // 컷 간 짧은 간격
            }

            // 녹화 종료(마지막 컷 후)
            await _camera.StopRecordingAsync();

            // 컷 선택 화면으로
            await _shell.NavigateAsync(AppState.CutSelect);
        }
        catch (OperationCanceledException)
        {
            _logger?.LogInformation("촬영 취소됨");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "촬영 시퀀스 오류");
            _shell.ReturnHome("촬영 오류");
        }
        finally
        {
            IsReceiving = false;
        }
    }

    /// <summary>
    /// 한 컷 확보. 소스가 웹캠이면 현행 경로 그대로, 외부면 수신 → 재시도 → 웹캠 강등 순서다(§6.4).
    /// null 반환은 "강등할 웹캠도 없다"는 뜻이며 호출측이 세션을 중단한다.
    /// </summary>
    private async Task<CapturedStill?> CaptureCutAsync(CancellationToken ct)
    {
        if (_source == StillSource.Webcam)
            return await _camera.CaptureStillAsync(ct);

        var still = await TryCaptureExternalAsync(ct);
        if (still is not null) return still;

        // 1회 재시도(재연결 포함). 반복 재시도를 하지 않는 이유: 실패한 장치를 컷마다 다시 기다리면
        // 손님이 타임아웃을 반복 대기한다(§6.4).
        for (int attempt = 0; attempt < ExternalCapturePolicy.CaptureRetryCount; attempt++)
        {
            if (ct.IsCancellationRequested) return null;
            _logger?.LogWarning("외부 카메라 컷 {Cut} 실패 — 재연결 후 재시도", CurrentCut);
            try { await _external.ConnectAsync(ct); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { _logger?.LogWarning(ex, "외부 카메라 재연결 실패"); }

            still = await TryCaptureExternalAsync(ct);
            if (still is not null) return still;
        }

        // 강등: 이 컷부터 세션 끝까지 웹캠(컷 단위 복귀 없음). 이미 확보한 컷은 유지한다 —
        // ②③ 정규화 덕에 기하는 동일하고, 화질 차이는 배너로 고지된다. 전량 폐기보다 손님 피해가 작다.
        if (_webcamRunning)
        {
            _source = StillSource.Webcam;
            IsExternalSource = false;
            _physicalFlashOpen = false;
            DegradeBanner = "외부 카메라 연결이 끊겨 웹캠으로 촬영합니다";
            _logger?.LogWarning("외부 카메라 → 웹캠 강등(컷 {Cut}부터)", CurrentCut);
            return await _camera.CaptureStillAsync(ct);
        }

        _logger?.LogError("외부 카메라 실패 + 웹캠 부재 — 세션 중단");
        return null;
    }

    /// <summary>
    /// DSLR 1컷 수신 + 정규화. 실패(수신 null·디코드 실패)는 null이다(§11 E6·E11 — 재시도 대상).
    /// </summary>
    private async Task<CapturedStill?> TryCaptureExternalAsync(CancellationToken ct)
    {
        IsReceiving = true;
        try
        {
            // ⚠️ 수신 대기(최대 CaptureTimeout)는 사용자 입력이 없는 구간이다. 유휴 감시는 촬영 화면에서도
            //    동작하므로(SessionStateMachine.IsSessionActive에 Capture 포함), 진행 중임을 알려
            //    수신 대기가 유휴로 오인되지 않게 한다. 웹캠 경로는 이 호출을 타지 않는다(회귀 0).
            _shell.NotifyUserActivity();

            var bytes = await _external.CaptureAsync(ct);
            if (bytes is null) return null;

            // 24MP 디코드+크롭은 수백 ms 급 — UI 스레드에서 하면 프리뷰가 얼어붙는다(§12.1).
            var still = await Task.Run(() => _decoder.Decode(bytes, _slotAspect, _mirror), ct);

            _shell.NotifyUserActivity();
            return still;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "외부 카메라 컷 수신 중 예외(컷 실패로 강등)");
            return null;
        }
        finally
        {
            IsReceiving = false;
        }
    }

    private async Task CountdownAsync(int seconds, CancellationToken ct)
    {
        _countdownCts?.Dispose();
        _countdownCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token = _countdownCts.Token;

        RemainingSeconds = seconds;
        try
        {
            while (RemainingSeconds > 0)
            {
                await Task.Delay(1000, token);
                RemainingSeconds--;
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // [바로촬영]으로 카운트다운만 스킵 — 세션은 계속
            RemainingSeconds = 0;
        }
    }

    /// <summary>[바로 촬영]: 남은 카운트다운 스킵, 즉시 셔터. 매 컷 사용 가능(§9 #37).</summary>
    [RelayCommand]
    private void ShootNow() => _countdownCts?.Cancel();

    [RelayCommand]
    private void Cancel() => _shell.ReturnHome("촬영 취소");

    public override async Task OnLeaveAsync()
    {
        // 수신 대기도 이 취소로 함께 끊긴다(§12.2 — 어댑터가 링크 토큰을 쓴다).
        _sessionCts?.Cancel();
        _countdownCts?.Cancel();
        IsReceiving = false;
        try { await _camera.StopRecordingAsync(); } catch { /* 무시 */ }
        await _camera.StopAsync();
        // ⚠️ 외부 카메라는 여기서 끊지 않는다 — 다음 세션의 ConnectAsync가 즉시 성립하도록
        //    연결을 유지한다(재연결 비용 회피, §9.3과 동일 방침). 해제는 모달 닫기·앱 종료가 담당한다.
    }
}
