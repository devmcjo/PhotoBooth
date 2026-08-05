/**
 * 서버 구성값 로드 — env / Secret Manager에서 시크릿·설정을 읽는다(설계 §8.2).
 *
 * 코드/리포에 시크릿을 하드코딩하지 않는다. 로컬/Emulator는 .env(gitignore),
 * 배포는 Functions secrets(firebase functions:secrets:set)로 주입된다.
 */
import type { OAuthClientKind } from "./domain/validation";

/** 한 종류의 OAuth 클라이언트 자격(code 교환에 함께 쓰인다). */
export interface OAuthClientPair {
  clientId: string;
  clientSecret: string;
}

export interface AppConfig {
  /** JWT 서명 시크릿(HS256). */
  jwtSecret: string;
  /** JWT 만료(초). */
  jwtExpiresInSeconds: number;
  /** 배포별 클라이언트 API 키 목록(게스트 엔드포인트 게이트). */
  clientApiKeys: string[];
  /** Storage 버킷(서명 URL·토큰 URL 조립). */
  storageBucket: string;
  /** 모바일 다운로드 페이지 base URL(domain/session.ts downloadPageUrl 조립에 사용). */
  hostingBaseUrl: string;
  /**
   * item1b: Google OAuth 클라이언트 ID(비밀 아님, env). 미설정이면 /auth/google 비활성(§8.2).
   * 클라(WPF)도 이 값을 알아야 authorize URL을 조립하지만, 서버는 code 교환·id_token audience 검증에 쓴다.
   */
  googleOAuthClientId: string;
  /**
   * item1b: Google OAuth 클라이언트 secret(Secret Manager 주입, /auth/google 사용 시 필수).
   * code 교환에만 쓰이며 **백엔드 전용**(클라 미보관, §8.2). 로그에 노출 금지.
   */
  googleOAuthClientSecret: string;
  /**
   * item1b: Google 로그인 활성화 여부(client id·secret이 모두 있을 때만 true).
   * false면 /auth/google는 501(구성 오류)로 응답한다(§8.2, "사용 시에만 자격 강제" 원칙).
   */
  googleOAuthEnabled: boolean;
  /**
   * item1b: 허용 Workspace hosted domain(선택). 설정 시 id_token.hd가 이 값과 일치해야 로그인 허용(§6.5).
   * 미설정(빈 문자열)이면 hd 제한 없음(email 매핑 화이트리스트로만 통제).
   */
  googleAllowedHd: string;
  /**
   * B2: 종류별 OAuth 클라이언트. 구성되지 않은 종류는 키가 없다.
   *   desktop = `GOOGLE_OAUTH_CLIENT_ID` + `GOOGLE_OAUTH_CLIENT_SECRET`(기존)
   *   web     = `GOOGLE_OAUTH_CLIENT_ID_WEB` + `GOOGLE_OAUTH_CLIENT_SECRET_WEB`(신규)
   */
  googleOAuthClients: Partial<Record<OAuthClientKind, OAuthClientPair>>;
  /**
   * B2: 허용 id_token audience 목록 = 구성된 모든 client_id.
   * code 교환이 이미 한 클라이언트로 고정되므로 이 목록은 **우리 소유 클라이언트끼리만** 넓힌다.
   */
  googleOAuthAudiences: string[];
  /**
   * B1: loopback이 아닌 `redirectUri`의 허용 목록(**완전 일치만**).
   * `OAUTH_REDIRECT_ALLOWLIST`(CSV, env)로 주입해 코드 재배포 없이 도메인을 추가한다.
   * prefix·정규식 매칭을 쓰지 않는 이유: open redirect·SSRF 통로가 된다(08 §4.1).
   */
  oauthRedirectAllowlist: string[];
}

/** 쉼표 구분 문자열을 트림된 비어있지 않은 항목 배열로. */
function parseCsv(value: string | undefined): string[] {
  if (!value) return [];
  return value
    .split(",")
    .map((s) => s.trim())
    .filter((s) => s.length > 0);
}

let cached: AppConfig | null = null;

/**
 * 구성 로드(1회 캐시). 필수 값이 없으면 예외로 조기 실패(오구성 배포 방지).
 */
