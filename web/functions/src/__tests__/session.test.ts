import {
  computeExpiresAt,
  downloadPageUrl,
  finalImagePath,
  isValidSessionId,
  newSessionId,
  stampPrefix,
  timelapsePath,
  tokenDownloadUrl,
} from "../domain/session";

describe("session — UploadContract(C#) 이식 정합", () => {
  test("stampPrefix: yyyyMMdd_HHmmss (UTC)", () => {
    const d = new Date(Date.UTC(2026, 6, 24, 9, 5, 3)); // 2026-07-24 09:05:03Z
    expect(stampPrefix(d)).toBe("20260724_090503");
  });

  test("newSessionId: {prefix}_{uuidv4} 형식이며 형식검증 통과", () => {
    const d = new Date(Date.UTC(2026, 0, 2, 3, 4, 5));
    const id = newSessionId(d);
    expect(id.startsWith("20260102_030405_")).toBe(true);
    expect(isValidSessionId(id)).toBe(true);
  });

  test("newSessionId: 매 호출 고유(UUID 부분)", () => {
    const a = newSessionId();
    const b = newSessionId();
    expect(a).not.toBe(b);
  });

  test("isValidSessionId: 순차/이상 ID 거부(열거 방어)", () => {
    expect(isValidSessionId("123")).toBe(false);
    expect(isValidSessionId("20260724_090503_not-a-uuid")).toBe(false);
    expect(isValidSessionId("20260724_090503")).toBe(false);
    expect(isValidSessionId("../etc/passwd")).toBe(false);
    expect(
      isValidSessionId("20260724_090503_11111111-1111-4111-8111-111111111111")
    ).toBe(true);
    expect(isValidSessionId(42)).toBe(false);
  });

  test("finalImagePath: results/{sid}/final.{ext}", () => {
    const sid = "20260724_090503_11111111-1111-4111-8111-111111111111";
    expect(finalImagePath(sid, "jpg")).toBe(`results/${sid}/final.jpg`);
    expect(finalImagePath(sid, "png")).toBe(`results/${sid}/final.png`);
  });

  test("timelapsePath: results/{sid}/timelapse.mp4", () => {
    const sid = "20260724_090503_11111111-1111-4111-8111-111111111111";
    expect(timelapsePath(sid)).toBe(`results/${sid}/timelapse.mp4`);
  });

  test("tokenDownloadUrl: 슬래시 %2F 인코딩·alt=media&token", () => {
    const url = tokenDownloadUrl(
      "mcphoto-955fb.firebasestorage.app",
      "results/abc/final.jpg",
      "tok-123"
    );
    expect(url).toBe(
      "https://firebasestorage.googleapis.com/v0/b/mcphoto-955fb.firebasestorage.app/o/results%2Fabc%2Ffinal.jpg?alt=media&token=tok-123"
    );
  });

  test("downloadPageUrl: 쿼리형 /?s={token}, 트레일링 슬래시 제거", () => {
    expect(downloadPageUrl("https://mcphoto-955fb.web.app", "T1")).toBe(
      "https://mcphoto-955fb.web.app/?s=T1"
    );
    expect(downloadPageUrl("https://mcphoto-955fb.web.app///", "T1")).toBe(
      "https://mcphoto-955fb.web.app/?s=T1"
    );
  });

  test("computeExpiresAt: createdAt + retentionHours", () => {
    const created = new Date(Date.UTC(2026, 6, 24, 0, 0, 0));
    const expires = computeExpiresAt(created, 24);
    expect(expires.getTime()).toBe(created.getTime() + 24 * 3600 * 1000);
  });
});
