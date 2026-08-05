import { readFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { beforeEach, describe, expect, it } from "vitest";
import {
  createQrMatrix,
  drawQrToCanvas,
  QR_ECC_LEVEL,
  QR_TARGET_PX,
  type QrMatrix,
} from "@adapters/qr/qrService";
import { planQrRender, QR_QUIET_ZONE_MODULES } from "@domain/upload/qrRenderPlan";
import {
  attachLogStore,
  createLogStore,
  createMemoryLogSink,
  detachLogStore,
} from "@adapters/storage/logStore";

/**
 * QR 생성·렌더 — 03 §9 · VF-13
 *
 * vitest 환경은 node라 canvas가 없다. **최소 가짜**로 계약만 고정한다.
 */

/**
 * ⚠️ **주석을 제거하고 검사한다.** 불변식은 *코드*에 대한 것이라, 규칙을 설명하는 주석
 * ("`innerHTML`을 쓰지 않는다")이 그 규칙을 깨뜨린 것처럼 보이면 안 된다
 * (`purity.test.ts`가 같은 이유로 같은 처리를 한다).
 */
function stripComments(source: string): string {
  return source.replace(/\/\*[\s\S]*?\*\//g, "").replace(/(^|[^:])\/\/.*$/gm, "$1");
}

const QR_SOURCE = stripComments(
  readFileSync(
    join(
      dirname(fileURLToPath(import.meta.url)),
      "..",
      "..",
      "..",
      "src",
      "adapters",
      "qr",
      "qrService.ts",
    ),
    "utf8",
  ),
);

/** 실제 길이의 다운로드 페이지 URL(약 90자) — A3 검증 입력. */
const REAL_URL =
  "https://mcphoto-955fb.web.app/?s=20260730_143022_a1b2c3d4-5e6f-4708-9a0b-1c2d3e4f5a6b";

interface FillRectCall {
  readonly style: string;
  readonly args: [number, number, number, number];
}

function fakeCanvas(withContext = true): {
  canvas: HTMLCanvasElement;
  calls: FillRectCall[];
} {
  const calls: FillRectCall[] = [];
  const context = {
    fillStyle: "",
    fillRect(x: number, y: number, w: number, h: number) {
      calls.push({ style: String(context.fillStyle), args: [x, y, w, h] });
    },
  };
  const canvas = {
    width: 0,
    height: 0,
    getContext: () => (withContext ? context : null),
  };
  return { canvas: canvas as unknown as HTMLCanvasElement, calls };
}

/** 체커보드 모듈 행렬(라이브러리 없이 렌더 계약만 볼 때 쓴다). */
function checkerMatrix(moduleCount: number): QrMatrix {
  return { moduleCount, isDark: (row, col) => (row + col) % 2 === 0 };
}

beforeEach(() => {
  detachLogStore();
  attachLogStore(createLogStore({ sink: createMemoryLogSink(), now: () => 0 }));
});

describe("qrService — ECC 레벨 (VF-13)", () => {
  it("오류정정 레벨이 Q다(Windows QrService.cs와 일치)", () => {
    expect(QR_ECC_LEVEL).toBe("Q");
  });

  it("소스에 다른 ECC 리터럴이 없다", () => {
    for (const other of ['"L"', '"M"', '"H"']) {
      expect(QR_SOURCE, `${other} — ECC 레벨은 Q 고정이다`).not.toContain(other);
    }
  });

  it("innerHTML 경로를 만들지 않는다(canvas 렌더)", () => {
    expect(QR_SOURCE).not.toContain("innerHTML");
    expect(QR_SOURCE).not.toContain("createImgTag(");
    expect(QR_SOURCE).not.toContain("createSvgTag(");
  });
});

describe("qrService — 행렬 생성 (A3)", () => {
  it("실제 길이의 다운로드 페이지 URL을 수용한다", () => {
    const matrix = createQrMatrix(REAL_URL);

    expect(matrix).not.toBeNull();
    expect(matrix!.moduleCount).toBeGreaterThan(0);
    // 좌상단 finder 패턴은 항상 어둡다.
    expect(matrix!.isDark(0, 0)).toBe(true);
  });

  it("모듈 수가 QR 규격(21 + 4k)에 맞는다", () => {
    const matrix = createQrMatrix(REAL_URL);
    expect((matrix!.moduleCount - 21) % 4).toBe(0);
    expect(matrix!.moduleCount).toBeGreaterThanOrEqual(21);
  });

  it("빈 문자열에도 던지지 않는다", () => {
    expect(() => createQrMatrix("")).not.toThrow();
  });

  it("용량을 넘는 입력은 예외가 아니라 null이다", () => {
    // ECC Q · type 40의 바이트 모드 상한(약 1663자)을 확실히 넘긴다.
    expect(createQrMatrix("x".repeat(8000))).toBeNull();
  });
});

describe("qrService — canvas 렌더", () => {
  it("배경 1회 + 어두운 모듈 수만큼 fillRect를 부른다", () => {
    const matrix = checkerMatrix(21);
    const { canvas, calls } = fakeCanvas();

    expect(drawQrToCanvas(canvas, matrix, 640)).toBe(true);

    let dark = 0;
    for (let row = 0; row < 21; row++) {
      for (let col = 0; col < 21; col++) if (matrix.isDark(row, col)) dark++;
    }
    expect(calls).toHaveLength(1 + dark);
  });

  it("첫 호출은 흰 배경이고 다크모드에서도 반전하지 않는다", () => {
    const { canvas, calls } = fakeCanvas();
    drawQrToCanvas(canvas, checkerMatrix(21), 640);

    expect(calls[0]!.style).toBe("#ffffff");
    expect(calls.slice(1).every((c) => c.style === "#000000")).toBe(true);
  });

  it("캔버스 크기 = modulePx * (모듈 + 여백*2)이고 여백만큼 안쪽에 그린다", () => {
    const matrix = checkerMatrix(21);
    const plan = planQrRender(21, 640);
    const { canvas, calls } = fakeCanvas();

    drawQrToCanvas(canvas, matrix, 640);

    expect(canvas.width).toBe(plan.canvasPx);
    expect(canvas.height).toBe(plan.canvasPx);
    expect(plan.canvasPx).toBe(plan.modulePx * (21 + QR_QUIET_ZONE_MODULES * 2));
    // (0,0) 모듈은 여백만큼 밀려 있다.
    expect(calls[1]!.args).toEqual([plan.quietPx, plan.quietPx, plan.modulePx, plan.modulePx]);
  });

  it("2D 컨텍스트가 없으면 false를 돌려주고 던지지 않는다", () => {
    const { canvas, calls } = fakeCanvas(false);
    expect(drawQrToCanvas(canvas, checkerMatrix(21), 640)).toBe(false);
    expect(calls).toEqual([]);
  });

  it("기본 표시 크기로도 그려진다", () => {
    const { canvas } = fakeCanvas();
    expect(drawQrToCanvas(canvas, checkerMatrix(21))).toBe(true);
    expect(QR_TARGET_PX).toBeGreaterThan(0);
  });

  it("실제 행렬로 끝까지 그려진다(라이브러리 ↔ 렌더 결합)", () => {
    const matrix = createQrMatrix(REAL_URL);
    const { canvas, calls } = fakeCanvas();

    expect(drawQrToCanvas(canvas, matrix!, 640)).toBe(true);
    expect(calls.length).toBeGreaterThan(1);
  });
});
