namespace MCPhoto.Core.Devices;

/// <summary>
/// 외부 카메라(DSLR 등) 연동 추상화. (item3 스캐폴드 → it23 실배선)
///
/// <para>
/// it23에서 Nikon MAID 어댑터(<c>MCPhoto.Devices.Nikon</c>)가 이 계약을 구현한다. 계약은 **SDK를 모른다** —
/// MAID 함수·상수 이름은 어댑터 내부의 shim 1파일에만 등장한다(설계 §3). 그래서 SDK가 도착하거나
/// 다른 벤더로 갈아타도 이 파일과 소비자(App)는 바뀌지 않는다.
/// </para>
///
/// 관례: **미지원·실패는 예외가 아니라 false/null**이다(<see cref="MCPhoto.Core.Capture.ICameraService"/>와 동일).
/// 손님 세션 중에 예외가 올라오면 키오스크가 죽는다 — 강등은 반환값으로 표현한다.
/// 기본 무해 구현은 <see cref="NullExternalCamera"/>(항상 미지원 · no-op).
/// </summary>
public interface IExternalCamera
{
    /// <summary>외부 카메라 사용 가능 여부(연결 확립 + 미탈락).</summary>
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

    // ── it23 추가 멤버(기존 4멤버 시그니처는 불변 — 프로덕션 소비자가 DI 등록뿐이라 확장 비용이 최소다) ──

    /// <summary>연결된 모델 표시명(미연결이면 null). 예: <c>"Nikon D5300"</c>.</summary>
    string? ModelName { get; }

    /// <summary>
    /// 사용 불가 사유(사용자 노출용 짧은 한국어 문구). 사용 가능하면 null.
    /// 예: <c>"카메라 모듈 파일이 없습니다 (NikonSdk\Type0011.md3)"</c>.
    /// <para>
    /// 왜 bool이 아니라 문구인가: "외부 카메라가 안 된다"만 알려주면 운영자가 USB·전원·SDK 배치 중
    /// 무엇을 손봐야 할지 알 수 없다. 강등은 조용히 하되 **이유는 화면에 남긴다**(설계 요구 8).
    /// </para>
    /// </summary>
    string? UnavailableReason { get; }

    /// <summary>
    /// capability 프로브 결과(연결 직후 1회 조회한 캐시). 미연결이면 null.
    /// 개별 항목의 프로브가 실패하면 그 항목만 <see cref="CapabilityState.Unknown"/>이다(§4.1).
    /// </summary>
    Task<ExternalCameraCapabilities?> GetCapabilitiesAsync(CancellationToken ct = default);

    /// <summary>노출 3요소의 이산 도메인 + 현재값. 미연결/미지원이면 null(예외 금지).</summary>
    Task<ExposureDomain?> GetExposureDomainAsync(CancellationToken ct = default);

    /// <summary>
    /// 노출값 적용(도메인 안의 표시 문자열로 지정). 미지원·도메인 불일치·쓰기 거부면 false.
    /// false는 "카메라 현재값이 그대로 유지됐다"는 뜻이며 촬영을 막지 않는다(§11 E9).
    /// </summary>
    Task<bool> SetExposureAsync(ExposureParameter parameter, string value, CancellationToken ct = default);

    /// <summary>
    /// 물리 플래시(내장 팝업) 발광 모드 설정 시도. capability가 Supported일 때만 true가 될 수 있다(§4.3).
    /// ⚠️ 현재 프로덕션 구현은 항상 false다 — 활성 경로는 화면 플래시 단독이다.
    /// </summary>
    Task<bool> TrySetPhysicalFlashAsync(bool enabled, CancellationToken ct = default);

    /// <summary>
    /// 연결 상태 변화(USB 뽑힘·전원 꺼짐 등).
    /// ⚠️ <b>임의 스레드에서 발생한다</b>(SDK 콜백 스레드 모델 미검증) — UI를 만지는 구독자가
    /// Dispatcher로 마샬링할 책임이 있다(§12.1). 구독자는 반드시 해제 경로를 갖는다(§12.2).
    /// </summary>
    event EventHandler<ExternalCameraConnectionChange>? ConnectionChanged;
}
