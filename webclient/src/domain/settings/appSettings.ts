import { clamp } from "../mathCompat";
import { AUTO_CUT_COUNT, isAutoCutCount } from "./cutCountPolicy";
import { normalizeQrToggles } from "./qrDeliveryPolicy";

/**
 * 앱 설정 모델·기본값·clamp — Windows `Settings/AppSettings.cs` 이식 (analysis/41 §2.1)
 *
 * ⚠️ **키 이름을 웹 관례(camelCase)로 바꾸지 않는다.** 내보내기 파일이 Windows·다른 클라이언트와
 *    호환되어야 한다(05 §2.1). 미노출 키(`DisplayMode`·`WindowBounds`·외부 장치)도 값을 보존한다(WD7·WD8).
 * ⚠️ `BackendApiKey`는 **설정에 저장하지 않는다**(analysis/41 §2.5) — 이 모델에 필드가 없는 것이 규격이다.
 */

export const ALLOWED_CUT_COUNTS: readonly number[] = [6, 8, 10];
export const ALLOWED_COUNTDOWN_SECS: readonly number[] = [3, 6, 8, 10];
export const ALLOWED_RETAKE_LIMITS: readonly number[] = [1, 2, 3];
export const MIN_RETENTION_HOURS = 1;
export const MAX_RETENTION_HOURS = 72;
export const MIN_WINDOW_WIDTH = 1280;
export const MIN_WINDOW_HEIGHT = 720;

export type OutputFormat = "Jpg" | "Png";
export type DisplayMode = "Fullscreen" | "Windowed";

export interface WindowBoundsValue {
  readonly Left: number | null;
  readonly Top: number | null;
  readonly Width: number;
  readonly Height: number;
}

export interface AppSettingsValues {
  /** 촬영 컷 수 의도. 6/8/10 또는 `0`(자동 — WD19). */
  readonly CutCount: number;
  readonly CountdownSec: number;
  readonly MirrorMode: boolean;
  readonly FlashMode: boolean;
  readonly ShutterSound: boolean;
  readonly RetakeEnabled: boolean;
  readonly RetakeLimit: number;
  readonly OutputFormat: OutputFormat;
  readonly RetentionHours: number;
  readonly EnableQrDelivery: boolean;
  readonly SendPhoto: boolean;
  readonly SendTimelapse: boolean;
  readonly FilterGrayscale: boolean;
  readonly FilterBrightness: boolean;
  readonly FilterBeauty: boolean;
  readonly SaveLocalCopy: boolean;
  readonly LocalSavePath: string;
  /** 미노출(WD7) — 값만 보존한다. */
  readonly DisplayMode: DisplayMode;
  /** 미노출(WD7) — 값만 보존한다. */
  readonly WindowBounds: WindowBoundsValue;
  /** 웹에서는 `deviceId` 문자열이다(analysis/41 §2.2가 허용). */
  readonly CameraDevice: string;
  /** 트레일링 슬래시 **없음**. */
  readonly HostingBaseUrl: string;
  readonly StorageBucket: string;
  /** 트레일링 슬래시 **있음**. */
  readonly BackendBaseUrl: string;
  readonly GoogleClientId: string;
  /** 미노출(WD8) — 값만 보존한다. */
  readonly ExternalCameraEnabled: boolean;
  /** 미노출(WD8) — 값만 보존한다. */
  readonly PhotoPrinterEnabled: boolean;
}

/** 웹 전용 보조값(`analysis/41` 계약 밖 — 별 객체에 담아 설정 호환성을 깨지 않는다). */
export interface WebExtras {
  /** `deviceId` 불안정 대비 폴백 매칭 정보(WC3). */
  readonly CameraDeviceLabel: string;
  readonly CameraDeviceGroupId: string;
  readonly CameraFacing: "user" | "environment";
}

export const DEFAULT_SETTINGS: AppSettingsValues = {
  CutCount: 6,
  CountdownSec: 6,
  MirrorMode: true,
  FlashMode: false,
  ShutterSound: false,
  RetakeEnabled: false,
  RetakeLimit: 1,
  OutputFormat: "Jpg",
  RetentionHours: 24,
  EnableQrDelivery: true,
  SendPhoto: true,
  SendTimelapse: true,
  FilterGrayscale: true,
  FilterBrightness: true,
  FilterBeauty: true,
  SaveLocalCopy: true,
  LocalSavePath: "",
  DisplayMode: "Windowed",
  WindowBounds: { Left: null, Top: null, Width: 1280, Height: 720 },
  CameraDevice: "",
  HostingBaseUrl: "https://mcphoto-955fb.web.app",
  StorageBucket: "mcphoto-955fb.firebasestorage.app",
  BackendBaseUrl: "https://asia-northeast3-mcphoto-955fb.cloudfunctions.net/api",
  GoogleClientId: "",
  ExternalCameraEnabled: false,
  PhotoPrinterEnabled: false,
};

export const DEFAULT_WEB_EXTRAS: WebExtras = {
  CameraDeviceLabel: "",
  CameraDeviceGroupId: "",
  CameraFacing: "user",
};

/**
 * 게스트가 편집할 수 없는 키(analysis/41 §2.3).
 * 화면은 비활성 표시만 하고 **저장 시 이 키들을 기록하지 않아 운영자 값이 보존**된다.
 * 이 게이트는 화면 로직에만 있다 — 모델은 전 필드를 항상 직렬화한다.
 */
