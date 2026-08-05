import { useEffect, useMemo, useRef, useState } from "react";
import {
  initialPinAttemptState,
  isPinFormatValid,
  isPinInputBlocked,
  pinInputsMatch,
  PIN_LENGTH,
  type PinAttemptState,
} from "@domain/auth/pinGatePolicy";
import { PinKeypad } from "@ui/components/PinKeypad";
import { createAccountService } from "@adapters/http/accountService";
import { getPinLockRepo } from "@adapters/storage/pinLockRepo";
import {
  currentPinPrompt,
  notifyPinPromptMounted,
  resolvePinPrompt,
  usePinPrompt,
} from "@shell/pinGate";
import { sessionStore } from "@shell/sessionStore";
import { Button, Modal } from "@ui/components";
import { formatCount, STRINGS } from "@ui/strings";
import { runPinAttempt, type PinMessageKey } from "./pinPromptRunner";
import styles from "./pinPrompt.module.css";

/**
 * PIN 입력 모달 — 03 §15.3 · 07 §6.4
 *
 * 이 컴포넌트가 소유하는 것은 **입력 버퍼와 타이머뿐**이다. 판정·서버 왕복·잠금 기록은
 * `pinPromptRunner`(React 무관)가 하고, 게이트 개폐는 `shell/pinGate`가 한다.
 *
 * ⚠️ **PIN을 로그·에러 메시지에 남기지 않는다.** 버퍼는 제출 즉시 비운다.
 * ⚠️ `<input>`을 쓰지 않는다 — `autocomplete`·비밀번호 관리자 노출면 자체를 만들지 않는다.
 * ⚠️ 4자리가 차도 **자동 제출하지 않는다**. 오입력 1회가 곧 실패 카운트라 실수 여지를 줄인다.
 */

type PromptStep = "verify" | "setup" | "setupConfirm";

/**
 * `<StrictMode>`의 이중 effect가 **1회차 cleanup으로 게이트를 취소**하는 것을 막는다.
 * (Step 12에서 콜백 처리로 같은 함정을 밟았다 — 15 §6.)
 * 취소를 다음 태스크로 미루고, 재마운트가 그 사이에 취소를 걷는다.
 */
let unmountCancelTimer: ReturnType<typeof setTimeout> | null = null;

function scheduleUnmountCancel(): void {
  if (unmountCancelTimer !== null) clearTimeout(unmountCancelTimer);
  unmountCancelTimer = setTimeout(() => {
    unmountCancelTimer = null;
    resolvePinPrompt({ kind: "cancelled" });
  }, 0);
}

function abortUnmountCancel(): void {
  if (unmountCancelTimer === null) return;
  clearTimeout(unmountCancelTimer);
  unmountCancelTimer = null;
}

function titleOf(step: PromptStep): string {
  switch (step) {
    case "setup":
      return STRINGS.pin.titleSetup;
    case "setupConfirm":
      return STRINGS.pin.titleSetupConfirm;
    default:
      return STRINGS.pin.titleVerify;
  }
}

