import { centerCrop } from "@domain/capture/centerCrop";
import {
  shouldSpoolFrame,
  TIMELAPSE_SPOOL_INTERVAL_MS,
} from "@domain/capture/timelapseSpool";
import { logger } from "@adapters/storage/logStore";
import type {
  FramePayload,
  FrameProcessor,
  PreviewMode,
  ProcessedSize,
  SpoolFrame,
} from "./cameraTypes";
import { SPOOL_JPEG_QUALITY, STILL_JPEG_QUALITY } from "./frameProcessorProtocol";

/**
 * 메인 스레드 가공기 — **04 §2.3.1이 요구한 폴백의 실물**(2026-08-06 신설)
 *
 * `OffscreenCanvas` 2D 또는 `Worker`가 없는 브라우저에서 `frameProcessor.worker.ts`와 **동일한
 * 픽셀 규격**을 만족시킨다. 설계는 이 경로를 "저성능 모드"로 부르고 진단에 표시하라고 요구한다.
 *
 * ## 무엇이 같고 무엇이 다른가
 *
 * | 항목 | Worker 경로 | 이 경로 |
 * |------|-------------|---------|
 * | 거울 → 중앙 크롭 | 프레임당 1회 | **같다**(같은 `centerCrop` 순수 함수) |
 * | 세 소비자 공유 | ○ | **○** — WM1(프리뷰=저장)이 여기서도 성립한다 |
 * | 실행 위치 | Worker | 메인 스레드(가공이 UI 프레임을 잡아먹는다) |
 * | 인코딩 | `convertToBlob` | `HTMLCanvasElement.toBlob` |
 *
 * ⚠️ **CSS 반전을 쓰지 않는다.** Worker 경로와 같은 이유다 — 프리뷰만 뒤집히고 저장 픽셀은
 *    원본이 되어 WYSIWYG가 깨진다(WM1).
 * ⚠️ `VideoFrame`·`ImageBitmap`은 GC 대상이 아니다 → 모든 경로를 `try/finally`로 닫는다.
 * ⚠️ 캔버스 1개를 **재사용**한다(매 프레임 새로 만들면 GC 압력으로 fps가 떨어진다).
 */

export interface MainThreadProcessorDeps {
  /** 테스트 주입점. 기본은 `document.createElement("canvas")`. */
  readonly createCanvas?: () => HTMLCanvasElement;
  readonly now?: () => number;
}

function defaultCanvas(): HTMLCanvasElement {
  return document.createElement("canvas");
}

/** `toBlob`을 Promise로 감싼다. 실패는 `null`(예외 전파 금지 — 01 §2.1). */
function canvasToBlob(
  canvas: HTMLCanvasElement,
  type: string,
  quality: number,
): Promise<Blob | null> {
  return new Promise((resolve) => {
    try {
      canvas.toBlob((blob) => resolve(blob), type, quality);
    } catch (err) {
      logger.warn("메인 스레드 인코딩 실패", {
        reason: err instanceof Error ? err.message : String(err),
      });
      resolve(null);
    }
  });
}

