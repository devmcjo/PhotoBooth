using MCPhoto.Core.Devices;

namespace MCPhoto.Devices.Nikon;

/// <summary>
/// SDK 부재 shim — <b>이번 이터레이션의 프로덕션 기본 구현</b>이다. (it23 §3.4)
/// <para>
/// 항상 "모듈 없음"을 반환하므로 <c>ExternalCameraEnabled=true</c>로 켜 두어도 촬영은 웹캠 단독으로
/// 강등되고 사유(W10)가 화면에 표시된다 — 설계 요구 8의 "런타임 비활성 + 사유 노출"이 이것이다.
/// </para>
/// <para>
/// SDK 실물이 도착하면 <c>NikonSdkShim.cs</c>를 신설하고 DI 등록 한 줄을 교체한다(설계 §15-C4).
/// 이 클래스는 그때도 <b>남겨 둔다</b>: SDK 미배치 PC에서의 강등 경로를 테스트로 계속 검증하려면
/// "항상 부재"인 구현이 필요하다.
/// </para>
/// </summary>
/// <remarks>
/// ⚠️ <see cref="IDisposable"/>도 함께 구현하는 이유(구현 세부 — 계약 아님): 이 shim은 DI Singleton이고
/// <c>App.OnExit</c>는 동기 메서드라 <c>ServiceProvider.Dispose()</c>(동기)로 컨테이너가 정리된다.
/// <b>IAsyncDisposable만</b> 구현한 싱글턴이 컨테이너에 있으면 그 동기 Dispose가
/// InvalidOperationException을 던져 종료 시 예외가 난다. 동기 경로를 함께 제공해 그 함정을 막는다.
/// </remarks>
public sealed class MissingNikonSdkShim : INikonSdkShim, IDisposable
{
    /// <summary>
    /// it24: 항상 false — 이 shim으로는 SDK를 호출할 수 없다. 이 값이 준비도 판정의 첫 관문이라,
    /// SDK 런타임 파일을 수동 배치해도 검색은 "확인할 수 없습니다"(S2)로 정직하게 남는다(it24 R1).
    /// </summary>
    public bool IsOperational => false;

    public Task<(bool ok, string? reason)> OpenAsync(string md3Path, CancellationToken ct)
        => Task.FromResult<(bool, string?)>((false, NikonCameraReasons.SdkMissing));

    public Task CloseAsync(CancellationToken ct) => Task.CompletedTask;

    public Task<byte[]?> CaptureImageAsync(CancellationToken ct) => Task.FromResult<byte[]?>(null);

    public Task<ExternalCameraCapabilities?> ProbeCapabilitiesAsync(CancellationToken ct)
        => Task.FromResult<ExternalCameraCapabilities?>(null);

    public Task<ExposureDomain?> ReadExposureDomainAsync(CancellationToken ct)
        => Task.FromResult<ExposureDomain?>(null);

    public Task<bool> WriteExposureAsync(ExposureParameter parameter, string value, CancellationToken ct)
        => Task.FromResult(false);

    public Task<bool> WritePhysicalFlashAsync(bool enabled, CancellationToken ct) => Task.FromResult(false);

    /// <summary>발화하지 않는다(연결이 성립하지 않으므로 탈락도 없다). 빈 접근자 — 구독자를 붙잡지 않는다.</summary>
    public event Action<string?>? DeviceLost { add { } remove { } }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    /// <summary>동기 해제(컨테이너 종료 경로). 부재 shim은 잡고 있는 자원이 없어 no-op이다.</summary>
    public void Dispose() { }
}
