import { clamp, roundHalfToEven } from "../mathCompat";
import type { CropRect } from "./cropRect";

/**
 * 슬롯 종횡비 중앙 크롭 ROI — Windows `Capture/CropCalculator.cs` 이식 (analysis/14 §3)
 *
 * 원본 종횡비와 목표 종횡비를 비교해 좌우 또는 상하를 중앙 기준으로 잘라낸다(왜곡 없음).
 *
 * 정수 연산(04 §9):
 *   `cropW = round(srcH * targetAspect)` → `roundHalfToEven`
 *   `x = (srcW - cropW) / 2`             → `Math.floor`
 */
export function centerCrop(srcWidth: number, srcHeight: number, targetAspect: number): CropRect {
  if (srcWidth <= 0 || srcHeight <= 0) {
    return { x: 0, y: 0, width: Math.max(0, srcWidth), height: Math.max(0, srcHeight) };
  }

  if (targetAspect <= 0) {
    return { x: 0, y: 0, width: srcWidth, height: srcHeight };
  }

  const srcAspect = srcWidth / srcHeight;

  let cropW: number;
  let cropH: number;
  if (srcAspect > targetAspect) {
    // 원본이 더 넓음 → 높이 유지, 폭 축소(좌우 잘라냄)
    cropH = srcHeight;
    cropW = roundHalfToEven(srcHeight * targetAspect);
  } else {
    // 원본이 더 좁음/길음 → 폭 유지, 높이 축소(상하 잘라냄)
    cropW = srcWidth;
    cropH = roundHalfToEven(srcWidth / targetAspect);
  }

  // 경계 보정(반올림 오차로 원본 초과 방지)
  cropW = clamp(cropW, 1, srcWidth);
  cropH = clamp(cropH, 1, srcHeight);

  return {
    x: Math.floor((srcWidth - cropW) / 2),
    y: Math.floor((srcHeight - cropH) / 2),
    width: cropW,
    height: cropH,
  };
}
