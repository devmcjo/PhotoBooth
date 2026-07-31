import { inflateSync } from "node:zlib";

/**
 * 최소 PNG 디코더(테스트 전용) — node에는 canvas가 없다
 *
 * 골든 이미지 대조를 node에서 하려면 PNG를 직접 풀어야 한다. `node:zlib`이 inflate를
 * 제공하므로 청크 파싱 + 언필터만 구현하면 된다(8bit, 컬러 타입 2/6만 지원 —
 * OpenCV `imwrite`가 그 형태로 쓴다).
 *
 * ⚠️ 제품 코드가 아니다. 브라우저는 `createImageBitmap`을 쓴다.
 */

export interface DecodedPng {
  readonly width: number;
  readonly height: number;
  /** RGBA, length = width * height * 4. */
  readonly data: Uint8ClampedArray;
}

function readUInt32(bytes: Uint8Array, offset: number): number {
  return (
    ((bytes[offset]! << 24) | (bytes[offset + 1]! << 16) | (bytes[offset + 2]! << 8) | bytes[offset + 3]!) >>>
    0
  );
}

function paeth(a: number, b: number, c: number): number {
  const p = a + b - c;
  const pa = Math.abs(p - a);
  const pb = Math.abs(p - b);
  const pc = Math.abs(p - c);
  if (pa <= pb && pa <= pc) return a;
  return pb <= pc ? b : c;
}

export function decodePng(bytes: Uint8Array): DecodedPng {
  const signature = [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a];
  for (let i = 0; i < signature.length; i++) {
    if (bytes[i] !== signature[i]) throw new Error("PNG 시그니처가 아닙니다.");
  }

  let offset = 8;
  let width = 0;
  let height = 0;
  let bitDepth = 0;
  let colorType = 0;
  const idatParts: Uint8Array[] = [];

  while (offset < bytes.length) {
    const length = readUInt32(bytes, offset);
    const type = String.fromCharCode(
      bytes[offset + 4]!,
      bytes[offset + 5]!,
      bytes[offset + 6]!,
      bytes[offset + 7]!,
    );
    const dataStart = offset + 8;

    if (type === "IHDR") {
      width = readUInt32(bytes, dataStart);
      height = readUInt32(bytes, dataStart + 4);
      bitDepth = bytes[dataStart + 8]!;
      colorType = bytes[dataStart + 9]!;
      const interlace = bytes[dataStart + 12]!;
      if (bitDepth !== 8) throw new Error(`지원하지 않는 비트 심도: ${bitDepth}`);
      if (colorType !== 2 && colorType !== 6) {
        throw new Error(`지원하지 않는 컬러 타입: ${colorType}`);
      }
      if (interlace !== 0) throw new Error("인터레이스 PNG는 지원하지 않습니다.");
    } else if (type === "IDAT") {
      idatParts.push(bytes.subarray(dataStart, dataStart + length));
    } else if (type === "IEND") {
      break;
    }

    offset = dataStart + length + 4; // + CRC
  }

  const channels = colorType === 6 ? 4 : 3;
  const raw = new Uint8Array(inflateSync(Buffer.concat(idatParts.map((p) => Buffer.from(p)))));
  const stride = width * channels;
  const out = new Uint8ClampedArray(width * height * 4);
  const previous = new Uint8Array(stride);
  const current = new Uint8Array(stride);

  let rawOffset = 0;
  for (let y = 0; y < height; y++) {
    const filter = raw[rawOffset++]!;
    current.set(raw.subarray(rawOffset, rawOffset + stride));
    rawOffset += stride;

    for (let i = 0; i < stride; i++) {
      const left = i >= channels ? current[i - channels]! : 0;
      const up = previous[i]!;
      const upLeft = i >= channels ? previous[i - channels]! : 0;
      switch (filter) {
        case 0:
          break;
        case 1:
          current[i] = (current[i]! + left) & 0xff;
          break;
        case 2:
          current[i] = (current[i]! + up) & 0xff;
          break;
        case 3:
          current[i] = (current[i]! + ((left + up) >> 1)) & 0xff;
          break;
        case 4:
          current[i] = (current[i]! + paeth(left, up, upLeft)) & 0xff;
          break;
        default:
          throw new Error(`알 수 없는 PNG 필터: ${filter}`);
      }
    }

    for (let x = 0; x < width; x++) {
      const from = x * channels;
      const to = (y * width + x) * 4;
      out[to] = current[from]!;
      out[to + 1] = current[from + 1]!;
      out[to + 2] = current[from + 2]!;
      out[to + 3] = channels === 4 ? current[from + 3]! : 255;
    }

    previous.set(current);
  }

  return { width, height, data: out };
}

export interface PixelDiff {
  /** 평균 절대 오차(0~255 스케일, RGB 3채널 평균). */
  readonly mae: number;
  /** 채널 단위 최대 차이. */
  readonly maxDiff: number;
  /** 1 이상 차이 나는 픽셀 수. */
  readonly changedPixels: number;
}

/** RGB만 비교한다(알파는 결과물에서 항상 255다). */
export function comparePixels(a: DecodedPng, b: DecodedPng): PixelDiff {
  if (a.width !== b.width || a.height !== b.height) {
    throw new Error(`크기 불일치: ${a.width}x${a.height} vs ${b.width}x${b.height}`);
  }

  let total = 0;
  let maxDiff = 0;
  let changedPixels = 0;

  for (let i = 0; i < a.data.length; i += 4) {
    const dr = Math.abs(a.data[i]! - b.data[i]!);
    const dg = Math.abs(a.data[i + 1]! - b.data[i + 1]!);
    const db = Math.abs(a.data[i + 2]! - b.data[i + 2]!);
    total += dr + dg + db;
    const localMax = Math.max(dr, dg, db);
    if (localMax > maxDiff) maxDiff = localMax;
    if (localMax > 0) changedPixels++;
  }

  const pixelCount = a.width * a.height;
  return { mae: total / (pixelCount * 3), maxDiff, changedPixels };
}
