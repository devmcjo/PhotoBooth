import { readdirSync, readFileSync, statSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { afterEach, beforeEach, describe, expect, it } from "vitest";
import {
  createTimelapseService,
  type TimelapseServiceDeps,
} from "@adapters/encode/timelapseService";
import type { TimelapseResult } from "@adapters/encode/timelapseEncoder";
import type { SpoolFrame, SpoolOptions } from "@adapters/camera/cameraTypes";
import type { SessionWorkspace } from "@adapters/storage/sessionWorkspace";
import { TIMELAPSE_SPOOL_MAX_FRAMES } from "@domain/capture/timelapseSpool";
import {
  attachLogStore,
  createLogStore,
  createMemoryLogSink,
  detachLogStore,
} from "@adapters/storage/logStore";

/**
 * 수집 수명 + 오케스트레이션 — 가짜 카메라·작업 공간·인코더로 검증한다.
 */

class FakeCamera {
  configs: SpoolOptions[] = [];
  private listeners = new Set<(frame: SpoolFrame) => void>();

  configureTimelapseSpool(options: SpoolOptions): void {
    this.configs.push(options);
  }
  onTimelapseFrame(listener: (frame: SpoolFrame) => void): () => void {
    this.listeners.add(listener);
    return () => this.listeners.delete(listener);
  }
  emit(width = 810, height = 1080): void {
    for (const listener of this.listeners) {
      listener({ blob: new Blob(["jpeg"]), width, height });
    }
  }
  get subscriberCount(): number {
    return this.listeners.size;
  }
}

class FakeWorkspace implements SessionWorkspace {
  readonly sessionId = "s-1";
  writes: number[] = [];
  removed: string[] = [];
  writeResult = true;
  /** `listTimelapseFrames()`가 돌려줄 이름 수. 기본은 기록된 수와 같다. */
  listOverride: number | null = null;

  async writeCut(): Promise<boolean> {
    return true;
  }
  async writeTimelapseFrame(index: number): Promise<boolean> {
    this.writes.push(index);
    return this.writeResult;
  }
  async listTimelapseFrames(): Promise<string[]> {
    const count = this.listOverride ?? this.writes.length;
    return Array.from({ length: count }, (_, i) => `${String(i).padStart(5, "0")}.jpg`);
  }
  async removeTimelapseFrame(name: string): Promise<boolean> {
    this.removed.push(name);
    return true;
  }
  async writeComposed(): Promise<boolean> {
    return true;
  }
  async readFile(): Promise<File | null> {
    return null;
  }
  async discard(): Promise<boolean> {
    return true;
  }
}

/** 마이크로태스크 큐를 비운다(스풀 쓰기는 비동기다). */
async function settle(times = 4): Promise<void> {
  for (let i = 0; i < times; i++) await Promise.resolve();
}

const RESULT: TimelapseResult = {
  blob: new Blob(["mp4"], { type: "video/mp4" }),
  path: "webcodecs",
  width: 810,
  height: 1080,
  frameCount: 375,
  durationSec: 12.5,
  speedFactor: 3.04,
  bytes: 3,
  elapsedMs: 1200,
};

interface Harness {
  readonly service: ReturnType<typeof createTimelapseService>;
  readonly camera: FakeCamera;
  readonly workspace: FakeWorkspace;
  readonly encodeCalls: { actualSeconds: number; size: { width: number; height: number } }[];
  readonly aborts: { count: number };
  clock: { value: number };
}

function harness(
  options: {
    encode?: TimelapseServiceDeps["encode"];
  } = {},
): Harness {
  const camera = new FakeCamera();
  const workspace = new FakeWorkspace();
  const clock = { value: 0 };
  const encodeCalls: Harness["encodeCalls"] = [];
  const aborts = { count: 0 };

  const service = createTimelapseService({
    camera,
    now: () => clock.value,
    client: {
      run: async () => ({ error: "미사용", stats: null }),
      abort: () => {
        aborts.count++;
      },
    },
    encode:
      options.encode ??
      (async (input) => {
        encodeCalls.push({ actualSeconds: input.actualSeconds, size: input.size });
        return RESULT;
      }),
  });

  return { service, camera, workspace, encodeCalls, aborts, clock };
}

beforeEach(() => {
  attachLogStore(createLogStore({ sink: createMemoryLogSink(), now: () => 0 }));
});

afterEach(() => {
  detachLogStore();
});

describe("timelapseService — 수집 수명([trigger])", () => {
  it("startCollection이 스풀을 켜고 66.67ms 간격을 통지한다", () => {
    const h = harness();
    h.service.startCollection(h.workspace);
    expect(h.camera.configs).toHaveLength(1);
    expect(h.camera.configs[0]!.enabled).toBe(true);
    expect(h.camera.configs[0]!.intervalMs).toBeCloseTo(66.667, 3);
    expect(h.camera.configs[0]!.quality).toBe(0.8);
    expect(h.service.stats().collecting).toBe(true);
  });

  it("멱등이다 — StrictMode 이중 마운트에서 두 번 시작하지 않는다", () => {
    const h = harness();
    h.service.startCollection(h.workspace);
    h.service.startCollection(h.workspace);
    expect(h.camera.configs).toHaveLength(1);
    expect(h.camera.subscriberCount).toBe(1);
  });

  it("프레임 도착이 0부터 순차로 기록된다", async () => {
    const h = harness();
    h.service.startCollection(h.workspace);
    for (let i = 0; i < 3; i++) {
      h.camera.emit();
      await settle();
    }
    expect(h.workspace.writes).toEqual([0, 1, 2]);
    expect(h.service.stats().spooled).toBe(3);
  });

  it("인플라이트 중 도착한 프레임은 드롭한다(쓰기 호출 없음)", async () => {
    const h = harness();
    h.service.startCollection(h.workspace);
    // 두 프레임을 마이크로태스크 정산 없이 연달아 흘린다.
    h.camera.emit();
    h.camera.emit();
    h.camera.emit();
    await settle();
    expect(h.workspace.writes).toEqual([0]);
    expect(h.service.stats().droppedSpool).toBe(2);
  });

  it("쓰기 실패도 드롭으로 집계한다(성공 오인 금지 — M4)", async () => {
    const h = harness();
    h.workspace.writeResult = false;
    h.service.startCollection(h.workspace);
    h.camera.emit();
    await settle();
    expect(h.service.stats().spooled).toBe(0);
    expect(h.service.stats().droppedSpool).toBe(1);
  });

  it("stopCollection이 실경과를 초로 확정하고 스풀을 끈다", () => {
    const h = harness();
    h.service.startCollection(h.workspace);
    h.clock.value = 38_000;
    h.service.stopCollection();

    expect(h.service.stats().collecting).toBe(false);
    expect(h.service.stats().elapsedSec).toBe(38);
    expect(h.camera.configs.at(-1)!.enabled).toBe(false);
    expect(h.camera.subscriberCount).toBe(0);
  });

  it("stopCollection 후 도착한 프레임은 기록하지 않는다", async () => {
    const h = harness();
    h.service.startCollection(h.workspace);
    h.camera.emit();
    await settle();
    h.service.stopCollection();

    // 구독은 이미 해제됐지만, 늦게 도착한 통지도 방어한다.
    h.camera.emit();
    await settle();
    expect(h.workspace.writes).toEqual([0]);
  });

  it("stopCollection은 멱등이다(경과를 다시 재지 않는다)", () => {
    const h = harness();
    h.service.startCollection(h.workspace);
    h.clock.value = 5_000;
    h.service.stopCollection();
    h.clock.value = 90_000;
    h.service.stopCollection();
    expect(h.service.stats().elapsedSec).toBe(5);
  });
});

describe("timelapseService — 스풀 상한 솎아내기", () => {
  it("900장에 도달하면 450장을 지우고 간격을 2배로 재통지한다", async () => {
    const h = harness();
    h.service.startCollection(h.workspace);
    // 상한 도달 상태를 만든다(실제 900회 쓰기는 느리다).
    h.workspace.listOverride = TIMELAPSE_SPOOL_MAX_FRAMES;
    for (let i = 0; i < TIMELAPSE_SPOOL_MAX_FRAMES; i++) {
      h.camera.emit();
      await settle();
    }
    // 솎아내기는 450회 삭제를 순차로 await한다 — 매크로태스크 한 번으로 전부 비운다.
    await new Promise((resolve) => setTimeout(resolve, 0));

    expect(h.workspace.removed).toHaveLength(450);
    expect(h.service.stats().spooled).toBe(450);
    expect(h.service.stats().decimations).toBeGreaterThanOrEqual(1);
    expect(h.service.stats().intervalMs).toBeCloseTo(133.333, 3);
    const last = h.camera.configs.at(-1)!;
    expect(last.enabled).toBe(true);
    expect(last.intervalMs).toBeCloseTo(133.333, 3);
  });
});

describe("timelapseService — finish()", () => {
  it("멱등이다(2회 호출에 encode는 1회)", async () => {
    const h = harness();
    h.service.startCollection(h.workspace);
    h.camera.emit();
    await settle();
    h.clock.value = 38_000;
    h.service.stopCollection();

    const first = await h.service.finish();
    const second = await h.service.finish();
    expect(first).toBe(RESULT);
    expect(second).toBe(RESULT);
    expect(h.encodeCalls).toHaveLength(1);
    expect(h.service.current()).toBe(RESULT);
  });

  it("동시 호출 2건이 같은 Promise에 합류한다", async () => {
    const h = harness();
    h.service.startCollection(h.workspace);
    h.camera.emit();
    await settle();
    h.clock.value = 38_000;
    h.service.stopCollection();

    const [a, b] = await Promise.all([h.service.finish(), h.service.finish()]);
    expect(a).toBe(RESULT);
    expect(b).toBe(RESULT);
    expect(h.encodeCalls).toHaveLength(1);
  });

  it("아직 수집 중이면 먼저 stopCollection한다", async () => {
    const h = harness();
    h.service.startCollection(h.workspace);
    h.camera.emit();
    await settle();
    h.clock.value = 12_000;

    await h.service.finish();
    expect(h.service.stats().collecting).toBe(false);
    expect(h.encodeCalls[0]!.actualSeconds).toBe(12);
  });

  it("**size는 카메라가 아니라 마지막 스풀 프레임에서 온다**", async () => {
    // Result 시점에는 이미 `camera.stop()` 이후라 `processedSize()`가 null이다.
    const h = harness();
    h.service.startCollection(h.workspace);
    h.camera.emit(1443, 1081);
    await settle();
    h.clock.value = 38_000;
    h.service.stopCollection();

    await h.service.finish();
    expect(h.encodeCalls[0]!.size).toEqual({ width: 1443, height: 1081 });
  });

  it("encode가 throw해도 null을 돌려주고 던지지 않는다", async () => {
    const h = harness({
      encode: async () => {
        throw new Error("인코더 붕괴");
      },
    });
    h.service.startCollection(h.workspace);
    h.camera.emit();
    await settle();
    h.service.stopCollection();

    await expect(h.service.finish()).resolves.toBeNull();
    expect(h.service.current()).toBeNull();
  });

  it("수집한 적이 없으면 null이다(인코더를 부르지 않는다)", async () => {
    const h = harness();
    expect(await h.service.finish()).toBeNull();
    expect(h.encodeCalls).toHaveLength(0);
  });

  it("스풀 프레임이 한 장도 없으면(크기 미상) null이다", async () => {
    const h = harness();
    h.service.startCollection(h.workspace);
    h.clock.value = 38_000;
    h.service.stopCollection();
    expect(await h.service.finish()).toBeNull();
    expect(h.encodeCalls).toHaveLength(0);
  });
});

describe("timelapseService — stop()(홈 복귀 훅)", () => {
  it("수집을 끊고 결과를 폐기하며 진행 중 인코딩을 abort한다", async () => {
    const h = harness();
    h.service.startCollection(h.workspace);
    h.camera.emit();
    await settle();
    h.clock.value = 38_000;
    await h.service.finish();
    expect(h.service.current()).toBe(RESULT);

    h.service.stop();
    expect(h.service.current()).toBeNull();
    expect(h.aborts.count).toBe(1);
    expect(h.service.stats().collecting).toBe(false);
    expect(h.service.stats().size).toBeNull();
    expect(h.service.stats().elapsedSec).toBeNull();
  });

  it("수집 중에 불러도 안전하다(멱등)", () => {
    const h = harness();
    h.service.startCollection(h.workspace);
    expect(() => {
      h.service.stop();
      h.service.stop();
    }).not.toThrow();
    expect(h.aborts.count).toBe(2);
  });
});

// ─────────────────────── 정적 불변식(15 §3.4 관례) ───────────────────────

describe("인코더 계층 정적 불변식", () => {
  const ENCODE_DIR = join(
    dirname(fileURLToPath(import.meta.url)),
    "..",
    "..",
    "..",
    "src",
    "adapters",
    "encode",
  );

  function encodeSources(): { name: string; code: string }[] {
    return readdirSync(ENCODE_DIR)
      .filter((entry) => entry.endsWith(".ts") && statSync(join(ENCODE_DIR, entry)).isFile())
      .map((entry) => ({ name: entry, code: readFileSync(join(ENCODE_DIR, entry), "utf8") }));
  }

  it("MP4 muxer 패키지를 import하는 파일은 encode.worker.ts **하나뿐**이다", () => {
    // 코어를 node 테스트 가능 상태로 기계적으로 고정한다.
    const offenders = encodeSources()
      .filter((file) => file.code.includes("mp4-muxer"))
      .map((file) => file.name);
    expect(offenders).toEqual(["encode.worker.ts"]);
  });

  it("Worker에서 도는 코어(webCodecsMp4.ts)에 로거 사용이 0건이다", () => {
    // Worker에는 로그 스토어가 붙지 않아 여기서 남긴 로그는 진단에 영원히 도달하지 않는다(F9).
    const code = readFileSync(join(ENCODE_DIR, "webCodecsMp4.ts"), "utf8");
    expect(code.includes("logStore")).toBe(false);
    expect(code.includes("logger.")).toBe(false);
  });

  it("encode.worker.ts가 OPFS에 쓰지 않는다(읽기 전용)", () => {
    // 쓰기는 opfsWriter Worker 전용이다 — 다른 곳에서 쓰면 iOS에서 전 저장이 실패한다.
    const code = readFileSync(join(ENCODE_DIR, "encode.worker.ts"), "utf8");
    expect(code.includes("createWritable")).toBe(false);
    expect(code.includes("createSyncAccessHandle")).toBe(false);
    expect(code.includes("logger")).toBe(false);
  });
});
