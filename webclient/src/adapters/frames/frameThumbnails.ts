import { logger } from "@adapters/storage/logStore";

/**
 * 프레임 목록 썸네일 — 04 §5.2 · 03 §4(웹 차이)
 *
 * 원본(1200×1600)을 여러 장 그대로 들고 있으면 모바일 메모리를 태운다. `createImageBitmap`의
 * resize 옵션으로 줄이되 — ⚠️ **그 옵션은 미지원 시 예외가 아니라 조용히 무시된다.** 결과 `width`를
 * 확인하지 않으면 "줄인 줄 알았는데 원본"이 되어 절감이 사라진다. 1회 확인 후 판정을 캐시한다.
 *
 * ⚠️ 어댑터 규약: 실패·미지원은 `null`이다(카드가 이름만 보여준다). 예외를 던지지 않는다.
 * ⚠️ 중간 `ImageBitmap`은 반드시 `close()` 한다 — GC 대상이 아니다(WR8).
 */

export const FRAME_THUMB_WIDTH = 240;

/** `null` = 아직 확인 전. `true`/`false` = 확인된 resize 옵션 실효 여부. */
let resizeSupported: boolean | null = null;

/** 테스트용 프로브 리셋. 운영 경로에서는 부르지 않는다. */
export function resetThumbnailProbeForTests(): void {
  resizeSupported = null;
}

/** 진단·테스트용 현재 프로브 판정. */
export function thumbnailResizeSupported(): boolean | null {
  return resizeSupported;
}

/**
 * resize 옵션 경로. 옵션이 실효하지 않으면 그 비트맵을 닫고 `null`을 돌려준다(판정도 캐시).
 *
 * `resizeHeight`를 주지 않는 이유: 명세상 한쪽만 지정하면 **종횡비를 유지해** 나머지를 계산한다.
 * 원본 크기를 모르는 상태에서 두 값을 다 주면 프레임이 찌그러진다.
 */
async function tryResizePath(blob: Blob, targetWidth: number): Promise<ImageBitmap | null> {
  if (resizeSupported === false) return null;

  const bitmap = await createImageBitmap(blob, {
    resizeWidth: targetWidth,
    resizeQuality: "high",
  });

  if (bitmap.width === targetWidth) {
    resizeSupported = true;
    return bitmap;
  }

  // 옵션이 무시됐다 — 원본 크기 비트맵이 왔다. 닫고 폴백으로 간다(판정은 1회만 한다).
  resizeSupported = false;
  bitmap.close();
  logger.info("createImageBitmap resize 옵션 미실효 — 캔버스 축소 폴백으로 전환", {
    requested: targetWidth,
    got: bitmap.width,
  });
  return null;
}

/** 전체 디코드 → OffscreenCanvas 축소. 중간 비트맵을 반드시 닫는다. */
async function canvasFallback(blob: Blob, targetWidth: number): Promise<ImageBitmap | null> {
  const full = await createImageBitmap(blob);
  try {
    if (full.width === 0 || full.height === 0) return null;
    const height = Math.max(1, Math.round((full.height * targetWidth) / full.width));
    const canvas = new OffscreenCanvas(targetWidth, height);
    const ctx = canvas.getContext("2d");
    if (ctx === null) return null;
    ctx.imageSmoothingEnabled = true;
    ctx.imageSmoothingQuality = "high";
    ctx.drawImage(full, 0, 0, targetWidth, height);
    return canvas.transferToImageBitmap();
  } finally {
    full.close();
  }
}

/**
 * 썸네일 생성. 실패·미지원은 `null`이다.
 * 호출자(카드 컴포넌트)가 **cleanup에서 `close()`** 해야 한다 — 전역 캐시를 두지 않는 이유다.
 */
export async function createFrameThumbnail(
  blob: Blob,
  targetWidth: number = FRAME_THUMB_WIDTH,
): Promise<ImageBitmap | null> {
  try {
    const resized = await tryResizePath(blob, targetWidth);
    if (resized !== null) return resized;
    return await canvasFallback(blob, targetWidth);
  } catch (err) {
    logger.warn("프레임 썸네일 생성 실패", {
      reason: err instanceof Error ? err.message : String(err),
    });
    return null;
  }
}
