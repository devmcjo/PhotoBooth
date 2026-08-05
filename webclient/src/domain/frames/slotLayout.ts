import { clamp, roundHalfToEven } from "../mathCompat";
import type { Slot } from "./types";

/**
 * 슬롯 자동 배치·경계 제약·겹침 검사 — Windows `Frames/SlotLayout.cs` 이식 (analysis/14 §4.1~4.4)
 *
 * ⚠️ 이 모듈의 클램프는 **편집기용**이다(합성용은 `capture/slotPlacement.ts` — 식이 다르다).
 *    편집기: `x ∈ [0, frameW - w]` (슬롯 전체가 프레임 안)
 *    합성  : `x ∈ [0, frameW - 1]` 후 `w`를 남은 폭으로 클램프(이상 데이터 방어)
 *
 * 정수 연산(04 §9): margin·gap·cell·행 인덱스는 `Math.floor`, 비율 맞춤은 `roundHalfToEven`.
 */

export const MIN_SLOTS = 1;
export const MAX_SLOTS = 6;

/** 세로로 긴 프레임(1열 스트립) 판정 임계값. */
const VERTICAL_STRIP_ASPECT = 0.6;

/**
 * 슬롯 개수(1~6)에 따라 프레임 크기에 맞춰 자동 배치.
 * 세로 긴 프레임(aspect < 0.6)은 1열 스트립, 그 외는 격자.
 *
 * @param targetAspect 지정하면 각 셀 안에서 그 비율을 유지하는 최대 사각형을 셀 중앙에 배치한다.
 *                     `null`이면 셀 크기 그대로.
 */
export function autoArrange(
  slotCount: number,
  frameW: number,
  frameH: number,
  targetAspect: number | null = null,
): Slot[] {
  const count = clamp(slotCount, MIN_SLOTS, MAX_SLOTS);
  const frameAspect = frameW / frameH;
  const verticalStrip = frameAspect < VERTICAL_STRIP_ASPECT;

  let cols: number;
  let rows: number;
  if (verticalStrip) {
    cols = 1;
    rows = count;
  } else {
    // 격자: 4=2×2, 6=2×3(3열), 2=1×2 등
    cols = gridCols(count);
    rows = Math.ceil(count / cols);
  }

  const marginX = Math.max(20, Math.floor(frameW / 20));
  const marginY = Math.max(20, Math.floor(frameH / 20));
  const gapX = Math.max(12, Math.floor(frameW / 40));
  const gapY = Math.max(12, Math.floor(frameH / 40));

  const cellW = Math.floor((frameW - marginX * 2 - gapX * (cols - 1)) / cols);
  const cellH = Math.floor((frameH - marginY * 2 - gapY * (rows - 1)) / rows);

  const slots: Slot[] = [];
  for (let i = 0; i < count; i++) {
    const r = Math.floor(i / cols);
    const c = i % cols;
    const cellX = marginX + c * (cellW + gapX);
    const cellY = marginY + r * (cellH + gapY);

    const fit = fitInCell(cellW, cellH, targetAspect);
    slots.push({
      index: i,
      x: cellX + fit.offX,
      y: cellY + fit.offY,
      width: fit.w,
      height: fit.h,
    });
  }
  return slots;
}

function gridCols(slotCount: number): number {
  switch (slotCount) {
    case 1:
      return 1;
    case 2:
      return 2;
    case 3:
      return 3;
    case 4:
      return 2;
    case 5:
      return 3;
    case 6:
      return 3;
    default:
      return 2;
  }
}

interface CellFit {
  readonly w: number;
  readonly h: number;
  readonly offX: number;
  readonly offY: number;
}

/**
 * 셀 안에서 targetAspect(=w/h)를 유지하는 최대 사각형 + 중앙 정렬 오프셋.
 * targetAspect가 null·0 이하면 셀 크기 그대로(오프셋 0).
 */
function fitInCell(cellW: number, cellH: number, targetAspect: number | null): CellFit {
  if (targetAspect === null || targetAspect <= 0) {
    return { w: cellW, h: cellH, offX: 0, offY: 0 };
  }

  const cellAspect = cellW / cellH;
  let w: number;
  let h: number;
  if (cellAspect > targetAspect) {
    // 셀이 목표보다 가로로 넓음 → 높이를 셀에 맞추고 폭을 비율로
    h = cellH;
    w = roundHalfToEven(h * targetAspect);
  } else {
    // 셀이 목표보다 세로로 김 → 폭을 셀에 맞추고 높이를 비율로
    w = cellW;
    h = roundHalfToEven(w / targetAspect);
  }
  w = clamp(w, 1, cellW);
  h = clamp(h, 1, cellH);
  return {
    w,
    h,
    offX: Math.floor((cellW - w) / 2),
    offY: Math.floor((cellH - h) / 2),
  };
}

