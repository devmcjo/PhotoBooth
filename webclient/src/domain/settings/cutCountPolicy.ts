/**
 * 자동 컷 수 정책 — Windows `Settings/CutCountPolicy.cs` 이식 (analysis/41 §2.7, it17)
 *
 * 설정값은 **의도**만 담는다: 고정(6/8/10) 또는 자동(`AUTO_CUT_COUNT` = 0).
 * 실제 촬영 컷 수는 프레임 슬롯 수가 확정된 뒤(`FrameSelect`의 [다음]) 산출한다 —
 * **유일한 해석 지점**이며 `Guide`·`Capture`·전체 재촬영에서 재해석하지 않는다(WD19, VF-12).
 */

/**
 * "자동" sentinel. 저장 왕복에서 **clamp 보정 대상이 아니다** —
 * 허용 집합에 넣으면 `CutCount=3` 오입력이 6이 아니라 0으로 보정되고,
 * 가드가 없으면 저장 한 번에 "자동"이 6으로 덮여 소멸한다(analysis/41 §2.7).
 */
export const AUTO_CUT_COUNT = 0;

/** 자동 모드의 최소 촬영 컷 수(고정 기본값과 동일 — "최소 6"). */
export const AUTO_MINIMUM = 6;

/** 자동 모드에서 슬롯 수에 더하는 여유분(컷 선택의 여지 확보). */
export const AUTO_MARGIN = 2;

/** 설정값이 자동 모드인가. `-1` 등 다른 값은 자동이 아니다. */
export function isAutoCutCount(configured: number): boolean {
  return configured === AUTO_CUT_COUNT;
}

/**
 * 실제 촬영 컷 수 산출.
 *   자동: `max(6, 슬롯 + 2)`
 *   고정: `max(설정값, 슬롯)` — "컷 수 ≥ 슬롯 수" 불변 유지
 * `slotCount`가 음수·0(프레임 미확정)이면 0으로 취급한다.
 */
export function resolveCutCount(configured: number, slotCount: number): number {
  const slots = Math.max(slotCount, 0);
  return isAutoCutCount(configured)
    ? Math.max(AUTO_MINIMUM, slots + AUTO_MARGIN)
    : Math.max(configured, slots);
}
