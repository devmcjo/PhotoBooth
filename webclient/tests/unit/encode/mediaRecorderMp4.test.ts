import { describe, expect, it } from "vitest";
import {
  encodeWithMediaRecorder,
  type CanvasRecorderPort,
  type MediaRecorderMp4Deps,
} from "@adapters/encode/mediaRecorderMp4";
import type { EncodeJob, TimelapseEncodeConfig } from "@adapters/encode/encodeProtocol";

/**
 * 경로 A 코어 — 가짜 포트·가짜 시계로 검증한다.
 * (브라우저 구현 `createCanvasRecorderPort`는 DOM이 필요해 V18 실측 대상이다.)
 */

const CONFIG: TimelapseEncodeConfig = {
  codec: "avc1.42001E",
  width: 810,
  height: 1080,
  bitrate: 5_000_000,
  framerate: 30,
};

function job(frameCount: number, durationUs = 33333): EncodeJob {
  return {
    dirPath: "sessions/s1/tl",
    names: Array.from({ length: frameCount }, (_, i) => `${String(i).padStart(5, "0")}.jpg`),
    timestampsUs: Array.from({ length: frameCount }, (_, i) => i * durationUs),
    frameDurationUs: durationUs,
    config: CONFIG,
  };
}

class FakePort implements CanvasRecorderPort {
  started = 0;
  pushed: Blob[] = [];
  disposed = 0;
  stopCalls: number[] = [];
  pushResult: (index: number) => boolean = () => true;
  stopResult: Blob | null = new Blob(["mp4"], { type: "video/mp4" });
  pushThrows = false;

  start(): void {
    this.started++;
  }
  async pushFrame(blob: Blob): Promise<boolean> {
    if (this.pushThrows) throw new Error("push 폭발");
    const ok = this.pushResult(this.pushed.length);
    this.pushed.push(blob);
    return ok;
  }
  async stop(timeoutMs: number): Promise<Blob | null> {
    this.stopCalls.push(timeoutMs);
    return this.stopResult;
  }
  dispose(): void {
    this.disposed++;
  }
}

interface Harness {
  readonly deps: MediaRecorderMp4Deps;
  readonly port: FakePort;
  readonly waits: number[];
  /** 가짜 시계. `delay(ms)`가 그만큼 전진시킨다. */
  clock: { value: number };
}

function harness(
  options: {
    port?: FakePort | null;
    loadFrame?: (name: string) => Promise<Blob | null>;
    /** delay가 요청보다 오래 걸리는 느린 기기를 흉내낸다. */
    delayFactor?: number;
  } = {},
): Harness {
  const port = options.port === undefined ? new FakePort() : options.port;
  const clock = { value: 1000 };
  const waits: number[] = [];

  const deps: MediaRecorderMp4Deps = {
    loadFrame: options.loadFrame ?? (async () => new Blob(["jpeg"])),
    createPort: () => port,
    now: () => clock.value,
    async delay(ms) {
      waits.push(ms);
      clock.value += ms * (options.delayFactor ?? 1);
    },
    stopTimeoutMs: 500,
  };

  return { deps, port: port as FakePort, waits, clock };
}