export function PinPromptModal() {
  const request = usePinPrompt();
  const [step, setStep] = useState<PromptStep>(() => currentPinPrompt()?.mode ?? "verify");
  const [buffer, setBuffer] = useState("");
  /** 최초 설정 1단계 값. 2단계 제출 직후 비운다. */
  const [firstPin, setFirstPin] = useState("");
  const [attempt, setAttempt] = useState<PinAttemptState>(initialPinAttemptState);
  const [message, setMessage] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  /** 쿨다운 표시 갱신용(값 자체는 `attempt`가 들고 있다). */
  const [tick, setTick] = useState(0);

  const service = useMemo(() => createAccountService(), []);
  const mountedRef = useRef(true);

  const nowMs = Date.now();
  const cooling = isPinInputBlocked(attempt, nowMs);
  const disabled = busy || cooling;

  // 마운트 통지 + 언마운트 취소(멱등). 게이트가 무한 스피너로 고착되지 않게 한다.
  useEffect(() => {
    mountedRef.current = true;
    abortUnmountCancel();
    notifyPinPromptMounted();
    return () => {
      mountedRef.current = false;
      scheduleUnmountCancel();
    };
  }, []);

  // 쿨다운이 끝나는 시점에 한 번 다시 그린다(키가 저절로 살아나야 한다).
  useEffect(() => {
    const remaining = attempt.cooldownUntilMs - Date.now();
    if (!Number.isFinite(remaining) || remaining <= 0) return () => undefined;
    const timer = setTimeout(() => setTick((value) => value + 1), remaining);
    return () => clearTimeout(timer);
  }, [attempt.cooldownUntilMs, tick]);

  function append(digit: string): void {
    if (disabled) return;
    setMessage(null);
    setBuffer((current) => (current.length >= PIN_LENGTH ? current : current + digit));
  }

  function backspace(): void {
    if (disabled) return;
    setBuffer((current) => current.slice(0, -1));
  }

  function cancel(): void {
    resolvePinPrompt({ kind: "cancelled" });
  }

  function showMessage(key: PinMessageKey, failCount?: number): void {
    const base = STRINGS.pin.messages[key];
    setMessage(
      failCount === undefined
        ? base
        : `${base} ${formatCount(STRINGS.pin.failCount, failCount)}`,
    );
  }

  async function submit(): Promise<void> {
    if (disabled) return;

    const value = buffer;
    // ⚠️ 버퍼는 즉시 비운다(화면·메모리에 남기지 않는다).
    setBuffer("");

    if (!isPinFormatValid(value)) {
      showMessage("invalidFormat");
      return;
    }

    // 최초 설정 1단계 — 서버 왕복이 없다.
    if (step === "setup") {
      setFirstPin(value);
      setStep("setupConfirm");
      setMessage(null);
      return;
    }

    // 최초 설정 2단계 불일치는 **사용자의 오타**다. 실패 카운트에 세지 않는다.
    if (step === "setupConfirm" && !pinInputsMatch(firstPin, value)) {
      setFirstPin("");
      setStep("setup");
      showMessage("confirmMismatch");
      return;
    }

    setBusy(true);
    const result = await runPinAttempt(attempt, value, {
      mode: step === "verify" ? "verify" : "setup",
      verifyPin: (pin) => service.verifyMyPin(pin),
      setPin: (newPin, currentPin) => service.setMyPin(newPin, currentPin),
      now: () => Date.now(),
      lock: getPinLockRepo(),
      markPinSet: () => sessionStore.getState().markPinSet(),
    });

    // 왕복 중 모달이 닫혔을 수 있다(화면 변경·로그아웃). 그때는 상태를 건드리지 않는다.
    if (!mountedRef.current) return;
    setBusy(false);
    setFirstPin("");

    switch (result.kind) {
      case "granted":
        resolvePinPrompt({ kind: "granted" });
        return;
      case "exhausted":
        resolvePinPrompt({ kind: "exhausted" });
        return;
      case "retry":
        setAttempt(result.state);
        showMessage(result.message, result.state.fails);
        return;
      case "switchToSetup":
        setStep("setup");
        setMessage(null);
        return;
      case "switchToVerify":
        setStep("verify");
        showMessage("alreadySet");
        return;
      default:
        showMessage(result.message);
    }
  }

  // 물리 키보드(0~9 · Backspace · Enter · Esc). 최신 핸들러를 ref로 읽어 재바인딩을 피한다.
  const keyApi = useRef({ append, backspace, submit, cancel });
  useEffect(() => {
    keyApi.current = { append, backspace, submit, cancel };
  });

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent): void => {
      if (event.key.length === 1 && event.key >= "0" && event.key <= "9") {
        event.preventDefault();
        keyApi.current.append(event.key);
        return;
      }
      if (event.key === "Backspace") {
        event.preventDefault();
        keyApi.current.backspace();
        return;
      }
      if (event.key === "Enter") {
        event.preventDefault();
        void keyApi.current.submit();
        return;
      }
      if (event.key === "Escape") {
        event.preventDefault();
        keyApi.current.cancel();
      }
    };
    document.addEventListener("keydown", onKeyDown);
    return () => document.removeEventListener("keydown", onKeyDown);
  }, []);

  if (request === null) return null;

  const cooldownSeconds = Math.max(
    0,
    Math.ceil((attempt.cooldownUntilMs - nowMs) / 1000),
  );

  return (
    <Modal
      id="pinPrompt"
      title={titleOf(step)}
      /*
       * ⚠️ `Modal`의 내장 `Esc`(→ `popModal`)를 쓰지 않는다. 그 경로는 **대기 중인 약속을
       *    해제하지 않아** 게이트가 스피너에 고착된다. 위 keydown 핸들러가 `resolvePinPrompt`로
       *    닫으며, 그것이 `popModal`을 함께 수행한다.
       */
      dismissible={false}
      actions={
        <Button variant="ghost" onClick={() => cancel()}>
          {STRINGS.common.close}
        </Button>
      }
    >
      <div className={styles.body}>
        {/* 표현은 공용 `PinKeypad`가 담당한다 — 판정·서버 왕복은 여전히 여기 밖(runner)이다. */}
        <PinKeypad
          value={buffer}
          disabled={disabled}
          onDigit={(digit) => append(digit)}
          onBackspace={() => backspace()}
          onSubmit={() => void submit()}
        />

        {/* 실패 사유는 스크린리더가 즉시 읽어야 한다(07 §6.4). */}
        <p className={styles.message} role="alert" aria-live="assertive">
          {message ?? ""}
        </p>

        {cooling && (
          <p className={styles.hint}>{formatCount(STRINGS.pin.cooldown, cooldownSeconds)}</p>
        )}
      </div>
    </Modal>
  );
}
