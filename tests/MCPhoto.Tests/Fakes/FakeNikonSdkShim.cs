using MCPhoto.Core.Devices;
using MCPhoto.Devices.Nikon;

namespace MCPhoto.Tests.Fakes;

/// <summary>
/// <see cref="INikonSdkShim"/> 페이크. 지연·실패·이벤트를 스크립트로 주입해
/// <c>NikonExternalCamera</c>의 오케스트레이션(상태머신·타임아웃·단일 비행·강등)을 실물 SDK 없이 검증한다.
/// (it23 §14.2)
/// <para>
/// 이 페이크의 시나리오가 곧 <c>NikonSdkShim</c> 실구현의 명세다(설계 §15-C4) — SDK가 도착하면
/// 여기 적힌 응답 규약을 실제 MAID 호출로 번역하면 된다.
/// </para>
/// </summary>
public sealed class FakeNikonSdkShim : INikonSdkShim
{
    // ── 주입 스크립트 ──

    /// <summary>OpenAsync 결과. 기본 성공.</summary>
    public bool OpenResult { get; set; } = true;

    /// <summary>OpenAsync 실패 사유(OpenResult=false일 때).</summary>
    public string? OpenReason { get; set; }

    /// <summary>OpenAsync가 소요하는 시간(연결 타임아웃 검증용).</summary>
    public TimeSpan OpenDelay { get; set; } = TimeSpan.Zero;

    /// <summary>CaptureImageAsync가 반환할 바이트. null이면 수신 실패.</summary>
    public byte[]? CaptureResult { get; set; } = new byte[] { 1, 2, 3, 4 };

    /// <summary>CaptureImageAsync가 소요하는 시간(수신 타임아웃 검증용).</summary>
    public TimeSpan CaptureDelay { get; set; } = TimeSpan.Zero;

    /// <summary>ProbeCapabilitiesAsync 결과. null이면 조회 실패(어댑터가 전 항목 Unknown으로 승격).</summary>
    public ExternalCameraCapabilities? Capabilities { get; set; } = AllSupported;

    /// <summary>ReadExposureDomainAsync 결과. null이면 도메인 미확보.</summary>
    public ExposureDomain? Domain { get; set; }

    /// <summary>WriteExposureAsync 결과.</summary>
    public bool WriteExposureResult { get; set; } = true;

    /// <summary>WritePhysicalFlashAsync 결과.</summary>
    public bool WritePhysicalFlashResult { get; set; } = true;

    /// <summary>지정 메서드에서 던질 예외(예외 삼킴·강등 경로 검증용).</summary>
    public Exception? OpenThrows { get; set; }
    public Exception? CaptureThrows { get; set; }
    public Exception? ProbeThrows { get; set; }

    // ── 관측 ──

    public int OpenCalls { get; private set; }
    public int CloseCalls { get; private set; }
    public int CaptureCalls { get; private set; }
    public int ProbeCalls { get; private set; }
    public int ReadDomainCalls { get; private set; }
    public int DisposeCalls { get; private set; }
    public string? LastMd3Path { get; private set; }

    /// <summary>WriteExposureAsync로 전달된 (파라미터, 값) 기록. 스킵 검증에 사용.</summary>
    public List<(ExposureParameter Parameter, string Value)> ExposureWrites { get; } = new();

    /// <summary>WritePhysicalFlashAsync로 전달된 값 기록.</summary>
    public List<bool> FlashWrites { get; } = new();

    /// <summary>캡처 진행 중 최대 동시 진입 수(단일 비행 검증 — 1을 넘으면 계약 위반).</summary>
    public int MaxConcurrentCaptures { get; private set; }
    private int _inFlightCaptures;

    /// <summary>전 항목 Supported + 배터리 80%(정상 카메라 모사).</summary>
    public static ExternalCameraCapabilities AllSupported { get; } = new(
        CapabilityState.Supported, CapabilityState.Supported, CapabilityState.Supported,
        CapabilityState.Unsupported, CapabilityState.Unsupported, 80);

    public async Task<(bool ok, string? reason)> OpenAsync(string md3Path, CancellationToken ct)
    {
        OpenCalls++;
        LastMd3Path = md3Path;
        if (OpenThrows is not null) throw OpenThrows;
        if (OpenDelay > TimeSpan.Zero) await Task.Delay(OpenDelay, ct);
        return (OpenResult, OpenResult ? null : OpenReason);
    }

    public Task CloseAsync(CancellationToken ct)
    {
        CloseCalls++;
        return Task.CompletedTask;
    }

    public async Task<byte[]?> CaptureImageAsync(CancellationToken ct)
    {
        CaptureCalls++;
        var concurrent = Interlocked.Increment(ref _inFlightCaptures);
        if (concurrent > MaxConcurrentCaptures) MaxConcurrentCaptures = concurrent;
        try
        {
            if (CaptureThrows is not null) throw CaptureThrows;
            if (CaptureDelay > TimeSpan.Zero) await Task.Delay(CaptureDelay, ct);
            return CaptureResult;
        }
        finally { Interlocked.Decrement(ref _inFlightCaptures); }
    }

    public Task<ExternalCameraCapabilities?> ProbeCapabilitiesAsync(CancellationToken ct)
    {
        ProbeCalls++;
        if (ProbeThrows is not null) throw ProbeThrows;
        return Task.FromResult(Capabilities);
    }

    public Task<ExposureDomain?> ReadExposureDomainAsync(CancellationToken ct)
    {
        ReadDomainCalls++;
        return Task.FromResult(Domain);
    }

    public Task<bool> WriteExposureAsync(ExposureParameter parameter, string value, CancellationToken ct)
    {
        ExposureWrites.Add((parameter, value));
        return Task.FromResult(WriteExposureResult);
    }

    public Task<bool> WritePhysicalFlashAsync(bool enabled, CancellationToken ct)
    {
        FlashWrites.Add(enabled);
        return Task.FromResult(WritePhysicalFlashResult);
    }

    public event Action<string?>? DeviceLost;

    /// <summary>테스트가 장치 탈락을 발화(USB 뽑힘 모사).</summary>
    public void RaiseDeviceLost(string? reason) => DeviceLost?.Invoke(reason);

    public ValueTask DisposeAsync()
    {
        DisposeCalls++;
        return ValueTask.CompletedTask;
    }
}
