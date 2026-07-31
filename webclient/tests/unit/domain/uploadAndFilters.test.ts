import { describe, expect, it } from "vitest";
import {
  finalImageContentType,
  isValidSessionId,
  newSessionId,
  SESSION_ID_PATTERN,
  TIMELAPSE_CONTENT_TYPE,
} from "@domain/upload/uploadContract";
import {
  activeStages,
  overallProgress,
  resolveUploadTargets,
  UPLOAD_STAGES,
} from "@domain/upload/uploadOrchestration";
import { planQrRender, QR_QUIET_ZONE_MODULES } from "@domain/upload/qrRenderPlan";
import { EXPORT_FILE_PREFIX, exportFileName } from "@domain/upload/exportFileName";
import {
  availableFilters,
  BEAUTY_PARAMS,
  BRIGHTNESS_PARAMS,
  BT601,
  bt601Gray,
  convertScaleAbs,
  FILTER_KINDS,
} from "@domain/filters/filterParams";

describe("uploadContract — 세션 ID 형식(M13)", () => {
  const uuid = "3f2a1b4c-5d6e-4f70-8a9b-0c1d2e3f4a5b";

  it("정규식이 규격 형식을 강제한다", () => {
    const id = newSessionId(new Date(2026, 6, 30, 21, 5, 9), uuid);
    expect(id).toBe(`20260730_210509_${uuid}`);
    expect(isValidSessionId(id)).toBe(true);
    expect(SESSION_ID_PATTERN.test(id)).toBe(true);
  });

  it("형식을 벗어난 값을 거부한다", () => {
    expect(isValidSessionId("20260730_210509")).toBe(false);
    expect(isValidSessionId(`2026730_210509_${uuid}`)).toBe(false);
    expect(isValidSessionId(`20260730_2105_${uuid}`)).toBe(false);
    expect(isValidSessionId(`20260730_210509_${uuid.toUpperCase()}`)).toBe(false);
    expect(isValidSessionId(`prefix_20260730_210509_${uuid}`)).toBe(false);
    expect(isValidSessionId("")).toBe(false);
  });

  it("한 자리 월·일·시를 0으로 채운다", () => {
    expect(newSessionId(new Date(2026, 0, 1, 0, 0, 0), uuid)).toBe(`20260101_000000_${uuid}`);
  });

  it("MIME 타입이 서버 검증과 일치한다(VF-5)", () => {
    expect(finalImageContentType("Jpg")).toBe("image/jpeg");
    expect(finalImageContentType("Png")).toBe("image/png");
    expect(TIMELAPSE_CONTENT_TYPE).toBe("video/mp4");
  });
});

describe("uploadOrchestration — 전송 대상 확정(M7)", () => {
  it("토글과 파일 존재가 모두 참일 때만 올린다", () => {
    expect(
      resolveUploadTargets({
        sendPhoto: true,
        sendTimelapse: true,
        hasFinalImage: true,
        hasTimelapse: true,
      }),
    ).toEqual({ uploadPhoto: true, uploadTimelapse: true, canUpload: true });

    // 타임랩스 미지원 브라우저: 토글은 on이지만 파일이 없다 → 사진만(정상 축소)
    expect(
      resolveUploadTargets({
        sendPhoto: true,
        sendTimelapse: true,
        hasFinalImage: true,
        hasTimelapse: false,
      }),
    ).toEqual({ uploadPhoto: true, uploadTimelapse: false, canUpload: true });
  });

  it("둘 다 없으면 업로드를 시도하지 않는다 — 빈 commit 금지", () => {
    expect(
      resolveUploadTargets({
        sendPhoto: false,
        sendTimelapse: false,
        hasFinalImage: true,
        hasTimelapse: true,
      }).canUpload,
    ).toBe(false);

    expect(
      resolveUploadTargets({
        sendPhoto: true,
        sendTimelapse: true,
        hasFinalImage: false,
        hasTimelapse: false,
      }).canUpload,
    ).toBe(false);
  });

  it("단계 순서는 사진 → 타임랩스 → commit이다", () => {
    expect(UPLOAD_STAGES).toEqual(["Photo", "Timelapse", "Finalizing"]);

    const both = resolveUploadTargets({
      sendPhoto: true,
      sendTimelapse: true,
      hasFinalImage: true,
      hasTimelapse: true,
    });
    expect(activeStages(both)).toEqual(["Photo", "Timelapse", "Finalizing"]);

    const photoOnly = resolveUploadTargets({
      sendPhoto: true,
      sendTimelapse: false,
      hasFinalImage: true,
      hasTimelapse: false,
    });
    expect(activeStages(photoOnly)).toEqual(["Photo", "Finalizing"]);

    const none = resolveUploadTargets({
      sendPhoto: false,
      sendTimelapse: false,
      hasFinalImage: false,
      hasTimelapse: false,
    });
    expect(activeStages(none)).toEqual([]);
  });
});

