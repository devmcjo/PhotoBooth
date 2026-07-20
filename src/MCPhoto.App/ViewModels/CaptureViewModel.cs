using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MCPhoto.Core.Capture;
using MCPhoto.Core.Navigation;
using Microsoft.Extensions.Logging;

namespace MCPhoto.App.ViewModels;

/// <summary>
/// 촬영/카운트다운. N컷 연속 촬영(컷별 카운트다운 → 자동 셔터), [바로촬영], 플래시, 세션 녹화. (PRD §F1/F3)
/// 카운트다운 타이머는 async 루프로 구동, [바로촬영]은 남은 시간 스킵.
/// </summary>
public sealed partial class CaptureViewModel : ViewModelBase
{
    private readonly AppShellViewModel _shell;
    private readonly ICameraService _camera;
    private readonly ILogger<CaptureViewModel>? _logger;

    private CancellationTokenSource? _countdownCts;
    private CancellationTokenSource? _sessionCts;

    [ObservableProperty] private int _currentCut;
    [ObservableProperty] private int _totalCuts;
    [ObservableProperty] private int _remainingSeconds;
    [ObservableProperty] private bool _cameraAvailable = true;
    [ObservableProperty] private bool _flashActive;
    [ObservableProperty] private string _statusMessage = string.Empty;

    /// <summary>View가 프리뷰 렌더를 위해 구독.</summary>
    public ICameraService Camera => _camera;

    public CaptureViewModel(AppShellViewModel shell, ICameraService camera, ILogger<CaptureViewModel>? logger = null)
    {
        _shell = shell;
        _camera = camera;
        _logger = logger;
    }

    public override async Task OnEnterAsync()
    {
        var session = _shell.Session;
        var settings = _shell.Settings.Current;
        var frame = session.SelectedFrame;
        if (frame is null) { _shell.ReturnHome("프레임 없음"); return; }

        TotalCuts = session.Capture.CutCount;

        // 대표 슬롯 종횡비로 크롭
        double aspect = frame.Slots.Count > 0 ? frame.Slots[0].AspectRatio : 3.0 / 4.0;

        bool ok = await _camera.StartAsync(settings.CameraDevice, aspect, settings.MirrorMode);
        CameraAvailable = ok;
        if (!ok)
        {
            StatusMessage = "카메라를 찾을 수 없습니다.";
            _logger?.LogWarning("촬영 화면: 카메라 미연결");
            return;
        }

        // 세션 작업 폴더 준비
        session.WorkFolder = Path.Combine(App.DataFolder, "sessions", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(session.WorkFolder);
        session.SessionVideoPath = Path.Combine(session.WorkFolder, "session.mp4");
        session.SessionTime = DateTime.Now;

        _sessionCts = new CancellationTokenSource();
        _ = RunCaptureSequenceAsync(_sessionCts.Token);
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
                    await Task.Delay(120, ct);
                }

                var still = await _camera.CaptureStillAsync(ct);
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
        _sessionCts?.Cancel();
        _countdownCts?.Cancel();
        try { await _camera.StopRecordingAsync(); } catch { /* 무시 */ }
        await _camera.StopAsync();
    }
}
