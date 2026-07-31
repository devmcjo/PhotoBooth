import type { AppSettingsValues, WebExtras } from "@domain/settings/appSettings";
import {
  diffImportedSettings,
  parseSettingsFile,
  type SettingsImportChange,
  type SettingsImportRejection,
} from "@domain/settings/settingsImport";
import { exportBlob } from "@adapters/platform/fileExport";
import { SETTINGS_SCHEMA_VERSION } from "@adapters/storage/settingsRepo";
import { logger } from "@adapters/storage/logStore";
import { currentSettingsRepo } from "@shell/settingsStore";
import { saveSettings, type SaveSettingsDeps, type SettingsDraft } from "./settingsForm";

/**
 * 설정 내보내기 / 가져오기 — WD17 · 05 §2.5 (React 무관)
 *
 * ⚠️ **즉시 덮어쓰지 않는다**: 파싱 → 변경 예정 항목 미리보기 → [적용]. 설정 파일 한 장이
 *    운영자의 전 설정을 조용히 바꾸면 되돌릴 방법이 없다.
 * ⚠️ [적용]도 `buildSavePatch` → `save(..., {isGuest})`를 지나므로 **게스트 제한 키는 자동 제외**된다.
 * ⚠️ `BackendApiKey`는 모델에 없어 내보내기·가져오기 어느 쪽에도 실리지 않는다(analysis/41 §2.5).
 *
 * 이 모듈은 **잘라내기 쉽게** 독립돼 있다(설계 D2 — 리뷰가 이월을 택하면 이 파일과 섹션 6의
 * 두 버튼만 지우면 된다).
 */

function pad2(value: number): string {
  return String(value).padStart(2, "0");
}

/** `mcphoto-settings-{YYMMDD_HHMM}.json` — 로컬 시각 성분(운영자가 시각으로 찾는다). */
export function settingsExportFileName(localTime: Date): string {
  return (
    "mcphoto-settings-" +
    pad2(localTime.getFullYear() % 100) +
    pad2(localTime.getMonth() + 1) +
    pad2(localTime.getDate()) +
    "_" +
    pad2(localTime.getHours()) +
    pad2(localTime.getMinutes()) +
    ".json"
  );
}

export interface SettingsExportDeps {
  readonly exportJson: () => string;
  readonly write: (blob: Blob, fileName: string) => boolean;
  readonly now: () => Date;
}

/** 내보내기. 실패는 `false`(화면이 실패 토스트를 낸다 — M4). */
export function buildExport(deps: SettingsExportDeps): boolean {
  const json = deps.exportJson();
  const blob = new Blob([json], { type: "application/json" });
  const ok = deps.write(blob, settingsExportFileName(deps.now()));
  if (!ok) logger.warn("설정 내보내기 실패");
  return ok;
}

export function defaultSettingsExportDeps(
  overrides: Partial<SettingsExportDeps> = {},
): SettingsExportDeps {
  return {
    exportJson: () => currentSettingsRepo()?.exportJson() ?? "{}",
    write: (blob, fileName) => exportBlob(blob, fileName),
    now: () => new Date(),
    ...overrides,
  };
}

export interface ImportPreview {
  readonly values: Partial<AppSettingsValues>;
  readonly webExtras: Partial<WebExtras>;
  /** 실제로 달라지는 항목만. 같은 값을 "변경 예정"으로 보이면 신뢰를 잃는다. */
  readonly changes: readonly SettingsImportChange[];
  readonly warnings: readonly string[];
}

export type ImportPreviewResult =
  | { readonly ok: true; readonly preview: ImportPreview }
  | { readonly ok: false; readonly reason: SettingsImportRejection };

/** 파일 텍스트 → 미리보기. **파싱 실패로 예외를 던지지 않는다.** */
export function previewImport(text: string, current: AppSettingsValues): ImportPreviewResult {
  let raw: unknown;
  try {
    raw = JSON.parse(text);
  } catch {
    return { ok: false, reason: "malformed" };
  }

  const parsed = parseSettingsFile(raw, SETTINGS_SCHEMA_VERSION);
  if (!parsed.ok) return { ok: false, reason: parsed.reason };

  return {
    ok: true,
    preview: {
      values: parsed.values,
      webExtras: parsed.webExtras,
      changes: diffImportedSettings(current, parsed.values),
      warnings: parsed.warnings,
    },
  };
}

export interface ApplyImportDeps extends Omit<SaveSettingsDeps, "draft"> {
  readonly preview: ImportPreview;
  /** 현재 draft. 가져온 값을 이 위에 병합한다(파일에 없는 키는 그대로 둔다). */
  readonly draft: SettingsDraft;
}

/** [적용] — 저장 절차(03 §12.4)를 그대로 탄다. 제한 키 제외·재반영·정직한 토스트가 전부 성립한다. */
export function applyImport(deps: ApplyImportDeps): { readonly ok: boolean } {
  const merged: SettingsDraft = {
    values: { ...deps.draft.values, ...deps.preview.values },
    webExtras: { ...deps.draft.webExtras, ...deps.preview.webExtras },
  };
  logger.info("설정 가져오기 적용", { changeCount: deps.preview.changes.length });
  return saveSettings({ ...deps, draft: merged });
}
