using MCPhoto.Core.Capture;

namespace MCPhoto.Tests.Fakes;

/// <summary>
/// <see cref="ICameraService"/> 페이크 — <b>프레임을 실제로 펌핑</b>한다.
/// <para>
/// 왜 프레임 펌핑이 필요한가: <c>CaptureViewModel</c>의 Ready 게이트는
/// <see cref="PreviewReadiness"/>(연속 8프레임 + 500ms 경과 + fps&gt;0)를 요구한다. 프레임을 주지 않으면
/// 8초 타임아웃 후 Failed로 떨어져 촬영 시퀀스가 아예 시작되지 않는다 —
/// 그러면 컷 루프 관련 테스트가 전부 "카메라 없음" 경로만 검증하게 된다.
/// </para>
/// </summary>
public sealed class PumpingCameraService : ICameraService
{
    private readonly IReadOnlyList<CameraDevice> _devices;
    private CancellationTokenSource? _pumpCts;
    private Task? _pump;

    /// <summary>StartAsync 결과. false면 "웹캠 없음" 경로를 모사한다.</summary>
    public bool StartResult { get; set; } = true;

    /// <summary>프레임 펌핑 여부. false면 열리기만 하고 프레임이 오지 않는다(프리뷰 타임아웃 모사).</summary>
    public bool PumpFrames { get; set; } = true;

    public int StartCalls { get; private set; }
    public int StopCalls { get; private set; }
    public int StillCalls { get; private set; }
    public int StartRecordingCalls { get; private set; }
    public string? LastRecordingPath { get; private set; }

    public PumpingCameraService(params CameraDevice[] devices)
        => _devices = devices.Length > 0 ? devices : new[] { new CameraDevice(0, "Camera 0") };

    public event EventHandler<CameraFrame>? FrameReady;

    /// <summary>펌핑 중이면 30(PreviewReadiness가 fps&gt;0을 요구한다).</summary>
    public double CurrentFps => IsRunning && PumpFrames ? 30 : 0;

    public bool IsRunning { get; private set; }

    public Task<bool> StartAsync(int deviceIndex, double targetAspect, bool mirror, CancellationToken ct = default)
    {
        StartCalls++;
        if (!StartResult) return Task.FromResult(false);

        IsRunning = true;
        if (PumpFrames && _pump is null)
        {
            _pumpCts = new CancellationTokenSource();
            _pump = PumpAsync(_pumpCts.Token);
        }
        return Task.FromResult(true);
    }

    private async Task PumpAsync(CancellationToken ct)
    {
        var frame = new CameraFrame { Width = 8, Height = 8, Pixels = new byte[8 * 8 * 3], Stride = 8 * 3 };
        try
        {
            while (!ct.IsCancellationRequested)
            {
                FrameReady?.Invoke(this, frame);
                await Task.Delay(20, ct);
            }
        }
        catch (OperationCanceledException) { /* 정상 종료 */ }
    }

    public async Task StopAsync()
    {
        StopCalls++;
        IsRunning = false;
        _pumpCts?.Cancel();
        if (_pump is not null)
        {
            try { await _pump; } catch { /* 무시 */ }
            _pump = null;
        }
        _pumpCts?.Dispose();
        _pumpCts = null;
    }

    public void SetMirror(bool mirror) { }

    public void SetTargetAspect(double aspect) { }

    /// <summary>웹캠 스틸. 소스 판별을 위해 폭 1로 표시한다(외부 컷과 구분).</summary>
    public Task<CapturedStill> CaptureStillAsync(CancellationToken ct = default)
    {
        StillCalls++;
        return Task.FromResult(new CapturedStill { Width = 1, Height = 1, Pixels = new byte[3] });
    }

    public void StartRecording(string outputPath)
    {
        StartRecordingCalls++;
        LastRecordingPath = outputPath;
    }

    public Task StopRecordingAsync() => Task.CompletedTask;

    public IReadOnlyList<CameraDevice> EnumerateDevices() => _devices;

    public void Dispose() => _pumpCts?.Cancel();
}
