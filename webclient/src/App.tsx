import { Component, useEffect, type ErrorInfo, type ReactNode } from "react";
import { APP_STATES, type AppState } from "@domain/navigation/appState";
import { canTransition, isTopBarVisible } from "@domain/navigation/stateMachine";
import { logger } from "@adapters/storage/logStore";
import { getFullscreenController } from "@shell/fullscreenController";
import { getIdleWatchdog } from "@shell/idleWatchdog";
import { shellStore, useShellStore } from "@shell/shellStore";
import { useSessionStore } from "@shell/sessionStore";
import { CameraTestModal } from "@screens/modals/cameraTest/CameraTestModal";
import { PinPromptModal } from "@screens/modals/pinPrompt/PinPromptModal";
import { Banner, Button, Modal, ToastHost, TopBar } from "@ui/components";
import {
  CaptureView,
  CutSelectView,
  GuideView,
  HomeView,
  ResultView,
} from "@ui/views/FlowViews";
import { FrameEditorView } from "@ui/views/FrameEditorView";
import { FrameSelectView } from "@ui/views/FrameSelectView";
import { QrView } from "@ui/views/QrView";
import { DoneView } from "@ui/views/DoneView";
import { LoginView } from "@ui/views/LoginView";
import { PinGate } from "@ui/views/PinGate";
import { SettingsView } from "@ui/views/SettingsView";
import { formatCount, STRINGS } from "@ui/strings";
import { env, versionCaption } from "./env";
import type { Branding } from "@adapters/platform/branding";

/**
 * 앱 셸 — 상단바 · 현재 화면 · 모달 스택 · 토스트 · 전체화면 배너 (02 §1)
 *
 * 화면 내용은 Step 7~16이 채운다. 지금은 **전이만 검증할 수 있는 더미 화면**이다
 * (13개 상태 전부가 렌더되고, 합법 전이만 버튼으로 노출된다).
 */

/** React 트리 오류에서 셸만 남기고 홈으로 리셋한다(M16 — 화이트스크린 금지). */
class ScreenErrorBoundary extends Component<{ children: ReactNode }, { failed: boolean }> {
  override state = { failed: false };

  static getDerivedStateFromError(): { failed: boolean } {
    return { failed: true };
  }

  override componentDidCatch(error: Error, info: ErrorInfo): void {
    logger.error("화면 렌더 오류 — 홈으로 리셋", {
      reason: error.message,
      componentStack: info.componentStack ?? "",
    });
    void shellStore.getState().returnHome("화면 렌더 오류");
    shellStore.getState().toast("error", STRINGS.error.temporary);
  }

  override componentDidUpdate(): void {
    // 홈 복귀가 반영되면 다시 렌더를 허용한다.
    if (this.state.failed && shellStore.getState().screen === "Home") {
      this.setState({ failed: false });
    }
  }

  override render(): ReactNode {
    return this.state.failed ? null : this.props.children;
  }
}

/**
 * 더미 화면 — 전이 검증 전용. 남은 것은 `Account`·`UserMgmt`이고 Step 16이 교체한다.
 *
 * ⚠️ 여기에 기능 진입점을 두지 마라. Step 6·10이 임시로 두었던 카메라 테스트·로컬 폴더 지정
 *    버튼은 Step 13에서 설정 화면의 정식 위치로 옮겼다(정적 검사 SET-3이 재발을 막는다).
 */
function DummyScreen({ screen }: { readonly screen: AppState }) {
  const targets = APP_STATES.filter((to) => to !== screen && canTransition(screen, to));
  return (
    <main className="boot">
      <h1 className="boot__title">{screen}</h1>
      <p className="boot__subtitle">전이 검증용 임시 화면 (Step 16에서 교체)</p>
      <div style={{ display: "flex", flexWrap: "wrap", gap: "0.5rem", justifyContent: "center" }}>
        {targets.map((to) => (
          <Button key={to} onClick={() => shellStore.getState().go(to)}>
            → {to}
          </Button>
        ))}
      </div>
      <Button variant="ghost" onClick={() => void shellStore.getState().returnHome("사용자 취소")}>
        홈으로
      </Button>
    </main>
  );
}

function IdleWarningModal() {
  const watchdog = getIdleWatchdog();
  return (
    <Modal
      id="idleWarning"
      title={STRINGS.idle.title}
      dismissible={false}
      actions={
        <>
          <Button variant="ghost" onClick={() => void shellStore.getState().returnHome("유휴 경고에서 홈 선택")}>
            {STRINGS.idle.goHome}
          </Button>
          <Button variant="primary" onClick={() => watchdog.continueSession()}>
            {STRINGS.idle.continue}
          </Button>
        </>
      }
    >
      <p aria-live="assertive">{formatCount(STRINGS.idle.body, watchdog.remainingSeconds())}</p>
    </Modal>
  );
}

