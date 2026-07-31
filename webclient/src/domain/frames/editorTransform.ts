/**
 * 편집기 좌표 변환 — Windows `Frames/EditorTransform.cs` 이식 (analysis/14 §4.5)
 *
 * Uniform 스케일 + 중앙 레터박스 정렬. **표시·드래그·클램프가 모두 이 변환을 공유해야** WYSIWYG가 성립한다
 * (Windows B3 버그의 원인: 세 경로가 각자 좌표를 계산했다).
 *
 * - 프레임 좌표(F): 슬롯 x/y/width/height·저장·클램프·캡처 크롭 기준(진실의 좌표)
 * - 캔버스 좌표(C): 화면에 슬롯 사각형을 그리는 좌표
 */

export interface EditorTransform {
  /** 프레임→캔버스 배율(uniform). 무효 변환이면 0. */
  readonly scale: number;
  /** 이미지 표시 영역 좌상단(캔버스 좌표, 중앙 레터박스 여백). */
  readonly originX: number;
  readonly originY: number;
  /** 화면에 그려지는 이미지 크기(레터박스 여백 제외). */
  readonly displayWidth: number;
  readonly displayHeight: number;
}

const INVALID: EditorTransform = {
  scale: 0,
  originX: 0,
  originY: 0,
  displayWidth: 0,
  displayHeight: 0,
};

/** 변환 계산. 캔버스·프레임 크기가 0 이하면 `scale=0`인 무효 변환을 반환한다. */
export function computeEditorTransform(
  canvasW: number,
  canvasH: number,
  frameW: number,
  frameH: number,
): EditorTransform {
  if (canvasW <= 0 || canvasH <= 0 || frameW <= 0 || frameH <= 0) return INVALID;

  // 부동소수 그대로 유지한다(04 §9 — floor 금지).
  const scale = Math.min(canvasW / frameW, canvasH / frameH);
  const displayWidth = frameW * scale;
  const displayHeight = frameH * scale;
  return {
    scale,
    originX: (canvasW - displayWidth) / 2,
    originY: (canvasH - displayHeight) / 2,
    displayWidth,
    displayHeight,
  };
}

/** 변환이 유효한가(그리기·이동 가능). */
export function isValidTransform(t: EditorTransform): boolean {
  return t.scale > 0;
}

export interface Point {
  readonly x: number;
  readonly y: number;
}

/** 프레임 좌표 → 캔버스 좌표. */
export function frameToCanvas(t: EditorTransform, fx: number, fy: number): Point {
  return { x: t.originX + fx * t.scale, y: t.originY + fy * t.scale };
}

/** 캔버스 좌표 → 프레임 좌표. `scale`이 0이면 (0,0). */
export function canvasToFrame(t: EditorTransform, cx: number, cy: number): Point {
  if (t.scale <= 0) return { x: 0, y: 0 };
  return { x: (cx - t.originX) / t.scale, y: (cy - t.originY) / t.scale };
}
