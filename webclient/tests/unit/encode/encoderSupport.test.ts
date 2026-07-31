import { describe, expect, it } from "vitest";
import {
  detectEncoderPath,
  lastEncoderProbe,
  MEDIARECORDER_MP4_MIME,
  type EncoderProbeDeps,
} from "@adapters/encode/encoderSupport";

/**
 * 경로 판정 — 04 §7.3
 *
 * node에는 `VideoEncoder`·`MediaRecorder`가 없으므로 전부 주입한 가짜로 재현한다.
 * (그래서 이 판정이 UA·버전 문자열이 아니라 **기능 감지**여야만 검증 가능하다.)
 */

interface Query {
  readonly codec: string;
  readonly width: number;
  readonly height: number;
  readonly bitrate: number;
  readonly framerate: number;
}

function fakeVideoEncoder(
  supports: (codec: string) => boolean | "throw",
  log?: Query[],
): NonNullable<EncoderProbeDeps["videoEncoder"]> {
  return {
    async isConfigSupported(config) {
      log?.push({
        codec: config.codec,
        width: config.width ?? 0,
        height: config.height ?? 0,
        bitrate: config.bitrate ?? 0,
        framerate: config.framerate ?? 0,
      });
      const verdict = supports(config.codec);
      if (verdict === "throw") throw new TypeError("unsupported config");
      return { supported: verdict };
    },
  };
}

const SIZE = { width: 811, height: 1080 };

describe("detectEncoderPath — B → A → none 순서가 계약이다", () => {
  it("둘 다 지원하면 WebCodecs(경로 B)를 고른다", async () => {
    const probe = await detectEncoderPath(SIZE, {
      videoEncoder: fakeVideoEncoder(() => true),
      mediaRecorder: { isTypeSupported: () => true },
      workerAvailable: true,
    });
    expect(probe.path).toBe("webcodecs");
    expect(probe.codec).toBe("avc1.42001E");
  });

  it("WebCodecs가 없으면 MediaRecorder(경로 A)로 내려간다", async () => {
    const probe = await detectEncoderPath(SIZE, {
      videoEncoder: undefined,
      mediaRecorder: { isTypeSupported: (type) => type === MEDIARECORDER_MP4_MIME },
      workerAvailable: true,
    });
    expect(probe.path).toBe("mediarecorder");
    expect(probe.codec).toBeNull();
  });

  it("둘 다 없으면 none이다(예외가 아니라 미제공)", async () => {
    const probe = await detectEncoderPath(SIZE, {
      videoEncoder: undefined,
      mediaRecorder: undefined,
      workerAvailable: true,
    });
    expect(probe.path).toBe("none");
    expect(probe.codec).toBeNull();
    expect(probe.reason).toContain("H.264");
  });

  it("첫 코덱이 미지원이면 다음 후보로 넘어간다", async () => {
    const log: Query[] = [];
    const probe = await detectEncoderPath(SIZE, {
      videoEncoder: fakeVideoEncoder((codec) => codec === "avc1.4D001E", log),
      mediaRecorder: undefined,
      workerAvailable: true,
    });
    expect(probe.path).toBe("webcodecs");
    expect(probe.codec).toBe("avc1.4D001E");
    expect(log.map((q) => q.codec)).toEqual(["avc1.42001E", "avc1.42E01E", "avc1.4D001E"]);
    expect(probe.probed).toEqual([
      { codec: "avc1.42001E", supported: false },
      { codec: "avc1.42E01E", supported: false },
      { codec: "avc1.4D001E", supported: true },
    ]);
  });

  it("isConfigSupported가 throw해도 예외가 새지 않는다", async () => {
    const probe = await detectEncoderPath(SIZE, {
      videoEncoder: fakeVideoEncoder(() => "throw"),
      mediaRecorder: undefined,
      workerAvailable: true,
    });
    expect(probe.path).toBe("none");
    expect(probe.probed.every((p) => !p.supported)).toBe(true);
  });

  it("isTypeSupported가 throw해도 예외가 새지 않는다", async () => {
    const probe = await detectEncoderPath(SIZE, {
      videoEncoder: undefined,
      mediaRecorder: {
        isTypeSupported: () => {
          throw new Error("boom");
        },
      },
      workerAvailable: true,
    });
    expect(probe.path).toBe("none");
  });

  it("Worker가 없으면 VideoEncoder가 있어도 경로 B를 건너뛴다", async () => {
    // 경로 B는 Worker 전용 구현이다(§7). 메인에서 375프레임을 디코딩하면 화면이 얼어붙는다.
    const log: Query[] = [];
    const probe = await detectEncoderPath(SIZE, {
      videoEncoder: fakeVideoEncoder(() => true, log),
      mediaRecorder: { isTypeSupported: () => true },
      workerAvailable: false,
    });
    expect(probe.path).toBe("mediarecorder");
    expect(log).toHaveLength(0);
  });

  it("질의 config에 짝수 클램프된 크기와 04 §7.4 비트레이트가 들어간다", async () => {
    const log: Query[] = [];
    await detectEncoderPath(
      { width: 1443, height: 1081 },
      {
        videoEncoder: fakeVideoEncoder(() => true, log),
        mediaRecorder: undefined,
        workerAvailable: true,
      },
    );
    expect(log[0]).toEqual({
      codec: "avc1.42001E",
      width: 1442,
      height: 1080,
      // 1442×1080은 표의 마지막 구간(≤1080×1440)을 넘으므로 화소수 산출값이다.
      bitrate: Math.round(1442 * 1080 * 30 * 0.12),
      framerate: 30,
    });
  });

  it("lastEncoderProbe()가 마지막 판정을 노출한다(진단 E6)", async () => {
    const probe = await detectEncoderPath(SIZE, {
      videoEncoder: undefined,
      mediaRecorder: undefined,
      workerAvailable: true,
    });
    expect(lastEncoderProbe()).toEqual(probe);

    const second = await detectEncoderPath(SIZE, {
      videoEncoder: fakeVideoEncoder(() => true),
      mediaRecorder: undefined,
      workerAvailable: true,
    });
    expect(lastEncoderProbe()).toEqual(second);
    expect(lastEncoderProbe()?.path).toBe("webcodecs");
  });

  it("소스에 UA·버전 문자열 분기가 없다", async () => {
    const { readFileSync } = await import("node:fs");
    const { dirname, join } = await import("node:path");
    const { fileURLToPath } = await import("node:url");
    const file = join(
      dirname(fileURLToPath(import.meta.url)),
      "..",
      "..",
      "..",
      "src",
      "adapters",
      "encode",
      "encoderSupport.ts",
    );
    const code = readFileSync(file, "utf8")
      .replace(/\/\*[\s\S]*?\*\//g, "")
      .replace(/(^|[^:])\/\/.*$/gm, "$1");
    expect(code.includes("userAgent")).toBe(false);
    expect(code.includes("navigator.")).toBe(false);
  });
});
