import { readFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { APP_STATES } from "@domain/navigation/appState";
import { createEmptySession } from "@domain/capture/captureSession";
import {
  clearToken,
  getToken,
  hasToken,
  installTokenLifecycle,
  resetAuthForTests,
  setToken,
  uninstallTokenLifecycle,
} from "@shell/authStore";
import { createIdleWatchdog, IDLE_TIMEOUT_MS, setIdleWatchdogForTests } from "@shell/idleWatchdog";
import {
  IDLE_WARNING_REFERENCE_SECONDS,
  MAX_TOTAL_WAIT_MS,
  MAX_TOTAL_WAIT_SECONDS,
  NO_PROGRESS_TIMEOUT_SECONDS,
} from "@domain/frames/frameLoadPolicy";
import { installGlobalErrorHandler } from "@shell/globalErrorHandler";
import { classifyRoute, needsUnloadGuard } from "@shell/router";
import {
  configureShell,
  MAX_TOASTS,
  resetShellHooks,
  shellStore,
  TOAST_DURATION_MS,
} from "@shell/shellStore";
import { sessionStore } from "@shell/sessionStore";
import { installVisibilityHandlers } from "@adapters/platform/visibility";
import {
  attachLogStore,
  createLogStore,
  createMemoryLogSink,
  detachLogStore,
} from "@adapters/storage/logStore";
import type { SessionUser } from "@domain/accounts/sessionUser";

const USER: SessionUser = {
  id: "devmcjo",
  role: "admin",
  createdAt: "2026-01-01T00:00:00.000Z",
  email: "devmcjo@example.com",
  authMethod: "google",
  hasPin: true,
};

/**
 * `returnHome`은 규격 순서(02 §2.5)대로 **정리를 끝낸 뒤** 화면을 Home으로 바꾼다.
 * 유휴 만료·탭 hidden·전역 예외는 그것을 `void`로 부르므로(await하지 않는다)
 * 테스트는 매크로태스크 경계를 한 번 지나 대기 중인 마이크로태스크를 모두 흘려보낸다.
 */
async function flushPending(): Promise<void> {
  await new Promise<void>((resolve) => setTimeout(resolve, 0));
}

/** 페이크 타이머 구간용 — 타이머와 마이크로태스크를 함께 흘린다. */
async function flushPendingFake(): Promise<void> {
  await vi.advanceTimersByTimeAsync(1);
}

beforeEach(() => {
  attachLogStore(createLogStore({ sink: createMemoryLogSink(), now: () => 0 }));
  shellStore.setState({
    screen: "Home",
    overlayReturnTo: null,
    modals: [],
    toasts: [],
    fullscreenLost: false,
  });
  sessionStore.setState({
    currentUser: null,
    session: createEmptySession(),
    sessionId: null,
    selectedFilter: "None",
  });
  resetAuthForTests();
  resetShellHooks();
});

afterEach(() => {
  uninstallTokenLifecycle();
  setIdleWatchdogForTests(null);
  detachLogStore();
});

describe("authStore — M1 배선(가장 중요한 배선)", () => {
  it("logout() 호출 시 토큰이 null이 된다 — 구독이 끊긴 회귀를 잡는다", () => {
    installTokenLifecycle();
    sessionStore.getState().login(USER);
    setToken("jwt-abc", 3600, 0);
    expect(hasToken()).toBe(true);

    sessionStore.getState().logout();
    expect(getToken()).toBeNull();
  });

  it("currentUser를 null로 만드는 **어떤 경로든** 토큰이 폐기된다(버튼에 걸지 않았다)", () => {
    installTokenLifecycle();
    sessionStore.getState().login(USER);
    setToken("jwt-abc", 3600, 0);

    // logout()이 아니라 스토어를 직접 조작해도 구독이 덮는다.
    sessionStore.setState({ currentUser: null });
    expect(getToken()).toBeNull();
  });

  it("재로그인 시 토큰이 교체된다(A의 잔존 토큰이 남지 않는다 — E3b)", () => {
    installTokenLifecycle();
    sessionStore.getState().login(USER);
    setToken("token-A", 3600, 0);

    sessionStore.getState().logout();
    expect(getToken()).toBeNull();

    sessionStore.getState().login({ ...USER, id: "userB" });
    setToken("token-B", 3600, 0);
    expect(getToken()).toBe("token-B");
  });

  it("구독을 설치하지 않으면 토큰이 남는다 — 배선이 필요하다는 증거", () => {
    // installTokenLifecycle()을 부르지 않는다.
    sessionStore.getState().login(USER);
    setToken("jwt-abc", 3600, 0);
    sessionStore.setState({ currentUser: null });
    expect(getToken()).toBe("jwt-abc"); // 배선이 없으면 이렇게 된다
  });

  it("토큰은 모듈 변수에만 있다(저장소 API를 부르지 않는다 — M2)", () => {
    setToken("jwt-abc", 3600, 0);
    // 저장소가 없는 node 환경에서도 동작한다는 것이 곧 저장소를 쓰지 않는다는 뜻이다.
    expect(getToken()).toBe("jwt-abc");
    clearToken("테스트");
    expect(getToken()).toBeNull();
  });
});

describe("authStore — M2 정적 검사(E4의 절반)", () => {
  it("토큰 홀더 소스에 저장소 API가 등장하지 않는다", () => {
    const source = readFileSync(
      join(dirname(fileURLToPath(import.meta.url)), "..", "..", "..", "src", "shell", "authStore.ts"),
      "utf8",
    );
    // 주석의 설명 문구를 제외하고 실제 호출만 본다.
    const code = source.replace(/\/\*[\s\S]*?\*\//g, "").replace(/(^|[^:])\/\/.*$/gm, "$1");
    for (const forbidden of [
      "localStorage",
      "sessionStorage",
      "indexedDB",
      "document.cookie",
      "persist(",
    ]) {
      expect(code.includes(forbidden), `${forbidden} 사용 금지 — JWT는 메모리 전용(M2)`).toBe(false);
    }
  });
});

describe("shellStore — 전이", () => {
  it("합법 전이는 통과하고 불법 전이는 거부된다(예외 없음)", () => {
    expect(shellStore.getState().go("FrameSelect")).toBe(true);
    expect(shellStore.getState().screen).toBe("FrameSelect");

    expect(shellStore.getState().go("Result")).toBe(false);
    expect(shellStore.getState().screen).toBe("FrameSelect"); // 화면이 바뀌지 않는다
  });

  it("정상 촬영 흐름을 완주한다", () => {
    const path = ["FrameSelect", "Guide", "Capture", "CutSelect", "Result", "Qr", "Done", "Home"] as const;
    for (const to of path) {
      expect(shellStore.getState().go(to), to).toBe(true);
    }
  });

  it("13개 상태가 모두 Home으로 갈 수 있다", () => {
    for (const from of APP_STATES) {
      shellStore.setState({ screen: from });
      expect(shellStore.getState().go("Home"), from).toBe(true);
    }
  });
});

describe("shellStore — 오버레이 복귀(it19)", () => {
  it("오버레이 진입 시 복귀 지점을 저장한다", () => {
    shellStore.getState().go("FrameSelect");
    shellStore.getState().go("Settings");
    expect(shellStore.getState().overlayReturnTo).toBe("FrameSelect");

    shellStore.getState().closeOverlay();
    expect(shellStore.getState().screen).toBe("FrameSelect");
    expect(shellStore.getState().overlayReturnTo).toBeNull();
  });

  it("오버레이끼리 전환할 때 복귀 지점을 덮어쓰지 않는다 — [닫기] 무반응 버그 방지", () => {
    shellStore.getState().go("FrameSelect");
    shellStore.getState().go("Account");
    shellStore.getState().go("UserMgmt"); // 오버레이 → 오버레이
    shellStore.getState().go("Account");

    expect(shellStore.getState().overlayReturnTo).toBe("FrameSelect");
    shellStore.getState().closeOverlay();
    expect(shellStore.getState().screen).toBe("FrameSelect");
  });

  it("복귀 지점이 없으면 Home으로 간다", () => {
    shellStore.setState({ screen: "Settings", overlayReturnTo: null });
    shellStore.getState().closeOverlay();
    expect(shellStore.getState().screen).toBe("Home");
  });

  it("오버레이 복귀는 촬영 세션 데이터를 폐기하지 않는다", () => {
    sessionStore.setState({ sessionId: "20260730_120000_uuid" });
    shellStore.getState().go("Result");
    shellStore.getState().go("Settings");
    shellStore.getState().closeOverlay();
    expect(sessionStore.getState().sessionId).toBe("20260730_120000_uuid");
  });
});

describe("shellStore — 홈 복귀(02 §2.5)", () => {
  it("6단계를 규격 순서로 수행한다", async () => {
    const order: string[] = [];
    configureShell({
      cancelCaptureSequence: () => {
        order.push("cancelSequence");
      },
      cleanupWorkspace: () => {
        order.push("cleanupWorkspace");
      },
      stopEncoder: () => {
        order.push("stopEncoder");
      },
      stopCamera: () => {
        order.push("stopCamera");
      },
      stopIdleWatch: () => {
        order.push("stopIdle");
      },
    });

    shellStore.getState().go("Capture");
    await shellStore.getState().returnHome("테스트");

    expect(order).toEqual([
      "cancelSequence",
      "cleanupWorkspace",
      "stopEncoder",
      "stopCamera",
      "stopIdle",
    ]);
    expect(shellStore.getState().screen).toBe("Home");
  });

  it("로그인을 유지하고 촬영 데이터만 폐기한다(M3)", async () => {
    sessionStore.getState().login(USER);
    sessionStore.setState({ sessionId: "s1" });

    await shellStore.getState().returnHome("유휴 시간 초과");

    expect(sessionStore.getState().currentUser).not.toBeNull();
    expect(sessionStore.getState().sessionId).toBeNull();
  });

  it("정리 단계가 던져도 홈 복귀가 완료된다(키오스크가 갇히지 않는다)", async () => {
    configureShell({
      stopCamera: () => {
        throw new Error("카메라 정지 실패");
      },
    });
    shellStore.getState().go("Capture");
    await shellStore.getState().returnHome("테스트");
    expect(shellStore.getState().screen).toBe("Home");
  });

  it("열린 모달을 모두 닫는다", async () => {
    shellStore.getState().pushModal({ id: "diagnostics", dismissible: true });
    await shellStore.getState().returnHome("테스트");
    expect(shellStore.getState().modals).toHaveLength(0);
  });
});

describe("shellStore — 모달 스택·토스트", () => {
  it("같은 모달을 두 번 쌓지 않는다", () => {
    shellStore.getState().pushModal({ id: "pinPrompt", dismissible: true });
    shellStore.getState().pushModal({ id: "pinPrompt", dismissible: true });
    expect(shellStore.getState().modals).toHaveLength(1);
  });

  it("popModal(id)는 해당 모달만, 인자 없으면 최상단을 닫는다", () => {
    shellStore.getState().pushModal({ id: "diagnostics", dismissible: true });
    shellStore.getState().pushModal({ id: "idleWarning", dismissible: false });

    shellStore.getState().popModal("diagnostics");
    expect(shellStore.getState().modals.map((m) => m.id)).toEqual(["idleWarning"]);

    shellStore.getState().popModal();
    expect(shellStore.getState().modals).toHaveLength(0);
  });

  it("토스트 지속시간이 종류별 규격이다(실패는 더 길다)", () => {
    shellStore.getState().toast("success", "저장했습니다.");
    shellStore.getState().toast("error", "실패했습니다.");
    const [success, error] = shellStore.getState().toasts;
    expect(success!.durationMs).toBe(TOAST_DURATION_MS.success);
    expect(error!.durationMs).toBe(TOAST_DURATION_MS.error);
    expect(error!.durationMs).toBeGreaterThan(success!.durationMs);
  });

  it("동시 3개를 넘으면 오래된 것부터 제거한다", () => {
    for (let i = 0; i < 5; i++) shellStore.getState().toast("info", `m${i}`);
    const messages = shellStore.getState().toasts.map((t) => t.message);
    expect(messages).toHaveLength(MAX_TOASTS);
    expect(messages).toEqual(["m2", "m3", "m4"]);
  });
});

describe("idleWatchdog — 실경과 기반(WM3)", () => {
  function setup(clock: { value: number }) {
    return createIdleWatchdog({
      now: () => clock.value,
      timeoutMs: 1000,
      countdownMs: 100,
      tickMs: 10,
      target: { addEventListener: () => undefined, removeEventListener: () => undefined },
    });
  }

  it("감시 대상 화면에서 무동작이 임계를 넘으면 경고를 띄운다", () => {
    vi.useFakeTimers();
    try {
      const clock = { value: 0 };
      const watchdog = setup(clock);
      shellStore.setState({ screen: "Result" });
      watchdog.start();

      clock.value = 999;
      vi.advanceTimersByTime(10);
      expect(watchdog.isWarning).toBe(false);

      clock.value = 1000;
      vi.advanceTimersByTime(10);
      expect(watchdog.isWarning).toBe(true);
      expect(shellStore.getState().modals.map((m) => m.id)).toContain("idleWarning");
      watchdog.stop();
    } finally {
      vi.useRealTimers();
    }
  });

  it("tick 수가 아니라 실경과로 판정한다 — 스로틀링에서도 정확하다", () => {
    vi.useFakeTimers();
    try {
      const clock = { value: 0 };
      const watchdog = setup(clock);
      shellStore.setState({ screen: "Capture" });
      watchdog.start();

      // tick은 1번뿐이지만 실경과가 임계를 넘었다 → 경고
      clock.value = 5000;
      vi.advanceTimersByTime(10);
      expect(watchdog.isWarning).toBe(true);
      watchdog.stop();
    } finally {
      vi.useRealTimers();
    }
  });

  it("카운트다운이 끝나면 홈 복귀하고 **로그아웃하지 않는다**(M3)", async () => {
    vi.useFakeTimers();
    try {
      const clock = { value: 0 };
      const watchdog = setup(clock);
      sessionStore.getState().login(USER);
      shellStore.setState({ screen: "Result" });
      watchdog.start();

      clock.value = 1000;
      await vi.advanceTimersByTimeAsync(10); // 경고
      clock.value = 1100;
      await vi.advanceTimersByTimeAsync(10); // 만료
      await flushPendingFake();

      expect(shellStore.getState().screen).toBe("Home");
      expect(sessionStore.getState().currentUser).not.toBeNull();
      watchdog.stop();
    } finally {
      vi.useRealTimers();
    }
  });

  it("경고 중 활동은 무시된다(버튼으로만 해제)", async () => {
    vi.useFakeTimers();
    try {
      const clock = { value: 0 };
      const watchdog = setup(clock);
      shellStore.setState({ screen: "Result" });
      watchdog.start();

      clock.value = 1000;
      await vi.advanceTimersByTimeAsync(10);
      expect(watchdog.isWarning).toBe(true);

      watchdog.noteActivity(); // 무시돼야 한다
      clock.value = 1100;
      await vi.advanceTimersByTimeAsync(10);
      await flushPendingFake();
      expect(shellStore.getState().screen).toBe("Home"); // 그대로 만료됐다
      watchdog.stop();
    } finally {
      vi.useRealTimers();
    }
  });

  it("[이어서 진행하기]가 경고를 닫고 타이머를 재시작한다", () => {
    vi.useFakeTimers();
    try {
      const clock = { value: 0 };
      const watchdog = setup(clock);
      shellStore.setState({ screen: "Result" });
      watchdog.start();

      clock.value = 1000;
      vi.advanceTimersByTime(10);
      watchdog.continueSession();

      expect(watchdog.isWarning).toBe(false);
      expect(shellStore.getState().modals).toHaveLength(0);

      clock.value = 1900; // 재시작 후 900ms — 아직 임계 미달
      vi.advanceTimersByTime(10);
      expect(watchdog.isWarning).toBe(false);
      expect(shellStore.getState().screen).toBe("Result");
      watchdog.stop();
    } finally {
      vi.useRealTimers();
    }
  });

  it("감시 제외 화면에서는 만료되지 않는다(설정·편집기에서 작업 중 튕기지 않는다)", () => {
    vi.useFakeTimers();
    try {
      const clock = { value: 0 };
      const watchdog = setup(clock);
      for (const screen of ["Settings", "FrameEditor", "Login", "Home", "Account"] as const) {
        shellStore.setState({ screen });
        watchdog.start();
        clock.value += 100_000;
        vi.advanceTimersByTime(10);
        expect(watchdog.isWarning, screen).toBe(false);
        watchdog.stop();
      }
    } finally {
      vi.useRealTimers();
    }
  });

  it("남은 초는 올림이다", () => {
    vi.useFakeTimers();
    try {
      const clock = { value: 0 };
      const watchdog = createIdleWatchdog({
        now: () => clock.value,
        timeoutMs: 1000,
        countdownMs: 10_000,
        tickMs: 10,
        target: { addEventListener: () => undefined, removeEventListener: () => undefined },
      });
      shellStore.setState({ screen: "Result" });
      watchdog.start();
      clock.value = 1000;
      vi.advanceTimersByTime(10);

      clock.value = 1000 + 1500; // 8.5초 남음 → 9
      expect(watchdog.remainingSeconds()).toBe(9);
      watchdog.stop();
    } finally {
      vi.useRealTimers();
    }
  });
});

/**
 * 프레임 준비 대기 상한 × 유휴 경고 (02 §6.2 · it20)
 *
 * 문서에만 있으면 어느 한쪽 상수를 고칠 때 조용히 깨진다 — 15 §3.4 관례대로 테스트가 막는다.
 * 깨졌을 때의 증상: 손님이 대기 오버레이를 보는 중에 "자리를 비우셨나요?" 팝업이 겹친다.
 */
describe("프레임 대기 상한 불변식(02 §6.2)", () => {
  it("총 대기 상한이 유휴 무동작 판정보다 짧다", () => {
    expect(MAX_TOTAL_WAIT_SECONDS * 1000).toBeLessThan(IDLE_TIMEOUT_MS);
  });

  it("무진행 상한이 총 상한보다 짧다 — 2단 상한이 의미를 가지려면", () => {
    expect(NO_PROGRESS_TIMEOUT_SECONDS).toBeLessThan(MAX_TOTAL_WAIT_SECONDS);
  });

  it("도메인이 들고 있는 유휴 참조 상수가 셸의 실제 값과 같다", () => {
    // 도메인은 셸을 import할 수 없어 값을 **복사**해 둔다(Windows도 동일한 사본 구조).
    // 사본이 낡으면 위 두 검사가 거짓 안심을 준다 → 여기서 동기화까지 고정한다.
    expect(IDLE_WARNING_REFERENCE_SECONDS * 1000).toBe(IDLE_TIMEOUT_MS);
  });

  it("ms 파생 상수가 초 상수와 일치한다", () => {
    expect(MAX_TOTAL_WAIT_MS).toBe(MAX_TOTAL_WAIT_SECONDS * 1000);
  });
});

describe("visibility — 탭 hidden 처리(WM4)", () => {
  function fakeDoc(state: DocumentVisibilityState) {
    const listeners = new Map<string, (() => void)[]>();
    return {
      visibilityState: state,
      addEventListener(type: string, listener: () => void) {
        listeners.set(type, [...(listeners.get(type) ?? []), listener]);
      },
      removeEventListener() {
        /* no-op */
      },
      fire(type: string) {
        for (const listener of listeners.get(type) ?? []) listener();
      },
    };
  }

  it("Capture에서 hidden이면 촬영을 취소하고 홈으로 간다", async () => {
    const doc = fakeDoc("hidden");
    installVisibilityHandlers(doc);
    shellStore.setState({ screen: "Capture" });
    sessionStore.setState({ sessionId: "s1" });

    doc.fire("visibilitychange");
    await flushPending();

    expect(shellStore.getState().screen).toBe("Home");
    expect(sessionStore.getState().sessionId).toBeNull();
  });

  it("Qr에서는 취소하지 않는다(업로드는 계속 진행)", async () => {
    const doc = fakeDoc("hidden");
    installVisibilityHandlers(doc);
    shellStore.setState({ screen: "Qr" });

    doc.fire("visibilitychange");
    await flushPending();
    expect(shellStore.getState().screen).toBe("Qr");
  });

  it("그 외 화면에서는 아무 것도 하지 않는다", async () => {
    const doc = fakeDoc("hidden");
    installVisibilityHandlers(doc);
    shellStore.setState({ screen: "Settings" });

    doc.fire("visibilitychange");
    await flushPending();
    expect(shellStore.getState().screen).toBe("Settings");
  });
});

describe("globalErrorHandler — M16", () => {
  function fakeWindow() {
    const listeners = new Map<string, ((event: Event) => void)[]>();
    return {
      addEventListener(type: string, listener: (event: Event) => void) {
        listeners.set(type, [...(listeners.get(type) ?? []), listener]);
      },
      removeEventListener() {
        /* no-op */
      },
      fire(type: string, event: Event) {
        for (const listener of listeners.get(type) ?? []) listener(event);
      },
    };
  }

  it("예외가 나면 홈으로 복귀하고 **로그인은 유지**한다", async () => {
    const target = fakeWindow();
    installGlobalErrorHandler(target, () => 0);
    sessionStore.getState().login(USER);
    shellStore.setState({ screen: "Result" });

    target.fire("error", { message: "boom" } as unknown as Event);
    await flushPending();

    expect(shellStore.getState().screen).toBe("Home");
    expect(sessionStore.getState().currentUser).not.toBeNull();
    expect(shellStore.getState().toasts.map((t) => t.kind)).toContain("error");
  });

  it("unhandledrejection도 같은 경로로 복구한다", async () => {
    const target = fakeWindow();
    installGlobalErrorHandler(target, () => 0);
    shellStore.setState({ screen: "Capture" });

    target.fire("unhandledrejection", { reason: new Error("nope") } as unknown as Event);
    await flushPending();
    expect(shellStore.getState().screen).toBe("Home");
  });

  it("오류가 폭주해도 홈 복귀를 반복하지 않는다(쿨다운)", async () => {
    const target = fakeWindow();
    let clock = 0;
    installGlobalErrorHandler(target, () => clock);
    shellStore.setState({ screen: "Result" });

    target.fire("error", { message: "1" } as unknown as Event);
    await flushPending();
    expect(shellStore.getState().screen).toBe("Home");

    shellStore.setState({ screen: "Result" }); // 화면을 되돌려 두 번째 복구를 관측
    target.fire("error", { message: "2" } as unknown as Event);
    await flushPending();
    expect(shellStore.getState().screen).toBe("Result"); // 쿨다운으로 복구 안 함

    clock = 5000;
    target.fire("error", { message: "3" } as unknown as Event);
    await flushPending();
    expect(shellStore.getState().screen).toBe("Home");
  });
});

describe("router — 화면 상태를 URL에 싣지 않는다", () => {
  it("경로는 2종뿐이다", () => {
    expect(classifyRoute("/")).toBe("app");
    expect(classifyRoute("/oauth2callback")).toBe("oauthCallback");
    expect(classifyRoute("/oauth2callback/")).toBe("oauthCallback");
    expect(classifyRoute("/anything/else")).toBe("app");
  });

  it("이탈 확인은 촬영·QR·편집기에서만 건다", () => {
    expect(needsUnloadGuard("Capture")).toBe(true);
    expect(needsUnloadGuard("Qr")).toBe(true);
    expect(needsUnloadGuard("FrameEditor")).toBe(true);
    expect(needsUnloadGuard("Home")).toBe(false);
    expect(needsUnloadGuard("Result")).toBe(false);
  });
});
