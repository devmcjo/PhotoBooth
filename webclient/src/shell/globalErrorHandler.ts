import { logger } from "@adapters/storage/logStore";
import { STRINGS } from "@ui/strings";
import { shellStore } from "./shellStore";

/**
 * 전역 예외 복구 — M16 · 02 §9
 *
 * **크래시 대신 복구한다**: 로그 → 홈 복귀 → 토스트. **로그인은 유지**하고 촬영 데이터만 폐기한다.
 * 화이트스크린은 키오스크에서 최악의 실패다(손님 앞에서 아무 것도 못 한다).
 */

export interface ErrorHandlerHandle {
  uninstall(): void;
}

interface WindowLike {
  addEventListener(type: string, listener: (event: Event) => void): void;
  removeEventListener(type: string, listener: (event: Event) => void): void;
}

/** 같은 오류가 폭주할 때 홈 복귀를 반복하지 않도록 최소 간격을 둔다. */
const RECOVERY_COOLDOWN_MS = 3000;

export function installGlobalErrorHandler(
  target: WindowLike | undefined = typeof window !== "undefined"
    ? (window as unknown as WindowLike)
    : undefined,
  now: () => number = () => Date.now(),
): ErrorHandlerHandle {
  if (target === undefined) return { uninstall: () => undefined };

  // ⚠️ `0`으로 초기화하면 안 된다 — `now()`가 작은 값을 돌려주는 시계
  //    (`performance.now()`나 테스트 스텁)에서 **첫 오류가 쿨다운에 먹혀** 복구가 일어나지 않는다.
  let lastRecoveryAt = Number.NEGATIVE_INFINITY;

  function recover(kind: string, reason: string): void {
    logger.error(`처리되지 않은 ${kind}`, { reason });

    const current = now();
    if (current - lastRecoveryAt < RECOVERY_COOLDOWN_MS) {
      // 폭주 중이다. 로그만 남기고 홈 복귀를 반복하지 않는다.
      return;
    }
    lastRecoveryAt = current;

    void shellStore.getState().returnHome(`전역 예외 복구(${kind})`);
    shellStore.getState().toast("error", STRINGS.error.temporary);
  }

  const onError = (event: Event): void => {
    const errorEvent = event as ErrorEvent;
    recover("예외", errorEvent.message || String(errorEvent.error ?? "unknown"));
  };

  const onRejection = (event: Event): void => {
    const rejection = event as PromiseRejectionEvent;
    const reason = rejection.reason;
    recover("Promise 거부", reason instanceof Error ? reason.message : String(reason));
  };

  target.addEventListener("error", onError);
  target.addEventListener("unhandledrejection", onRejection);

  return {
    uninstall() {
      target.removeEventListener("error", onError);
      target.removeEventListener("unhandledrejection", onRejection);
    },
  };
}
