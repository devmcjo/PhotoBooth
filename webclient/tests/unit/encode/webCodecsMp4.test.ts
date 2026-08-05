import { describe, expect, it } from "vitest";
import {
  encodeWithWebCodecs,
  type EncodableFrame,
  type Mp4MuxerLike,
  type VideoEncoderLike,
  type WebCodecsMp4Deps,
} from "@adapters/encode/webCodecsMp4";
import type { EncodeJob, TimelapseEncodeConfig } from "@adapters/encode/encodeProtocol";

/**
 * 경로 B 코어 — 가짜 인코더·muxer·프레임으로 전량 검증한다(브라우저 불필요).
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

interface Recorded {
  readonly frames: { timestampUs: number; durationUs: number; width: number; height: number }[];
  readonly encodes: { keyFrame: boolean | undefined }[];
  readonly closes: number[];
  readonly order: string[];
  readonly encoder: FakeEncoder;
}

class FakeEncoder implements VideoEncoderLike {
  encodeQueueSize = 0;
  state = "unconfigured";
  configured: VideoEncoderConfig | null = null;
  closeCount = 0;
  configureThrows = false;
  encodeThrows = false;
  /** null이면 즉시 resolve. `"pending"`이면 영원히 대기(타임아웃 검증). */
  flushMode: "resolve" | "pending" | "reject" = "resolve";

  constructor(
    private readonly order: string[],
    private readonly handlers: {
      output: (chunk: unknown, meta: unknown) => void;
      error: (reason: string) => void;
    },
  ) {}

  configure(config: VideoEncoderConfig): void {
    if (this.configureThrows) throw new TypeError("config rejected");
    this.configured = config;
    this.state = "configured";
  }

  encode(_frame: EncodableFrame, _options?: { keyFrame?: boolean }): void {
    if (this.encodeThrows) throw new Error("encode failed");
    // 실제 인코더처럼 chunk를 콜백으로 내보낸다.
    this.handlers.output({ tag: "chunk" }, { decoderConfig: { description: new Uint8Array(1) } });
  }

  flush(): Promise<void> {
    this.order.push("flush");
    if (this.flushMode === "pending") return new Promise<void>(() => undefined);
    if (this.flushMode === "reject") return Promise.reject(new Error("flush blew up"));
    return Promise.resolve();
  }

  close(): void {
    this.closeCount++;
    this.state = "closed";
  }

  /** 비동기 오류 콜백을 흉내낸다. */
  raiseError(reason: string): void {
    this.handlers.error(reason);
  }
}

function harness(
  overrides: {
    loadFrame?: (name: string) => Promise<Blob | null>;
    tuneEncoder?: (encoder: FakeEncoder) => void;
    muxerThrowsOnAdd?: boolean;
    createFrameFails?: (index: number) => boolean;
    createMuxerThrows?: boolean;
  } = {},
): { deps: WebCodecsMp4Deps; recorded: Recorded; chunks: unknown[] } {
  const order: string[] = [];
  const frames: Recorded["frames"] = [];
  const encodes: Recorded["encodes"] = [];
  const closes: number[] = [];
  const chunks: unknown[] = [];
  let created = 0;
  let clock = 0;
  let encoder: FakeEncoder | null = null;

  const muxer: Mp4MuxerLike = {
    addVideoChunk(chunk) {
      if (overrides.muxerThrowsOnAdd === true) throw new Error("mux failed");
      order.push("addVideoChunk");
      chunks.push(chunk);
    },
    finalize() {
      order.push("finalize");
    },
    buffer() {
      return new ArrayBuffer(1024);
    },
  };

  const deps: WebCodecsMp4Deps = {
    loadFrame: overrides.loadFrame ?? (async () => new Blob(["jpeg"])),
    async createFrame(_blob, init) {
      const index = created++;
      if (overrides.createFrameFails?.(index) === true) return null;
      frames.push({ ...init });
      return {
        close() {
          closes.push(index);
        },
      };
    },
    createEncoder(handlers) {
      encoder = new FakeEncoder(order, handlers);
      overrides.tuneEncoder?.(encoder);
      // encode 호출을 기록하려면 래핑이 필요하다(FakeEncoder는 순수 흉내만 낸다).
      const inner = encoder;
      return {
        get encodeQueueSize() {
          return inner.encodeQueueSize;
        },
        get state() {
          return inner.state;
        },
        configure: (config) => inner.configure(config),
        encode: (frame, options) => {
          encodes.push({ keyFrame: options?.keyFrame });
          order.push("encode");
          inner.encode(frame, options);
        },
        flush: () => inner.flush(),
        close: () => inner.close(),
      };
    },
    createMuxer() {
      if (overrides.createMuxerThrows === true) throw new Error("muxer boom");
      return muxer;
    },
    now: () => (clock += 5),
    flushTimeoutMs: 20,
  };

  return {
    deps,
    recorded: {
      frames,
      encodes,
      closes,
      order,
      get encoder() {
        return encoder!;
      },
    } as Recorded,
    chunks,
  };
}

