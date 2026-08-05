import { nextFrameLoadDeadlineMs } from "@domain/frames/frameLoadPolicy";

/**
 * 프레임 로딩 대기 상한 타이머 — 03 §4.1 (무진행 30초 + 총 60초 2단)
 *
 * ⚠️ **실경과 기준이다**(WM3와 동종). `setTimeout` 발화 횟수를 누적하지 않고 매번
 *    `now() - startedAt`으로 다음 만기를 다시 계산한다 — 탭 백그라운드 스로틀로 타이머가 늘어져도
 *    총 상한이 부풀지 않는다(늘어짐은 대기를 **연장**할 뿐이고, 그 바깥 안전망은 실경과 기반 유휴
 *    감시다).
 * ⚠️ `visibilitychange`를 구독하지 않는다: ① 스로틀은 대기를 늘릴 뿐 짧게 만들지 않는다
 *    ② 복귀 시 유휴 감시가 이미 실경과로 재판정한다 ③ 화면 로컬 리스너를 늘리면 해제 누락 위험만 커진다.
 *
 * 시계·타이머를 **주입**받는 이유는 node에서 상한을 직접 검증하기 위함이다(15 §3.1).
 */

export interface LoadDeadline {
  /** 진행이 관측됐다 — 무진행 창을 재무장한다. 총 상한이 이미 소진됐으면 **즉시 취소**한다. */
  arm(): void;
  /** 타이머 해제(멱등). `finally`에서 무조건 부른다. */
  dispose(): void;
}

export interface LoadDeadlineDeps {
  /** `performance.now` 주입(테스트 결정성). 단조 증가 ms. */
  now(): number;
  abort(): void;
  setTimer(fn: () => void, ms: number): unknown;
  clearTimer(handle: unknown): void;
}

export function createLoadDeadline(deps: LoadDeadlineDeps): LoadDeadline {
  const startedAt = deps.now();
  let handle: unknown = null;
  let disposed = false;
  /** 마지막 `arm()`의 시각과 그때 예약한 만기(ms). 발화가 **정당한지** 실경과로 재검사한다. */
  let armedAt = startedAt;
  let armedDue = 0;

  function clear(): void {
    if (handle !== null) {
      deps.clearTimer(handle);
      handle = null;
    }
  }

  function fire(): void {
    handle = null;
    if (disposed) return;
    // 발화 시점에 **경과를 다시 잰다.** 예약한 시간이 실제로 지나지 않았다면(조기 발화·시계 되감김)
    // 남은 만큼 다시 예약한다 — 대기를 짧게 자르지 않는다. 지났으면 그대로 끊는다.
    const waited = deps.now() - armedAt;
    if (waited < armedDue) {
      handle = deps.setTimer(fire, armedDue - waited);
      return;
    }
    deps.abort();
  }

  function arm(): void {
    if (disposed) return;
    clear();
    const now = deps.now();
    const due = nextFrameLoadDeadlineMs(now - startedAt);
    if (due <= 0) {
      // 총 상한 도달 — 예약하지 않고 즉시 끊는다.
      deps.abort();
      return;
    }
    armedAt = now;
    armedDue = due;
    handle = deps.setTimer(fire, due);
  }

  function dispose(): void {
    disposed = true;
    clear();
  }

  return { arm, dispose };
}

/** 실제 배선(브라우저 시계·타이머). */
export function defaultLoadDeadline(abort: () => void): LoadDeadline {
  return createLoadDeadline({
    now: () => performance.now(),
    abort,
    setTimer: (fn, ms) => setTimeout(fn, ms),
    clearTimer: (handleValue) => clearTimeout(handleValue as ReturnType<typeof setTimeout>),
  });
}
