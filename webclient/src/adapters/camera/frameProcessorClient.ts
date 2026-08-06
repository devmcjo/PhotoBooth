import { logger } from "@adapters/storage/logStore";
import type {
  FrameProcessor,
  FramePayload,
  PreviewMode,
  ProcessedSize,
  SpoolFrame,
} from "./cameraTypes";
import { createMainThreadProcessor } from "./mainThreadProcessor";
import {
  STILL_JPEG_QUALITY,
  type FrameProcessorRequest,
  type FrameProcessorResponse,
} from "./frameProcessorProtocol";

/**
 * 가공 Worker RPC 클라이언트 — 04 §4
 *
 * ## 2026-08-06: 폴백 2개를 실제로 배선했다
 *
 * 그 전까지 `isWorkerPipelineSupported()`는 **정의만 있고 호출처가 없었다**. 설계(04 §2.3.1 ·
 * 10 §6.2)가 요구한 두 폴백이 코드에 없었고, 그래서 `OffscreenCanvas`나
 * `transferControlToOffscreen`이 없는 기기에서는 다음이 벌어졌다:
 *
 * - `OffscreenCanvas` 없음 → Worker가 캔버스를 못 만들어 `processed`가 **한 번도 오지 않음** →
 *   8초 Ready 타임아웃 → "카메라를 사용할 수 없습니다"(사유는 `unknown`)
 * - 이관 불가 → `bindPreview`가 `false`만 돌려주고 끝 → **상태는 Ready인데 화면은 검은색**
 *
 * 지금은 ① Worker 경로를 쓸 수 없으면 메인 스레드 가공기로 내려가고, ② 이관이 안 되면
 * 비트맵 전송 채널로 내려간다. 어느 경로인지는 **진단에 표시**된다.
 */

export interface WorkerLike {
  postMessage(message: FrameProcessorRequest, options?: StructuredSerializeOptions): void;
  addEventListener(
    type: "message",
    listener: (event: MessageEvent<FrameProcessorResponse>) => void,
  ): void;
  terminate(): void;
}

/**
 * Worker 가공 경로를 쓸 수 있는가(런타임 감지 — UA 분기 금지).
 *
 * ⚠️ `OffscreenCanvas`의 **존재만으로는 부족하다.** 생성자가 있는데 `getContext("2d")`가
 *    `null`인 구현이 있으므로 실제로 하나 만들어 확인한다(10 §6.2가 요구하는 판정 방식).
 */
export function isWorkerPipelineSupported(): boolean {
  if (typeof Worker === "undefined" || typeof OffscreenCanvas === "undefined") return false;
  try {
    // 1×1이면 비용이 사실상 없다. 여기서 던지는 브라우저가 실제로 있다.
    return new OffscreenCanvas(1, 1).getContext("2d") !== null;
  } catch {
    return false;
  }
}

const STILL_TIMEOUT_MS = 5000;

