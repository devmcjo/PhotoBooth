import { clamp } from "../mathCompat";

/**
 * 진입 PIN 게이트 정책(순수) — analysis/61 §7 · 07 §6 · WD16
 *
 * 여기 있는 것: 형식 검사 · 서버 응답 분류 · 실패 상태머신 · 쿨다운·잠금 시각 계산 ·
 * 기기 잠금 레코드 파싱.
 *
 * ⚠️ **PIN 값 자체를 어디에도 남기지 않는다.** 이 파일은 로거를 가지지 않으며(도메인),
 *    입력값은 판정에만 쓰고 반환값에 실어 보내지 않는다.
 * ⚠️ 도메인은 시각을 만들지 않는다 — `nowMs`를 **항상 인자로** 받는다(`purity.test.ts`).
 * ⚠️ `domain/index.ts` 배럴에 **등재하지 않는다** — `domain/auth/*`는 명시 경로 import 규약이고
 *    평면 배럴에서는 짧은 이름이 충돌한다.
 */

/** 서버 계약 `^\d{4}$`(`web/functions/src/domain/validation.ts`의 `PIN_RE`). */
export const PIN_LENGTH = 4;
/** 연속 불일치 상한. 5회째에 모달이 닫히고 기기 잠금이 걸린다(07 §6.2). */
export const MAX_PIN_FAILS = 5;
/** 불일치 후 입력 비활성 시간(03 §15.3). */
export const PIN_COOLDOWN_MS = 1_500;
/** 기기 단위 잠금 시간(WD16). */
export const PIN_LOCK_MS = 5 * 60 * 1_000;

/**
 * 정확히 4자리 ASCII 숫자인가.
 *
 * ⚠️ `u` 플래그를 붙이지 않는다 — `\d`가 ASCII `0-9`만 매칭해야 전각 숫자(`１２３４`)가
 *    거부된다. 서버 정규식과 같은 축이다.
 */
export function isPinFormatValid(value: string): boolean {
  return /^\d{4}$/.test(value);
}

/** 최초 설정 2단계(새 PIN → 재입력)의 일치 판정. 빈 값끼리는 일치로 보지 않는다. */
export function pinInputsMatch(first: string, second: string): boolean {
  return isPinFormatValid(first) && first === second;
}

/**
 * 어댑터가 HTTP 응답·예외를 이 판별 유니온으로 접어 넘긴다.
 * 도메인은 `fetch`·예외 타입을 알지 못한다.
 */
export type PinCallOutcome =
  | { readonly kind: "ok" }
  | { readonly kind: "status"; readonly status: number }
  /** 응답 자체가 없다(연결 실패·타임아웃·CORS). */
  | { readonly kind: "network" };

export type PinVerifyClass = "granted" | "mismatch" | "unset" | "unavailable";

/**
 * `POST /accounts/me/pin/verify` 응답 분류(06 §2.0).
 *   200 → granted · 401 → 불일치 · **409 → 서버에 PIN이 없다**(최초 설정 플로우로 전환)
 *   그 외·네트워크 → `unavailable`(**실패 카운트 미가산 · 게이트 미개방**)
 */
export function classifyPinVerify(outcome: PinCallOutcome): PinVerifyClass {
  if (outcome.kind === "ok") return "granted";
  if (outcome.kind === "network") return "unavailable";
  if (outcome.status === 401) return "mismatch";
  if (outcome.status === 409) return "unset";
  return "unavailable";
}

export type PinSetClass = "granted" | "mismatch" | "alreadySet" | "invalid" | "unavailable";

/**
 * `PUT /accounts/me/pin` 응답 분류(06 §2.0).
 *
 * ⚠️ **`sentCurrentPin === false`(최초 설정)에서의 401은 불일치가 아니라 `alreadySet`이다.**
 *    서버가 이미 `pinHash`를 갖고 있어 `currentPin`을 요구하는 상태다 —
 *    이것을 "불일치"로 세면 클라가 `hasPin=false`를 믿는 동안 5회 만에 기기가 잠긴다.
 */
export function classifyPinSet(outcome: PinCallOutcome, sentCurrentPin: boolean): PinSetClass {
  if (outcome.kind === "ok") return "granted";
  if (outcome.kind === "network") return "unavailable";
  if (outcome.status === 401) return sentCurrentPin ? "mismatch" : "alreadySet";
  if (outcome.status === 400) return "invalid";
  return "unavailable";
}

