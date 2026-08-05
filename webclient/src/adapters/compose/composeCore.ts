import { centerCrop } from "@domain/capture/centerCrop";
import { clampSlotToFrame } from "@domain/capture/slotPlacement";
import type { FilterKind } from "@domain/filters/filterParams";
import type { Slot } from "@domain/frames/types";
import { applyFilter } from "./filters/pixelFilters";
import { blitOver, cropImage, cloneImage, type RgbaImage } from "./pixelBuffer";
import { resizeArea } from "./resizeArea";

/**
 * 합성 핵심 — analysis/14 §5 · 04 §5.2 (**브라우저 API 없이** 동작한다)
 *
 * ```
 * 1 컷 수 == 슬롯 수 확인(M12)
 * 2 배경 = 프레임 이미지, 출력 = 프레임 원본 해상도
 * 3 슬롯을 index 오름차순 정렬
 * 4 슬롯마다: 합성용 클램프 → 필터 → 슬롯 비율로 중앙 크롭 → 슬롯 크기로 축소 → **덮어쓰기**
 * ```
 *
 * ⚠️ 클램프는 **합성용**(`capture/slotPlacement`)이다. 편집기용(`frames/slotLayout`)과 식이 다르다.
 * ⚠️ 알파 블렌딩이 아니라 **덮어쓰기**다. 프레임 PNG의 슬롯 영역은 비어 있어야 한다.
 * ⚠️ 프레임 이미지가 없으면 **명확히 실패**한다(빈 배경으로 조용히 진행 금지).
 */

export class ComposeError extends Error {
  constructor(message: string) {
    super(message);
    this.name = "ComposeError";
  }
}

export interface ComposeInput {
  readonly frameImage: RgbaImage;
  readonly slots: readonly Slot[];
  /** 슬롯 index 순서와 **같은 순서**로 정렬된 컷(선택 순서 = 슬롯 순서 — M12). */
  readonly cuts: readonly RgbaImage[];
  readonly filter: FilterKind;
}

export function composeCore(input: ComposeInput): RgbaImage {
  const { frameImage, filter } = input;

  if (frameImage.width <= 0 || frameImage.height <= 0) {
    throw new ComposeError("프레임 이미지가 없습니다.");
  }
  // 1. 개수 불일치는 오류다 — 조용히 일부만 채우면 손님이 빈 칸을 받는다.
  if (input.cuts.length !== input.slots.length) {
    throw new ComposeError(
      `컷 수(${input.cuts.length})와 슬롯 수(${input.slots.length})가 다릅니다.`,
    );
  }

  // 2. 배경 복사(원본 프레임 버퍼를 변형하지 않는다 — 필터 변경 시 재합성한다).
  const output = cloneImage(frameImage);

  // 3. 슬롯 index 오름차순. 입력 배열 순서에 의존하지 않는다.
  const ordered = input.slots
    .map((slot, position) => ({ slot, cut: input.cuts[position]! }))
    .sort((a, b) => a.slot.index - b.slot.index);

  for (const { slot, cut } of ordered) {
    const slotRect = clampSlotToFrame(slot, frameImage.width, frameImage.height);
    if (slotRect.width <= 0 || slotRect.height <= 0) continue;

    // 4. 필터 → 슬롯 비율 중앙 크롭 → 슬롯 크기 축소 → 덮어쓰기
    const filtered = applyFilter(cut, filter);
    const srcCrop = centerCrop(filtered.width, filtered.height, slotRect.width / slotRect.height);
    const cropped = cropImage(filtered, srcCrop.x, srcCrop.y, srcCrop.width, srcCrop.height);
    const scaled = resizeArea(cropped, slotRect.width, slotRect.height);
    blitOver(output, scaled, slotRect.x, slotRect.y);
  }

  return output;
}
