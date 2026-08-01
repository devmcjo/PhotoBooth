/**
 * Google SSO 검증 서비스 (설계 §5.3) — google-auth-library 의존을 이 파일에 격리한다.
 *
 * 흐름: 클라가 시스템 브라우저 + loopback + PKCE로 받은 authorization code를 백엔드로 전달하면,
 *  (1) OAuth2Client.getToken 으로 code 교환(client_secret 백엔드 전용) → id_token 획득,
 *  (2) verifyIdToken 으로 Google 공개키 서명·exp·iss 검증(라이브러리) + aud/iss/exp/email_verified/
 *      nonce/hd 를 코드에서 방어적 재확인(A+B 이중화, §4 하이브리드),
 *  (3) 검증된 email(소문자 정규화)을 반환한다.
 *
 * 계정 매핑·JWT 발급은 이 파일의 책임이 아니다(관심사 분리 — routes/auth.ts + services/accounts.ts).
 * 실패는 모두 GoogleAuthError로 신호하고, 라우트가 일반화 401로 매핑한다(사유는 로그만, §6.4·§8.6).
 * 토큰·code·email 은 로그에 남기지 않는다(§8.6).
 */
import { OAuth2Client } from "google-auth-library";
import type { TokenPayload } from "google-auth-library";

/** 허용되는 issuer(§5.3-2). */
const ALLOWED_ISS = ["https://accounts.google.com", "accounts.google.com"];

/**
 * Google 검증 실패의 종류.
 *
 * - `clientConfig`: **우리 서버의 OAuth 클라이언트 자격이 틀렸다**(client_id/secret이 Google에 등록된
 *   값과 불일치). 계정 존재 여부와 무관하므로 401 일반화(열거 방지)의 대상이 아니다 → 라우트가 501.
 * - `rejected`: 그 외 전부(만료·재사용 code, nonce 불일치, hd 불일치, email 미검증 …) → 라우트가 401.
 */
export type GoogleAuthErrorKind = "clientConfig" | "rejected";

/**
 * Google 검증 실패. reason은 서버 로그 전용(email·토큰 미포함).
 * 라우트는 `kind`에 따라 501(구성 오류) 또는 일반화 401(열거 방지)로 변환한다.
 *
 * ⚠️ 기본값이 `"rejected"`인 이유: 기존 throw 지점을 하나도 고치지 않아도 종전 동작(401)이 유지된다.
 */
export class GoogleAuthError extends Error {
  readonly kind: GoogleAuthErrorKind;

  constructor(message: string, kind: GoogleAuthErrorKind = "rejected") {
    super(message);
    this.name = "GoogleAuthError";
    this.kind = kind;
  }
}

/**
 * Google 토큰 엔드포인트 오류 메시지가 **클라이언트 자격 오류**를 가리키는가(순수 판정).
 *
 * ⚠️ `invalid_grant`를 여기 넣지 마라 — 만료·재사용된 code에서도 나오며 그것은 손님 흐름의 문제다
 * (재시도로 해결된다). 구성 오류로 표시하면 운영자가 없는 문제를 찾는다.
 */
export function isClientCredentialError(message: string): boolean {
  const lower = message.toLowerCase();
  return lower.includes("invalid_client") || lower.includes("unauthorized_client");
}

/** googleAuth가 필요로 하는 OAuth2Client의 최소 표면(테스트에서 mock 주입 가능). */
export interface OAuth2ClientLike {
  getToken(options: {
    code: string;
    codeVerifier?: string;
    redirect_uri?: string;
  }): Promise<{ tokens: { id_token?: string | null } }>;
  verifyIdToken(options: {
    idToken: string;
    audience?: string | string[];
  }): Promise<{ getPayload(): TokenPayload | undefined }>;
}

/** OAuth2Client 생성에 필요한 구성. */
export interface GoogleAuthConfig {
  /** code 교환에 쓸 클라이언트 id(선택된 `clientKind`의 것). */
  clientId: string;
  /** code 교환에 쓸 클라이언트 secret(선택된 `clientKind`의 것). */
  clientSecret: string;
  /** 허용 hosted domain(빈 문자열이면 hd 제한 없음, §6.5). */
  allowedHd?: string;
  /**
   * B2: 허용 id_token audience 목록(구성된 모든 client_id).
   * 비었으면 `[clientId]`로 폴백한다(하위 호환 — 종전 단일 client_id 동작).
   */
  audiences?: string[];
}

/**
 * 허용 audience 목록. 목록이 없으면 code 교환에 쓴 client_id 하나만 허용한다.
 *
 * code 교환이 이미 한 클라이언트로 고정되므로 이 목록은 **우리가 소유한 클라이언트끼리만** 넓힌다
 * (외부 client_id는 목록에 없으므로 통과하지 못한다).
 */
export function acceptableAudiences(cfg: GoogleAuthConfig): string[] {
  const list = (cfg.audiences ?? []).filter((a) => a.length > 0);
  return list.length > 0 ? list : [cfg.clientId];
}

/** 검증 입력(클라가 /auth/google로 전달한, 이미 형식 검증된 값). */
export interface GoogleVerifyInput {
  code: string;
  codeVerifier: string;
  redirectUri: string;
  /** 있으면 id_token.nonce와 대조(§8.4). 없으면 nonce 검증 생략. */
  nonce?: string;
}

