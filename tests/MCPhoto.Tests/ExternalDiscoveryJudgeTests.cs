using MCPhoto.Core.Devices;

namespace MCPhoto.Tests;

/// <summary>
/// it24 Step 1: 검색 상태 판정 순수 함수(설계 §5.3 · §12.1 T-J1·T-J2).
/// <para>
/// 이 테스트가 지키는 것은 코드가 아니라 <b>명제</b>다: 어떤 관측 조합에서 화면이 "없다"고 말해도 되고
/// 어디서는 "확인할 수 없다"고만 말해야 하는지를 8조합 전수로 고정한다. 판정이 관측(WMI·SDK) 안으로
/// 흩어지면 이 표를 실물 장비 없이 검증할 방법이 사라진다.
/// </para>
/// </summary>
public class ExternalDiscoveryJudgeTests
{
    private static ExternalCameraReadiness Ready() => new(true, null);
    private static ExternalCameraReadiness NotReady() => new(false, "SDK 모듈이 설치되지 않았습니다");

    // ── T-J1: (CanControl, usbSeen, connected) 8조합 전수 ──

    [Theory]
    // 스택 미비: connected 입력과 무관하게 S2/S3 — 판정 능력이 없으면 어떤 시도 결과도 부재의 근거가 아니다.
    [InlineData(false, false, false, ExternalCameraDiscoveryState.UndeterminedStackMissing)]
    [InlineData(false, false, true, ExternalCameraDiscoveryState.UndeterminedStackMissing)]
    [InlineData(false, true, false, ExternalCameraDiscoveryState.DetectedUncontrollable)]
    [InlineData(false, true, true, ExternalCameraDiscoveryState.DetectedUncontrollable)]
    // 스택 정상: 연결 성공이면 USB 관측과 무관하게 S6, 실패면 USB 관측이 S4/S5를 가른다.
    [InlineData(true, false, false, ExternalCameraDiscoveryState.NotFound)]
    [InlineData(true, false, true, ExternalCameraDiscoveryState.Connected)]
    [InlineData(true, true, false, ExternalCameraDiscoveryState.DetectedConnectFailed)]
    [InlineData(true, true, true, ExternalCameraDiscoveryState.Connected)]
    public void Judge_Covers_All_Eight_Observation_Combinations(
        bool canControl, bool usbSeen, bool connected, ExternalCameraDiscoveryState expected)
    {
        var readiness = canControl ? Ready() : NotReady();
        Assert.Equal(expected, ExternalDiscoveryJudge.Judge(readiness, usbSeen, connected));
    }

    /// <summary>
    /// ★ 이 설계의 핵: 스택 미비 상태에서 <b>부재 단정(S4)이 나올 수 없다</b>.
    /// 어떤 입력이든 S2/S3만 나오므로, 화면이 "연결 가능한 장치를 찾지 못했습니다"를 말할 자격이 생기지 않는다.
    /// </summary>
    [Fact]
    public void Judge_Never_Claims_Absence_When_Stack_Is_Missing()
    {
        foreach (var usbSeen in new[] { false, true })
        foreach (var connected in new[] { false, true })
        {
            var state = ExternalDiscoveryJudge.Judge(NotReady(), usbSeen, connected);
            Assert.NotEqual(ExternalCameraDiscoveryState.NotFound, state);
            Assert.NotEqual(ExternalCameraDiscoveryState.Connected, state);
        }
    }

    // ── T-J2: 호출측이 게이트를 어겨도 판정은 흔들리지 않는다(방어) ──

    [Fact]
    public void Judge_Ignores_Connected_When_CanControl_Is_False()
    {
        // §5.2 ②는 스택 미비 시 ConnectAsync를 부르지 않게 게이트하지만, 그 게이트가 깨져도
        // 판정이 S6("연결 확인됨")으로 넘어가지 않아야 한다 — 부재 shim의 성공은 성립할 수 없는 관측이다.
        Assert.Equal(ExternalCameraDiscoveryState.UndeterminedStackMissing,
            ExternalDiscoveryJudge.Judge(NotReady(), usbCandidateSeen: false, connected: true));
        Assert.Equal(ExternalCameraDiscoveryState.DetectedUncontrollable,
            ExternalDiscoveryJudge.Judge(NotReady(), usbCandidateSeen: true, connected: true));
    }

    /// <summary>readiness가 null이어도 크래시 없이 "확인 불가"로 축퇴한다(크래시 금지 관례).</summary>
    [Fact]
    public void Judge_Null_Readiness_Degrades_To_Undetermined()
    {
        Assert.Equal(ExternalCameraDiscoveryState.UndeterminedStackMissing,
            ExternalDiscoveryJudge.Judge(null!, usbCandidateSeen: false, connected: true));
    }

    // ── T-R4: 기본 무해 구현의 준비도 ──

    [Fact]
    public void NullExternalCamera_Is_Never_Controllable()
    {
        IExternalCamera cam = new NullExternalCamera();
        var readiness = cam.CheckReadiness();

        Assert.False(readiness.CanControl);
        // 사유는 UnavailableReason과 같은 문구여야 한다(같은 원인이 화면마다 다르게 설명되지 않게).
        Assert.Equal("외부 카메라 미구성", readiness.Reason);
        Assert.Equal(cam.UnavailableReason, readiness.Reason);
    }
}
