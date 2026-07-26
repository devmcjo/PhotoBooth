/**
 * 서버 구성값 로드 — env / Secret Manager에서 시크릿·설정을 읽는다(설계 §8.2).
 *
 * 코드/리포에 시크릿을 하드코딩하지 않는다. 로컬/Emulator는 .env(gitignore),
 * 배포는 Functions secrets(firebase functions:secrets:set)로 주입된다.
 */

/** 이메일 공급자 식별자(설계 §10). "log"=개발용 콘솔 sender(외부 의존 0). */
export type EmailProvider = "log" | "sendgrid";

export interface AppConfig {
  /** JWT 서명 시크릿(HS256). */
  jwtSecret: string;
  /** JWT 만료(초). */
  jwtExpiresInSeconds: number;
  /** 배포별 클라이언트 API 키 목록(게스트 엔드포인트 게이트). */
  clientApiKeys: string[];
  /** Storage 버킷(서명 URL·토큰 URL 조립). */
  storageBucket: string;
  /** 모바일 다운로드 페이지 base URL. */
  hostingBaseUrl: string;
  /** 이메일 공급자(기본 "log" — 개발/Emulator는 실제 메일 미발송, 설계 §10.2). */
  emailProvider: EmailProvider;
  /** 발신 주소(sendgrid 사용 시 필수, 콘솔 등록값). */
  emailFrom: string;
  /** SendGrid API 키(Secret Manager 주입, sendgrid 사용 시 필수). 로그에 노출 금지. */
  sendgridApiKey: string;
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
   * false면 /auth/google는 501(구성 오류)로 응답한다(§8.2, sendgrid와 동일한 "사용 시에만 강제" 원칙).
   */
  googleOAuthEnabled: boolean;
  /**
   * item1b: 허용 Workspace hosted domain(선택). 설정 시 id_token.hd가 이 값과 일치해야 로그인 허용(§6.5).
   * 미설정(빈 문자열)이면 hd 제한 없음(email 매핑 화이트리스트로만 통제).
   */
  googleAllowedHd: string;
}

/** env 문자열 → EmailProvider(미지정/미지원은 "log"로 폴백 = 개발 안전 기본). */
function parseEmailProvider(value: string | undefined): EmailProvider {
  return value === "sendgrid" ? "sendgrid" : "log";
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

  const emailProvider = parseEmailProvider(process.env.EMAIL_PROVIDER);
  const emailFrom = process.env.EMAIL_FROM ?? "";
  const sendgridApiKey = process.env.SENDGRID_API_KEY ?? "";
  // sendgrid 선택 시에만 자격을 강제(개발 기본 "log"는 외부 의존 0이라 자격 불요).
  if (emailProvider === "sendgrid") {
    if (!emailFrom) {
      throw new Error("EMAIL_FROM 미설정 — sendgrid 사용 시 발신 주소가 필요합니다.");
    }
    if (!sendgridApiKey) {
      throw new Error(
        "SENDGRID_API_KEY 미설정 — sendgrid 사용 시 API 키가 필요합니다(Functions secrets)."
      );
    }
  }

  // item1b: Google OAuth. 활성화 신호는 **CLIENT_ID(비밀 아님·env)** 다.
  //  - defineSecret 모델에선 GOOGLE_OAUTH_CLIENT_SECRET이 배포 시 항상 존재해야 하므로
  //    (SSO 미사용이어도 placeholder 등록 필요), "시크릿만 있고 id 없음"은 **정상 비활성** 상태다.
  //  - 따라서 대칭 검사(hasId !== hasSecret)는 쓰지 않는다. id를 켰는데 시크릿이 없을 때만 오구성으로 조기 실패.
  //  - 둘 다(혹은 id) 없으면 비활성(/auth/google는 501).
  const googleOAuthClientId = (process.env.GOOGLE_OAUTH_CLIENT_ID ?? "").trim();
  const googleOAuthClientSecret = (process.env.GOOGLE_OAUTH_CLIENT_SECRET ?? "").trim();
  const hasId = googleOAuthClientId.length > 0;
  const hasSecret = googleOAuthClientSecret.length > 0;
  if (hasId && !hasSecret) {
    throw new Error(
      "GOOGLE_OAUTH_CLIENT_ID가 설정됐지만 GOOGLE_OAUTH_CLIENT_SECRET이 없습니다 — SSO 활성화엔 둘 다 필요합니다."
    );
  }
  const googleOAuthEnabled = hasId && hasSecret;
  const googleAllowedHd = (process.env.GOOGLE_ALLOWED_HD ?? "").trim();

  cached = {
    jwtSecret,
    jwtExpiresInSeconds,
    clientApiKeys,
    storageBucket,
    hostingBaseUrl: process.env.HOSTING_BASE_URL ?? "",
    emailProvider,
    emailFrom,
    sendgridApiKey,
    googleOAuthClientId,
    googleOAuthClientSecret,
    googleOAuthEnabled,
    googleAllowedHd,
  };
  return cached;
}

/** 테스트/재구성용 캐시 리셋. */
export function resetConfigCache(): void {
  cached = null;
}
