using System.Diagnostics;
using MCPhoto.Core.Capture;
using Microsoft.Extensions.Logging;
using OpenCvSharp;

namespace MCPhoto.Capture;

/// <summary>
/// OpenCvSharp 기반 웹캠 캡처 파이프라인. 전용 백그라운드 스레드에서 프레임을 읽어
/// 거울반전·슬롯 종횡비 중앙 크롭을 프레임당 1회 수행하고(WYSIWYG), 가공 프레임을 세 소비자
/// (프리뷰 이벤트·스틸 요청·녹화 훅)로 분기한다. (architecture §2)
/// </summary>
public sealed class OpenCvCameraService : ICameraService
{
    private readonly ILogger<OpenCvCameraService>? _logger;

    private Thread? _captureThread;
    private volatile bool _running;
    private volatile bool _mirror;
    private volatile int _targetAspectMilli; // targetAspect * 1000 (스레드 안전 원자 접근용)

    private readonly object _stillLock = new();
    private TaskCompletionSource<CapturedStill>? _pendingStill;

    // 녹화(ffmpeg stdin 파이프). 가공 프레임 바이트를 소비.
    private FfmpegRunner? _recorder;
    private readonly object _recordLock = new();
    private volatile bool _recording;
    private int _recordWidth;
    private int _recordHeight;
    private long _recordedFrames;
    private const int RecordFps = 30;

    private readonly Func<FfmpegRunner> _recorderFactory;

    private double _fps;
    private int _deviceIndex;

    /// <summary>마지막 녹화 세션 길이(초) = 프레임 수 / fps. TimelapseService가 배속 역산에 사용.</summary>
    public double LastSessionSeconds { get; private set; }

    public OpenCvCameraService(ILogger<OpenCvCameraService>? logger = null, Func<FfmpegRunner>? recorderFactory = null)
    {
        _logger = logger;
        _recorderFactory = recorderFactory ?? (() => new FfmpegRunner(logger: logger));
    }

    public event EventHandler<CameraFrame>? FrameReady;

    public double CurrentFps => _fps;
    public bool IsRunning => _running;

    private double TargetAspect => _targetAspectMilli / 1000.0;

    public Task<bool> StartAsync(int deviceIndex, double targetAspect, bool mirror, CancellationToken ct = default)
    {
        if (_running) return Task.FromResult(true);

        _deviceIndex = deviceIndex;
        _mirror = mirror;
        _targetAspectMilli = (int)Math.Round(targetAspect * 1000);

        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _running = true;
        _captureThread = new Thread(() => CaptureLoop(started))
        {
            IsBackground = true,
            Name = "MCPhoto.Capture"
        };
        _captureThread.Start();
        return started.Task;
    }