export const GUEST_LOCKED_KEYS: readonly (keyof AppSettingsValues)[] = [
  "MirrorMode",
  "RetakeEnabled",
  "RetakeLimit",
  "FilterGrayscale",
  "FilterBrightness",
  "FilterBeauty",
  "EnableQrDelivery",
  "SendPhoto",
  "SendTimelapse",
  "HostingBaseUrl",
  "StorageBucket",
];

/**
 * C# `ClosestFrom` 대응 — 최근접 허용값. **거리 동률이면 배열의 앞선 값이 이긴다**
 * (7 → 6, 9 → 8). 부등호를 `<=`로 바꾸면 뒤쪽 값이 이겨 Windows와 어긋난다.
 */
export function closestFrom(value: number, allowed: readonly number[], fallback: number): number {
  if (allowed.length === 0) return fallback;
  let best = allowed[0]!;
  let bestDist = Math.abs(value - best);
  for (const candidate of allowed) {
    const dist = Math.abs(value - candidate);
    if (dist < bestDist) {
      best = candidate;
      bestDist = dist;
    }
  }
  return best;
}

/** `https://a/b` — 트레일링 슬래시를 **제거**한다(HostingBaseUrl 전용). */
function trimEndSlashes(url: string): string {
  return url.replace(/\/+$/, "");
}

/**
 * 값 범위·옵션 제약을 강제한다(로드·저장 시 호출). 잘못된 값은 가장 가까운 허용값으로 보정.
 *
 * ⚠️ **자동 sentinel(`CutCount=0`)은 보정 대상이 아니다** — 가드가 없으면 저장 왕복 1회에
 *    "자동"이 6으로 덮여 소멸한다(WD19). `-1` 등 다른 이탈값은 종전대로 6으로 보정된다.
 * ⚠️ 두 URL의 정규화 **방향이 반대다**(Hosting은 제거, Backend는 부여) — 같은 함수로 처리하지 말 것.
 */
export function clampSettings(values: AppSettingsValues): AppSettingsValues {
  const cutCount =
    isAutoCutCount(values.CutCount) || ALLOWED_CUT_COUNTS.includes(values.CutCount)
      ? values.CutCount
      : closestFrom(values.CutCount, ALLOWED_CUT_COUNTS, 6);

  const countdownSec = ALLOWED_COUNTDOWN_SECS.includes(values.CountdownSec)
    ? values.CountdownSec
    : closestFrom(values.CountdownSec, ALLOWED_COUNTDOWN_SECS, 6);

  const retakeLimit = ALLOWED_RETAKE_LIMITS.includes(values.RetakeLimit)
    ? values.RetakeLimit
    : closestFrom(values.RetakeLimit, ALLOWED_RETAKE_LIMITS, 1);

  const backendBaseUrl = values.BackendBaseUrl.trim();

  const normalized = normalizeQrToggles({
    enableQrDelivery: values.EnableQrDelivery,
    sendPhoto: values.SendPhoto,
    sendTimelapse: values.SendTimelapse,
  });

  return {
    ...values,
    CutCount: cutCount,
    CountdownSec: countdownSec,
    RetakeLimit: retakeLimit,
    RetentionHours: clamp(values.RetentionHours, MIN_RETENTION_HOURS, MAX_RETENTION_HOURS),
    WindowBounds: {
      ...values.WindowBounds,
      Width: Math.max(MIN_WINDOW_WIDTH, values.WindowBounds.Width),
      Height: Math.max(MIN_WINDOW_HEIGHT, values.WindowBounds.Height),
    },
    HostingBaseUrl: trimEndSlashes(values.HostingBaseUrl),
    // 빈 값이면 보정할 것이 없어 그대로 둔다(미구성 상태는 런타임 호출 실패로 드러난다).
    BackendBaseUrl:
      backendBaseUrl.length === 0 || backendBaseUrl.endsWith("/")
        ? backendBaseUrl
        : `${backendBaseUrl}/`,
    GoogleClientId: values.GoogleClientId.trim(),
    EnableQrDelivery: normalized.enableQrDelivery,
    SendPhoto: normalized.sendPhoto,
    SendTimelapse: normalized.sendTimelapse,
  };
}

/** 접속 구성 4키의 빌드 주입 폴백값. */
export interface ConnectionDefaults {
  readonly backendBaseUrl: string;
  readonly hostingBaseUrl: string;
  readonly storageBucket: string;
  readonly googleClientId: string;
}

/**
 * 접속 구성 4키가 **빈 문자열이면 빌드 주입값으로 대체**한다(05 §2.2).
 *
 * 빈 값이 영속되면 재배포로 값을 바꿀 수 없고, `GoogleClientId: ""` 저장 한 번에
 * 로그인 버튼이 영구히 사라진다. 저장값이 비어 있지 않으면 그 값이 우선한다.
 */
export function applyConnectionFallbacks(
  values: AppSettingsValues,
  defaults: ConnectionDefaults,
): AppSettingsValues {
  const pick = (stored: string, fallback: string): string =>
    stored.trim().length > 0 ? stored : fallback;

  return {
    ...values,
    BackendBaseUrl: pick(values.BackendBaseUrl, defaults.backendBaseUrl),
    HostingBaseUrl: pick(values.HostingBaseUrl, defaults.hostingBaseUrl),
    StorageBucket: pick(values.StorageBucket, defaults.storageBucket),
    GoogleClientId: pick(values.GoogleClientId, defaults.googleClientId),
  };
}

/** 자동 컷 수 의도를 만든다(설정 UI에서 "자동" 선택). */
export function autoCutCountValue(): number {
  return AUTO_CUT_COUNT;
}
