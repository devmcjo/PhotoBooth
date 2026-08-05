import { useCallback, useEffect, useRef, useState } from "react";
import type { SessionUser } from "@domain/accounts/sessionUser";
import { PIN_LENGTH } from "@domain/auth/pinGatePolicy";
import type { UserRole } from "@domain/roles/userRole";
import { createAccountService } from "@adapters/http/accountService";
import { ACCOUNT_MODE_ADMIN, writeAccountModeIntent } from "@shell/accountModeIntent";
import { sessionStore, useSessionStore } from "@shell/sessionStore";
import { shellStore } from "@shell/shellStore";
import { formatCount, STRINGS } from "@ui/strings";
import { defaultUserListDeps, loadUserList, type UserListView } from "./userListRunner";
import { runDeleteAccount, runSetRole, type UserActionResult } from "./userActions";
import { runPinReset } from "./pinResetRunner";

/**
 * `UserMgmt` 화면 상태를 묶는 **얇은** 훅 — 판정·조립은 러너들이 한다(15 §3.1).
 *
 * ⚠️ 이 파일에 권한 판정을 넣지 마라. jsdom이 없어 훅은 테스트에서 호출할 수 없다.
 * ⚠️ 삭제·PIN 재설정 확인은 **화면 로컬**이다 — `pushModal`을 부르지 않는다(ACC-3).
 */

function toast(kind: "success" | "error" | "info", message: string): void {
  shellStore.getState().toast(kind, message);
}

interface PinResetState {
  readonly target: SessionUser;
  readonly step: "first" | "second";
  readonly buffer: string;
  readonly first: string;
  readonly message: string | null;
  readonly busy: boolean;
}

export function useUserMgmtScreen() {
  const actor = useSessionStore((s) => s.currentUser);
  const [view, setView] = useState<UserListView>({ kind: "loading" });
  const [confirmingDeleteId, setConfirmingDeleteId] = useState<string | null>(null);
  const [pinReset, setPinReset] = useState<PinResetState | null>(null);
  const [busy, setBusy] = useState(false);

  const mountedRef = useRef(true);
  useEffect(() => {
    mountedRef.current = true;
    return () => {
      mountedRef.current = false;
    };
  }, []);

  const refresh = useCallback((signal?: AbortSignal): void => {
    setView({ kind: "loading" });
    void loadUserList(defaultUserListDeps(), signal).then((next) => {
      if (next.kind === "cancelled" || !mountedRef.current) return;
      setView(next);
      setConfirmingDeleteId(null);
    });
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    refresh(controller.signal);
    return () => controller.abort();
  }, [refresh]);

  /** 액션 결과 → 토스트. 403은 **목록을 유지**한다(화면이 비지 않는다). */
  const reportFailure = useCallback((result: UserActionResult): void => {
    if (result.kind === "forbidden") {
      toast("error", STRINGS.error.forbidden);
      return;
    }
    if (result.kind === "notFound") {
      toast("error", STRINGS.userMgmt.notFound);
      return;
    }
    toast("error", STRINGS.error.temporary);
  }, []);

  // ── 삭제(인라인 2단 확인) ─────────────────────────────────────────────
  const deleteAccount = useCallback(
    (target: SessionUser): void => {
      if (actor === null || busy) return;
      setConfirmingDeleteId(null);
      setBusy(true);
      void runDeleteAccount({
        actor,
        target,
        deleteAccount: (id) => createAccountService().deleteAccount(id),
      }).then((result) => {
        if (!mountedRef.current) return;
        setBusy(false);
        if (result.kind === "ok") {
          toast("success", formatCount(STRINGS.userMgmt.deleted, target.id));
          refresh();
          return;
        }
        reportFailure(result);
      });
    },
    [actor, busy, refresh, reportFailure],
  );

  // ── 역할 변경 ─────────────────────────────────────────────────────────
  const changeRole = useCallback(
    (target: SessionUser, nextRole: UserRole): void => {
      if (actor === null || busy) return;
      setBusy(true);
      void runSetRole({
        actor,
        target,
        nextRole,
        setRole: (id, role) => createAccountService().setRole(id, role),
      }).then((result) => {
        if (!mountedRef.current) return;
        setBusy(false);
        if (result.kind === "noop") return;
        if (result.kind === "ok") {
          toast("success", STRINGS.userMgmt.roleChanged);
          refresh();
          return;
        }
        reportFailure(result);
      });
    },
    [actor, busy, refresh, reportFailure],
  );

  // ── 타 계정 PIN 재설정(화면 로컬 오버레이 2단계) ──────────────────────
  const openPinReset = useCallback((target: SessionUser): void => {
    setPinReset({ target, step: "first", buffer: "", first: "", message: null, busy: false });
  }, []);

  const closePinReset = useCallback((): void => {
    setPinReset(null);
  }, []);

  const pinDigit = useCallback((digit: string): void => {
    setPinReset((current) => {
      if (current === null || current.busy) return current;
      if (current.buffer.length >= PIN_LENGTH) return { ...current, message: null };
      return { ...current, buffer: current.buffer + digit, message: null };
    });
  }, []);

  const pinBackspace = useCallback((): void => {
    setPinReset((current) =>
      current === null || current.busy
        ? current
        : { ...current, buffer: current.buffer.slice(0, -1) },
    );
  }, []);

  const submitPinReset = useCallback((): void => {
    const state = pinReset;
    if (state === null || state.busy || actor === null) return;

    const value = state.buffer;
    if (state.step === "first") {
      // 서버 왕복 없이 2단계로. 버퍼는 즉시 비운다.
      setPinReset({ ...state, step: "second", first: value, buffer: "", message: null });
      return;
    }

    setPinReset({ ...state, buffer: "", busy: true, message: null });
    void runPinReset({
      actor,
      target: state.target,
      first: state.first,
      second: value,
      resetOtherPin: (id, newPin) => createAccountService().resetOtherPin(id, newPin),
    }).then((result) => {
      if (!mountedRef.current) return;
      if (result.kind === "ok") {
        setPinReset(null);
        toast("success", STRINGS.userMgmt.pinResetDone);
        return;
      }
      if (result.kind === "confirmMismatch" || result.kind === "invalidFormat") {
        setPinReset({
          target: state.target,
          step: "first",
          buffer: "",
          first: "",
          message:
            result.kind === "confirmMismatch"
              ? STRINGS.pin.messages.confirmMismatch
              : STRINGS.pin.messages.invalidFormat,
          busy: false,
        });
        return;
      }
      setPinReset(null);
      reportFailure(result);
    });
  }, [pinReset, actor, reportFailure]);

  /** [뒤로] — `Account` **직행**이다(03 §14). 복귀 지점을 쓰지 않는다. */
  const back = useCallback((): void => {
    writeAccountModeIntent(ACCOUNT_MODE_ADMIN);
    shellStore.getState().go("Account");
  }, []);

  return {
    actor,
    view,
    busy,
    confirmingDeleteId,
    setConfirmingDeleteId,
    deleteAccount,
    changeRole,
    pinReset,
    openPinReset,
    closePinReset,
    pinDigit,
    pinBackspace,
    submitPinReset,
    refresh,
    back,
  };
}

/** React 밖에서 현재 actor를 읽는 경로(테스트·디버깅 편의). */
export function currentActor(): SessionUser | null {
  return sessionStore.getState().currentUser;
}
