import {
  isFrameImageSizeWithinLimit,
  isSupportedFrameImage,
  scaledFrameImageSize,
} from "@domain/frames/frameImagePolicy";
import { logger } from "@adapters/storage/logStore";

/**
 * 프레임 이미지 로드·재인코딩 — Windows `FrameEditorViewModel.LoadImage`의 웹 대응 (03 §11.2·§11.7)
 *
 * 두 진입점(`<input type="file">` · 피커의 앱 내부 URL)이 **같은 코어**를 지나 항상 PNG Blob과
 * 실제 픽셀 크기를 돌려준다. 판정(용량·장변·형식)은 전부 `domain/frames/frameImagePolicy.ts`다.
 *
 * ⚠️ 어댑터 규약: **예외를 전파하지 않는다.** 모든 실패가 `FrameImageOutcome` 판별 유니온이다.
 * ⚠️ **[선택 편집] 진입에서는 이 모듈을 쓰지 않는다.** 재인코딩 경로는 장변 4000 축소를 적용하므로
 *    저장된 `frame.slots`의 좌표계와 어긋나 기존 슬롯이 전부 밀린다(설계 §9.3).
 * ⚠️ OPFS에 아무것도 쓰지 않는다 — 임시 파일 금지(03 §11.7). 디스크 쓰기는 저장 1회뿐이다.
 */

export interface LoadedFrameImage {
  /** **항상 PNG**다(축소가 없어도 재인코딩한다 — 저장 포맷 규격). */
  readonly blob: Blob;
  readonly width: number;
  readonly height: number;
}

export type FrameImageFailure =
  | "unsupported-type"
  | "too-large"
  | "decode-failed"
  | "encode-failed"
  | "fetch-failed";

export type FrameImageOutcome =
  | { readonly ok: true; readonly image: LoadedFrameImage }
  | { readonly ok: false; readonly failure: FrameImageFailure };

function describe(err: unknown): string {
  return err instanceof Error ? err.message : String(err);
}

/**
 * PNG 인코딩. `OffscreenCanvas.convertToBlob`을 먼저 시도하고 없거나 던지면
 * `HTMLCanvasElement.toBlob`으로 폴백한다(A15-1 — 대상 브라우저 실측은 V24-1).
 *
 * ⚠️ TS DOM lib은 `OffscreenCanvas`를 항상 있는 것처럼 선언한다 — **런타임 감지**가 필요하다
 *    (15 §4 함정 2와 같은 성질).
 */
async function toPngBlob(bitmap: ImageBitmap, width: number, height: number): Promise<Blob | null> {
  if (typeof OffscreenCanvas !== "undefined") {
    try {
      const canvas = new OffscreenCanvas(width, height);
      const ctx = canvas.getContext("2d");
      if (ctx !== null) {
        ctx.imageSmoothingEnabled = true;
        ctx.imageSmoothingQuality = "high";
        ctx.drawImage(bitmap, 0, 0, width, height);
        return await canvas.convertToBlob({ type: "image/png" });
      }
    } catch (err) {
      logger.warn("OffscreenCanvas convertToBlob 실패 — HTMLCanvasElement 폴백", {
        reason: describe(err),
      });
    }
  }

  if (typeof document === "undefined") return null;
  try {
    const canvas = document.createElement("canvas");
    canvas.width = width;
    canvas.height = height;
    const ctx = canvas.getContext("2d");
    if (ctx === null) return null;
    ctx.imageSmoothingEnabled = true;
    ctx.imageSmoothingQuality = "high";
    ctx.drawImage(bitmap, 0, 0, width, height);
    return await new Promise<Blob | null>((resolve) => {
      canvas.toBlob((blob) => resolve(blob), "image/png");
    });
  } catch (err) {
    logger.warn("PNG 재인코딩 실패", { reason: describe(err) });
    return null;
  }
}

/**
 * 디코드 → 축소 → PNG 재인코딩. 두 진입점이 공유하는 코어다.
 *
 * `imageOrientation: "from-image"`가 **필수**다 — 빼면 EXIF 회전이 붙은 아이폰 세로 사진이
 * 옆으로 누운 채 저장된다(A15-2 · 실측 V24-2).
 */
async function decodeAndReencode(blob: Blob): Promise<FrameImageOutcome> {
  let bitmap: ImageBitmap;
  try {
    bitmap = await createImageBitmap(blob, { imageOrientation: "from-image" });
  } catch (err) {
    logger.warn("프레임 이미지 디코드 실패", { reason: describe(err) });
    return { ok: false, failure: "decode-failed" };
  }

  try {
    if (bitmap.width <= 0 || bitmap.height <= 0) {
      return { ok: false, failure: "decode-failed" };
    }
    const target = scaledFrameImageSize(bitmap.width, bitmap.height);
    const png = await toPngBlob(bitmap, target.width, target.height);
    if (png === null || png.size === 0) return { ok: false, failure: "encode-failed" };
    return { ok: true, image: { blob: png, width: target.width, height: target.height } };
  } finally {
    // ⚠️ ImageBitmap은 GC 대상이 아니다(WR8) — 어떤 경로로 빠져나가도 닫는다.
    bitmap.close();
  }
}