/**
 * OAuth2Client 생성자 팩토리. 테스트에서 mock 주입점.
 * 기본은 실제 google-auth-library의 OAuth2Client를 생성한다.
 */
export type OAuth2ClientFactory = (cfg: GoogleAuthConfig) => OAuth2ClientLike;

const defaultClientFactory: OAuth2ClientFactory = (cfg) =>
  new OAuth2Client({
    clientId: cfg.clientId,
    clientSecret: cfg.clientSecret,
  });

/**
 * id_token payload를 코드에서 방어적으로 재확인한다(라이브러리 검증에 더해, §5.3-2).
 * 실패 시 GoogleAuthError. 성공 시 소문자 정규화된 email 반환.
 */
export function assertPayloadAndExtractEmail(
  payload: TokenPayload | undefined,
  cfg: GoogleAuthConfig,
  input: GoogleVerifyInput
): string {
  if (!payload) {
    throw new GoogleAuthError("id_token payload가 비어 있습니다.");
  }
  // audience: 우리가 구성한 client_id 중 하나와 정확히 일치해야 한다(B2 — 목록화).
  if (typeof payload.aud !== "string" || !acceptableAudiences(cfg).includes(payload.aud)) {
    throw new GoogleAuthError("id_token audience 불일치.");
  }
  // issuer: Google 발행 여부.
  if (!ALLOWED_ISS.includes(payload.iss)) {
    throw new GoogleAuthError("id_token issuer 불일치.");
  }
  // 만료: 라이브러리도 검증하나 방어적 재확인(초 단위 Unix time).
  const nowSec = Math.floor(Date.now() / 1000);
  if (typeof payload.exp !== "number" || payload.exp <= nowSec) {
    throw new GoogleAuthError("id_token이 만료되었습니다.");
  }
  // nonce: 요청에 nonce가 있으면 payload.nonce와 일치해야 한다(replay 방어, §8.4).
  if (input.nonce !== undefined) {
    if (payload.nonce !== input.nonce) {
      throw new GoogleAuthError("id_token nonce 불일치.");
    }
  }
  // hosted domain(선택): 설정된 경우에만 강제(§6.5).
  if (cfg.allowedHd && cfg.allowedHd.length > 0) {
    if (payload.hd !== cfg.allowedHd) {
      throw new GoogleAuthError("허용되지 않은 hosted domain.");
    }
  }
  // email 소유 확인: Google이 email 소유를 확인했는지(§6.2).
  if (payload.email_verified !== true) {
    throw new GoogleAuthError("Google email이 미확인 상태입니다.");
  }
  if (typeof payload.email !== "string" || payload.email.length === 0) {
    throw new GoogleAuthError("id_token에 email이 없습니다.");
  }
  // findByIdOrEmail이 소문자 비교하므로 소문자 정규화(§5.3-3).
  return payload.email.trim().toLowerCase();
}

/**
 * code 교환 + id_token 검증 → 검증된 email(소문자) 반환.
 * @param cfg client id/secret + 허용 hd.
 * @param input 이미 형식 검증된 code/codeVerifier/redirectUri/nonce.
 * @param factory OAuth2Client 팩토리(테스트 mock 주입점, 기본은 실제 클라이언트).
 * @throws GoogleAuthError 검증 실패(라우트가 일반화 401로 매핑).
 */
export async function verifyGoogleCodeAndGetEmail(
  cfg: GoogleAuthConfig,
  input: GoogleVerifyInput,
  factory: OAuth2ClientFactory = defaultClientFactory
): Promise<string> {
  const client = factory(cfg);

  // (1) code 교환. redirect_uri는 클라가 실제 쓴 loopback 주소와 정확히 일치해야 성공(§4.2).
  let idToken: string | null | undefined;
  try {
    const { tokens } = await client.getToken({
      code: input.code,
      codeVerifier: input.codeVerifier,
      redirect_uri: input.redirectUri,
    });
    idToken = tokens.id_token;
  } catch (err) {
    // Google 오류 상세는 message만(토큰·code 미노출).
    // invalid_client / unauthorized_client 는 **우리 서버 구성 오류**이므로 401로 감추지 않는다
    // (계정 열거와 무관 — 라우트가 501로 매핑해 운영자가 원인을 볼 수 있게 한다).
    const detail = err instanceof Error ? err.message : "unknown";
    throw new GoogleAuthError(
      `code 교환 실패: ${detail}`,
      isClientCredentialError(detail) ? "clientConfig" : "rejected"
    );
  }
  if (!idToken) {
    throw new GoogleAuthError("code 교환 응답에 id_token이 없습니다.");
  }

  // (2) id_token 검증(서명·exp·iss는 라이브러리, aud 지정). payload 재확인은 (3)에서.
  let payload: TokenPayload | undefined;
  try {
    const ticket = await client.verifyIdToken({
      idToken,
      audience: acceptableAudiences(cfg),
    });
    payload = ticket.getPayload();
  } catch (err) {
    throw new GoogleAuthError(
      `id_token 검증 실패: ${err instanceof Error ? err.message : "unknown"}`
    );
  }

  // (3) payload 방어적 재확인 → email 추출.
  return assertPayloadAndExtractEmail(payload, cfg, input);
}
