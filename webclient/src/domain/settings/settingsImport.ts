import {
  DEFAULT_SETTINGS,
  DEFAULT_WEB_EXTRAS,
  type AppSettingsValues,
  type WebExtras,
} from "./appSettings";

/**
 * 설정 가져오기 파일 파싱(순수) — WD17 · 05 §2.5
 *
 * ⚠️ **즉시 덮어쓰지 않는다.** 이 함수는 "적용 후보"만 만들고, 화면이 미리보기를 보여준 뒤
 *    [적용]에서 비로소 `settingsStore.save`를 부른다.
 * ⚠️ **내구성**: 손상 값·알 수 없는 키는 경고로 남기고 계속한다(예외 금지).
 * ⚠️ `BackendApiKey`는 모델에 없는 키이므로 자동으로 "알 수 없는 키"가 되어 적용되지 않는다
 *    (analysis/41 §2.5 — 이 파일에 특례를 만들지 않는 것이 규격이다).
 */

export type SettingsImportRejection = "tooNew" | "malformed";

export type SettingsImportResult =
  | {
      readonly ok: true;
      readonly values: Partial<AppSettingsValues>;
      readonly webExtras: Partial<WebExtras>;
      readonly warnings: readonly string[];
    }
  | { readonly ok: false; readonly reason: SettingsImportRejection };

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

/**
 * 내보내기 파일(§2.1 구조)을 해석한다.
 *
 * - `schemaVersion`이 현재보다 **높으면 거부**한다(`tooNew`) — 모르는 구조를 반쯤 적용하면
 *   어떤 키가 반영됐는지 아무도 모른다.
 * - 최상위가 객체가 아니거나 `values`가 객체가 아니면 `malformed`.
 */
export function parseSettingsFile(
  raw: unknown,
  currentSchemaVersion: number,
): SettingsImportResult {
  if (!isRecord(raw)) return { ok: false, reason: "malformed" };

  const schemaVersion = raw.schemaVersion;
  if (typeof schemaVersion === "number" && schemaVersion > currentSchemaVersion) {
    return { ok: false, reason: "tooNew" };
  }

  if (!isRecord(raw.values)) return { ok: false, reason: "malformed" };

  const warnings: string[] = [];
  const values: Record<string, unknown> = {};

  for (const [key, value] of Object.entries(raw.values)) {
    if (!(key in DEFAULT_SETTINGS)) {
      // 다른 클라이언트·미래 버전의 키. 적용하지 않지만 저장소가 기존 값을 보존한다.
      warnings.push(`알 수 없는 설정 ${key}는 적용하지 않습니다.`);
      continue;
    }
    if (key === "WindowBounds") {
      if (isRecord(value)) values[key] = { ...DEFAULT_SETTINGS.WindowBounds, ...value };
      else warnings.push(`설정 ${key}의 형식이 올바르지 않아 건너뜁니다.`);
      continue;
    }
    if (typeof value !== typeof DEFAULT_SETTINGS[key as keyof AppSettingsValues]) {
      warnings.push(`설정 ${key}의 형식이 올바르지 않아 건너뜁니다.`);
      continue;
    }
    values[key] = value;
  }

  const webExtras: Record<string, unknown> = {};
  if (isRecord(raw.webExtras)) {
    for (const [key, value] of Object.entries(raw.webExtras)) {
      if (key in DEFAULT_WEB_EXTRAS && typeof value === "string") webExtras[key] = value;
    }
  }

  return {
    ok: true,
    values: values as Partial<AppSettingsValues>,
    webExtras: webExtras as Partial<WebExtras>,
    warnings,
  };
}

/** 미리보기 1행. 화면이 "무엇이 바뀌는지"를 보여줄 때 쓴다. */
export interface SettingsImportChange {
  readonly key: keyof AppSettingsValues;
  readonly from: unknown;
  readonly to: unknown;
}

/** 현재 값과 다른 항목만 추린다(같은 값을 "변경 예정"으로 보여주면 신뢰를 잃는다). */
export function diffImportedSettings(
  current: AppSettingsValues,
  incoming: Partial<AppSettingsValues>,
): readonly SettingsImportChange[] {
  const changes: SettingsImportChange[] = [];
  for (const [key, value] of Object.entries(incoming)) {
    const typedKey = key as keyof AppSettingsValues;
    const before = current[typedKey];
    // 객체(WindowBounds)는 JSON 비교로 충분하다 — 값 타입이 단순하다.
    const same =
      typeof before === "object" || typeof value === "object"
        ? JSON.stringify(before) === JSON.stringify(value)
        : before === value;
    if (!same) changes.push({ key: typedKey, from: before, to: value });
  }
  return changes;
}
