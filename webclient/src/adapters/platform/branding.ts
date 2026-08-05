/**
 * 브랜딩 로드 — WD13 · 05 §8.1
 *
 * `/branding.json`을 **첫 화면 렌더 전에** 800ms 타임아웃으로 읽는다.
 * 운영자가 파일만 교체하면 재빌드 없이 이름이 바뀐다.
 * **두 값은 독립적으로 폴백**한다(AppName만 있고 Subtitle이 비어도 각자 기본값을 쓴다).
 * 어떤 실패에도 크래시하지 않는다.
 */

export interface Branding {
  readonly appName: string;
  readonly subtitle: string;
}

export const DEFAULT_BRANDING: Branding = {
  appName: "MCPhoto",
  subtitle: "self custom photobooth",
};

export const BRANDING_TIMEOUT_MS = 800;
export const BRANDING_URL = "/branding.json";

/** 문자열이고 트림 후 비어 있지 않을 때만 채택한다. */
function pick(value: unknown, fallback: string): string {
  if (typeof value !== "string") return fallback;
  const trimmed = value.trim();
  return trimmed.length > 0 ? trimmed : fallback;
}

export function parseBranding(raw: unknown): Branding {
  if (typeof raw !== "object" || raw === null) return DEFAULT_BRANDING;
  const record = raw as Record<string, unknown>;
  return {
    appName: pick(record.AppName, DEFAULT_BRANDING.appName),
    subtitle: pick(record.Subtitle, DEFAULT_BRANDING.subtitle),
  };
}

export interface LoadBrandingResult {
  readonly branding: Branding;
  /** 기본값으로 폴백했는가(로그·진단 표시용). */
  readonly usedFallback: boolean;
  readonly reason?: string;
}

/**
 * 브랜딩을 읽는다. `fetchImpl`을 주입할 수 있다(테스트).
 * 타임아웃·네트워크 오류·JSON 손상 모두 기본값 폴백이다.
 */
export async function loadBranding(
  fetchImpl: typeof fetch = fetch,
  timeoutMs: number = BRANDING_TIMEOUT_MS,
): Promise<LoadBrandingResult> {
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), timeoutMs);
  try {
    const response = await fetchImpl(BRANDING_URL, {
      signal: controller.signal,
      cache: "no-store",
    });
    if (!response.ok) {
      return { branding: DEFAULT_BRANDING, usedFallback: true, reason: `HTTP ${response.status}` };
    }
    const json: unknown = await response.json();
    const branding = parseBranding(json);
    const usedFallback =
      branding.appName === DEFAULT_BRANDING.appName &&
      branding.subtitle === DEFAULT_BRANDING.subtitle;
    return { branding, usedFallback };
  } catch (err) {
    const reason =
      err instanceof DOMException && err.name === "AbortError"
        ? `타임아웃(${timeoutMs}ms)`
        : err instanceof Error
          ? err.message
          : String(err);
    return { branding: DEFAULT_BRANDING, usedFallback: true, reason };
  } finally {
    clearTimeout(timer);
  }
}

/** 문서 타이틀에 적용한다(05 §8.1 적용 지점). */
export function applyBrandingToDocument(branding: Branding, doc: Document): void {
  doc.title = branding.appName;
}
