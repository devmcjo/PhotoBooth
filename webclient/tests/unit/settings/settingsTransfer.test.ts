import { afterEach, beforeEach, describe, expect, it } from "vitest";
import {
  DEFAULT_SETTINGS,
  DEFAULT_WEB_EXTRAS,
  type AppSettingsValues,
} from "@domain/settings/appSettings";
import type { SettingsEditContext } from "@domain/settings/settingsEditPolicy";
import {
  createSettingsRepo,
  SETTINGS_STORAGE_KEY,
  type StorageLike,
} from "@adapters/storage/settingsRepo";
import { attachSettingsRepo, useSettingsStore } from "@shell/settingsStore";
import type { ToastKind } from "@shell/shellStore";
import { createDraft, type SettingsDraft } from "@screens/settings/settingsForm";
import {
  applyImport,
  buildExport,
  previewImport,
  settingsExportFileName,
} from "@screens/settings/settingsTransfer";

/**
 * 설정 내보내기 / 가져오기 — WD17 · 05 §2.5
 *
 * ⚠️ 가장 중요한 두 가지: **`BackendApiKey`가 파일에 없다** · **게스트 [적용]이 제한 키를 쓰지 않는다**.
 */

const OPERATOR: SettingsEditContext = { isGuest: false, qrBlocked: false };
const GUEST: SettingsEditContext = { isGuest: true, qrBlocked: false };

const CONNECTION_DEFAULTS = {
  backendBaseUrl: "https://api.example.com/",
  hostingBaseUrl: "https://web.example.com",
  storageBucket: "bucket.example.app",
  googleClientId: "client-id",
};

function memoryStorage(initial: Record<string, string> = {}): StorageLike & {
  readonly data: Record<string, string>;
} {
  const data: Record<string, string> = { ...initial };
  return {
    data,
    getItem: (key) => data[key] ?? null,
    setItem: (key, value) => {
      data[key] = value;
    },
    removeItem: (key) => {
      delete data[key];
    },
  };
}

beforeEach(() => {
  useSettingsStore.getState().hydrate(DEFAULT_SETTINGS, DEFAULT_WEB_EXTRAS);
});

afterEach(() => {
  attachSettingsRepo(null);
});

describe("내보내기", () => {
  it("파일명이 `mcphoto-settings-{YYMMDD_HHMM}.json`이다", () => {
    // 로컬 시각 성분으로 조립한다(운영자가 시각으로 찾는다).
    expect(settingsExportFileName(new Date(2026, 6, 20, 14, 45))).toBe(
      "mcphoto-settings-260720_1445.json",
    );
    expect(settingsExportFileName(new Date(2026, 0, 2, 3, 4))).toBe(
      "mcphoto-settings-260102_0304.json",
    );
  });

  it("내보낸 JSON에 `BackendApiKey`가 없다(analysis/41 §2.5)", async () => {
    // 저장소에 남아 있더라도 내보내기에는 실리지 않아야 한다.
    const storage = memoryStorage({
      [SETTINGS_STORAGE_KEY]: JSON.stringify({
        schemaVersion: 1,
        values: { ...DEFAULT_SETTINGS, BackendApiKey: "super-secret" },
        webExtras: DEFAULT_WEB_EXTRAS,
      }),
    });
    const repo = createSettingsRepo(storage, CONNECTION_DEFAULTS);

    const written: { blob: Blob; fileName: string }[] = [];
    const ok = buildExport({
      exportJson: () => repo.exportJson(),
      write: (blob, fileName) => {
        written.push({ blob, fileName });
        return true;
      },
      now: () => new Date(2026, 6, 20, 14, 45),
    });

    expect(ok).toBe(true);
    expect(written).toHaveLength(1);
    const text = await written[0]!.blob.text();
    expect(text).not.toContain("BackendApiKey");
    expect(text).not.toContain("super-secret");
    expect(written[0]!.fileName).toBe("mcphoto-settings-260720_1445.json");
  });

  it("쓰기 실패는 false다(성공 오인 금지 — M4)", () => {
    const ok = buildExport({
      exportJson: () => "{}",
      write: () => false,
      now: () => new Date(),
    });
    expect(ok).toBe(false);
  });
});

