import { afterEach, beforeEach, describe, expect, it } from "vitest";
import {
  CUT_INTERVAL_MS,
  FLASH_DURATION_MS,
} from "@domain/capture/captureTiming";
import {
  CaptureCancelledError,
  createCaptureSequence,
  type CaptureSequence,
  type CaptureSequenceSettings,
} from "@screens/capture/captureSequence";
import {
  attachLogStore,
  createLogStore,
  createMemoryLogSink,
  detachLogStore,
} from "@adapters/storage/logStore";

/**
 * 촬영 시퀀스 — 03 §6.1의 **순서가 규격**이다.
 * 가상 시계 + 가상 delay로 실제 대기 없이 순서·타이밍을 관측한다.
 */

interface Harness {
  sequence: CaptureSequence;
  /** 발생 순서대로 기록된 이벤트. */
  readonly events: string[];
  readonly written: number[];
  clock: number;
  stillResult: Blob | null;
  writeResult: boolean;
}

function harness(overrides: { thumbnails?: boolean } = {}): Harness {
  const state: Harness = {
    events: [],
    written: [],
    clock: 0,
    stillResult: new Blob(["jpeg"]),
    writeResult: true,
    sequence: null as unknown as CaptureSequence,
  };

  state.sequence = createCaptureSequence({
    captureStill: async () => {
      state.events.push("capture");
      return state.stillResult;
    },
    writeCut: async (index) => {
      state.events.push(`write:${index}`);
      if (state.writeResult) state.written.push(index);
      return state.writeResult;
    },
    cutFileName: (index) => `cut${index}.jpg`,
    ...(overrides.thumbnails === true
      ? {
          makeThumbnail: async () => {
            state.events.push("thumb");
            return undefined;
          },
        }
      : {}),
    now: () => state.clock,
    delay: async (ms) => {
      state.events.push(`delay:${ms}`);
      // 가상 시계를 전진시킨다 — 실경과 기반 카운트다운이 이 값을 본다.
      state.clock += ms;
    },
    onCountdown: (remaining) => state.events.push(`countdown:${remaining}`),
    onFlash: (on) => state.events.push(`flash:${on ? "on" : "off"}`),
    onCutCaptured: (cut) => state.events.push(`captured:${cut.index}`),
    playShutter: () => state.events.push("shutter"),
  });

  return state;
}

/** 마지막 플래시 이벤트(없으면 undefined). */
function lastFlash(events: readonly string[]): string | undefined {
  return events.filter((e) => e.startsWith("flash:")).at(-1);
}

const SETTINGS: CaptureSequenceSettings = {
  countdownSec: 1,
  flash: true,
  shutterSound: true,
};

beforeEach(() => {
  attachLogStore(createLogStore({ sink: createMemoryLogSink(), now: () => 0 }));
});

afterEach(() => {
  detachLogStore();
});

describe("captureSequence — 컷 루프 순서(03 §6.1)", () => {
  it("a→f 순서를 정확히 지킨다", async () => {
    const h = harness();
    await h.sequence.run(1, SETTINGS);

    // 카운트다운 → 플래시 on → 120ms → 셔터음 → 캡처 → 저장 → 플래시 off
    const order = h.events.filter((e) => !e.startsWith("countdown") && !e.startsWith("delay:100"));
    expect(order).toEqual([
      "flash:on",
      `delay:${FLASH_DURATION_MS}`,
      "shutter",
      "capture",
      "write:1",
      "captured:1",
      "flash:off",
    ]);
  });

  it("플래시 off는 **캡처 후**다(캡처 순간 화면이 하얗다)", async () => {
    const h = harness();
    await h.sequence.run(1, SETTINGS);
    expect(h.events.indexOf("flash:off")).toBeGreaterThan(h.events.indexOf("capture"));
  });

  it("컷 사이에 300ms를 두고, 마지막 컷 뒤에는 기다리지 않는다", async () => {
    const h = harness();
    await h.sequence.run(3, SETTINGS);
    const intervals = h.events.filter((e) => e === `delay:${CUT_INTERVAL_MS}`);
    expect(intervals).toHaveLength(2); // 3컷 → 사이 2번
  });

  it("설정이 off면 플래시·셔터음을 재현하지 않는다(불필요한 지연도 없다)", async () => {
    const h = harness();
    await h.sequence.run(1, { countdownSec: 1, flash: false, shutterSound: false });
    expect(h.events).not.toContain("flash:on");
    expect(h.events).not.toContain("shutter");
    expect(h.events).not.toContain(`delay:${FLASH_DURATION_MS}`);
  });

  it("썸네일 생성은 저장 뒤에 일어나고 실패해도 컷이 유효하다", async () => {
    const h = harness({ thumbnails: true });
    const cuts = await h.sequence.run(1, SETTINGS);
    expect(h.events.indexOf("thumb")).toBeGreaterThan(h.events.indexOf("write:1"));
    expect(cuts).toHaveLength(1);
  });
});

