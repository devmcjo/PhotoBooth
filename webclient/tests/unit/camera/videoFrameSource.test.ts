import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import {
  createVideoFrameSource,
  resetVideoFramePathForTests,
} from "@adapters/camera/videoFrameSource";
import type { FramePayload } from "@adapters/camera/cameraTypes";
import {
  attachLogStore,
  createLogStore,
  createMemoryLogSink,
  detachLogStore,
  type LogSink,
  type LogStore,
} from "@adapters/storage/logStore";

/**
 * `videoFrameSource` 프레임 루프 — 04 §2.3.1·§2.4 (2026-08-07 신설)
 *
 * 이 파일이 고정하는 것은 전부 "그 전에는 없었던 것"이다:
 *
 * | # | 고정 대상 | 없었을 때의 증상 |
 * |---|-----------|------------------|
 * | 1 | `VideoFrame` **실증** 프로브 | 생성자만 있고 생성이 실패하는 브라우저에서 매 프레임 throw |
 * | 2 | 런타임 **영구 강등** | 전송 실패가 warn만 남기고 비트맵 폴백으로 절대 내려가지 않음 |
 * | 3 | 강등 로그 1회 | 초당 30회 경고가 로그 링버퍼를 태움 |
 * | 4 | 실패 프레임 `close()` | `VideoFrame`은 GC 대상이 아니라 그대로 누수 |
 * | 5 | `attach`가 `play()` `name`을 돌려줌 | 재생 실패가 권한 실패와 같은 `unknown`으로 보고됨 |
 */

// ─────────────────────────── 브라우저 표면 대역 ───────────────────────────

interface FakeVideoFrame {
  readonly source: unknown;
  closed: boolean;
  close(): void;
}

/** `VideoFrame` 전역 스텁. `failOn`으로 특정 입력에서만 던지게 한다(프로브 vs 실촬영 분리). */
function stubVideoFrame(options: { failOnCanvas?: boolean } = {}): {
  created: FakeVideoFrame[];
  constructorCalls: number;
} {
  const state = { created: [] as FakeVideoFrame[], constructorCalls: 0 };
  vi.stubGlobal(
    "VideoFrame",
    function VideoFrameStub(this: FakeVideoFrame, source: { kind?: string }) {
      state.constructorCalls++;
      if (options.failOnCanvas === true && source.kind === "canvas") {
        throw new TypeError("VideoFrame from canvas is not supported");
      }
      const frame: FakeVideoFrame = {
        source,
        closed: false,
        close() {
          this.closed = true;
        },
      };
      state.created.push(frame);
      return frame;
    },
  );
  return state;
}

interface FakeVideo {
  kind: "video";
  srcObject: unknown;
  parentNode: unknown;
  videoWidth: number;
  videoHeight: number;
  currentTime: number;
  play(): Promise<void>;
  remove(): void;
  requestVideoFrameCallback?: (cb: (now: number, meta: { mediaTime: number }) => void) => number;
  cancelVideoFrameCallback?: (handle: number) => void;
}

interface Harness {
  readonly video: FakeVideo;
  readonly doc: Document;
  /** rVFC 프레임 1장을 흘린다. `mediaTime`은 자동 증가한다(중복 스킵 회피). */
  tick(): Promise<void>;
}

function harness(playRejection?: unknown): Harness {
  let pending: ((now: number, meta: { mediaTime: number }) => void) | null = null;
  let mediaTime = 0;

  const video: FakeVideo = {
    kind: "video",
    srcObject: null,
    parentNode: null,
    videoWidth: 1280,
    videoHeight: 720,
    currentTime: 0,
    play: async () => {
      if (playRejection !== undefined) throw playRejection;
    },
    remove: () => undefined,
    requestVideoFrameCallback: (cb) => {
      pending = cb;
      return 1;
    },
    cancelVideoFrameCallback: () => {
      pending = null;
    },
  };

  const doc = {
    body: {
      appendChild: (node: unknown) => {
        video.parentNode = doc.body;
        return node;
      },
    },
    createElement: (tag: string) => ({ kind: tag, width: 0, height: 0 }),
  } as unknown as Document;

  return {
    video,
    doc,
    async tick() {
      const cb = pending;
      pending = null;
      mediaTime += 1;
      cb?.(mediaTime, { mediaTime });
      // `createImageBitmap` 경로는 await를 지나야 emit에 도달한다.
      await Promise.resolve();
      await Promise.resolve();
    },
  };
}

