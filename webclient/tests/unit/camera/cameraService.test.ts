import { readFileSync, readdirSync, statSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import {
  createCameraService,
  READY_TIMEOUT_MS,
  type CameraService,
} from "@adapters/camera/cameraService";
import type {
  CameraState,
  FramePayload,
  FrameProcessor,
  FrameSource,
  ProcessedSize,
  SpoolFrame,
  SpoolOptions,
} from "@adapters/camera/cameraTypes";
import { SPOOL_JPEG_QUALITY } from "@adapters/camera/frameProcessorProtocol";
import { createFpsMeter, FPS_WINDOW_MS } from "@adapters/camera/fpsMeter";
import {
  displayLabel,
  matchDevice,
  type CameraDevice,
} from "@adapters/camera/deviceEnumerator";
import { createCameraTestPresenter, FLASH_DURATION_MS } from "@screens/modals/cameraTest/cameraTestPresenter";
import {
  attachLogStore,
  createLogStore,
  createMemoryLogSink,
  detachLogStore,
} from "@adapters/storage/logStore";

// ─────────────────────────────── 가짜 하드웨어 ───────────────────────────────

class FakeTrack {
  stopped = false;
  constructor(
    readonly label: string,
    private readonly settings: MediaTrackSettings,
  ) {}
  getSettings(): MediaTrackSettings {
    return this.settings;
  }
  stop(): void {
    this.stopped = true;
  }
}

class FakeStream {
  constructor(readonly tracks: FakeTrack[]) {}
  getTracks(): FakeTrack[] {
    return this.tracks;
  }
  getVideoTracks(): FakeTrack[] {
    return this.tracks;
  }
}

function fakeStream(overrides: Partial<MediaTrackSettings> = {}): FakeStream {
  return new FakeStream([
    new FakeTrack("FakeCam", {
      deviceId: "dev-1",
      width: 1920,
      height: 1080,
      frameRate: 30,
      ...overrides,
    }),
  ]);
}

class FakeFrameSource implements FrameSource {
  attached = false;
  detached = false;
  attachResult = true;
  private listener: ((payload: FramePayload) => void) | null = null;

  async attach(): Promise<boolean> {
    this.attached = this.attachResult;
    return this.attachResult;
  }
  onFrame(listener: (payload: FramePayload) => void): () => void {
    this.listener = listener;
    return () => {
      this.listener = null;
    };
  }
  detach(): void {
    this.detached = true;
    this.listener = null;
  }
  size(): ProcessedSize {
    return { width: 1920, height: 1080 };
  }
  /** 테스트에서 프레임 도착을 흉내낸다. */
  emit(): void {
    this.listener?.({ close: () => undefined } as unknown as FramePayload);
  }
}

class FakeProcessor implements FrameProcessor {
  configured: { targetAspect: number; mirror: boolean }[] = [];
  terminated = false;
  stillRequests = 0;
  stillResult: Blob | null = new Blob(["x"]);
  processedCount = 0;
  boundPreview = false;
  spoolConfigs: SpoolOptions[] = [];
  private listeners = new Set<(size: ProcessedSize) => void>();
  private spoolListeners = new Set<(frame: SpoolFrame) => void>();

  configure(options: { targetAspect: number; mirror: boolean }): void {
    this.configured.push(options);
  }
  process(payload: FramePayload): void {
    payload.close();
    this.processedCount++;
    // 실제 Worker처럼 가공 완료를 통지한다.
    for (const listener of this.listeners) listener({ width: 810, height: 1080 });
  }
  onProcessed(listener: (size: ProcessedSize) => void): () => void {
    this.listeners.add(listener);
    return () => this.listeners.delete(listener);
  }
  async requestStill(): Promise<Blob | null> {
    this.stillRequests++;
    return this.stillResult;
  }
  bindPreview(): void {
    this.boundPreview = true;
  }
  configureSpool(options: SpoolOptions): void {
    this.spoolConfigs.push(options);
  }
  onSpoolFrame(listener: (frame: SpoolFrame) => void): () => void {
    this.spoolListeners.add(listener);
    return () => this.spoolListeners.delete(listener);
  }
  /** 테스트에서 스풀 프레임 도착을 흉내낸다. */
  emitSpool(frame: SpoolFrame): void {
    for (const listener of this.spoolListeners) listener(frame);
  }
  terminate(): void {
    this.terminated = true;
    this.listeners.clear();
    this.spoolListeners.clear();
  }
}

interface Harness {
  readonly camera: CameraService;
  readonly source: FakeFrameSource;
  readonly processor: FakeProcessor;
  readonly stream: FakeStream;
  readonly states: CameraState[];
  advance(ms: number): void;
}

function harness(
  options: {
    openStream?: () => Promise<MediaStream>;
    attachResult?: boolean;
  } = {},
): Harness {
  const clock = { value: 0 };
  const source = new FakeFrameSource();
  const processor = new FakeProcessor();
  const stream = fakeStream();
  if (options.attachResult === false) source.attachResult = false;

  const camera = createCameraService({
    openStream:
      options.openStream ?? (async () => stream as unknown as MediaStream),
    createFrameSource: () => source,
    createProcessor: () => processor,
    now: () => clock.value,
  });

  const states: CameraState[] = [];
  camera.onState((state) => states.push(state));

  return {
    camera,
    source,
    processor,
    stream,
    states,
    advance(ms) {
      clock.value += ms;
    },
  };
}

/** Ready 조건(가공 8프레임 + 500ms + fps>0)을 채운다. */
function driveToReady(h: Harness): void {
  h.advance(500);
  for (let i = 0; i < 8; i++) h.source.emit();
}

beforeEach(() => {
  attachLogStore(createLogStore({ sink: createMemoryLogSink(), now: () => 0 }));
});

afterEach(() => {
  detachLogStore();
  vi.useRealTimers();
});

// ─────────────────────────────────── 테스트 ───────────────────────────────────

describe("fpsMeter — 최근 1초 윈도우(가공 완료 기준)", () => {
  it("윈도우 밖의 프레임은 세지 않는다", () => {
    const meter = createFpsMeter();
    meter.mark(0);
    meter.mark(500);
    expect(meter.fps(500)).toBe(2);
    expect(meter.fps(1400)).toBe(1); // t=0 프레임이 윈도우를 벗어났다
    expect(meter.fps(2000)).toBe(0);
  });

  it("정확히 1초 경계의 프레임은 윈도우 밖이다 — fps를 과대보고하지 않는다", () => {
    // 경계를 포함하면 fps>0이 실제보다 오래 유지되고, 그 조건이 Ready 게이트에 걸려 있다.
    // 과소보고는 Ready가 조금 늦어질 뿐이라 안전측이다.
    const meter = createFpsMeter();
    meter.mark(500);
    expect(meter.fps(1499)).toBe(1);
    expect(meter.fps(1500)).toBe(0);
  });

  it("누적 수는 윈도우와 무관하게 증가한다(Ready 게이트의 프레임 조건)", () => {
    const meter = createFpsMeter();
    for (let i = 0; i < 10; i++) meter.mark(i * 1000);
    expect(meter.total).toBe(10);
    expect(meter.fps(9000)).toBe(1);
  });

  it("reset이 둘 다 비운다", () => {
    const meter = createFpsMeter();
    meter.mark(0);
    meter.reset();
    expect(meter.total).toBe(0);
    expect(meter.fps(0)).toBe(0);
    expect(FPS_WINDOW_MS).toBe(1000);
  });
});

describe("deviceEnumerator — deviceId 불안정 대비 폴백(WC3)", () => {
  const devices: CameraDevice[] = [
    { deviceId: "a", label: "Front", groupId: "g1" },
    { deviceId: "b", label: "Back", groupId: "g2" },
  ];

  it("deviceId → label → groupId → 첫 장치 순으로 매칭한다", () => {
    expect(matchDevice(devices, { deviceId: "b", label: "", groupId: "" })).toEqual({
      device: devices[1],
      reason: "deviceId",
    });
    expect(matchDevice(devices, { deviceId: "gone", label: "Back", groupId: "" }).reason).toBe(
      "label",
    );
    expect(matchDevice(devices, { deviceId: "gone", label: "없음", groupId: "g1" }).reason).toBe(
      "groupId",
    );
    expect(matchDevice(devices, { deviceId: "gone", label: "없음", groupId: "없음" })).toEqual({
      device: devices[0],
      reason: "first",
    });
  });

  it("저장값이 없으면 첫 장치다", () => {
    expect(matchDevice(devices, null).reason).toBe("first");
  });

  it("장치가 없으면 none이다", () => {
    expect(matchDevice([], { deviceId: "a", label: "", groupId: "" })).toEqual({
      device: null,
      reason: "none",
    });
  });

  it("빈 라벨로는 매칭하지 않는다 — 권한 전 빈 라벨끼리 엉뚱하게 일치하는 것을 막는다", () => {
    const unlabeled: CameraDevice[] = [
      { deviceId: "x", label: "", groupId: "" },
      { deviceId: "y", label: "", groupId: "" },
    ];
    // 저장된 라벨도 비어 있으면 라벨 단계를 건너뛰고 첫 장치로 간다.
    expect(matchDevice(unlabeled, { deviceId: "gone", label: "", groupId: "" }).reason).toBe("first");
  });

  it("라벨이 비면 순번으로 표시한다(권한 부여 전)", () => {
    expect(displayLabel({ deviceId: "x", label: "", groupId: "" }, 0)).toBe("카메라 1");
    expect(displayLabel({ deviceId: "x", label: "  ", groupId: "" }, 2)).toBe("카메라 3");
    expect(displayLabel({ deviceId: "x", label: "FaceTime", groupId: "" }, 0)).toBe("FaceTime");
  });
});

describe("cameraService — Ready 게이트(04 §3)", () => {
  it("세 조건을 모두 채워야 Ready다", async () => {
    const h = harness();
    await h.camera.start({ targetAspect: 0.75, mirror: true });
    expect(h.camera.state()).toBe("Starting");

    // 프레임만 8개(경과 부족)
    for (let i = 0; i < 8; i++) h.source.emit();
    expect(h.camera.state()).toBe("Starting");

    // 경과를 채우고 프레임을 1개 더 → Ready
    h.advance(500);
    h.source.emit();
    expect(h.camera.state()).toBe("Ready");
    expect(h.states).toEqual(["Starting", "Ready"]);
  });

  it("경과만 채우고 프레임이 부족하면 Ready가 아니다", async () => {
    const h = harness();
    await h.camera.start({ targetAspect: 0.75, mirror: false });
    h.advance(5000);
    for (let i = 0; i < 7; i++) h.source.emit();
    expect(h.camera.state()).toBe("Starting");
  });

  it("Ready 신호는 1회만 발행된다", async () => {
    const h = harness();
    await h.camera.start({ targetAspect: 0.75, mirror: false });
    driveToReady(h);
    for (let i = 0; i < 20; i++) h.source.emit();
    expect(h.states.filter((s) => s === "Ready")).toHaveLength(1);
  });

  it("8초 안에 Ready가 되지 않으면 Failed다 — 무한 로딩 금지", async () => {
    vi.useFakeTimers();
    const h = harness();
    await h.camera.start({ targetAspect: 0.75, mirror: false });
    expect(h.camera.state()).toBe("Starting");

    vi.advanceTimersByTime(READY_TIMEOUT_MS + 1);
    expect(h.camera.state()).toBe("Failed");
    // 타임아웃에서도 자원을 정리한다(LED가 켜진 채 남지 않는다).
    expect(h.stream.tracks[0]!.stopped).toBe(true);
    expect(h.processor.terminated).toBe(true);
  });

  it("Ready 이후에는 타임아웃이 상태를 바꾸지 않는다", async () => {
    vi.useFakeTimers();
    const h = harness();
    await h.camera.start({ targetAspect: 0.75, mirror: false });
    driveToReady(h);
    expect(h.camera.state()).toBe("Ready");

    vi.advanceTimersByTime(READY_TIMEOUT_MS + 1);
    expect(h.camera.state()).toBe("Ready");
  });
});

describe("cameraService — 열기 실패는 false다(예외 전파 금지)", () => {
  it("getUserMedia 거부는 false + Failed다 — 사유는 permissionDenied다", async () => {
    const h = harness({
      openStream: async () => {
        throw new DOMException("denied", "NotAllowedError");
      },
    });
    expect(await h.camera.start({ targetAspect: 0.75, mirror: false })).toBe(false);
    expect(h.camera.state()).toBe("Failed");
    // 권한 거부와 장치 부재는 손님이 할 조치가 다르다 → 화면이 사유별 문구를 고른다(03 §6.3).
    expect(h.camera.failureReason()).toBe("permissionDenied");
  });

  it("점유(NotReadableError)는 inUse다", async () => {
    const h = harness({
      openStream: async () => {
        throw new DOMException("busy", "NotReadableError");
      },
    });
    expect(await h.camera.start({ targetAspect: 0.75, mirror: false })).toBe(false);
    expect(h.camera.failureReason()).toBe("inUse");
  });

  it("성공하면 직전 실패 사유가 지워진다", async () => {
    let fail = true;
    const stream = fakeStream();
    const h = harness({
      openStream: async () => {
        if (fail) throw new DOMException("denied", "NotAllowedError");
        return stream as unknown as MediaStream;
      },
    });
    expect(await h.camera.start({ targetAspect: 0.75, mirror: false })).toBe(false);
    expect(h.camera.failureReason()).toBe("permissionDenied");

    fail = false;
    h.camera.stop();
    expect(await h.camera.start({ targetAspect: 0.75, mirror: false })).toBe(true);
    expect(h.camera.failureReason()).toBeNull();
  });

  it("OverconstrainedError는 제약 없이 1회 재시도한다(저장된 deviceId가 사라진 경우)", async () => {
    const attempts: MediaStreamConstraints[] = [];
    const stream = fakeStream();
    const h = harness({
      openStream: async () => {
        // 첫 호출(exact deviceId)은 실패, 두 번째(제약 없음)는 성공
        if (attempts.length === 0) {
          attempts.push({});
          throw new DOMException("over", "OverconstrainedError");
        }
        attempts.push({});
        return stream as unknown as MediaStream;
      },
    });

    expect(await h.camera.start({ deviceId: "gone", targetAspect: 0.75, mirror: false })).toBe(true);
    expect(attempts).toHaveLength(2);
    expect(h.camera.state()).toBe("Starting");
  });

  it("재시도까지 실패하면 Failed다", async () => {
    const h = harness({
      openStream: async () => {
        throw new DOMException("over", "OverconstrainedError");
      },
    });
    expect(await h.camera.start({ deviceId: "gone", targetAspect: 0.75, mirror: false })).toBe(false);
    expect(h.camera.state()).toBe("Failed");
    // 재시도까지 실패했을 때만 noDevice로 확정한다.
    expect(h.camera.failureReason()).toBe("noDevice");
  });

  it("video.play() 실패도 Failed + 자원 정리다", async () => {
    const h = harness({ attachResult: false });
    expect(await h.camera.start({ targetAspect: 0.75, mirror: false })).toBe(false);
    expect(h.camera.state()).toBe("Failed");
    expect(h.stream.tracks[0]!.stopped).toBe(true);
  });
});

describe("cameraService — 멱등성과 정지", () => {
  it("이미 실행 중이면 무시하고 성공한다(자동 재시작하지 않는다)", async () => {
    let opens = 0;
    const stream = fakeStream();
    const h = harness({
      openStream: async () => {
        opens++;
        return stream as unknown as MediaStream;
      },
    });

    await h.camera.start({ targetAspect: 0.75, mirror: false });
    expect(await h.camera.start({ deviceId: "other", targetAspect: 0.75, mirror: false })).toBe(true);
    expect(opens).toBe(1); // 장치 변경은 호출측이 stop() 후 부른다
  });

  it("stop()이 트랙을 멈추고 Worker를 종료한다 — LED가 꺼지는 조건", async () => {
    const h = harness();
    await h.camera.start({ targetAspect: 0.75, mirror: false });
    driveToReady(h);

    h.camera.stop();
    expect(h.stream.tracks[0]!.stopped).toBe(true);
    expect(h.source.detached).toBe(true);
    expect(h.processor.terminated).toBe(true);
    expect(h.camera.state()).toBe("Idle");
    expect(h.camera.settings()).toBeNull();
  });

  it("Idle에서 stop()은 무해하다", () => {
    const h = harness();
    expect(() => h.camera.stop()).not.toThrow();
    expect(h.states).toHaveLength(0);
  });

  it("정지 후 다시 시작할 수 있다(Ready 카운터가 초기화된다)", async () => {
    const h = harness();
    await h.camera.start({ targetAspect: 0.75, mirror: false });
    driveToReady(h);
    h.camera.stop();

    await h.camera.start({ targetAspect: 0.75, mirror: false });
    expect(h.camera.state()).toBe("Starting"); // 이전 프레임 수가 남아 있지 않다
    driveToReady(h);
    expect(h.camera.state()).toBe("Ready");
  });
});

describe("cameraService — 실제 획득값·거울 토글", () => {
  it("트랙이 보고한 실제 해상도·fps를 그대로 노출한다(WC2)", async () => {
    const h = harness();
    await h.camera.start({ targetAspect: 0.75, mirror: false });
    expect(h.camera.settings()).toEqual({
      deviceId: "dev-1",
      label: "FakeCam",
      width: 1920,
      height: 1080,
      frameRate: 30,
    });
  });

  it("fps를 보고하지 않는 트랙도 처리한다", async () => {
    const stream = new FakeStream([new FakeTrack("NoFps", { deviceId: "d", width: 640, height: 480 })]);
    const h = harness({ openStream: async () => stream as unknown as MediaStream });
    await h.camera.start({ targetAspect: 1, mirror: false });
    expect(h.camera.settings()?.frameRate).toBeNull();
  });

  it("가공 결과 크기를 보고한다(크롭 후)", async () => {
    const h = harness();
    await h.camera.start({ targetAspect: 0.75, mirror: false });
    h.source.emit();
    expect(h.camera.processedSize()).toEqual({ width: 810, height: 1080 });
  });

  it("거울·종횡비 변경이 재시작 없이 Worker로 전달된다", async () => {
    const h = harness();
    await h.camera.start({ targetAspect: 0.75, mirror: false });
    expect(h.processor.configured[0]).toEqual({ targetAspect: 0.75, mirror: false });

    h.camera.configure({ mirror: true });
    expect(h.processor.configured.at(-1)).toEqual({ targetAspect: 0.75, mirror: true });

    h.camera.configure({ targetAspect: 1 });
    expect(h.processor.configured.at(-1)).toEqual({ targetAspect: 1, mirror: true });
  });
});

describe("cameraService — 스틸 캡처", () => {
  it("Ready 전에는 거부하고 null이다(빈 컷을 만들지 않는다)", async () => {
    const h = harness();
    await h.camera.start({ targetAspect: 0.75, mirror: false });
    expect(await h.camera.captureStill()).toBeNull();
    expect(h.processor.stillRequests).toBe(0);
  });

  it("Ready면 Worker에 요청해 Blob을 돌려준다", async () => {
    const h = harness();
    await h.camera.start({ targetAspect: 0.75, mirror: false });
    driveToReady(h);
    const blob = await h.camera.captureStill();
    expect(blob).not.toBeNull();
    expect(h.processor.stillRequests).toBe(1);
  });

  it("Worker가 실패하면 null이다(예외를 던지지 않는다)", async () => {
    const h = harness();
    await h.camera.start({ targetAspect: 0.75, mirror: false });
    driveToReady(h);
    h.processor.stillResult = null;
    expect(await h.camera.captureStill()).toBeNull();
  });
});

describe("cameraService — 타임랩스 스풀 채널(04 §7.2)", () => {
  it("스풀 설정을 가공 Worker로 위임한다", async () => {
    const h = harness();
    await h.camera.start({ targetAspect: 0.75, mirror: false });
    h.camera.configureTimelapseSpool({ enabled: true, intervalMs: 66.67, quality: SPOOL_JPEG_QUALITY });
    expect(h.processor.spoolConfigs).toEqual([
      { enabled: true, intervalMs: 66.67, quality: 0.8 },
    ]);
  });

  it("카메라가 열려 있지 않으면 무해한 no-op이다(예외 금지)", () => {
    const h = harness();
    expect(() =>
      h.camera.configureTimelapseSpool({ enabled: false, intervalMs: 66.67, quality: 0.8 }),
    ).not.toThrow();
  });

  it("스풀 프레임이 구독자에게 전달되고 해제된다", async () => {
    const h = harness();
    await h.camera.start({ targetAspect: 0.75, mirror: false });

    const received: SpoolFrame[] = [];
    const off = h.camera.onTimelapseFrame((frame) => received.push(frame));
    h.processor.emitSpool({ blob: new Blob(["a"]), width: 810, height: 1080 });
    expect(received).toHaveLength(1);
    expect(received[0]!.width).toBe(810);

    off();
    h.processor.emitSpool({ blob: new Blob(["b"]), width: 810, height: 1080 });
    expect(received).toHaveLength(1);
  });

  it("스틸 요청과 스풀이 서로를 침범하지 않는다(전용 채널)", async () => {
    // 스풀을 스틸 채널로 구현했다면 컷 요청이 덮여 사라진다(F4·F5).
    const h = harness();
    await h.camera.start({ targetAspect: 0.75, mirror: false });
    driveToReady(h);

    h.camera.configureTimelapseSpool({ enabled: true, intervalMs: 66.67, quality: 0.8 });
    h.processor.emitSpool({ blob: new Blob(["spool"]), width: 810, height: 1080 });

    expect(await h.camera.captureStill()).not.toBeNull();
    expect(h.processor.stillRequests).toBe(1);
  });
});

describe("카메라 테스트 모달 로직(03 §15.1)", () => {
  it("정지 후 지정 장치로 재시작한다 — start()가 멱등이라 그냥 부르면 무시된다", async () => {
    const h = harness();
    await h.camera.start({ targetAspect: 0.75, mirror: false });
    driveToReady(h);

    const calls: string[] = [];
    const spy: CameraService = {
      ...h.camera,
      stop: () => {
        calls.push("stop");
        h.camera.stop();
      },
      start: async (options) => {
        calls.push(`start:${options.deviceId ?? "default"}`);
        return h.camera.start(options);
      },
    };

    const presenter = createCameraTestPresenter(spy);
    await presenter.open({ deviceId: "dev-2", mirror: true, flash: false });
    expect(calls).toEqual(["stop", "start:dev-2"]);
  });

  it("셔터가 플래시를 재현하고 결과를 버린다(저장 경로가 없다)", async () => {
    const h = harness();
    const presenter = createCameraTestPresenter(h.camera);
    await presenter.open({ deviceId: null, mirror: false, flash: true });
    driveToReady(h);

    const flashes: number[] = [];
    const ok = await presenter.shoot(async (ms) => {
      flashes.push(ms);
    });

    expect(ok).toBe(true);
    expect(flashes).toEqual([FLASH_DURATION_MS]);
    // 반환값이 boolean이다 — Blob을 돌려주지 않으므로 저장 경로를 만들 수 없다.
    expect(typeof ok).toBe("boolean");
  });

  it("플래시가 off면 재현하지 않는다", async () => {
    const h = harness();
    const presenter = createCameraTestPresenter(h.camera);
    await presenter.open({ deviceId: null, mirror: false, flash: false });
    driveToReady(h);

    const flashes: number[] = [];
    await presenter.shoot(async (ms) => {
      flashes.push(ms);
    });
    expect(flashes).toEqual([]);
  });

  it("close()가 카메라를 확실히 정지한다", async () => {
    const h = harness();
    const presenter = createCameraTestPresenter(h.camera);
    await presenter.open({ deviceId: null, mirror: false, flash: false });
    driveToReady(h);

    presenter.close();
    expect(h.stream.tracks[0]!.stopped).toBe(true);
    expect(h.camera.state()).toBe("Idle");
  });

  it("시작 실패는 false를 돌려주고 예외를 던지지 않는다", async () => {
    const h = harness({
      openStream: async () => {
        throw new DOMException("denied", "NotAllowedError");
      },
    });
    const presenter = createCameraTestPresenter(h.camera);
    expect(await presenter.open({ deviceId: null, mirror: false, flash: false })).toBe(false);
  });
});

describe("WM1 — CSS 반전 금지 정적 검사", () => {
  const SRC = join(dirname(fileURLToPath(import.meta.url)), "..", "..", "..", "src");

  function collect(dir: string, exts: string[]): string[] {
    const result: string[] = [];
    for (const entry of readdirSync(dir)) {
      const full = join(dir, entry);
      if (statSync(full).isDirectory()) result.push(...collect(full, exts));
      else if (exts.some((ext) => entry.endsWith(ext))) result.push(full);
    }
    return result;
  }

  it("소스 어디에도 scaleX(-1) / rotateY(180deg) 반전이 없다", () => {
    const offenders: string[] = [];
    for (const file of collect(SRC, [".ts", ".tsx", ".css"])) {
      const code = readFileSync(file, "utf8")
        .replace(/\/\*[\s\S]*?\*\//g, "")
        .replace(/(^|[^:])\/\/.*$/gm, "$1");
      if (/scaleX\s*\(\s*-1\s*\)/.test(code) || /rotateY\s*\(\s*180deg\s*\)/.test(code)) {
        offenders.push(file.slice(SRC.length + 1));
      }
    }
    // CSS로 반전하면 프리뷰만 뒤집히고 저장 픽셀은 원본이 된다(WYSIWYG 파손).
    expect(offenders).toEqual([]);
  });

  it("프리뷰 뷰가 <video>를 직접 렌더하지 않는다", () => {
    // 주석에는 설명 목적으로 <video>가 등장하므로 코드만 본다.
    const preview = readFileSync(join(SRC, "ui", "views", "CameraPreview.tsx"), "utf8")
      .replace(/\/\*[\s\S]*?\*\//g, "")
      .replace(/(^|[^:])\/\/.*$/gm, "$1");
    expect(preview.includes("<video")).toBe(false);
    expect(preview.includes("<canvas")).toBe(true);
  });
});
