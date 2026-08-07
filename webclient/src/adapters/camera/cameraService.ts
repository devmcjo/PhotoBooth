import {
  cameraFailure,
  classifyCameraFailureFrom,
  formatCameraFailureCode,
  type CameraFailure,
  type CameraFailureReason,
} from "@domain/capture/cameraFailure";
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
  FrameProcessorMode,
  FrameSource,
  FrameTransferMode,
  PreviewMode,
  ProcessedSize,
  SpoolFrame,
  SpoolOptions,
} from "./cameraTypes";
import { constraintLadder, shouldTryNextStep } from "./cameraConstraints";
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
  /**
   * 마지막 실패 사유. 실패한 적이 없으면 `null`.
   *
   * 화면은 이 값으로 **사유별 문구·[다시 시도] 노출**을 정한다(03 §6.3) —
   * 권한 거부와 장치 부재는 손님이 할 조치가 완전히 다르다.
   */
  failureReason(): CameraFailureReason | null;
  /**
   * 마지막 실패의 **사유 + 상세**. 실패한 적이 없으면 `null`.
   *
   * 화면·진단이 `오류 코드 <사유>/<상세>`를 보이는 근거다 — 사유만으로는 `unknown` 하나에
   * 성격이 다른 예외가 모여 현장에서 원인을 좁힐 수 없다(2026-08-07 신설).
   */
  failure(): CameraFailure | null;
  /** 실제 획득값. 열려 있지 않으면 null. */
  settings(): CameraSettings | null;
  /** 가공 결과 크기(크롭 후). */
  processedSize(): ProcessedSize | null;
  /** 최근 1초 가공 fps. */
  fps(): number;
  /**
   * 현재 가공 경로. 열려 있지 않으면 `null`.
   * 진단이 **"저성능 모드"** 를 표시하는 근거다(04 §2.3.1).
   */
  pipelineMode(): FrameProcessorMode | null;
  /** 현재 프리뷰 연결 방식. `none`이면 화면에 아무것도 그려지지 않는다. */
  previewMode(): PreviewMode;
  /**
   * 현재 프레임 전달 경로(04 §2.3.2). 카메라가 닫혀 있으면 `null`.
   * `imageBitmapDemoted`는 **정상 폴백이 아니라 브라우저 결함 신호**다.
   */
  frameTransferMode(): FrameTransferMode | null;
  /** 실제로 스트림이 열린 제약 사다리 칸(04 §2.1). 열려 있지 않으면 `null`. */
  constraintStep(): string | null;
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

