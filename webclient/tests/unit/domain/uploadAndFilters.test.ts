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
