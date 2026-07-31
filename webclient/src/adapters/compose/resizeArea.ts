import { createImage, type RgbaImage } from "./pixelBuffer";

/**
 * 면적 평균 리사이즈 — OpenCV `INTER_AREA` 대응 (analysis/14 §5.2)
 *
 * Windows `CompositionService`가 `Cv2.Resize(..., InterpolationFlags.Area)`를 쓴다.
 * 축소에 강한 보간이며, 목적 픽셀 하나가 덮는 **원본 사각형의 면적 가중 평균**이다.
 *
 * ⚠️ OpenCV와 **비트 단위로 같지는 않다**(내부 고정소수점 누적 차이). 골든 허용 오차
 *    MAE ≤ 1.0/255 안에 들어오는 것이 목표다(10 §4.2).
 * ⚠️ 확대 시 OpenCV `INTER_AREA`는 최근접과 유사하게 동작한다 — 그 동작을 따른다.
 */
export function resizeArea(src: RgbaImage, destWidth: number, destHeight: number): RgbaImage {
  const width = Math.max(1, Math.round(destWidth));
  const height = Math.max(1, Math.round(destHeight));
  if (src.width === width && src.height === height) {
    return { width, height, data: new Uint8ClampedArray(src.data) };
  }

  const scaleX = src.width / width;
  const scaleY = src.height / height;

  // 확대(스케일 < 1)는 최근접으로 — OpenCV INTER_AREA의 문서화된 동작.
  if (scaleX < 1 || scaleY < 1) return resizeNearest(src, width, height);

  const out = createImage(width, height);
  for (let dy = 0; dy < height; dy++) {
    // 이 목적 행이 덮는 원본 y 구간 [y0, y1)
    const y0 = dy * scaleY;
    const y1 = Math.min((dy + 1) * scaleY, src.height);
    const firstRow = Math.floor(y0);
    const lastRow = Math.min(Math.ceil(y1) - 1, src.height - 1);

    for (let dx = 0; dx < width; dx++) {
      const x0 = dx * scaleX;
      const x1 = Math.min((dx + 1) * scaleX, src.width);
      const firstCol = Math.floor(x0);
      const lastCol = Math.min(Math.ceil(x1) - 1, src.width - 1);

      let sumR = 0;
      let sumG = 0;
      let sumB = 0;
      let sumWeight = 0;

      for (let sy = firstRow; sy <= lastRow; sy++) {
        // 경계 픽셀은 겹치는 만큼만 반영한다(부분 면적 가중).
        const wy = Math.min(sy + 1, y1) - Math.max(sy, y0);
        if (wy <= 0) continue;
        for (let sx = firstCol; sx <= lastCol; sx++) {
          const wx = Math.min(sx + 1, x1) - Math.max(sx, x0);
          if (wx <= 0) continue;
          const weight = wx * wy;
          const offset = (sy * src.width + sx) * 4;
          sumR += src.data[offset]! * weight;
          sumG += src.data[offset + 1]! * weight;
          sumB += src.data[offset + 2]! * weight;
          sumWeight += weight;
        }
      }

      const to = (dy * width + dx) * 4;
      if (sumWeight > 0) {
        out.data[to] = Math.round(sumR / sumWeight);
        out.data[to + 1] = Math.round(sumG / sumWeight);
        out.data[to + 2] = Math.round(sumB / sumWeight);
      }
      out.data[to + 3] = 255;
    }
  }
  return out;
}

function resizeNearest(src: RgbaImage, width: number, height: number): RgbaImage {
  const out = createImage(width, height);
  const scaleX = src.width / width;
  const scaleY = src.height / height;
  for (let dy = 0; dy < height; dy++) {
    const sy = Math.min(src.height - 1, Math.floor(dy * scaleY));
    for (let dx = 0; dx < width; dx++) {
      const sx = Math.min(src.width - 1, Math.floor(dx * scaleX));
      const from = (sy * src.width + sx) * 4;
      const to = (dy * width + dx) * 4;
      out.data[to] = src.data[from]!;
      out.data[to + 1] = src.data[from + 1]!;
      out.data[to + 2] = src.data[from + 2]!;
      out.data[to + 3] = 255;
    }
  }
  return out;
}
