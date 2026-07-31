import { roundHalfToEven } from "../mathCompat";
import type { ImageSize } from "./types";

/**
 * 프레임 이미지 제한 — Windows `Frames/FrameImageValidator.cs` 이식 (03 §11.2 · analysis/13 §6.2)
 *
 * PNG/JPG/JPEG만 · 10MB 이하 · 장변 4000px 초과 시 축소 · 저장 포맷은 **항상 PNG**.
 * 실제 디코드·재인코딩은 `adapters/frames/frameImageLoader.ts`가 한다(여기는 순수 판정만).
 */

/** 10MB. Windows `FrameImageValidator.MaxBytes`와 같은 값이다. */
export const MAX_FRAME_IMAGE_BYTES = 10 * 1024 * 1024;

/** 장변 상한. 초과분은 비율을 유지한 채 축소한다. */
export const MAX_FRAME_IMAGE_LONG_SIDE = 4000;

export const SUPPORTED_FRAME_IMAGE_EXTENSIONS = [".png", ".jpg", ".jpeg"] as const;
export const SUPPORTED_FRAME_IMAGE_MIME_TYPES = ["image/png", "image/jpeg"] as const;

/** 용량 판정. 경계값(정확히 10MB)은 **허용**이다. */
export function isFrameImageSizeWithinLimit(byteLength: number): boolean {
  if (!Number.isFinite(byteLength) || byteLength < 0) return false;
  return byteLength <= MAX_FRAME_IMAGE_BYTES;
}

/** 장변 4000 초과 시 축소 배율(1 = 축소 불필요). */
export function frameImageResizeFactor(width: number, height: number): number {
  const longSide = Math.max(width, height);
  if (!Number.isFinite(longSide) || longSide <= MAX_FRAME_IMAGE_LONG_SIDE) return 1;
  return MAX_FRAME_IMAGE_LONG_SIDE / longSide;
}

/**
 * 축소 후 크기. 반올림은 `roundHalfToEven`이다 — C# `(int)Math.Round(x)`의 기본이
 * MidpointRounding.ToEven이라 Windows와 픽셀이 갈라지지 않게 맞춘다(04 §9).
 */
export function scaledFrameImageSize(width: number, height: number): ImageSize {
  const factor = frameImageResizeFactor(width, height);
  if (factor >= 1) return { width, height };
  return {
    width: Math.max(1, roundHalfToEven(width * factor)),
    height: Math.max(1, roundHalfToEven(height * factor)),
  };
}

/**
 * 지원 형식 판정.
 *
 * ⚠️ **MIME이 있으면 MIME이 우선**이고, 비어 있으면(일부 안드로이드 파일 선택기가 그렇다)
 *    파일명 확장자로 판정한다. Windows는 확장자만 보지만 웹은 `File.type`이 더 신뢰할 수 있다 —
 *    확장자만 보면 `.png`로 이름만 바꾼 GIF가 통과해 디코드 단계에서야 실패한다.
 */
export function isSupportedFrameImage(mimeType: string, fileName: string): boolean {
  const mime = typeof mimeType === "string" ? mimeType.trim().toLowerCase() : "";
  if (mime.length > 0) {
    return (SUPPORTED_FRAME_IMAGE_MIME_TYPES as readonly string[]).includes(mime);
  }
  const name = typeof fileName === "string" ? fileName.trim().toLowerCase() : "";
  return SUPPORTED_FRAME_IMAGE_EXTENSIONS.some((ext) => name.endsWith(ext));
}
