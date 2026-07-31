import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { App, installFirstGestureHandlers } from "./App";
import { bootstrap, DEFAULT_BRANDING } from "@shell/bootstrap";
import { installGlobalErrorHandler } from "@shell/globalErrorHandler";
import { classifyRoute, installRouter } from "@shell/router";
import { installTokenLifecycle } from "@shell/authStore";
import { installQrUsageLifecycle } from "@shell/qrUsageStore";
import { getFullscreenController } from "@shell/fullscreenController";
import {
  applyOauthCallbackOutcome,
  captureOauthCallback,
  runOauthCallback,
} from "@screens/oauthCallback/oauthCallbackRunner";
import { installVisibilityHandlers } from "@adapters/platform/visibility";
import { requestWakeLock } from "@adapters/platform/wakeLock";
import { logger, getLogStore } from "@adapters/storage/logStore";
import { OauthCallbackGate } from "@ui/views/OauthCallbackView";
import type { Branding } from "@adapters/platform/branding";
import "@ui/theme/tokens.css";
import "./main.css";

/**
 * 앱 진입점 — 부트스트랩 순서는 01 §4.2가 규격이다.
 *   1~6 `shell/bootstrap.ts` · 7 SW(Step 16) · **8 전역 예외** · 9 OAuth 콜백
 *   · **10 React 마운트** · **11 첫 제스처(전체화면·오디오·WakeLock)**
 *
 * ⚠️ 이 파일에 `location.assign`·`location.replace`·`location.href =` 를 두지 않는다 —
 *    리로드하면 메모리 전용 JWT(M2)가 즉시 사라진다. URL 정리는 `history.replaceState`다.
 */

function mount(branding: Branding, callbackPending: Promise<void> | null): void {
  const container = document.getElementById("root");
  if (container === null) return;
  createRoot(container).render(
    <StrictMode>
      {/* 콜백 처리가 끝나기 전에는 <App>을 마운트하지 않는다(계정 라벨 깜빡임·순서 의존 방지). */}
      <OauthCallbackGate pending={callbackPending}>
        <App branding={branding} />
      </OauthCallbackGate>
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

/**
 * 9b. 교환(비동기). **절대 reject하지 않는다** — 게이트가 이 promise 하나를 구독하므로
 * 거절되면 스피너에 고착된다.
 */
function runCallback(decision: ReturnType<typeof captureOauthCallback>): Promise<void> {
  return runOauthCallback(decision).then(
    (outcome) => applyOauthCallbackOutcome(outcome),
    (err: unknown) => {
      logger.error("OAuth 콜백 처리 실패", {
        reason: err instanceof Error ? err.message : String(err),
      });
      applyOauthCallbackOutcome({ kind: "failed", reason: "network" });
    },
  );
}

/**
 * 부트스트랩 8~11단계. 성공·실패 폴백이 **같은 순서**를 쓴다(순서가 곧 규격이다).
 *
 * ⚠️ 9a(`captureOauthCallback`)가 `installShellHandlers()`보다 **먼저**다:
 *    `installRouter`가 설치 즉시 현재 URL 위에 더미 history 엔트리를 쌓기 때문에,
 *    스크럽을 나중에 하면 `/oauth2callback`이 히스토리에 남는다.
 */
function startApp(branding: Branding): void {
  // 9a. 콜백 소비는 **React 밖 동기 1회**다(StrictMode 이중 effect 방어 — 설계 §4.3).
  const decision =
    classifyRoute(window.location.pathname) === "oauthCallback" ? captureOauthCallback() : null;

  installShellHandlers();

  const callbackPending = decision === null ? null : runCallback(decision);

  mount(branding, callbackPending);
  installFirstGesture();
}

bootstrap().then(
  (result) => {
    startApp(result.branding);
    logger.info("첫 화면 마운트 완료", {
      opfsCapability: result.opfsCapability,
      persistState: result.storage.persistState,
    });
  },
  (err: unknown) => {
    // 부트스트랩이 실패해도 **화면은 뜬다**(M16 정신 — 크래시 대신 복구).
    startApp(DEFAULT_BRANDING);
    logger.fatal("부트스트랩 실패 — 기본값으로 진행", {
      reason: err instanceof Error ? err.message : String(err),
    });
  },
);
