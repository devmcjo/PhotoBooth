import {
  applyConnectionFallbacks,
  clampSettings,
  DEFAULT_SETTINGS,
  DEFAULT_WEB_EXTRAS,
  type AppSettingsValues,
  type ConnectionDefaults,
  type WebExtras,
} from "@domain/settings/appSettings";

/**
 * 설정 영속(localStorage) — WD17 · 05 §2
 *
 * 규칙:
 * - **키 이름·기본값·범위는 `analysis/41 §2.1` 그대로.** camelCase로 바꾸지 않는다(내보내기 호환).
 * - **알 수 없는 키는 보존**한다(다른 클라이언트가 쓴 값을 지우지 않는다).
 * - 로드 실패·손상은 **기본값 + 경고**로 복구한다(크래시 금지).
 * - 저장은 **boolean을 반환**한다 — 실패를 조용히 넘기면 M4(성공 오인 금지) 위반이다.
 * - `BackendApiKey`는 **저장하지 않는다**(analysis/41 §2.5).
 */

export const SETTINGS_STORAGE_KEY = "mcphoto.settings.v1";
export const SETTINGS_SCHEMA_VERSION = 1;

/** localStorage 최소 표면. 테스트가 가짜 저장소를 주입한다. */
export interface StorageLike {
  getItem(key: string): string | null;
  setItem(key: string, value: string): void;
  removeItem(key: string): void;
}

export interface SettingsSnapshot {
  readonly values: AppSettingsValues;
  readonly webExtras: WebExtras;
}

export interface SettingsLoadResult extends SettingsSnapshot {
  /** 로드 중 발생한 경고(로그로 흘린다). 비어 있으면 정상 로드다. */
  readonly warnings: readonly string[];
  /** 저장된 값이 하나도 없었는가(최초 실행). */
  readonly firstRun: boolean;
}

export interface SettingsRepo {
  load(): SettingsLoadResult;
  /**
   * 저장. `omitKeys`에 든 키는 **기록하지 않아 기존 값이 보존**된다
   * (게스트 편집 제한 — analysis/41 §2.3. 화면이 `GUEST_LOCKED_KEYS`를 넘긴다).
   */
  save(snapshot: SettingsSnapshot, options?: { omitKeys?: readonly (keyof AppSettingsValues)[] }): boolean;
  /** 내보내기용 원문(BackendApiKey는 애초에 없다). */
  exportJson(): string;
}

