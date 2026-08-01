import { oauthRedirectUri } from "@domain/auth/authorizeUrl";
import {
  abortReasonToLoginFailure,
  type LoginFailureReason,
} from "@domain/auth/loginFailure";
import {
  decideOauthCallback,
  parseOauthCallbackParams,
  type OauthCallbackDecision,
  type OauthPendingState,
} from "@domain/auth/oauthCallbackPolicy";
import type { AppState } from "@domain/navigation/appState";
import {
  exchangeGoogleCode,
  type GoogleExchangeOutcome,
  type GoogleExchangeRequest,
  type GoogleLoginResult,
} from "@adapters/auth/googleSignIn";
import { takePendingOauth } from "@adapters/auth/oauthStateStore";
import { logger } from "@adapters/storage/logStore";
import { setToken } from "@shell/authStore";
import { loginStore } from "@shell/loginStore";
import { sessionStore } from "@shell/sessionStore";
import { shellStore } from "@shell/shellStore";

/**
 * `/oauth2callback` 처리 — 07 §2.2 5단계
 *
 * ⚠️ **React를 import하지 않는다**(`uploadRunner`와 같은 형태 — 15 §3.1). 순서가 불변식인데
 *    컴포넌트 안에 있으면 node 테스트가 닿지 못한다.
 *
 * 3단 분리의 이유:
 *   `captureOauthCallback` **동기**  — `<StrictMode>`가 effect를 2회 실행해도 소비는 1회다.
 *                                      (2회째는 저장소가 비어 반드시 `no-pending`이 된다)
 *   `runOauthCallback`     비동기    — 네트워크 왕복.
 *   `applyOauthCallbackOutcome`      — 화면 전이(부트스트랩이 아니라 결과에만 의존).
 */

// ───────────────────────────── ① 동기 1회성 소비 ─────────────────────────────

export interface CaptureDeps {
  /** `location.search`. */
  readonly search: () => string;
  readonly takePending: () => OauthPendingState | null;
  readonly now: () => number;
  /** `history.replaceState(null, "", "/")` — **리로드가 아니다**(메모리 토큰이 사라진다). */
  readonly scrubUrl: () => void;
}

export function defaultCaptureDeps(): CaptureDeps {
  return {
    search: () => window.location.search,
    takePending: () => takePendingOauth(),
    now: () => Date.now(),
    scrubUrl: () => window.history.replaceState(null, "", "/"),
  };
}

/**
 * **동기 1회성 소비**. `main.tsx`가 React 마운트·`installRouter` **이전에** 정확히 한 번 부른다.
 *
 * 순서가 계약이다: `search` 스냅샷 → `takePending`(읽고 삭제) → 판정 → `scrubUrl`.
 *
 * ⚠️ URL 스크럽을 **판정 직후·교환 전**에 한다(규격 07 §2.2의 h를 e 앞으로 당겼다):
 *    ① 실패 경로에도 주소창에 `code`가 남지 않는다
 *    ② 교환은 최대 100초다 — 그 사이 새로고침해도 같은 code로 재진입할 수 없다
 *    ③ `installRouter`가 더미 history 엔트리를 쌓기 전이라 콜백 URL이 히스토리에 남지 않는다
 */
export function captureOauthCallback(
  deps: CaptureDeps = defaultCaptureDeps(),
): OauthCallbackDecision {
  const params = parseOauthCallbackParams(deps.search());
  const pending = deps.takePending();
  const decision = decideOauthCallback(params, pending, deps.now());

  deps.scrubUrl();

  if (decision.kind === "abort") {
    // ⚠️ `abortReason`으로 남긴다 — `reason` 자체는 허용 키지만 사유 축을 이름으로 구분한다.
    logger.warn("Google 로그인 중단", { abortReason: decision.reason });
  }
  return decision;
}

// ─────────────────────────────── ② 비동기 교환 ───────────────────────────────

export type OauthCallbackOutcome =
  | { readonly kind: "success"; readonly returnTo: AppState }
  | { readonly kind: "failed"; readonly reason: LoginFailureReason };

export interface RunDeps {
  /** 교환 요청에 실을 값. **개시 때와 문자 단위로 같아야 한다**(서버가 완전 일치로 검사한다). */
  readonly redirectUri: string;
  readonly exchange: (req: GoogleExchangeRequest) => Promise<GoogleExchangeOutcome>;
  readonly applySession: (result: GoogleLoginResult) => void;
  readonly now: () => number;
}

export function defaultRunDeps(): RunDeps {
  return {
    redirectUri: oauthRedirectUri(window.location.origin),
    exchange: (req) => exchangeGoogleCode(req),
    applySession: (result) => {
      // 토큰을 **먼저** 넣는다 — 사용자 통지에 반응하는 첫 요청(qrUsage 재조회 등)에
      // Bearer가 붙어야 한다.
      setToken(result.token, result.expiresInSeconds, Date.now());
      sessionStore.getState().login(result.user);
    },
    now: () => Date.now(),
  };
}

/** abort면 **교환하지 않는다**(네트워크 요청 0건). */
export async function runOauthCallback(
  decision: OauthCallbackDecision,
  deps: RunDeps = defaultRunDeps(),
): Promise<OauthCallbackOutcome> {
  if (decision.kind === "abort") {
    return { kind: "failed", reason: abortReasonToLoginFailure(decision.reason) };
  }

  const startedAt = deps.now();
  const outcome = await deps.exchange({
    code: decision.code,
    codeVerifier: decision.codeVerifier,
    redirectUri: deps.redirectUri,
    nonce: decision.nonce,
  });

  if (!outcome.ok) {
    logger.warn("Google 로그인 교환 실패", {
      failureReason: outcome.reason,
      elapsedMs: Math.round(deps.now() - startedAt),
    });
    return { kind: "failed", reason: outcome.reason };
  }

  deps.applySession(outcome.result);
  return { kind: "success", returnTo: decision.returnTo };
}

// ─────────────────────────────── ③ 결과 반영 ───────────────────────────────

export interface ApplyDeps {
  readonly go: (to: AppState) => void;
  readonly fail: (reason: LoginFailureReason) => void;
  /** 로그인 성공 시 진단 흔적을 지운다(진단 [마지막 로그인 실패] 행 — 07 §2.5). */
  readonly clearLastFailure: () => void;
}

export function defaultApplyDeps(): ApplyDeps {
  return {
    go: (to) => {
      shellStore.getState().go(to);
    },
    fail: (reason) => loginStore.getState().fail(reason),
    clearLastFailure: () => loginStore.getState().clearLastFailure(),
  };
}

/**
 * `success` → 복귀 화면 · `failed` → 오류를 실어 `Login`으로.
 * `returnTo`는 이미 콜드 스타트에서 합법인 4종으로 clamp돼 있어 `go()`가 거부되지 않는다.
 */
export function applyOauthCallbackOutcome(
  outcome: OauthCallbackOutcome,
  deps: ApplyDeps = defaultApplyDeps(),
): void {
  if (outcome.kind === "success") {
    // 진단 흔적은 **성공에서만** 지운다 — `Login` 화면을 여는 것만으로 사라지면 쓸모가 없다.
    deps.clearLastFailure();
    deps.go(outcome.returnTo);
    return;
  }
  // 문구를 먼저 실어야 `Login`이 첫 페인트부터 오류를 보여준다.
  deps.fail(outcome.reason);
  deps.go("Login");
}
