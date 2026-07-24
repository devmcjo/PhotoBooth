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

  cached = {
    jwtSecret,
    jwtExpiresInSeconds,
    clientApiKeys,
    storageBucket,
    hostingBaseUrl: process.env.HOSTING_BASE_URL ?? "",
    emailProvider,
    emailFrom,
    sendgridApiKey,
  };
  return cached;
}

/** 테스트/재구성용 캐시 리셋. */
export function resetConfigCache(): void {
  cached = null;
}
