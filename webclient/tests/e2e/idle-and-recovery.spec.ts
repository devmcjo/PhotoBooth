import { IDLE_COUNTDOWN_MS, IDLE_TIMEOUT_MS } from "@shell/idleWatchdog";
import { STRINGS } from "@ui/strings";
import { expect, test } from "./fixtures/app";
import { accountButton, fakeLogin } from "./fixtures/auth";
import { gotoFrameSelect } from "./fixtures/capture";
import { USERS } from "./fixtures/users";

/**
 * 유휴·복구 — E5 · E14
 *
 * 둘 다 **카메라가 필요 없다**(WebKit에서도 돈다).
 * · E5: 유휴 감시 대상 6화면에 `FrameSelect`가 포함돼 있어 카메라 없이 검증할 수 있다.
 * · 값(120초 + 10초)은 `idleCountdown` 단위 테스트가 이미 고정한다 — 여기서 보는 것은
 *   **화면·모달·홈 복귀가 실제로 연결되는가**다(설계 §4.6).
 *
 * ⚠️ `page.clock`은 **이 파일에서만** 쓴다. 촬영 시퀀스에 시간 조작을 섞으면 검증 대상이 아니라
 *    하네스를 시험하게 된다.
 */

test.describe("유휴·복구", () => {
  test.beforeEach(async ({ app }) => {
    app.backend.setFrames([]);
    await app.seedSettings();
  });

  test("E5 — 유휴 상한을 넘기면 경고 뒤 홈으로 돌아가되 로그아웃하지 않는다(M3)", async ({
    page,
    app,
  }) => {
    // ⚠️ `goto` 전에 설치해야 앱이 처음부터 가짜 시계를 본다(판정 시계는 `performance.now()`다).
    await page.clock.install();
    await app.goto();
    await fakeLogin(page, app.backend, USERS.user, { token: "e2e-jwt-idle" });

    await gotoFrameSelect(page);
    await expect(page.getByText(STRINGS.idle.title)).toHaveCount(0);

    // 120초 + 여유 → 경고 모달.
    await page.clock.runFor(IDLE_TIMEOUT_MS + 5_000);
    await expect(page.getByText(STRINGS.idle.title)).toBeVisible();
    await expect(page.getByRole("button", { name: STRINGS.idle.continue })).toBeVisible();

    // 10초 카운트다운 만료 → 홈.
    await page.clock.runFor(IDLE_COUNTDOWN_MS + 1_000);
    await expect(page.getByRole("button", { name: STRINGS.home.start, exact: true })).toBeVisible();
    await expect(page.getByText(STRINGS.idle.title)).toHaveCount(0);

    // ★ M3 — 만료는 홈 복귀일 뿐 로그아웃이 아니다.
    await expect(accountButton(page, USERS.user.id)).toBeVisible();
  });

  test("E5 — 경고에서 [이어서 진행하기]를 누르면 화면을 유지한다", async ({ page, app }) => {
    await page.clock.install();
    await app.goto();
    await gotoFrameSelect(page);

    await page.clock.runFor(IDLE_TIMEOUT_MS + 5_000);
    await expect(page.getByText(STRINGS.idle.title)).toBeVisible();

    await page.getByRole("button", { name: STRINGS.idle.continue }).click();
    await expect(page.getByText(STRINGS.idle.title)).toHaveCount(0);
    // 카운트다운이 지나도 홈으로 가지 않는다(타이머가 재시작됐다).
    await page.clock.runFor(IDLE_COUNTDOWN_MS + 1_000);
    await expect(page.getByRole("heading", { name: "프레임 선택" })).toBeVisible();
  });

  test("E14 — 미처리 예외가 나도 화이트스크린 대신 홈 + 토스트로 복구하고 로그인을 유지한다(M16)", async ({
    page,
    app,
  }) => {
    await app.goto();
    await fakeLogin(page, app.backend, USERS.user, { token: "e2e-jwt-recover" });
    await gotoFrameSelect(page);

    // 주입한 예외는 브라우저가 `pageerror`로도 보고한다 — 이 spec에서는 의도된 것이다.
    app.allowConsoleError(/pageerror: E2E 주입 예외/);
    app.allowConsoleError(/Uncaught Error: E2E 주입 예외/);
    app.allowConsoleError(/처리되지 않은 예외/);

    await page.evaluate(() => {
      // 콜스택 밖에서 던져야 전역 `error` 핸들러가 받는다.
      window.setTimeout(() => {
        throw new Error("E2E 주입 예외");
      }, 0);
    });

    await expect(page.getByRole("button", { name: STRINGS.home.start, exact: true })).toBeVisible();
    await expect(page.getByText(STRINGS.error.temporary)).toBeVisible();
    // ★ 로그인은 유지된다(촬영 데이터만 폐기).
    await expect(accountButton(page, USERS.user.id)).toBeVisible();
  });
});
