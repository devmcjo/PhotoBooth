import { parseSessionUser, type SessionUser } from "@domain/accounts/sessionUser";
import { buildAuthorizeUrl, oauthRedirectUri } from "@domain/auth/authorizeUrl";
import type { LoginFailureReason } from "@domain/auth/loginFailure";
import type { OauthPendingState } from "@domain/auth/oauthCallbackPolicy";
import type { AppState } from "@domain/navigation/appState";
import { getBackendClient, type BackendClient } from "@adapters/http/backendClient";
import {
  BackendError,
  NetworkError,
  SsoNotConfiguredError,
} from "@adapters/http/errors";
import { logger } from "@adapters/storage/logStore";
import { createPkce, randomUrlSafeToken, type PkcePair } from "./pkce";
import { clearPendingOauth, savePendingOauth } from "./oauthStateStore";
import { env } from "../../env";

/**
 * Google SSO 어댑터 — 리디렉트 개시 + code 교환 (07 §2)
 *
 * ⚠️ `POST /auth/google`은 **`auth: "none"`** 이다. 로그인 전이라 Bearer가 없고 서버는
 *    API 키 게이트만 요구한다(`web/functions/src/routes/auth.ts:34-65`).
 *    `auth:"required"`로 두면 토큰이 없어 **요청조차 나가지 않는다**.
 * ⚠️ 그래서 `accountService`(전 메서드 `auth:"required"`)에 넣지 않고 별 파일로 둔다 —
 *    "이 서비스는 Bearer가 필요하다"는 불변식을 흐리지 않기 위해서다.
 * ⚠️ **예외를 전파하지 않는다**(15 §2). 결과는 판별 유니온이다.
 * ⚠️ authorize URL·`code`·`state`·`nonce`·`codeVerifier`·토큰을 **로그에 남기지 않는다**
 *    (키 이름 자체가 마스킹 대상이다 — `logPolicy.ts`).
 */

/** ★ 미지정은 서버가 **desktop**으로 취급한다(하위 호환) → 웹은 반드시 이 값을 보낸다. */
export const OAUTH_CLIENT_KIND = "web" as const;

/** 서버 `expiresIn`이 유효하지 않을 때의 폴백(8시간 — analysis/61 §6). */
export const DEFAULT_TOKEN_EXPIRES_IN_SECONDS = 28_800;

// ─────────────────────────────── 리디렉트 개시 ───────────────────────────────

export interface StartSignInDeps {
  /** `env.googleClientId`. 빈 값이면 시작하지 않는다. */
  readonly clientId: string;
  /** `location.origin`. */
  readonly origin: string;
  readonly createPkce: () => Promise<PkcePair | null>;
  readonly randomToken: () => string;
  readonly savePending: (state: OauthPendingState) => boolean;
  /** `location.assign` — 이 호출 뒤 페이지가 사라진다. */
  readonly assign: (url: string) => void;
  readonly now: () => number;
}

export type StartSignInOutcome =
  | { readonly ok: true }
  | { readonly ok: false; readonly reason: LoginFailureReason };

/** 실제 배선. **호출 시점에** 브라우저 전역을 해석한다(모듈 로드 시 접근하지 않는다). */
export function defaultStartDeps(): StartSignInDeps {
  return {
    clientId: env.googleClientId,
    origin: window.location.origin,
    createPkce: () => createPkce(),
    randomToken: () => randomUrlSafeToken(),
    savePending: (state) => savePendingOauth(state),
    assign: (url) => window.location.assign(url),
    now: () => Date.now(),
  };
}

/**
 * 리디렉트 개시. 성공하면 **곧 페이지가 사라진다** — 호출자는 버튼을 비활성으로 두고 기다린다.
 *
 * 실패 경로에서는 **`assign`을 부르지 않는다**(pending 없이 Google에 다녀오면 콜백이
 * 무조건 "취소"로 끝난다).
 */
export async function startGoogleSignIn(
  input: { readonly returnTo: AppState },
  deps: StartSignInDeps = defaultStartDeps(),
): Promise<StartSignInOutcome> {
  if (deps.clientId.length === 0) {
    logger.warn("Google 로그인 시작 불가 — client_id 미구성");
    return { ok: false, reason: "clientNotConfigured" };
  }

  const pkce = await deps.createPkce();
  if (pkce === null) {
    // 사유는 `createPkce`가 이미 로그로 남겼다(보안 컨텍스트·subtle 부재).
    return { ok: false, reason: "network" };
  }

  const state = deps.randomToken();
  const nonce = deps.randomToken();
  if (state.length === 0 || nonce.length === 0) {
    logger.error("Google 로그인 시작 불가 — 난수 생성 실패");
    return { ok: false, reason: "network" };
  }

  const pending: OauthPendingState = {
    codeVerifier: pkce.codeVerifier,
    state,
    nonce,
    returnTo: input.returnTo,
    startedAt: deps.now(),
  };

  if (!deps.savePending(pending)) {
    // 부분 저장 잔재를 남기지 않는다 — 다음 시도가 낡은 state로 실패하면 원인을 못 찾는다.
    clearPendingOauth();
    logger.error("Google 로그인 시작 불가 — 임시 상태를 저장할 수 없다(프라이빗 모드 의심)");
    return { ok: false, reason: "network" };
  }

  const url = buildAuthorizeUrl({
    clientId: deps.clientId,
    redirectUri: oauthRedirectUri(deps.origin),
    codeChallenge: pkce.codeChallenge,
    state,
    nonce,
  });

  // ⚠️ URL 전체를 남기지 않는다(state·nonce가 들어 있다). 남기는 것은 복귀 지점뿐이다.
  logger.info("Google 로그인 리디렉트", { returnTo: input.returnTo });
  deps.assign(url);
  return { ok: true };
}