/** 열기 결과. 어느 칸에서 열렸는지 함께 돌려준다(진단·로그). */
interface OpenResult {
  readonly stream: MediaStream;
  readonly step: string;
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
  /** 실제로 열린 제약 사다리 칸. 진단이 "왜 해상도가 낮은가"를 답하는 근거다. */
  let currentStep: string | null = null;
  /**
   * 마지막 실패(사유 + 상세). `teardown()`이 지우지 않는다 — 실패 직후 teardown이 돌기 때문이다.
   *
   * ⚠️ **대입은 `null` 또는 `cameraFailure(...)`/`classifyCameraFailureFrom(...)` 결과뿐이다**
   *    (정적 검사 CAM-7). 객체 리터럴로 우회하면 `err.message`가 화면 코드로 새어 나간다.
   */
  let lastFailure: CameraFailure | null = null;

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
    currentStep = null;
  }

  /** `isSecureContext` 미지원 환경(구형 WebView·node 테스트)에서는 `true`로 본다. */
  function secureContext(): boolean {
    return typeof isSecureContext === "boolean" ? isSecureContext : true;
  }

  /**
   * `getUserMedia` 예외를 사유+상세로 분류해 보관한다. 화면은 `failure()`/`failureReason()`으로 읽는다.
   * 상세는 예외 **이름**이다(`message`가 아니다 — 기기명·경로가 섞일 수 있다).
   */
  function recordFailure(err: unknown): CameraFailure {
    lastFailure = classifyCameraFailureFrom(err, secureContext());
    return lastFailure;
  }

  /** 파이프라인 내부 사유(예외가 없는 실패)를 보관한다. */
  function recordPipelineFailure(
    reason: CameraFailureReason,
    detail?: string | null,
  ): CameraFailure {
    lastFailure = cameraFailure(reason, detail);
    return lastFailure;
  }

  /**
   * 제약 **사다리**를 위에서부터 내려가며 연다 — 04 §2.1.
   *
   * ⚠️ 이 사다리를 한 벌 제약으로 되돌리지 마라. 예전에는 `frameRate: { min: 15 }`가 걸린 요청
   *    하나뿐이었고, 실패하면 `{ video: true }`로 곧장 떨어져 **해상도와 전후면이 통째로**
   *    사라졌다. 사다리는 요구를 한 칸씩만 낮춘다.
   * ⚠️ 사유는 **마지막 실패**로 확정한다. 마지막 칸이 `{ video: true }`라서, 그것마저 실패한
   *    이유가 손님에게 가장 정확한 안내다(권한/장치없음/점유).
   */
  async function open(options: CameraStartOptions): Promise<OpenResult | null> {
    const ladder = constraintLadder({
      deviceId: options.deviceId ?? null,
      facing: options.facing ?? "user",
    });

    let lastError: unknown = null;

    for (let index = 0; index < ladder.length; index++) {
      const step = ladder[index]!;
      try {
        const opened = await openStream(step.constraints);
        lastFailure = null; // 성공하면 직전 실패 흔적을 지운다.
        if (index > 0) {
          // 첫 칸이 아니면 요청 해상도가 낮아졌을 수 있다 — 진단에서 원인을 찾을 수 있게 남긴다.
          logger.warn("카메라를 낮은 제약으로 열었다", { step: step.label, attempts: index + 1 });
        }
        return { stream: opened, step: step.label };
      } catch (err) {
        lastError = err;
        const name = err instanceof Error ? err.name : "";
        if (!shouldTryNextStep(name)) {
          // 권한 거부·브라우저 미지원 — 제약을 낮춰도 결과가 같다. 즉시 확정한다.
          const failure = recordFailure(err);
          logger.error("카메라 열기 실패(즉시 확정)", {
            step: step.label,
            failureReason: failure.reason,
            failureCode: formatCameraFailureCode(failure),
          });
          return null;
        }
        logger.warn("카메라 제약 단계 실패 — 다음 단계 시도", {
          step: step.label,
          name,
          remaining: ladder.length - index - 1,
        });
      }
    }

    const failure = recordFailure(lastError);
    logger.error("카메라 열기 실패(사다리 전부 소진)", {
      steps: ladder.length,
      failureReason: failure.reason,
      // ⚠️ 확정 단계의 예외 **이름**을 남긴다. 이것이 없어서 `unknown`이 떴을 때 어떤 예외였는지
      //    통째로 유실됐다(중간 단계 로그는 `name`을 남기는데 확정 단계만 빠져 있었다).
      name: lastError instanceof Error ? lastError.name : "",
      failureCode: formatCameraFailureCode(failure),
      // `message`는 진단에 도움이 되지만 **`failureCode`에는 절대 넣지 않는다**(기기명·경로 혼입).
      reason: lastError instanceof Error ? lastError.message : String(lastError),
    });
    return null;
  }

  return {
    state: () => state,
    failure: () => lastFailure,
    // 기존 호출처(문구·[다시 시도] 판정)는 사유만 보면 된다 — 두 값이 어긋나지 않게 파생시킨다.
    failureReason: () => lastFailure?.reason ?? null,
    settings: () => currentSettings,
    processedSize: () => lastProcessedSize,
    fps: () => meter.fps(now()),
    pipelineMode: () => processor?.mode ?? null,
    previewMode: () => processor?.previewMode() ?? "none",
    frameTransferMode: () => source?.transferMode() ?? null,
    constraintStep: () => currentStep,

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
        // detail은 **사유 열거값**이다(한국어 문장이 아니다) — 문구 결정은 화면이 한다(03 §6.3).
        setState("Failed", lastFailure?.reason ?? "unknown");
        teardown();
        return false;
      }
      stream = opened.stream;
      currentStep = opened.step;

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

      // ⚠️ 가공기 생성은 **던질 수 있다**(Worker 생성 실패·CSP). 여기서 삼키지 않으면
      //    `start()`가 예외로 끝나 "예외를 던지지 않는다"는 계약(위 §33)이 깨지고, 화면은
      //    로딩에 고착된다. `spawnFrameProcessor`도 자체 폴백을 갖지만 주입 팩토리는 그렇지 않다.
      try {
        processor = makeProcessor();
      } catch (err) {
        logger.error("프레임 가공기 생성 실패", {
          reason: err instanceof Error ? err.message : String(err),
        });
        recordPipelineFailure("pipelineStalled", "processor-spawn");
        setState("Failed", "pipelineStalled");
        teardown();
        return false;
      }
      processor.configure({ targetAspect: options.targetAspect, mirror: options.mirror });
      unsubscribeProcessed = processor.onProcessed(onProcessed);
      unsubscribeSpool = processor.onSpoolFrame((frame) => {
        for (const listener of spoolListeners) listener(frame);
      });

      source = makeFrameSource();
      unsubscribeFrames = source.onFrame((payload) => processor?.process(payload));

      /*
       * ⚠️ **`if (!result.ok)`이다 — `if (!result)`로 되돌리지 마라.** `attach()`는 이제 객체를
       *    돌려주고 객체는 항상 truthy라, `!result`는 구문상 유효한데 **항상 거짓**이다
       *    (`tsc`가 잡지 못한다). 그러면 재생 실패가 조용히 성공으로 처리되어 Ready 타임아웃
       *    8초를 기다린 뒤 엉뚱한 사유로 실패한다.
       */
      const result = await source.attach(stream);
      if (!result.ok) {
        // 스트림은 열렸는데 재생이 시작되지 않았다 — 권한·장치 문제가 아니다(iOS 자동재생 정책 등).
        const failure = recordPipelineFailure("playbackBlocked", result.errorName);
        logger.error("카메라 재생 시작 실패", {
          failureReason: failure.reason,
          failureCode: formatCameraFailureCode(failure),
        });
        setState("Failed", "playbackBlocked");
        teardown();
        return false;
      }

      // 무한 로딩 금지 — 8초 안에 Ready가 되지 않으면 Failed로 확정한다.
      readyTimer = setTimeout(() => {
        readyTimer = null;
        if (state === "Starting") {
          /*
           * 가공 프레임이 **한 장도** 없으면 파이프라인 정체다(권한·장치 문제가 아니다).
           * 프레임이 오는데도 Ready가 아니면 **느린 것**이지 막힌 것이 아니다 → `pipelineSlow`.
           *
           * 상세가 방향을 가른다: 정체는 `{가공경로}-{프리뷰경로}`(`worker-transferred`면
           * 프레임 소스 쪽, `main-none`이면 프리뷰가 못 붙은 것), 느림은 `f{가공프레임수}`.
           */
          const failure =
            meter.total === 0
              ? recordPipelineFailure(
                  "pipelineStalled",
                  `${processor?.mode ?? "?"}-${processor?.previewMode() ?? "none"}`,
                )
              : recordPipelineFailure("pipelineSlow", `f${meter.total}`);
          logger.error("카메라 Ready 타임아웃", {
            elapsedMs: Math.round(now() - startedAt),
            processedFrames: meter.total,
            failureReason: failure.reason,
            failureCode: formatCameraFailureCode(failure),
            pipelineMode: processor?.mode ?? null,
            previewMode: processor?.previewMode() ?? "none",
            frameTransferMode: source?.transferMode() ?? null,
            constraintStep: currentStep,
          });
          setState("Failed", failure.reason);
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

    /**
     * 프리뷰 연결을 **가공기에 위임**한다(2026-08-06 변경).
     *
     * ⚠️ 여기서 `transferControlToOffscreen()`을 직접 부르지 마라. 예전에는 그랬고, 이관이
     *    안 되는 브라우저에서는 폴백 없이 `false`만 돌려주어 **상태는 Ready인데 화면은
     *    검은색**이 됐다. 어떤 방식으로 붙일지는 경로를 아는 가공기가 정한다.
     */
    bindPreview(canvas) {
      if (processor === null) return false;
      try {
        return processor.bindPreview(canvas);
      } catch (err) {
        logger.warn("프리뷰 연결 실패", {
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
