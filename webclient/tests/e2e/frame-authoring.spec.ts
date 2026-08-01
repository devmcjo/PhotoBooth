import type { Page } from "@playwright/test";
import { STRINGS } from "@ui/strings";
import { expect, test } from "./fixtures/app";
import { fakeLogin } from "./fixtures/auth";
import { FLOW_LITERALS } from "./fixtures/capture";
import { makePng } from "./fixtures/png";
import { USERS } from "./fixtures/users";

/**
 * 프레임 저작 — E13(M15: 이름의 `_`)
 *
 * ⚠️ `_` 판정은 **세 축**이고 축마다 결과가 다르다(`frameNaming.ts`):
 *    · 서버 등록 = `validateFrameNameForServer` → **하드 거부**
 *    · 로컬 저장 = `validateFrameName` + `underscoreWarning` → **비차단 경고**
 *    · 저장 전 선검증 = `isFileNameSafe` → `_`를 보지 않는다
 *    따라서 M15의 관측 지점은 **서버 등록 체크가 켜진 [저장]** 이다(설계 §7.4).
 *    "로컬 저장도 거부된다"로 쓰면 규격과 어긋난 테스트가 된다.
 */

const UNDERSCORE_NAME = "a_b";

/** manager로 로그인해 새 프레임 편집기까지 간다. */
async function openNewFrameEditor(page: Page): Promise<void> {
  await page.getByRole("button", { name: STRINGS.home.start, exact: true }).click();
  await expect(page.getByRole("heading", { name: "프레임 선택" })).toBeVisible();
  const create = page.getByRole("button", { name: FLOW_LITERALS.frameSelectCreate, exact: true });
  await expect(create).toBeEnabled();
  await create.click();
  await expect(page.getByRole("heading", { name: STRINGS.frameEditor.titleNew })).toBeVisible();
}

/** 편집기에 PNG를 주입하고 이름을 채운다. */
async function loadImageAndName(page: Page, name: string): Promise<void> {
  await page.locator('input[type="file"]').setInputFiles({
    name: "e2e-frame.png",
    mimeType: "image/png",
    buffer: makePng(),
  });
  // 이미지가 붙으면(디코드 → PNG 재인코딩 완료) 슬롯 컨트롤이 살아난다.
  const slotGroup = page.getByRole("group", { name: STRINGS.frameEditor.slotCount });
  await expect(slotGroup).toBeVisible();
  await expect(slotGroup.getByRole("button").first()).toBeEnabled({ timeout: 20_000 });

  await page.getByRole("textbox", { name: STRINGS.frameEditor.nameLabel }).fill(name);
}

test.describe("프레임 저작", () => {
  test.beforeEach(async ({ app, page }) => {
    app.backend.setFrames([]);
    await app.seedSettings();
    await app.goto();
    await fakeLogin(page, app.backend, USERS.manager, { token: "e2e-jwt-frames" });
  });

  test("E13 — 서버 등록 체크가 켜져 있으면 '_' 이름을 하드 거부하고 요청을 보내지 않는다(M15)", async ({
    page,
    app,
  }) => {
    await openNewFrameEditor(page);
    await loadImageAndName(page, UNDERSCORE_NAME);

    // 편집기 본문에는 **비차단 경고**만 뜬다(로컬 축).
    await expect(page.getByText(STRINGS.frames.underscoreWarning)).toBeVisible();

    await page.getByRole("button", { name: STRINGS.common.save, exact: true }).click();

    // 서버 등록 확인 오버레이 — 체크박스는 **기본 on**이다.
    const dialog = page.getByRole("dialog", { name: STRINGS.frameEditor.registerTitle });
    await expect(dialog).toBeVisible();
    const checkbox = dialog.getByRole("checkbox");
    await expect(checkbox).toBeChecked();
    // 체크가 켜진 상태에서는 하드 거부 문구를 **미리** 보여준다.
    await expect(dialog.getByText(STRINGS.frames.nameUnderscoreRejected)).toBeVisible();

    await dialog.getByRole("button", { name: STRINGS.common.save, exact: true }).click();

    // ★ 거부 문구가 뜨고 서버 요청이 0건이다(왕복 낭비·성공 오인 방지).
    await expect(page.getByText(STRINGS.frames.nameUnderscoreRejected).first()).toBeVisible();
    expect(app.backend.calls.filter((call) => call.path === "frames" && call.method === "POST")).toEqual(
      [],
    );
    // 편집기를 떠나지 않는다 — 이름을 고칠 수 있어야 한다.
    await expect(page.getByRole("heading", { name: STRINGS.frameEditor.titleNew })).toBeVisible();
  });

  // ⚠️ `@opfs-write` — 이 시나리오만 **실제 OPFS 쓰기**가 필요하다. Playwright WebKit
  //    18.2(Windows) 빌드에는 OPFS가 없어 로컬 저장이 항상 실패한다(설계 §6 · config 주석).
  test("E13 — 서버 등록 체크를 끄면 '_' 이름도 저장되고 경고만 남는다", { tag: "@opfs-write" }, async ({
    page,
    app,
  }) => {
    await openNewFrameEditor(page);
    await loadImageAndName(page, UNDERSCORE_NAME);

    await page.getByRole("button", { name: STRINGS.common.save, exact: true }).click();
    const dialog = page.getByRole("dialog", { name: STRINGS.frameEditor.registerTitle });
    await expect(dialog).toBeVisible();

    // 체크를 끄면 하드 거부 안내가 사라진다(그 축이 적용되지 않는다).
    await dialog.getByRole("checkbox").uncheck();
    await expect(dialog.getByText(STRINGS.frames.nameUnderscoreRejected)).toHaveCount(0);

    await dialog.getByRole("button", { name: STRINGS.common.save, exact: true }).click();

    // 저장이 성공해 목록으로 돌아온다.
    await expect(page.getByRole("heading", { name: "프레임 선택" })).toBeVisible({
      timeout: 30_000,
    });
    // 카드 버튼(= `aria-pressed`가 있는 쪽)이다. 형제인 삭제 ✕ 버튼과 구분한다.
    await expect(
      page.locator("main button[aria-pressed]").filter({ hasText: UNDERSCORE_NAME }),
    ).toBeVisible();
    // ★ 서버에는 아무것도 보내지 않았다.
    expect(app.backend.calls.filter((call) => call.path === "frames" && call.method === "POST")).toEqual(
      [],
    );
  });
});
