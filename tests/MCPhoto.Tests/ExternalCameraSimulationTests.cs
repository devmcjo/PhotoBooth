using MCPhoto.Core.Devices;
using MCPhoto.Core.Settings;

namespace MCPhoto.Tests;

/// <summary>
/// it25 Step 3: <c>[Test]</c> 외부 카메라 시뮬레이션 계획(순수 함수) 검증 — 설계 §12.1 T-B4.
/// <para>
/// 이 스위트의 요점은 <b>계획이 기존 판정 파이프라인을 그대로 통과한다</b>는 것이다: 계획을
/// <see cref="ExternalDiscoveryJudge.Judge"/>에 태웠을 때 나오는 상태가 §5.4 표(S4·S6)와 일치해야
/// 화면 문구가 실관측과 어긋날 수 없다.
/// </para>
/// </summary>
public class ExternalCameraSimulationTests
{
    private static TestModeOptions Parse(string ini) => TestModeOptions.FromIni(IniFile.Parse(ini));

    // ── 계획 없음(= 실관측 수행) ──

    /// <summary>테스트 모드가 꺼져 있으면 계획이 없다 — <c>Disabled</c> 객체로도 null이어야 한다.</summary>
    [Fact]
    public void Plan_Is_Null_When_Test_Mode_Disabled()
    {
        Assert.Null(ExternalCameraSimulation.Plan(TestModeOptions.Disabled));
        Assert.Null(ExternalCameraSimulation.Plan(Parse("[Test]\nTestMode=0\nExternalCamera=1\n")));
    }

    /// <summary>
    /// 테스트 모드가 켜져 있어도 <c>ExternalCamera=0</c>(기본)이면 계획이 없다.
    /// ★ 기본값이 "시뮬레이션 없음"이어야 테스트 ini를 쓰는 기존 QA 흐름이 이번 변경에 영향받지 않는다.
    /// </summary>
    [Fact]
    public void Plan_Is_Null_When_Switch_Off()
    {
        Assert.Null(ExternalCameraSimulation.Plan(Parse("[Test]\nTestMode=1\n")));
        Assert.Null(ExternalCameraSimulation.Plan(
            Parse("[Test]\nTestMode=1\nExternalCamera=0\nExternalCameraType=0\n")));
    }

    // ── S4: 시뮬레이션 켜짐 + 인식된 장치 없음 ──

    [Fact]
    public void Plan_Type_Minus_One_Judges_As_NotFound()
    {
        var plan = ExternalCameraSimulation.Plan(
            Parse("[Test]\nTestMode=1\nExternalCamera=1\nExternalCameraType=-1\n"));

        Assert.NotNull(plan);
        // 스택은 항상 "정상"이다 — 시뮬레이션은 장비·SDK 없이는 볼 수 없는 상태만 공급한다.
        Assert.True(plan!.Readiness.CanControl);
        Assert.Null(plan.Readiness.Reason);
        Assert.False(plan.Connected);
        Assert.Null(plan.Model);

        // ★ 같은 Judge를 통과한다(판정 규칙 우회 없음) → S4.
        Assert.Equal(
            ExternalCameraDiscoveryState.NotFound,
            ExternalDiscoveryJudge.Judge(plan.Readiness, usbCandidateSeen: false, plan.Connected));
    }

    /// <summary>목록 밖 코드는 <c>FromIni</c>가 -1로 폴백하므로 계획도 S4가 된다(E21 연쇄).</summary>
    [Fact]
    public void Plan_Unknown_Type_Degrades_To_NotFound_Scenario()
    {
        var options = Parse("[Test]\nTestMode=1\nExternalCamera=1\nExternalCameraType=99\n");
        var plan = ExternalCameraSimulation.Plan(options);

        Assert.Single(options.Warnings);
        Assert.NotNull(plan);
        Assert.False(plan!.Connected);
        Assert.Null(plan.Model);
    }

    // ── S6: 시뮬레이션 켜짐 + 매핑 성공 ──

    [Fact]
    public void Plan_Type_Zero_Judges_As_Connected_With_D5300()
    {
        var plan = ExternalCameraSimulation.Plan(
            Parse("[Test]\nTestMode=1\nExternalCamera=1\nExternalCameraType=0\n"));

        Assert.NotNull(plan);
        Assert.True(plan!.Readiness.CanControl);
        Assert.True(plan.Connected);
        Assert.Same(ExternalCameraModels.Default, plan.Model);
        Assert.Equal("Nikon D5300", plan.Model!.DisplayName);

        Assert.Equal(
            ExternalCameraDiscoveryState.Connected,
            ExternalDiscoveryJudge.Judge(plan.Readiness, usbCandidateSeen: false, plan.Connected));
    }

    /// <summary>
    /// ★ 순수성: 같은 입력이면 같은 계획이고(값 동등), 계획은 세션·장치·파일을 읽지 않는다.
    /// 세션을 모르는 순수 함수는 어디에 주입돼도 우회를 만들 수 없다 — 그 사실이 봉인의 일부다(TS2).
    /// </summary>
    [Fact]
    public void Plan_Is_Pure_And_Session_Agnostic()
    {
        var options = Parse("[Test]\nTestMode=1\nExternalCamera=1\nExternalCameraType=0\n");

        Assert.Equal(ExternalCameraSimulation.Plan(options), ExternalCameraSimulation.Plan(options));

        // Plan의 입력은 TestModeOptions 하나다 — User·세션·서비스를 받는 오버로드가 없어야 한다.
        var parameters = typeof(ExternalCameraSimulation)
            .GetMethod(nameof(ExternalCameraSimulation.Plan))!
            .GetParameters();
        Assert.Single(parameters);
        Assert.Equal(typeof(TestModeOptions), parameters[0].ParameterType);
    }
}
