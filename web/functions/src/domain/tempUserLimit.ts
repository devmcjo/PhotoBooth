/**
 * TempUser QR 한도 판정(순수 로직) — 설계 §4.3.
 *
 * 시간 한도(계정 createdAt + qrHours 경과)와 횟수 한도(qrUsedCount >= qrCount)를 독립(OR)으로 평가한다.
 * 서버가 이 함수로 초과 여부를 판정해 업로드를 거부하고(과금 안전), 사용량 조회에도 같은 함수를 쓴다.
 * 시계·타임존은 호출측이 ms epoch(서버 UTC)로 넘긴다 — 여기서는 시각을 읽지 않는다(테스트 용이).
 *
 * **둘 다 초과 시 시간 우선**(설계 §8.1: 시간 초과는 회복 불가, 횟수는 한도 상향으로 회복 가능).
 */

/** 초과 사유. "ok"=미초과, "time"=시간 한도, "count"=횟수 한도. */
export type QrGateReason = "ok" | "time" | "count";

/** 전역 한도 1쌍(config/tempUserLimits 또는 기본값). */
export interface TempUserLimits {
  /** 시간 한도(시간). */
  qrHours: number;
  /** 횟수 한도(성공 세션 수). */
  qrCount: number;
}

/** 전역 한도 기본값(config 문서 부재 시 폴백). 설계 §0·§4.3. */
export const DEFAULT_TEMP_USER_LIMITS: TempUserLimits = {
  qrHours: 48,
  qrCount: 30,
};

/** evaluateQrGate 결과(거부 여부·사유·잔여). */
export interface QrGateResult {
  /** 초과(거부) 여부: 시간 또는 횟수 중 하나라도 초과면 true. */
  blocked: boolean;
  /** 사유(시간 우선). */
  reason: QrGateReason;
  /** 시간 잔여(ms). 초과 시 0. */
  remainingMs: number;
  /** 횟수 잔여. 초과 시 0. */
  remainingCount: number;
}

/**
 * QR 한도 초과 판정(순수). now·createdAtMs는 ms epoch(서버 UTC). usedCount는 계정 qrUsedCount(미설정=0).
 * 경계: 경과 시간이 정확히 한도이면 초과(>=), usedCount가 정확히 한도이면 초과(>=). 시간 우선(둘 다 초과면 time).
 */
export function evaluateQrGate(
  now: number,
  createdAtMs: number,
  usedCount: number,
  limits: TempUserLimits
): QrGateResult {
  const elapsedMs = now - createdAtMs;
  const limitMs = limits.qrHours * 3600_000;
  const timeExceeded = elapsedMs >= limitMs;
  const countExceeded = usedCount >= limits.qrCount;
  const remainingMs = Math.max(0, limitMs - elapsedMs);
  const remainingCount = Math.max(0, limits.qrCount - usedCount);
  // 시간 우선: 둘 다 초과면 time.
  const reason: QrGateReason = timeExceeded ? "time" : countExceeded ? "count" : "ok";
  return {
    blocked: timeExceeded || countExceeded,
    reason,
    remainingMs,
    remainingCount,
  };
}
