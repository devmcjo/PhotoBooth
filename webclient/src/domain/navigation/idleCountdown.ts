/**
 * 유휴 경고 카운트다운 — Windows `Navigation/IdleCountdown.cs` 이식 (analysis/13 §7)
 *
 * Windows는 가변 클래스지만 웹은 **불변 값 + 순수 함수**로 이식한다(Zustand 스토어에 그대로 담기 위함).
 * 타이머 자체는 셸이 구동하고 감소·완료·리셋 규칙만 여기에 둔다.
 *
 * ⚠️ 셸은 tick 누적으로 시간을 세지 않는다 — `performance.now()` 실경과 기반이어야 한다(WM3).
 *    이 모듈은 "1초 경과가 확정됐을 때" 호출되는 규칙일 뿐이다.
 */

export interface IdleCountdownState {
  readonly startSeconds: number;
  readonly remaining: number;
}

/** 시작값(초, 최소 1)으로 카운트다운을 만든다. */
export function createIdleCountdown(startSeconds: number): IdleCountdownState {
  const start = Math.max(1, startSeconds);
  return { startSeconds: start, remaining: start };
}

/** 카운트다운 완료(0 도달). */
export function isExpired(state: IdleCountdownState): boolean {
  return state.remaining <= 0;
}

export interface IdleTickResult {
  readonly state: IdleCountdownState;
  /** 이번 tick으로 0에 도달했는가(만료 전이 — 1회만 true). 이미 0이면 false. */
  readonly justExpired: boolean;
}

/** 1초 경과 반영. 남은 초를 1 줄이고(하한 0), 만료 전이 여부를 함께 돌려준다. */
export function tick(state: IdleCountdownState): IdleTickResult {
  if (state.remaining <= 0) return { state, justExpired: false };
  const remaining = state.remaining - 1;
  return {
    state: { startSeconds: state.startSeconds, remaining },
    justExpired: remaining === 0,
  };
}

/** 시작값으로 되돌림([이어서 진행하기] 또는 경고 해제). */
export function reset(state: IdleCountdownState): IdleCountdownState {
  return { startSeconds: state.startSeconds, remaining: state.startSeconds };
}
