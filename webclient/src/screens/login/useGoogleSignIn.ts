import { useCallback, useState } from "react";
import {
  loginFailureMessageKey,
  type LoginFailureReason,
  type LoginMessageKey,
} from "@domain/auth/loginFailure";
import type { AppState } from "@domain/navigation/appState";
import { startGoogleSignIn, type StartSignInOutcome } from "@adapters/auth/googleSignIn";
import { loginStore, useLoginStore } from "@shell/loginStore";
import { shellStore } from "@shell/shellStore";
import { env } from "../../env";

/**
 * `Login` 화면 배선 — 03 §3
 *
 * 리디렉트 개시 로직은 **React 밖의 `runSignIn`** 이 소유한다(15 §3.1) — node 테스트가
 * "미구성이면 `startGoogleSignIn`을 부르지 않는다" 같은 불변식에 직접 닿아야 한다.
 */

export type SignInPhase = "idle" | "redirecting";

export interface SignInActionDeps {
  /** `env.googleClientId`가 비어 있지 않은가. */
  readonly available: boolean;
  readonly setPhase: (phase: SignInPhase) => void;
  readonly fail: (reason: LoginFailureReason) => void;
  readonly clear: () => void;
  readonly start: (input: { readonly returnTo: AppState }) => Promise<StartSignInOutcome>;
  readonly returnTo: () => AppState;
}

/**
 * [Google로 로그인] 처리.
 *
 * ⚠️ 성공하면 `phase`를 되돌리지 **않는다** — 곧 페이지가 리디렉트로 사라지므로 버튼을
 *    비활성 상태로 두는 것이 맞다(중복 클릭 방지 — 03 §3).
 * ⚠️ `available === false`에서도 커맨드 가드를 둔다(M10 — 렌더 가드 + 액션 첫 줄 가드 2중).
 */
export async function runSignIn(deps: SignInActionDeps): Promise<void> {
  deps.clear();

  if (!deps.available) {
    deps.fail("clientNotConfigured");
    return;
  }

  deps.setPhase("redirecting");
  const outcome = await deps.start({ returnTo: deps.returnTo() });
  if (outcome.ok) return;

  deps.setPhase("idle");
  deps.fail(outcome.reason);
}

export interface GoogleSignInBinding {
  /** false면 버튼을 렌더하지 않고 정적 안내만 보여준다(07 §3). */
  readonly available: boolean;
  readonly phase: SignInPhase;
  /** 표시할 오류 문구 키(없으면 null). 콜백이 실어 보낸 것도 여기로 온다. */
  readonly notice: LoginMessageKey | null;
  signIn(): void;
  /** [닫기] — 오류를 지우고 오버레이 복귀. 어떤 상태에서도 동작한다. */
  close(): void;
}

export function useGoogleSignIn(): GoogleSignInBinding {
  const [phase, setPhase] = useState<SignInPhase>("idle");
  const notice = useLoginStore((s) => s.notice);
  const available = env.googleClientId.length > 0;

  const signIn = useCallback(() => {
    void runSignIn({
      available,
      setPhase,
      fail: (reason) => loginStore.getState().fail(reason),
      clear: () => loginStore.getState().clear(),
      start: (input) => startGoogleSignIn(input),
      // 리디렉트로 앱이 재시작되므로 복귀 지점을 sessionStorage에 실어 보낸다(설계 §4.4).
      returnTo: () => shellStore.getState().overlayReturnTo ?? "Home",
    });
  }, [available]);

  const close = useCallback(() => {
    loginStore.getState().clear();
    shellStore.getState().closeOverlay();
  }, []);

  return {
    available,
    phase,
    // 진단축(6종) → 문구축(5종) 접기는 **화면 경계에서만** 한다.
    notice: notice === null ? null : loginFailureMessageKey(notice),
    signIn,
    close,
  };
}
