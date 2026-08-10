using MCPhoto.Core.Devices;

namespace MCPhoto.Tests.Fakes;

/// <summary>
/// <see cref="IExternalCamera"/> 페이크. 모든 응답 시나리오(연결 실패·capability 미지원·컷별 캡처 실패)를
/// 주입할 수 있고, 호출 횟수를 관측한다. (it23 §14.3)
/// <para>
/// 이 페이크의 가장 중요한 용도는 <b>호출 0회</b> 검증이다: <c>ExternalCameraEnabled=false</c>에서
/// 촬영 흐름이 외부 카메라를 만지지 않는다는 회귀 0 계약(T-C1)을 여기서만 증명할 수 있다.
/// </para>
/// </summary>
public sealed class FakeExternalCamera : IExternalCamera
{
    // ── 주입 스크립트 ──

    /// <summary>ConnectAsync 결과. 기본 성공.</summary>
    public bool ConnectResult { get; set; } = true;

    /// <summary>연결 실패 시 사용자 노출 사유.</summary>
    public string? Reason { get; set; }

    /// <summary>GetCapabilitiesAsync 결과(연결 성공 시). 기본 = 스틸·노출·플래시 전부 Supported.</summary>
    public ExternalCameraCapabilities? Capabilities { get; set; } = new(
        CapabilityState.Supported, CapabilityState.Supported, CapabilityState.Supported,
        CapabilityState.Unsupported, CapabilityState.Unsupported, 75);

    /// <summary>CaptureAsync 기본 반환 바이트(null이면 실패).</summary>
    public byte[]? CaptureResult { get; set; }

    /// <summary>
    /// 앞쪽 N회 캡처를 강제로 실패시킨다(컷 중간 실패 → 재시도 → 강등 경로 검증).
    /// 1컷 실패는 재시도까지 포함해 2회 실패가 필요하다(설계 §6.4).
    /// </summary>
    public int FailFirstCaptures { get; set; }

    /// <summary>CaptureAsync 지연(수신 대기 상태 관측용).</summary>
    public TimeSpan CaptureDelay { get; set; } = TimeSpan.Zero;

    /// <summary>CaptureAsync 직전에 실행되는 훅(수신 대기 중 UI 상태를 관측하는 데 쓴다).</summary>
    public Action? OnCapture { get; set; }

    // ── 관측 ──

    public int ConnectCalls { get; private set; }
    public int CaptureCalls { get; private set; }
    public int CapabilityCalls { get; private set; }
    public int DisconnectCalls { get; private set; }
    public int PhysicalFlashCalls { get; private set; }
    public List<bool> FlashValues { get; } = new();

    /// <summary>모든 멤버를 통틀어 한 번이라도 접촉됐는지 — 회귀 0(T-C1) 판정에 쓴다.</summary>
    public bool Touched => ConnectCalls > 0 || CaptureCalls > 0 || CapabilityCalls > 0
                           || DisconnectCalls > 0 || PhysicalFlashCalls > 0;

    public bool IsAvailable { get; private set; }

    public string? ModelName => IsAvailable ? "Nikon D5300" : null;

    public string? UnavailableReason => IsAvailable ? null : Reason;

    public Task<bool> ConnectAsync(CancellationToken ct = default)
    {
        ConnectCalls++;
        IsAvailable = ConnectResult;
        return Task.FromResult(ConnectResult);
    }

    public async Task<byte[]?> CaptureAsync(CancellationToken ct = default)
    {
        CaptureCalls++;
        OnCapture?.Invoke();
        if (CaptureDelay > TimeSpan.Zero) await Task.Delay(CaptureDelay, ct);
        if (FailFirstCaptures > 0)
        {
            FailFirstCaptures--;
            return null;
        }
        return CaptureResult;
    }

    public Task DisconnectAsync()
    {
        DisconnectCalls++;
        IsAvailable = false;
        return Task.CompletedTask;
    }

    public Task<ExternalCameraCapabilities?> GetCapabilitiesAsync(CancellationToken ct = default)
    {
        CapabilityCalls++;
        return Task.FromResult(IsAvailable ? Capabilities : null);
    }

    public Task<ExposureDomain?> GetExposureDomainAsync(CancellationToken ct = default)
        => Task.FromResult<ExposureDomain?>(Domain);

    /// <summary>GetExposureDomainAsync 결과(설정·모달 테스트에서 주입).</summary>
    public ExposureDomain? Domain { get; set; }

    /// <summary>SetExposureAsync로 전달된 기록.</summary>
    public List<(ExposureParameter Parameter, string Value)> ExposureWrites { get; } = new();

    /// <summary>SetExposureAsync 결과.</summary>
    public bool SetExposureResult { get; set; } = true;

    public Task<bool> SetExposureAsync(ExposureParameter parameter, string value, CancellationToken ct = default)
    {
        ExposureWrites.Add((parameter, value));
        return Task.FromResult(SetExposureResult);
    }

    public Task<bool> TrySetPhysicalFlashAsync(bool enabled, CancellationToken ct = default)
    {
        PhysicalFlashCalls++;
        FlashValues.Add(enabled);
        return Task.FromResult(true);
    }

    public event EventHandler<ExternalCameraConnectionChange>? ConnectionChanged;

    /// <summary>테스트가 연결 상태 변화를 발화(USB 뽑힘 모사).</summary>
    public void RaiseConnectionChanged(bool connected, string? reason)
    {
        IsAvailable = connected;
        if (!connected) Reason = reason;
        ConnectionChanged?.Invoke(this, new ExternalCameraConnectionChange(connected, reason));
    }
}
