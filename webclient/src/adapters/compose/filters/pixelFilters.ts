import { BEAUTY_PARAMS, BRIGHTNESS_PARAMS, BT601, type FilterKind } from "@domain/filters/filterParams";
import { cloneImage, createImage, type RgbaImage } from "../pixelBuffer";

/**
 * 필터 CPU 구현 — 04 §6 (입력을 변형하지 않고 **새 버퍼**를 만든다)
 *
 * ⚠️ **흑백에 CSS/canvas `filter`를 쓰면 안 된다.** `grayscale(1)`은 CSS Color 스펙의
 *    BT.709 계열 계수(0.2126/0.7152/0.0722)를 쓰고, 규격은 **BT.601**(0.299/0.587/0.114)이다.
 *    눈에 보이게 달라진다 — 직접 계산한다.
 *
 * ⚠️ 색 공간은 **sRGB 값 그대로**다(선형화하지 않는다). OpenCV가 8bit 값에 직접 연산하므로
 *    선형화하면 결과가 달라진다(04 §6.2).
 */

/** `saturate(round(|v * alpha + beta|))` — OpenCV `ConvertScaleAbs` 대응. */
function scaleAbs(value: number, alpha: number, beta: number): number {
  const scaled = Math.abs(value * alpha + beta);
  return scaled > 255 ? 255 : Math.round(scaled);
}

export function applyGrayscale(src: RgbaImage): RgbaImage {
  const out = createImage(src.width, src.height);
  for (let i = 0; i < src.data.length; i += 4) {
    // OpenCV BGR2GRAY와 같은 계수·반올림.
    const y = Math.round(
      BT601.r * src.data[i]! + BT601.g * src.data[i + 1]! + BT601.b * src.data[i + 2]!,
    );
    out.data[i] = y;
    out.data[i + 1] = y;
    out.data[i + 2] = y;
    out.data[i + 3] = 255;
  }
  return out;
}

export function applyBrightness(src: RgbaImage): RgbaImage {
  const out = createImage(src.width, src.height);
  const { alpha, beta } = BRIGHTNESS_PARAMS;
  for (let i = 0; i < src.data.length; i += 4) {
    out.data[i] = scaleAbs(src.data[i]!, alpha, beta);
    out.data[i + 1] = scaleAbs(src.data[i + 1]!, alpha, beta);
    out.data[i + 2] = scaleAbs(src.data[i + 2]!, alpha, beta);
    out.data[i + 3] = 255;
  }
  return out;
}

/**
 * 뷰티 = bilateral(d=7, σColor=40, σSpace=7) → 원본과 60/40 블렌드 → 톤 보정(1.03, +6).
 *
 * OpenCV `BilateralFilter`는 채널 차이의 **제곱합**으로 색 가중치를 만든다(채널별 독립이 아니다).
 * 공간 가중치는 미리 계산해 재사용한다(49탭 × 픽셀 수라 매 픽셀 exp 호출은 비싸다).
 */
export function applyBeauty(src: RgbaImage): RgbaImage {
  const radius = Math.floor(BEAUTY_PARAMS.diameter / 2); // d=7 → 반경 3
  const sigmaColor = BEAUTY_PARAMS.sigmaColor;
  const sigmaSpace = BEAUTY_PARAMS.sigmaSpace;

  // 공간 가중치 테이블(7×7).
  const size = radius * 2 + 1;
  const spaceWeights = new Float64Array(size * size);
  for (let dy = -radius; dy <= radius; dy++) {
    for (let dx = -radius; dx <= radius; dx++) {
      spaceWeights[(dy + radius) * size + (dx + radius)] = Math.exp(
        -(dx * dx + dy * dy) / (2 * sigmaSpace * sigmaSpace),
      );
    }
  }

  // 색 가중치 테이블: 제곱합은 0~3*255² 범위라 정수 인덱스로 캐시할 수 있다.
  const colorDenominator = 2 * sigmaColor * sigmaColor;
  const colorWeights = new Float64Array(3 * 255 * 255 + 1);
  for (let d = 0; d < colorWeights.length; d++) {
    colorWeights[d] = Math.exp(-d / colorDenominator);
  }

  const out = createImage(src.width, src.height);
  const { smoothWeight, tone } = BEAUTY_PARAMS;
  const srcWeight = 1 - smoothWeight;

  for (let y = 0; y < src.height; y++) {
    for (let x = 0; x < src.width; x++) {
      const center = (y * src.width + x) * 4;
      const cr = src.data[center]!;
      const cg = src.data[center + 1]!;
      const cb = src.data[center + 2]!;

      let sumR = 0;
      let sumG = 0;
      let sumB = 0;
      let sumWeight = 0;

      for (let dy = -radius; dy <= radius; dy++) {
        const sy = y + dy;
        if (sy < 0 || sy >= src.height) continue;
        for (let dx = -radius; dx <= radius; dx++) {
          const sx = x + dx;
          if (sx < 0 || sx >= src.width) continue;

          const offset = (sy * src.width + sx) * 4;
          const dr = src.data[offset]! - cr;
          const dg = src.data[offset + 1]! - cg;
          const db = src.data[offset + 2]! - cb;
          const weight =
            spaceWeights[(dy + radius) * size + (dx + radius)]! *
            colorWeights[dr * dr + dg * dg + db * db]!;

          sumR += src.data[offset]! * weight;
          sumG += src.data[offset + 1]! * weight;
          sumB += src.data[offset + 2]! * weight;
          sumWeight += weight;
        }
      }

      // 블렌드 → 톤 보정. 블렌드는 반올림하지 않고 이어서 계산한다(중간 반올림 누적 방지).
      const blendR = (sumR / sumWeight) * smoothWeight + cr * srcWeight;
      const blendG = (sumG / sumWeight) * smoothWeight + cg * srcWeight;
      const blendB = (sumB / sumWeight) * smoothWeight + cb * srcWeight;

      out.data[center] = scaleAbs(blendR, tone.alpha, tone.beta);
      out.data[center + 1] = scaleAbs(blendG, tone.alpha, tone.beta);
      out.data[center + 2] = scaleAbs(blendB, tone.alpha, tone.beta);
      out.data[center + 3] = 255;
    }
  }
  return out;
}

/** 필터 적용. `None`은 복사본을 돌려준다(원본 불변 계약). */
export function applyFilter(src: RgbaImage, filter: FilterKind): RgbaImage {
  switch (filter) {
    case "Grayscale":
      return applyGrayscale(src);
    case "Brightness":
      return applyBrightness(src);
    case "Beauty":
      return applyBeauty(src);
    default:
      return cloneImage(src);
  }
}
