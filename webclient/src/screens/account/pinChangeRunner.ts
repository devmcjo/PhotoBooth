import {
  initialPinAttemptState,
  isPinFormatValid,
  pinInputsMatch,
} from "@domain/auth/pinGatePolicy";
import type { PinLockRepo } from "@adapters/storage/pinLockRepo";
import { logger } from "@adapters/storage/logStore";
import { runPinAttempt } from "@screens/modals/pinPrompt/pinPromptRunner";

/**
 * 내 PIN 변경 1회 제출 — 03 §13.1 (React 무관)
 *
 * ⚠️ **서버 왕복을 새로 쓰지 않는다.** `runPinAttempt`에 `mode:"setup"` + `currentPin`을 위임하면
 *    ① `unauthorized:"reject"` 경유(PIN 1회 오입력이 로그아웃을 유발하지 않는다 — E17)
 *    ② 성공 시 `markPinSet` ③ 기기 잠금 클리어 ④ **PIN 값을 로그에 담지 않음**이 전부 보장된다.
 *    직접 `setMyPin`을 부르면 ①이 빠져 회귀한다.
 * ⚠️ PIN 값을 로그·반환값·에러 메시지에 **절대** 싣지 않는다(PIN-1).
 */

export type PinChangeStep = "current" | "next" | "confirm";

export type PinChangeResult =
  | { readonly kind: "ok" }
  /** 새 PIN 2회 불일치 — **서버 왕복 없음**. */
  | { readonly kind: "confirmMismatch" }
  | { readonly kind: "invalidFormat" }
  /** 401 — 현재 PIN이 다르다(또는 서버에 이미 PIN이 있는데 보내지 않았다). */
  | { readonly kind: "currentWrong" }
  /** 네트워크·기타. 변경되지 않았다. */
  | { readonly kind: "unavailable" };

export interface PinChangeDeps {
  /** 현재 계정이 PIN을 보유하는가. `true`면 `currentPin`이 필수다. */
  readonly hasPin: boolean;
  readonly currentPin: string | undefined;
  readonly newPin: string;
  readonly confirmPin: string;
  /** `accountService.setMyPin`. */
  readonly setPin: (newPin: string, currentPin?: string) => Promise<void>;
  readonly markPinSet: () => void;
  readonly now: () => number;
  readonly lock: PinLockRepo;
}

export async function runPinChange(deps: PinChangeDeps): Promise<PinChangeResult> {
  // ⚠️ `hasPin === false`인 계정은 애초에 게이트가 최초 설정을 강제하므로 `Account`에 도달한
  //    시점에는 항상 true다. 그래도 분기를 남긴다 — 가정을 코드로 굳히지 않는다.
  if (deps.hasPin && (deps.currentPin === undefined || !isPinFormatValid(deps.currentPin))) {
    return { kind: "invalidFormat" };
  }
  if (!isPinFormatValid(deps.newPin)) return { kind: "invalidFormat" };
  if (!pinInputsMatch(deps.newPin, deps.confirmPin)) return { kind: "confirmMismatch" };

  /*
   * ⚠️ 시도 상태는 **매 제출마다 초기값**이다. 5회 기기 잠금은 *진입* 게이트의 방어이고
   *    (07 §6.2), 여기까지 온 사용자는 이미 그 게이트를 통과했다. 카운터를 누적하면 정상
   *    운영자가 변경 화면에서 오타 몇 번으로 키오스크를 5분간 잠근다.
   */
  const result = await runPinAttempt(initialPinAttemptState(), deps.newPin, {
    mode: "setup",
    // setup 경로만 쓰므로 호출되지 않는다. 인터페이스를 만족시키는 자리다.
    verifyPin: async () => {
      throw new Error("PIN 변경은 setup 경로만 사용한다");
    },
    setPin: deps.setPin,
    ...(deps.hasPin && deps.currentPin !== undefined ? { currentPin: deps.currentPin } : {}),
    now: deps.now,
    lock: deps.lock,
    markPinSet: deps.markPinSet,
  });

  switch (result.kind) {
    case "granted":
      return { kind: "ok" };
    case "retry":
    case "exhausted":
      // setup + currentPin 전송에서의 401은 **현재 PIN 불일치**다(`classifyPinSet`).
      return { kind: "currentWrong" };
    case "switchToVerify":
      // 서버에 이미 PIN이 있는데 `currentPin`을 보내지 않았다 — 사용자에게는 같은 사유다.
      logger.warn("PIN 변경: 서버에 PIN이 이미 있어 현재 PIN이 필요하다");
      return { kind: "currentWrong" };
    case "unavailable":
      return result.message === "invalidFormat"
        ? { kind: "invalidFormat" }
        : { kind: "unavailable" };
    default:
      return { kind: "unavailable" };
  }
}