describe("captureSequence — 카운트다운(실경과 기반 — WM3)", () => {
  it("설정 초만큼 세고 0으로 끝난다", async () => {
    const h = harness();
    await h.sequence.run(1, { ...SETTINGS, countdownSec: 3 });
    const counts = h.events.filter((e) => e.startsWith("countdown:"));
    expect(counts[0]).toBe("countdown:3");
    expect(counts.at(-1)).toBe("countdown:0");
  });

  it("tick 수가 아니라 실경과로 끝난다 — 스로틀링에서도 정확하다", async () => {
    const h = harness();
    // delay가 요청보다 오래 걸린 것처럼 시계를 크게 밀어도 1초면 끝난다.
    const sequence = createCaptureSequence({
      captureStill: async () => new Blob(["x"]),
      writeCut: async () => true,
      cutFileName: (i) => `cut${i}.jpg`,
      now: () => h.clock,
      delay: async () => {
        h.clock += 900; // 요청은 100ms인데 실제로 900ms가 흘렀다
        h.events.push("slow-delay");
      },
      onCountdown: (r) => h.events.push(`countdown:${r}`),
      onFlash: () => undefined,
      onCutCaptured: () => undefined,
      playShutter: () => undefined,
    });

    await sequence.run(1, { countdownSec: 1, flash: false, shutterSound: false });
    // 900ms 한 번이면 아직 남고, 두 번째에 넘어간다 — tick을 세었다면 10번이 필요했을 것이다.
    expect(h.events.filter((e) => e === "slow-delay")).toHaveLength(2);
  });

  it("남은 시간이 tick보다 짧으면 그만큼만 기다린다", async () => {
    const h = harness();
    await h.sequence.run(1, { ...SETTINGS, countdownSec: 0.25 });
    const waits = h.events.filter((e) => e.startsWith("delay:")).map((e) => Number(e.slice(6)));
    // 100·100·50 → 마지막은 남은 만큼만
    expect(waits.slice(0, 3)).toEqual([100, 100, 50]);
  });
});

describe("captureSequence — [바로 촬영]", () => {
  it("남은 카운트다운을 건너뛰고 즉시 셔터로 간다", async () => {
    const h = harness();
    const promise = h.sequence.run(1, { ...SETTINGS, countdownSec: 10 });
    // 첫 tick 뒤에 요청
    await Promise.resolve();
    h.sequence.skipCountdown();
    await promise;

    // 10초를 다 세지 않았다 — countdown 이벤트가 몇 개뿐이다.
    const counts = h.events.filter((e) => e.startsWith("countdown:"));
    expect(counts.length).toBeLessThan(10);
    expect(h.written).toEqual([1]);
  });

  it("**매 컷** 사용할 수 있다(한 번 쓰면 소진되지 않는다)", async () => {
    const h = harness();
    const promise = h.sequence.run(2, { ...SETTINGS, countdownSec: 10 });

    await Promise.resolve();
    h.sequence.skipCountdown();
    // 첫 컷이 끝난 뒤 두 번째 컷에서도 다시 쓸 수 있다.
    while (h.written.length < 1) await Promise.resolve();
    h.sequence.skipCountdown();
    await promise;

    expect(h.written).toEqual([1, 2]);
  });

  it("시퀀스가 돌지 않을 때의 요청은 무시된다(다음 세션으로 새지 않는다)", async () => {
    const h = harness();
    h.sequence.skipCountdown(); // 실행 전
    await h.sequence.run(1, { ...SETTINGS, countdownSec: 1 });
    // 카운트다운이 정상적으로 소비됐다.
    expect(h.events.filter((e) => e.startsWith("countdown:")).length).toBeGreaterThan(1);
  });
});

