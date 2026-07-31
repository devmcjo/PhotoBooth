/**
 * RGBA 픽셀 버퍼 — 합성·필터의 공용 표현
 *
 * ⚠️ 합성 핵심 로직을 **브라우저 API 없이** 이 타입 위에서 돌리는 이유:
 *    골든 이미지 검증을 node(vitest)에서 실행하려면 `OffscreenCanvas`가 없어야 한다.
 *    브라우저에서는 `ImageBitmap → ImageData`로 변환해 같은 함수를 쓴다.
 *    덕분에 **브라우저와 테스트가 같은 코드 경로**를 지난다(테스트만 통과하는 구현을 막는다).
 */

export interface RgbaImage {
  readonly width: number;
  readonly height: number;
  /** length = width * height * 4, 순서 RGBA. */
  readonly data: Uint8ClampedArray;
}

export function createImage(width: number, height: number): RgbaImage {
  return { width, height, data: new Uint8ClampedArray(width * height * 4) };
}

/** 원본을 변형하지 않는다(필터 규격 — 04 §6 "새 버퍼를 만든다"). */
export function cloneImage(image: RgbaImage): RgbaImage {
  return { width: image.width, height: image.height, data: new Uint8ClampedArray(image.data) };
}

/** 지정 사각형을 잘라 새 버퍼로. 경계를 벗어나면 클램프한다. */
export function cropImage(
  image: RgbaImage,
  x: number,
  y: number,
  width: number,
  height: number,
): RgbaImage {
  const sx = Math.max(0, Math.min(x, image.width));
  const sy = Math.max(0, Math.min(y, image.height));
  const w = Math.max(0, Math.min(width, image.width - sx));
  const h = Math.max(0, Math.min(height, image.height - sy));

  const out = createImage(w, h);
  for (let row = 0; row < h; row++) {
    const from = ((sy + row) * image.width + sx) * 4;
    out.data.set(image.data.subarray(from, from + w * 4), row * w * 4);
  }
  return out;
}

/**
 * `src`를 `dest`의 (x, y)에 **덮어쓴다**(알파 블렌딩이 아니다 — 04 §5.2).
 * 프레임 배경 위에 컷을 얹는 유일한 방법이다.
 */
export function blitOver(dest: RgbaImage, src: RgbaImage, x: number, y: number): void {
  for (let row = 0; row < src.height; row++) {
    const destY = y + row;
    if (destY < 0 || destY >= dest.height) continue;
    for (let col = 0; col < src.width; col++) {
      const destX = x + col;
      if (destX < 0 || destX >= dest.width) continue;
      const from = (row * src.width + col) * 4;
      const to = (destY * dest.width + destX) * 4;
      dest.data[to] = src.data[from]!;
      dest.data[to + 1] = src.data[from + 1]!;
      dest.data[to + 2] = src.data[from + 2]!;
      dest.data[to + 3] = 255; // 결과물은 불투명하다(JPEG 출력이 알파를 버린다)
    }
  }
}

/** 단색으로 채운 이미지(테스트·fallback 배경). */
export function solidImage(width: number, height: number, r: number, g: number, b: number): RgbaImage {
  const image = createImage(width, height);
  for (let i = 0; i < image.data.length; i += 4) {
    image.data[i] = r;
    image.data[i + 1] = g;
    image.data[i + 2] = b;
    image.data[i + 3] = 255;
  }
  return image;
}
