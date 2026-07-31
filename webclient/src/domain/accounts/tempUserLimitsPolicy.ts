/**
 * 전역 무료 한도(TempUser) 편집 정책 — analysis/31 §4.9 (순수)
 *
 * ⚠️ **서버가 400으로 거부할 요청을 보내지 않는다.** 범위 검증은 서버에만 있으므로
 *    (`tempUserLimitsService`는 예외를 던진다) 저장 전에 여기서 한 번 막는다.
 * ⚠️ 서버는 PATCH에 **최소 1개 키**를 요구한다 → `buildLimitsPatch`가 실제로 달라진 키만 담는다.
 */

/** 서버 계약 범위(analysis/31 §4.9). */
export const MIN_QR_HOURS = 1;
export const MAX_QR_HOURS = 8760;
export const MIN_QR_COUNT = 1;
export const MAX_QR_COUNT = 100000;

/**
 * 전역 무료 한도 값.
 *
 * ⚠️ 이 타입의 **정본은 도메인**이다. `adapters/http/tempUserLimitsService.ts`가 여기서
 *    재수출하므로 두 곳이 갈라지지 않는다(도메인은 어댑터를 import할 수 없다 — 01 §2.1).
 */
export interface TempUserLimits {
  readonly qrHours: number;
  readonly qrCount: number;
}

/** 편집 중 값. 파싱 실패·빈 입력은 `null`이며 저장을 막는다. */
export interface TempUserLimitsDraft {
  readonly qrHours: number | null;
  readonly qrCount: number | null;
}

/**
 * 텍스트 → 정수. 앞뒤 공백·부호를 허용하고 소수점·지수·16진수·빈 값은 `null`이다
 * (`slotsFile.tryParseInt`와 같은 엄격도 — `Number.parseInt("12abc")`가 12를 내는 함정을 피한다).
 */
export function parseLimitInput(raw: string): number | null {
  const text = raw.trim();
  if (!/^[+-]?\d+$/.test(text)) return null;
  const value = Number(text);
  return Number.isSafeInteger(value) ? value : null;
}

export type LimitsRejection = "qrHours-range" | "qrCount-range" | "no-change";

export interface LimitsValidation {
  readonly ok: boolean;
  readonly reason?: LimitsRejection;
}

function inRange(value: number, min: number, max: number): boolean {
  return Number.isSafeInteger(value) && value >= min && value <= max;
}

/**
 * 저장 전 검증.
 *
 * `null`(파싱 실패·빈 입력)은 **"바꾸지 않음"이 아니라 오류**다 — 운영자가 지우고 저장했을 때
 * 조용히 기존 값이 유지되면 "저장했는데 안 바뀐다"가 된다.
 */
export function validateTempUserLimits(
  draft: TempUserLimitsDraft,
  current: TempUserLimits,
): LimitsValidation {
  if (draft.qrHours === null || !inRange(draft.qrHours, MIN_QR_HOURS, MAX_QR_HOURS)) {
    return { ok: false, reason: "qrHours-range" };
  }
  if (draft.qrCount === null || !inRange(draft.qrCount, MIN_QR_COUNT, MAX_QR_COUNT)) {
    return { ok: false, reason: "qrCount-range" };
  }
  if (draft.qrHours === current.qrHours && draft.qrCount === current.qrCount) {
    return { ok: false, reason: "no-change" };
  }
  return { ok: true };
}

/**
 * 실제로 달라진 키만 담는다(서버는 "최소 1개"를 요구한다).
 * 검증을 통과한 draft에만 쓴다 — `null`은 애초에 담지 않는다.
 */
export function buildLimitsPatch(
  draft: TempUserLimitsDraft,
  current: TempUserLimits,
): Partial<TempUserLimits> {
  const patch: { qrHours?: number; qrCount?: number } = {};
  if (draft.qrHours !== null && draft.qrHours !== current.qrHours) patch.qrHours = draft.qrHours;
  if (draft.qrCount !== null && draft.qrCount !== current.qrCount) patch.qrCount = draft.qrCount;
  return patch;
}
