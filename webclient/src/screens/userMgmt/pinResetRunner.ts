import { buildUserRow } from "@domain/accounts/accountAdminPolicy";
import type { SessionUser } from "@domain/accounts/sessionUser";
import { isPinFormatValid, pinInputsMatch } from "@domain/auth/pinGatePolicy";
import { logger } from "@adapters/storage/logStore";
import { toActionResult } from "./userActions";

/**
 * 타 계정 PIN 재설정 — `PUT /accounts/{id}/pin` (03 §14 · analysis/31 §4.7, React 무관)
 *
 * ⚠️ **PIN 값을 로그·반환값·에러 메시지에 절대 싣지 않는다.** 로그 컨텍스트는 `targetId`·
 *    `attemptOutcome`만 쓴다 — `pin`·`newPin`은 마스킹 대상이라 담아도 무의미하고, 이름을
 *    바꿔 우회하면 **진짜로 샌다**(정적 검사 PIN-1).
 * ⚠️ `resetOtherPin`에 `unauthorized:"reject"`를 넘기지 않는다(PIN-2b). 그 라우트의 401은
 *    진짜 세션 만료뿐이다 — 권한 위반은 403이다.
 * ⚠️ 첫 실행문이 권한 가드다(ACC-2). `canResetPin`은 **동급을 차단**한다(동급 삭제는 허용).
 */

export type PinResetResult =
  | { readonly kind: "ok" }
  | { readonly kind: "forbidden" }
  | { readonly kind: "invalidFormat" }
  | { readonly kind: "confirmMismatch" }
  | { readonly kind: "notFound" }
  | { readonly kind: "failed" };

export interface PinResetDeps {
  readonly actor: SessionUser;
  readonly target: SessionUser;
  readonly first: string;
  readonly second: string;
  readonly resetOtherPin: (id: string, newPin: string) => Promise<void>;
}

export async function runPinReset(deps: PinResetDeps): Promise<PinResetResult> {
  if (!buildUserRow(deps.actor, deps.target).canResetPin) {
    logger.warn("PIN 재설정 거부(권한 없음)", { targetId: deps.target.id });
    return { kind: "forbidden" };
  }

  if (!isPinFormatValid(deps.first)) return { kind: "invalidFormat" };
  if (!pinInputsMatch(deps.first, deps.second)) return { kind: "confirmMismatch" };

  try {
    await deps.resetOtherPin(deps.target.id, deps.first);
    logger.info("타 계정 PIN 재설정", { targetId: deps.target.id, attemptOutcome: "ok" });
    return { kind: "ok" };
  } catch (err) {
    const result = toActionResult(err, "타 계정 PIN 재설정 실패", deps.target.id);
    return result;
  }
}
