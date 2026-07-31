/**
 * 슬롯 종횡비 선택 — Windows `Frames/SlotAspect.cs` 이식 (analysis/14 §1.1)
 * 캡처 중앙 크롭이 이 비율을 따른다.
 */

export const SLOT_ASPECTS = ["Ratio4x3", "Ratio3x4", "Ratio1x1"] as const;

export type SlotAspect = (typeof SLOT_ASPECTS)[number];

/** 기본값 3:4(세로). */
export const DEFAULT_SLOT_ASPECT: SlotAspect = "Ratio3x4";

/** 가로/세로 비율값(width / height). */
export function slotAspectToRatio(aspect: SlotAspect): number {
  switch (aspect) {
    case "Ratio4x3":
      return 4 / 3;
    case "Ratio3x4":
      return 3 / 4;
    case "Ratio1x1":
      return 1;
    default:
      return 3 / 4;
  }
}

/** 표시 라벨. */
export function slotAspectToLabel(aspect: SlotAspect): string {
  switch (aspect) {
    case "Ratio4x3":
      return "4:3";
    case "Ratio3x4":
      return "3:4";
    case "Ratio1x1":
      return "1:1";
    default:
      return "3:4";
  }
}
