import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { SessionUser } from "@domain/accounts/sessionUser";
import { buildPinLockRecord } from "@domain/auth/pinGatePolicy";
import type { PinLockRepo } from "@adapters/storage/pinLockRepo";
import {
  currentPinPrompt,
  ensurePinGate,
  ensureScreenPinGate,
  installPinGateLifecycle,
  notifyPinPromptMounted,
  openPinPrompt,
  pinGateStore,
  resetPinGateForTests,
  resolvePinPrompt,
  uninstallPinGateLifecycle,
  PIN_PROMPT_MOUNT_TIMEOUT_MS,
  type PinGateDeps,
  type PinPromptRequest,
} from "@shell/pinGate";
import { sessionStore } from "@shell/sessionStore";
import { shellStore } from "@shell/shellStore";
import { STRINGS } from "@ui/strings";

/**
 * PIN 게이트 — fail-closed · "매번 확인" · StrictMode 멱등 (07 §6 · 설계 §3)
 *
 * 여기서 고정하는 것 중 가장 중요한 것: **어떤 실패 경로도 게이트를 열지 않는다.**
 */

const NOW = 1_700_000_000_000;

const USER: SessionUser = {
  id: "operator-1",
  role: "admin",
  createdAt: "2026-01-01T00:00:00.000Z",
  email: "op@example.com",
  authMethod: "google",
  hasPin: true,
};

function noLock(): PinLockRepo {
  return { read: () => null, write: () => true, clear: () => undefined };
}

interface GateHarness {
  readonly deps: PinGateDeps;
  readonly prompts: PinPromptRequest[];
  readonly toasts: { kind: string; message: string }[];
}

function gateHarness(overrides: Partial<PinGateDeps> = {}): GateHarness {
  const prompts: PinPromptRequest[] = [];
  const toasts: { kind: string; message: string }[] = [];

  const deps: PinGateDeps = {
    user: USER,
    now: () => NOW,
    lock: noLock(),
    accountAvailable: true,
    openPrompt: async (request) => {
      prompts.push(request);
      return { kind: "granted" };
    },
    toast: (kind, message) => {
      toasts.push({ kind, message });
    },
    ...overrides,
  };

  return { deps, prompts, toasts };
}

beforeEach(() => {
  resetPinGateForTests();
  shellStore.setState({ screen: "Home", overlayReturnTo: null, modals: [], toasts: [] });
  sessionStore.setState({ currentUser: null });
});

afterEach(() => {
  uninstallPinGateLifecycle();
  resetPinGateForTests();
  vi.useRealTimers();
});

