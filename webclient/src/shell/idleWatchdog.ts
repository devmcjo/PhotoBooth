import { isSessionActive } from "@domain/navigation/stateMachine";
import { logger } from "@adapters/storage/logStore";
import { shellStore } from "./shellStore";

/**
 * 유휴 감시 — 02 §6 · analysis/13 §7
 *
 * ⚠️ **실경과(`performance.now()`) 기반이다**(WM3). `setInterval` tick을 세면 탭 스로틀링에서
 *    120초가 실제로는 몇 분이 된다. tick은 **표시 갱신용**이고 판정은 항상 델타로 한다.
 *
 * ⚠️ 만료는 **홈 복귀일 뿐 로그아웃이 아니다**(M3). `logout()`을 부르지 않는다.
 */

export const IDLE_TIMEOUT_MS = 120_000;
export const IDLE_COUNTDOWN_MS = 10_000;
export const IDLE_TICK_MS = 250;

/** 활동 신호 — capture 단계·passive로 듣는다(02 §6). */
const ACTIVITY_EVENTS = ["pointerdown", "keydown", "touchstart", "wheel"] as const;

export interface IdleWatchdogOptions {
  /** 실경과 시계. 기본 `performance.now`. */
  readonly now?: () => number;
  readonly timeoutMs?: number;
  readonly countdownMs?: number;
  readonly tickMs?: number;
  /** 이벤트를 붙일 대상. 기본 `window`. */
  readonly target?: Pick<EventTarget, "addEventListener" | "removeEventListener">;
}

export interface IdleWatchdog {
  start(): void;
  stop(): void;
  /** 활동 기록(외부에서 강제 갱신 — 예: visible 복귀 후 첫 입력). */
  noteActivity(): void;
  /** 경고 중 [이어서 진행하기]. 경고를 닫고 무동작 타이머를 재시작한다. */
  continueSession(): void;
  /** 남은 카운트다운 초(경고 중일 때만 의미). 표시용. */
  remainingSeconds(): number;
  /** 탭 복귀 시 즉시 재판정(이미 만료됐으면 바로 홈 복귀). */
  reevaluate(): void;
  readonly isWarning: boolean;
}

export function createIdleWatchdog(options: IdleWatchdogOptions = {}): IdleWatchdog {
  const now = options.now ?? (() => performance.now());
  const timeoutMs = options.timeoutMs ?? IDLE_TIMEOUT_MS;
  const countdownMs = options.countdownMs ?? IDLE_COUNTDOWN_MS;
  const tickMs = options.tickMs ?? IDLE_TICK_MS;
  const target = options.target ?? (typeof window !== "undefined" ? window : undefined);

  let lastActivityAt = now();
  let warningStartedAt: number | null = null;
  let timer: ReturnType<typeof setInterval> | null = null;
  let listening = false;

  const onActivity = (): void => {
    // 경고 표시 중 활동은 **무시한다** — 버튼으로만 해제한다(02 §6).
    if (warningStartedAt !== null) return;
    lastActivityAt = now();
  };

  function addListeners(): void {
    if (listening || target === undefined) return;
    for (const type of ACTIVITY_EVENTS) {
      target.addEventListener(type, onActivity, { capture: true, passive: true });
    }
    listening = true;
  }

  function removeListeners(): void {
    if (!listening || target === undefined) return;
    for (const type of ACTIVITY_EVENTS) {
      target.removeEventListener(type, onActivity, { capture: true });
    }
    listening = false;
  }

  function showWarning(at: number): void {
    warningStartedAt = at;
    // 유휴 경고는 모달 스택 최상단이다(다른 모달을 가려도 된다 — 02 §6.1).
    shellStore.getState().pushModal({ id: "idleWarning", dismissible: false });
    logger.info("유휴 경고 표시", { idleMs: Math.round(at - lastActivityAt) });
  }

  function expire(): void {
    warningStartedAt = null;
    shellStore.getState().popModal("idleWarning");
    // 로그아웃하지 않는다(M3).
    void shellStore.getState().returnHome("유휴 시간 초과");
  }

  function evaluate(): void {
    // 감시 대상 화면이 아니면 아무 것도 하지 않는다(설정·로그인·편집기는 제외).
    if (!isSessionActive(shellStore.getState().screen)) {
      if (warningStartedAt !== null) {
        warningStartedAt = null;
        shellStore.getState().popModal("idleWarning");
      }
      lastActivityAt = now();
      return;
    }

    const current = now();
    if (warningStartedAt !== null) {
      if (current - warningStartedAt >= countdownMs) expire();
      return;
    }
    if (current - lastActivityAt >= timeoutMs) showWarning(current);
  }

  return {
    get isWarning() {
      return warningStartedAt !== null;
    },

    start() {
      if (timer !== null) return;
      lastActivityAt = now();
      warningStartedAt = null;
      addListeners();
      timer = setInterval(evaluate, tickMs);
    },

    stop() {
      if (timer !== null) {
        clearInterval(timer);
        timer = null;
      }
      removeListeners();
      if (warningStartedAt !== null) {
        warningStartedAt = null;
        shellStore.getState().popModal("idleWarning");
      }
    },

    noteActivity() {
      onActivity();
    },

    continueSession() {
      warningStartedAt = null;
      lastActivityAt = now();
      shellStore.getState().popModal("idleWarning");
      logger.info("유휴 경고에서 계속 진행");
    },

    remainingSeconds() {
      if (warningStartedAt === null) return Math.ceil(countdownMs / 1000);
      const elapsed = now() - warningStartedAt;
      return Math.max(0, Math.ceil((countdownMs - elapsed) / 1000));
    },

    reevaluate() {
      evaluate();
    },
  };
}

let singleton: IdleWatchdog | null = null;

export function getIdleWatchdog(): IdleWatchdog {
  singleton ??= createIdleWatchdog();
  return singleton;
}

export function setIdleWatchdogForTests(watchdog: IdleWatchdog | null): void {
  singleton = watchdog;
}
