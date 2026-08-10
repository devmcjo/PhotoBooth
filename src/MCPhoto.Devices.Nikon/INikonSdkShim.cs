using MCPhoto.Core.Devices;

namespace MCPhoto.Devices.Nikon;

/// <summary>
/// MAID SDK 원시 호출 경계. (it23 §3.4)
/// <para>
/// <b>이 인터페이스에는 SDK 이름이 없다</b> — 동사만 추상화한다. SDK 함수·상수 이름이 등장할 수 있는
/// 파일은 구현체 <c>NikonSdkShim.cs</c> 하나뿐이며, 그 파일은 <b>지금 존재하지 않는다</b>
/// (SDK 실물이 없다 → 파일의 부재가 "미착수" 신호다. 빈 껍데기를 미리 두면 그 신호가 사라진다).
/// 현재 프로덕션 구현은 <see cref="MissingNikonSdkShim"/>이다.
/// </para>
/// <para>
/// 오케스트레이션(상태머신·타임아웃·재시도·이벤트 수명)은 <see cref="NikonExternalCamera"/>가 전부 갖는다.
/// 그래서 SDK가 도착했을 때 사람이 채울 것은 <b>얇은 번역 계층뿐</b>이고, 그 구현 명세는 이미
/// FakeShim 계약 테스트(설계 §14.2 T-A1~A8)로 확정되어 있다.
/// </para>
/// <para>
/// ⚠️ <b>구현 근거 제약</b>(설계 §13.2 D5): 실구현의 시그니처 근거는 <b>SDK 동봉 공식 문서</b>(API 사양서·샘플)만
/// 허용한다. md3에서 export 심볼을 덤프하거나 디스어셈블해 역추적하는 방식은 라이선스의
/// 리버스 엔지니어링 금지 조항에 걸릴 소지가 있어 <b>금지</b>다. 공개 커뮤니티 자료는 설계 참고까지이며
/// 구현 확정 근거로 쓰지 않는다 — 그래서 이 파일과 어댑터에는 추정한 SDK 이름을 주석으로도 적지 않는다.
/// </para>
/// 계약:
/// <list type="bullet">
/// <item>모든 메서드는 <b>예외 대신 실패 결과</b>를 반환한다(크래시 금지 관례 계승).</item>
/// <item>호출 스레드·콜백 스레드 보장 없음(⚠️ SDK 스레딩 모델 미검증 — 설계 A1).
///       MAID가 특정 스레드를 요구한다고 판명되면 <b>구현체 내부에</b> 전용 스레드 펌프를 넣는다(계약 무변경).</item>
/// <item>취소 토큰의 타임아웃 부과는 호출측(오케스트레이션) 소관이다.</item>
/// </list>
/// </summary>
public interface INikonSdkShim : IAsyncDisposable
{
    /// <summary>
    /// 모듈 로드 + 장치 대기. md3 <b>절대 경로</b>를 받는다(경로 규약은 호출측이 결정 — shim은 파일 배치를 모른다).
    /// 실패 시 <c>(false, 사용자 노출용 사유)</c>.
    /// </summary>
    Task<(bool ok, string? reason)> OpenAsync(string md3Path, CancellationToken ct);

    /// <summary>
    /// 장치·모듈 해제. <b>재연결 가능한 상태</b>로 되돌린다(<see cref="OpenAsync"/> 재호출 가능).
    /// <para>
    /// ⚠️ <see cref="IAsyncDisposable.DisposeAsync"/>와 구분되는 이유: 어댑터는 DI Singleton이고
    /// 테스트 모달 닫기·세션 종료마다 연결을 끊었다가 다음 세션에 다시 연결한다(설계 §9.3·§6.1).
    /// 그 경로에서 Dispose를 부르면 이후 재연결이 영구 불가가 된다 — Dispose는 앱 종료 1회뿐이다.
    /// </para>
    /// </summary>
    Task CloseAsync(CancellationToken ct);

    /// <summary>셔터 릴리즈 → 이미지 수신 완료까지. 실패 null. 타임아웃은 호출측이 토큰으로 부과한다.</summary>
    Task<byte[]?> CaptureImageAsync(CancellationToken ct);

    /// <summary>capability 열거. 조회 자체가 실패하면 null(호출측이 전 항목 Unknown으로 해석).</summary>
    Task<ExternalCameraCapabilities?> ProbeCapabilitiesAsync(CancellationToken ct);

    /// <summary>노출 3요소의 이산 도메인 + 현재값. 미지원·실패 null.</summary>
    Task<ExposureDomain?> ReadExposureDomainAsync(CancellationToken ct);

    /// <summary>노출값 쓰기. 미지원·거부 false.</summary>
    Task<bool> WriteExposureAsync(ExposureParameter parameter, string value, CancellationToken ct);

    /// <summary>물리 플래시 발광 모드 쓰기. 미지원·거부 false.</summary>
    Task<bool> WritePhysicalFlashAsync(bool enabled, CancellationToken ct);

    /// <summary>
    /// 장치 탈락 통지(USB 뽑힘·전원 꺼짐). 인자는 사유 문구(없으면 null).
    /// ⚠️ <b>임의 스레드</b>에서 발화한다 — 구독자(<see cref="NikonExternalCamera"/>)는 마샬링하지 않고 재발행하며,
    /// UI 마샬링 책임은 최종 구독자(VM)에게 있다(설계 §12.1).
    /// </summary>
    event Action<string?>? DeviceLost;
}