    private void CaptureLoop(TaskCompletionSource<bool> started)
    {
        VideoCapture? cap = null;
        try
        {
            cap = new VideoCapture(_deviceIndex, VideoCaptureAPIs.DSHOW);
            if (!cap.IsOpened())
            {
                _logger?.LogWarning("카메라 장치 {Index} 열기 실패", _deviceIndex);
                _running = false;
                started.TrySetResult(false); // 예외 대신 false(크래시 금지, 완료 기준)
                return;
            }

            // 1080p MJPG 요청(UVC 1080p 확보에 필요). 실패 시 장치 기본값으로 폴백.
            cap.Set(VideoCaptureProperties.FourCC, VideoWriter.FourCC('M', 'J', 'P', 'G'));
            cap.Set(VideoCaptureProperties.FrameWidth, 1920);
            cap.Set(VideoCaptureProperties.FrameHeight, 1080);
            cap.Set(VideoCaptureProperties.Fps, 30);

            started.TrySetResult(true);

            using var frame = new Mat();
            var sw = Stopwatch.StartNew();
            int frameCount = 0;

            while (_running)
            {
                if (!cap.Read(frame) || frame.Empty())
                {
                    Thread.Sleep(5);
                    continue;
                }

                ProcessAndDispatch(frame);

                frameCount++;
                if (sw.ElapsedMilliseconds >= 1000)
                {
                    _fps = frameCount * 1000.0 / sw.ElapsedMilliseconds;
                    _logger?.LogDebug("프리뷰 {Fps:F1} fps", _fps);
                    frameCount = 0;
                    sw.Restart();
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "캡처 루프 오류");
            started.TrySetResult(false);
        }
        finally
        {
            cap?.Release();
            cap?.Dispose();
            _running = false;
        }
    }

    /// <summary>거울반전 + 중앙 크롭을 1회 수행하고 세 소비자로 분기.</summary>
    private void ProcessAndDispatch(Mat frame)
    {
        // 1) 거울모드(WYSIWYG)
        if (_mirror)
            Cv2.Flip(frame, frame, FlipMode.Y);

        // 2) 슬롯 종횡비 중앙 크롭
        var crop = CropCalculator.CenterCrop(frame.Width, frame.Height, TargetAspect);
        using var processed = new Mat(frame, new Rect(crop.X, crop.Y, crop.Width, crop.Height));

        // 프레임 크기 기록(녹화 시작 시 크기 확정용)
        LastFrameWidth = processed.Width;
        LastFrameHeight = processed.Height;
        if (!_recording)
        {
            _recordWidth = processed.Width;
            _recordHeight = processed.Height;
        }

        // 3-a) 프리뷰: 연속 BGR24 버퍼 추출 → 이벤트 발행
        var (buffer, stride) = ExtractBgr24(processed);

        FrameReady?.Invoke(this, new CameraFrame
        {
            Width = processed.Width,
            Height = processed.Height,
            Pixels = buffer,
            Stride = stride
        });

        // 3-b) 녹화: 가공 프레임 바이트를 ffmpeg stdin으로 write(백프레셔 시 드롭 허용, 프리뷰 우선)
        if (_recording)
        {
            var rec = _recorder;
            if (rec is not null)
            {
                rec.WriteFrame(buffer, buffer.Length);
                Interlocked.Increment(ref _recordedFrames);
            }
        }

        // 3-c) 스틸: 대기 중인 요청이 있으면 이 프레임을 컷으로 확정
        lock (_stillLock)
        {
            if (_pendingStill is { } tcs)
            {
                _pendingStill = null;
                var still = new CapturedStill
                {
                    Width = processed.Width,
                    Height = processed.Height,
                    Pixels = (byte[])buffer.Clone()
                };
                tcs.TrySetResult(still);
            }
        }
    }

    /// <summary>Mat(BGR, 3채널)에서 연속 BGR24 바이트 배열 추출(행 패딩 제거).</summary>
    private static (byte[] buffer, int stride) ExtractBgr24(Mat mat)
    {
        int width = mat.Width;
        int height = mat.Height;
        int stride = width * 3;
        var buffer = new byte[stride * height];

        // Mat이 연속이면 한 번에, 아니면 행별 복사
        if (mat.IsContinuous())
        {
            System.Runtime.InteropServices.Marshal.Copy(mat.Data, buffer, 0, buffer.Length);
        }
        else
        {
            for (int row = 0; row < height; row++)
            {
                nint rowPtr = mat.Ptr(row);
                System.Runtime.InteropServices.Marshal.Copy(rowPtr, buffer, row * stride, stride);
            }
        }

        return (buffer, stride);
    }

    public Task StopAsync()
    {
        _running = false;
        var t = _captureThread;
        _captureThread = null;
        if (t is not null && t.IsAlive)
        {
            // 캡처 스레드 종료 대기(최대 2초)
            return Task.Run(() => t.Join(TimeSpan.FromSeconds(2)));
        }
        return Task.CompletedTask;
    }

    public void SetMirror(bool mirror) => _mirror = mirror;

    public void SetTargetAspect(double aspect) => _targetAspectMilli = (int)Math.Round(aspect * 1000);

    public Task<CapturedStill> CaptureStillAsync(CancellationToken ct = default)
    {
        var tcs = new TaskCompletionSource<CapturedStill>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_stillLock)
        {
            _pendingStill = tcs;
        }
        ct.Register(() => tcs.TrySetCanceled(ct));
        return tcs.Task;
    }

