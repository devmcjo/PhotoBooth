import {
  createPreviewReadiness,
  onFrame as advanceReadiness,
  type PreviewReadinessState,
} from "@domain/capture/previewReadiness";
import { logger } from "@adapters/storage/logStore";
import type {
  CameraSettings,
  CameraStartOptions,
  CameraState,
  CameraStateListener,
  FrameProcessor,
  FrameSource,
  ProcessedSize,
  SpoolFrame,
  SpoolOptions,
} from "./cameraTypes";
import { createFpsMeter, type FpsMeter } from "./fpsMeter";
import { spawnFrameProcessor } from "./frameProcessorClient";
import { STILL_JPEG_QUALITY } from "./frameProcessorProtocol";
import { createVideoFrameSource } from "./videoFrameSource";

/**
 * 카메라 서비스 — 04 §2·§3 (**모듈 싱글턴 1개**)
 *
 * 실촬영·라이브 프리뷰·카메라 테스트 모달이 **같은 인스턴스를 빌려 쓴다**.
 * 화면이 카메라를 소유하면 모달을 닫을 때 실촬영 스트림까지 끊기거나, 반대로 LED가 켜진 채 남는다.
 *
 * ⚠️ **장치 열기 실패를 예외로 던지지 않는다.** `false`를 돌려주고 상위가 `Failed` 상태로
 *    안내한다(analysis/14 §2.1 · 01 §2.1).
 *
 * ⚠️ **`start()`는 멱등이며 자동 재시작하지 않는다.** 장치를 바꿀 때는 호출측이
 *    `stop()` 후 `start()`를 부른다(카메라 테스트 모달이 그렇게 한다 — 03 §15.1).
 */

export const READY_TIMEOUT_MS = 8000;

export interface CameraServiceDeps {
  readonly openStream?: (constraints: MediaStreamConstraints) => Promise<MediaStream>;
  readonly createFrameSource?: () => FrameSource;
  readonly createProcessor?: () => FrameProcessor;
  readonly now?: () => number;
  readonly readyTimeoutMs?: number;
}

export interface CameraService {
  /** 카메라를 연다. 실패는 `false`(예외 없음). 이미 실행 중이면 무시하고 `true`. */
  start(options: CameraStartOptions): Promise<boolean>;
  stop(): void;
  /** 거울·종횡비 런타임 변경. 재시작 없이 즉시 반영된다. */
  configure(options: { targetAspect?: number; mirror?: boolean }): void;
  /** 다음 가공 프레임에서 스틸 JPEG를 만든다. 실패·타임아웃은 `null`. */
  captureStill(quality?: number): Promise<Blob | null>;
  /** 프리뷰 캔버스 제어권 이관(zero-copy). 실패해도 무해하다. */
  bindPreview(canvas: HTMLCanvasElement): boolean;
  state(): CameraState;
  /** 실제 획득값. 열려 있지 않으면 null. */
  settings(): CameraSettings | null;
  /** 가공 결과 크기(크롭 후). */
  processedSize(): ProcessedSize | null;
  /** 최근 1초 가공 fps. */
  fps(): number;
  onState(listener: CameraStateListener): () => void;
  /** 가공 완료 통지(계측용). */
  onProcessedFrame(listener: (size: ProcessedSize) => void): () => void;
  /**
   * 타임랩스 스풀 채널 on/off(04 §7.2). 카메라가 열려 있지 않으면 **무해한 no-op**이다
   * (촬영 종료 뒤 off를 부르는 경로가 있다 — 그때 예외가 나면 안 된다).
   */
  configureTimelapseSpool(options: SpoolOptions): void;
  /** 스풀 프레임 도착 구독. 카메라 재시작을 넘어 유지된다. */
  onTimelapseFrame(listener: (frame: SpoolFrame) => void): () => void;
}

function buildConstraints(options: CameraStartOptions): MediaStreamConstraints {
  const deviceId = options.deviceId ?? null;
  return {
    // 오디오는 전혀 쓰지 않는다 — 타임랩스는 무음이 규격이고, 권한 범위만 넓어진다.
    audio: false,
    video: {
      width: { ideal: 1920 },
      height: { ideal: 1080 },
      frameRate: { ideal: 30, min: 15 },
      ...(deviceId !== null && deviceId.length > 0
        ? { deviceId: { exact: deviceId } }
        : { facingMode: { ideal: options.facing ?? "user" } }),
    },
  };
}

