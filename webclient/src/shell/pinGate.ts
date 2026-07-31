import { createStore } from "zustand/vanilla";
import { useStore } from "zustand";
import type { SessionUser } from "@domain/accounts/sessionUser";
import type { AppState } from "@domain/navigation/appState";
import {
  formatPinLockRemaining,
  pinLockRemainingMs,
  PIN_LOCK_MS,
} from "@domain/auth/pinGatePolicy";
import { getPinLockRepo, type PinLockRepo } from "@adapters/storage/pinLockRepo";
import { logger } from "@adapters/storage/logStore";
import { formatCount, STRINGS } from "@ui/strings";
import { env } from "../env";
import { sessionStore } from "./sessionStore";
import { shellStore } from "./shellStore";

/**
 * 진입 PIN 게이트 — 판정 · 모달 채널 · 생명주기 (07 §6 · analysis/61 §7)
 *
 * ## 왜 렌더 게이트인가
 * 설정 진입로는 ① 상단바 [설정] ② OAuth 복귀(`returnTo="Settings"`) ③ 화면 왕복 …로 계속 늘어난다.
 * 호출부마다 게이트를 붙이면 **반드시 하나가 빠진다**(analysis/61 §7.1). 그래서 판정을
 * `<PinGate>`의 렌더 시점에 걸어 **라우터를 지나야만 화면이 있는** 구조로 만든다.
 *
 * ## fail-closed
 * 잠금 중 · 계정 서비스 불가 · 모달 미마운트(5초) · 취소 · 5회 소진 — **전부 denied**다.
 * "확인할 수 없으면 통과시키지 않는다."
 *
 * ## StrictMode
 * 게이트 상태를 **React 밖 스토어**에 두고 `ensureScreenPinGate`를 **멱등**으로 만든다.
 * `useEffect` cleanup에서 취소하면 `<StrictMode>`의 이중 effect가 1회차를 즉시 취소해
 * 사용자가 설정 화면에서 튕겨 나간다(Step 12에서 밟은 함정과 동종 — 15 §6).
 * "매번 확인"은 cleanup이 아니라 **화면·사용자 변경 구독**이 승인을 폐기해 성립한다.
 */

// ──────────────────────────────────────────────────────────────────────────
// 모달 채널 — `pushModal`이 결과를 돌려주지 않는 문제(F21)의 답
// ──────────────────────────────────────────────────────────────────────────

export interface PinPromptRequest {
  readonly mode: "verify" | "setup";
}

export type PinPromptOutcome =
  | { readonly kind: "granted" }
  /** [닫기] · `Esc` · 컴포넌트 언마운트 · 화면/사용자 변경. */
  | { readonly kind: "cancelled" }
  /** 5회 실패 → 기기 잠금. */
  | { readonly kind: "exhausted" }
  /** 모달이 뜨지 못했다(마운트 감시 타임아웃). */
  | { readonly kind: "unavailable" };

/**
 * 모달이 실제로 마운트됐는지 감시하는 시간. 렌더 트리가 깨졌거나 모달 스택이 다른 모달에
 * 점유돼 있으면 게이트가 무한 스피너로 고착되는 대신 **우아하게 튕겨 나온다**.
 */
export const PIN_PROMPT_MOUNT_TIMEOUT_MS = 5_000;

interface PendingPrompt {
  readonly request: PinPromptRequest;
  readonly promise: Promise<PinPromptOutcome>;
  readonly settle: (outcome: PinPromptOutcome) => void;
  mountTimer: ReturnType<typeof setTimeout> | null;
}

/** 동시에 **1개만** 존재한다. 두 경로가 동시에 닫아도 안전하도록 해제는 멱등이다. */
let pending: PendingPrompt | null = null;

/** React가 구독할 수 있는 표면(요청 자체는 위 모듈 변수가 소유한다). */
const pinPromptStore = createStore<{ readonly request: PinPromptRequest | null }>()(() => ({
  request: null,
}));

/** 현재 열려 있어야 하는 PIN 모달 요청(React 밖). */
export function currentPinPrompt(): PinPromptRequest | null {
  return pinPromptStore.getState().request;
}

export function usePinPrompt(): PinPromptRequest | null {
  return useStore(pinPromptStore, (s) => s.request);
}

/**
 * PIN 모달을 띄우고 결과를 기다린다. **절대 reject하지 않는다.**
 *
 * ⚠️ `pinPrompt` 모달을 `pushModal` 하는 코드는 이 함수 하나뿐이다(정적 불변식 PIN-4) —
 *    게이트를 우회해 모달만 띄우는 경로가 생기면 승인 없이 설정이 열린다.
 * 이미 pending이 있으면 **기존 promise를 그대로** 돌려준다(모달 2중 오픈 금지).
 */
