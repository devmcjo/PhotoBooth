import { createStore } from "zustand/vanilla";
import { useStore } from "zustand";
import type { AppState } from "@domain/navigation/appState";
import { canTransition, isOverlayScreen } from "@domain/navigation/stateMachine";
import { logger } from "@adapters/storage/logStore";
import { sessionStore } from "./sessionStore";

/**
 * 앱 셸 스토어 — 화면 상태 · 오버레이 복귀 · 모달 스택 · 토스트 (02 §2·§10)
 */

export type ToastKind = "success" | "error" | "info";

export interface Toast {
  readonly id: number;
  readonly kind: ToastKind;
  readonly message: string;
  /** 자동 소멸까지 ms. 성공·정보 4초, 실패 6초(02 §10). */
  readonly durationMs: number;
}

export const TOAST_DURATION_MS = { success: 4000, info: 4000, error: 6000 } as const;
export const MAX_TOASTS = 3;

/** 모달 식별자. 6종 + 유휴 경고(02 §10 · 00 §2). */
export type ModalId =
  | "cameraTest"
  | "diagnostics"
  | "pinPrompt"
  | "framePicker"
  | "confirmDelete"
  | "idleWarning";

export interface ModalEntry {
  readonly id: ModalId;
  /** `Esc`로 닫을 수 있는가. 유휴 경고는 버튼만(02 §10). */
  readonly dismissible: boolean;
  readonly payload?: unknown;
}

/**
 * 홈 복귀 시 정리해야 하는 외부 자원. Step 6(카메라)·7(시퀀스)·9(인코더)가 채운다.
 * 등록 전에는 no-op이므로 Step 4 단독으로도 동작한다.
 */
export interface ShellHooks {
  /** 진행 중인 촬영 시퀀스·카운트다운 취소. */
  cancelCaptureSequence?: () => void | Promise<void>;
  /** 타임랩스 인코더 정지. */
  stopEncoder?: () => void | Promise<void>;
  /** 카메라 정지(트랙 stop). */
  stopCamera?: () => void | Promise<void>;
  /** OPFS 세션 작업 공간 삭제. */
  cleanupWorkspace?: () => void | Promise<void>;
  /** 유휴 감시 정지. */
  stopIdleWatch?: () => void;
}

let hooks: ShellHooks = {};

/** 셸 훅 등록(부분 갱신). 나중 Step이 자기 몫만 채운다. */
export function configureShell(next: ShellHooks): void {
  hooks = { ...hooks, ...next };
}

export function resetShellHooks(): void {
  hooks = {};
}

export interface ShellState {
  readonly screen: AppState;
  /** 오버레이(`Settings`·`Login`·`Account`) 진입 전 화면. 없으면 null. */
  readonly overlayReturnTo: AppState | null;
  readonly modals: readonly ModalEntry[];
  readonly toasts: readonly Toast[];
  /** 전체화면이 해제된 상태인가(재진입 배너 표시 조건 — WD7). */
  readonly fullscreenLost: boolean;

  /** 정방향·오버레이 전이. 불법 전이는 **거부 + 경고 로그**만 남긴다. */
  go(to: AppState): boolean;
  /** 오버레이 [닫기]. 복귀 지점으로 돌아가며 **전이 검증을 면제**한다. */
  closeOverlay(): void;
  /** 홈 복귀(02 §2.5의 6단계). 로그인은 유지한다(M3). */
  returnHome(reason: string): Promise<void>;
  pushModal(entry: ModalEntry): void;
  popModal(id?: ModalId): void;
  clearModals(): void;
  toast(kind: ToastKind, message: string): void;
  dismissToast(id: number): void;
  setFullscreenLost(lost: boolean): void;
}

let nextToastId = 1;

export const shellStore = createStore<ShellState>()((set, get) => ({
  screen: "Home",
  overlayReturnTo: null,
  modals: [],
  toasts: [],
  fullscreenLost: false,

  go(to) {
    const from = get().screen;
    if (!canTransition(from, to)) {
      logger.warn("화면 전이 거부", { from, to });
      return false;
    }
    if (from === to) return true;

    // 오버레이 진입 시에만 복귀 지점을 저장한다.
    // **현재 화면이 이미 오버레이면 저장하지 않는다**(it19) — 덮어쓰면 [닫기]가 자기 자신으로 가서 무반응이 된다.
    const enteringOverlay = isOverlayScreen(to) && to !== "Home";
    const overlayReturnTo =
      enteringOverlay && !isOverlayScreen(from) ? from : get().overlayReturnTo;

    set({ screen: to, overlayReturnTo });
    logger.info("화면 전이", { from, to });
    return true;
  },

  closeOverlay() {
    const { overlayReturnTo, screen } = get();
    const to = overlayReturnTo ?? "Home";
    // 복귀는 전이 검증 면제(진입의 역방향은 항상 합법 — 02 §2.4).
    // **촬영 세션 데이터를 폐기하지 않는다.**
    set({ screen: to, overlayReturnTo: null });
    logger.info("오버레이 복귀", { from: screen, to });
  },

  async returnHome(reason) {
    // 순서가 규격이다(02 §2.5). 각 단계 실패는 무시하고 다음으로 간다 —
    // 정리 실패로 홈 복귀 자체가 막히면 키오스크가 갇힌다.
    await safely(() => hooks.cancelCaptureSequence?.());
    sessionStore.getState().discardCaptureData(); // 1. 촬영 데이터 폐기(로그인 유지 — M3)
    await safely(() => hooks.cleanupWorkspace?.()); // 2. OPFS sessions/{id}/ 삭제
    await safely(() => hooks.stopEncoder?.()); // 3. 인코더 → 카메라 순서
    await safely(() => hooks.stopCamera?.());
    hooks.stopIdleWatch?.(); // 4. 유휴 감시 정지

    set({ screen: "Home", overlayReturnTo: null, modals: [] }); // 5. 화면 = Home
    logger.info(`홈 복귀: ${reason}`); // 6. 로그
  },

  pushModal(entry) {
    // 같은 모달을 두 번 쌓지 않는다(중복 열기 방지).
    if (get().modals.some((m) => m.id === entry.id)) return;
    set({ modals: [...get().modals, entry] });
  },

  popModal(id) {
    const modals = get().modals;
    if (modals.length === 0) return;
    set({
      modals: id === undefined ? modals.slice(0, -1) : modals.filter((m) => m.id !== id),
    });
  },

  clearModals() {
    set({ modals: [] });
  },

  toast(kind, message) {
    const toast: Toast = {
      id: nextToastId++,
      kind,
      message,
      durationMs: TOAST_DURATION_MS[kind],
    };
    const toasts = [...get().toasts, toast];
    // 동시 최대 3개 — 초과 시 오래된 것부터 제거(02 §10).
    set({ toasts: toasts.length > MAX_TOASTS ? toasts.slice(toasts.length - MAX_TOASTS) : toasts });
  },

  dismissToast(id) {
    set({ toasts: get().toasts.filter((t) => t.id !== id) });
  },

  setFullscreenLost(lost) {
    set({ fullscreenLost: lost });
  },
}));

async function safely(run: () => void | Promise<void>): Promise<void> {
  try {
    await run();
  } catch (err) {
    logger.warn("홈 복귀 정리 단계 실패(무시하고 계속)", {
      reason: err instanceof Error ? err.message : String(err),
    });
  }
}

export function useShellStore<T>(selector: (state: ShellState) => T): T {
  return useStore(shellStore, selector);
}

/** 현재 화면(React 밖). */
export function currentScreen(): AppState {
  return shellStore.getState().screen;
}
