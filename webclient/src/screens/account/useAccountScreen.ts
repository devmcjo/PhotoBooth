import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import {
  canEditGlobalLimits,
  canOpenUserMgmt,
} from "@domain/accounts/accountAdminPolicy";
import {
  parseLimitInput,
  type TempUserLimitsDraft,
} from "@domain/accounts/tempUserLimitsPolicy";
import { PIN_LENGTH } from "@domain/auth/pinGatePolicy";
import { createAccountService } from "@adapters/http/accountService";
import { getPinLockRepo } from "@adapters/storage/pinLockRepo";
import { getFullscreenController } from "@shell/fullscreenController";
import {
  ACCOUNT_MODE_ADMIN,
  readAccountModeIntent,
  writeAccountModeIntent,
  type AccountMode,
} from "@shell/accountModeIntent";
import { sessionStore, useSessionStore } from "@shell/sessionStore";
import { shellStore } from "@shell/shellStore";
import { STRINGS } from "@ui/strings";
import {
  buildAccountInfoRows,
  defaultAccountInfoDeps,
  type AccountInfoRow,
} from "./accountInfoRows";
import {
  defaultLimitsLoadDeps,
  defaultLimitsSaveDeps,
  loadTempUserLimits,
  saveTempUserLimits,
  type LimitsView,
} from "./adminLimitsForm";
import { runKioskExit } from "./kioskExit";
import { runPinChange, type PinChangeStep } from "./pinChangeRunner";

/**
 * `Account` 화면 상태를 묶는 **얇은** 훅 — 판정·조립은 위 모듈들이 한다(15 §3.1).
 *
 * ⚠️ 이 파일에 판정 로직을 넣지 마라. jsdom이 없어 훅은 테스트에서 호출할 수 없다.
 * ⚠️ 모드 전환에 `go()`를 쓰지 않는다 — 오버레이 간 전환이 복귀 지점을 덮어쓰는 it19 실패를
 *    구조적으로 불가능하게 만든다. `overlayReturnTo`는 `Account` 진입 1회에만 기록된다.
 */

function toast(kind: "success" | "error" | "info", message: string): void {
  shellStore.getState().toast(kind, message);
}

/** PIN 변경 오버레이의 로컬 상태. 셸 모달을 쓰지 않는다(ACC-3). */
interface PinChangeState {
  readonly step: PinChangeStep;
  readonly buffer: string;
  /** 1단계에서 받은 현재 PIN. 제출 직후 비운다. */
  readonly currentPin: string;
  /** 2단계에서 받은 새 PIN. 제출 직후 비운다. */
  readonly nextPin: string;
  readonly message: string | null;
  readonly busy: boolean;
}

function initialPinChange(hasPin: boolean): PinChangeState {
  return {
    step: hasPin ? "current" : "next",
    buffer: "",
    currentPin: "",
    nextPin: "",
    message: null,
    busy: false,
  };
}

export interface LimitsDraftText {
  readonly qrHours: string;
  readonly qrCount: string;
}

const EMPTY_LIMITS_DRAFT: LimitsDraftText = { qrHours: "", qrCount: "" };