describe("ensurePinGate — 판정(07 §6.2)", () => {
  it("게스트는 notRequired이고 모달을 **0회** 연다", async () => {
    const h = gateHarness({ user: null });
    expect(await ensurePinGate(h.deps)).toEqual({ kind: "notRequired" });
    expect(h.prompts).toHaveLength(0);
    expect(h.toasts).toHaveLength(0);
  });

  it("잠금 중이면 denied('locked')이고 모달을 **0회** 연다", async () => {
    const record = buildPinLockRecord(NOW, 5);
    const h = gateHarness({
      lock: { read: () => record, write: () => true, clear: () => undefined },
    });

    expect(await ensurePinGate(h.deps)).toEqual({ kind: "denied", reason: "locked" });
    expect(h.prompts).toHaveLength(0);
    expect(h.toasts).toHaveLength(1);
    // 남은 시간이 안내에 들어간다(WD16).
    expect(h.toasts[0]!.message).toContain("5분");
    expect(h.toasts[0]!.message).toContain("차단");
  });

  it("accountAvailable=false는 denied('unavailable')이다(fail-closed)", async () => {
    const h = gateHarness({ accountAvailable: false });
    expect(await ensurePinGate(h.deps)).toEqual({ kind: "denied", reason: "unavailable" });
    expect(h.prompts).toHaveLength(0);
  });

  it("openPrompt가 reject해도 denied('unavailable')이다(통과하지 않는다)", async () => {
    const h = gateHarness({
      openPrompt: () => Promise.reject(new Error("렌더 트리 파손")),
    });
    expect(await ensurePinGate(h.deps)).toEqual({ kind: "denied", reason: "unavailable" });
  });

  it("hasPin=true면 verify 모드, false면 setup 모드다", async () => {
    const verifyHarness = gateHarness();
    await ensurePinGate(verifyHarness.deps);
    expect(verifyHarness.prompts).toEqual([{ mode: "verify" }]);

    const setupHarness = gateHarness({ user: { ...USER, hasPin: false } });
    await ensurePinGate(setupHarness.deps);
    expect(setupHarness.prompts).toEqual([{ mode: "setup" }]);
  });

  it("취소는 denied('cancelled')이고 **토스트를 내지 않는다**(본인 조작)", async () => {
    const h = gateHarness({ openPrompt: async () => ({ kind: "cancelled" }) });
    expect(await ensurePinGate(h.deps)).toEqual({ kind: "denied", reason: "cancelled" });
    expect(h.toasts).toHaveLength(0);
  });

  it("5회 소진은 denied('exhausted') + 잠금 안내다", async () => {
    let record: ReturnType<typeof buildPinLockRecord> | null = null;
    const h = gateHarness({
      lock: {
        read: () => record,
        write: () => true,
        clear: () => undefined,
      },
      openPrompt: async () => {
        // 러너가 잠금을 기록한 뒤 exhausted로 닫는다.
        record = buildPinLockRecord(NOW, 5);
        return { kind: "exhausted" };
      },
    });

    expect(await ensurePinGate(h.deps)).toEqual({ kind: "denied", reason: "exhausted" });
    expect(h.toasts[0]!.message).toContain("5분");
  });

  it("모달 미마운트(unavailable)는 규격 문구로 안내한다", async () => {
    const h = gateHarness({ openPrompt: async () => ({ kind: "unavailable" }) });
    expect(await ensurePinGate(h.deps)).toEqual({ kind: "denied", reason: "unavailable" });
    expect(h.toasts[0]!.message).toBe(STRINGS.pin.messages.unavailable);
  });
});

describe("모달 채널 — pending 1개 · 해제 멱등", () => {
  it("openPinPrompt가 모달을 쌓고 resolve가 걷는다", async () => {
    const promise = openPinPrompt({ mode: "verify" });
    expect(currentPinPrompt()).toEqual({ mode: "verify" });
    expect(shellStore.getState().modals.map((m) => m.id)).toEqual(["pinPrompt"]);

    resolvePinPrompt({ kind: "granted" });
    expect(await promise).toEqual({ kind: "granted" });
    expect(currentPinPrompt()).toBeNull();
    expect(shellStore.getState().modals).toHaveLength(0);
  });

  it("이미 pending이 있으면 같은 promise를 돌려준다(모달 2중 오픈 금지)", async () => {
    const first = openPinPrompt({ mode: "verify" });
    const second = openPinPrompt({ mode: "setup" });
    expect(second).toBe(first);
    // 요청 모드도 처음 것이 유지된다.
    expect(currentPinPrompt()).toEqual({ mode: "verify" });
    expect(shellStore.getState().modals).toHaveLength(1);

    resolvePinPrompt({ kind: "cancelled" });
    expect(await first).toEqual({ kind: "cancelled" });
  });

  it("resolvePinPrompt는 멱등이다(두 경로가 동시에 닫아도 안전)", async () => {
    const promise = openPinPrompt({ mode: "verify" });
    resolvePinPrompt({ kind: "granted" });
    // 두 번째 해제는 무시된다 — 결과가 덮이지 않는다.
    resolvePinPrompt({ kind: "cancelled" });
    expect(await promise).toEqual({ kind: "granted" });
    expect(() => resolvePinPrompt({ kind: "cancelled" })).not.toThrow();
  });

  it("마운트 감시 5초가 지나면 unavailable로 해제된다(무한 스피너 금지)", async () => {
    vi.useFakeTimers();
    const promise = openPinPrompt({ mode: "verify" });
    vi.advanceTimersByTime(PIN_PROMPT_MOUNT_TIMEOUT_MS);
    expect(await promise).toEqual({ kind: "unavailable" });
    expect(shellStore.getState().modals).toHaveLength(0);
  });

  it("마운트를 알리면 타임아웃이 걷힌다", async () => {
    vi.useFakeTimers();
    const promise = openPinPrompt({ mode: "verify" });
    notifyPinPromptMounted();
    vi.advanceTimersByTime(PIN_PROMPT_MOUNT_TIMEOUT_MS * 3);

    let settled = false;
    void promise.then(() => {
      settled = true;
    });
    await Promise.resolve();
    expect(settled).toBe(false);

    resolvePinPrompt({ kind: "granted" });
    expect(await promise).toEqual({ kind: "granted" });
  });
});

