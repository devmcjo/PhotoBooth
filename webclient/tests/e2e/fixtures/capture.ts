import { expect, type Locator, type Page } from "@playwright/test";
import {
  FALLBACK_FRAME_NAME,
  FALLBACK_SLOT_COUNT,
} from "@domain/frames/fallbackFrameSpec";
import { STRINGS } from "@ui/strings";

/**
 * 촬영 흐름 헬퍼 — Home → FrameSelect → Guide → Capture → CutSelect → Result
 *
 * ⚠️ **단언을 하지 않는다**(도달 대기만 한다). 시나리오 단언은 spec이 소유한다(설계 §14).
 * ⚠️ 매 컷 [바로 촬영]을 눌러 카운트다운을 건너뛴다 — 자연 만료를 기다리지 않는다.
 * ⚠️ `page.clock`을 여기서 쓰지 않는다. 미디어 파이프라인과 섞이면 검증 대상이 아니라
 *    하네스를 시험하게 된다(설계 §4.6).
 */

/**
 * `@ui/strings`에 없는 **리터럴 문구 3곳**(`FlowViews.tsx`).
 * spec에 복사하면 문구가 바뀌었을 때 어디가 깨졌는지 알 수 없으므로 여기 한 곳에 모은다.
 */
export const FLOW_LITERALS = {
  /** Guide의 [촬영 시작] — Home의 `STRINGS.home.start`와 글자는 같지만 별 상수다. */
  guideStart: "촬영 시작",
  capturePromptShootNow: "바로 촬영",
  frameSelectCreate: "프레임 만들기",
  frameSelectEditSelected: "선택 편집",
  cutSelectRetake: "재촬영",
} as const;

/** 기본 시드에서 쓰는 값(설계 §8). fallback 프레임은 슬롯 4개다. */
export const DEFAULT_CUT_COUNT = 6;
export const DEFAULT_SLOT_COUNT = FALLBACK_SLOT_COUNT;

/** `FrameSelect`의 프레임 카드(= 유일한 `aria-pressed` 버튼). */
export function frameCards(page: Page): Locator {
  return page.locator("main button[aria-pressed]");
}

/** `CutSelect`의 컷 카드(= 그 화면의 유일한 `aria-pressed` 버튼). */
export function cutCards(page: Page): Locator {
  return page.locator("main button[aria-pressed]");
}

function nextButton(page: Page): Locator {
  return page.getByRole("button", { name: STRINGS.common.next, exact: true });
}

/** Home → FrameSelect. 목록이 **조작 가능(카드 활성)** 해질 때까지 기다린다. */
export async function gotoFrameSelect(page: Page): Promise<void> {
  await page.getByRole("button", { name: STRINGS.home.start, exact: true }).click();
  await expect(page.getByRole("heading", { name: "프레임 선택" })).toBeVisible();
  // 서버 목록이 비어 있으면 코드 생성 fallback 프레임 1개가 뜬다(`fallbackFrameSpec`).
  await expect(frameCards(page).first()).toBeEnabled();
}

/** FrameSelect → Guide. 첫 카드를 고른다. */
export async function selectFirstFrame(page: Page): Promise<void> {
  await frameCards(page).first().click();
  await expect(nextButton(page)).toBeEnabled();
  await nextButton(page).click();
  await expect(page.getByRole("heading", { name: "촬영 안내" })).toBeVisible();
}

/** Guide → Capture. 카메라 Ready(= [바로 촬영] 활성)까지 기다린다. */
export async function enterCapture(page: Page): Promise<void> {
  await page.getByRole("button", { name: FLOW_LITERALS.guideStart, exact: true }).click();
  await expect(
    page.getByRole("button", { name: FLOW_LITERALS.capturePromptShootNow, exact: true }),
  ).toBeEnabled({ timeout: 30_000 });
}

/** `Capture`의 진행 표시(`{n} / {total}`). */
export function captureProgress(page: Page, captured: number, total: number): Locator {
  return page.getByText(`${captured} / ${total}`, { exact: true });
}

/** 컷을 `count`장 찍는다. 매 컷 진행 표시가 올라갈 때까지 기다린다(클릭 유실 방지). */
export async function shootCuts(page: Page, count: number, total = DEFAULT_CUT_COUNT): Promise<void> {
  const shootNow = page.getByRole("button", {
    name: FLOW_LITERALS.capturePromptShootNow,
    exact: true,
  });
  for (let i = 1; i <= count; i++) {
    await shootNow.click();
    if (i < total) {
      await expect(captureProgress(page, i, total)).toBeVisible({ timeout: 30_000 });
    }
  }
}

/** Capture → CutSelect(전 컷 촬영). */
export async function shootAllCuts(page: Page, total = DEFAULT_CUT_COUNT): Promise<void> {
  await shootCuts(page, total, total);
  await expect(page.getByRole("heading", { name: /^컷 선택/ })).toBeVisible({ timeout: 30_000 });
}

/** CutSelect에서 `count`장을 고른다(전이는 하지 않는다). */
export async function selectCuts(page: Page, count = DEFAULT_SLOT_COUNT): Promise<void> {
  for (let i = 0; i < count; i++) {
    await cutCards(page).nth(i).click();
  }
}

/** CutSelect → Result. 합성이 끝나 [다음]이 활성화될 때까지 기다린다. */
export async function goToResult(page: Page, slots = DEFAULT_SLOT_COUNT): Promise<void> {
  await selectCuts(page, slots);
  await expect(nextButton(page)).toBeEnabled();
  await nextButton(page).click();
  await waitForResultReady(page);
}

/** `Result` 도달 + 합성 완료(= [다음] 활성). */
export async function waitForResultReady(page: Page): Promise<void> {
  await expect(page.getByRole("img", { name: "합성 결과" })).toBeVisible({ timeout: 60_000 });
  await expect(nextButton(page)).toBeEnabled({ timeout: 60_000 });
}

/** `Result`의 [다음]. 목적지(Qr/Done)는 effective QR 판정이 정한다. */
export async function clickResultNext(page: Page): Promise<void> {
  await nextButton(page).click();
}

export interface CaptureOptions {
  readonly cutCount?: number;
  readonly slotCount?: number;
}

/** Home → Result 완주. 가장 자주 쓰는 조합이다. */
export async function runCaptureToResult(page: Page, options: CaptureOptions = {}): Promise<void> {
  const cutCount = options.cutCount ?? DEFAULT_CUT_COUNT;
  const slotCount = options.slotCount ?? DEFAULT_SLOT_COUNT;

  await gotoFrameSelect(page);
  await selectFirstFrame(page);
  await enterCapture(page);
  await shootAllCuts(page, cutCount);
  await goToResult(page, slotCount);
}

/** fallback 프레임 이름(카드 단언용). */
export { FALLBACK_FRAME_NAME };
