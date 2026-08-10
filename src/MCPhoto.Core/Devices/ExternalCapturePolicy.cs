namespace MCPhoto.Core.Devices;

/// <summary>
/// 외부 카메라 촬영 정책 상수·순수 판정. (it23 §3.4·§4.1·§5.2)
/// <para>
/// 이 클래스의 존재 이유는 **실기 없이 정한 값들을 한곳에 격리**하는 것이다. 셔터→수신 소요 시간은
/// 현재 추정값(설계 A7)이므로 실기 측정 후 조정될 것이 확실하다 — 상수가 코드 여러 곳에 흩어져 있으면
/// 그 조정이 "값 하나 바꾸기"가 아니라 "숨은 상수 찾기"가 된다.
/// </para>
/// </summary>
public static class ExternalCapturePolicy
{
    /// <summary>
    /// 연결(모듈 로드 + 장치 대기) 타임아웃. 세션 시작 지연의 상한이므로 짧게 잡는다 —
    /// 초과하면 웹캠 단독으로 강등되고 세션은 계속된다(§6.1).
    /// ⚠️ 실기 미검증(설계 §15-C5에서 조정).
    /// </summary>
    public static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// 셔터 릴리즈 → 이미지 수신 완료 타임아웃. 컷 1개당 손님이 기다리는 최대 시간이다.
    /// ⚠️ 실기 미검증(설계 A7 — 수 초로 추정). 과소하면 정상 촬영이 실패로 오판되지만
    /// 오판의 결과도 강등 경로(§6.4)라 세션은 죽지 않는다.
    /// </summary>
    public static readonly TimeSpan CaptureTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// 수신 스틸의 긴 변 상한(px). 초과하면 균등 축소한다(§5.2 ④).
    /// <para>
    /// 필요한 이유: D5300은 24MP(6000×4000)급이라 <c>CapturedStill</c>의 BGR24 원시 버퍼가
    /// 컷당 약 72MB다. 세션 내내 전 컷을 메모리에 들고 있는 현행 구조에서 10컷 = 720MB로 감당 불가다.
    /// 2400px 상한이면 컷당 약 11.5MB로, 합성 출력(프레임 캔버스)보다 여전히 크므로 화질 손실이 없다.
    /// </para>
    /// </summary>
    public const int MaxIngestLongEdge = 2400;

    /// <summary>
    /// 컷 캡처 실패 시 재시도 횟수(재연결 포함). 1회로 고정한 이유: 실패한 장치를 여러 번 재시도하면
    /// 손님이 타임아웃을 반복 대기한다(§6.4).
    /// </summary>
    public const int CaptureRetryCount = 1;

    /// <summary>
    /// capability 게이트: <see cref="CapabilityState.Supported"/>만 열린다.
    /// <see cref="CapabilityState.Unknown"/>도 닫는다 — "확인 못 한 기능"을 열어 두면
    /// 미검증 경로가 손님 세션에서 처음 실행된다(설계 요구 8: 막히면 비활성 + 사유 노출).
    /// </summary>
    public static bool IsOpen(CapabilityState state) => state == CapabilityState.Supported;

    /// <summary>
    /// capability 상태별 사용자 노출 사유 문구(§9.4 W13·W14). Supported면 사유가 없으므로 null.
    /// </summary>
    public static string? DescribeClosed(CapabilityState state) => state switch
    {
        CapabilityState.Supported => null,
        CapabilityState.Unsupported => "이 카메라가 지원하지 않는 기능입니다",
        _ => "기능 지원 여부를 확인하지 못했습니다"
    };
}