describe("captureSequence — 취소(WM4)", () => {
  it("취소하면 CaptureCancelledError를 던진다", async () => {
    const h = harness();
    const promise = h.sequence.run(6, { ...SETTINGS, countdownSec: 10 });
    await Promise.resolve();
    h.sequence.cancel();
    await expect(promise).rejects.toBeInstanceOf(CaptureCancelledError);
  });

  it("어떤 경로로 끝나도 플래시가 켜진 채 남지 않는다", async () => {
    // 카운트다운 중 취소 — 플래시는 켜진 적이 없다.
    const duringCountdown = harness();
    const p1 = duringCountdown.sequence.run(6, { ...SETTINGS, countdownSec: 10 });
    await Promise.resolve();
    duringCountdown.sequence.cancel();
    await p1.catch(() => undefined);
    expect(lastFlash(duringCountdown.events)).not.toBe("flash:on");

    // 정상 완주 — on/off가 짝을 이룬다(멱등 토글이라 중복 통지가 없다).
    const completed = harness();
    await completed.sequence.run(2, { ...SETTINGS, countdownSec: 0.1 });
    const flashes = completed.events.filter((e) => e.startsWith("flash:"));
    expect(flashes).toEqual(["flash:on", "flash:off", "flash:on", "flash:off"]);
  });

  it("취소 후 isRunning이 false다", async () => {
    const h = harness();
    const promise = h.sequence.run(6, { ...SETTINGS, countdownSec: 10 });
    expect(h.sequence.isRunning).toBe(true);
    h.sequence.cancel();
    await promise.catch(() => undefined);
    expect(h.sequence.isRunning).toBe(false);
  });
});

describe("captureSequence — 실패 처리(M4 성공 오인 금지)", () => {
  it("스틸이 없으면 그 컷을 버리고 계속한다", async () => {
    const h = harness();
    h.stillResult = null;
    const cuts = await h.sequence.run(2, SETTINGS);
    expect(cuts).toHaveLength(0);
    expect(h.events).not.toContain("write:1");
  });

  it("OPFS 저장 실패는 컷으로 세지 않는다", async () => {
    const h = harness();
    h.writeResult = false;
    const cuts = await h.sequence.run(2, SETTINGS);
    expect(cuts).toHaveLength(0);
    // 저장을 시도는 했다(조용히 건너뛴 것이 아니다).
    expect(h.events).toContain("write:1");
  });
});

describe("captureSequence — 임의 컷 수 N(it17)", () => {
  it.each([1, 6, 7, 8, 9, 10])("%i컷을 하드코딩 없이 촬영한다", async (n) => {
    const h = harness();
    const cuts = await h.sequence.run(n, { ...SETTINGS, countdownSec: 0.1 });
    expect(cuts).toHaveLength(n);
    expect(h.written).toEqual(Array.from({ length: n }, (_, i) => i + 1));
  });

  it("0컷이면 아무 것도 하지 않는다", async () => {
    const h = harness();
    expect(await h.sequence.run(0, SETTINGS)).toEqual([]);
    expect(h.events.filter((e) => e === "capture")).toHaveLength(0);
  });
});

describe("captureTiming — 계약 상수(WD18)", () => {
  it("규격 값을 바꾸지 않는다", () => {
    expect(FLASH_DURATION_MS).toBe(120);
    expect(CUT_INTERVAL_MS).toBe(300);
  });
});
