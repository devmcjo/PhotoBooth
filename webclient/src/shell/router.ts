import { logger } from "@adapters/storage/logStore";
import { isOverlayScreen } from "@domain/navigation/stateMachine";
import { shellStore } from "./shellStore";

/**
 * 라우팅 — **화면 상태를 URL에 싣지 않는다** (02 §3)
 *
 * 경로는 2개뿐이다: `/`(앱 본체) · `/oauth2callback`(인가 코드 수신).
 *
 * ⚠️ URL에 화면을 실으면 뒤로가기로 **촬영 중간 상태에 재진입**할 수 있다.
 *    대신 더미 history 엔트리를 1개 쌓고 `popstate`를 **"현재 화면의 취소 동작"**으로 매핑한 뒤
 *    다시 push해 브라우저를 떠나지 않게 한다.
 */

export type AppRoute = "app" | "oauthCallback";

export function classifyRoute(pathname: string): AppRoute {
  return pathname.replace(/\/+$/, "") === "/oauth2callback" ? "oauthCallback" : "app";
}

/** 뒤로가기 확인 프롬프트를 걸 화면(사고 방지용 — 02 §3.1). */
export function needsUnloadGuard(screen: string): boolean {
  return screen === "Capture" || screen === "Qr" || screen === "FrameEditor";
}

export interface RouterHandle {
  uninstall(): void;
}

interface HistoryLike {
  pushState(data: unknown, unused: string, url?: string): void;
}

interface WindowLike {
  addEventListener(type: string, listener: (event: Event) => void): void;
  removeEventListener(type: string, listener: (event: Event) => void): void;
  readonly history: HistoryLike;
  readonly location: { pathname: string };
}

/**
 * 뒤로가기 가로채기를 설치한다.
 * 오버레이 화면에서는 [닫기]와 같은 동작(복귀), 그 외에는 홈 복귀로 매핑한다.
 */
export function installRouter(
  target: WindowLike | undefined = typeof window !== "undefined"
    ? (window as unknown as WindowLike)
    : undefined,
): RouterHandle {
  if (target === undefined) return { uninstall: () => undefined };

  // 더미 엔트리 1개 — 첫 뒤로가기가 브라우저를 떠나지 않게 한다.
  target.history.pushState({ mcphoto: true }, "");

  const onPopState = (): void => {
    const screen = shellStore.getState().screen;
    if (isOverlayScreen(screen)) {
      shellStore.getState().closeOverlay();
    } else if (screen !== "Home") {
      void shellStore.getState().returnHome("뒤로가기");
    }
    // 엔트리를 다시 쌓아 다음 뒤로가기도 가로챈다.
    target.history.pushState({ mcphoto: true }, "");
    logger.info("뒤로가기 가로챔", { screen });
  };

  const onBeforeUnload = (event: Event): void => {
    if (!needsUnloadGuard(shellStore.getState().screen)) return;
    // 브라우저가 문구를 자체 표시한다(커스텀 문구는 무시된다).
    event.preventDefault();
    (event as BeforeUnloadEvent).returnValue = "";
  };

  target.addEventListener("popstate", onPopState);
  target.addEventListener("beforeunload", onBeforeUnload);

  return {
    uninstall() {
      target.removeEventListener("popstate", onPopState);
      target.removeEventListener("beforeunload", onBeforeUnload);
    },
  };
}
