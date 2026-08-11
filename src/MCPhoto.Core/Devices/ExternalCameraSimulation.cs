using MCPhoto.Core.Settings;

namespace MCPhoto.Core.Devices;

/// <summary>
/// <c>[Test]</c> 외부 카메라 시뮬레이션이 만들어 낼 <b>관측 입력 1세트</b>(it25 §5.4).
/// <para>
/// 관측 결과를 위조해 화면에 끼워 넣는 것이 <b>아니다</b> — 관측 채취 단계를 통째로 대체할 입력을 만들어
/// 기존 판정·문구 파이프라인(<see cref="ExternalDiscoveryJudge"/> → 문구 조립 1곳)에 그대로 태운다.
/// 그래서 시뮬레이션 상태와 실관측 상태의 화면 표현이 어긋날 수 없다.
/// </para>
/// </summary>
/// <param name="Readiness">
/// 제어 스택 준비도. 항상 <c>(CanControl: true, Reason: null)</c>이다 — 시뮬레이션은 "스택 정상" 시나리오만
/// 공급한다. S2(스택 미비)·S3/S5(WMI 감지)는 현 프로덕션 실경로가 이미 도달하므로 재현할 가치가 없고,
/// 시뮬레이션은 <b>장비·SDK 없이는 볼 수 없는 상태</b>(S4·S6)만 만든다.
/// </param>
/// <param name="Connected">모델 매핑 성공 여부(= S6인가 S4인가).</param>
/// <param name="Model">인식된 것으로 표시할 모델. 없으면 null(<c>ExternalCameraType=-1</c>).</param>
public sealed record ExternalDiscoverySimPlan(
    ExternalCameraReadiness Readiness,
    bool Connected,
    ExternalCameraModel? Model);

/// <summary>
/// 시뮬레이션 계획 생성(순수 — I/O·로그·시간 의존 없음). it25 §5.4.
/// <para>
/// ⚠️ 이 클래스는 <b>계획만</b> 만든다. "이 세션에 적용해도 되는가"(<c>IsTestUser</c> 참조 동일성 —
/// 불변식 TS2)는 호출측 단 한 곳(<c>SettingsViewModel</c>의 검색 시퀀스)이 판정한다. 여기서 세션을
/// 읽지 않는 것이 봉인의 일부다 — 세션을 모르는 순수 함수는 어디에 주입돼도 우회를 만들 수 없다.
/// </para>
/// </summary>
public static class ExternalCameraSimulation
{
    /// <summary>
    /// 시뮬레이션 계획. <b>null = 시뮬레이션 없음</b>(호출측이 실관측을 수행한다).
    /// <c>TestMode</c>와 <c>ExternalCamera</c>가 모두 참일 때만 계획을 만든다.
    /// </summary>
    public static ExternalDiscoverySimPlan? Plan(TestModeOptions options)
    {
        if (options is null || !options.Enabled || !options.ExternalCamera) return null;

        var readiness = new ExternalCameraReadiness(CanControl: true, Reason: null);
        var model = ExternalCameraModels.FindByTestType(options.ExternalCameraType);

        // Type=-1(또는 미지 코드 폴백)은 모순이 아니라 **정의된 조합**이다:
        // "시뮬레이션은 켰지만 인식된 장치는 없음" — 빈 인식 상태(콤보 sentinel 단독·W19 문구)를
        // 결정론적으로 확인하는 수단이며, SDK 없이는 도달할 수 없던 S4의 유일한 QA 경로다.
        return model is null
            ? new ExternalDiscoverySimPlan(readiness, Connected: false, Model: null)   // → Judge: S4
            : new ExternalDiscoverySimPlan(readiness, Connected: true, Model: model);  // → Judge: S6
    }
}
