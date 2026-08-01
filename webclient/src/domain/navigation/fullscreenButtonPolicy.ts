/**
 * 상단바 [전체화면] 버튼 노출 판정 — 02 §7 · 12 C4 (순수)
 *
 * 2026-08-01 이전에는 **첫 터치 아무 곳에서나** 전체화면으로 들어갔다. 손님 입장에서는 원인 없는
 * 상태 변화라 폐지했고, 진입점은 이 버튼 하나(+ 이탈 배너의 [다시 전체화면으로])로 좁혔다.
 *
 * 조건이 4개라 화면에 인라인으로 두지 않는다 — 다음에 조건이 하나 늘면 판정 축이 갈라진다(ACC-1 정신).
 */

export interface FullscreenButtonInput {
  /** Fullscreen API를 이 문서에서 쓸 수 있는가(`controller.isSupported()` — 런타임 감지). */
  readonly supported: boolean;
  /** 지금 전체화면인가(`shellStore.isFullscreen`). */
  readonly isFullscreen: boolean;
  /** 전체화면에서 이탈해 배너가 떠 있는가(`shellStore.fullscreenLost`). */
  readonly fullscreenLost: boolean;
  /** PWA standalone/fullscreen 표시 모드인가(`isStandaloneDisplay()`). */
  readonly standalone: boolean;
}

/**
 * 상단바 [전체화면] 버튼을 렌더할 것인가. **네 조건이 전부 아니어야** 보인다.
 *
 * - `supported === false` → 죽은 버튼 금지(iOS Safari는 `requestFullscreen`이 없다).
 * - `isFullscreen === true` → 이미 전체화면인데 버튼이 남으면 오작동으로 보인다.
 * - `fullscreenLost === true` → 이탈 배너의 [다시 전체화면으로]가 같은 일을 한다(중복 방지).
 * - `standalone === true` → 이미 몰입 상태다(홈 화면에서 실행된 PWA).
 */
export function isFullscreenButtonVisible(input: FullscreenButtonInput): boolean {
  return input.supported && !input.isFullscreen && !input.fullscreenLost && !input.standalone;
}