export function createCameraService(deps: CameraServiceDeps = {}): CameraService {
  const openStream =
    deps.openStream ??
    ((constraints: MediaStreamConstraints) => navigator.mediaDevices.getUserMedia(constraints));
  const makeFrameSource = deps.createFrameSource ?? (() => createVideoFrameSource());
  const makeProcessor = deps.createProcessor ?? (() => spawnFrameProcessor());
  const now = deps.now ?? (() => performance.now());
  const readyTimeoutMs = deps.readyTimeoutMs ?? READY_TIMEOUT_MS;

  let state: CameraState = "Idle";
  let stream: MediaStream | null = null;
  let source: FrameSource | null = null;
  let processor: FrameProcessor | null = null;
  let unsubscribeFrames: (() => void) | null = null;
  let unsubscribeProcessed: (() => void) | null = null;
  let unsubscribeSpool: (() => void) | null = null;
  let readiness: PreviewReadinessState = createPreviewReadiness();
  let meter: FpsMeter = createFpsMeter();
  let startedAt = 0;
  let readyTimer: ReturnType<typeof setTimeout> | null = null;
  let currentSettings: CameraSettings | null = null;
  let lastProcessedSize: ProcessedSize | null = null;
  let currentOptions: CameraStartOptions | null = null;

  const stateListeners = new Set<CameraStateListener>();
  const processedListeners = new Set<(size: ProcessedSize) => void>();
  /**
   * 스풀 구독자는 **서비스가 들고 있다**(프로세서가 아니라). 프로세서는 카메라 재시작마다
   * 새로 만들어지므로, 프로세서에 직접 붙이면 재시작 한 번에 구독이 조용히 사라진다.
   */
  const spoolListeners = new Set<(frame: SpoolFrame) => void>();

  function setState(next: CameraState, detail?: string): void {
    if (state === next) return;
    state = next;
    for (const listener of stateListeners) listener(next, detail);
  }

  function clearReadyTimer(): void {
    if (readyTimer !== null) {
      clearTimeout(readyTimer);
      readyTimer = null;
    }
  }

  function onProcessed(size: ProcessedSize): void {
    lastProcessedSize = size;
    const timestamp = now();
    meter.mark(timestamp);

    // Ready 판정은 **가공 완료** 기준이다(획득 수가 아니다 — 가공이 막히면 프리뷰가 멈춘 것이다).
    if (state === "Starting") {
      const result = advanceReadiness(readiness, timestamp - startedAt, meter.fps(timestamp));
      readiness = result.state;
      if (result.becameReady) {
        clearReadyTimer();
        setState("Ready");
        logger.info("카메라 Ready", {
          elapsedMs: Math.round(timestamp - startedAt),
          frames: meter.total,
          fps: meter.fps(timestamp),
          width: size.width,
          height: size.height,
        });
      }
    }

    for (const listener of processedListeners) listener(size);
  }

  function teardown(): void {
    clearReadyTimer();
    unsubscribeFrames?.();
    unsubscribeProcessed?.();
    unsubscribeSpool?.();
    unsubscribeFrames = null;
    unsubscribeProcessed = null;
    unsubscribeSpool = null;

    source?.detach();
    source = null;

    processor?.terminate();
    processor = null;

    // 트랙을 멈춰야 카메라 LED가 꺼진다. 이것을 빠뜨리면 모달을 닫아도 LED가 켜진 채 남는다.
    stream?.getTracks().forEach((track) => track.stop());
    stream = null;

    currentSettings = null;
    lastProcessedSize = null;
    currentOptions = null;
  }

  async function open(options: CameraStartOptions): Promise<MediaStream | null> {
    try {
      return await openStream(buildConstraints(options));
    } catch (err) {
      const name = err instanceof Error ? err.name : "";
      // 저장된 deviceId가 사라진 경우다(장치 교체·권한 재부여). 제약 없이 첫 장치로 재시도한다.
      if (name === "OverconstrainedError" || name === "NotFoundError") {
        logger.warn("지정한 카메라를 열 수 없어 기본 장치로 재시도", { deviceId: options.deviceId });
        try {
          return await openStream({ audio: false, video: true });
        } catch (retryErr) {
          logger.error("카메라 열기 실패(재시도 포함)", {
            reason: retryErr instanceof Error ? retryErr.message : String(retryErr),
          });
          return null;
        }
      }
      logger.error("카메라 열기 실패", {
        name,
        reason: err instanceof Error ? err.message : String(err),
      });
      return null;
    }
  }

  return {
    state: () => state,
    settings: () => currentSettings,
    processedSize: () => lastProcessedSize,
    fps: () => meter.fps(now()),

    onState(listener) {
      stateListeners.add(listener);
      return () => stateListeners.delete(listener);
    },

    onProcessedFrame(listener) {
      processedListeners.add(listener);
      return () => processedListeners.delete(listener);
    },

    onTimelapseFrame(listener) {
      spoolListeners.add(listener);
      return () => spoolListeners.delete(listener);
    },

    configureTimelapseSpool(options) {
      // 카메라가 이미 멈춰 프로세서가 없을 수 있다(수집 종료가 정지 뒤에 오는 경로).
      processor?.configureSpool(options);
    },

    async start(options) {
      // 멱등: 이미 열려 있으면 성공으로 본다. 장치 변경은 호출측이 stop() 후 부른다.
      if (state === "Starting" || state === "Ready") {
        // 종횡비·거울은 재시작 없이 반영한다.
        this.configure(options);
        return true;
      }

      setState("Starting");
      startedAt = now();
      readiness = createPreviewReadiness();
      meter = createFpsMeter();
      currentOptions = options;

      const opened = await open(options);
      if (opened === null) {
        setState("Failed", "카메라를 열 수 없습니다.");
        teardown();
        return false;
      }
      stream = opened;

      const track = stream.getVideoTracks()[0];
      const trackSettings = track?.getSettings() ?? {};
      currentSettings = {
        deviceId: trackSettings.deviceId ?? null,
        label: track?.label ?? null,
        width: trackSettings.width ?? 0,
        height: trackSettings.height ?? 0,
        frameRate: trackSettings.frameRate ?? null,
      };
      logger.info("카메라 시작", {
        requestedDeviceId: options.deviceId ?? null,
        actualDeviceId: currentSettings.deviceId,
        width: currentSettings.width,
        height: currentSettings.height,
        frameRate: currentSettings.frameRate,
        targetAspect: options.targetAspect,
        mirror: options.mirror,
      });

      processor = makeProcessor();
      processor.configure({ targetAspect: options.targetAspect, mirror: options.mirror });
      unsubscribeProcessed = processor.onProcessed(onProcessed);
      unsubscribeSpool = processor.onSpoolFrame((frame) => {
        for (const listener of spoolListeners) listener(frame);
      });

      source = makeFrameSource();
      unsubscribeFrames = source.onFrame((payload) => processor?.process(payload));

      const attached = await source.attach(stream);
      if (!attached) {
        setState("Failed", "카메라 재생을 시작할 수 없습니다.");
        teardown();
        return false;
      }

      // 무한 로딩 금지 — 8초 안에 Ready가 되지 않으면 Failed로 확정한다.
      readyTimer = setTimeout(() => {
        readyTimer = null;
        if (state === "Starting") {
          logger.error("카메라 Ready 타임아웃", {
            elapsedMs: Math.round(now() - startedAt),
            processedFrames: meter.total,
          });
          setState("Failed", "카메라 준비가 완료되지 않았습니다.");
          teardown();
        }
      }, readyTimeoutMs);

      return true;
    },

    stop() {
      if (state === "Idle") return;
      teardown();
      setState("Idle");
      logger.info("카메라 정지");
    },

    configure(options) {
      if (currentOptions !== null) {
        currentOptions = {
          ...currentOptions,
          targetAspect: options.targetAspect ?? currentOptions.targetAspect,
          mirror: options.mirror ?? currentOptions.mirror,
        };
        processor?.configure({
          targetAspect: currentOptions.targetAspect,
          mirror: currentOptions.mirror,
        });
      }
    },

    captureStill(quality = STILL_JPEG_QUALITY) {
      if (processor === null || state !== "Ready") {
        logger.warn("스틸 캡처 요청 거부(카메라 준비 전)", { state });
        return Promise.resolve(null);
      }
      return processor.requestStill(quality);
    },

    bindPreview(canvas) {
      if (processor === null) return false;
      if (typeof canvas.transferControlToOffscreen !== "function") return false;
      try {
        processor.bindPreview(canvas.transferControlToOffscreen());
        return true;
      } catch (err) {
        // 이미 이관된 캔버스를 다시 넘기면 던진다 — 무해하게 넘긴다.
        logger.warn("프리뷰 캔버스 이관 실패", {
          reason: err instanceof Error ? err.message : String(err),
        });
        return false;
      }
    },
  };
}

let singleton: CameraService | null = null;

/** 앱 전역 카메라. **하드웨어 단일 소유**(01 §2.1). */
export function getCameraService(): CameraService {
  singleton ??= createCameraService();
  return singleton;
}

export function setCameraServiceForTests(service: CameraService | null): void {
  singleton = service;
}