function makeSource(h: Harness) {
  return createVideoFrameSource(h.video as unknown as HTMLVideoElement, h.doc);
}

// ─────────────────────────────── 로그 관측 ───────────────────────────────

let sink: LogSink;
let store: LogStore;

/** `LogEntry`는 logStore가 내보내지 않으므로 싱크 반환형에서 유도한다. */
type SinkEntry = Awaited<ReturnType<LogSink["readAll"]>>[number];

async function logEntries(): Promise<SinkEntry[]> {
  await store.flush();
  return sink.readAll();
}

beforeEach(() => {
  // ⚠️ 강등 상태는 **모듈 레벨**이다(카메라 재시작을 넘어 유지되어야 하므로).
  //    리셋하지 않으면 앞 테스트의 강등이 뒤 테스트로 샌다.
  resetVideoFramePathForTests();
  sink = createMemoryLogSink();
  store = createLogStore({ sink, now: () => 0 });
  attachLogStore(store);
  vi.stubGlobal("createImageBitmap", vi.fn(async () => ({ close: () => undefined })));
});

afterEach(() => {
  detachLogStore();
  vi.unstubAllGlobals();
});

// ─────────────────────────── 1. 실증 프로브 ───────────────────────────

describe("videoFrameSource — VideoFrame 실증 프로브", () => {
  it("프로브가 통과하면 zero-copy 경로다", async () => {
    const frames = stubVideoFrame();
    const h = harness();
    const source = makeSource(h);
    source.onFrame(() => undefined);

    await source.attach({} as MediaStream);
    await h.tick();

    expect(source.transferMode()).toBe("videoFrame");
    // 프로브 1회 + 실촬영 1프레임.
    expect(frames.constructorCalls).toBe(2);
  });

  it("VideoFrame이 아예 없으면 처음부터 createImageBitmap이다", async () => {
    vi.stubGlobal("VideoFrame", undefined);
    const h = harness();
    const source = makeSource(h);
    source.onFrame(() => undefined);

    await source.attach({} as MediaStream);
    await h.tick();

    expect(source.transferMode()).toBe("imageBitmap");
    expect(createImageBitmap).toHaveBeenCalledTimes(1);
  });

  it("생성자는 있는데 실제 생성이 실패하면 **실증 검사가 존재 검사를 이긴다**", async () => {
    // 존재 검사만 하던 시절에는 여기서 매 프레임 throw하고 폴백으로 내려가지 않았다.
    const frames = stubVideoFrame({ failOnCanvas: true });
    const h = harness();
    const source = makeSource(h);
    source.onFrame(() => undefined);

    await source.attach({} as MediaStream);
    await h.tick();

    expect(source.transferMode()).toBe("imageBitmap");
    expect(createImageBitmap).toHaveBeenCalledTimes(1);
    // 프로브 1회뿐 — 실촬영 프레임으로는 시도조차 하지 않는다.
    expect(frames.constructorCalls).toBe(1);
  });
});

// ─────────────────────── 2·3·4. 런타임 영구 강등 ───────────────────────

