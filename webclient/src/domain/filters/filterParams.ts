/**
 * 필터 파라미터 — Windows `MCPhoto.Capture/Filters.cs` 이식 (analysis/14 §6)
 *
 * ⚠️ **CSS `filter`·canvas `ctx.filter`로 흑백을 만들면 안 된다.** 브라우저의 `grayscale()`은
 *    Rec.709 선형 계수를 쓰고 OpenCV `BGR2GRAY`는 **BT.601**을 쓴다 — 계수가 달라 골든 이미지가 깨진다.
 *    아래 계수로 **직접 계산**한다(04 §6.1).
 */

export const FILTER_KINDS = ["None", "Grayscale", "Brightness", "Beauty"] as const;
export type FilterKind = (typeof FILTER_KINDS)[number];

/** OpenCV `BGR2GRAY`의 BT.601 계수. `gray = 0.299R + 0.587G + 0.114B`. */
export const BT601 = { r: 0.299, g: 0.587, b: 0.114 } as const;

/** `dst = src * alpha + beta`(OpenCV `ConvertScaleAbs`) 파라미터. */
export interface ScaleAbsParams {
  readonly alpha: number;
  readonly beta: number;
}

/** 밝게: 약한 대비(1.1) + 밝기(+20). */
export const BRIGHTNESS_PARAMS: ScaleAbsParams = { alpha: 1.1, beta: 20 };

/**
 * 뷰티 = bilateral 스무딩 → 원본과 블렌드 → 약한 톤 보정.
 * 웹은 WebGL2 7×7 bilateral로 구현하고(CPU 폴백 있음) **파라미터 의도를 유지**한다.
 * 픽셀 완전 일치는 목표가 아니다 — 골든 허용 오차 MAE ≤ 3.0/255로 관리한다(10 §4.2).
 */
export const BEAUTY_PARAMS = {
  /** bilateral 커널 지름(OpenCV `d`). */
  diameter: 7,
  sigmaColor: 40,
  sigmaSpace: 7,
  /** 스무딩 결과 가중(원본은 `1 - smoothWeight`). */
  smoothWeight: 0.6,
  tone: { alpha: 1.03, beta: 6 } satisfies ScaleAbsParams,
} as const;

/** BT.601 그레이 값(0~255 입력, 반올림 없이 실수 반환 — 호출측이 클램프·양자화한다). */
export function bt601Gray(r: number, g: number, b: number): number {
  return BT601.r * r + BT601.g * g + BT601.b * b;
}

/** `ConvertScaleAbs` 대응: `|src * alpha + beta|`를 0~255로 포화(saturate)한다. */
export function convertScaleAbs(value: number, params: ScaleAbsParams): number {
  const scaled = Math.abs(value * params.alpha + params.beta);
  return Math.min(255, Math.round(scaled));
}

/** 설정 토글에 따라 결과 화면에 노출할 필터 목록. **원본(None)은 항상 제공한다**(it8 A6). */
export function availableFilters(toggles: {
  readonly FilterGrayscale: boolean;
  readonly FilterBrightness: boolean;
  readonly FilterBeauty: boolean;
}): FilterKind[] {
  const filters: FilterKind[] = ["None"];
  if (toggles.FilterGrayscale) filters.push("Grayscale");
  if (toggles.FilterBrightness) filters.push("Brightness");
  if (toggles.FilterBeauty) filters.push("Beauty");
  return filters;
}
