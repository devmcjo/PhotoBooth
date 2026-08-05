/**
 * 빌드 주입값(`import.meta.env`) 검증·정규화 — 01 §4.1
 *
 * 규칙:
 * - `BackendBaseUrl`은 트레일링 슬래시를 **부여**하고 `HostingBaseUrl`은 **제거**한다.
 *   방향이 반대이므로 **같은 함수를 쓰지 않는다**(analysis/41 §2.1 경고).
 * - 값이 비어 있으면 기본값(빌드 주입 폴백)으로 대체한다. 빈 문자열이 그대로 흘러가면
 *   재배포로 값을 바꿀 수 없고 로그인 버튼이 영구히 사라진다(05 §2.2).
 * - 필수값(게이트 키) 부재는 **경고만** 남긴다. 크래시 금지.
 */

export const ENV_DEFAULTS = {
  backendBaseUrl: "https://asia-northeast3-mcphoto-955fb.cloudfunctions.net/api",
  hostingBaseUrl: "https://mcphoto-955fb.web.app",
  storageBucket: "mcphoto-955fb.firebasestorage.app",
  appVersion: "0.0.0",
} as const;

export interface EnvConfig {
  /** 트레일링 슬래시 **있음** */
  readonly backendBaseUrl: string;
  /** 배포 게이트 키. 빈 문자열이면 미설정(진단 화면에 "미설정" 표시) */
  readonly backendApiKey: string;
  /** 빈 문자열이면 로그인 버튼을 숨긴다(analysis/61 §8) */
  readonly googleClientId: string;
  /** 트레일링 슬래시 **없음** */
  readonly hostingBaseUrl: string;
  readonly storageBucket: string;
  readonly appVersion: string;
  /** 진단 화면 전용. 하단 캡션에 쓰지 않는다(it18) */
  readonly buildDate: string;
}

/** `https://a/b/` — 트레일링 슬래시를 부여한다(BackendBaseUrl 전용) */
export function ensureTrailingSlash(url: string): string {
  const trimmed = url.trim();
  if (trimmed.length === 0) return trimmed;
  return trimmed.endsWith("/") ? trimmed : `${trimmed}/`;
}

/** `https://a/b` — 트레일링 슬래시를 제거한다(HostingBaseUrl 전용) */
export function stripTrailingSlash(url: string): string {
  let trimmed = url.trim();
  while (trimmed.length > 1 && trimmed.endsWith("/")) {
    trimmed = trimmed.slice(0, -1);
  }
  return trimmed;
}

/** 빈 값·공백만 있는 값을 폴백으로 대체한다 */
function orDefault(raw: string | undefined, fallback: string): string {
  const value = (raw ?? "").trim();
  return value.length > 0 ? value : fallback;
}

export interface EnvResolution {
  readonly config: EnvConfig;
  /**
   * 부트스트랩 경고. 로그 스토어 초기화 직후 `logger.warn`으로 flush한다(01 §4.2 2단계).
   * `console.*`을 직접 부르지 않는다(로그 스토어 우회 금지 — 01 §8).
   */
  readonly warnings: readonly string[];
}

export function resolveEnv(raw: Partial<Record<string, string>>): EnvResolution {
  const warnings: string[] = [];

  const backendApiKey = (raw.VITE_BACKEND_API_KEY ?? "").trim();
  if (backendApiKey.length === 0) {
    warnings.push(
      "VITE_BACKEND_API_KEY가 비어 있습니다. 배포 게이트 키 없이는 백엔드 호출이 401로 거부됩니다.",
    );
  }

  const googleClientId = (raw.VITE_GOOGLE_CLIENT_ID ?? "").trim();
  if (googleClientId.length === 0) {
    warnings.push("VITE_GOOGLE_CLIENT_ID가 비어 있습니다. 로그인 버튼을 숨깁니다.");
  }

  const config: EnvConfig = {
    backendBaseUrl: ensureTrailingSlash(
      orDefault(raw.VITE_BACKEND_BASE_URL, ENV_DEFAULTS.backendBaseUrl),
    ),
    backendApiKey,
    googleClientId,
    hostingBaseUrl: stripTrailingSlash(
      orDefault(raw.VITE_HOSTING_BASE_URL, ENV_DEFAULTS.hostingBaseUrl),
    ),
    storageBucket: orDefault(raw.VITE_STORAGE_BUCKET, ENV_DEFAULTS.storageBucket),
    appVersion: orDefault(raw.VITE_APP_VERSION, ENV_DEFAULTS.appVersion),
    buildDate: (raw.VITE_BUILD_DATE ?? "").trim(),
  };

  return { config, warnings };
}

/** 화면 하단 캡션 문자열. 배포 채널·빌드 시각을 넣지 않는다(it18 — 05 §8.2) */
export function versionCaption(appVersion: string): string {
  const value = appVersion.trim();
  return `v${value.length > 0 ? value : ENV_DEFAULTS.appVersion}`;
}

const resolution = resolveEnv(import.meta.env as unknown as Partial<Record<string, string>>);

export const env: EnvConfig = resolution.config;
export const envWarnings: readonly string[] = resolution.warnings;
