import type { Page } from "@playwright/test";
import { STRINGS } from "@ui/strings";
import { expect, test } from "./fixtures/app";
import { accountButton, fakeLogin } from "./fixtures/auth";
import { FLOW_LITERALS } from "./fixtures/capture";
import { USERS } from "./fixtures/users";

/**
 * 문구 카탈로그 — E22
 *
 * 검사하는 것: **화면에 실제로 그려진 문자열이 `@ui/strings`의 값과 정확히 같은가.**
 * 컴포넌트에 문구를 하드코딩해 카탈로그를 우회하면 여기서 깨진다.
 *
 * ⚠️ 카탈로그 ↔ `analysis/13 §14` 규격 문서 대조는 **사람 검토**다(설계 §7 E22 "부분").
 *    자동화가 확인하는 것은 "코드가 카탈로그를 실제로 쓰는가"까지다.
 * ⚠️ 카메라가 필요 없는 6화면만 본다(WebKit에서도 돈다).
 */

/** 화면에 이 문구들이 **정확히 그대로** 보이는가. */
async function expectVisibleTexts(page: Page, texts: readonly string[]): Promise<void> {
  for (const text of texts) {
    await expect(page.getByText(text, { exact: true }).first()).toBeVisible();
  }
}

const PIN = "2468";

async function enterPin(page: Page): Promise<void> {
  const dialog = page.getByRole("dialog");
  for (const digit of PIN) {
    await dialog.getByRole("button", { name: digit, exact: true }).click();
  }
  await dialog.getByRole("button", { name: STRINGS.pin.confirm, exact: true }).click();
}

test.describe("문구 카탈로그", () => {
  test.beforeEach(async ({ app }) => {
    app.backend.setFrames([]);
    await app.seedSettings();
    await app.goto();
  });

  test("E22 — Home · Login · FrameSelect의 문구가 카탈로그와 일치한다", async ({ page }) => {
    // ① Home
    await expectVisibleTexts(page, [
      STRINGS.home.start,
      STRINGS.common.settings,
      STRINGS.common.login,
    ]);

    // ② Login
    await accountButton(page, STRINGS.common.login).click();
    await expectVisibleTexts(page, [
      STRINGS.login.title,
      STRINGS.login.google,
      STRINGS.common.close,
    ]);

    // ③ FrameSelect
    await page.getByRole("button", { name: STRINGS.common.close, exact: true }).click();
    await page.getByRole("button", { name: STRINGS.home.start, exact: true }).click();
    await expectVisibleTexts(page, [STRINGS.common.cancel, STRINGS.common.next]);
  });

  test("E22 — Settings(게스트) 문구가 카탈로그와 일치한다", async ({ page }) => {
    await page.getByRole("button", { name: STRINGS.common.settings, exact: true }).click();

    await expectVisibleTexts(page, [
      STRINGS.settings.title,
      STRINGS.settings.guestBanner,
      STRINGS.settings.sections.capture,
      STRINGS.settings.sections.output,
      STRINGS.settings.cutCount,
      STRINGS.settings.countdown,
      STRINGS.settings.enableQrDelivery,
    ]);
  });

  test("E22 — Account · UserMgmt 문구가 카탈로그와 일치한다", async ({ page, app }) => {
    await fakeLogin(page, app.backend, USERS.manager, { token: "e2e-jwt-strings" });

    await accountButton(page, USERS.manager.id).click();
    await page
      .getByRole("menuitem", { name: STRINGS.account.adminTitle, exact: true })
      .click();
    await enterPin(page);

    // ④ Account(관리자 도구 탭)
    await expectVisibleTexts(page, [
      STRINGS.account.title,
      STRINGS.account.tabInfo,
      STRINGS.account.tabAdmin,
      STRINGS.account.openUserMgmt,
    ]);

    // ⑤ UserMgmt
    await page.getByRole("button", { name: STRINGS.account.openUserMgmt, exact: true }).click();
    await expectVisibleTexts(page, [
      STRINGS.userMgmt.title,
      STRINGS.userMgmt.colId,
      STRINGS.userMgmt.colEmail,
      STRINGS.userMgmt.colCreatedAt,
      STRINGS.userMgmt.colActions,
      STRINGS.userMgmt.back,
    ]);
  });

  test("E22 — 진단 모달 문구가 카탈로그와 일치한다", async ({ page, app }) => {
    // ⑥ 진단·상태는 **로그인 전용**이다(03 §15.2 — 게스트에게는 버튼 자체가 없다).
    await page.getByRole("button", { name: STRINGS.common.settings, exact: true }).click();
    await expect(page.getByText(STRINGS.settings.guestBanner)).toBeVisible();
    await expect(
      page.getByRole("button", { name: STRINGS.diagnostics.open, exact: true }),
    ).toHaveCount(0);

    // 로그인 후 다시 들어간다(Settings는 자기 그룹이라 매번 PIN을 묻는다).
    await page.getByRole("button", { name: STRINGS.common.close, exact: true }).click();
    await fakeLogin(page, app.backend, USERS.manager, { token: "e2e-jwt-diag" });
    await page.getByRole("button", { name: STRINGS.common.settings, exact: true }).click();
    await enterPin(page);

    await page.getByRole("button", { name: STRINGS.diagnostics.open, exact: true }).click();

    await expectVisibleTexts(page, [
      STRINGS.diagnostics.title,
      STRINGS.diagnostics.sections.camera,
      STRINGS.diagnostics.sections.server,
      STRINGS.diagnostics.sections.app,
      STRINGS.diagnostics.recheck,
    ]);
  });

  test("E22 — 카탈로그 밖 리터럴 3곳이 여전히 그 값 그대로다", async ({ page }) => {
    // ⚠️ 흐름 화면의 문구 중 **3곳은 `@ui/strings`에 없는 리터럴**이다
    //    (`FlowViews.tsx`의 Guide [촬영 시작] · Capture [바로 촬영] · CutSelect `aria-label`).
    //    카탈로그로 옮기는 것은 이 Step의 범위가 아니므로(§17 `src/**` 무변경),
    //    **하네스 상수와 소스가 어긋나지 않는다는 사실만** 여기에 고정해 둔다.
    //    Home의 [촬영 시작]은 카탈로그(`STRINGS.home.start`)이고 Guide의 것은 리터럴인데
    //    글자가 같다 — 한쪽만 바뀌면 이 단언이 알려 준다.
    expect(FLOW_LITERALS.guideStart).toBe(STRINGS.home.start);
    await expect(
      page.getByRole("button", { name: FLOW_LITERALS.guideStart, exact: true }),
    ).toBeVisible();
  });
});