export function loadConfig(): AppConfig {
  if (cached) return cached;

  const jwtSecret = process.env.JWT_SECRET ?? "";
  if (!jwtSecret) {
    throw new Error(
      "JWT_SECRET 미설정 — 서버 시크릿이 필요합니다(.env 또는 Functions secrets)."
    );
  }

  const clientApiKeys = parseCsv(process.env.CLIENT_API_KEYS);
  if (clientApiKeys.length === 0) {
    throw new Error(
      "CLIENT_API_KEYS 미설정 — 최소 1개의 배포 클라이언트 키가 필요합니다."
    );
  }

  const storageBucket = process.env.STORAGE_BUCKET ?? "";
  if (!storageBucket) {
    throw new Error("STORAGE_BUCKET 미설정 — 버킷명이 필요합니다.");
  }

  const expiresRaw = process.env.JWT_EXPIRES_IN_SECONDS;
  const jwtExpiresInSeconds = expiresRaw ? Number.parseInt(expiresRaw, 10) : 28800;
  if (!Number.isInteger(jwtExpiresInSeconds) || jwtExpiresInSeconds <= 0) {
    throw new Error("JWT_EXPIRES_IN_SECONDS가 올바르지 않습니다(양의 정수).");
  }

  // item1b: Google OAuth. 활성화 신호는 **CLIENT_ID(비밀 아님·env)** 다.
  //  - defineSecret 모델에선 GOOGLE_OAUTH_CLIENT_SECRET이 배포 시 항상 존재해야 하므로
  //    (SSO 미사용이어도 placeholder 등록 필요), "시크릿만 있고 id 없음"은 **정상 비활성** 상태다.
  //  - 따라서 대칭 검사(hasId !== hasSecret)는 쓰지 않는다. id를 켰는데 시크릿이 없을 때만 오구성으로 조기 실패.
  //  - 둘 다(혹은 id) 없으면 비활성(/auth/google는 501).
  const googleOAuthClientId = (process.env.GOOGLE_OAUTH_CLIENT_ID ?? "").trim();
  const googleOAuthClientSecret = (process.env.GOOGLE_OAUTH_CLIENT_SECRET ?? "").trim();

  // B2: 종류별로 같은 규칙을 적용한다 — id를 켰는데 secret이 없으면 조기 실패.
  //     종류가 늘어도 규칙이 갈라지지 않게 한 곳에서 돈다.
  const clientSources: {
    kind: OAuthClientKind;
    idVar: string;
    secretVar: string;
    clientId: string;
    clientSecret: string;
  }[] = [
    {
      kind: "desktop",
      idVar: "GOOGLE_OAUTH_CLIENT_ID",
      secretVar: "GOOGLE_OAUTH_CLIENT_SECRET",
      clientId: googleOAuthClientId,
      clientSecret: googleOAuthClientSecret,
    },
    {
      kind: "web",
      idVar: "GOOGLE_OAUTH_CLIENT_ID_WEB",
      secretVar: "GOOGLE_OAUTH_CLIENT_SECRET_WEB",
      clientId: (process.env.GOOGLE_OAUTH_CLIENT_ID_WEB ?? "").trim(),
      clientSecret: (process.env.GOOGLE_OAUTH_CLIENT_SECRET_WEB ?? "").trim(),
    },
  ];

  const googleOAuthClients: Partial<Record<OAuthClientKind, OAuthClientPair>> = {};
  for (const source of clientSources) {
    const hasId = source.clientId.length > 0;
    const hasSecret = source.clientSecret.length > 0;
    if (hasId && !hasSecret) {
      throw new Error(
        `${source.idVar}가 설정됐지만 ${source.secretVar}이 없습니다 — SSO 활성화엔 둘 다 필요합니다.`
      );
    }
    if (hasId && hasSecret) {
      googleOAuthClients[source.kind] = {
        clientId: source.clientId,
        clientSecret: source.clientSecret,
      };
    }
  }

  const googleOAuthAudiences = Object.values(googleOAuthClients).map((c) => c.clientId);
  // B2: 활성 판정은 "구성된 클라이언트가 하나 이상"이다(종전은 desktop 단일 기준).
  const googleOAuthEnabled = googleOAuthAudiences.length > 0;
  const googleAllowedHd = (process.env.GOOGLE_ALLOWED_HD ?? "").trim();

  cached = {
    jwtSecret,
    jwtExpiresInSeconds,
    clientApiKeys,
    storageBucket,
    hostingBaseUrl: process.env.HOSTING_BASE_URL ?? "",
    googleOAuthClientId,
    googleOAuthClientSecret,
    googleOAuthEnabled,
    googleAllowedHd,
    googleOAuthClients,
    googleOAuthAudiences,
    oauthRedirectAllowlist: parseCsv(process.env.OAUTH_REDIRECT_ALLOWLIST),
  };
  return cached;
}

/** 테스트/재구성용 캐시 리셋. */
export function resetConfigCache(): void {
  cached = null;
}
