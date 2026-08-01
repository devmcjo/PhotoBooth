import { isValidSessionId } from "@domain/upload/uploadContract";
import { STRINGS } from "@ui/strings";
import { expect, test } from "./fixtures/app";
import { accountButton, fakeLogin, logout } from "./fixtures/auth";
import { clickResultNext, runCaptureToResult } from "./fixtures/capture";
import { blockedQrUsage, okQrUsage, type RecordedCall } from "./fixtures/backend";
import { USERS } from "./fixtures/users";

/**
 * 업로드·QR — Step 11 이월분 ② (E1b · E2 · E7 · E9 · E12 · E24)
 *
 * ⚠️ `putUrl`을 목이 **같은 오리진**으로 발급하므로 CORS preflight가 발생하지 않는다 →
 *    **`OPTIONS 204`는 이 spec의 검증 대상이 아니다**(설계 §4.2). 실왕복은 실측 V20-1이 소유한다.
 * ⚠️ dev 서버에서는 `<StrictMode>`가 `useUploadRun`의 effect를 2회 돌려 **prepare만 2건**이
 *    관측된다(첫 실행은 cleanup에서 abort된다). PUT·commit은 1건씩임을 E2가 고정한다.
 */

/** 이 spec이 P1 다운로드 페이지 도메인으로 쓰는 값(설정에 심는다). */
const HOSTING_BASE = "https://p1.e2e.invalid/download";

const TOKEN = "e2e-jwt-upload";

function kindOf(call: RecordedCall): "prepare" | "put" | "commit" | "other" {
  if (call.path === "uploads/prepare") return "prepare";
  if (call.path === "uploads/commit") return "commit";
  if (call.method === "PUT" && call.path.startsWith("__mock-storage/")) return "put";
  return "other";
}