export function openPinPrompt(request: PinPromptRequest): Promise<PinPromptOutcome> {
  if (pending !== null) return pending.promise;

  let settle: (outcome: PinPromptOutcome) => void = () => undefined;
  const promise = new Promise<PinPromptOutcome>((resolve) => {
    settle = resolve;
  });

  const entry: PendingPrompt = { request, promise, settle, mountTimer: null };
  pending = entry;
  pinPromptStore.setState({ request });
  shellStore.getState().pushModal({ id: "pinPrompt", dismissible: true });

  entry.mountTimer = setTimeout(() => {
    logger.error("PIN 모달이 표시되지 않았습니다", { gateMode: request.mode });
    resolvePinPrompt({ kind: "unavailable" });
  }, PIN_PROMPT_MOUNT_TIMEOUT_MS);

  return promise;
}

/**
 * 대기 중인 요청을 해제한다. **멱등** — 이미 해제됐으면 무시한다.
 * 항상 `popModal("pinPrompt")`를 동반하므로 모달이 남지 않는다.
 */
export function resolvePinPrompt(outcome: PinPromptOutcome): void {
  const current = pending;
  if (current === null) return;

  pending = null;
  if (current.mountTimer !== null) {
    clearTimeout(current.mountTimer);
    current.mountTimer = null;
  }
  pinPromptStore.setState({ request: null });
  shellStore.getState().popModal("pinPrompt");
  current.settle(outcome);
}

/** 모달이 마운트됐음을 알린다(마운트 감시 타이머 해제). */
export function notifyPinPromptMounted(): void {
  if (pending === null || pending.mountTimer === null) return;
  clearTimeout(pending.mountTimer);
  pending.mountTimer = null;
}

// ──────────────────────────────────────────────────────────────────────────
// 게이트 판정 — 07 §6.2 의사코드
// ──────────────────────────────────────────────────────────────────────────

export type PinGateDenial = "locked" | "unavailable" | "cancelled" | "exhausted";

export type PinGateResult =
  /** 게스트 — 무가드(07 §6.1). */
  | { readonly kind: "notRequired" }
  | { readonly kind: "granted" }
  | { readonly kind: "denied"; readonly reason: PinGateDenial };

export interface PinGateDeps {
  readonly user: SessionUser | null;
  readonly now: () => number;
  readonly lock: PinLockRepo;
  /** 계정 API를 호출할 수 있는 구성인가. false면 **fail-closed**. */
  readonly accountAvailable: boolean;
  /** 모달 채널. reject해도 게이트는 열리지 않는다(호출부가 try/catch로 한 번 더 막는다). */
  readonly openPrompt: (request: PinPromptRequest) => Promise<PinPromptOutcome>;
  readonly toast: (kind: "error" | "info", message: string) => void;
}

function lockedMessage(lock: PinLockRepo, nowMs: number): string {
  const record = lock.read(nowMs);
  const remainingMs =
    record === null ? PIN_LOCK_MS : pinLockRemainingMs(record.until, nowMs);
  return formatCount(STRINGS.pin.locked, formatPinLockRemaining(remainingMs));
}

/**
 * 게이트 1회 판정. **어떤 분기도 "확인 불가 → 통과"가 없다.**
 *
 * 거부 안내(토스트)는 여기서 **한 번만** 낸다 — 호출부가 또 띄우면 토스트가 2개가 된다.
 * 단 `cancelled`(사용자가 직접 닫음)에는 안내를 내지 않는다(본인이 한 조작이다).
 */
export async function ensurePinGate(deps: PinGateDeps): Promise<PinGateResult> {
  if (deps.user === null) return { kind: "notRequired" };

  const nowMs = deps.now();
  if (deps.lock.read(nowMs) !== null) {
    // 잠금 중에는 **모달을 열지 않는다**(WD16).
    deps.toast("error", lockedMessage(deps.lock, nowMs));
    return { kind: "denied", reason: "locked" };
  }

  if (!deps.accountAvailable) {
    logger.error("PIN 게이트: 계정 서비스를 사용할 수 없습니다(fail-closed)");
    deps.toast("error", STRINGS.error.notConfigured);
    return { kind: "denied", reason: "unavailable" };
  }

  const mode = deps.user.hasPin ? "verify" : "setup";

  let outcome: PinPromptOutcome;
  try {
    outcome = await deps.openPrompt({ mode });
  } catch (err) {
    // 채널은 reject하지 않도록 만들었지만, 그 전제가 깨져도 게이트는 열리지 않는다.
    logger.error("PIN 모달 채널 실패", {
      gateMode: mode,
      reason: err instanceof Error ? err.message : String(err),
    });
    deps.toast("error", STRINGS.pin.messages.unavailable);
    return { kind: "denied", reason: "unavailable" };
  }

  if (outcome.kind === "granted") return { kind: "granted" };

  if (outcome.kind === "exhausted") {
    deps.toast("error", lockedMessage(deps.lock, deps.now()));
  } else if (outcome.kind === "unavailable") {
    deps.toast("error", STRINGS.pin.messages.unavailable);
  }
  return { kind: "denied", reason: outcome.kind };
}

