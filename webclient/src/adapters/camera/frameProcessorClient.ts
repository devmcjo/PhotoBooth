import { logger } from "@adapters/storage/logStore";
import type { FrameProcessor, FramePayload, ProcessedSize, SpoolFrame } from "./cameraTypes";
import {
  STILL_JPEG_QUALITY,
  type FrameProcessorRequest,
  type FrameProcessorResponse,
} from "./frameProcessorProtocol";

/**
 * 가공 Worker RPC 클라이언트 — 04 §4
 *
 * ⚠️ `OffscreenCanvas` 2D가 없으면 §1의 Worker 구조가 성립하지 않는다.
 *    그때는 **메인 스레드 가공으로 축소**하고 진단에 "저성능 모드"로 표시한다(04 §2.3.1).
 *    이 판정은 `isWorkerPipelineSupported()`가 담당한다.
 */

export interface WorkerLike {
  postMessage(message: FrameProcessorRequest, options?: StructuredSerializeOptions): void;
  addEventListener(
    type: "message",
    listener: (event: MessageEvent<FrameProcessorResponse>) => void,
  ): void;
  terminate(): void;
}

/** Worker 가공 경로를 쓸 수 있는가(런타임 기능 감지 — UA 분기 금지). */
export function isWorkerPipelineSupported(): boolean {
  return typeof Worker !== "undefined" && typeof OffscreenCanvas !== "undefined";
}

const STILL_TIMEOUT_MS = 5000;

export function createFrameProcessorClient(worker: WorkerLike): FrameProcessor {
  const processedListeners = new Set<(size: ProcessedSize) => void>();
  const spoolListeners = new Set<(frame: SpoolFrame) => void>();
  const pendingStills = new Map<number, (blob: Blob | null) => void>();
  let nextStillId = 1;

  worker.addEventListener("message", (event) => {
    const response = event.data;
    if (response.type === "processed") {
      for (const listener of processedListeners) {
        listener({ width: response.width, height: response.height });
      }
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

  return {
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

    bindPreview(canvas) {
      worker.postMessage({ type: "bindPreview", canvas }, { transfer: [canvas] });
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
      worker.terminate();
    },
  };
}

/** 실제 Worker를 띄운다. */
export function spawnFrameProcessor(): FrameProcessor {
  const worker = new Worker(new URL("./frameProcessor.worker.ts", import.meta.url), {
    type: "module",
    name: "mcphoto-frame-processor",
  });
  return createFrameProcessorClient(worker as unknown as WorkerLike);
}
