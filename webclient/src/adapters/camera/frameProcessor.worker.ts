/// <reference lib="webworker" />
/**
 * 프레임 가공 Worker — 04 §4 (프레임당 **1회** 거울 + 중앙 크롭)
 *
 * ⚠️ **CSS `transform: scaleX(-1)`로 반전하면 안 된다.** 프리뷰만 뒤집히고 저장 픽셀은
 *    원본이 되어 손님이 본 구도와 결과가 좌우 반대가 된다(WM1 · analysis/14 §2.4 위반).
 *    반전은 여기 canvas 변환에서 하고, 프리뷰·스틸·타임랩스가 **그 결과를 공유**한다.
 *
 * ⚠️ `VideoFrame`·`ImageBitmap`은 **GC 대상이 아니다.** `close()`를 빠뜨리면 수십 프레임
 *    만에 메모리가 폭발한다 → 모든 경로를 `try/finally`로 감싼다.
 *
 * ⚠️ `OffscreenCanvas` 1개를 **재사용**한다(매 프레임 새로 만들면 GC 압력으로 fps가 떨어진다).
 */
import { centerCrop } from "@domain/capture/centerCrop";
import type {
  FrameProcessorRequest,
  FrameProcessorResponse,
} from "./frameProcessorProtocol";

let targetAspect = 0;
let mirror = false;

/** 가공 결과를 담는 캔버스(재사용). */
let canvas: OffscreenCanvas | null = null;
let ctx: OffscreenCanvasRenderingContext2D | null = null;

/** 프리뷰 전용 캔버스(메인에서 제어권을 넘겨받은 것). 있으면 zero-copy로 그린다. */
let previewCanvas: OffscreenCanvas | null = null;
let previewCtx: OffscreenCanvasRenderingContext2D | null = null;

/** 대기 중인 스틸 요청. 다음 가공 프레임에서 완성한다(04 §5.1 원자성). */
let pendingStill: { id: number; quality: number } | null = null;

function ensureCanvas(width: number, height: number): OffscreenCanvasRenderingContext2D | null {
  if (canvas === null) {
    canvas = new OffscreenCanvas(width, height);
    ctx = canvas.getContext("2d", { alpha: false });
  } else if (canvas.width !== width || canvas.height !== height) {
    // 크기가 변할 때만 재설정한다(매 프레임 설정하면 캔버스가 초기화되고 느리다).
    canvas.width = width;
    canvas.height = height;
  }
  return ctx;
}

function post(response: FrameProcessorResponse, transfer?: Transferable[]): void {
  self.postMessage(response, { transfer: transfer ?? [] });
}

async function processFrame(payload: ImageBitmap | VideoFrame): Promise<void> {
  // 어떤 경로로 나가도 프레임을 닫는다.
  try {
    const srcWidth = "displayWidth" in payload ? payload.displayWidth : payload.width;
    const srcHeight = "displayHeight" in payload ? payload.displayHeight : payload.height;
    if (srcWidth <= 0 || srcHeight <= 0) return;

    // 도메인 순수 함수 — 정수 나눗셈·은행가 반올림까지 Windows와 동일하다(04 §9).
    const crop = centerCrop(srcWidth, srcHeight, targetAspect);
    const context = ensureCanvas(crop.width, crop.height);
    if (context === null || canvas === null) return;

    if (mirror) {
      // 좌우 반전: x축을 뒤집고 원점을 오른쪽으로 옮긴다.
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
    if (previewCanvas !== null) {
      if (previewCanvas.width !== crop.width || previewCanvas.height !== crop.height) {
        previewCanvas.width = crop.width;
        previewCanvas.height = crop.height;
        previewCtx = previewCanvas.getContext("2d", { alpha: false });
      }
      previewCtx ??= previewCanvas.getContext("2d", { alpha: false });
      previewCtx?.drawImage(canvas, 0, 0);
    }

    // ── 소비자 2: 스틸(대기 중일 때만) ──
    if (pendingStill !== null) {
      const request = pendingStill;
      pendingStill = null;
      try {
        const blob = await canvas.convertToBlob({
          type: "image/jpeg",
          quality: request.quality,
        });
        post({ type: "still", id: request.id, blob });
      } catch (err) {
        post({
          type: "still",
          id: request.id,
          blob: null,
          error: err instanceof Error ? err.message : String(err),
        });
      }
    }

    // ── 소비자 3: 타임랩스 샘플러는 Step 9가 이 통지에 붙는다 ──
    post({ type: "processed", width: crop.width, height: crop.height });
  } finally {
    payload.close();
  }
}

/** 가공 중이면 최신 프레임만 남기고 큐를 쌓지 않는다(analysis/14 §2.2). */
let busy = false;
let queued: ImageBitmap | VideoFrame | null = null;

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

self.addEventListener("message", (event: MessageEvent<FrameProcessorRequest>) => {
  const request = event.data;
  switch (request.type) {
    case "configure":
      targetAspect = request.targetAspect;
      mirror = request.mirror;
      break;

    case "frame": {
      // 이미 대기 중인 프레임이 있으면 **그것을 버리고** 최신으로 교체한다.
      if (queued !== null) queued.close();
      queued = request.payload;
      void drain();
      break;
    }

    case "bindPreview":
      previewCanvas = request.canvas;
      previewCtx = null;
      break;

    case "requestStill":
      // 이전 요청이 아직 안 끝났으면 덮어쓴다(가장 최근 요청만 유효).
      pendingStill = { id: request.id, quality: request.quality };
      break;

    case "reset":
      if (queued !== null) {
        queued.close();
        queued = null;
      }
      pendingStill = null;
      break;

    default: {
      const never: never = request;
      throw new Error(`알 수 없는 가공 요청: ${JSON.stringify(never)}`);
    }
  }
});
