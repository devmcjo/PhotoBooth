import type { Slot } from "./types";

/**
 * 코드 생성 fallback 프레임의 기하 — Windows `Frames/DefaultFrameProvider.cs` 이식 (analysis/14 §4.7)
 *
 * 이미지(하양 배경 PNG) 생성은 어댑터 책임이고, 여기서는 **좌표만** 계산한다.
 * 서버·번들 프레임이 하나도 없어도 촬영이 가능해야 한다(오프라인 최종 폴백).
 */

export const FALLBACK_FRAME_ID = "fallback";
export const FALLBACK_FRAME_NAME = "기본 프레임";
export const FALLBACK_WIDTH = 1200;
export const FALLBACK_HEIGHT = 1600; // 3:4
export const FALLBACK_SLOT_COUNT = 4;

const MARGIN = 80;
const GAP = 60;

/**
 * 2×2 격자, 슬롯 종횡비 3:4, 세로 중앙 정렬.
 *
 * 정수 연산(04 §9): `cellW = floor((1200 - 160 - 60) / 2) = 490`,
 * `cellH = trunc(cellW * 4 / 3) = 653`(C#의 `(int)` 캐스트는 **절단**이다 — 반올림이 아니다),
 * `top = floor((1600 - (cellH*2 + gap)) / 2)`.
 */
export function fallbackFrameSlots(): Slot[] {
  const cellW = Math.floor((FALLBACK_WIDTH - MARGIN * 2 - GAP) / 2);
  const cellH = Math.trunc((cellW * 4) / 3);
  const totalH = cellH * 2 + GAP;
  const top = Math.floor((FALLBACK_HEIGHT - totalH) / 2);
  const right = MARGIN + cellW + GAP;
  const bottom = top + cellH + GAP;

  const origins: readonly [number, number][] = [
    [MARGIN, top],
    [right, top],
    [MARGIN, bottom],
    [right, bottom],
  ];

  return origins.map(([x, y], index) => ({ index, x, y, width: cellW, height: cellH }));
}