describe("가져오기 미리보기", () => {
  it("변경될 항목만 뽑는다", () => {
    const text = JSON.stringify({
      schemaVersion: 1,
      values: { CutCount: DEFAULT_SETTINGS.CutCount, CountdownSec: 10 },
    });
    const result = previewImport(text, DEFAULT_SETTINGS);

    expect(result.ok).toBe(true);
    if (!result.ok) return;
    expect(result.preview.changes).toEqual([
      { key: "CountdownSec", from: DEFAULT_SETTINGS.CountdownSec, to: 10 },
    ]);
  });

  it("상위 schemaVersion은 거부한다", () => {
    const text = JSON.stringify({ schemaVersion: 99, values: { CutCount: 10 } });
    expect(previewImport(text, DEFAULT_SETTINGS)).toEqual({ ok: false, reason: "tooNew" });
  });

  it("JSON이 아니면 malformed다(예외를 던지지 않는다)", () => {
    expect(() => previewImport("{ nope", DEFAULT_SETTINGS)).not.toThrow();
    expect(previewImport("{ nope", DEFAULT_SETTINGS)).toEqual({
      ok: false,
      reason: "malformed",
    });
  });

  it("알 수 없는 키는 경고로 남고 적용 후보에 없다", () => {
    const text = JSON.stringify({
      schemaVersion: 1,
      values: { BackendApiKey: "leak", CountdownSec: 10 },
    });
    const result = previewImport(text, DEFAULT_SETTINGS);

    expect(result.ok).toBe(true);
    if (!result.ok) return;
    expect(Object.hasOwn(result.preview.values, "BackendApiKey")).toBe(false);
    expect(result.preview.warnings.join()).toContain("BackendApiKey");
  });
});

describe("가져오기 [적용] — 저장 절차를 그대로 탄다", () => {
  interface ApplyHarness {
    readonly draft: SettingsDraft;
    readonly toasts: { kind: ToastKind; message: string }[];
    readonly resets: SettingsDraft[];
    readonly storage: ReturnType<typeof memoryStorage>;
  }

  function harness(initial?: Partial<AppSettingsValues>): ApplyHarness {
    const storage = memoryStorage();
    attachSettingsRepo(createSettingsRepo(storage, CONNECTION_DEFAULTS));
    const values: AppSettingsValues = { ...DEFAULT_SETTINGS, ...initial };
    useSettingsStore.getState().hydrate(values, DEFAULT_WEB_EXTRAS);
    return {
      draft: createDraft(values, DEFAULT_WEB_EXTRAS),
      toasts: [],
      resets: [],
      storage,
    };
  }

  function apply(h: ApplyHarness, text: string, ctx: SettingsEditContext) {
    const preview = previewImport(text, h.draft.values);
    expect(preview.ok).toBe(true);
    if (!preview.ok) throw new Error("미리보기 실패");

    return applyImport({
      preview: preview.preview,
      draft: h.draft,
      ctx,
      save: (patch, options) => useSettingsStore.getState().save(patch, options),
      readBack: () => {
        const state = useSettingsStore.getState();
        return createDraft(state.values, state.webExtras);
      },
      resetDraft: (next) => h.resets.push(next),
      toast: (kind, message) => h.toasts.push({ kind, message }),
    });
  }

  it("파일에 있는 키만 바꾸고 나머지는 그대로 둔다", () => {
    const h = harness({ CountdownSec: 3 });
    const result = apply(
      h,
      JSON.stringify({ schemaVersion: 1, values: { CountdownSec: 10 } }),
      OPERATOR,
    );

    expect(result.ok).toBe(true);
    expect(h.resets[0]!.values.CountdownSec).toBe(10);
    expect(h.resets[0]!.values.CutCount).toBe(DEFAULT_SETTINGS.CutCount);
  });

  it("게스트 [적용]은 제한 키를 쓰지 않는다(운영자 값 보존)", () => {
    const h = harness({ MirrorMode: false, EnableQrDelivery: false });
    // 운영자 값을 먼저 저장해 둔다.
    apply(h, JSON.stringify({ schemaVersion: 1, values: {} }), OPERATOR);
    h.resets.length = 0;

    apply(
      h,
      JSON.stringify({
        schemaVersion: 1,
        values: { MirrorMode: true, EnableQrDelivery: true, FlashMode: true },
      }),
      GUEST,
    );

    const stored = JSON.parse(h.storage.data[SETTINGS_STORAGE_KEY]!) as {
      values: Record<string, unknown>;
    };
    expect(stored.values.MirrorMode).toBe(false);
    expect(stored.values.EnableQrDelivery).toBe(false);
    expect(stored.values.FlashMode).toBe(true);
  });

  it("적용 후 보정된 값이 재반영된다(컷 수 7 → 6)", () => {
    const h = harness();
    apply(h, JSON.stringify({ schemaVersion: 1, values: { CutCount: 7 } }), OPERATOR);
    expect(h.resets[0]!.values.CutCount).toBe(6);
  });

  it("저장 실패는 실패 토스트다", () => {
    const h = harness();
    attachSettingsRepo(null);
    const result = apply(
      h,
      JSON.stringify({ schemaVersion: 1, values: { CountdownSec: 10 } }),
      OPERATOR,
    );
    expect(result.ok).toBe(false);
    expect(h.toasts.at(-1)?.kind).toBe("error");
  });
});
