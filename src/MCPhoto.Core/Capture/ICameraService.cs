namespace MCPhoto.Core.Capture;

using MCPhoto.Core.Models;

/// <summary>
/// 웹캠 캡처 파이프라인. 하나의 스트림에서 프리뷰+스틸+녹화를 분기한다. (architecture §2)
/// 거울반전·슬롯 종횡비 중앙 크롭을 프레임당 1회만 수행(WYSIWYG).
/// </summary>
public interface ICameraService : IDisposable
{
    /// <summary>가공된 프리뷰 프레임 발행(백그라운드 캡처 스레드에서).</summary>
    event EventHandler<CameraFrame>? FrameReady;

    /// <summary>초당 렌더 프레임 수(진단).</summary>
    double CurrentFps { get; }

    bool IsRunning { get; }

    /// <summary>
    /// 캡처 시작. targetAspect = 대표 슬롯 종횡비(중앙 크롭 기준). mirror = 거울모드.
    /// 장치 미연결/열기 실패 시 예외 대신 false 반환(크래시 금지).
    /// </summary>
    Task<bool> StartAsync(int deviceIndex, double targetAspect, bool mirror, CancellationToken ct = default);

    /// <summary>캡처 정지 및 리소스 해제.</summary>
    Task StopAsync();

    /// <summary>거울모드 런타임 토글.</summary>
    void SetMirror(bool mirror);

    /// <summary>대표 슬롯 종횡비 변경(프레임 선택 시).</summary>
    void SetTargetAspect(double aspect);

    /// <summary>다음 프레임을 스틸 컷으로 캡처 요청(비동기 반환).</summary>
    Task<CapturedStill> CaptureStillAsync(CancellationToken ct = default);

    /// <summary>세션 녹화 시작(ffmpeg stdin 파이프).</summary>
    void StartRecording(string outputPath);

    /// <summary>세션 녹화 종료(stdin flush+close, moov atom 완성).</summary>
    Task StopRecordingAsync();

    /// <summary>연결된 카메라 장치 열거.</summary>
    IReadOnlyList<CameraDevice> EnumerateDevices();
}

/// <summary>캡처된 스틸 컷(가공 프레임).</summary>
public sealed class CapturedStill
{
    public int Width { get; init; }
    public int Height { get; init; }
    public byte[] Pixels { get; init; } = Array.Empty<byte>();
}

/// <summary>카메라 장치 정보.</summary>
public sealed record CameraDevice(int Index, string Name);
