import { logger } from "@adapters/storage/logStore";
import { lockEscapeKeys } from "@adapters/platform/keyboardLock";
import { shellStore } from "./shellStore";

/**
 * 전체화면 제어 — WD7 · 02 §7
 *
 * Windows의 `DisplayMode`·`WindowBounds`를 대체한다. **설정 항목을 만들지 않는다.**
 * 전체화면은 **사용자 제스처에서만** 요청할 수 있고, ESC 탈출은 막을 수 없다 —
 * 이탈을 감지하면 재진입 배너를 띄우고 **촬영 흐름은 중단하지 않는다**.
 */

export interface FullscreenController {
  /** 상단바 [전체화면] 버튼·이탈 배너에서 호출. 실패는 로그만 남긴다(강제 불가). */
  request(): Promise<boolean>;
  exit(): Promise<void>;
  isFullscreen(): boolean;
  /**
   * Fullscreen API를 이 문서에서 실제로 쓸 수 있는가.
   *
   * ⚠️ **런타임 감지다 — 타입을 믿지 않는다.** TS DOM lib은 `requestFullscreen`을 필수 멤버로
   *    선언하지만 iOS Safari에는 없다(15 §4 함정 2). 없는데 버튼을 렌더하면 눌러도 아무 일이
   *    일어나지 않는 "죽은 버튼"이 된다.
   */
  isSupported(): boolean;
  /** `fullscreenchange` 구독 설치. 반환값은 해제 함수. */
  install(): () => void;
}

interface DocumentLike {
  fullscreenElement: Element | null;
  documentElement: {
    requestFullscreen?: (options?: FullscreenOptions) => Promise<void>;
  };
  exitFullscreen?: () => Promise<void>;
  addEventListener(type: string, listener: () => void): void;
  removeEventListener(type: string, listener: () => void): void;
}

export function createFullscreenController(
  doc: DocumentLike | undefined = typeof document !== "undefined"
    ? (document as unknown as DocumentLike)
    : undefined,
): FullscreenController {
  function isFullscreen(): boolean {
    return doc !== undefined && doc.fullscreenElement !== null;
  }

  return {
    isFullscreen,

    isSupported() {
      return doc !== undefined && typeof doc.documentElement.requestFullscreen === "function";
    },

    async request() {
      if (doc === undefined) return false;
      const requestFullscreen = doc.documentElement.requestFullscreen;
      if (typeof requestFullscreen !== "function") {
        logger.info("전체화면 미지원 — OS 키오스크 모드에 의존합니다.");
        return false;
      }
      try {
        await requestFullscreen.call(doc.documentElement, { navigationUI: "hide" });
        shellStore.getState().setFullscreenLost(false);
        // 상단바 버튼의 표시 여부는 "지금 전체화면인가"를 따로 본다(`fullscreenLost`와 별 축).
        shellStore.getState().setIsFullscreen(true);
        // 성공한 뒤에만 시도한다(전체화면이 아니면 잠금이 의미 없다).
        void lockEscapeKeys();
        return true;
      } catch (err) {
        // 제스처 밖 호출·정책 거부. 강제할 수 없으므로 로그만 남긴다.
        logger.info("전체화면 요청 실패", {
          reason: err instanceof Error ? err.message : String(err),
        });
        return false;
      }
    },

    async exit() {
      if (doc === undefined || typeof doc.exitFullscreen !== "function") return;
      try {
        if (isFullscreen()) await doc.exitFullscreen();
      } catch {
        // 무해
      }
    },

    install() {
      if (doc === undefined) return () => undefined;
      // 초기값 동기화: 키오스크 기동 직후 이미 전체화면일 수 있다.
      shellStore.getState().setIsFullscreen(isFullscreen());
      const onChange = (): void => {
        const lost = !isFullscreen();
        // ⚠️ `fullscreenLost`의 의미를 바꾸지 마라 — "한 번 들어갔다가 나왔다"이고 배너의 유일한
        //    조건이다. `isFullscreen`은 "지금 전체화면인가"로 **별 축**이다(합치면 초기 상태에서
        //    배너가 뜬다).
        shellStore.getState().setFullscreenLost(lost);
        shellStore.getState().setIsFullscreen(!lost);
        if (lost) logger.info("전체화면 이탈 감지 — 재진입 배너 표시");
      };
      doc.addEventListener("fullscreenchange", onChange);
      return () => doc.removeEventListener("fullscreenchange", onChange);
    },
  };
}

let singleton: FullscreenController | null = null;

export function getFullscreenController(): FullscreenController {
  singleton ??= createFullscreenController();
  return singleton;
}

export function setFullscreenControllerForTests(controller: FullscreenController | null): void {
  singleton = controller;
}