export function useAccountScreen() {
  const user = useSessionStore((s) => s.currentUser);
  const role = user?.role ?? null;

  // 진입 모드는 **비파괴 읽기**로 1회만 초기화한다(StrictMode 2회차가 기본값으로 떨어지지 않게).
  const [mode, setModeState] = useState<AccountMode>(() => readAccountModeIntent());
  const [pinChange, setPinChange] = useState<PinChangeState | null>(null);
  const [limits, setLimits] = useState<LimitsView>({ kind: "loading" });
  const [limitsDraft, setLimitsDraft] = useState<LimitsDraftText>(EMPTY_LIMITS_DRAFT);
  const [limitsSaving, setLimitsSaving] = useState(false);
  const [confirmingExit, setConfirmingExit] = useState(false);

  const mountedRef = useRef(true);
  useEffect(() => {
    mountedRef.current = true;
    return () => {
      mountedRef.current = false;
    };
  }, []);

  const canAdmin = canOpenUserMgmt(role) || canEditGlobalLimits(role);
  const canManageUsers = canOpenUserMgmt(role);
  const canEditLimits = canEditGlobalLimits(role);

  const infoRows = useMemo<readonly AccountInfoRow[]>(
    () => (user === null ? [] : buildAccountInfoRows(user, defaultAccountInfoDeps())),
    [user],
  );

  const setMode = useCallback((next: AccountMode): void => {
    // 인계 채널도 함께 갱신한다 — `UserMgmt`에서 [뒤로] 왔을 때 같은 모드로 돌아온다.
    writeAccountModeIntent(next);
    setModeState(next);
  }, []);

  // ── 전역 무료 한도(admin 전용) ────────────────────────────────────────
  const refreshLimits = useCallback((): void => {
    if (!canEditGlobalLimits(role)) {
      setLimits({ kind: "forbidden" });
      return;
    }
    setLimits({ kind: "loading" });
    void loadTempUserLimits(defaultLimitsLoadDeps(role)).then((view) => {
      if (!mountedRef.current) return;
      setLimits(view);
      setLimitsDraft(
        view.kind === "ready"
          ? { qrHours: String(view.current.qrHours), qrCount: String(view.current.qrCount) }
          : EMPTY_LIMITS_DRAFT,
      );
    });
  }, [role]);

  useEffect(() => {
    // 관리자 도구 모드에 들어갔을 때만 조회한다(내 정보만 보는 사용자는 서버를 부르지 않는다).
    if (mode !== ACCOUNT_MODE_ADMIN || !canEditLimits) return;
    refreshLimits();
  }, [mode, canEditLimits, refreshLimits]);

  const changeLimit = useCallback((key: keyof LimitsDraftText, value: string): void => {
    setLimitsDraft((current) => ({ ...current, [key]: value }));
  }, []);

  const saveLimits = useCallback((): void => {
    if (limits.kind !== "ready" || limitsSaving) return;
    const draft: TempUserLimitsDraft = {
      qrHours: parseLimitInput(limitsDraft.qrHours),
      qrCount: parseLimitInput(limitsDraft.qrCount),
    };
    setLimitsSaving(true);
    void saveTempUserLimits(defaultLimitsSaveDeps(role, draft, limits.current)).then((result) => {
      if (!mountedRef.current) return;
      setLimitsSaving(false);
      switch (result.kind) {
        case "ok":
          setLimits({ kind: "ready", current: result.current });
          // 서버가 돌려준 값으로 draft를 **재반영**한다(03 §12.4 4단과 같은 규칙).
          setLimitsDraft({
            qrHours: String(result.current.qrHours),
            qrCount: String(result.current.qrCount),
          });
          toast("success", STRINGS.account.limitsSaved);
          return;
        case "forbidden":
          toast("error", STRINGS.error.forbidden);
          return;
        case "rejected":
          toast(
            "error",
            result.reason === "no-change"
              ? STRINGS.account.limitsNoChange
              : STRINGS.account.limitsRange,
          );
          return;
        default:
          toast("error", STRINGS.account.limitsSaveFailed);
      }
    });
  }, [limits, limitsDraft, limitsSaving, role]);

  // ── PIN 변경 오버레이 ─────────────────────────────────────────────────
  const openPinChange = useCallback((): void => {
    if (user === null) return;
    setPinChange(initialPinChange(user.hasPin));
  }, [user]);

  const closePinChange = useCallback((): void => {
    setPinChange(null);
  }, []);

  const pinDigit = useCallback((digit: string): void => {
    setPinChange((current) => {
      if (current === null || current.busy) return current;
      if (current.buffer.length >= PIN_LENGTH) return { ...current, message: null };
      return { ...current, buffer: current.buffer + digit, message: null };
    });
  }, []);

  const pinBackspace = useCallback((): void => {
    setPinChange((current) =>
      current === null || current.busy ? current : { ...current, buffer: current.buffer.slice(0, -1) },
    );
  }, []);

  const submitPin = useCallback((): void => {
    const state = pinChange;
    if (state === null || state.busy || user === null) return;

    const value = state.buffer;

    if (state.step === "current") {
      // 서버 왕복 없이 다음 단계로. 값은 상태에 옮기고 버퍼는 즉시 비운다.
      setPinChange({ ...state, step: "next", currentPin: value, buffer: "", message: null });
      return;
    }
    if (state.step === "next") {
      setPinChange({ ...state, step: "confirm", nextPin: value, buffer: "", message: null });
      return;
    }

    setPinChange({ ...state, buffer: "", busy: true, message: null });
    void runPinChange({
      hasPin: user.hasPin,
      currentPin: user.hasPin ? state.currentPin : undefined,
      newPin: state.nextPin,
      confirmPin: value,
      setPin: (newPin, currentPin) => createAccountService().setMyPin(newPin, currentPin),
      markPinSet: () => sessionStore.getState().markPinSet(),
      now: () => Date.now(),
      lock: getPinLockRepo(),
    }).then((result) => {
      if (!mountedRef.current) return;
      if (result.kind === "ok") {
        setPinChange(null);
        toast("success", STRINGS.account.pinChanged);
        return;
      }
      // 실패는 처음 단계로 되돌린다 — 부분 입력이 남아 있으면 어느 값이 틀렸는지 알 수 없다.
      setPinChange({
        ...initialPinChange(user.hasPin),
        message: pinChangeMessage(result.kind),
      });
    });
  }, [pinChange, user]);

  // ── 키오스크 종료(인라인 2단 확인) ────────────────────────────────────
  const exitKiosk = useCallback((): void => {
    setConfirmingExit(false);
    void runKioskExit({
      role,
      exitFullscreen: () => getFullscreenController().exit(),
      logout: () => sessionStore.getState().logout(),
      returnHome: (reason) => shellStore.getState().returnHome(reason),
      toast: (kind, message) => toast(kind, message),
    }).then((ok) => {
      if (!ok && mountedRef.current) toast("error", STRINGS.error.forbidden);
    });
  }, [role]);

  const openUserMgmt = useCallback((): void => {
    // 렌더 가드 + 액션 가드 2중(M10). 판정은 도메인이 소유한다.
    if (!canOpenUserMgmt(role)) {
      toast("error", STRINGS.error.forbidden);
      return;
    }
    shellStore.getState().go("UserMgmt");
  }, [role]);

  const close = useCallback((): void => {
    shellStore.getState().closeOverlay();
  }, []);

  return {
    user,
    mode,
    setMode,
    canAdmin,
    canManageUsers,
    canEditLimits,
    infoRows,

    pinChange,
    openPinChange,
    closePinChange,
    pinDigit,
    pinBackspace,
    submitPin,

    limits,
    limitsDraft,
    limitsSaving,
    changeLimit,
    saveLimits,
    refreshLimits,

    confirmingExit,
    setConfirmingExit,
    exitKiosk,

    openUserMgmt,
    close,
  };
}

/** 실패 사유 → 문구. 카탈로그 밖에서 문자열을 조립하지 않는다. */
function pinChangeMessage(kind: "confirmMismatch" | "invalidFormat" | "currentWrong" | "unavailable"): string {
  switch (kind) {
    case "confirmMismatch":
      return STRINGS.pin.messages.confirmMismatch;
    case "invalidFormat":
      return STRINGS.pin.messages.invalidFormat;
    case "currentWrong":
      return STRINGS.account.pinCurrentWrong;
    default:
      return STRINGS.pin.messages.unavailable;
  }
}
