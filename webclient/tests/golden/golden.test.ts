import { readFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";
import { composeCore, ComposeError } from "@adapters/compose/composeCore";
import type { RgbaImage } from "@adapters/compose/pixelBuffer";
import type { FilterKind } from "@domain/filters/filterParams";
import type { Slot } from "@domain/frames/types";
import { comparePixels, decodePng } from "./png";

/**
 * 골든 이미지 대조 — 10 §4
 *
 * **Windows가 만든 기준 이미지**(`docs/spec-vectors/golden/`)와 웹 합성 결과를 비교한다.
 * 기준은 `tests/MCPhoto.Tests/GoldenImageTests.cs`가 생성·검증하므로, 양쪽이 **같은 파일**을 본다.
 *
 * ⚠️ **슬롯 위치는 0px 오차가 계약이다**(색은 근사 허용). 고주파·체커보드 컷을 넣은 이유가
 *    그것이다 — 1px만 밀려도 MAE가 허용치를 훌쩍 넘는다.
 */

const GOLDEN_DIR = join(
  dirname(fileURLToPath(import.meta.url)),
  "..",
  "..",
  "..",
  "docs",
  "spec-vectors",
  "golden",
);
const INPUT_DIR = join(GOLDEN_DIR, "input");

function loadPng(path: string) {
  return decodePng(new Uint8Array(readFileSync(path)));
}

function toRgbaImage(png: ReturnType<typeof loadPng>): RgbaImage {
  return { width: png.width, height: png.height, data: png.data };
}

/** 10 §4.2 허용 오차. */
const TOLERANCE: Record<FilterKind, { mae: number; maxDiff: number }> = {
  None: { mae: 1.0, maxDiff: 4 },
  Grayscale: { mae: 1.5, maxDiff: 5 },
  Brightness: { mae: 1.5, maxDiff: 5 },
  Beauty: { mae: 3.0, maxDiff: 12 },
};

const FILTER_FILES: [FilterKind, string][] = [
  ["None", "expected-none.png"],
  ["Grayscale", "expected-grayscale.png"],
  ["Brightness", "expected-brightness.png"],
  ["Beauty", "expected-beauty.png"],
];

function loadFixture() {
  const frameImage = toRgbaImage(loadPng(join(INPUT_DIR, "frame.png")));
  const cuts = [
    "cut0-checkerboard.png",
    "cut1-gradient.png",
    "cut2-skintone.png",
    "cut3-highfreq.png",
  ].map((name) => toRgbaImage(loadPng(join(INPUT_DIR, name))));
  const slots = JSON.parse(readFileSync(join(INPUT_DIR, "slots.json"), "utf8")) as Slot[];
  return { frameImage, cuts, slots };
}

describe("골든 이미지 — Windows 합성 결과와 대조(10 §4)", () => {
  const fixture = loadFixture();

  it("입력이 기대한 형태다(프레임 1200×1600 · 슬롯 4개)", () => {
    expect(fixture.frameImage.width).toBe(1200);
    expect(fixture.frameImage.height).toBe(1600);
    expect(fixture.slots).toHaveLength(4);
    expect(fixture.cuts).toHaveLength(4);
  });

  it.each(FILTER_FILES)("%s 필터가 허용 오차 안에 들어온다", (filter, file) => {
    const composed = composeCore({
      frameImage: fixture.frameImage,
      slots: fixture.slots,
      cuts: fixture.cuts,
      filter,
    });
    const expectedPng = loadPng(join(GOLDEN_DIR, file));
    const diff = comparePixels(
      { width: composed.width, height: composed.height, data: composed.data },
      expectedPng,
    );

    const limit = TOLERANCE[filter];
    expect(
      diff.mae,
      `${filter} MAE ${diff.mae.toFixed(3)} (허용 ${limit.mae}) · 최대 ${diff.maxDiff}`,
    ).toBeLessThanOrEqual(limit.mae);
    expect(diff.maxDiff).toBeLessThanOrEqual(limit.maxDiff);
  });

  it("슬롯 위치가 0px 오차다 — 프레임 여백은 완전히 동일해야 한다", () => {
    const composed = composeCore({
      frameImage: fixture.frameImage,
      slots: fixture.slots,
      cuts: fixture.cuts,
      filter: "None",
    });
    const expected = loadPng(join(GOLDEN_DIR, "expected-none.png"));

    // 슬롯 **밖**(프레임 배경)은 컷이 닿지 않으므로 무손실로 같아야 한다.
    // 슬롯이 1px이라도 밀리면 경계에서 차이가 나 여기서 걸린다.
    let outsideDiff = 0;
    for (let y = 0; y < composed.height; y++) {
      for (let x = 0; x < composed.width; x++) {
        const inside = fixture.slots.some(
          (s) => x >= s.x && x < s.x + s.width && y >= s.y && y < s.y + s.height,
        );
        if (inside) continue;
        const offset = (y * composed.width + x) * 4;
        outsideDiff += Math.abs(composed.data[offset]! - expected.data[offset]!);
      }
    }
    expect(outsideDiff).toBe(0);
  });
});

describe("composeCore — 계약 방어", () => {
  const fixture = loadFixture();

  it("컷 수와 슬롯 수가 다르면 오류다(M12)", () => {
    expect(() =>
      composeCore({
        frameImage: fixture.frameImage,
        slots: fixture.slots,
        cuts: fixture.cuts.slice(0, 3),
        filter: "None",
      }),
    ).toThrow(ComposeError);
  });

  it("프레임 이미지가 없으면 조용히 진행하지 않고 실패한다", () => {
    expect(() =>
      composeCore({
        frameImage: { width: 0, height: 0, data: new Uint8ClampedArray() },
        slots: fixture.slots,
        cuts: fixture.cuts,
        filter: "None",
      }),
    ).toThrow(ComposeError);
  });

  it("슬롯 index 순서대로 배치한다(입력 배열 순서에 의존하지 않는다)", () => {
    // 슬롯을 뒤집어 넣고 컷도 같은 순서로 뒤집으면 결과가 같아야 한다.
    const reversedSlots = [...fixture.slots].reverse();
    const reversedCuts = [...fixture.cuts].reverse();

    const normal = composeCore({
      frameImage: fixture.frameImage,
      slots: fixture.slots,
      cuts: fixture.cuts,
      filter: "None",
    });
    const reversed = composeCore({
      frameImage: fixture.frameImage,
      slots: reversedSlots,
      cuts: reversedCuts,
      filter: "None",
    });

    expect(Buffer.from(reversed.data)).toEqual(Buffer.from(normal.data));
  });

  it("원본 프레임 버퍼를 변형하지 않는다(필터 변경 시 재합성할 수 있어야 한다)", () => {
    const before = Buffer.from(fixture.frameImage.data);
    composeCore({
      frameImage: fixture.frameImage,
      slots: fixture.slots,
      cuts: fixture.cuts,
      filter: "Grayscale",
    });
    expect(Buffer.from(fixture.frameImage.data)).toEqual(before);
  });
});