test.describe("업로드·QR", () => {
  test.beforeEach(async ({ app }) => {
    app.backend.setFrames([]);
  });

  test(
    "E1b · E2 · E12 — 로그인 완주 → prepare→PUT→commit → QR 렌더 → Done",
    { tag: "@camera" },
    async ({ page, app }) => {
      await app.seedSettings({ HostingBaseUrl: HOSTING_BASE });
      await app.goto();
      await fakeLogin(page, app.backend, USERS.user, { token: TOKEN });
      app.backend.clearCalls();

      await runCaptureToResult(page);
      await clickResultNext(page);

      // ★ E1b — 업로드가 끝나면 QR canvas가 렌더된다(M5: 성공 후에만).
      await expect(page.getByRole("img", { name: STRINGS.upload.qrAltText })).toBeVisible({
        timeout: 30_000,
      });
      await expect(page.getByText(/자동 삭제됩니다/)).toBeVisible();

      // ★ E2 — 3단계 순서.
      const sequence = app.backend.calls.map(kindOf).filter((kind) => kind !== "other");
      expect(sequence.filter((kind) => kind === "put")).toHaveLength(1);
      expect(sequence.filter((kind) => kind === "commit")).toHaveLength(1);
      expect(sequence.indexOf("prepare")).toBeGreaterThanOrEqual(0);
      expect(sequence.indexOf("prepare")).toBeLessThan(sequence.indexOf("put"));
      expect(sequence.indexOf("put")).toBeLessThan(sequence.indexOf("commit"));

      // ★ E2 — 서명 PUT은 `requiredHeaders`를 **전량** 붙이고 자격 증명은 붙이지 않는다(M14).
      const put = app.backend.calls.find((call) => kindOf(call) === "put");
      expect(put).toBeDefined();
      expect(put?.headers["content-type"]).toBe("image/jpeg");
      expect(put?.headers["x-goog-meta-firebasestoragedownloadtokens"]).toBe("e2e-storage-token");
      expect(put?.headers.authorization).toBeUndefined();
      expect(put?.headers["x-mcphoto-client"]).toBeUndefined();
      expect(put?.bodyBytes).toBeGreaterThan(0);

      // ★ E2 — commit의 `downloadPageUrl`은 **P1 다운로드 페이지 도메인**이다(키오스크 오리진이 아니다).
      const commit = app.backend.calls.find((call) => kindOf(call) === "commit");
      const commitBody = commit?.bodyJson as Record<string, unknown> | undefined;
      expect(String(commitBody?.downloadPageUrl)).toMatch(
        new RegExp(`^${HOSTING_BASE.replace(/[.*+?^${}()|[\]\\]/g, "\\$&")}/\\?s=`),
      );
      expect(String(commitBody?.downloadPageUrl)).not.toContain("localhost:5173");

      // ★ E12 — 세션 ID 형식은 **도메인 함수로** 판정한다(정규식을 여기서 다시 쓰지 않는다).
      const prepareBody = app.backend
        .callsTo("uploads/prepare")
        .map((call) => (call.bodyJson as Record<string, unknown>).sessionId);
      expect(prepareBody.length).toBeGreaterThan(0);
      for (const sessionId of prepareBody) {
        expect(typeof sessionId).toBe("string");
        expect(isValidSessionId(String(sessionId))).toBe(true);
      }
      // commit이 prepare와 **같은** 세션 ID를 쓴다.
      expect(commitBody?.sessionId).toBe(prepareBody[0]);

      // ★ E1b — [완료] → Done.
      await page.getByRole("button", { name: STRINGS.common.done, exact: true }).click();
      await expect(page.getByText(STRINGS.done.thanks)).toBeVisible();
    },
  );

  test(
    "E7 — prepare가 실패하면 QR을 노출하지 않고 사유만 보여준다(M5·M8)",
    { tag: "@camera" },
    async ({ page, app }) => {
      await app.seedSettings();
      await app.goto();
      await fakeLogin(page, app.backend, USERS.user, { token: TOKEN });
      app.backend.clearCalls();
      // 목 실패는 브라우저 콘솔에 500 리소스 오류를 남긴다 — 이 spec에서만 통과시킨다.
      app.backend.fail("uploads/prepare", 500);
      app.allowConsoleError(/status of 500/);
      app.allowConsoleError(/백엔드 오류 응답/);
      app.allowConsoleError(/업로드 실패/);

      await runCaptureToResult(page);
      await clickResultNext(page);

      // 실패 사유가 뜨고 QR은 없다. 로컬 저장이 켜져 있으므로 "기기에 저장되었습니다" 문구다.
      await expect(page.getByText(STRINGS.upload.failedSaved)).toBeVisible({ timeout: 30_000 });
      await expect(page.getByRole("img", { name: STRINGS.upload.qrAltText })).toHaveCount(0);

      // ★ M8 — PUT도 commit도 호출되지 않는다.
      expect(app.backend.callsTo("uploads/commit")).toEqual([]);
      expect(app.backend.callsTo("__mock-storage/")).toEqual([]);

      // 손님이 갇히지 않는다 — [완료]가 살아 있다.
      const done = page.getByRole("button", { name: STRINGS.common.done, exact: true });
      await expect(done).toBeEnabled();
      await done.click();
      await expect(page.getByText(STRINGS.done.thanks)).toBeVisible();
    },
  );

  test(
    "E9 — 사진·타임랩스 전송이 둘 다 off면 QR 자체가 꺼지고 요청이 0건이다(M7)",
    { tag: "@camera" },
    async ({ page, app }) => {
      // ⚠️ 설계 §7의 E9는 `Qr` 화면의 "전송할 결과물이 없습니다."까지 보려 했지만,
      //    `normalizeQrToggles`가 **두 토글이 모두 off면 `EnableQrDelivery`를 off로 정규화**한다
      //    → `Qr`에 애초에 도달하지 않는다. 그 문구가 뜨는 상태(결과물 자체가 없는 `Qr` 진입)는
      //    설정만으로 만들 수 없고, `runUpload`의 `nothing` 분기는
      //    `tests/unit/screens/uploadRunner.test.ts`가 이미 요청 0건까지 고정하고 있다(설계 §9).
      //    그래서 여기서는 **브라우저에서만 확인할 수 있는 것** — 정규화의 최종 효과를 본다.
      await app.seedSettings({ SendPhoto: false, SendTimelapse: false, EnableQrDelivery: true });
      await app.goto();
      await fakeLogin(page, app.backend, USERS.user, { token: TOKEN });
      app.backend.clearCalls();

      await runCaptureToResult(page);
      await clickResultNext(page);

      await expect(page.getByText(STRINGS.done.thanks)).toBeVisible();
      expect(app.backend.callsTo("uploads/")).toEqual([]);
      expect(app.backend.callsTo("__mock-storage/")).toEqual([]);
    },
  );

  test(
    "E24 — TempUser 한도 초과면 Done으로 끝나고, 해제 후 재로그인하면 Qr에 진입한다",
    { tag: "@camera" },
    async ({ page, app }) => {
      await app.seedSettings();
      await app.goto();

      app.backend.setQrUsage(blockedQrUsage("count"));
      await fakeLogin(page, app.backend, USERS.tempUser, { token: TOKEN });
      // 한도 조회는 temp_user에게만 나간다(비TempUser에게는 요청 0건 — `qrUsageStore`).
      await expect
        .poll(() => app.backend.callsTo("accounts/me/qr-usage").length)
        .toBeGreaterThan(0);
      app.backend.clearCalls();

      await runCaptureToResult(page);
      await clickResultNext(page);

      // 한도 초과 → `Qr`을 건너뛰고 `Done`. 업로드 요청은 0건이다.
      await expect(page.getByText(STRINGS.done.thanks)).toBeVisible();
      expect(app.backend.callsTo("uploads/")).toEqual([]);

      // 저장된 `EnableQrDelivery`는 **바뀌지 않는다**(런타임 오버라이드일 뿐이다).
      const stored = await page.evaluate(() =>
        window.localStorage.getItem("mcphoto.settings.v1"),
      );
      const parsed = JSON.parse(stored ?? "{}") as { values?: Record<string, unknown> };
      expect(parsed.values?.EnableQrDelivery).toBe(true);

      // 한도를 풀고 재로그인하면(계정 변경 → 캐시 재조회) 곧바로 `Qr`에 진입한다.
      await page.getByRole("button", { name: STRINGS.done.goHome, exact: true }).click();
      await expect(accountButton(page, USERS.tempUser.id)).toBeVisible();
      await logout(page, USERS.tempUser);
      app.backend.setQrUsage(okQrUsage("temp_user"));
      await fakeLogin(page, app.backend, USERS.tempUser, { token: TOKEN });
      app.backend.clearCalls();

      await runCaptureToResult(page);
      await clickResultNext(page);
      await expect(page.getByRole("img", { name: STRINGS.upload.qrAltText })).toBeVisible({
        timeout: 30_000,
      });
      expect(app.backend.callsTo("uploads/prepare").length).toBeGreaterThan(0);
    },
  );
});