describe("videoFrameSource — 전송 실패 시 영구 강등", () => {
  /** 소비자가 던진다 = Worker `postMessage(..., {transfer})` 실패 흉내(F-7). */
  function throwingConsumer(): () => void {
    return () => {
      throw new DOMException("transfer failed", "DataCloneError");
    };
  }

  it("실패 1회로 강등되고 **다음 프레임부터 비트맵 경로**다", async () => {
    const frames = stubVideoFrame();
    const h = harness();
    const source = makeSource(h);
    source.onFrame(throwingConsumer());

    await source.attach({} as MediaStream);
    await h.tick(); // VideoFrame 시도 → 전송 실패 → 강등
    expect(source.transferMode()).toBe("imageBitmapDemoted");

    const callsAfterDemote = frames.constructorCalls;
    await h.tick();
    await h.tick();

    // VideoFrame 생성 시도가 **0회 더** 일어난다 — 매 프레임 재시도가 구조적으로 불가능하다.
    expect(frames.constructorCalls).toBe(callsAfterDemote);
    expect(createImageBitmap).toHaveBeenCalledTimes(2);
  });

  it("전송에 실패한 프레임을 닫는다 — VideoFrame은 GC 대상이 아니다", async () => {
    const frames = stubVideoFrame();
    const h = harness();
    const source = makeSource(h);
    source.onFrame(throwingConsumer());

    await source.attach({} as MediaStream);
    await h.tick();

    // 프로브가 만든 것(닫힘) + 실촬영 프레임(전송 실패 후 닫힘).
    expect(frames.created).toHaveLength(2);
    for (const frame of frames.created) expect(frame.closed).toBe(true);
  });

  it("프레임 10장이 실패해도 강등 로그는 1건이다(링버퍼를 태우지 않는다)", async () => {
    stubVideoFrame();
    const h = harness();
    const source = makeSource(h);
    source.onFrame(throwingConsumer());

    await source.attach({} as MediaStream);
    for (let i = 0; i < 10; i++) await h.tick();

    const demotions = (await logEntries()).filter((entry) => entry.msg.includes("영구 강등"));
    expect(demotions).toHaveLength(1);
    // 로그에는 예외 **이름**만 남긴다(메시지에는 기기명·경로가 섞인다).
    expect(demotions[0]?.ctx).toMatchObject({ name: "DataCloneError" });
  });

  it("강등은 detach → attach 재시작을 넘어 유지된다(전이가 단방향이다)", async () => {
    const frames = stubVideoFrame();
    const h = harness();
    const source = makeSource(h);
    source.onFrame(throwingConsumer());

    await source.attach({} as MediaStream);
    await h.tick();
    expect(source.transferMode()).toBe("imageBitmapDemoted");
    source.detach();

    // 못 하던 기기가 카메라를 다시 연다고 갑자기 하게 되지는 않는다.
    const restarted = makeSource(harness());
    expect(restarted.transferMode()).toBe("imageBitmapDemoted");
    const before = frames.constructorCalls;
    await restarted.attach({} as MediaStream);
    expect(frames.constructorCalls).toBe(before);
  });
});

// ─────────────────────────── 5. attach 실패 이유 ───────────────────────────

describe("videoFrameSource — attach는 실패 이유를 돌려준다", () => {
  it("play()가 reject되면 `{ok:false, errorName}`이고 **예외를 던지지 않는다**", async () => {
    stubVideoFrame();
    const h = harness(new DOMException("gesture required", "NotAllowedError"));
    const source = makeSource(h);

    const result = await source.attach({} as MediaStream);
    expect(result).toEqual({ ok: false, errorName: "NotAllowedError" });
  });

  it("Error가 아닌 rejection도 빈 이름으로 접는다(01 §2.1 — 예외 전파 금지)", async () => {
    stubVideoFrame();
    const h = harness("문자열 거부");
    const source = makeSource(h);

    await expect(source.attach({} as MediaStream)).resolves.toEqual({ ok: false, errorName: "" });
  });

  it("성공하면 `{ok:true}`이고 프레임 루프가 돈다", async () => {
    stubVideoFrame();
    const h = harness();
    const source = makeSource(h);
    const payloads: FramePayload[] = [];
    source.onFrame((payload) => payloads.push(payload));

    expect(await source.attach({} as MediaStream)).toEqual({ ok: true });
    await h.tick();
    expect(payloads).toHaveLength(1);
  });
});
