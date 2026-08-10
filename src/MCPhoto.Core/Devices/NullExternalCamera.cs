namespace MCPhoto.Core.Devices;

/// <summary>
/// <see cref="IExternalCamera"/>의 미지원(no-op) 기본 구현. (item3 스캐폴드)
///
/// 항상 미지원 상태를 반환한다: <see cref="IsAvailable"/>=false,
/// <see cref="ConnectAsync"/>=false, <see cref="CaptureAsync"/>=null, <see cref="DisconnectAsync"/>=no-op.
/// 예외를 던지지 않아 호출측이 안전하게 미지원을 감지·우회할 수 있다.
///
/// <para>
/// it23에서 프로덕션 DI 등록은 <c>NikonExternalCamera</c>로 옮겨갔지만 이 구현은 <b>삭제하지 않는다</b>:
/// (a) 테스트의 무해한 기본값이고, (b) 라이선스 문제로 <c>MCPhoto.Devices.Nikon</c> 프로젝트를
/// 통째로 제외해야 하는 사태(설계 §13 L1~L3)의 즉시 복귀처다 — 등록 한 줄만 되돌리면 앱이 산다.
/// </para>
/// </summary>
public sealed class NullExternalCamera : IExternalCamera
{
    public bool IsAvailable => false;

    public Task<bool> ConnectAsync(CancellationToken ct = default) => Task.FromResult(false);

    public Task<byte[]?> CaptureAsync(CancellationToken ct = default) => Task.FromResult<byte[]?>(null);

    public Task DisconnectAsync() => Task.CompletedTask;

    // ── it23 추가 멤버: 전부 "미구성" 고정. 사유 문구만 제공해 화면이 침묵하지 않게 한다. ──

    public string? ModelName => null;

    public string? UnavailableReason => "외부 카메라 미구성";

    public Task<ExternalCameraCapabilities?> GetCapabilitiesAsync(CancellationToken ct = default)
        => Task.FromResult<ExternalCameraCapabilities?>(null);

    public Task<ExposureDomain?> GetExposureDomainAsync(CancellationToken ct = default)
        => Task.FromResult<ExposureDomain?>(null);

    public Task<bool> SetExposureAsync(ExposureParameter parameter, string value, CancellationToken ct = default)
        => Task.FromResult(false);

    public Task<bool> TrySetPhysicalFlashAsync(bool enabled, CancellationToken ct = default)
        => Task.FromResult(false);

    /// <summary>
    /// it24: 제어 스택 없음 고정. 사유는 <see cref="UnavailableReason"/>과 같은 문구를 쓴다 —
    /// 같은 원인이 화면마다 다르게 설명되지 않게 하는 것이 문구 집약의 목적이다.
    /// </summary>
    public ExternalCameraReadiness CheckReadiness() => new(false, UnavailableReason);

    /// <summary>
    /// 이벤트는 정의만 하고 발행하지 않는다(상태가 변하지 않는 구현이므로).
    /// ⚠️ <c>add { } remove { }</c> 빈 접근자로 두는 이유: 자동 구현 이벤트로 두면 구독자를 실제로
    /// 붙잡아 두는데, 발행이 없으므로 그 참조는 순수한 누수 위험일 뿐이다.
    /// </summary>
    public event EventHandler<ExternalCameraConnectionChange>? ConnectionChanged { add { } remove { } }
}
