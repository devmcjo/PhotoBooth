import { SETTINGS_STORAGE_KEY } from "@adapters/storage/settingsRepo";
import { STRINGS } from "@ui/strings";
import { expect, test } from "./fixtures/app";
import {
  DEFAULT_CUT_COUNT,
  FLOW_LITERALS,
  captureProgress,
  clickResultNext,
  cutCards,
  enterCapture,
  gotoFrameSelect,
  runCaptureToResult,
  selectFirstFrame,
  shootAllCuts,
  shootCuts,
} from "./fixtures/capture";
import { listOpfs } from "./fixtures/opfs";
import { emulateHidden, emulateVisible } from "./fixtures/visibility";

/**
 * 게스트 흐름 — Step 11 이월분 ① (E1 · E23 · E10 · E11 · E19 · E21)
 *
 * 이 파일의 축은 하나다: **게스트는 `Qr`에 도달하지 않고 `Done`으로 끝나며 업로드 요청이 0건이다**
 * (VF-11). 그리고 그 사실이 저장된 운영자 설정(`EnableQrDelivery`)을 **바꾸지 않는다**(E23).
 */

test.describe("게스트 촬영 흐름", { tag: "@camera" }, () => {
  test.beforeEach(async ({ app }) => {
    // 서버 프레임 목록을 비우면 코드 생성 fallback 프레임(슬롯 4개)으로 촬영할 수 있다.
    app.backend.setFrames([]);
    await app.seedSettings();
    await app.goto();
  });

  test("E1 · E23 — 게스트가 Done까지 완주하고 업로드 요청이 0건이며 QR 설정이 불변이다", async ({
    page,
    app,
  }) => {
    await runCaptureToResult(page);
    await clickResultNext(page);

    // effective QR 판정이 게스트에게 무조건 false다 → Qr을 건너뛰고 Done이다.
    await expect(page.getByText(STRINGS.done.thanks)).toBeVisible();
    await expect(page.getByRole("img", { name: STRINGS.upload.qrAltText })).toHaveCount(0);

    // ★ E1 — 업로드 요청 0건.
    expect(app.backend.callsTo("uploads/")).toEqual([]);
    // 라우트 표에 없는 경로를 부른 적이 없다(501 가드 미발동).
    expect(app.backend.calls.filter((call) => call.unhandled)).toEqual([]);

    // 로컬 보관은 됐다 — `results/`에 폴더가 1개 생긴다(M6-W).
    const results = await listOpfs(page, "results");
    expect(results).toHaveLength(1);
    expect(results[0]).toMatch(/\/$/);

    // ★ E23 — 저장된 `EnableQrDelivery`가 그대로다(게스트 촬영이 운영자 설정을 끄지 않는다).
    const stored = await page.evaluate(
      (key: string) => window.localStorage.getItem(key),
      SETTINGS_STORAGE_KEY,
    );
    expect(stored).not.toBeNull();
    const parsed = JSON.parse(stored ?? "{}") as { values?: Record<string, unknown> };
    expect(parsed.values?.EnableQrDelivery).toBe(true);
  });

  test("E10 — Capture에는 프레임을 바꾸는 컨트롤이 없다(M11)", async ({ page }) => {
    await gotoFrameSelect(page);
    await selectFirstFrame(page);
    await enterCapture(page);

    // 촬영 중 화면의 버튼은 [취소]·[바로 촬영] 둘뿐이다.
    const labels = await page.locator("main button").allInnerTexts();
    expect(labels).toEqual([STRINGS.common.cancel, FLOW_LITERALS.capturePromptShootNow]);
    // 프레임 선택 화면의 카드·버튼이 어디에도 없다.
    await expect(page.getByRole("button", { name: FLOW_LITERALS.frameSelectCreate })).toHaveCount(0);
    await expect(page.locator("main button[aria-pressed]")).toHaveCount(0);
  });

  test("E11 — 슬롯 수만큼 골라야 [다음]이 열린다(M12)", async ({ page }) => {
    await gotoFrameSelect(page);
    await selectFirstFrame(page);
    await enterCapture(page);
    await shootAllCuts(page, DEFAULT_CUT_COUNT);

    const next = page.getByRole("button", { name: STRINGS.common.next, exact: true });
    const cards = cutCards(page);
    await expect(cards).toHaveCount(DEFAULT_CUT_COUNT);

    // 0/4 · 3/4 → 비활성.
    await expect(next).toBeDisabled();
    for (let i = 0; i < 3; i++) await cards.nth(i).click();
    await expect(page.getByRole("heading", { name: "컷 선택 (3/4)" })).toBeVisible();
    await expect(next).toBeDisabled();

    // 4/4 → 활성.
    await cards.nth(3).click();
    await expect(page.getByRole("heading", { name: "컷 선택 (4/4)" })).toBeVisible();
    await expect(next).toBeEnabled();

    // 5번째 클릭은 선택을 늘리지 않는다(개수 상한).
    await cards.nth(4).click();
    await expect(page.getByRole("heading", { name: "컷 선택 (4/4)" })).toBeVisible();
    await expect(next).toBeEnabled();
  });

  test("E19 — 탭이 hidden이 되면 촬영을 취소하고 부분 컷을 남기지 않는다(WM4)", async ({ page }) => {
    await gotoFrameSelect(page);
    await selectFirstFrame(page);
    await enterCapture(page);

    // 몇 컷만 찍은 상태에서 탭을 숨긴다.
    await shootCuts(page, 2, DEFAULT_CUT_COUNT);
    await expect(captureProgress(page, 2, DEFAULT_CUT_COUNT)).toBeVisible();
    expect(await listOpfs(page, "sessions")).toHaveLength(1);

    await emulateHidden(page);

    await expect(page.getByRole("button", { name: STRINGS.home.start, exact: true })).toBeVisible();
    // ★ 부분 결과를 남기지 않는다 — 세션 작업 공간이 지워진다.
    expect(await listOpfs(page, "sessions")).toEqual([]);
    // 완주하지 않았으므로 보관 결과물도 없다.
    expect(await listOpfs(page, "results")).toEqual([]);

    await emulateVisible(page);
  });

  test("E21 — 촬영 중 새로고침해도 홈에서 시작하고 세션 잔재가 남지 않는다", async ({ page }) => {
    await gotoFrameSelect(page);
    await selectFirstFrame(page);
    await enterCapture(page);
    await shootCuts(page, 2, DEFAULT_CUT_COUNT);
    await expect(captureProgress(page, 2, DEFAULT_CUT_COUNT)).toBeVisible();

    // `Capture`에는 beforeunload 가드가 걸려 있다. Playwright가 대화상자를 자동 수락한다.
    await page.reload();

    await expect(page.getByRole("button", { name: STRINGS.home.start, exact: true })).toBeVisible();
    // 부트스트랩 6단계의 `purgeSessionLeftovers`가 잔재를 걷는다.
    await expect.poll(() => listOpfs(page, "sessions")).toEqual([]);
    expect(await listOpfs(page, "results")).toEqual([]);
  });
});
