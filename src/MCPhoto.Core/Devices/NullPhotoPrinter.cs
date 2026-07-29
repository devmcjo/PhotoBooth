namespace MCPhoto.Core.Devices;

/// <summary>
/// <see cref="IPhotoPrinter"/>의 미지원(no-op) 기본 구현. (item3 스캐폴드)
///
/// 항상 미지원 상태를 반환한다: <see cref="IsAvailable"/>=false, <see cref="PrintAsync"/>=false(no-op).
/// 예외를 던지지 않아 호출측이 안전하게 미지원을 감지·우회할 수 있다.
///
/// ⚠️ 실제 프린터 연동은 장비 확정 후 이 구현을 대체하는 실 구현으로 DI에서 교체한다.
/// </summary>
public sealed class NullPhotoPrinter : IPhotoPrinter
{
    public bool IsAvailable => false;

    public Task<bool> PrintAsync(byte[] imageBytes, CancellationToken ct = default) => Task.FromResult(false);
}
