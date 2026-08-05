import { clamp } from "../mathCompat";
import type { Slot } from "../frames/types";
import { centerCrop } from "./centerCrop";
import type { CropRect } from "./cropRect";

/**
 * 합성 시 슬롯 배치 기하 — Windows `Capture/SlotPlacement.cs` 이식 (analysis/14 §5.1·§5.2)
 *
 * ⚠️ **편집기용 `frames/slotLayout.ts`의 `clampToFrame`과 식이 다르다.**
 *    편집기: 슬롯 전체가 프레임 안(`x ≤ frameW - w`) — 사용자가 움직이는 대상이라 크기를 지킨다.
 *    합성  : 좌표를 먼저 프레임 안으로 넣고(`x ≤ frameW - 1`) 폭을 남은 공간으로 줄인다 —
 *            이상 데이터(경계 밖 슬롯)가 들어와도 합성이 죽지 않게 하는 방어다.
 *    두 식을 하나로 합치면 편집기 WYSIWYG 또는 합성 방어 중 하나가 깨진다.
 */

/**
 * 소스 이미지를 슬롯 종횡비에 맞춰 cover(중앙 크롭)할 소스 Rect.
 * 이 Rect를 슬롯 픽셀 영역에 uniform 스케일로 채우면 왜곡 없이 슬롯을 덮는다.
 */
export function sourceCropForSlot(
  srcW: number,
  srcH: number,
  slotW: number,
  slotH: number,
): CropRect {
  if (srcW <= 0 || srcH <= 0 || slotW <= 0 || slotH <= 0) {
    return { x: 0, y: 0, width: Math.max(0, srcW), height: Math.max(0, srcH) };
  }
  return centerCrop(srcW, srcH, slotW / slotH);
}

/** 슬롯을 프레임 이미지 경계 내로 클램프한 **목적지** Rect(합성용 방어). */
export function clampSlotToFrame(slot: Slot, frameW: number, frameH: number): CropRect {
  const x = clamp(slot.x, 0, Math.max(0, frameW - 1));
  const y = clamp(slot.y, 0, Math.max(0, frameH - 1));
  return {
    x,
    y,
    width: clamp(slot.width, 1, frameW - x),
    height: clamp(slot.height, 1, frameH - y),
  };
}
