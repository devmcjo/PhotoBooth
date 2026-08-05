import {
  applyPinFailure,
  classifyPinSet,
  classifyPinVerify,
  isPinFormatValid,
  type PinAttemptState,
  type PinCallOutcome,
} from "@domain/auth/pinGatePolicy";
import { BackendError, NetworkError } from "@adapters/http/errors";
import { logger } from "@adapters/storage/logStore";
import { writePinLock, type PinLockRepo } from "@adapters/storage/pinLockRepo";

/**
 * PIN 제출 1회의 **전 경로** — 07 §6.2 · 06 §2.0
 *
 * React를 import하지 않는다. jsdom이 없어(15 §3.1 · F13) 컴포넌트에 판정을 넣으면
 * 영원히 검증되지 않기 때문이다. 모달은 **입력 버퍼와 타이머만** 소유한다.
 *
 * ⚠️ **PIN 값을 로그·반환값에 절대 싣지 않는다.** 로그 컨텍스트는
 *    `gateMode`·`failCount`·`attemptOutcome`·`errorStatus`만 쓴다 — `pin`·`newPin`·`currentPin`은
 *    마스킹 대상이라(`logPolicy`) 담아도 무의미하고, 담으려 이름을 바꾸면 **진짜로 샌다**(PIN-1).
 * ⚠️ 네트워크·서버 오류는 **실패 카운트에 세지 않고 게이트도 열지 않는다**(fail-closed).
 */

/** 모달이 문구 카탈로그(`STRINGS.pin.messages`)로 옮기는 키. 문구를 여기서 조립하지 않는다. */
export type PinMessageKey =
  | "mismatch"
  | "unavailable"
  | "alreadySet"
  | "invalidFormat"
  | "confirmMismatch";

export type PinAttemptMode = "verify" | "setup";

export type PinAttemptResult =
  | { readonly kind: "granted" }
  | { readonly kind: "retry"; readonly state: PinAttemptState; readonly message: PinMessageKey }
  | { readonly kind: "exhausted"; readonly state: PinAttemptState }
  /** verify 중 409 — 서버에 PIN이 없다. **실패로 세지 않는다.** */
  | { readonly kind: "switchToSetup" }
  /** setup 중 401(currentPin 미전송) — 서버에 이미 PIN이 있다(A5). **실패로 세지 않는다.** */
  | { readonly kind: "switchToVerify" }
  /** 네트워크·기타 오류. **실패 카운트 미가산 · 게이트 미개방.** */
  | { readonly kind: "unavailable"; readonly message: PinMessageKey };

export interface PinAttemptDeps {
  readonly mode: PinAttemptMode;
  /** `accountService.verifyMyPin`. */
  readonly verifyPin: (pin: string) => Promise<void>;
  /** `accountService.setMyPin`. */
  readonly setPin: (newPin: string, currentPin?: string) => Promise<void>;
  /**
   * 기존 PIN. 게이트의 최초 설정 플로우에서는 **항상 undefined**다
   * (보유 중이면 verify 모드로 가므로). Step 16의 "PIN 변경"이 값을 넣는 자리다.
   */
  readonly currentPin?: string;
  readonly now: () => number;
  readonly lock: PinLockRepo;
  /** `sessionStore.markPinSet` — 최초 설정 성공에만 부른다. */
  readonly markPinSet: () => void;
}

/** 예외를 도메인 판별 유니온으로 접는다. 도메인은 HTTP·예외 타입을 모른다. */
function toCallOutcome(err: unknown): PinCallOutcome {
  if (err instanceof BackendError) return { kind: "status", status: err.status };
  if (err instanceof NetworkError) return { kind: "network" };
  // `NotAuthenticatedError`(토큰 없음)도 여기로 온다 — 게이트를 열 근거가 없으므로 unavailable이다.
  return { kind: "network" };
}

