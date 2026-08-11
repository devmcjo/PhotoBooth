namespace MCPhoto.Core.Devices;

/// <summary>
/// capability 3상. (it23 §4.1)
/// <para>
/// <see cref="Unknown"/>은 "프로브를 못 했거나 실패했다"는 뜻이다 — 게이트 판정에서는
/// <see cref="Unsupported"/>와 **동일하게 닫히지만**(<see cref="ExternalCapturePolicy.IsOpen"/>),
/// 사용자에게 보이는 사유 문구가 다르다("지원하지 않음" vs "확인하지 못함").
/// 두 상태를 하나로 합치면 "카메라가 못 하는 것"과 "우리가 못 물어본 것"이 구분되지 않아
/// 운영자가 USB·전원을 점검할 단서를 잃는다.
/// </para>
/// </summary>
public enum CapabilityState
{
    /// <summary>프로브 미실시·실패. 게이트는 닫히되 사유 문구는 "확인 불가"(W14).</summary>
    Unknown,

    /// <summary>카메라가 지원하지 않는 기능(프로브 결과 확정). 사유 문구는 "미지원"(W13).</summary>
    Unsupported,

    /// <summary>지원 확인됨 — 해당 경로가 열린다.</summary>
    Supported
}

/// <summary>
/// capability 프로브 결과 묶음. 항목별 3상이므로 **부분 실패를 허용**한다
/// (노출 제어는 확인됐지만 물리 플래시는 확인 못 한 상태가 정상적으로 표현된다).
/// <para>
/// <see cref="LiveView"/>·<see cref="VideoRecord"/>는 이번 이터레이션의 **비목표**로 자리만 확보한다
/// (it23 §17.2) — 값은 진단 표시에만 쓰이고 UI 경로는 배선하지 않는다. 자리를 비워 두지 않는 이유는,
/// 훗날 지원 여부가 판명될 때 프로브 계약을 바꾸지 않고 값만 채우면 되게 하기 위해서다.
/// </para>
/// </summary>
/// <param name="StillCapture">스틸 캡처(셔터 릴리즈 → PC 수신).</param>
/// <param name="ExposureControl">노출 3요소 조회·설정.</param>
/// <param name="PhysicalFlash">물리 플래시(내장 팝업) 발광 모드 제어.</param>
/// <param name="LiveView">LiveView 스트림(비목표 — 자리 확보).</param>
/// <param name="VideoRecord">동영상 녹화(비목표 — 타임랩스는 웹캠 전담).</param>
/// <param name="BatteryLevelPercent">배터리 잔량 %. 조회 실패·미지원이면 null.</param>
public sealed record ExternalCameraCapabilities(
    CapabilityState StillCapture,
    CapabilityState ExposureControl,
    CapabilityState PhysicalFlash,
    CapabilityState LiveView,
    CapabilityState VideoRecord,
    int? BatteryLevelPercent)
{
    /// <summary>
    /// 프로브 실패 시의 전 항목 Unknown 결과(it23 E10). 호출측이 매번 6개 인자를 나열하지 않도록 제공한다.
    /// </summary>
    public static ExternalCameraCapabilities AllUnknown { get; } = new(
        CapabilityState.Unknown, CapabilityState.Unknown, CapabilityState.Unknown,
        CapabilityState.Unknown, CapabilityState.Unknown, null);
}

/// <summary>노출 3요소 식별자. (it23 §10)</summary>
public enum ExposureParameter
{
    ShutterSpeed,
    Aperture,
    Iso
}

