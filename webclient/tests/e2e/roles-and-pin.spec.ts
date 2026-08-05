import type { Locator, Page } from "@playwright/test";
import { MAX_PIN_FAILS, PIN_COOLDOWN_MS } from "@domain/auth/pinGatePolicy";
import { roleLabel } from "@domain/roles/userRole";
import { PIN_LOCK_STORAGE_KEY } from "@adapters/storage/pinLockRepo";
import { formatCount, STRINGS } from "@ui/strings";
import { expect, test } from "./fixtures/app";
import { accountButton, fakeLogin } from "./fixtures/auth";
import { FLOW_LITERALS, gotoFrameSelect } from "./fixtures/capture";
import { USERS } from "./fixtures/users";

/**
 * 권한·PIN — E15 · E16 · E17 · E18
 *
 * 카메라가 필요 없는 화면 검증군이며 **WebKit에서도 돈다**(`@camera` 태그를 달지 않는다).
 *
 * ⚠️ PIN 계열의 401은 **불일치**이지 세션 만료가 아니다(`unauthorized: "reject"` — PIN-2).
 *    1회 오입력에 로그아웃되면 안 된다(E17).
 * ⚠️ 리로드하면 메모리 전용 JWT가 사라져 **게스트**가 되고, 게스트는 PIN 게이트를 지나지 않는다.
 *    잠금 지속을 보려면 리로드 **후 재로그인**해야 한다(E16).
 * ⚠️ spec 어디에도 실제 PIN 값의 의미를 두지 않는다 — 목이 성패를 정한다.
 */

const PIN = "1357";

/** 열려 있는 모달 안에서 PIN을 눌러 제출한다. */
async function enterPin(page: Page, pin = PIN): Promise<void> {
  const dialog = page.getByRole("dialog");
  for (const digit of pin) {
    await dialog.getByRole("button", { name: digit, exact: true }).click();
  }
  await dialog.getByRole("button", { name: STRINGS.pin.confirm, exact: true }).click();
}

/** 상단바 [설정] → PIN 게이트. */
async function openSettings(page: Page): Promise<void> {
  await page.getByRole("button", { name: STRINGS.common.settings, exact: true }).click();
}

/** 계정 팝오버에서 항목 하나를 고른다. */
async function pickAccountMenu(page: Page, userId: string, label: string): Promise<void> {
  await accountButton(page, userId).click();
  await page.getByRole("menuitem", { name: label, exact: true }).click();
}

/** 사용자 관리 표(넓은 화면)에서 계정 id의 행. */
function tableRow(page: Page, id: string): Locator {
  return page.locator("table tbody tr").filter({ hasText: id });
}

/** 사용자 관리 카드(좁은 화면)에서 계정 id의 항목. */
function cardRow(page: Page, id: string): Locator {
  return page.locator("ul li").filter({ hasText: id });
}