export function createMainThreadProcessor(deps: MainThreadProcessorDeps = {}): FrameProcessor {
  const makeCanvas = deps.createCanvas ?? defaultCanvas;
  const now = deps.now ?? ((): number => performance.now());

  let targetAspect = 0;
  let mirror = false;

  /** 가공 결과 캔버스(재사용). 화면에 붙지 않는다 — 프리뷰는 별 캔버스에 복사한다. */
  let canvas: HTMLCanvasElement | null = null;
  let ctx: CanvasRenderingContext2D | null = null;

  let previewCtx: CanvasRenderingContext2D | null = null;
  let previewMode: PreviewMode = "none";

  let pendingStill: { quality: number; resolve: (blob: Blob | null) => void } | null = null;

  let spoolEnabled = false;
  let spoolIntervalMs = TIMELAPSE_SPOOL_INTERVAL_MS;
  let spoolQuality = SPOOL_JPEG_QUALITY;
  /** `0`이면 시계 원점 근처에서 첫 프레임을 먹는다(15 §4 함정 #4) → `-Infinity`. */
  let lastSpoolAtMs = Number.NEGATIVE_INFINITY;

  const processedListeners = new Set<(size: ProcessedSize) => void>();
  const spoolListeners = new Set<(frame: SpoolFrame) => void>();

  let terminated = false;
  /** 가공 중 도착한 프레임은 **최신 1장만** 남긴다(큐를 쌓지 않는다 — analysis/14 §2.2). */
  let busy = false;
  let queued: FramePayload | null = null;

  function ensureCanvas(width: number, height: number): CanvasRenderingContext2D | null {
    if (canvas === null) {
      canvas = makeCanvas();
      canvas.width = width;
      canvas.height = height;
      ctx = canvas.getContext("2d", { alpha: false });
    } else if (canvas.width !== width || canvas.height !== height) {
      // 크기가 변할 때만 재설정한다(매 프레임 설정하면 캔버스가 초기화되고 느리다).
      canvas.width = width;
      canvas.height = height;
    }
    return ctx;
  }

  async function processFrame(payload: FramePayload): Promise<void> {
    try {
      if (terminated) return;
      const source = payload as unknown as {
        displayWidth?: number;
        displayHeight?: number;
        width?: number;
        height?: number;
      };
      const srcWidth = source.displayWidth ?? source.width ?? 0;
      const srcHeight = source.displayHeight ?? source.height ?? 0;
      if (srcWidth <= 0 || srcHeight <= 0) return;

      // 도메인 순수 함수 — Worker 경로와 **같은 것**을 쓴다(픽셀이 갈라지지 않는 근거).
      const crop = centerCrop(srcWidth, srcHeight, targetAspect);
      const context = ensureCanvas(crop.width, crop.height);
      if (context === null || canvas === null) return;

      if (mirror) {
        context.setTransform(-1, 0, 0, 1, crop.width, 0);
      } else {
        context.setTransform(1, 0, 0, 1, 0, 0);
      }
      context.drawImage(
        payload as unknown as CanvasImageSource,
        crop.x,
        crop.y,
        crop.width,
        crop.height,
        0,
        0,
        crop.width,
        crop.height,
      );

      // ── 소비자 1: 프리뷰 ──
      if (previewCtx !== null) {
        const target = previewCtx.canvas;
        if (target.width !== crop.width || target.height !== crop.height) {
          target.width = crop.width;
          target.height = crop.height;
        }
        // 변환이 남아 있으면 프리뷰가 두 번 뒤집힌다 — 복사 전에 초기화한다.
        previewCtx.setTransform(1, 0, 0, 1, 0, 0);
        previewCtx.drawImage(canvas, 0, 0);
      }

      // ── 소비자 2: 스틸(대기 중일 때만) ──
      if (pendingStill !== null) {
        const request = pendingStill;
        pendingStill = null;
        const blob = await canvasToBlob(canvas, "image/jpeg", request.quality);
        request.resolve(blob);
      }

      // ── 소비자 3: 타임랩스 스풀 ──
      // **스틸 뒤**에 둔다. 컷 촬영이 항상 우선이고, 스풀 인코딩이 컷을 지연시키면 안 된다.
      if (spoolEnabled) {
        const nowMs = now();
        if (shouldSpoolFrame(lastSpoolAtMs, nowMs, spoolIntervalMs)) {
          // 시각을 **await 전에** 기록한다(뒤에 두면 인코딩 소요만큼 간격이 밀린다).
          lastSpoolAtMs = nowMs;
          const blob = await canvasToBlob(canvas, "image/jpeg", spoolQuality);
          if (blob !== null) {
            for (const listener of spoolListeners) {
              listener({ blob, width: crop.width, height: crop.height });
            }
          }
        }
      }

      for (const listener of processedListeners) {
        listener({ width: crop.width, height: crop.height });
      }
    } finally {
      payload.close();
    }
  }

  async function drain(): Promise<void> {
    if (busy) return;
    busy = true;
    try {
      while (queued !== null) {
        const next = queued;
        queued = null;
        await processFrame(next);
      }
    } finally {
      busy = false;
    }
  }

  return {
    mode: "main",

    previewMode: () => previewMode,

    configure(options) {
      targetAspect = options.targetAspect;
      mirror = options.mirror;
    },

    process(payload) {
      if (terminated) {
        payload.close();
        return;
      }
      // 이미 대기 중인 프레임이 있으면 **그것을 버리고** 최신으로 교체한다.
      if (queued !== null) queued.close();
      queued = payload;
      void drain();
    },

    onProcessed(listener) {
      processedListeners.add(listener);
      return () => processedListeners.delete(listener);
    },

    requestStill(quality = STILL_JPEG_QUALITY) {
      if (terminated) return Promise.resolve(null);
      return new Promise<Blob | null>((resolve) => {
        // 이전 요청이 아직 안 끝났으면 덮어쓴다(가장 최근 요청만 유효 — Worker 경로와 동일).
        pendingStill?.resolve(null);
        pendingStill = { quality, resolve };
      });
    },

    /**
     * 화면 캔버스에 **직접** 그린다. 이관이 필요 없으므로 이 경로에는 실패 모드가 사실상 없다
     * (2D 컨텍스트를 못 얻는 경우만).
     */
    bindPreview(target: HTMLCanvasElement): boolean {
      const context = target.getContext("2d", { alpha: false });
      if (context === null) {
        logger.error("프리뷰 2D 컨텍스트를 얻지 못했다 — 프리뷰 없이 진행");
        previewMode = "none";
        return false;
      }
      previewCtx = context;
      previewMode = "direct";
      return true;
    },

    configureSpool(options) {
      spoolEnabled = options.enabled;
      spoolIntervalMs = options.intervalMs;
      spoolQuality = options.quality;
      // off로 내려갈 때 마지막 시각을 지운다 — 다음 세션의 첫 프레임이 간격 판정에 걸리지 않게.
      if (!options.enabled) lastSpoolAtMs = Number.NEGATIVE_INFINITY;
    },

    onSpoolFrame(listener) {
      spoolListeners.add(listener);
      return () => spoolListeners.delete(listener);
    },

    terminate() {
      terminated = true;
      if (queued !== null) {
        queued.close();
        queued = null;
      }
      // 대기 중인 스틸을 매달아 두면 호출측이 5초 타임아웃까지 기다린다 — 지금 끊는다.
      pendingStill?.resolve(null);
      pendingStill = null;
      processedListeners.clear();
      spoolListeners.clear();
      previewCtx = null;
      previewMode = "none";
      canvas = null;
      ctx = null;
    },
  };
}