describe("uploadOrchestration — 진행률 합산(WM5)", () => {
  const both = resolveUploadTargets({
    sendPhoto: true,
    sendTimelapse: true,
    hasFinalImage: true,
    hasTimelapse: true,
  });

  it("활성 단계에 균등 가중을 준다", () => {
    expect(overallProgress(both, "Photo", 0)).toBeCloseTo(0, 10);
    expect(overallProgress(both, "Photo", 1)).toBeCloseTo(1 / 3, 10);
    expect(overallProgress(both, "Timelapse", 0.5)).toBeCloseTo(1 / 3 + 1 / 6, 10);
    expect(overallProgress(both, "Finalizing", 1)).toBeCloseTo(1, 10);
  });

  it("단조 증가한다 — 0에서 100으로 점프하지 않는다", () => {
    const samples = [
      overallProgress(both, "Photo", 0),
      overallProgress(both, "Photo", 0.5),
      overallProgress(both, "Photo", 1),
      overallProgress(both, "Timelapse", 0.5),
      overallProgress(both, "Finalizing", 1),
    ];
    for (let i = 1; i < samples.length; i++) {
      expect(samples[i]!).toBeGreaterThan(samples[i - 1]!);
    }
  });

  it("파일 진행률을 0~1로 클램프한다", () => {
    expect(overallProgress(both, "Photo", -5)).toBeCloseTo(0, 10);
    expect(overallProgress(both, "Photo", 99)).toBeCloseTo(1 / 3, 10);
  });

  it("비활성 단계·업로드 없음은 0이다(조용히 100%가 되지 않는다)", () => {
    const photoOnly = resolveUploadTargets({
      sendPhoto: true,
      sendTimelapse: false,
      hasFinalImage: true,
      hasTimelapse: false,
    });
    expect(overallProgress(photoOnly, "Timelapse", 1)).toBe(0);

    const none = resolveUploadTargets({
      sendPhoto: false,
      sendTimelapse: false,
      hasFinalImage: false,
      hasTimelapse: false,
    });
    expect(overallProgress(none, "Photo", 1)).toBe(0);
  });
});

describe("qrRenderPlan — 정수 배율 + 여백 4모듈 (03 §9)", () => {
  it("여백은 4모듈이 규격이다", () => {
    expect(QR_QUIET_ZONE_MODULES).toBe(4);
  });

  it("표시 크기에서 정수 배율을 뽑는다(type 6 = 41모듈)", () => {
    // 41 + 4*2 = 49모듈 → floor(640/49) = 13
    expect(planQrRender(41, 640)).toEqual({ modulePx: 13, canvasPx: 637, quietPx: 52 });
  });

  it("표시 크기가 작아도 최소 1px을 보장한다(빈 캔버스 금지)", () => {
    expect(planQrRender(41, 40)).toEqual({ modulePx: 1, canvasPx: 49, quietPx: 4 });
  });

  it("여백 0을 명시하면 여백 없이 계산한다", () => {
    expect(planQrRender(41, 640, 0)).toEqual({ modulePx: 15, canvasPx: 615, quietPx: 0 });
  });

  it("캔버스 한 변은 항상 modulePx * (모듈 + 여백*2)다", () => {
    for (const moduleCount of [21, 25, 41, 57, 177]) {
      const plan = planQrRender(moduleCount, 640);
      expect(plan.canvasPx).toBe(plan.modulePx * (moduleCount + QR_QUIET_ZONE_MODULES * 2));
      expect(plan.quietPx).toBe(plan.modulePx * QR_QUIET_ZONE_MODULES);
    }
  });

  it("방어: 모듈 수가 0·음수·NaN이면 1x1로 축소하고 던지지 않는다", () => {
    expect(planQrRender(0, 640)).toEqual({ modulePx: 1, canvasPx: 1, quietPx: 0 });
    expect(planQrRender(-1, 640)).toEqual({ modulePx: 1, canvasPx: 1, quietPx: 0 });
    expect(planQrRender(Number.NaN, 640)).toEqual({ modulePx: 1, canvasPx: 1, quietPx: 0 });
  });

  it("방어: 표시 크기가 0·음수·무한대여도 던지지 않는다", () => {
    expect(planQrRender(41, 0)).toEqual({ modulePx: 1, canvasPx: 49, quietPx: 4 });
    expect(planQrRender(41, -100)).toEqual({ modulePx: 1, canvasPx: 49, quietPx: 4 });
    expect(planQrRender(41, Number.NaN)).toEqual({ modulePx: 1, canvasPx: 49, quietPx: 4 });
  });

  it("방어: 여백이 음수·NaN이면 기본 4모듈로 되돌린다", () => {
    expect(planQrRender(41, 640, -3)).toEqual(planQrRender(41, 640));
    expect(planQrRender(41, 640, Number.NaN)).toEqual(planQrRender(41, 640));
  });
});

