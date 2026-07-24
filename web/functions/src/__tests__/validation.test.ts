import {
  extToFormat,
  validateAccountId,
  validateFrameName,
  validateImageSize,
  validatePassword,
  validateRetentionHours,
  validateRole,
  validateSlots,
  validateUploadFile,
} from "../domain/validation";

describe("validation — 서버 입력 검증(경계 방어)", () => {
  test("validateAccountId: 형식/길이", () => {
    expect(validateAccountId("devmcjo").ok).toBe(true);
    expect(validateAccountId("a.b_c-1").ok).toBe(true);
    expect(validateAccountId("ab").ok).toBe(false); // 3자 미만
    expect(validateAccountId("has space").ok).toBe(false);
    expect(validateAccountId("한글계정").ok).toBe(false);
    expect(validateAccountId(123).ok).toBe(false);
    expect(validateAccountId("x".repeat(41)).ok).toBe(false);
  });

  test("validatePassword: 비어있음/과길이", () => {
    expect(validatePassword("1111").ok).toBe(true);
    expect(validatePassword("").ok).toBe(false);
    expect(validatePassword(null).ok).toBe(false);
    expect(validatePassword("x".repeat(201)).ok).toBe(false);
  });

  test("validateRole: 화이트리스트", () => {
    expect(validateRole("admin").ok).toBe(true);
    expect(validateRole("root").ok).toBe(false);
    expect(validateRole(undefined).ok).toBe(false);
  });

  test("validateRetentionHours: 정수 1~72", () => {
    expect(validateRetentionHours(24).ok).toBe(true);
    expect(validateRetentionHours(1).ok).toBe(true);
    expect(validateRetentionHours(72).ok).toBe(true);
    expect(validateRetentionHours(0).ok).toBe(false);
    expect(validateRetentionHours(73).ok).toBe(false);
    expect(validateRetentionHours(1.5).ok).toBe(false);
    expect(validateRetentionHours("24").ok).toBe(false);
  });

  test("validateSlots: 1~6개·양수 width/height", () => {
    const okSlots = [{ index: 0, x: 1, y: 2, width: 10, height: 20 }];
    const res = validateSlots(okSlots);
    expect(res.ok).toBe(true);
    if (res.ok) expect(res.value).toHaveLength(1);

    expect(validateSlots([]).ok).toBe(false); // 0개
    expect(validateSlots(new Array(7).fill({ index: 0, x: 0, y: 0, width: 1, height: 1 })).ok).toBe(false); // 7개
    expect(validateSlots([{ index: 0, x: 0, y: 0, width: 0, height: 1 }]).ok).toBe(false); // width 0
    expect(validateSlots([{ index: 0, x: -1, y: 0, width: 1, height: 1 }]).ok).toBe(false); // 음수
    expect(validateSlots("nope").ok).toBe(false);
  });

  test("validateImageSize: width/height>0", () => {
    expect(validateImageSize({ width: 100, height: 200 }).ok).toBe(true);
    expect(validateImageSize({ width: 0, height: 200 }).ok).toBe(false);
    expect(validateImageSize({ width: 100 }).ok).toBe(false);
    expect(validateImageSize(null).ok).toBe(false);
  });

  test("validateFrameName: 1~100자·'_' 금지", () => {
    expect(validateFrameName("여름프레임").ok).toBe(true);
    expect(validateFrameName("has_underscore").ok).toBe(false);
    expect(validateFrameName("").ok).toBe(false);
    expect(validateFrameName("x".repeat(101)).ok).toBe(false);
  });

  test("validateUploadFile: kind별 ext/contentType 정합", () => {
    expect(validateUploadFile({ kind: "final", ext: "jpg", contentType: "image/jpeg" }).ok).toBe(true);
    expect(validateUploadFile({ kind: "final", ext: "png", contentType: "image/png" }).ok).toBe(true);
    expect(validateUploadFile({ kind: "timelapse", ext: "mp4", contentType: "video/mp4" }).ok).toBe(true);

    // ext/kind 불일치
    expect(validateUploadFile({ kind: "final", ext: "mp4", contentType: "video/mp4" }).ok).toBe(false);
    // contentType 불일치
    expect(validateUploadFile({ kind: "final", ext: "jpg", contentType: "video/mp4" }).ok).toBe(false);
    // 알 수 없는 kind
    expect(validateUploadFile({ kind: "raw", ext: "jpg", contentType: "image/jpeg" }).ok).toBe(false);
  });

  test("extToFormat: png만 png, 그 외 jpg", () => {
    expect(extToFormat("png")).toBe("png");
    expect(extToFormat("jpg")).toBe("jpg");
  });
});
