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
import {
  shouldSpoolFrame,
  TIMELAPSE_SPOOL_INTERVAL_MS,
} from "@domain/capture/timelapseSpool";
import { SPOOL_JPEG_QUALITY } from "./frameProcessorProtocol";
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

/**
 * 비트맵 프리뷰 채널(폴백). 캔버스 이관이 불가한 브라우저에서만 켜진다 — 04 §2.3.1.
 * ⚠️ `previewCanvas`와 **동시에 켜지지 않는다**(클라이언트가 하나만 고른다).
 */
let previewChannelEnabled = false;

/** 대기 중인 스틸 요청. 다음 가공 프레임에서 완성한다(04 §5.1 원자성). */
let pendingStill: { id: number; quality: number } | null = null;

/**
 * 타임랩스 스풀 채널 상태(04 §7.2) — 스틸과 **완전히 분리**돼 있다.
 * 초기값이 `-Infinity`인 이유: 0으로 두면 시계 원점 근처에서 첫 프레임을 먹는다(15 §4 함정 #4).
 */
let spoolEnabled = false;
let spoolIntervalMs = TIMELAPSE_SPOOL_INTERVAL_MS;
let spoolQuality = SPOOL_JPEG_QUALITY;
let lastSpoolAtMs = Number.NEGATIVE_INFINITY;

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
  self.postMessage(response, { transfer: (transfer ?? []) as Transferable[] });
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

    // ── 소비자 3: 타임랩스 스풀(≤15fps) ──
    // **스틸 분기 뒤**에 둔다. 컷 촬영이 항상 우선이고, 스풀 JPEG 인코딩이 컷을 지연시키면 안 된다.
    if (spoolEnabled) {
      const nowMs = performance.now();
      if (shouldSpoolFrame(lastSpoolAtMs, nowMs, spoolIntervalMs)) {
        // 시각을 **await 전에** 기록한다. 뒤에 기록하면 인코딩 소요만큼 간격이 밀려 fps가 떨어진다.
        lastSpoolAtMs = nowMs;
        try {
          const blob = await canvas.convertToBlob({
            type: "image/jpeg",
            quality: spoolQuality,
          });
          post({ type: "spoolFrame", blob, width: crop.width, height: crop.height });
        } catch {
          // 스풀 1장 실패는 촬영과 무관하다 — 조용히 넘기고 다음 프레임을 노린다.
          // (여기서 로그를 남겨도 Worker에는 로그 스토어가 붙지 않아 진단에 도달하지 않는다.)
        }
      }
    }

    // ── 소비자 4: 프리뷰 비트맵 채널(폴백 — 캔버스 이관이 불가한 브라우저) ──
    // ⚠️ **반드시 스틸·스풀 뒤**다. `transferToImageBitmap()`은 캔버스를 **비우므로**,
    //    앞에 두면 같은 프레임의 스틸·스풀이 빈 이미지가 된다. 다음 프레임에서 `drawImage`가
    //    다시 채우므로 비워진 상태 자체는 무해하다.
    if (previewChannelEnabled) {
      const bitmap = takePreviewBitmap(canvas);
      if (bitmap !== null) post({ type: "previewFrame", bitmap }, [bitmap]);
    }

    post({ type: "processed", width: crop.width, height: crop.height });
  } finally {
    payload.close();
  }
}

/**
 * 가공 캔버스 → 프리뷰 비트맵.
 *
 * `transferToImageBitmap()`이 있으면 그것을 쓴다(복사 없음). 없는 구현에서는 이 폴백 경로
 * 자체가 의미를 잃으므로 `null`을 돌려주고 조용히 넘긴다 — 프리뷰 1프레임 누락은 촬영과 무관하다.
 */
function takePreviewBitmap(source: OffscreenCanvas): ImageBitmap | null {
  const transfer = (source as { transferToImageBitmap?: () => ImageBitmap })
    .transferToImageBitmap;
  if (typeof transfer !== "function") return null;
  try {
    return transfer.call(source);
  } catch {
    return null;
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
      // 이관에 성공했으므로 비트맵 채널은 필요 없다(둘 다 켜면 이중 렌더 + 복사 비용).
      previewChannelEnabled = false;
      break;

    case "previewChannel":
      previewChannelEnabled = request.enabled;
      break;

    case "requestStill":
      // 이전 요청이 아직 안 끝났으면 덮어쓴다(가장 최근 요청만 유효).
      pendingStill = { id: request.id, quality: request.quality };
      break;

    case "configureSpool":
      spoolEnabled = request.enabled;
      spoolIntervalMs = request.intervalMs;
      spoolQuality = request.quality;
      // off로 내려갈 때 마지막 시각을 지운다 — 다음 세션의 첫 프레임이 간격 판정에 걸리지 않게.
      if (!request.enabled) lastSpoolAtMs = Number.NEGATIVE_INFINITY;
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
