import { logger } from "@adapters/storage/logStore";
import { shellStore } from "@shell/shellStore";

/**
 * `Done` 자동 홈 복귀 — 03 §10 · analysis/13 §4.9
 *
 * ⚠️ **실경과 기반**이다. `setTimeout` 한 번에 맡기면 탭이 백그라운드로 갔을 때 스로틀돼
 *    일찍/늦게 깨어난다(WM3와 동종). 깨어날 때마다 `now()`로 재판정하고 남은 만큼 재무장한다.
 * ⚠️ **로그아웃하지 않는다**(M3). 홈 복귀가 촬영 데이터만 폐기한다.
 * ⚠️ `Done`은 유휴 감시 대상이 아니다 — 중복 홈 복귀가 생기지 않는다.
 */

export const DONE_AUTO_HOME_MS = 6_000;

export interface DoneAutoHomeDeps {
  /** 기본 `performance.now`. */
  readonly now?: () => number;
  readonly setTimer?: (fn: () => void, ms: number) => unknown;
  readonly clearTimer?: (handle: unknown) => void;
  readonly onExpire?: () => void;
  readonly target?: Pick<EventTarget, "addEventListener" | "removeEventListener"> | null;
  readonly isHidden?: () => boolean;
}

/**
 * 무장하고 **정리 함수**를 돌려준다. 언마운트에서 반드시 호출한다 —
 * 타이머와 `visibilitychange` 리스너를 **하나의 함수**가 함께 걷는다.
 */
export function startDoneAutoHome(deps: DoneAutoHomeDeps = {}): () => void {
  const now = deps.now ?? ((): number => performance.now());
  const setTimer = deps.setTimer ?? ((fn: () => void, ms: number): unknown => setTimeout(fn, ms));
  const clearTimer =
    deps.clearTimer ??
    ((handle: unknown): void => clearTimeout(handle as ReturnType<typeof setTimeout>));
  const onExpire =
    deps.onExpire ??
    ((): void => {
      void shellStore.getState().returnHome("완료 화면 자동 복귀");
    });
  const target =
    deps.target !== undefined ? deps.target : typeof document === "undefined" ? null : document;
  const isHidden =
    deps.isHidden ??
    ((): boolean => typeof document !== "undefined" && document.visibilityState === "hidden");

  const deadline = now() + DONE_AUTO_HOME_MS;
  let handle: unknown = null;
  let finished = false;

  const clear = (): void => {
    if (handle === null) return;
    clearTimer(handle);
    handle = null;
  };

  const check = (): void => {
    if (finished) return;
    const remaining = deadline - now();
    if (remaining <= 0) {
      finished = true;
      clear();
      onExpire();
      return;
    }
    // 스로틀로 일찍 깼다 — 남은 만큼 다시 무장한다(6초를 새로 세지 않는다).
    clear();
    handle = setTimer(check, remaining);
  };

  const onVisibilityChange = (): void => {
    // 탭이 다시 보이면 즉시 재판정한다(hidden 동안 시간이 다 갔을 수 있다).
    if (!isHidden()) check();
  };

  target?.addEventListener("visibilitychange", onVisibilityChange);
  handle = setTimer(check, DONE_AUTO_HOME_MS);
  logger.info("완료 화면 자동 복귀 무장", { afterMs: DONE_AUTO_HOME_MS });

  return () => {
    finished = true;
    clear();
    target?.removeEventListener("visibilitychange", onVisibilityChange);
  };
}
