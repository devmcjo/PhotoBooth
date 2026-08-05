import type { Page } from "@playwright/test";

/**
 * 탭 가시성 에뮬레이션 — WM4(탭 hidden 시 촬영 취소) 검증용
 *
 * ⚠️ **진짜 탭 전환이 아니다.** Playwright는 실제 백그라운드 전환(프레임 스로틀링·타이머 감속)을
 *    만들 수 없다. 여기서 하는 것은 `document.hidden`/`visibilityState`를 덮고
 *    `visibilitychange`를 발화하는 것뿐이다 — 앱의 **반응**만 검증한다.
 *    진짜 탭 전환에서의 동작은 실측 **V16**이 소유한다.
 */

function setVisibility(page: Page, state: "hidden" | "visible"): Promise<void> {
  return page.evaluate((next: "hidden" | "visible") => {
    Object.defineProperty(document, "visibilityState", {
      configurable: true,
      get: () => next,
    });
    Object.defineProperty(document, "hidden", {
      configurable: true,
      get: () => next === "hidden",
    });
    document.dispatchEvent(new Event("visibilitychange"));
  }, state);
}

export function emulateHidden(page: Page): Promise<void> {
  return setVisibility(page, "hidden");
}

export function emulateVisible(page: Page): Promise<void> {
  return setVisibility(page, "visible");
}
