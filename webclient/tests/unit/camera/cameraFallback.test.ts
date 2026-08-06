import { readFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { createCameraService, READY_TIMEOUT_MS } from "@adapters/camera/cameraService";
import {
  constraintLadder,
  shouldTryNextStep,
} from "@adapters/camera/cameraConstraints";
import { createMainThreadProcessor } from "@adapters/camera/mainThreadProcessor";
import {
  hasStoredDevice,
  resolveStartDeviceId,
} from "@adapters/camera/deviceEnumerator";
import type {
  FramePayload,
  FrameProcessor,
  FrameSource,
  ProcessedSize,
} from "@adapters/camera/cameraTypes";
import {
  attachLogStore,
  createLogStore,
  createMemoryLogSink,
  detachLogStore,
} from "@adapters/storage/logStore";

/**
 * 카메라 폴백 회귀 — **2026-08-06 모바일 카메라 복구(P0)**
 *
 * 이 파일이 고정하는 것은 전부 "그 전에는 없었던 것"이다:
 *
 * | # | 고정 대상 | 없었을 때의 증상 |
 * |---|-----------|------------------|
 * | 1 | 제약 **사다리**(`frameRate.min` 부재) | 저조도 안드로이드가 `OverconstrainedError` → 640×480 후면으로 열림 |
 * | 2 | 권한 거부에서 사다리 **중단** | 같은 실패를 5번 반복하며 손님을 기다리게 함 |
 * | 3 | 가공기 생성 예외 **포착** | `start()`가 예외로 끝나 화면이 로딩에 고착 |
 * | 4 | Ready 타임아웃 사유 `pipelineStalled` | 파이프라인 정체가 권한 문제와 구분되지 않음 |
 * | 5 | 메인 스레드 가공기(폴백 실물) | `OffscreenCanvas` 없는 기기에서 촬영 자체가 불가 |
 * | 6 | `display:none` **금지** 정적 검사 | WebKit에서 프레임 콜백이 돌지 않아 프레임 0 |
 */

// ─────────────────────────────── 가짜 하드웨어 ───────────────────────────────

class FakeTrack {
  stopped = false;
  getSettings(): MediaTrackSettings {
    return { deviceId: "dev-1", width: 1280, height: 720 };
  }
  stop(): void {
    this.stopped = true;
  }
}

class FakeStream {
  readonly track = new FakeTrack();
  getTracks(): FakeTrack[] {
    return [this.track];
  }
  getVideoTracks(): FakeTrack[] {
    return [this.track];
  }
}

class FakeSource implements FrameSource {
  private listener: ((payload: FramePayload) => void) | null = null;
  async attach(): Promise<boolean> {
    return true;
  }
  onFrame(listener: (payload: FramePayload) => void): () => void {
    this.listener = listener;
    return () => {
      this.listener = null;
    };
  }
  detach(): void {
    this.listener = null;
  }
  size(): ProcessedSize {
    return { width: 1280, height: 720 };
  }
  emit(): void {
    this.listener?.({ close: () => undefined } as unknown as FramePayload);
  }
}

function stubProcessor(): FrameProcessor {
  const processed = new Set<(size: ProcessedSize) => void>();
  return {
    mode: "worker",
    configure: () => undefined,
    process: (payload) => {
      payload.close();
      for (const listener of processed) listener({ width: 540, height: 720 });
    },
    onProcessed: (listener) => {
      processed.add(listener);
      return () => processed.delete(listener);
    },
    requestStill: async () => null,
    bindPreview: () => true,
    previewMode: () => "transferred",
    configureSpool: () => undefined,
    onSpoolFrame: () => () => undefined,
    terminate: () => processed.clear(),
  };
}

beforeEach(() => {
  attachLogStore(createLogStore({ sink: createMemoryLogSink(), now: () => 0 }));
});

afterEach(() => {
  detachLogStore();
  vi.useRealTimers();
});

// ───────────────────────────── 1·2. 제약 사다리 ─────────────────────────────

describe("constraintLadder — 요구를 한 칸씩만 낮춘다(04 §2.1)", () => {
  it("어떤 칸에도 frameRate.min·exact가 없다 — 저조도 기기를 튕기지 않는다", () => {
    for (const step of constraintLadder({ deviceId: "cam", facing: "user" })) {
      const video = step.constraints.video;
      if (typeof video !== "object" || video === null) continue;
      const frameRate = (video as MediaTrackConstraints).frameRate;
      if (frameRate === undefined) continue;
      expect(typeof frameRate === "object" ? frameRate : {}).not.toHaveProperty("min");
      expect(typeof frameRate === "object" ? frameRate : {}).not.toHaveProperty("exact");
    }
  });

  it("저장된 장치가 있으면 장치를 지킨 채 해상도만 먼저 낮춘다", () => {
    const labels = constraintLadder({ deviceId: "cam", facing: "user" }).map((s) => s.label);
    expect(labels).toEqual(["device+1080p", "device+720p", "facing+1080p", "facing", "any"]);
  });

  it("저장된 장치가 없으면 장치 칸을 건너뛴다(같은 요청을 두 번 보내지 않는다)", () => {
    expect(constraintLadder({ deviceId: null }).map((s) => s.label)).toEqual([
      "facing+1080p",
      "facing",
      "any",
    ]);
    expect(constraintLadder({ deviceId: "" }).map((s) => s.label)).toHaveLength(3);
  });

  it("facing 힌트가 실제로 제약에 들어간다 — 저장값이 무시되지 않는다", () => {
    const step = constraintLadder({ facing: "environment" })[0]!;
    const video = step.constraints.video as MediaTrackConstraints;
    expect(video.facingMode).toEqual({ ideal: "environment" });
  });

  it("마지막 칸은 무엇이든 여는 안전망이다", () => {
    const last = constraintLadder({ deviceId: "cam" }).at(-1)!;
    expect(last.constraints).toEqual({ audio: false, video: true });
  });

  it("오디오는 어떤 칸에서도 요구하지 않는다(무음 규격 · 권한 범위 최소화)", () => {
    for (const step of constraintLadder({ deviceId: "cam" })) {
      expect(step.constraints.audio).toBe(false);
    }
  });

  it("권한 거부에서는 다음 칸으로 가지 않는다 — 제약을 낮춰도 결과가 같다", () => {
    expect(shouldTryNextStep("NotAllowedError")).toBe(false);
    expect(shouldTryNextStep("SecurityError")).toBe(false);
    expect(shouldTryNextStep("PermissionDeniedError")).toBe(false);
  });

  it("점유·과제약·장치부재에서는 계속 내려간다", () => {
    // 해상도를 낮추면 열리는 안드로이드 기기가 실제로 있다.
    expect(shouldTryNextStep("NotReadableError")).toBe(true);
    expect(shouldTryNextStep("OverconstrainedError")).toBe(true);
    expect(shouldTryNextStep("NotFoundError")).toBe(true);
  });
});

describe("cameraService — 사다리를 실제로 내려간다", () => {
  it("앞 칸이 과제약으로 실패하면 다음 칸으로 내려가 열린다", async () => {
    const attempts: MediaStreamConstraints[] = [];
    const stream = new FakeStream();
    const camera = createCameraService({
      openStream: async (constraints) => {
        attempts.push(constraints);
        if (attempts.length < 3) throw new DOMException("over", "OverconstrainedError");
        return stream as unknown as MediaStream;
      },
      createFrameSource: () => new FakeSource(),
      createProcessor: stubProcessor,
      now: () => 0,
    });

    expect(await camera.start({ deviceId: "cam", targetAspect: 0.75, mirror: false })).toBe(true);
    expect(attempts).toHaveLength(3);
    // 3번째 칸(facing+1080p)에서 열렸다 — 진단이 그 사실을 말할 수 있다.
    expect(camera.constraintStep()).toBe("facing+1080p");
  });

  it("권한 거부는 **첫 칸에서 멈춘다**(5번 반복하지 않는다)", async () => {
    let calls = 0;
    const camera = createCameraService({
      openStream: async () => {
        calls++;
        throw new DOMException("denied", "NotAllowedError");
      },
      createFrameSource: () => new FakeSource(),
      createProcessor: stubProcessor,
      now: () => 0,
    });

    expect(await camera.start({ deviceId: "cam", targetAspect: 0.75, mirror: false })).toBe(false);
    expect(calls).toBe(1);
    expect(camera.failureReason()).toBe("permissionDenied");
  });

  it("사다리를 전부 소진하면 **마지막** 실패로 사유를 확정한다", async () => {
    // 마지막 칸은 `{video:true}`이므로 그 실패 사유가 손님에게 가장 정확하다.
    const camera = createCameraService({
      openStream: async () => {
        throw new DOMException("none", "NotFoundError");
      },
      createFrameSource: () => new FakeSource(),
      createProcessor: stubProcessor,
      now: () => 0,
    });

    expect(await camera.start({ deviceId: "cam", targetAspect: 0.75, mirror: false })).toBe(false);
    expect(camera.failureReason()).toBe("noDevice");
    expect(camera.constraintStep()).toBeNull();
  });
});

// ──────────────────── 3·4. 가공기 생성 실패와 정체 판정 ────────────────────

describe("cameraService — 가공기 생성 실패를 예외로 흘리지 않는다", () => {
  it("생성이 던져도 start()는 false를 돌려주고 스트림을 정리한다", async () => {
    const stream = new FakeStream();
    const camera = createCameraService({
      openStream: async () => stream as unknown as MediaStream,
      createFrameSource: () => new FakeSource(),
      createProcessor: () => {
        throw new Error("Worker 생성 실패(CSP)");
      },
      now: () => 0,
    });

    // 예전에는 여기서 예외가 새어 화면이 로딩에 고착됐다.
    await expect(
      camera.start({ targetAspect: 0.75, mirror: false }),
    ).resolves.toBe(false);
    expect(camera.state()).toBe("Failed");
    expect(camera.failureReason()).toBe("pipelineStalled");
    // LED가 켜진 채 남지 않는다.
    expect(stream.track.stopped).toBe(true);
  });
});

describe("cameraService — Ready 타임아웃 사유를 가른다", () => {
  it("가공 프레임이 0이면 pipelineStalled다(권한 문제와 구분된다)", async () => {
    vi.useFakeTimers();
    const stream = new FakeStream();
    const camera = createCameraService({
      openStream: async () => stream as unknown as MediaStream,
      createFrameSource: () => new FakeSource(),
      createProcessor: stubProcessor,
      now: () => 0,
    });

    await camera.start({ targetAspect: 0.75, mirror: false });
    vi.advanceTimersByTime(READY_TIMEOUT_MS + 1);

    expect(camera.state()).toBe("Failed");
    expect(camera.failureReason()).toBe("pipelineStalled");
  });

  it("프레임은 오는데 조건 미달이면 unknown이다(정체가 아니다)", async () => {
    vi.useFakeTimers();
    const stream = new FakeStream();
    const source = new FakeSource();
    // 시계를 고정해 경과 조건(500ms)을 채우지 못하게 한다 → 프레임만 쌓인다.
    const camera = createCameraService({
      openStream: async () => stream as unknown as MediaStream,
      createFrameSource: () => source,
      createProcessor: stubProcessor,
      now: () => 0,
    });

    await camera.start({ targetAspect: 0.75, mirror: false });
    for (let i = 0; i < 10; i++) source.emit();
    vi.advanceTimersByTime(READY_TIMEOUT_MS + 1);

    expect(camera.state()).toBe("Failed");
    expect(camera.failureReason()).toBe("unknown");
  });
});

describe("cameraService — 경로 노출(진단 3행의 데이터원)", () => {
  it("가공·프리뷰 경로와 제약 칸을 읽을 수 있다", async () => {
    const stream = new FakeStream();
    const camera = createCameraService({
      openStream: async () => stream as unknown as MediaStream,
      createFrameSource: () => new FakeSource(),
      createProcessor: stubProcessor,
      now: () => 0,
    });

    expect(camera.pipelineMode()).toBeNull(); // 닫혀 있으면 null
    expect(camera.previewMode()).toBe("none");

    await camera.start({ targetAspect: 0.75, mirror: false });
    expect(camera.pipelineMode()).toBe("worker");
    expect(camera.constraintStep()).toBe("facing+1080p");

    camera.stop();
    expect(camera.pipelineMode()).toBeNull();
  });
});

// ──────────────────── 5. 메인 스레드 가공기(폴백 실물) ────────────────────

/** `HTMLCanvasElement` 최소 대역(node 환경 — jsdom 없이 돈다). */
function fakeCanvas(): HTMLCanvasElement {
  const calls: { setTransform: number[][]; drawImage: number } = {
    setTransform: [],
    drawImage: 0,
  };
  const ctx = {
    canvas: null as unknown as HTMLCanvasElement,
    setTransform: (...args: number[]) => calls.setTransform.push(args),
    drawImage: () => {
      calls.drawImage++;
    },
  };
  const canvas = {
    width: 0,
    height: 0,
    getContext: () => ctx,
    toBlob: (cb: (blob: Blob | null) => void) => cb(new Blob(["jpeg"])),
    /** 테스트 관찰용. */
    calls,
  } as unknown as HTMLCanvasElement;
  ctx.canvas = canvas;
  return canvas;
}

function fakeFrame(width: number, height: number): FramePayload {
  let closed = false;
  return {
    width,
    height,
    close: () => {
      closed = true;
    },
    get closed(): boolean {
      return closed;
    },
  } as unknown as FramePayload;
}

describe("mainThreadProcessor — Worker 없이 같은 규격을 만족한다(04 §2.3.1)", () => {
  it("mode가 main이다 — 진단이 저성능 모드를 표시할 근거", () => {
    const processor = createMainThreadProcessor({ createCanvas: fakeCanvas });
    expect(processor.mode).toBe("main");
  });

  it("가공 완료를 통지한다 — 이것이 Ready 판정의 입력이다", async () => {
    const processor = createMainThreadProcessor({ createCanvas: fakeCanvas, now: () => 0 });
    processor.configure({ targetAspect: 0.75, mirror: false });

    const sizes: ProcessedSize[] = [];
    processor.onProcessed((size) => sizes.push(size));
    processor.process(fakeFrame(1280, 720));
    await Promise.resolve();
    await Promise.resolve();

    // 1280×720에 3:4를 적용하면 폭이 잘린다: round(720*0.75)=540.
    expect(sizes).toEqual([{ width: 540, height: 720 }]);
  });

  it("거울모드가 **픽셀 변환**으로 적용된다(CSS 아님 — WM1)", async () => {
    const canvas = fakeCanvas();
    const processor = createMainThreadProcessor({ createCanvas: () => canvas, now: () => 0 });
    processor.configure({ targetAspect: 0.75, mirror: true });
    processor.process(fakeFrame(1280, 720));
    await Promise.resolve();
    await Promise.resolve();

    const calls = (canvas as unknown as { calls: { setTransform: number[][] } }).calls;
    // x축 반전 + 원점 이동. 이 값이 없으면 프리뷰만 뒤집히고 저장 픽셀은 원본이 된다.
    expect(calls.setTransform).toContainEqual([-1, 0, 0, 1, 540, 0]);
  });

  it("스틸을 다음 프레임에서 만든다(원자성 — 04 §5.1)", async () => {
    const processor = createMainThreadProcessor({ createCanvas: fakeCanvas, now: () => 0 });
    processor.configure({ targetAspect: 0.75, mirror: false });

    const still = processor.requestStill(0.95);
    processor.process(fakeFrame(1280, 720));
    await expect(still).resolves.toBeInstanceOf(Blob);
  });

  it("프레임을 항상 닫는다 — ImageBitmap/VideoFrame은 GC 대상이 아니다", async () => {
    const processor = createMainThreadProcessor({ createCanvas: fakeCanvas, now: () => 0 });
    processor.configure({ targetAspect: 0, mirror: false });
    const frame = fakeFrame(640, 480);
    processor.process(frame);
    await Promise.resolve();
    await Promise.resolve();
    expect((frame as unknown as { closed: boolean }).closed).toBe(true);
  });

  it("terminate가 대기 중 스틸을 즉시 끊는다(5초 타임아웃까지 매달지 않는다)", async () => {
    const processor = createMainThreadProcessor({ createCanvas: fakeCanvas });
    const still = processor.requestStill(0.95);
    processor.terminate();
    await expect(still).resolves.toBeNull();
  });

  it("bindPreview가 direct 경로를 보고한다", () => {
    const processor = createMainThreadProcessor({ createCanvas: fakeCanvas });
    expect(processor.previewMode()).toBe("none");
    expect(processor.bindPreview(fakeCanvas())).toBe(true);
    expect(processor.previewMode()).toBe("direct");
  });

  it("스풀 off에서는 프레임을 만들지 않는다", async () => {
    const processor = createMainThreadProcessor({ createCanvas: fakeCanvas, now: () => 1000 });
    processor.configure({ targetAspect: 0, mirror: false });
    const frames: unknown[] = [];
    processor.onSpoolFrame((frame) => frames.push(frame));
    processor.process(fakeFrame(640, 480));
    await Promise.resolve();
    await Promise.resolve();
    expect(frames).toHaveLength(0);
  });
});

// ──────────────────── 6. 정적 불변식 CAM-2 · CAM-3 ────────────────────

const SRC = join(dirname(fileURLToPath(import.meta.url)), "..", "..", "..", "src");

function code(relative: string): string {
  return readFileSync(join(SRC, relative), "utf8")
    .replace(/\/\*[\s\S]*?\*\//g, "")
    .replace(/(^|[^:])\/\/.*$/gm, "$1");
}

describe("CAM-2 — frameRate 하한 제약 금지", () => {
  it("카메라 어댑터 어디에도 frameRate min이 없다", () => {
    // `min: 15`가 저조도 안드로이드를 튕겼고, 폴백이 해상도·전후면을 통째로 버렸다.
    for (const file of ["adapters/camera/cameraConstraints.ts", "adapters/camera/cameraService.ts"]) {
      expect(code(file)).not.toMatch(/frameRate\s*:\s*\{[^}]*\bmin\b/);
    }
  });
});

describe("CAM-3 — 프레임 소스 <video> 숨김 방식", () => {
  it("display:none을 쓰지 않는다 — WebKit에서 프레임 콜백이 멈춘다", () => {
    const source = code("adapters/camera/videoFrameSource.ts");
    expect(source).not.toMatch(/display\s*=\s*["']none["']/);
    // 대신 1×1 투명 고정 배치로 렌더링 트리에 남긴다.
    expect(source).toMatch(/position\s*=\s*["']fixed["']/);
    expect(source).toMatch(/opacity\s*=\s*["']0["']/);
  });
});

describe("CAM-4 — 폴백이 실제로 배선돼 있다", () => {
  it("spawnFrameProcessor가 능력 판정과 메인 스레드 폴백을 모두 참조한다", () => {
    const client = code("adapters/camera/frameProcessorClient.ts");
    // 판정 함수가 dead code로 되돌아가는 것을 막는다(2026-08-06 이전 상태).
    expect(client).toMatch(/isWorkerPipelineSupported\s*\(\s*\)/);
    expect(client).toMatch(/createMainThreadProcessor\s*\(/);
    // Worker 생성이 try 안에 있다.
    expect(client).toMatch(/try\s*\{[\s\S]*new Worker\(/);
  });

  it("cameraService가 transferControlToOffscreen을 직접 부르지 않는다(가공기에 위임)", () => {
    // 직접 부르면 실패 시 폴백 없이 검은 화면이 된다 — 그것이 이 수정의 원인이다.
    expect(code("adapters/camera/cameraService.ts")).not.toContain("transferControlToOffscreen");
  });
});

describe("resolveStartDeviceId — WC3 폴백을 실제로 태우는 진입점", () => {
  const devices = [
    { deviceId: "a", label: "Front", groupId: "g1" },
    { deviceId: "b", label: "Back", groupId: "g2" },
  ];

  it("저장한 적이 없으면 null이다 — facingMode 경로로 간다", () => {
    expect(hasStoredDevice(null)).toBe(false);
    expect(hasStoredDevice({ deviceId: "", label: "", groupId: "" })).toBe(false);
    expect(resolveStartDeviceId(devices, null)).toEqual({ deviceId: null, reason: "none" });
  });

  it("deviceId가 살아 있으면 그대로 쓴다", () => {
    expect(resolveStartDeviceId(devices, { deviceId: "b", label: "", groupId: "" })).toEqual({
      deviceId: "b",
      reason: "deviceId",
    });
  });

  it("deviceId가 바뀌었어도 **라벨로 되찾는다** — 모바일 재방문의 정상 경로", () => {
    // deviceId는 브라우저·OS 재시작으로 바뀐다. 이 폴백이 없으면 매번 엉뚱한 카메라가 열린다.
    expect(
      resolveStartDeviceId(devices, { deviceId: "stale", label: "Back", groupId: "" }),
    ).toEqual({ deviceId: "b", reason: "label" });
  });

  it("라벨까지 비어 있으면 groupId로 되찾는다", () => {
    expect(
      resolveStartDeviceId(devices, { deviceId: "stale", label: "", groupId: "g2" }),
    ).toEqual({ deviceId: "b", reason: "groupId" });
  });

  it("저장한 장치가 사라졌으면 **첫 장치를 강요하지 않고** null이다", () => {
    // `matchDevice`는 첫 장치를 돌려주지만 그것은 임의값이다. 모바일에서 첫 장치가 후면인
    // 기기가 많아, 강요하면 전면 설정을 조용히 뒤집는다 → facingMode에 맡긴다.
    const resolution = resolveStartDeviceId(devices, {
      deviceId: "gone",
      label: "없음",
      groupId: "없음",
    });
    expect(resolution).toEqual({ deviceId: null, reason: "first" });
  });

  it("장치 목록이 비어 있어도(열거 실패) 안전하게 null이다", () => {
    expect(resolveStartDeviceId([], { deviceId: "a", label: "A", groupId: "g" })).toEqual({
      deviceId: null,
      reason: "none",
    });
  });
});

describe("CAM-6 — WC3 폴백이 프로덕션 경로에 배선돼 있다", () => {
  /**
   * `matchDevice`의 호출처가 테스트뿐이던 상태를 막는다(2026-08-06 이전).
   * 그 동안 `CameraDeviceLabel`·`CameraDeviceGroupId`는 **쓰기만 되고 읽히지 않는** 값이었다.
   */
  /**
   * 되돌림 형태 = `deviceId: values.CameraDevice.length > 0 ? values.CameraDevice : null`
   * (2026-08-06 이전의 실제 코드). `storedRef` 구성에서 `values.CameraDevice`를 **입력으로**
   * 읽는 것은 정당하므로 그것까지 막지 않는다 — 막으면 폴백 자체를 못 만든다.
   */
  const RAW_PASS_THROUGH = /deviceId:\s*values\.CameraDevice\s*\.\s*length/;

  it("촬영이 저장 deviceId를 그대로 넘기지 않는다", () => {
    const runner = code("screens/capture/useCaptureRunner.ts");
    expect(runner).toMatch(/resolveStartDeviceId\(/);
    expect(runner).not.toMatch(RAW_PASS_THROUGH);
  });

  it("카메라 테스트 모달도 같은 해석을 쓴다", () => {
    const modal = code("screens/modals/cameraTest/CameraTestModal.tsx");
    expect(modal).toMatch(/resolveStartDeviceId\(/);
    expect(modal).not.toMatch(RAW_PASS_THROUGH);
  });

  it("라벨·groupId가 해석 입력으로 실제로 읽힌다", () => {
    for (const file of [
      "screens/capture/useCaptureRunner.ts",
      "screens/modals/cameraTest/CameraTestModal.tsx",
    ]) {
      expect(code(file)).toMatch(/CameraDeviceLabel/);
      expect(code(file)).toMatch(/CameraDeviceGroupId/);
    }
  });
});

describe("CAM-5 — CameraFacing 설정이 실제로 카메라에 도달한다", () => {
  /**
   * 설정 화면에 편집 UI가 있는데 `camera.start()`에 값이 전달되지 않던 상태를 막는다
   * (2026-08-06 이전). 저장은 되고 적용은 안 되는 설정은 **UI가 손님에게 거짓을 말하는 것**이다.
   */
  it("실촬영이 facing을 넘긴다", () => {
    const runner = code("screens/capture/useCaptureRunner.ts");
    expect(runner).toMatch(/CameraFacing/);
    // start() 인자 목록에 facing이 있다.
    expect(runner).toMatch(/camera\.start\(\{[\s\S]*?\bfacing\b[\s\S]*?\}\)/);
  });

  it("카메라 테스트 모달도 같은 값을 넘긴다 — 동일 재현이 목적이다", () => {
    const modal = code("screens/modals/cameraTest/CameraTestModal.tsx");
    // 3개 호출부(진입·장치 변경·재시도) 전부.
    const opens = modal.match(/presenter\.open\(\{/g) ?? [];
    const facings = modal.match(/facing:\s*webExtras\.CameraFacing/g) ?? [];
    expect(facings).toHaveLength(opens.length);
  });

  it("설정 UI에 편집 수단이 남아 있다(배선만 있고 UI가 사라지는 반대 상황 방지)", () => {
    expect(code("ui/views/SettingsView.tsx")).toMatch(/CameraFacing/);
  });
});
