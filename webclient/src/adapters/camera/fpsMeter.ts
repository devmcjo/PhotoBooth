/**
 * fps 측정 — 04 §3
 *
 * Ready 게이트의 `fps > 0` 조건은 **최근 1초 윈도우의 가공 완료 프레임 수**다
 * (획득 수가 아니라 **가공 완료** 기준 — 가공이 막히면 프리뷰가 멈춘 것이다).
 *
 * 순수 계산이라 단위 테스트로 고정한다. 시각은 주입받는다.
 */

export const FPS_WINDOW_MS = 1000;

export interface FpsMeter {
  /** 가공 완료 1건 기록. */
  mark(now: number): void;
  /** 최근 1초 윈도우의 프레임 수. */
  fps(now: number): number;
  /** 누적 가공 완료 수(Ready 게이트의 프레임 수 조건). */
  readonly total: number;
  reset(): void;
}

export function createFpsMeter(windowMs: number = FPS_WINDOW_MS): FpsMeter {
  let timestamps: number[] = [];
  let total = 0;

  function prune(now: number): void {
    const cutoff = now - windowMs;
    // 오래된 것만 앞에서 잘라낸다(윈도우가 1초라 배열이 짧다).
    let firstFresh = 0;
    while (firstFresh < timestamps.length && timestamps[firstFresh]! <= cutoff) firstFresh++;
    if (firstFresh > 0) timestamps = timestamps.slice(firstFresh);
  }

  return {
    get total() {
      return total;
    },

    mark(now) {
      timestamps.push(now);
      total++;
      prune(now);
    },

    fps(now) {
      prune(now);
      return timestamps.length;
    },

    reset() {
      timestamps = [];
      total = 0;
    },
  };
}
