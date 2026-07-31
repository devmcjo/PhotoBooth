import { Component, useEffect, type ErrorInfo, type ReactNode } from "react";
import { APP_STATES, type AppState } from "@domain/navigation/appState";
import { canTransition, isTopBarVisible } from "@domain/navigation/stateMachine";
import { getDirHandleRepo } from "@adapters/storage/dirHandleRepo";
import { logger } from "@adapters/storage/logStore";
import { getFullscreenController } from "@shell/fullscreenController";
import { getIdleWatchdog } from "@shell/idleWatchdog";
import { shellStore, useShellStore } from "@shell/shellStore";
import { sessionStore, useSessionStore } from "@shell/sessionStore";
import { useSettingsStore } from "@shell/settingsStore";
import { CameraTestModal } from "@screens/modals/cameraTest/CameraTestModal";
import { Banner, Button, Modal, ToastHost, TopBar } from "@ui/components";
import {
  CaptureView,
  CutSelectView,
  FrameSelectView,
  GuideView,
  HomeView,
} from "@ui/views/FlowViews";
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
 * ② 계층(사용자 지정 폴더 복사)의 폴더 지정. **사용자 제스처에서만** 호출한다 —
 * `showDirectoryPicker`는 클릭 없이는 열리지 않는다.
 *
 * ⚠️ `LocalSavePath`에는 **폴더 이름만** 들어간다. 브라우저는 실 경로를 노출하지 않는다(05 §5.3).
 */
async function pickLocalSaveFolder(): Promise<void> {
  const repo = getDirHandleRepo();
  const handle = await repo.pick();
  if (handle === null) return; // 취소 — 설정을 건드리지 않는다

  if (!(await repo.store(handle))) {
    shellStore.getState().toast("error", STRINGS.save.failed);
    return;
  }
  // `LocalSavePath`는 게스트 제한 키가 아니다.
  const saved = useSettingsStore.getState().save({ LocalSavePath: handle.name }, { isGuest: false });
  shellStore
    .getState()
    .toast(saved ? "success" : "error", saved ? STRINGS.save.succeeded : STRINGS.save.failed);
}

/** 더미 화면 — 전이 검증 전용. Step 7부터 실제 화면으로 교체된다. */
function DummyScreen({ screen }: { readonly screen: AppState }) {
  const targets = APP_STATES.filter((to) => to !== screen && canTransition(screen, to));
  return (
    <main className="boot">
      <h1 className="boot__title">{screen}</h1>
      <p className="boot__subtitle">전이 검증용 임시 화면 (Step 7부터 교체)</p>
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

      {/* Step 6 실측용 진입점. Step 13에서 설정 화면의 [카메라 테스트]로 옮긴다. */}
      <Button
        onClick={() =>
          shellStore.getState().pushModal({ id: "cameraTest", dismissible: true })
        }
      >
        카메라 테스트 열기
      </Button>

      {/* Step 10 ② 계층 실측용 진입점. Step 13에서 설정 화면의 [로컬 저장 폴더 선택]으로 옮긴다.
          미지원 브라우저(Safari·Firefox·모바일)에서는 렌더하지 않는다 — 05 §5.3. */}
      {screen === "Settings" && getDirHandleRepo().isSupported() && (
        <Button onClick={() => void pickLocalSaveFolder()}>로컬 저장 폴더 선택</Button>
      )}
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
    default:
      // 나머지 모달은 Step 6·13·15·16이 채운다. 그때까지 열려도 앱이 깨지지 않게 스텁을 둔다.
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
 * 화면 라우팅. Step 7까지 구현된 화면은 실물을, 나머지는 더미를 렌더한다.
 * 더미는 Step 8·11~16이 하나씩 교체한다.
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
    case "Guide":
      return <GuideView />;
    case "Capture":
      return <CaptureView />;
    case "CutSelect":
      return <CutSelectView />;
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

/** 세션 사용자를 세팅하는 개발용 헬퍼(Step 12가 실 로그인으로 대체). */
export function devLogin(id: string): void {
  sessionStore.getState().login({
    id,
    role: "user",
    createdAt: new Date().toISOString(),
    email: null,
    authMethod: "google",
    hasPin: false,
  });
}
