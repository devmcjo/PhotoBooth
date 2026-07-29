namespace MCPhoto.Core.Devices;

/// <summary>
/// 사진 프린터(BT/WiFi 등) 연동 추상화. (item3 스캐폴드)
///
/// ⚠️ 현재는 골격(자리)만 존재한다. 실제 하드웨어 연동은 특정 모델·SDK·연결방식(BT/WiFi)에
/// 의존하므로 <b>장비 확정 후</b> 실제 구현(SDK/드라이버 배선)으로 교체한다.
/// 기본 등록 구현은 <see cref="NullPhotoPrinter"/>(항상 미지원 · no-op).
/// </summary>
public interface IPhotoPrinter
{
    /// <summary>프린터 사용 가능 여부. 스캐폴드 단계에서는 항상 false.</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// 이미지 바이트(인코딩된 JPG/PNG)를 인쇄. 미지원/실패면 예외 대신 false 반환(크래시 금지).
    /// </summary>
    Task<bool> PrintAsync(byte[] imageBytes, CancellationToken ct = default);
}