test.describe("권한·PIN", () => {
  test.beforeEach(async ({ app }) => {
    app.backend.setFrames([]);
    await app.seedSettings();
    await app.goto();
  });

  test("E15 — 프레임 저작 권한이 없는 계정에는 [프레임 만들기]가 없다(M10)", async ({
    page,
    app,
  }) => {
    await fakeLogin(page, app.backend, USERS.user, { token: "e2e-jwt-role-user" });
    await gotoFrameSelect(page);

    await expect(page.getByRole("button", { name: FLOW_LITERALS.frameSelectCreate })).toHaveCount(0);
    await expect(
      page.getByRole("button", { name: FLOW_LITERALS.frameSelectEditSelected }),
    ).toHaveCount(0);

    // ⚠️ "액션 함수를 직접 호출해도 거부된다"(M10의 2중 가드 중 뒷단)는 **내부 함수 호출**이라
    //    E2E 범위 밖이다 — `frameSelectActions` 단위 테스트가 이미 고정한다(설계 §9).
  });

  test("E17 — PIN을 한 번 틀려도 로그아웃되지 않는다(PIN-2)", async ({ page, app }) => {
    await fakeLogin(page, app.backend, USERS.manager, { token: "e2e-jwt-pin1" });
    app.backend.fail("accounts/me/pin/verify", 401);
    app.allowConsoleError(/status of 401/);
    app.allowConsoleError(/백엔드 오류 응답/);

    await openSettings(page);
    await expect(page.getByText(STRINGS.pin.titleVerify)).toBeVisible();

    await enterPin(page);

    // 불일치 문구 + 실패 횟수. 모달은 그대로 열려 있다.
    await expect(
      page.getByText(
        `${STRINGS.pin.messages.mismatch} ${formatCount(STRINGS.pin.failCount, 1)}`,
      ),
    ).toBeVisible();
    await expect(page.getByText(STRINGS.pin.titleVerify)).toBeVisible();

    // ★ 세션이 살아 있다 — 401을 "만료"로 오해하지 않는다.
    await page.getByRole("button", { name: STRINGS.common.close, exact: true }).click();
    await expect(accountButton(page, USERS.manager.id)).toBeVisible();
  });

  test("E16 — 5회 실패하면 기기가 잠기고 재로그인 후에도 잠금이 유지된다", async ({
    page,
    app,
  }) => {
    await fakeLogin(page, app.backend, USERS.manager, { token: "e2e-jwt-pin5" });
    app.backend.fail("accounts/me/pin/verify", 401);
    app.allowConsoleError(/status of 401/);
    app.allowConsoleError(/백엔드 오류 응답/);
    app.allowConsoleError(/PIN 게이트 거부/);

    await openSettings(page);
    await expect(page.getByText(STRINGS.pin.titleVerify)).toBeVisible();

    for (let attempt = 1; attempt <= MAX_PIN_FAILS; attempt++) {
      // 불일치마다 쿨다운(1.5초) 동안 키가 비활성이다 — 살아날 때까지 기다린다.
      await expect(
        page.getByRole("dialog").getByRole("button", { name: "1", exact: true }),
      ).toBeEnabled({ timeout: PIN_COOLDOWN_MS * 4 });
      await enterPin(page);
    }

    // 5회째 → 모달이 닫히고 기기 잠금이 기록된다.
    await expect(page.getByRole("dialog")).toHaveCount(0);
    const locked = await page.evaluate(
      (key: string) => window.localStorage.getItem(key),
      PIN_LOCK_STORAGE_KEY,
    );
    expect(locked).not.toBeNull();

    // ★ 리로드하면 게스트가 된다(JWT는 메모리 전용) → 재로그인해야 게이트가 다시 판정된다.
    await page.reload();
    await fakeLogin(page, app.backend, USERS.manager, { token: "e2e-jwt-pin5b" });
    app.backend.clearCalls();

    await openSettings(page);
    // 잠금 중에는 **모달을 열지 않는다**(WD16) — 안내만 뜬다.
    await expect(page.getByText(/PIN 입력이 일시적으로 차단되었습니다/)).toBeVisible();
    await expect(page.getByText(STRINGS.pin.titleVerify)).toHaveCount(0);
    // 잠금 중에는 서버에 물어보지도 않는다.
    expect(app.backend.callsTo("accounts/me/pin")).toEqual([]);
  });

  test("E18 — 사용자 관리 행 액션이 역할 매트릭스와 일치한다(좁은 화면 카드도 동일)", async ({
    page,
    app,
  }) => {
    await fakeLogin(page, app.backend, USERS.manager, { token: "e2e-jwt-mgr" });

    await pickAccountMenu(page, USERS.manager.id, STRINGS.account.adminTitle);
    await expect(page.getByText(STRINGS.pin.titleVerify)).toBeVisible();
    await enterPin(page);

    await expect(page.getByRole("heading", { name: STRINGS.account.title })).toBeVisible();
    await page.getByRole("button", { name: STRINGS.account.openUserMgmt, exact: true }).click();
    await expect(page.getByRole("heading", { name: STRINGS.userMgmt.title })).toBeVisible();

    // ── 넓은 화면(표) ──
    const otherManager = tableRow(page, "e2e-other-manager");
    await expect(otherManager).toBeVisible();
    // manager → manager: [삭제]는 있고 [PIN]은 **없다**(비대칭이 규격이다).
    await expect(otherManager.getByRole("button", { name: STRINGS.common.delete })).toBeVisible();
    await expect(otherManager.getByRole("button", { name: STRINGS.userMgmt.resetPin })).toHaveCount(
      0,
    );
    // 동급에게는 역할 콤보도 없다.
    await expect(otherManager.locator("select")).toHaveCount(0);

    // 하위 대역 행의 콤보에는 admin·manager가 없다.
    const userRow = tableRow(page, USERS.user.id);
    const options = await userRow.locator("select option").allInnerTexts();
    expect(options).toEqual([
      roleLabel("temp_user"),
      roleLabel("user"),
      roleLabel("advanced_user"),
    ]);
    expect(options).not.toContain(roleLabel("admin"));

    // admin 행은 아무 것도 할 수 없다.
    const adminRow = tableRow(page, USERS.admin.id);
    await expect(adminRow.getByRole("button", { name: STRINGS.common.delete })).toHaveCount(0);
    await expect(adminRow.locator("select")).toHaveCount(0);

    // 자기 행에는 액션이 없다.
    const selfRow = tableRow(page, USERS.manager.id).first();
    await expect(selfRow.getByRole("button", { name: STRINGS.common.delete })).toHaveCount(0);
    await expect(selfRow.locator("select")).toHaveCount(0);

    // ── 좁은 화면(카드) — **같은 판정**이어야 한다 ──
    await page.setViewportSize({ width: 480, height: 900 });
    const otherManagerCard = cardRow(page, "e2e-other-manager");
    await expect(otherManagerCard).toBeVisible();
    await expect(
      otherManagerCard.getByRole("button", { name: STRINGS.common.delete }),
    ).toBeVisible();
    await expect(
      otherManagerCard.getByRole("button", { name: STRINGS.userMgmt.resetPin }),
    ).toHaveCount(0);
    await expect(cardRow(page, USERS.manager.id).first().locator("select")).toHaveCount(0);
  });

  test("E18 변형 — 목록 조회 실패를 빈 목록으로 위장하지 않는다", async ({ page, app }) => {
    await fakeLogin(page, app.backend, USERS.manager, { token: "e2e-jwt-mgr-fail" });
    app.backend.fail("accounts", 500);
    app.allowConsoleError(/status of 500/);
    app.allowConsoleError(/백엔드 오류 응답/);

    await pickAccountMenu(page, USERS.manager.id, STRINGS.account.adminTitle);
    await enterPin(page);
    await page.getByRole("button", { name: STRINGS.account.openUserMgmt, exact: true }).click();

    await expect(page.getByText(STRINGS.userMgmt.loadFailed)).toBeVisible();
    await expect(page.getByText(STRINGS.userMgmt.empty)).toHaveCount(0);
    await expect(page.getByRole("button", { name: STRINGS.common.retry })).toBeVisible();
  });
});
