namespace MCPhoto.Core.Devices;

/// <summary>
/// 외부 카메라(DSLR 등) 연동 추상화. (item3 스캐폴드)
///
/// ⚠️ 현재는 골격(자리)만 존재한다. 실제 하드웨어 연동은 특정 모델·SDK·연결방식(USB/BT/WiFi)에
/// 의존하므로 <b>장비 확정 후</b> 실제 구현(SDK/드라이버 배선)으로 교체한다.
/// 기본 등록 구현은 <see cref="NullExternalCamera"/>(항상 미지원 · no-op).
/// </summary>
public interface IExternalCamera
{
    /// <summary>외부 카메라 사용 가능 여부. 스캐폴드 단계에서는 항상 false.</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// 외부 카메라 연결 시도. 미지원/미연결이면 예외 대신 false 반환(크래시 금지, <see cref="MCPhoto.Core.Capture.ICameraService"/> 관례).
    /// </summary>
    Task<bool> ConnectAsync(CancellationToken ct = default);

    /// <summary>
    /// 스틸 1컷 캡처. 미지원이면 null 반환(예외 금지). 반환 바이트는 인코딩된 이미지(JPG/PNG)로 가정.
    /// </summary>
    Task<byte[]?> CaptureAsync(CancellationToken ct = default);

    /// <summary>연결 해제(리소스 정리). 미연결이면 no-op.</summary>
    Task DisconnectAsync();
}