/**
 * 슬롯 폭을 기준으로 targetAspect를 유지하도록 높이를 재계산(비율 유지 리사이즈).
 * 경계·중앙 정렬은 호출측에서 `clampToFrame`으로 마무리한다.
 */
export function resizeKeepingAspect(slot: Slot, newWidth: number, targetAspect: number): Slot {
  const w = Math.max(1, newWidth);
  const h = targetAspect <= 0 ? slot.height : Math.max(1, roundHalfToEven(w / targetAspect));
  return { index: slot.index, x: slot.x, y: slot.y, width: w, height: h };
}

/**
 * 모든 슬롯을 동일 배율로 일괄 스케일(중심 유지·종횡비 유지·경계 클램프).
 *
 * ⚠️ 누적 오차 방지를 위해 항상 **기준(원본) 슬롯**에서 계산한다 — 호출측이 `baseSlots`를 보관해야 한다.
 * ⚠️ `cx`는 부동소수다(floor 금지). `newX`는 `roundHalfToEven` — .5가 흔히 발생하는 지점이다(04 §9).
 */
export function scaleSlots(
  baseSlots: readonly Slot[],
  factor: number,
  frameW: number,
  frameH: number,
): Slot[] {
  return baseSlots.map((s) => {
    const newW = Math.max(1, roundHalfToEven(s.width * factor));
    const newH = Math.max(1, roundHalfToEven(s.height * factor));
    const cx = s.x + s.width / 2;
    const cy = s.y + s.height / 2;
    return clampToFrame(
      {
        index: s.index,
        x: roundHalfToEven(cx - newW / 2),
        y: roundHalfToEven(cy - newH / 2),
        width: newW,
        height: newH,
      },
      frameW,
      frameH,
    );
  });
}

/**
 * 원본 이미지 크기 → 현재 프레임 크기 배율로 슬롯 값을 복사 보정한다.
 * Windows `FrameEditorViewModel.ApplyPickedFrame`(:396-415)의 인라인 계산을 순수 함수로 옮긴 것이다.
 *
 * ⚠️ `scaleSlots`와 **다르다**: 저쪽은 중심 유지 일괄 스케일(사용자 배율)이고 이쪽은 좌표계 환산이다.
 * ⚠️ 반올림은 `roundHalfToEven`이다 — C# `(int)Math.Round(x)`의 기본이 MidpointRounding.ToEven이라
 *    Windows와 픽셀이 갈라지지 않게 맞춘다(04 §9).
 */
export function rescaleSlots(
  slots: readonly Slot[],
  factor: number,
  frameW: number,
  frameH: number,
): Slot[] {
  return slots.map((s) =>
    clampToFrame(
      {
        index: s.index,
        x: roundHalfToEven(s.x * factor),
        y: roundHalfToEven(s.y * factor),
        width: Math.max(1, roundHalfToEven(s.width * factor)),
        height: Math.max(1, roundHalfToEven(s.height * factor)),
      },
      frameW,
      frameH,
    ),
  );
}

/** 슬롯을 프레임 경계 내로 클램프(편집기용 — 슬롯 **전체**가 프레임 안에 들어온다). */
export function clampToFrame(slot: Slot, frameW: number, frameH: number): Slot {
  const w = clamp(slot.width, 1, frameW);
  const h = clamp(slot.height, 1, frameH);
  return {
    index: slot.index,
    x: clamp(slot.x, 0, frameW - w),
    y: clamp(slot.y, 0, frameH - h),
    width: w,
    height: h,
  };
}

/** 두 슬롯이 겹치는가(경계 접촉은 겹침 아님). */
export function overlaps(a: Slot, b: Slot): boolean {
  return a.x < b.x + b.width && a.x + a.width > b.x && a.y < b.y + b.height && a.y + a.height > b.y;
}

/** 슬롯 목록에 겹침이 있는가. */
export function hasAnyOverlap(slots: readonly Slot[]): boolean {
  for (let i = 0; i < slots.length; i++) {
    for (let j = i + 1; j < slots.length; j++) {
      if (overlaps(slots[i]!, slots[j]!)) return true;
    }
  }
  return false;
}

/** 저장 가능 여부: 개수 1~6 + 모든 슬롯이 경계 내 + 겹침 없음. */
export function isValidLayout(slots: readonly Slot[], frameW: number, frameH: number): boolean {
  if (slots.length < MIN_SLOTS || slots.length > MAX_SLOTS) return false;
  for (const s of slots) {
    if (s.x < 0 || s.y < 0) return false;
    if (s.x + s.width > frameW || s.y + s.height > frameH) return false;
    if (s.width < 1 || s.height < 1) return false;
  }
  return !hasAnyOverlap(slots);
}
