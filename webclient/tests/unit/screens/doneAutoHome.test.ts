import { beforeEach, describe, expect, it } from "vitest";
import { DONE_AUTO_HOME_MS, startDoneAutoHome } from "@screens/done/doneAutoHome";
import {
  attachLogStore,
  createLogStore,
  createMemoryLogSink,
  detachLogStore,
} from "@adapters/storage/logStore";

/**
 * `Done` 자동 홈 복귀 — 03 §10
 *
 * 실경과 기반이라는 것이 규격이다(탭 스로틀 방어 — WM3와 동종).
 * 시계·타이머·이벤트 타깃을 전부 주입해 node에서 결정적으로 검증한다.
 */

interface Harness {
  readonly expires: number;
  readonly deferred: { fn: () => void; ms: number }[];
  readonly cleared: number;
  readonly added: string[];
  readonly removed: string[];
  set(ms: number): void;
  hide(hidden: boolean): void;
  /** 마지막으로 무장된 타이머를 발화시킨다. */
  fire(): void;
  emitVisibility(): void;
  start(): () => void;
}

function harness(): Harness {
  let clock = 0;
  let hidden = false;
  let cleared = 0;
  let expires = 0;
  const deferred: { fn: () => void; ms: number }[] = [];
  const added: string[] = [];
  const removed: string[] = [];
  const listeners: (() => void)[] = [];

  const h: Harness = {
    deferred,
    added,
    removed,
    get expires() {
      return expires;
    },
    get cleared() {
      return cleared;
    },
    set(ms) {
      clock = ms;
    },
    hide(next) {
      hidden = next;
    },
    fire() {
      const last = deferred.at(-1);
      last?.fn();
    },
    emitVisibility() {
      for (const listener of [...listeners]) listener();
    },
    start() {
      return startDoneAutoHome({
        now: () => clock,
        setTimer: (fn, ms) => {
          deferred.push({ fn, ms });
          return deferred.length;
        },
        clearTimer: () => {
          cleared++;
        },
        onExpire: () => {
          expires++;
        },
        isHidden: () => hidden,
        target: {
          addEventListener: (type: string, listener: EventListenerOrEventListenerObject | null) => {
            added.push(type);
            if (typeof listener === "function") listeners.push(() => listener(new Event(type)));
          },
          removeEventListener: (type: string) => {
            removed.push(type);
            listeners.length = 0;
          },
        },
      });
    },
  };
  return h;
}

beforeEach(() => {
  detachLogStore();
  attachLogStore(createLogStore({ sink: createMemoryLogSink(), now: () => 0 }));
});

describe("doneAutoHome — 6초 실경과", () => {
  it("규격 대기 시간이 6초다", () => {
    expect(DONE_AUTO_HOME_MS).toBe(6_000);
  });

  it("6초가 지나면 정확히 1회 복귀한다", () => {
    const h = harness();
    const stop = h.start();

    expect(h.deferred[0]!.ms).toBe(6_000);
    expect(h.expires).toBe(0);

    h.set(6_000);
    h.fire();

    expect(h.expires).toBe(1);
    stop();
  });

  it("타이머가 일찍 깨면 복귀하지 않고 남은 만큼 재무장한다(스로틀 방어)", () => {
    const h = harness();
    const stop = h.start();

    h.set(3_000);
    h.fire();

    expect(h.expires).toBe(0);
    expect(h.deferred).toHaveLength(2);
    // 6초를 새로 세지 않는다 — 남은 3초만 다시 잰다.
    expect(h.deferred[1]!.ms).toBe(3_000);

    h.set(6_000);
    h.fire();
    expect(h.expires).toBe(1);
    stop();
  });

  it("만료 후 타이머가 또 발화해도 두 번 복귀하지 않는다", () => {
    const h = harness();
    const stop = h.start();

    h.set(6_000);
    h.fire();
    h.fire();

    expect(h.expires).toBe(1);
    stop();
  });
});

describe("doneAutoHome — 탭 hidden 복귀", () => {
  it("hidden 동안 시간이 다 갔으면 visible 즉시 복귀한다", () => {
    const h = harness();
    const stop = h.start();

    h.hide(true);
    h.set(10_000);
    h.emitVisibility(); // 아직 hidden → 판정하지 않는다
    expect(h.expires).toBe(0);

    h.hide(false);
    h.emitVisibility();
    expect(h.expires).toBe(1);
    stop();
  });

  it("visible 복귀 시점에 시간이 남았으면 재무장만 한다", () => {
    const h = harness();
    const stop = h.start();

    h.set(2_000);
    h.emitVisibility();

    expect(h.expires).toBe(0);
    expect(h.deferred.at(-1)!.ms).toBe(4_000);
    stop();
  });

  it("visibilitychange를 구독한다", () => {
    const h = harness();
    const stop = h.start();
    expect(h.added).toEqual(["visibilitychange"]);
    stop();
  });
});

describe("doneAutoHome — 정리", () => {
  it("정리 후에는 시계를 아무리 돌려도 복귀하지 않는다", () => {
    const h = harness();
    const stop = h.start();

    stop();
    h.set(60_000);
    h.fire();
    h.emitVisibility();

    expect(h.expires).toBe(0);
  });

  it("타이머와 리스너를 함께 걷는다(누수 방지)", () => {
    const h = harness();
    const stop = h.start();

    stop();

    expect(h.cleared).toBeGreaterThanOrEqual(1);
    expect(h.removed).toEqual(["visibilitychange"]);
  });

  it("정리를 두 번 불러도 안전하다", () => {
    const h = harness();
    const stop = h.start();
    stop();
    expect(() => stop()).not.toThrow();
    expect(h.expires).toBe(0);
  });
});