    /// <summary>현재 녹화 중인지.</summary>
    public bool IsRecording => _recording;

    /// <summary>세션 녹화 시작. 현재 가공 프레임 크기로 ffmpeg 파이프 개시.</summary>
    public void StartRecording(string outputPath)
    {
        lock (_recordLock)
        {
            if (_recording) return;

            // 녹화 프레임 크기 = 현재 크롭 결과(마지막 발행 프레임). 없으면 1080 기반 추정.
            int w = _recordWidth > 0 ? _recordWidth : LastFrameWidth;
            int h = _recordHeight > 0 ? _recordHeight : LastFrameHeight;
            if (w <= 0 || h <= 0)
            {
                _logger?.LogWarning("녹화 프레임 크기 미확정 — 녹화 시작 보류");
                return;
            }

            _recorder = _recorderFactory();
            _recordedFrames = 0;
            bool ok = _recorder.StartRecording(w, h, RecordFps, outputPath);
            if (ok)
            {
                _recording = true;
                _logger?.LogInformation("세션 녹화 시작: {Path}", outputPath);
            }
            else
            {
                _recorder.Dispose();
                _recorder = null;
                _logger?.LogWarning("녹화 시작 실패(ffmpeg 미탑재 가능)");
            }
        }
    }

    public async Task StopRecordingAsync()
    {
        FfmpegRunner? rec;
        long frames;
        lock (_recordLock)
        {
            if (!_recording) return;
            _recording = false;
            rec = _recorder;
            _recorder = null;
            frames = Interlocked.Read(ref _recordedFrames);
        }

        LastSessionSeconds = frames / (double)RecordFps;

        if (rec is not null)
        {
            await rec.StopRecordingAsync();
            rec.Dispose();
        }
        _logger?.LogInformation("세션 녹화 종료: {Frames}프레임 ≈ {Sec:F1}s", frames, LastSessionSeconds);
    }

    /// <summary>마지막 발행 프레임 크기(녹화 시작 시 프레임 크기 확정용).</summary>
    public int LastFrameWidth { get; private set; }
    public int LastFrameHeight { get; private set; }

    public IReadOnlyList<CameraDevice> EnumerateDevices()
    {
        // (1) FriendlyName 후보를 WMI로 best-effort 조회(실패 시 빈 목록 → 인덱스 라벨 폴백).
        var friendlyNames = CameraNameProbe.TryGetImagingDeviceNames(_logger);

        // (2) OpenCvSharp/DShow는 장치 이름 열거 API가 제한적이므로 인덱스 프로빙(동작 기준).
        var openIndices = new List<int>();
        for (int i = 0; i < 8; i++)
        {
            try
            {
                using var cap = new VideoCapture(i, VideoCaptureAPIs.DSHOW);
                if (cap.IsOpened())
                {
                    openIndices.Add(i);
                    cap.Release();
                }
            }
            catch
            {
                // 장치 없음 — 무시
            }
        }

        // (3) 열린 인덱스에 WMI 이름을 순서 매핑(best-effort) + 폴백. 동작은 인덱스 기준 유지.
        return CameraNameProbe.ComposeDevices(openIndices, friendlyNames);
    }

    public void Dispose()
    {
        _running = false;
        _captureThread?.Join(TimeSpan.FromSeconds(2));

        lock (_recordLock)
        {
            _recording = false;
            _recorder?.Dispose();
            _recorder = null;
        }
    }
}
