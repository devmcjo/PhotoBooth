namespace MCPhoto.Core.Devices;

/// <summary>
/// 외부 카메라 검색 결과 상태(it24 §5.3 S0~S7).
/// <para>
/// 이 enum의 존재 이유는 <b>두 명제를 섞지 않는 것</b>이다:
/// <see cref="UndeterminedStackMissing"/>("확인할 수 없다")과 <see cref="NotFound"/>("찾지 못했다")는
/// 하나로 합칠 수 있어 보이지만, 합치면 SDK 미탑재 상태에서 화면이 장치 부재를 단정하게 된다(it24 R1).
/// 그 단정은 운영자에게 케이블·전원을 점검하라고 말하는 셈인데, 실제 조치는 SDK 모듈 배치다.
/// </para>
/// </summary>
public enum ExternalCameraDiscoveryState
{
    /// <summary>S0 — 아직 검색하지 않음(화면 진입 초기값). 검색은 명시 버튼으로만 시작된다(§5.4).</summary>
    NotSearched,

    /// <summary>S1 — 검색 진행 중. <b>Judge의 출력이 아니다</b>(진행 상태는 VM이 소유한다).</summary>
    Searching,

    /// <summary>S2 — 제어 스택 미비 + USB 후보 없음: 장치 유무를 <b>판정할 수 없다</b>.</summary>
    UndeterminedStackMissing,

    /// <summary>S3 — 제어 스택 미비 + USB 후보 감지: "꽂혀 있으나 제어할 수 없다"는 정직한 중간 상태.</summary>
    DetectedUncontrollable,

    /// <summary>S4 — 스택 정상 + 연결 실패 + USB 후보 없음: 부재 단정을 <b>완화형</b>으로 말할 수 있는 유일한 상태.</summary>
    NotFound,

    /// <summary>S5 — 스택 정상 + 연결 실패 + USB 후보 감지: 점유·케이블 문제를 의심할 근거가 있다.</summary>
    DetectedConnectFailed,

    /// <summary>S6 — 연결 확인됨(관찰 직후 즉시 해제하므로 "연결됨"이 아니라 "확인됨"이다, §5.5).</summary>
    Connected,

    /// <summary>S7 — 검색 시퀀스 자체가 예외로 끝남. <b>Judge의 출력이 아니다</b>(VM catch가 직접 설정).</summary>
    SearchFailed
}

/// <summary>
/// 검색 관측 3원 → 상태 판정. <b>순수 함수 1곳</b>이며 I/O가 없다(it24 §5.3).
/// <para>
/// 관측(WMI·SDK·파일)과 판정을 분리하는 이유: 상태 전수표를 실물 DSLR·SDK 없이 headless로 전수 검증할 수
/// 있게 하려는 것이다. 판정이 관측 코드 안에 흩어지면 "어떤 관측 조합에서 무엇을 말하는가"가 테스트로
/// 고정되지 않고, 화면 문구가 조용히 거짓이 된다.
/// </para>
/// </summary>
public static class ExternalDiscoveryJudge
{
    /// <summary>
    /// 상태 판정(§5.3 표의 조건열 그대로).
    /// <para>
    /// ⚠️ <paramref name="usbCandidateSeen"/>은 <b>양성 신호 전용</b>이다(it24 R3): true는 "감지되었다"를
    /// 말하지만 false는 어떤 단정도 강화하지 않는다. Nikon 바디가 제네릭 "MTP Portable Device"로 열거될 수
    /// 있어(WEB1) 매칭은 언제든 miss날 수 있고, 미관측을 부재의 근거로 쓰면 그 miss가 곧 거짓말이 된다.
    /// </para>
    /// </summary>
    /// <param name="readiness">로컬 전제 검사 결과(<see cref="IExternalCamera.CheckReadiness"/>).</param>
    /// <param name="usbCandidateSeen">PnP 트리에서 모델 키워드에 매칭된 장치가 있었는지.</param>
    /// <param name="connected">SDK 연결 시도 성공 여부. 스택 미비 시엔 시도 자체를 하지 않으므로 무시된다.</param>
    public static ExternalCameraDiscoveryState Judge(
        ExternalCameraReadiness readiness, bool usbCandidateSeen, bool connected)
    {
        // ⚠️ 스택 미비면 connected 입력을 **무시**한다(방어 — §12.1 T-J2). 호출측이 게이트를 어겨
        //    연결을 시도했더라도, 그 성공/실패는 판정 능력의 부재를 메워 주지 않는다.
        if (readiness is null || !readiness.CanControl)
        {
            return usbCandidateSeen
                ? ExternalCameraDiscoveryState.DetectedUncontrollable   // S3
                : ExternalCameraDiscoveryState.UndeterminedStackMissing; // S2
        }

        if (connected) return ExternalCameraDiscoveryState.Connected;    // S6

        return usbCandidateSeen
            ? ExternalCameraDiscoveryState.DetectedConnectFailed         // S5
            : ExternalCameraDiscoveryState.NotFound;                     // S4
    }
}