function ModalStack() {
  const modals = useShellStore((s) => s.modals);
  // 최상단만 렌더한다(포커스 트랩이 하나여야 한다 — 02 §10).
  const top = modals.at(-1);
  if (top === undefined) return null;

  switch (top.id) {
    case "idleWarning":
      return <IdleWarningModal />;
    case "cameraTest":
      return <CameraTestModal />;
    case "pinPrompt":
      return <PinPromptModal />;
    default:
      // 남은 모달(`diagnostics`)은 Step 16이 채운다. 그때까지 열려도 앱이 깨지지 않게 스텁을 둔다.
      // ⚠️ 프레임 불러오기·삭제 확인·서버 등록 확인은 **화면 로컬 오버레이**다(03 §790) —
      //    셸 모달 식별자로 되살리지 마라(FR-8).
      return (
        <Modal
          id={top.id}
          title={top.id}
          dismissible={top.dismissible}
          actions={
            <Button onClick={() => shellStore.getState().popModal(top.id)}>
              {STRINGS.common.close}
            </Button>
          }
        >
          <p>이 모달은 아직 구현되지 않았습니다.</p>
        </Modal>
      );
  }
}

/**
 * 화면 라우팅. 촬영 흐름 전체(Home~Done)와 `Login`·`Settings`·`FrameEditor`는 실물이고,
 * 남은 더미(`Account`·`UserMgmt`)는 Step 16이 교체한다.
 *
 * ⚠️ `Settings`·`Account`는 **`<PinGate>`로 감싼다**(07 §6.1). 게이트를 화면 렌더에 걸어야
 *    OAuth 복귀처럼 `go()`를 거치지 않는 진입로까지 구조적으로 덮인다.
 *    `Account`는 아직 더미지만 **배선을 지금 넣는다** — 나중에 붙이면 "한 경로가 게이트를
 *    빼먹는" 정확히 그 실패가 난다.
 */
function ScreenRouter({
  screen,
  branding,
}: {
  readonly screen: AppState;
  readonly branding: Branding;
}) {
  switch (screen) {
    case "Home":
      return <HomeView appName={branding.appName} subtitle={branding.subtitle} />;
    case "FrameSelect":
      return <FrameSelectView />;
    case "FrameEditor":
      return <FrameEditorView />;
    case "Guide":
      return <GuideView />;
    case "Capture":
      return <CaptureView />;
    case "CutSelect":
      return <CutSelectView />;
    case "Result":
      return <ResultView />;
    case "Qr":
      return <QrView />;
    case "Done":
      return <DoneView appName={branding.appName} />;
    case "Login":
      return <LoginView />;
    case "Settings":
      return (
        <PinGate screen="Settings">
          <SettingsView />
        </PinGate>
      );
    case "Account":
      return (
        <PinGate screen="Account">
          <DummyScreen screen="Account" />
        </PinGate>
      );
    default:
      return <DummyScreen screen={screen} />;
  }
}

export function App({ branding }: { readonly branding: Branding }) {
  const screen = useShellStore((s) => s.screen);
  const fullscreenLost = useShellStore((s) => s.fullscreenLost);
  const user = useSessionStore((s) => s.currentUser);

  // 감시 대상 화면에 있을 때만 유휴 감시를 돌린다(02 §6).
  useEffect(() => {
    const watchdog = getIdleWatchdog();
    watchdog.start();
    return () => watchdog.stop();
  }, []);

  const accountLabel = user === null ? STRINGS.common.login : user.id;

  return (
    <>
      {fullscreenLost && (
        <Banner
          message={STRINGS.fullscreen.lost}
          actionLabel={STRINGS.fullscreen.reenter}
          onAction={() => void getFullscreenController().request()}
        />
      )}

      {isTopBarVisible(screen) && (
        <TopBar
          title={branding.appName}
          accountLabel={accountLabel}
          onAccount={() =>
            shellStore.getState().go(user === null ? "Login" : "Account")
          }
          onSettings={() => shellStore.getState().go("Settings")}
        />
      )}

      <ScreenErrorBoundary>
        <ScreenRouter screen={screen} branding={branding} />
      </ScreenErrorBoundary>

      <ModalStack />
      <ToastHost />
      <p className="version-caption">{versionCaption(env.appVersion)}</p>
    </>
  );
}

/** 첫 사용자 제스처에서 전체화면·Wake Lock·오디오를 잠금 해제한다(01 §4.2 11단계). */
export function installFirstGestureHandlers(
  onFirstGesture: () => void,
  target: Pick<EventTarget, "addEventListener" | "removeEventListener"> | undefined = typeof window !==
  "undefined"
    ? window
    : undefined,
): () => void {
  if (target === undefined) return () => undefined;

  const handler = (): void => {
    remove();
    onFirstGesture();
  };
  const remove = (): void => {
    target.removeEventListener("pointerdown", handler);
    target.removeEventListener("keydown", handler);
  };

  target.addEventListener("pointerdown", handler, { once: true });
  target.addEventListener("keydown", handler, { once: true });
  return remove;
}
