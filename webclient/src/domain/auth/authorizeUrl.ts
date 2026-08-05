/**
 * Google 인가 URL 조립(순수) — 07 §2.2
 *
 * ⚠️ `URLSearchParams`를 쓰지 않는다. 공백을 `+`로 인코딩해 규격
 *    (`scope=openid%20email%20profile`)과 어긋나기 때문이다. 각 값에
 *    `encodeURIComponent`를 적용해 직접 조립하고, **파라미터 순서까지** 테스트로 고정한다.
 */

export const GOOGLE_AUTHORIZE_ENDPOINT = "https://accounts.google.com/o/oauth2/v2/auth";
export const OAUTH_SCOPE = "openid email profile";

/** Google Console에 **정확히 이 경로**로 등록돼 있다(완전 일치 — 07 §2.5). */
export const OAUTH_CALLBACK_PATH = "/oauth2callback";

/**
 * `https://host` → `https://host/oauth2callback`.
 * 트레일링 슬래시를 제거한 뒤 붙인다 — `https://host//oauth2callback`은 서버 허용 목록과 불일치다.
 */
export function oauthRedirectUri(origin: string): string {
  return `${origin.trim().replace(/\/+$/, "")}${OAUTH_CALLBACK_PATH}`;
}

export interface AuthorizeUrlInput {
  readonly clientId: string;
  readonly redirectUri: string;
  readonly codeChallenge: string;
  readonly state: string;
  readonly nonce: string;
}

/**
 * 인가 URL. **파라미터 순서와 인코딩이 계약이다**(테스트가 문자열 전체를 고정한다).
 *
 * ⚠️ `prompt=select_account`는 **생략 불가**다(07 §2.2). 공용 키오스크의 브라우저에
 *    직전 손님(또는 운영자)의 Google 세션이 남으면 이 파라미터 없이는 자격증명 입력 없이
 *    원탭으로 남의 계정에 로그인된다.
 * ⚠️ `access_type=offline`·`prompt=consent`를 넣지 않는다 — refresh token을 쓰지 않는다
 *    (analysis/61 §3.0).
 */
export function buildAuthorizeUrl(input: AuthorizeUrlInput): string {
  const params = [
    `client_id=${encodeURIComponent(input.clientId)}`,
    `redirect_uri=${encodeURIComponent(input.redirectUri)}`,
    "response_type=code",
    `scope=${encodeURIComponent(OAUTH_SCOPE)}`,
    `code_challenge=${encodeURIComponent(input.codeChallenge)}`,
    "code_challenge_method=S256",
    `state=${encodeURIComponent(input.state)}`,
    `nonce=${encodeURIComponent(input.nonce)}`,
    "prompt=select_account",
  ];
  return `${GOOGLE_AUTHORIZE_ENDPOINT}?${params.join("&")}`;
}