function statusOf(outcome: PinCallOutcome): number | null {
  return outcome.kind === "status" ? outcome.status : null;
}

/** 불일치 1회 반영. 5회째면 기기 잠금을 기록하고 `exhausted`를 돌려준다. */
function registerFailure(
  state: PinAttemptState,
  deps: PinAttemptDeps,
  nowMs: number,
): PinAttemptResult {
  const { state: next, exhausted } = applyPinFailure(state, nowMs);
  if (exhausted) {
    // 저장 실패(false)여도 계속한다 — 세션 내 5회 제한은 이미 성립했다(fail-open — §3.7).
    writePinLock(deps.lock, nowMs, next.fails);
    logger.warn("PIN 시도 소진", { gateMode: deps.mode, failCount: next.fails });
    return { kind: "exhausted", state: next };
  }
  logger.warn("PIN 불일치", { gateMode: deps.mode, failCount: next.fails });
  return { kind: "retry", state: next, message: "mismatch" };
}

function unavailable(
  deps: PinAttemptDeps,
  outcome: PinCallOutcome,
  message: PinMessageKey = "unavailable",
): PinAttemptResult {
  logger.error("PIN을 확인할 수 없습니다", {
    gateMode: deps.mode,
    attemptOutcome: outcome.kind,
    errorStatus: statusOf(outcome),
  });
  return { kind: "unavailable", message };
}

/**
 * PIN 제출 1회. 서버 왕복은 **정확히 1회**다.
 *
 * 성공 시 기기 잠금 레코드를 지운다(카운터 초기화 — 07 §6.3).
 */
export async function runPinAttempt(
  state: PinAttemptState,
  pin: string,
  deps: PinAttemptDeps,
): Promise<PinAttemptResult> {
  // 형식 방어. 모달이 먼저 막지만 진입점이 하나라고 가정하지 않는다(M10 2중 가드).
  if (!isPinFormatValid(pin)) {
    return { kind: "unavailable", message: "invalidFormat" };
  }

  const sentCurrentPin = deps.mode === "setup" && deps.currentPin !== undefined;

  let outcome: PinCallOutcome;
  try {
    if (deps.mode === "verify") {
      await deps.verifyPin(pin);
    } else {
      await deps.setPin(pin, deps.currentPin);
    }
    outcome = { kind: "ok" };
  } catch (err) {
    outcome = toCallOutcome(err);
  }

  const nowMs = deps.now();

  if (deps.mode === "verify") {
    switch (classifyPinVerify(outcome)) {
      case "granted":
        deps.lock.clear();
        logger.info("PIN 확인 통과", { gateMode: deps.mode });
        return { kind: "granted" };
      case "mismatch":
        return registerFailure(state, deps, nowMs);
      case "unset":
        // 오류가 아니다 — 최초 설정 플로우로 전환한다(06 §2.0).
        logger.info("PIN 미설정 — 최초 설정으로 전환", { gateMode: deps.mode });
        return { kind: "switchToSetup" };
      default:
        return unavailable(deps, outcome);
    }
  }

  switch (classifyPinSet(outcome, sentCurrentPin)) {
    case "granted":
      // ⚠️ 이 호출이 없으면 다음 진입에서 다시 setup 모드가 뜨고 401 데드락이 된다(§3.6).
      deps.markPinSet();
      deps.lock.clear();
      logger.info("PIN 설정 완료", { gateMode: deps.mode });
      return { kind: "granted" };
    case "mismatch":
      return registerFailure(state, deps, nowMs);
    case "alreadySet":
      // 서버에 이미 PIN이 있다. 실패로 세지 않고 확인 모드로 돌린다(A5).
      logger.warn("PIN이 이미 설정돼 있음 — 확인 모드로 전환", { gateMode: deps.mode });
      return { kind: "switchToVerify" };
    case "invalid":
      return unavailable(deps, outcome, "invalidFormat");
    default:
      return unavailable(deps, outcome);
  }
}