/** `<input type="file">` 경로. 형식·용량을 먼저 본다(디코드 비용을 아끼고 사유를 정확히 준다). */
export async function loadFrameImageFromFile(file: File): Promise<FrameImageOutcome> {
  if (!isSupportedFrameImage(file.type, file.name)) {
    return { ok: false, failure: "unsupported-type" };
  }
  if (!isFrameImageSizeWithinLimit(file.size)) {
    return { ok: false, failure: "too-large" };
  }
  return decodeAndReencode(file);
}

/**
 * 피커 경로(앱 내부 URL). 원본을 **읽기만** 한다.
 *
 * ⚠️ `compositor.loadFrameImage`와 **같은 분기**를 복제한다(WM2·FR-15): 원격 URL은
 *    `{mode:"cors", cache:"force-cache"}`로 받아야 canvas가 오염되지 않는다. 지금 피커 후보에
 *    원격 URL이 들어올 경로는 없지만(카탈로그가 same-origin만 돌려준다) 규약을 복제해 두면
 *    나중에 생겨도 `convertToBlob`이 SecurityError로 죽지 않는다.
 * ⚠️ 형식·용량 선검사를 하지 않는다 — 앱이 이미 보유한 자산이고 확장자·`Content-Length`가
 *    신뢰 가능한 값이 아니다. 실제 한도는 재인코딩 결과(장변 4000 축소)가 강제한다.
 */
export async function loadFrameImageFromUrl(url: string): Promise<FrameImageOutcome> {
  if (url.trim().length === 0) return { ok: false, failure: "fetch-failed" };
  let blob: Blob;
  try {
    const remote = /^https?:/i.test(url);
    const response = await fetch(url, remote ? { mode: "cors", cache: "force-cache" } : {});
    if (!response.ok) {
      logger.warn("프레임 원본 이미지를 가져오지 못했습니다", { status: response.status });
      return { ok: false, failure: "fetch-failed" };
    }
    blob = await response.blob();
  } catch (err) {
    logger.warn("프레임 원본 이미지 fetch 실패", { reason: describe(err) });
    return { ok: false, failure: "fetch-failed" };
  }
  if (blob.size === 0) return { ok: false, failure: "fetch-failed" };
  return decodeAndReencode(blob);
}

/**
 * [선택 편집] 진입 전용 — 원본 바이트를 **그대로** 읽는다(설계 §9.3).
 *
 * ⚠️ **`loadFrameImageFromUrl`을 쓰면 안 된다.** 그쪽은 장변 4000 축소 + PNG 재인코딩을 하므로
 *    저장된 `frame.slots`의 좌표계와 이미지 크기가 어긋나 **기존 슬롯이 전부 밀린다.**
 *    Windows `LoadForEdit`도 `LoadImage`를 경유하지 않고 파일을 그대로 읽는다 — 같은 이유다.
 */
export async function fetchFrameImageBytes(url: string): Promise<Blob | null> {
  if (url.trim().length === 0) return null;
  try {
    const remote = /^https?:/i.test(url);
    const response = await fetch(url, remote ? { mode: "cors", cache: "force-cache" } : {});
    if (!response.ok) {
      logger.warn("편집 대상 프레임 이미지를 가져오지 못했습니다", { status: response.status });
      return null;
    }
    const blob = await response.blob();
    return blob.size > 0 ? blob : null;
  } catch (err) {
    logger.warn("편집 대상 프레임 이미지 fetch 실패", { reason: describe(err) });
    return null;
  }
}

/** 메타에 이미지 크기가 없을 때만 쓰는 디코드 프로브. 실패는 `null`. */
export async function probeFrameImageSize(
  blob: Blob,
): Promise<{ readonly width: number; readonly height: number } | null> {
  let bitmap: ImageBitmap;
  try {
    bitmap = await createImageBitmap(blob, { imageOrientation: "from-image" });
  } catch (err) {
    logger.warn("프레임 이미지 크기 확인 실패", { reason: describe(err) });
    return null;
  }
  try {
    if (bitmap.width <= 0 || bitmap.height <= 0) return null;
    return { width: bitmap.width, height: bitmap.height };
  } finally {
    bitmap.close(); // WR8 — GC 대상이 아니다.
  }
}