interface StoredShape {
  schemaVersion?: unknown;
  values?: unknown;
  webExtras?: unknown;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

/**
 * 저장된 원시 객체를 기본값 위에 병합한다.
 * **타입이 맞는 키만** 채택하고(손상 값 무시), 기본값에 없는 키는 그대로 보존한다.
 */
function mergeValues(
  raw: Record<string, unknown>,
  warnings: string[],
): { values: AppSettingsValues; unknownKeys: Record<string, unknown> } {
  const merged: Record<string, unknown> = { ...DEFAULT_SETTINGS };
  const unknownKeys: Record<string, unknown> = {};

  for (const [key, value] of Object.entries(raw)) {
    if (!(key in DEFAULT_SETTINGS)) {
      unknownKeys[key] = value; // 다른 클라이언트·미래 버전의 키 — 보존
      continue;
    }
    const expected = typeof DEFAULT_SETTINGS[key as keyof AppSettingsValues];
    if (key === "WindowBounds") {
      if (isRecord(value)) merged[key] = { ...DEFAULT_SETTINGS.WindowBounds, ...value };
      else warnings.push(`설정 ${key}의 형식이 올바르지 않아 기본값을 씁니다.`);
      continue;
    }
    if (typeof value !== expected) {
      warnings.push(`설정 ${key}의 형식이 올바르지 않아 기본값을 씁니다.`);
      continue;
    }
    merged[key] = value;
  }

  return { values: merged as unknown as AppSettingsValues, unknownKeys };
}

function mergeWebExtras(raw: unknown): WebExtras {
  if (!isRecord(raw)) return DEFAULT_WEB_EXTRAS;
  const merged: Record<string, unknown> = { ...DEFAULT_WEB_EXTRAS };
  for (const [key, value] of Object.entries(raw)) {
    if (key in DEFAULT_WEB_EXTRAS && typeof value === "string") merged[key] = value;
  }
  return merged as unknown as WebExtras;
}

export function createSettingsRepo(
  storage: StorageLike,
  connectionDefaults: ConnectionDefaults,
): SettingsRepo {
  /** 저장된 원문(알 수 없는 키 보존용). */
  function readRaw(): { parsed: StoredShape | null; warning: string | null } {
    let text: string | null;
    try {
      text = storage.getItem(SETTINGS_STORAGE_KEY);
    } catch (err) {
      return { parsed: null, warning: `설정을 읽을 수 없습니다: ${describe(err)}` };
    }
    if (text === null) return { parsed: null, warning: null };
    try {
      const parsed: unknown = JSON.parse(text);
      if (!isRecord(parsed)) return { parsed: null, warning: "설정 형식이 올바르지 않아 기본값을 씁니다." };
      return { parsed: parsed as StoredShape, warning: null };
    } catch {
      return { parsed: null, warning: "설정 JSON이 손상되어 기본값을 씁니다." };
    }
  }

  function load(): SettingsLoadResult {
    const warnings: string[] = [];
    const { parsed, warning } = readRaw();
    if (warning !== null) warnings.push(warning);

    const firstRun = parsed === null && warning === null;

    if (parsed !== null && typeof parsed.schemaVersion === "number") {
      if (parsed.schemaVersion > SETTINGS_SCHEMA_VERSION) {
        warnings.push(
          `더 새 버전의 설정입니다(v${parsed.schemaVersion}) — 읽을 수 있는 값만 사용합니다.`,
        );
      }
    }

    const rawValues = isRecord(parsed?.values) ? (parsed!.values as Record<string, unknown>) : {};
    const { values } = mergeValues(rawValues, warnings);

    // 순서가 규격이다: 접속 구성 폴백 → clamp → QR 정규화(clamp 안에서).
    // 폴백을 clamp 뒤에 두면 빈 URL이 정규화를 거쳐 "/"가 되는 등 값이 오염된다.
    const withFallbacks = applyConnectionFallbacks(values, connectionDefaults);

    return {
      values: clampSettings(withFallbacks),
      webExtras: mergeWebExtras(parsed?.webExtras),
      warnings,
      firstRun,
    };
  }

  function save(
    snapshot: SettingsSnapshot,
    options?: { omitKeys?: readonly (keyof AppSettingsValues)[] },
  ): boolean {
    const omit = new Set<string>(options?.omitKeys ?? []);

    // 기존 저장값을 기준으로 삼아야 ① 알 수 없는 키가 보존되고
    // ② omitKeys(게스트 제한)가 **기존 값을 그대로 남긴다**.
    const { parsed } = readRaw();
    const existing = isRecord(parsed?.values) ? { ...(parsed!.values as Record<string, unknown>) } : {};

    const clamped = clampSettings(snapshot.values);
    for (const [key, value] of Object.entries(clamped)) {
      if (omit.has(key)) continue;
      existing[key] = value;
    }
    // 게이트 키는 어떤 경로로도 저장하지 않는다(analysis/41 §2.5).
    delete existing.BackendApiKey;

    const payload = JSON.stringify({
      schemaVersion: SETTINGS_SCHEMA_VERSION,
      values: existing,
      webExtras: snapshot.webExtras,
    });

    try {
      storage.setItem(SETTINGS_STORAGE_KEY, payload);
      return true;
    } catch {
      // QuotaExceededError·프라이빗 모드 등. 상위가 "저장 위치에 쓸 수 없습니다."를 표시한다.
      return false;
    }
  }

  function exportJson(): string {
    const { parsed } = readRaw();
    const loaded = load();
    const values: Record<string, unknown> = isRecord(parsed?.values)
      ? { ...(parsed!.values as Record<string, unknown>) }
      : { ...loaded.values };
    // 저장된 원문에 남아 있더라도 내보내기에는 절대 싣지 않는다(analysis/41 §2.5).
    delete values.BackendApiKey;
    return JSON.stringify(
      { schemaVersion: SETTINGS_SCHEMA_VERSION, values, webExtras: loaded.webExtras },
      null,
      2,
    );
  }

  return { load, save, exportJson };
}

function describe(err: unknown): string {
  return err instanceof Error ? err.message : String(err);
}