/// <summary>
/// 한 노출 파라미터의 이산 도메인: 카메라가 준 **표시 문자열 목록(순서 보존)** + 현재값 인덱스.
/// <para>
/// 값이 숫자가 아니라 문자열인 이유(it23 §3.2): 셔터는 <c>"1/125"</c>, 조리개는 <c>"f/5.6"</c>처럼
/// 카메라가 주는 표기를 그대로 운반해야 한다. Core가 숫자로 파싱하면 SDK 표기 관례라는
/// **미검증 가정**(설계 A3)이 Core 계약에 스며든다.
/// </para>
/// </summary>
/// <param name="Values">선택 가능한 표시 문자열(카메라가 준 순서 그대로). 빈 목록이면 도메인 미확보.</param>
/// <param name="CurrentIndex">현재값의 <paramref name="Values"/> 인덱스. -1 = 미확인.</param>
public sealed record ExposureDomainEntry(IReadOnlyList<string> Values, int CurrentIndex)
{
    /// <summary>현재값 표시 문자열. 인덱스가 범위를 벗어나면 null(미확인).</summary>
    public string? CurrentValue
        => CurrentIndex >= 0 && CurrentIndex < Values.Count ? Values[CurrentIndex] : null;

    /// <summary>
    /// 입력 문자열이 도메인에 있으면 그 인덱스, 없으면 -1.
    /// 대소문자·앞뒤 공백을 무시하되 **근사 매칭은 하지 않는다**(it23 §10.2·§17.2):
    /// <c>1/100</c>을 <c>1/125</c>로 바꿔 적용하는 것은 운영자 몰래 노출을 바꾸는 것이다.
    /// </summary>
    public int IndexOf(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return -1;
        var needle = value.Trim();
        for (int i = 0; i < Values.Count; i++)
        {
            if (string.Equals(Values[i]?.Trim(), needle, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return -1;
    }
}

/// <summary>
/// 노출 3요소 도메인. 파라미터별로 미지원이면 해당 엔트리가 null이다
/// (예: 카메라 모드에 따라 조리개만 잠기는 상황을 표현할 수 있어야 한다 — it23 §15-C6).
/// </summary>
public sealed record ExposureDomain(
    ExposureDomainEntry? ShutterSpeed,
    ExposureDomainEntry? Aperture,
    ExposureDomainEntry? Iso)
{
    /// <summary>파라미터별 엔트리 조회(미지원·미지 파라미터는 null).</summary>
    public ExposureDomainEntry? this[ExposureParameter parameter] => parameter switch
    {
        ExposureParameter.ShutterSpeed => ShutterSpeed,
        ExposureParameter.Aperture => Aperture,
        ExposureParameter.Iso => Iso,
        _ => null
    };
}

/// <summary>
/// 장치 접촉 없는 <b>로컬 전제 검사</b> 결과. (it24 §5.1)
/// <para>
/// <see cref="CanControl"/>=false는 "장치가 없다"가 아니라 <b>"장치 유무를 판정할 능력이 없다"</b>는 뜻이다(it24 R1).
/// 제어 스택(SDK shim 실구현 + 런타임 파일)이 갖춰지지 않은 상태에서 연결 실패는 장치 부재의 증거가 아니다 —
/// 카메라가 꽂혀 있어도 결과는 똑같이 실패한다. 이 구분을 타입으로 강제하지 않으면 화면이
/// "연결 가능한 장치가 없습니다"라고 단정해, 운영자가 케이블·전원을 헛되이 점검한다.
/// </para>
/// </summary>
/// <param name="CanControl">SDK 제어 스택이 갖춰졌는지(= 연결 시도의 성패를 장치 유무의 근거로 쓸 수 있는지).</param>
/// <param name="Reason">불가 사유(사용자 노출용 짧은 한국어 문구). <paramref name="CanControl"/>=true면 null.</param>
public sealed record ExternalCameraReadiness(bool CanControl, string? Reason);

/// <summary>
/// 연결 상태 변화 통지(USB 뽑힘·전원 꺼짐 등). (it23 §3.2)
/// ⚠️ <see cref="IExternalCamera.ConnectionChanged"/>는 **임의 스레드**에서 발생한다 —
/// UI를 만지는 구독자가 Dispatcher로 마샬링할 책임이 있다(§12.1).
/// </summary>
/// <param name="IsConnected">변화 후 연결 상태.</param>
/// <param name="Reason">사용자 노출용 짧은 한국어 사유(정상 연결이면 null).</param>
public sealed record ExternalCameraConnectionChange(bool IsConnected, string? Reason);
