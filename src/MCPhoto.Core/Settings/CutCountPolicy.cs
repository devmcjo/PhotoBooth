namespace MCPhoto.Core.Settings;

/// <summary>
/// 촬영 컷 수 정책(순수 함수 — UI·설정 인스턴스 무의존). (it17)
/// 설정값 <see cref="AppSettings.CutCount"/>는 "의도"만 담는다: 고정 컷 수(6/8/10) 또는
/// 자동(<see cref="AutoCutCount"/>). 실제 촬영 컷 수는 프레임 슬롯 수가 확정된 뒤
/// (<see cref="Capture.CaptureSession.Begin"/>) 이 클래스가 산출한다 — 유일한 해석 지점.
/// </summary>
public static class CutCountPolicy
{
    /// <summary>
    /// "자동" 모드 sentinel(ini에 그대로 기록된다). 0은 ini 누락·손상으로는 만들어질 수 없어
    /// (IniFile.GetInt가 fallback을 돌려줌) 명시적 의도만을 나타낸다. 설계 §4.1.
    /// </summary>
    public const int AutoCutCount = 0;

    /// <summary>자동 모드의 최소 촬영 컷 수(고정 기본값과 동일 — PRD "최소 6").</summary>
    public const int AutoMinimum = 6;

    /// <summary>자동 모드에서 슬롯 수에 더하는 여유분. 컷 선택의 여지를 확보한다(요구사항 §0.1).</summary>
    public const int AutoMargin = 2;

    /// <summary>설정값이 자동 모드인가. -1 등 다른 음수는 자동이 아니다(§4.1).</summary>
    public static bool IsAuto(int configured) => configured == AutoCutCount;

    /// <summary>
    /// 실제 촬영 컷 수 산출.
    /// 자동: max(<see cref="AutoMinimum"/>, 슬롯 + <see cref="AutoMargin"/>).
    /// 고정: max(설정값, 슬롯) — "컷 수 ≥ 슬롯 수" 불변 유지(종전 동작 그대로).
    /// slotCount가 음수/0(프레임 미확정)이면 0으로 취급 → 자동은 6, 고정은 설정값.
    /// </summary>
    public static int Resolve(int configured, int slotCount)
    {
        int slots = Math.Max(slotCount, 0);
        return IsAuto(configured)
            ? Math.Max(AutoMinimum, slots + AutoMargin)
            : Math.Max(configured, slots);
    }
}