export function createFrameProcessorClient(worker: WorkerLike): FrameProcessor {
  const processedListeners = new Set<(size: ProcessedSize) => void>();
  const spoolListeners = new Set<(frame: SpoolFrame) => void>();
  const pendingStills = new Map<number, (blob: Blob | null) => void>();
  let nextStillId = 1;
  let previewMode: PreviewMode = "none";
  /** 비트맵 폴백에서 그릴 대상. 이관 경로에서는 `null`이다(Worker가 직접 그린다). */
  let previewCtx: CanvasRenderingContext2D | null = null;

  worker.addEventListener("message", (event) => {
    const response = event.data;
    if (response.type === "processed") {
      for (const listener of processedListeners) {
        listener({ width: response.width, height: response.height });
      }
      return;
    }
    if (response.type === "previewFrame") {
      drawPreview(response.bitmap);
      return;
    }
    if (response.type === "spoolFrame") {
      for (const listener of spoolListeners) {
        listener({ blob: response.blob, width: response.width, height: response.height });
      }
      return;
    }
    const resolve = pendingStills.get(response.id);
    if (resolve !== undefined) {
      pendingStills.delete(response.id);
      if (response.error !== undefined) {
        logger.warn("스틸 캡처 실패", { reason: response.error });
      }
      resolve(response.blob);
    }
  });

  /**
   * 비트맵 폴백 렌더. **어떤 경로로 나가도 `close()`** 한다 — `ImageBitmap`은 GC 대상이 아니라
   * 빠뜨리면 수십 프레임 만에 메모리가 폭발한다.
   */
  function drawPreview(bitmap: ImageBitmap): void {
    try {
      const ctx = previewCtx;
      if (ctx === null) return;
      const canvas = ctx.canvas;
      if (canvas.width !== bitmap.width || canvas.height !== bitmap.height) {
        canvas.width = bitmap.width;
        canvas.height = bitmap.height;
      }
      ctx.drawImage(bitmap, 0, 0);
    } catch (err) {
      logger.warn("프리뷰 비트맵 렌더 실패", {
        reason: err instanceof Error ? err.message : String(err),
      });
    } finally {
      bitmap.close();
    }
  }

  return {
    mode: "worker",

    previewMode: () => previewMode,

    configure(options) {
      worker.postMessage({
        type: "configure",
        targetAspect: options.targetAspect,
        mirror: options.mirror,
      });
    },

    process(payload: FramePayload) {
      // 소유권을 Worker로 넘긴다(zero-copy). 넘긴 뒤 메인에서 이 프레임을 만지면 안 된다.
      worker.postMessage({ type: "frame", payload }, { transfer: [payload as Transferable] });
    },

    onProcessed(listener) {
      processedListeners.add(listener);
      return () => processedListeners.delete(listener);
    },

    requestStill(quality = STILL_JPEG_QUALITY) {
      const id = nextStillId++;
      return new Promise<Blob | null>((resolve) => {
        // 카메라가 멈춰 프레임이 오지 않으면 영구 대기가 된다 — 타임아웃으로 끊는다.
        const timer = setTimeout(() => {
          if (pendingStills.delete(id)) {
            logger.warn("스틸 캡처 타임아웃(프레임이 도착하지 않음)");
            resolve(null);
          }
        }, STILL_TIMEOUT_MS);

        pendingStills.set(id, (blob) => {
          clearTimeout(timer);
          resolve(blob);
        });
        worker.postMessage({ type: "requestStill", id, quality });
      });
    },

    /**
     * 이관 → 비트맵 순으로 시도한다. **둘 다 실패해도 `false`를 돌려주고 예외는 없다.**
     *
     * ⚠️ 이관은 캔버스당 **1회만** 가능하다. 두 번째 호출은 던지므로 그때는 비트맵 경로로 간다
     *    (React StrictMode 이중 마운트에서 실제로 발생한다).
     */
    bindPreview(canvas: HTMLCanvasElement): boolean {
      if (typeof canvas.transferControlToOffscreen === "function") {
        try {
          const offscreen = canvas.transferControlToOffscreen();
          worker.postMessage({ type: "bindPreview", canvas: offscreen }, { transfer: [offscreen] });
          previewCtx = null;
          previewMode = "transferred";
          return true;
        } catch (err) {
          logger.warn("프리뷰 캔버스 이관 실패 — 비트맵 전송으로 폴백", {
            reason: err instanceof Error ? err.message : String(err),
          });
        }
      }

      // 폴백: Worker가 프레임마다 비트맵을 보내고 여기서 그린다.
      const ctx = canvas.getContext("2d", { alpha: false });
      if (ctx === null) {
        logger.error("프리뷰 2D 컨텍스트를 얻지 못했다 — 프리뷰 없이 진행");
        previewMode = "none";
        return false;
      }
      previewCtx = ctx;
      previewMode = "bitmap";
      worker.postMessage({ type: "previewChannel", enabled: true });
      logger.info("프리뷰 비트맵 폴백 활성");
      return true;
    },

    configureSpool(options) {
      worker.postMessage({
        type: "configureSpool",
        enabled: options.enabled,
        intervalMs: options.intervalMs,
        quality: options.quality,
      });
    },

    onSpoolFrame(listener) {
      spoolListeners.add(listener);
      return () => spoolListeners.delete(listener);
    },

    terminate() {
      worker.postMessage({ type: "reset" });
      pendingStills.forEach((resolve) => resolve(null));
      pendingStills.clear();
      processedListeners.clear();
      // 스풀 구독을 남기면 Worker가 죽은 뒤에도 참조가 살아 있어 세션 Blob이 회수되지 않는다.
      spoolListeners.clear();
      previewCtx = null;
      previewMode = "none";
      worker.terminate();
    },
  };
}

/**
 * 가공기를 만든다. **Worker 경로를 쓸 수 없으면 메인 스레드 가공기로 내려간다**(04 §2.3.1).
 *
 * ⚠️ `new Worker(...)`가 던질 수 있다(CSP·module worker 미지원·번들 경로 오류). 그것을 여기서
 *    삼키지 않으면 `cameraService.start()`가 예외로 끝나 "예외를 던지지 않는다"는 계약(01 §2.1)이
 *    깨지고, 화면은 로딩에 고착된다.
 */
export function spawnFrameProcessor(): FrameProcessor {
  if (!isWorkerPipelineSupported()) {
    logger.warn("Worker 가공 경로 미지원 — 메인 스레드 가공으로 축소(저성능 모드)");
    return createMainThreadProcessor();
  }

  try {
    const worker = new Worker(new URL("./frameProcessor.worker.ts", import.meta.url), {
      type: "module",
      name: "mcphoto-frame-processor",
    });
    return createFrameProcessorClient(worker as unknown as WorkerLike);
  } catch (err) {
    logger.error("가공 Worker 생성 실패 — 메인 스레드 가공으로 축소", {
      reason: err instanceof Error ? err.message : String(err),
    });
    return createMainThreadProcessor();
  }
}