// ──────────────────────────────── code 교환 ────────────────────────────────

export interface GoogleLoginResult {
  readonly token: string;
  readonly expiresInSeconds: number;
  readonly user: SessionUser;
}

export type GoogleExchangeOutcome =
  | { readonly ok: true; readonly result: GoogleLoginResult }
  | { readonly ok: false; readonly reason: LoginFailureReason };

export interface GoogleExchangeRequest {
  readonly code: string;
  readonly codeVerifier: string;
  readonly redirectUri: string;
  readonly nonce: string;
}

/** 오류 → 사유 매핑의 **유일한 지점**. 상태코드를 화면에 흩뿌리지 않는다. */
function classifyExchangeError(err: unknown): LoginFailureReason {
  if (err instanceof SsoNotConfiguredError) return "notConfigured";
  if (err instanceof NetworkError) return "network";
  if (err instanceof BackendError) {
    if (err.status === 401) return "rejected";
    if (err.status === 400) {
      // 정상 흐름에서는 발생하지 않는다 — 발생하면 서버 허용 목록 미등록·오타다.
      // ⚠️ 키를 `code`로 쓰면 `[masked]`가 된다(15 §4 함정 #1) → `errorCode`.
      logger.error("서버가 redirectUri를 거부했다(B1 미적용 가능)", {
        status: 400,
        errorCode: err.code,
      });
      return "redirectRejected";
    }
  }
  return "network";
}

/**
 * `POST /auth/google` — 본문 `{ code, codeVerifier, redirectUri, nonce, clientKind: "web" }`.
 *
 * 매핑: 501 → notConfigured · 401 → rejected · 400 → redirectRejected ·
 *       그 외·네트워크·응답 형식 오류 → network.
 */
export async function exchangeGoogleCode(
  req: GoogleExchangeRequest,
  client: BackendClient = getBackendClient(),
): Promise<GoogleExchangeOutcome> {
  try {
    const raw = await client.request<unknown>({
      method: "POST",
      path: "auth/google",
      // 로그인 전이라 Bearer가 없다. 게이트 키만 붙는다(F1).
      auth: "none",
      body: {
        code: req.code,
        codeVerifier: req.codeVerifier,
        redirectUri: req.redirectUri,
        nonce: req.nonce,
        clientKind: OAUTH_CLIENT_KIND,
      },
    });

    const record = typeof raw === "object" && raw !== null ? (raw as Record<string, unknown>) : null;
    const token = typeof record?.token === "string" ? record.token.trim() : "";
    const user = parseSessionUser(record?.user);

    if (token.length === 0 || user === null) {
      // 200이지만 계약과 다르다 → 세션을 세우지 않는다(빈 토큰으로 Bearer를 붙이면 401 루프다).
      logger.error("로그인 응답 형식 오류", {
        hasToken: token.length > 0,
        hasUser: user !== null,
      });
      return { ok: false, reason: "network" };
    }

    const rawExpires = record?.expiresIn;
    let expiresInSeconds =
      typeof rawExpires === "number" && Number.isFinite(rawExpires) && rawExpires > 0
        ? Math.floor(rawExpires)
        : 0;
    if (expiresInSeconds === 0) {
      logger.warn("로그인 응답의 expiresIn이 유효하지 않아 기본값을 쓴다", {
        expiresInSec: DEFAULT_TOKEN_EXPIRES_IN_SECONDS,
      });
      expiresInSeconds = DEFAULT_TOKEN_EXPIRES_IN_SECONDS;
    }

    // ⚠️ `email`을 남기지 않는다(개인정보 — 표시에만 쓴다).
    logger.info("로그인 성공", {
      userId: user.id,
      role: user.role,
      expiresInSec: expiresInSeconds,
    });
    return { ok: true, result: { token, expiresInSeconds, user } };
  } catch (err) {
    return { ok: false, reason: classifyExchangeError(err) };
  }
}