describe("encodeWithWebCodecs — 정상 경로", () => {
  it("프레임 수·타임스탬프·duration이 job과 일치한다", async () => {
    const { deps, recorded } = harness();
    const result = await encodeWithWebCodecs(job(40), deps);

    expect(result.ok).toBe(true);
    expect(recorded.frames).toHaveLength(40);
    expect(recorded.frames[0]).toEqual({
      timestampUs: 0,
      durationUs: 33333,
      width: 810,
      height: 1080,
    });
    expect(recorded.frames[39]!.timestampUs).toBe(39 * 33333);
    if (result.ok) {
      expect(result.output.stats.encodedFrames).toBe(40);
      expect(result.output.stats.droppedFrames).toBe(0);
      expect(result.output.stats.skippedFrames).toBe(0);
      expect(result.output.buffer.byteLength).toBe(1024);
    }
  });

  it("인코더 설정에 짝수 크기·비트레이트·avc 포맷이 들어간다", async () => {
    const { deps, recorded } = harness();
    await encodeWithWebCodecs(job(35), deps);
    expect(recorded.encoder.configured).toEqual({
      codec: "avc1.42001E",
      width: 810,
      height: 1080,
      bitrate: 5_000_000,
      framerate: 30,
      latencyMode: "quality",
      avc: { format: "avc" },
    });
  });

  it("키프레임이 첫 프레임과 30프레임마다 true다", async () => {
    const { deps, recorded } = harness();
    await encodeWithWebCodecs(job(65), deps);
    const keyIndices = recorded.encodes
      .map((e, i) => (e.keyFrame === true ? i : -1))
      .filter((i) => i >= 0);
    expect(keyIndices).toEqual([0, 30, 60]);
  });

  it("finalize()가 flush() **뒤에** 불린다(컨테이너 정상 종료)", async () => {
    const { deps, recorded } = harness();
    await encodeWithWebCodecs(job(31), deps);
    const flushAt = recorded.order.indexOf("flush");
    const finalizeAt = recorded.order.indexOf("finalize");
    expect(flushAt).toBeGreaterThan(0);
    expect(finalizeAt).toBeGreaterThan(flushAt);
    // 마지막 encode가 flush보다 앞이다.
    expect(recorded.order.lastIndexOf("encode")).toBeLessThan(flushAt);
  });

  it("모든 프레임에 close()가 불린다", async () => {
    const { deps, recorded } = harness();
    await encodeWithWebCodecs(job(31), deps);
    expect(recorded.closes).toHaveLength(31);
  });
});

describe("encodeWithWebCodecs — 백프레셔(04 §7.5)", () => {
  it("큐가 9면 드롭하고 8이면 인코딩한다(경계)", async () => {
    // 8은 임계값 자체 — `> 8`이 조건이므로 인코딩된다.
    const atLimit = harness({ tuneEncoder: (e) => (e.encodeQueueSize = 8) });
    const a = await encodeWithWebCodecs(job(31), atLimit.deps);
    expect(a.ok).toBe(true);
    expect(atLimit.recorded.encodes).toHaveLength(31);

    const over = harness({ tuneEncoder: (e) => (e.encodeQueueSize = 9) });
    const b = await encodeWithWebCodecs(job(31), over.deps);
    expect(b.ok).toBe(false);
    if (!b.ok) {
      expect(b.stats.droppedFrames).toBe(31);
      expect(b.stats.encodedFrames).toBe(0);
      expect(b.reason).toContain("인코딩된 프레임이 없습니다");
    }
    // 드롭한 프레임도 닫아야 한다.
    expect(over.recorded.closes).toHaveLength(31);
  });

  it("드롭돼도 남은 프레임의 타임스탬프가 밀리지 않는다", async () => {
    // 앞 10프레임만 큐가 밀린 상황.
    let calls = 0;
    const { deps, recorded } = harness({
      tuneEncoder: (e) => {
        Object.defineProperty(e, "encodeQueueSize", {
          get: () => (calls++ < 10 ? 9 : 0),
        });
      },
    });
    const result = await encodeWithWebCodecs(job(40), deps);
    expect(result.ok).toBe(true);
    if (result.ok) {
      expect(result.output.stats.droppedFrames).toBe(10);
      expect(result.output.stats.encodedFrames).toBe(30);
    }
    // 11번째 프레임(인덱스 10)의 타임스탬프는 여전히 10 * 33333이다.
    expect(recorded.frames[10]!.timestampUs).toBe(10 * 33333);
  });
});