export interface PinAttemptState {
  /** 연속 불일치 횟수. 네트워크·서버 오류는 **세지 않는다**. */
  readonly fails: number;
  /** 이 시각까지 입력 비활성. "아직 한 번도 없음"은 `-Infinity`다. */
  readonly cooldownUntilMs: number;
  /** 기기 잠금 해제 시각. 잠금 없음은 `-Infinity`다. */
  readonly lockedUntilMs: number;
}

/**
 * 초기 상태.
 *
 * ⚠️ 쿨다운·잠금의 "없음"을 `0`으로 두면 안 된다 — `nowMs`가 항상 0보다 크므로 우연히
 *    동작하긴 하지만, 시각을 0 기준으로 주입하는 테스트에서 첫 입력이 차단된다(15 §4 함정 #4).
 */
export function initialPinAttemptState(): PinAttemptState {
  return {
    fails: 0,
    cooldownUntilMs: Number.NEGATIVE_INFINITY,
    lockedUntilMs: Number.NEGATIVE_INFINITY,
  };
}

/** 불일치 1회 반영. 5회째면 `exhausted`이고 잠금 시각이 함께 설정된다. */
export function applyPinFailure(
  state: PinAttemptState,
  nowMs: number,
): { readonly state: PinAttemptState; readonly exhausted: boolean } {
  const fails = state.fails + 1;
  const exhausted = fails >= MAX_PIN_FAILS;
  return {
    state: {
      fails,
      cooldownUntilMs: nowMs + PIN_COOLDOWN_MS,
      lockedUntilMs: exhausted ? nowMs + PIN_LOCK_MS : state.lockedUntilMs,
    },
    exhausted,
  };
}

/**
 * 지금 키 입력이 차단돼 있는가(쿨다운 또는 잠금).
 * **경계는 해제 쪽이다** — `nowMs === cooldownUntilMs`면 이미 풀린 것으로 본다.
 */
export function isPinInputBlocked(state: PinAttemptState, nowMs: number): boolean {
  return nowMs < state.cooldownUntilMs || nowMs < state.lockedUntilMs;
}

/** 잠금 남은 시간(ms). 잠금이 없거나 지났으면 0. */
export function pinLockRemainingMs(lockedUntilMs: number, nowMs: number): number {
  if (!Number.isFinite(lockedUntilMs)) return 0;
  return Math.max(0, lockedUntilMs - nowMs);
}

/**
 * 남은 시간 표기 — *"4분 32초"* / *"5분"* / *"45초"*.
 * **올림**한다(1초 미만이 "0초"로 보이면 이미 풀린 것처럼 읽힌다).
 */
export function formatPinLockRemaining(remainingMs: number): string {
  const totalSeconds = Math.max(0, Math.ceil(remainingMs / 1000));
  const minutes = Math.floor(totalSeconds / 60);
  const seconds = totalSeconds % 60;
  if (minutes === 0) return `${seconds}초`;
  return seconds === 0 ? `${minutes}분` : `${minutes}분 ${seconds}초`;
}

/** `localStorage["mcphoto.pinLock.v1"]`의 값 형태. 자격증명이 아니다(M2와 무충돌). */
export interface PinLockRecord {
  /** 잠금 해제 시각(epoch ms). */
  readonly until: number;
  readonly fails: number;
}

/**
 * 저장된 잠금 레코드를 해석한다.
 *
 * - 형식이 아니거나 유한수가 아니면 `null`(손상 → 잠금 없음)
 * - `until <= nowMs`면 `null`(만료)
 * - **`until - nowMs > PIN_LOCK_MS`면 `nowMs + PIN_LOCK_MS`로 clamp**
 *   → 시스템 시각을 과거로 되돌린 기기(또는 손상된 큰 값)가 키오스크를 **영구히 잠그지 못한다**.
 */
export function parsePinLockRecord(raw: unknown, nowMs: number): PinLockRecord | null {
  if (typeof raw !== "object" || raw === null || Array.isArray(raw)) return null;
  const record = raw as { until?: unknown; fails?: unknown };
  if (typeof record.until !== "number" || !Number.isFinite(record.until)) return null;

  if (record.until <= nowMs) return null;

  const until = Math.min(record.until, nowMs + PIN_LOCK_MS);
  const fails =
    typeof record.fails === "number" && Number.isFinite(record.fails)
      ? clamp(Math.floor(record.fails), 0, MAX_PIN_FAILS)
      : MAX_PIN_FAILS;

  return { until, fails };
}

/** 5회 소진 시 기록할 레코드. */
export function buildPinLockRecord(nowMs: number, fails: number): PinLockRecord {
  return { until: nowMs + PIN_LOCK_MS, fails };
}
