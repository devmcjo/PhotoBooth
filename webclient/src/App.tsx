import { Component, useEffect, useMemo, type ErrorInfo, type ReactNode } from "react";
import { APP_STATES, type AppState } from "@domain/navigation/appState";
import { canTransition, isTopBarVisible } from "@domain/navigation/stateMachine";
import { isFullscreenButtonVisible } from "@domain/navigation/fullscreenButtonPolicy";
import { logger } from "@adapters/storage/logStore";
import { isStandaloneDisplay } from "@adapters/platform/appInstall";
import { getFullscreenController } from "@shell/fullscreenController";
import { getIdleWatchdog } from "@shell/idleWatchdog";
import { writeAccountModeIntent } from "@shell/accountModeIntent";
import { shellStore, useShellStore } from "@shell/shellStore";
import { sessionStore, useSessionStore } from "@shell/sessionStore";
import {
  buildAccountMenuItems,
  type AccountMenuItemId,
} from "@screens/account/accountMenu";
import { CameraTestModal } from "@screens/modals/cameraTest/CameraTestModal";
import { DiagnosticsModal } from "@screens/modals/diagnostics/DiagnosticsModal";
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
import { AccountView } from "@ui/views/AccountView";
import { UserMgmtView } from "@ui/views/UserMgmtView";
import { formatCount, STRINGS } from "@ui/strings";
import { env, versionCaption } from "./env";
import type { Branding } from "@adapters/platform/branding";

/**
 * 앱 셸 — 상단바 · 현재 화면 · 모달 스택 · 토스트 · 전체화면 배너 (02 §1)
 *
 * **13개 화면이 전부 실물이다**(Step 16에서 `Account`·`UserMgmt`가 채워졌다).
 * `DummyScreen`은 라우터 `default` 분기의 안전망으로만 남는다.
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
 * 더미 화면 — 라우터 `default` 분기의 **안전망**이다. 실물이 없는 상태는 이제 0개다.
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

  // ⚠️ 셸 모달 4종이 **전부 실물**이다. 미구현 스텁 분기는 Step 16에서 사라졌다.
  //    프레임 불러오기·삭제 확인·서버 등록 확인은 **화면 로컬 오버레이**다(03 §790) —
  //    셸 모달 식별자로 되살리지 마라(FR-8).
  switch (top.id) {
    case "idleWarning":
      return <IdleWarningModal />;
    case "cameraTest":
      return <CameraTestModal />;
    case "pinPrompt":
      return <PinPromptModal />;
    case "diagnostics":
      return <DiagnosticsModal />;
    default:
      // 5번째 모달을 추가하고 케이스를 빠뜨리면 **여기서 타입 오류가 난다.**
      // `default`가 진단 모달을 렌더하던 시절에는 그 실수가 조용히 통과했다.
      return assertNoModal(top.id);
  }
}

/** `ModalId`를 전부 다루지 않으면 컴파일이 깨지게 하는 소진 검사. */
function assertNoModal(id: never): null {
  logger.warn("알 수 없는 모달 식별자 — 렌더하지 않는다", { modalId: String(id) });
  return null;
}

/**
 * 화면 라우팅. **13개 상태가 전부 실물**이다.
 *
 * ⚠️ `Settings`·`Account`·`UserMgmt`는 **`<PinGate>`로 감싼다**(07 §6.1 · 정적 검사 ACC-4).
 *    게이트를 화면 렌더에 걸어야 OAuth 복귀처럼 `go()`를 거치지 않는 진입로까지 구조적으로 덮인다.
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
          <AccountView />
        </PinGate>
      );
    case "UserMgmt":
      // ⚠️ `pinGateGroup("UserMgmt") === "Account"`이므로 `Account`의 승인을 공유한다 —
      //    왕복마다 PIN을 다시 묻지 않는다(07 §6.1 · 설계 §5.5).
      return (
        <PinGate screen="UserMgmt">
          <UserMgmtView />
        </PinGate>
      );
    default:
      return <DummyScreen screen={screen} />;
  }
}

/**
 * 계정 팝오버 선택 처리 — 02 §5.1.
 *
 * ⚠️ 로그아웃이 **토큰을 직접 지우지 않는다.** `logout()`이 `currentUser`를 null로 만들면
 *    M1 구독(`installTokenLifecycle`)이 JWT를 폐기한다. 두 곳에서 지우면 순서 의존이 생긴다.
 */
function handleAccountMenuSelect(id: AccountMenuItemId): void {
  const shell = shellStore.getState();
  switch (id) {
    case "manage":
      writeAccountModeIntent("account");
      shell.go("Account");
      return;
    case "adminTools":
      writeAccountModeIntent("admin");
      shell.go("Account");
      return;
    default:
      sessionStore.getState().logout();
      void shell.returnHome("로그아웃");
      shell.toast("info", STRINGS.account.logoutDone);
  }
}

export function App({ branding }: { readonly branding: Branding }) {
  const screen = useShellStore((s) => s.screen);
  const fullscreenLost = useShellStore((s) => s.fullscreenLost);
  const isFullscreen = useShellStore((s) => s.isFullscreen);
  const user = useSessionStore((s) => s.currentUser);

  // 감시 대상 화면에 있을 때만 유휴 감시를 돌린다(02 §6).
  useEffect(() => {
    const watchdog = getIdleWatchdog();
    watchdog.start();
    return () => watchdog.stop();
  }, []);

  const accountLabel = user === null ? STRINGS.common.login : user.id;

  // 팝오버 항목·권한 판정은 순수 함수가 소유하고 `TopBar`는 렌더만 한다(02 §5.1 · ACC-1 정신).
  const accountMenuItems = useMemo(() => buildAccountMenuItems(user), [user]);

  // [전체화면] 버튼 노출 — 판정은 도메인 순수 함수가 소유한다(조건 4개, 02 §7).
  // ⚠️ 배너와 **상호 배타**다: `fullscreenLost`가 참인 동안에는 배너의 [다시 전체화면으로]가
  //    같은 일을 하므로 버튼을 숨긴다.
  const showFullscreen = isFullscreenButtonVisible({
    supported: getFullscreenController().isSupported(),
    isFullscreen,
    fullscreenLost,
    standalone: isStandaloneDisplay(),
  });

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
          screen={screen}
          accountMenuItems={accountMenuItems}
          onAccountMenuSelect={handleAccountMenuSelect}
          /* 게스트는 팝오버 없이 곧바로 로그인으로 간다(항목이 빈 배열이다). */
          onAccount={() => shellStore.getState().go("Login")}
          onSettings={() => shellStore.getState().go("Settings")}
          showFullscreen={showFullscreen}
          onFullscreen={() => void getFullscreenController().request()}
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

/**
 * 첫 사용자 제스처 1회 콜백(01 §4.2 11단계).
 *
 * 콜백 내용은 호출자(`main.tsx`)가 정한다 — 지금은 **Wake Lock만**이다.
 * ⚠️ 여기에 전체화면 요청을 되살리지 마라(2026-08-01 폐지 — 원인 없는 상태 변화).
 *    정적 검사 FS-1이 `request(` 호출부를 App.tsx 2곳으로 고정한다.
 */
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