describe("encodeWithMediaRecorder — 정상 경로", () => {
  it("프레임을 순서대로 밀어 넣고 mp4를 돌려준다", async () => {
    const h = harness();
    const result = await encodeWithMediaRecorder(job(5), h.deps);

    expect(result.ok).toBe(true);
    expect(h.port.started).toBe(1);
    expect(h.port.pushed).toHaveLength(5);
    expect(h.port.stopCalls).toEqual([500]);
    if (result.ok) {
      expect(result.stats.encodedFrames).toBe(5);
      expect(result.stats.skippedFrames).toBe(0);
      expect(result.blob.type).toBe("video/mp4");
    }
  });

  it("페이싱이 **실경과 기준**이다 — 뒤처졌으면 기다리지 않는다", async () => {
    // delay가 요청의 9배 걸리는 느린 기기: 목표 시각을 이미 넘겼으므로 이후 대기가 사라진다.
    const h = harness({ delayFactor: 9 });
    await encodeWithMediaRecorder(job(6), h.deps);

    expect(h.waits[0]).toBeCloseTo(33.333, 2);
    // 첫 대기에서 300ms를 소모했다(9배) → 이후 목표 시각은 전부 이미 지났다 → 추가 대기 0회.
    // tick 누적 방식이었다면 프레임마다 33.3ms씩 6번을 더 기다려 길이가 늘어난다.
    expect(h.waits).toHaveLength(1);
    expect(h.port.pushed).toHaveLength(6);
  });

  it("정상 속도면 매 프레임 목표 간격만큼 기다린다(tick 누적이 아니다)", async () => {
    const h = harness();
    await encodeWithMediaRecorder(job(4), h.deps);
    // 기준점에서의 절대 목표 시각으로 계산하므로 매번 33.333ms가 나온다.
    expect(h.waits).toHaveLength(4);
    for (const wait of h.waits) expect(wait).toBeCloseTo(33.333, 2);
    expect(h.clock.value).toBeCloseTo(1000 + 4 * 33.333, 2);
  });
});

describe("encodeWithMediaRecorder — 실패 경로", () => {
  it("createPort가 null이면 ok:false다(dispose를 부르지 않는다)", async () => {
    const h = harness({ port: null });
    const result = await encodeWithMediaRecorder(job(5), h.deps);
    expect(result.ok).toBe(false);
    if (!result.ok) expect(result.reason).toContain("캔버스 녹화를 시작할 수 없습니다");
  });

  it("stop()이 null이면 ok:false다", async () => {
    const h = harness();
    h.port.stopResult = null;
    const result = await encodeWithMediaRecorder(job(3), h.deps);
    expect(result.ok).toBe(false);
    if (!result.ok) expect(result.reason).toBe("레코더 정지 실패");
  });

  it("pushFrame 실패는 skip만 올리고 페이싱은 유지한다", async () => {
    const h = harness();
    h.port.pushResult = (index) => index >= 2;
    const result = await encodeWithMediaRecorder(job(5), h.deps);
    expect(result.ok).toBe(true);
    if (result.ok) {
      expect(result.stats.encodedFrames).toBe(3);
      expect(result.stats.skippedFrames).toBe(2);
    }
    // 실패한 프레임에서는 대기하지 않는다(전진만 한다).
    expect(h.waits).toHaveLength(3);
  });

  it("loadFrame이 전부 null이면 ok:false다", async () => {
    const h = harness({ loadFrame: async () => null });
    const result = await encodeWithMediaRecorder(job(5), h.deps);
    expect(result.ok).toBe(false);
    if (!result.ok) {
      expect(result.reason).toBe("녹화된 프레임이 없습니다");
      expect(result.stats.skippedFrames).toBe(5);
    }
  });

  it("포트가 던져도 예외가 새지 않는다", async () => {
    const h = harness();
    h.port.pushThrows = true;
    const result = await encodeWithMediaRecorder(job(5), h.deps);
    expect(result.ok).toBe(false);
    if (!result.ok) expect(result.reason).toBe("push 폭발");
  });
});

describe("encodeWithMediaRecorder — 자원 해제", () => {
  it.each([
    ["성공", (p: FakePort) => p],
    [
      "정지 실패",
      (p: FakePort) => {
        p.stopResult = null;
        return p;
      },
    ],
    [
      "예외",
      (p: FakePort) => {
        p.pushThrows = true;
        return p;
      },
    ],
  ])("%s 경로에서도 dispose()가 정확히 1회다", async (_label, tune) => {
    const h = harness({ port: tune(new FakePort()) });
    await encodeWithMediaRecorder(job(3), h.deps);
    expect(h.port.disposed).toBe(1);
  });
});