describe("encodeWithWebCodecs — 실패 경로는 전부 ok:false다", () => {
  it("loadFrame이 null이면 skip하고, 전량 null이면 ok:false다", async () => {
    const partial = harness({
      loadFrame: async (name) => (name === "00000.jpg" ? null : new Blob(["x"])),
    });
    const a = await encodeWithWebCodecs(job(31), partial.deps);
    expect(a.ok).toBe(true);
    if (a.ok) expect(a.output.stats.skippedFrames).toBe(1);

    const none = harness({ loadFrame: async () => null });
    const b = await encodeWithWebCodecs(job(31), none.deps);
    expect(b.ok).toBe(false);
    if (!b.ok) expect(b.stats.skippedFrames).toBe(31);
  });

  it("createFrame이 null이면 skip 카운트만 오른다(인코딩하지 않는다)", async () => {
    const { deps, recorded } = harness({ createFrameFails: (i) => i < 5 });
    const result = await encodeWithWebCodecs(job(40), deps);
    expect(result.ok).toBe(true);
    if (result.ok) expect(result.output.stats.skippedFrames).toBe(5);
    expect(recorded.encodes).toHaveLength(35);
    expect(recorded.closes).toHaveLength(35);
  });

  it("error 콜백이 오면 루프를 끊고 인코더를 닫는다", async () => {
    let raised = false;
    const { deps, recorded } = harness({
      tuneEncoder: (e) => {
        const originalEncode = e.encode.bind(e);
        e.encode = (frame, options) => {
          originalEncode(frame, options);
          if (!raised) {
            raised = true;
            e.raiseError("하드웨어 인코더 오류");
          }
        };
      },
    });
    const result = await encodeWithWebCodecs(job(40), deps);
    expect(result.ok).toBe(false);
    if (!result.ok) expect(result.reason).toBe("하드웨어 인코더 오류");
    // 첫 프레임에서 오류가 났으므로 나머지는 인코딩되지 않는다.
    expect(recorded.encodes).toHaveLength(1);
    expect(recorded.encoder.closeCount).toBe(1);
  });

  it("configure 거부는 ok:false + close()다", async () => {
    const { deps, recorded } = harness({ tuneEncoder: (e) => (e.configureThrows = true) });
    const result = await encodeWithWebCodecs(job(40), deps);
    expect(result.ok).toBe(false);
    if (!result.ok) expect(result.reason).toContain("인코더 설정 거부");
    expect(recorded.encoder.closeCount).toBe(1);
    expect(recorded.frames).toHaveLength(0);
  });

  it("encode()가 throw하면 사유를 담아 ok:false다", async () => {
    const { deps, recorded } = harness({ tuneEncoder: (e) => (e.encodeThrows = true) });
    const result = await encodeWithWebCodecs(job(40), deps);
    expect(result.ok).toBe(false);
    if (!result.ok) expect(result.reason).toContain("인코딩 실패");
    // 예외 경로에서도 프레임을 닫는다.
    expect(recorded.closes).toHaveLength(1);
  });

  it("muxing 실패는 첫 사유를 남기고 중단한다", async () => {
    const { deps } = harness({ muxerThrowsOnAdd: true });
    const result = await encodeWithWebCodecs(job(40), deps);
    expect(result.ok).toBe(false);
    if (!result.ok) expect(result.reason).toContain("muxing 실패");
  });

  it("createMuxer가 throw하면 인코더를 만들지도 않는다", async () => {
    const { deps } = harness({ createMuxerThrows: true });
    const result = await encodeWithWebCodecs(job(40), deps);
    expect(result.ok).toBe(false);
    if (!result.ok) expect(result.reason).toContain("muxer 생성 실패");
  });

  it("flush()가 영원히 대기하면 타임아웃 후 강제 close()한다", async () => {
    const { deps, recorded } = harness({ tuneEncoder: (e) => (e.flushMode = "pending") });
    const result = await encodeWithWebCodecs(job(31), deps);
    expect(result.ok).toBe(false);
    if (!result.ok) expect(result.reason).toBe("인코더 flush 타임아웃");
    expect(recorded.encoder.closeCount).toBe(1);
    // 타임아웃이면 컨테이너를 닫지 않는다(불완전한 moov를 만들지 않는다).
    expect(recorded.order).not.toContain("finalize");
  });

  it("flush()가 reject해도 예외가 새지 않는다", async () => {
    const { deps } = harness({ tuneEncoder: (e) => (e.flushMode = "reject") });
    const result = await encodeWithWebCodecs(job(31), deps);
    expect(result.ok).toBe(false);
    if (!result.ok) expect(result.reason).toContain("인코더 flush 실패");
  });

  it("loadFrame이 throw해도 예외가 새지 않는다", async () => {
    const { deps } = harness({
      loadFrame: async () => {
        throw new Error("OPFS 읽기 실패");
      },
    });
    const result = await encodeWithWebCodecs(job(31), deps);
    expect(result.ok).toBe(false);
    if (!result.ok) expect(result.reason).toContain("OPFS 읽기 실패");
  });

  it("선별 목록이 비어 있어도 throw하지 않는다", async () => {
    const { deps } = harness();
    const result = await encodeWithWebCodecs(job(0), deps);
    expect(result.ok).toBe(false);
    if (!result.ok) expect(result.reason).toContain("인코딩된 프레임이 없습니다");
  });
});