describe("ensureScreenPinGate — 멱등 · 승인 폐기", () => {
  it("게스트는 동기 승인이고 모달이 뜨지 않는다", () => {
    ensureScreenPinGate("Settings");
    expect(pinGateStore.getState()).toEqual({
      screen: "Settings",
      userId: null,
      status: "granted",
    });
    expect(shellStore.getState().modals).toHaveLength(0);
  });

  it("두 번 호출해도 게이트는 1회다(StrictMode 이중 effect)", () => {
    sessionStore.setState({ currentUser: USER });

    ensureScreenPinGate("Settings");
    ensureScreenPinGate("Settings");

    expect(pinGateStore.getState().status).toBe("checking");
    // 모달은 하나만 떠 있다.
    expect(shellStore.getState().modals.map((m) => m.id)).toEqual(["pinPrompt"]);
  });

  it("화면이 바뀌면 승인이 폐기된다(**매번 확인**)", () => {
    installPinGateLifecycle();
    shellStore.getState().go("Settings");
    ensureScreenPinGate("Settings");
    expect(pinGateStore.getState().status).toBe("granted");

    shellStore.getState().closeOverlay();
    expect(shellStore.getState().screen).toBe("Home");
    expect(pinGateStore.getState().status).toBe("idle");

    // 다시 들어오면 처음부터 판정한다.
    shellStore.getState().go("Settings");
    ensureScreenPinGate("Settings");
    expect(pinGateStore.getState().screen).toBe("Settings");
  });

  it("currentUser가 바뀌면 승인이 폐기되고 열린 모달이 취소된다", async () => {
    installPinGateLifecycle();
    sessionStore.setState({ currentUser: USER });

    const promise = openPinPrompt({ mode: "verify" });
    pinGateStore.setState({ screen: "Settings", userId: USER.id, status: "granted" });

    sessionStore.getState().logout();

    expect(pinGateStore.getState().status).toBe("idle");
    expect(await promise).toEqual({ kind: "cancelled" });
    expect(shellStore.getState().modals).toHaveLength(0);
  });

  it("설치는 1회이고 해제 함수가 구독을 걷는다", () => {
    const first = installPinGateLifecycle();
    const second = installPinGateLifecycle();
    expect(second).toBe(first);

    pinGateStore.setState({ screen: "Settings", userId: null, status: "granted" });
    first();
    // 해제 후에는 화면이 바뀌어도 폐기되지 않는다.
    shellStore.getState().go("Login");
    expect(pinGateStore.getState().status).toBe("granted");
  });

  it("잠금 중 재진입은 모달 없이 거부되고 직전 화면으로 돌아간다", async () => {
    // 실제 배선(defaultPinGateDeps)을 타는 경로는 V22 실측이 담당한다.
    // 여기서는 거부 시 상태 전이 규칙만 고정한다.
    installPinGateLifecycle();
    sessionStore.setState({ currentUser: USER });
    shellStore.getState().go("Settings");
    expect(shellStore.getState().overlayReturnTo).toBe("Home");

    pinGateStore.setState({ screen: "Settings", userId: USER.id, status: "denied" });
    shellStore.getState().closeOverlay();

    expect(shellStore.getState().screen).toBe("Home");
    // 화면 변경 구독이 상태를 되돌린다 → 다음 진입은 다시 판정된다.
    expect(pinGateStore.getState().status).toBe("idle");
  });
});
