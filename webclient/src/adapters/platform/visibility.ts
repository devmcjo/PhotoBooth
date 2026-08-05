import { logger } from "@adapters/storage/logStore";
import { getIdleWatchdog } from "@shell/idleWatchdog";
import { shellStore } from "@shell/shellStore";
import { requestWakeLock } from "./wakeLock";

/**
 * 가시성·백그라운드 대응 — WM4 · WR3 · 02 §8
 *
 * ⚠️ **탭이 hidden이 되면 진행 중인 촬영을 취소하고 홈으로 복귀한다.**
 *    hidden 상태에서는 프레임 수신·타이머·인코딩이 모두 스로틀링되므로 계속 진행하면
 *    컷이 비거나 타임랩스가 깨진 채 컷 선택으로 넘어간다. **부분 결과를 남기지 않는 것이 안전측**이다.
 *
 * `Qr` 업로드 중에는 취소하지 않는다(업로드는 계속 진행된다).
 */

export interface VisibilityHandle {
  uninstall(): void;
}

interface DocumentVisibilityLike {
  visibilityState: DocumentVisibilityState;
  addEventListener(type: string, listener: () => void): void;
  removeEventListener(type: string, listener: () => void): void;
}

export function installVisibilityHandlers(
  doc: DocumentVisibilityLike | undefined = typeof document !== "undefined"
    ? (document as unknown as DocumentVisibilityLike)
    : undefined,
): VisibilityHandle {
  if (doc === undefined) return { uninstall: () => undefined };

  const onVisibilityChange = (): void => {
    if (doc.visibilityState === "hidden") {
      const screen = shellStore.getState().screen;
      if (screen === "Capture") {
        logger.warn("탭 비활성으로 촬영 취소", { screen });
        void shellStore.getState().returnHome("탭 비활성으로 촬영 취소");
      }
      // Qr(업로드 중)·그 외 화면은 아무 것도 하지 않는다.
      return;
    }

    // visible 복귀: 유휴 타이머를 **실경과로 즉시 재판정**하고(이미 만료면 바로 홈) Wake Lock을 다시 잡는다.
    getIdleWatchdog().reevaluate();
    void requestWakeLock();
  };

  const onPageHide = (): void => {
    // 카메라·인코더 정지 + 세션 데이터 폐기. 로그는 flush된다(logStore가 pagehide를 별로 듣는다).
    void shellStore.getState().returnHome("페이지 이탈");
  };

  doc.addEventListener("visibilitychange", onVisibilityChange);
  doc.addEventListener("pagehide", onPageHide);
  doc.addEventListener("freeze", onPageHide);

  return {
    uninstall() {
      doc.removeEventListener("visibilitychange", onVisibilityChange);
      doc.removeEventListener("pagehide", onPageHide);
      doc.removeEventListener("freeze", onPageHide);
    },
  };
}
