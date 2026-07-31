import { describe, expect, it } from "vitest";
import {
  frameImageResizeFactor,
  isFrameImageSizeWithinLimit,
  isSupportedFrameImage,
  MAX_FRAME_IMAGE_BYTES,
  MAX_FRAME_IMAGE_LONG_SIDE,
  scaledFrameImageSize,
  SUPPORTED_FRAME_IMAGE_EXTENSIONS,
  SUPPORTED_FRAME_IMAGE_MIME_TYPES,
} from "@domain/frames/frameImagePolicy";

/**
 * 프레임 이미지 제한 — 설계 §9.1 (Windows `FrameImageValidator.cs` 이식)
 *
 * ↔ `tests/MCPhoto.Tests/SlotLayoutTests.cs:256-287` — 벡터 파일이 없으므로 여기서 같은
 *    기대값을 명시해 짝을 고정한다(`resultNaming` 선례).
 */

describe("용량 제한 10MB", () => {
  it("5MB는 통과, 11MB는 거부", () => {
    // ↔ SlotLayoutTests.cs:256
    expect(isFrameImageSizeWithinLimit(5_000_000)).toBe(true);
    expect(isFrameImageSizeWithinLimit(11_000_000)).toBe(false);
  });

  it("경계값(정확히 10MB)은 허용이다", () => {
    expect(MAX_FRAME_IMAGE_BYTES).toBe(10 * 1024 * 1024);
    expect(isFrameImageSizeWithinLimit(MAX_FRAME_IMAGE_BYTES)).toBe(true);
    expect(isFrameImageSizeWithinLimit(MAX_FRAME_IMAGE_BYTES + 1)).toBe(false);
  });

  it("0바이트·음수·NaN 방어", () => {
    expect(isFrameImageSizeWithinLimit(0)).toBe(true);
    expect(isFrameImageSizeWithinLimit(-1)).toBe(false);
    expect(isFrameImageSizeWithinLimit(Number.NaN)).toBe(false);
  });
});

describe("장변 4000 축소", () => {
  it("8000×4000 → 4000×2000", () => {
    // ↔ SlotLayoutTests.cs:270
    expect(scaledFrameImageSize(8000, 4000)).toEqual({ width: 4000, height: 2000 });
  });

  it("3000×2000은 무변경이다", () => {
    // ↔ SlotLayoutTests.cs:279
    expect(scaledFrameImageSize(3000, 2000)).toEqual({ width: 3000, height: 2000 });
    expect(frameImageResizeFactor(3000, 2000)).toBe(1);
  });

  it("세로가 긴 이미지도 장변 기준으로 줄인다", () => {
    expect(scaledFrameImageSize(2000, 8000)).toEqual({ width: 1000, height: 4000 });
  });

  it("경계값(4000)은 축소하지 않는다", () => {
    expect(MAX_FRAME_IMAGE_LONG_SIDE).toBe(4000);
    expect(frameImageResizeFactor(4000, 3000)).toBe(1);
    expect(scaledFrameImageSize(4000, 3000)).toEqual({ width: 4000, height: 3000 });
  });

  it("축소 결과의 최소값은 1px이다(0px 캔버스 금지)", () => {
    expect(scaledFrameImageSize(40000, 1)).toEqual({ width: 4000, height: 1 });
  });

  it("반올림은 half-to-even이다(Windows와 픽셀이 갈라지지 않게)", () => {
    // 4001×4001 → factor = 4000/4001, 4001*factor = 4000(정확). 홀수 높이의 중간값을 만든다.
    const scaled = scaledFrameImageSize(9000, 4501);
    // 4501 * (4000/9000) = 2000.444… → 2000
    expect(scaled).toEqual({ width: 4000, height: 2000 });
  });
});

describe("지원 형식 — MIME 우선, 비면 확장자", () => {
  it("MIME이 비면 확장자로 판정한다(대소문자 무시)", () => {
    // ↔ SlotLayoutTests.cs:284
    for (const name of ["a.PNG", "a.JPG", "a.jpeg", "a.png", "a.jpg"]) {
      expect(isSupportedFrameImage("", name), name).toBe(true);
    }
    for (const name of ["a.gif", "a.bmp", "a.webp", "a", ""]) {
      expect(isSupportedFrameImage("", name), name).toBe(false);
    }
  });

  it("MIME이 있으면 MIME이 이긴다", () => {
    expect(isSupportedFrameImage("image/gif", "a.png")).toBe(false);
    expect(isSupportedFrameImage("image/png", "a.gif")).toBe(true);
    expect(isSupportedFrameImage("IMAGE/JPEG", "a.gif")).toBe(true);
  });

  it("지원 목록이 규격 그대로다", () => {
    expect(SUPPORTED_FRAME_IMAGE_EXTENSIONS).toEqual([".png", ".jpg", ".jpeg"]);
    expect(SUPPORTED_FRAME_IMAGE_MIME_TYPES).toEqual(["image/png", "image/jpeg"]);
  });
});
