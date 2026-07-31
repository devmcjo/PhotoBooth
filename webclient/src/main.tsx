import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { App, installFirstGestureHandlers } from "./App";
import { bootstrap, DEFAULT_BRANDING } from "@shell/bootstrap";
import { installGlobalErrorHandler } from "@shell/globalErrorHandler";
import { installRouter } from "@shell/router";
import { installTokenLifecycle } from "@shell/authStore";
import { installQrUsageLifecycle } from "@shell/qrUsageStore";
import { getFullscreenController } from "@shell/fullscreenController";
import { installVisibilityHandlers } from "@adapters/platform/visibility";
import { requestWakeLock } from "@adapters/platform/wakeLock";
import { logger, getLogStore } from "@adapters/storage/logStore";
import type { Branding } from "@adapters/platform/branding";
import "@ui/theme/tokens.css";
import "./main.css";

/**
 * 앱 진입점 — 부트스트랩 순서는 01 §4.2가 규격이다.
 *   1~6 `shell/bootstrap.ts` · 7 SW(Step 16) · **8 전역 예외** · 9 OAuth 콜백(Step 12)
 *   · **10 React 마운트** · **11 첫 제스처(전체화면·오디오·WakeLock)**
 */

function mount(branding: Branding): void {
  const container = document.getElementById("root");
  if (container === null) return;
  createRoot(container).render(
    <StrictMode>
      <App branding={branding} />
    </StrictMode>,
  );
}

function installShellHandlers(): void {
  // 8. 전역 예외 — 마운트 **전에** 설치해 렌더 중 오류도 잡는다(M16).
  installGlobalErrorHandler();
  // M1 배선: 세션 사용자 해제 → JWT 폐기. 구독 1곳이 모든 경로를 덮는다.
  installTokenLifecycle();
  // TempUser 무료 한도 캐시: 계정이 temp_user로 바뀔 때만 1회 조회한다(07 §7).
  installQrUsageLifecycle();
  installRouter();
  installVisibilityHandlers();
  getFullscreenController().install();

  // 로그는 페이지를 떠날 때 반드시 flush한다(05 §7.1).
  window.addEventListener("pagehide", () => void getLogStore()?.flush());
}

function installFirstGesture(): void {
  // 11. 전체화면·Wake Lock·오디오는 모두 **사용자 제스처**를 요구한다.
  //     Home의 [촬영 시작]과 별개로 화면 아무 곳이나 첫 터치에서 시도한다.
  installFirstGestureHandlers(() => {
    void getFullscreenController().request();
    void requestWakeLock();
    logger.info("첫 제스처 처리(전체화면·WakeLock 요청)");
  });
}

bootstrap().then(
  (result) => {
    installShellHandlers();
    mount(result.branding);
    installFirstGesture();
    logger.info("첫 화면 마운트 완료", {
      opfsCapability: result.opfsCapability,
      persistState: result.storage.persistState,
    });
  },
  (err: unknown) => {
    // 부트스트랩이 실패해도 **화면은 뜬다**(M16 정신 — 크래시 대신 복구).
    installShellHandlers();
    mount(DEFAULT_BRANDING);
    installFirstGesture();
    logger.fatal("부트스트랩 실패 — 기본값으로 진행", {
      reason: err instanceof Error ? err.message : String(err),
    });
  },
);