/** 실제 배선. 싱글턴은 **호출 시점**에 해석한다(모듈 로드 부작용 0). */
export function defaultPinGateDeps(overrides: Partial<PinGateDeps> = {}): PinGateDeps {
  return {
    user: sessionStore.getState().currentUser,
    now: () => Date.now(),
    lock: getPinLockRepo(),
    // 백엔드 주소가 없으면 PIN을 확인할 방법이 없다 → 통과시키지 않는다.
    accountAvailable: env.backendBaseUrl.trim().length > 0,
    openPrompt: openPinPrompt,
    toast: (kind, message) => shellStore.getState().toast(kind, message),
    ...overrides,
  };
}

// ──────────────────────────────────────────────────────────────────────────
// 생명주기 스토어 — "매번 확인"과 StrictMode를 동시에 만족시킨다
// ──────────────────────────────────────────────────────────────────────────

export type PinGateStatus = "idle" | "checking" | "granted" | "denied";

export interface PinGateState {
  /** 승인이 유효한 화면. */
  readonly screen: AppState | null;
  /** 승인이 유효한 사용자(게스트는 null). */
  readonly userId: string | null;
  readonly status: PinGateStatus;
}

const IDLE: PinGateState = { screen: null, userId: null, status: "idle" };

export const pinGateStore = createStore<PinGateState>()(() => IDLE);

/** 이 화면에 대한 게이트 상태. 다른 화면의 승인은 보이지 않는다. */
export function usePinGateStatus(screen: AppState): PinGateStatus {
  return useStore(pinGateStore, (s) => (s.screen === screen ? s.status : "idle"));
}

async function runGate(screen: AppState, userId: string | null): Promise<void> {
  const result = await ensurePinGate(defaultPinGateDeps());

  // 대기 중 화면·사용자가 바뀌었으면 결과를 버린다(경합 방어 — `qrUsageStore`와 같은 형태).
  const state = pinGateStore.getState();
  if (state.screen !== screen || state.userId !== userId || state.status !== "checking") return;

  if (result.kind === "denied") {
    // ⚠️ **상태를 먼저** 바꾸고 화면을 되돌린다. 순서를 뒤집으면 화면 변경 구독이 상태를
    //    idle로 만든 뒤 여기서 denied를 덮어써, 그 화면이 영구히 재판정 불가가 된다.
    pinGateStore.setState({ screen, userId, status: "denied" });
    logger.warn("PIN 게이트 거부", { gateScreen: screen, denyReason: result.reason });
    shellStore.getState().closeOverlay();
    return;
  }

  pinGateStore.setState({ screen, userId, status: "granted" });
}

/**
 * 이 화면의 게이트를 보장한다. **멱등** — 같은 `(screen, userId)`로 이미
 * checking·granted·denied면 아무 일도 하지 않는다(StrictMode 이중 effect는 no-op).
 *
 * ⚠️ 짝이 되는 cleanup을 만들지 마라. 승인 폐기는 `installPinGateLifecycle`이 담당한다.
 */
export function ensureScreenPinGate(screen: AppState): void {
  const user = sessionStore.getState().currentUser;
  const userId = user?.id ?? null;

  const state = pinGateStore.getState();
  if (state.status !== "idle" && state.screen === screen && state.userId === userId) return;

  // 게스트 무가드(07 §6.1). 스피너 한 프레임도 보이지 않게 **동기로** 승인한다.
  // (`ensurePinGate`도 같은 규칙을 갖고 있어 이 지름길이 판정을 느슨하게 만들지 않는다.)
  if (user === null) {
    pinGateStore.setState({ screen, userId: null, status: "granted" });
    return;
  }

  pinGateStore.setState({ screen, userId, status: "checking" });
  void runGate(screen, userId);
}

/** 승인을 폐기하고 열려 있는 PIN 모달을 취소한다(멱등). */
function discardPinGate(reason: string): void {
  if (pinGateStore.getState().status !== "idle") {
    pinGateStore.setState(IDLE);
    logger.info("PIN 승인 폐기", { discardReason: reason });
  }
  resolvePinPrompt({ kind: "cancelled" });
}

/** 테스트·재초기화용. 상태와 열린 모달을 초기화한다. */
export function resetPinGateForTests(): void {
  pinGateStore.setState(IDLE);
  resolvePinPrompt({ kind: "cancelled" });
}

let uninstall: (() => void) | null = null;

/**
 * 앱 시작 시 1회 설치(`main.tsx`). 화면 또는 `currentUser`가 바뀌면 승인을 폐기한다 —
 * 이것이 규격의 **"매번 확인"**(07 §6.1)과 로그아웃·세션 만료 처리를 동시에 만족시킨다.
 */
export function installPinGateLifecycle(): () => void {
  if (uninstall !== null) return uninstall;

  const unsubscribeScreen = shellStore.subscribe((state, previous) => {
    if (state.screen !== previous.screen) discardPinGate("screen");
  });
  const unsubscribeUser = sessionStore.subscribe(
    (state) => state.currentUser,
    () => discardPinGate("user"),
  );

  uninstall = () => {
    unsubscribeScreen();
    unsubscribeUser();
    uninstall = null;
  };
  return uninstall;
}

export function uninstallPinGateLifecycle(): void {
  uninstall?.();
}
