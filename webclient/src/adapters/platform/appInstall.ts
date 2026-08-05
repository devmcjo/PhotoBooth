/**
 * PWA 설치 상태 감지 — 진단 [앱] 섹션 (01 §6)
 *
 * ⚠️ **타입을 믿지 않고 런타임 감지**한다. `matchMedia`는 구형 WebView에 없을 수 있고,
 *    iOS Safari는 표준 `display-mode`가 아니라 비표준 `navigator.standalone`을 쓴다.
 * ⚠️ `beforeinstallprompt`를 붙잡아 설치 버튼을 만들지 않는다 — 키오스크 설치는 운영자가
 *    브라우저 메뉴로 1회 하는 작업이고, 프롬프트를 붙들면 손님 화면에 배너가 뜬다.
 */

export interface AppInstallDeps {
  /** 기본 전역 `window.matchMedia`. */
  readonly matchMedia?: ((query: string) => { matches: boolean }) | null;
  /** iOS Safari 비표준 플래그. 기본 `navigator.standalone`. */
  readonly navigatorStandalone?: boolean | null;
}

/** 홈 화면(스탠드얼론)에서 실행 중인가. 판정 불가면 `false`(= 브라우저에서 실행 중). */
export function isStandaloneDisplay(deps: AppInstallDeps = {}): boolean {
  const match =
    deps.matchMedia !== undefined
      ? deps.matchMedia
      : typeof window !== "undefined" && typeof window.matchMedia === "function"
        ? (query: string) => window.matchMedia(query)
        : null;

  if (match !== null) {
    try {
      if (match("(display-mode: standalone)").matches) return true;
      if (match("(display-mode: fullscreen)").matches) return true;
    } catch {
      // 미지원 쿼리는 무시하고 아래 폴백으로 간다.
    }
  }

  const legacy =
    deps.navigatorStandalone !== undefined
      ? deps.navigatorStandalone
      : typeof navigator === "undefined"
        ? null
        : ((navigator as { standalone?: boolean }).standalone ?? null);

  return legacy === true;
}