describe("exportFileName — P1 다운로드 페이지와 같은 규칙 (web-it17 §6)", () => {
  const SESSION = "20260730_143022_a1b2c3d4-5e6f-4708-9a0b-1c2d3e4f5a6b";

  it("사진은 스탬프 + 확장자다", () => {
    expect(exportFileName(SESSION, "final", "Jpg")).toBe("MCPhoto_20260730_143022.jpg");
    expect(exportFileName(SESSION, "final", "Png")).toBe("MCPhoto_20260730_143022.png");
  });

  it("타임랩스는 포맷과 무관하게 _timelapse.mp4다", () => {
    expect(exportFileName(SESSION, "timelapse", "Jpg")).toBe(
      "MCPhoto_20260730_143022_timelapse.mp4",
    );
    expect(exportFileName(SESSION, "timelapse", "Png")).toBe(
      "MCPhoto_20260730_143022_timelapse.mp4",
    );
  });

  it("세션 ID가 없거나 형식 위반이면 접두만 쓴다", () => {
    expect(exportFileName(null, "final", "Jpg")).toBe("MCPhoto.jpg");
    expect(exportFileName(null, "timelapse", "Jpg")).toBe("MCPhoto_timelapse.mp4");
    expect(exportFileName("20260730_143022", "final", "Jpg")).toBe("MCPhoto.jpg");
    expect(exportFileName("", "final", "Png")).toBe("MCPhoto.png");
  });

  it("⚠️ UUID를 파일명에 넣지 않는다(링크 유출 방지)", () => {
    for (const kind of ["final", "timelapse"] as const) {
      const name = exportFileName(SESSION, kind, "Jpg");
      // UUID는 하이픈을 포함한다 — 하이픈이 없다는 것이 UUID 미포함의 기계적 증거다.
      expect(name).not.toContain("-");
      expect(name).not.toContain("a1b2c3d4");
      expect(name.startsWith(EXPORT_FILE_PREFIX)).toBe(true);
    }
  });
});

describe("filterParams — BT.601(CSS filter 금지)", () => {
  it("계수 합이 1이고 OpenCV BGR2GRAY와 같다", () => {
    expect(BT601.r).toBe(0.299);
    expect(BT601.g).toBe(0.587);
    expect(BT601.b).toBe(0.114);
    expect(BT601.r + BT601.g + BT601.b).toBeCloseTo(1, 10);
  });

  it("회색 계산이 흑·백·중간을 보존한다", () => {
    expect(bt601Gray(0, 0, 0)).toBe(0);
    expect(bt601Gray(255, 255, 255)).toBeCloseTo(255, 6);
    expect(bt601Gray(255, 0, 0)).toBeCloseTo(76.245, 6);
  });

  it("CSS grayscale(Rec.709)와 계수가 다르다 — 그래서 직접 계산한다", () => {
    const rec709 = 0.2126 * 255;
    expect(Math.abs(bt601Gray(255, 0, 0) - rec709)).toBeGreaterThan(10);
  });

  it("밝게 필터는 alpha 1.1 / beta 20이고 255에서 포화한다", () => {
    expect(BRIGHTNESS_PARAMS).toEqual({ alpha: 1.1, beta: 20 });
    expect(convertScaleAbs(0, BRIGHTNESS_PARAMS)).toBe(20);
    expect(convertScaleAbs(100, BRIGHTNESS_PARAMS)).toBe(130);
    expect(convertScaleAbs(255, BRIGHTNESS_PARAMS)).toBe(255);
  });

  it("뷰티 파라미터 의도를 보존한다", () => {
    expect(BEAUTY_PARAMS.diameter).toBe(7);
    expect(BEAUTY_PARAMS.sigmaColor).toBe(40);
    expect(BEAUTY_PARAMS.sigmaSpace).toBe(7);
    expect(BEAUTY_PARAMS.smoothWeight).toBe(0.6);
    expect(BEAUTY_PARAMS.tone).toEqual({ alpha: 1.03, beta: 6 });
  });

  it("원본은 설정과 무관하게 항상 제공된다(it8 A6)", () => {
    expect(FILTER_KINDS).toEqual(["None", "Grayscale", "Brightness", "Beauty"]);
    expect(
      availableFilters({ FilterGrayscale: false, FilterBrightness: false, FilterBeauty: false }),
    ).toEqual(["None"]);
    expect(
      availableFilters({ FilterGrayscale: true, FilterBrightness: false, FilterBeauty: true }),
    ).toEqual(["None", "Grayscale", "Beauty"]);
    expect(
      availableFilters({ FilterGrayscale: true, FilterBrightness: true, FilterBeauty: true }),
    ).toEqual([...FILTER_KINDS]);
  });
});
