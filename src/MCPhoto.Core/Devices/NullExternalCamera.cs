namespace MCPhoto.Core.Devices;

/// <summary>
/// <see cref="IExternalCamera"/>의 미지원(no-op) 기본 구현. (item3 스캐폴드)
///
/// 항상 미지원 상태를 반환한다: <see cref="IsAvailable"/>=false,
/// <see cref="ConnectAsync"/>=false, <see cref="CaptureAsync"/>=null, <see cref="DisconnectAsync"/>=no-op.
/// 예외를 던지지 않아 호출측이 안전하게 미지원을 감지·우회할 수 있다.
///
/// ⚠️ 실제 외부 카메라 연동은 장비 확정 후 이 구현을 대체하는 실 구현으로 DI에서 교체한다.
/// 참조: docs/USER-ACTIONS.md §C1.
/// </summary>
public sealed class NullExternalCamera : IExternalCamera
{
    public bool IsAvailable => false;

    public Task<bool> ConnectAsync(CancellationToken ct = default) => Task.FromResult(false);

    public Task<byte[]?> CaptureAsync(CancellationToken ct = default) => Task.FromResult<byte[]?>(null);

    public Task DisconnectAsync() => Task.CompletedTask;
}
