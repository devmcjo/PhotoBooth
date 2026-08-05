import type { FilterKind } from "@domain/filters/filterParams";
import type { OutputFormat } from "@domain/settings/appSettings";
import type { Slot } from "@domain/frames/types";
import { logger } from "@adapters/storage/logStore";
import { composeCore, ComposeError } from "./composeCore";
import type { RgbaImage } from "./pixelBuffer";

/**
 * 브라우저 합성 어댑터 — 04 §5.2
 *
 * 픽셀 연산은 전부 `composeCore`(순수)가 한다. 여기서는 **이미지 디코딩·인코딩만** 다룬다.
 * 덕분에 골든 이미지 테스트가 node에서 같은 코드 경로를 검증할 수 있다.
 *
 * ⚠️ **서버 프레임 이미지는 CORS-clean하게 로드해야 한다**(WM2). 오염된 캔버스는
 *    `getImageData`·`convertToBlob`에서 **예외**가 난다 — 합성이 전면 실패한다.
 */

/** JPEG 품질 0.95 — OpenCV `imwrite` 기본값과 맞춘다(04 §5.2). */
export const OUTPUT_JPEG_QUALITY = 0.95;

export interface ComposeRequest {
  /** 프레임 이미지 URL(서버·OPFS·object URL). */
  readonly frameImageUrl: string;
  readonly slots: readonly Slot[];
  /** 컷 Blob(OPFS에서 읽은 JPEG). 슬롯 순서와 같은 순서. */
  readonly cuts: readonly Blob[];
  readonly filter: FilterKind;
  readonly format: OutputFormat;
}

/** `ImageBitmap`을 RGBA 버퍼로. Worker·메인 어느 쪽에서도 동작한다. */
async function toRgba(source: ImageBitmapSource): Promise<RgbaImage> {
  const bitmap = await createImageBitmap(source);
  try {
    const canvas = new OffscreenCanvas(bitmap.width, bitmap.height);
    const ctx = canvas.getContext("2d", { willReadFrequently: true });
    if (ctx === null) throw new ComposeError("2D 컨텍스트를 만들 수 없습니다.");
    ctx.drawImage(bitmap, 0, 0);
    const imageData = ctx.getImageData(0, 0, bitmap.width, bitmap.height);
    return { width: imageData.width, height: imageData.height, data: imageData.data };
  } finally {
    // ImageBitmap은 GC 대상이 아니다(WR8).
    bitmap.close();
  }
}

/**
 * 프레임 이미지를 **CORS-clean**하게 가져온다(WM2).
 * `fetch` + `createImageBitmap(blob)` 경로라 `crossOrigin` 속성 없이도 오염되지 않는다 —
 * 단 서버가 CORS 헤더를 주어야 한다(`firebasestorage`는 항상 `ACAO: *`).
 *
 * ⚠️ **원격(https)에만 CORS 규약을 적용한다.** Step 14부터 캐시된 프레임의 `imageUrl`은 OPFS 유래
 *    `blob:` object URL이고, 번들은 상대 경로다 — 둘 다 same-origin이라 `mode:"cors"`·
 *    `cache:"force-cache"`가 의미 없고 브라우저별 동작이 불확실하다.
 * ⚠️ https 분기에서 `mode: "cors"`를 없애면 WM2가 깨진다(정적 검사 FR-6이 이 문자열을 고정한다).
 */
async function loadFrameImage(url: string): Promise<RgbaImage> {
  if (url.length === 0) throw new ComposeError("프레임 이미지 URL이 비어 있습니다.");
  const remote = /^https?:/i.test(url);
  const response = await fetch(url, remote ? { mode: "cors", cache: "force-cache" } : {});
  if (!response.ok) {
    throw new ComposeError(`프레임 이미지를 불러올 수 없습니다(HTTP ${response.status}).`);
  }
  return toRgba(await response.blob());
}

export interface ComposeResult {
  readonly blob: Blob;
  readonly width: number;
  readonly height: number;
  readonly elapsedMs: number;
}

/** 합성 실행. 실패는 `ComposeError`로 던진다(상위가 오류 상태로 표현한다). */
export async function compose(request: ComposeRequest): Promise<ComposeResult> {
  const startedAt = performance.now();

  const frameImage = await loadFrameImage(request.frameImageUrl);
  const cuts: RgbaImage[] = [];
  for (const cut of request.cuts) {
    // 순차 처리 — 10컷을 동시에 디코딩하면 모바일에서 메모리가 터진다(WR8).
    cuts.push(await toRgba(cut));
  }

  const composed = composeCore({
    frameImage,
    slots: request.slots,
    cuts,
    filter: request.filter,
  });

  const canvas = new OffscreenCanvas(composed.width, composed.height);
  const ctx = canvas.getContext("2d");
  if (ctx === null) throw new ComposeError("출력 캔버스를 만들 수 없습니다.");
  ctx.putImageData(new ImageData(composed.data, composed.width, composed.height), 0, 0);

  const blob = await canvas.convertToBlob(
    request.format === "Png"
      ? { type: "image/png" }
      : { type: "image/jpeg", quality: OUTPUT_JPEG_QUALITY },
  );

  const elapsedMs = Math.round(performance.now() - startedAt);
  logger.info("합성 완료", {
    filter: request.filter,
    slots: request.slots.length,
    width: composed.width,
    height: composed.height,
    bytes: blob.size,
    elapsedMs,
  });

  return { blob, width: composed.width, height: composed.height, elapsedMs };
}
