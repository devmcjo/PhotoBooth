import { afterEach, beforeEach, describe, expect, it } from "vitest";
import {
  DEFAULT_SETTINGS,
  DEFAULT_WEB_EXTRAS,
  GUEST_LOCKED_KEYS,
  type AppSettingsValues,
} from "@domain/settings/appSettings";
import {
  SETTINGS_HIDDEN_KEYS,
  type SettingsEditContext,
} from "@domain/settings/settingsEditPolicy";
import {
  createSettingsRepo,
  SETTINGS_STORAGE_KEY,
  type StorageLike,
} from "@adapters/storage/settingsRepo";
import { attachSettingsRepo, useSettingsStore } from "@shell/settingsStore";
import type { ToastKind } from "@shell/shellStore";
import {
  applyQrToggle,
  buildSavePatch,
  changeSetting,
  createDraft,
  saveSettings,
  type SettingsDraft,
} from "@screens/settings/settingsForm";
import { STRINGS } from "@ui/strings";

/**
 * 설정 draft·패치·저장 절차 — 03 §12.4·§12.5 · analysis/41 §2.3
 *
 * 게스트 제한의 **본체는 패치에서 키를 빼는 것**이다. 그래서 "draft가 오염돼도 패치가 깨끗한가"를
 * 직접 검사한다 — 렌더 가드는 우회될 수 있지만 이 경계는 우회할 수 없어야 한다.
 */

const OPERATOR: SettingsEditContext = { isGuest: false, qrBlocked: false };
const GUEST: SettingsEditContext = { isGuest: true, qrBlocked: false };

const CONNECTION_DEFAULTS = {
  backendBaseUrl: "https://api.example.com/",
  hostingBaseUrl: "https://web.example.com",
  storageBucket: "bucket.example.app",
  googleClientId: "client-id",
};

function memoryStorage(): StorageLike & { readonly data: Record<string, string> } {
  const data: Record<string, string> = {};
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

interface Harness {
  readonly draft: SettingsDraft;
  readonly toasts: { kind: ToastKind; message: string }[];
  readonly resets: SettingsDraft[];
  readonly storage: ReturnType<typeof memoryStorage>;
}

function harness(initial?: Partial<AppSettingsValues>): Harness {
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

function saveWith(h: Harness, draft: SettingsDraft, ctx: SettingsEditContext) {
  return saveSettings({
    draft,
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

beforeEach(() => {
  useSettingsStore.getState().hydrate(DEFAULT_SETTINGS, DEFAULT_WEB_EXTRAS);
});

afterEach(() => {
  attachSettingsRepo(null);
});

describe("changeSetting — 액션 가드(M10 ②)", () => {
  it("로그인 사용자는 값을 바꿀 수 있다", () => {
    const h = harness();
    const next = changeSetting(h.draft, "CountdownSec", 10, OPERATOR);
    expect(next.values.CountdownSec).toBe(10);
  });

  it("게스트가 제한 키를 바꾸려 하면 draft가 **그대로**다", () => {
    const h = harness();
    for (const key of GUEST_LOCKED_KEYS) {
      const next = changeSetting(h.draft, key, false as never, GUEST);
      expect(next, key).toBe(h.draft);
    }
  });

  it("게스트도 제한 밖 키는 바꿀 수 있다", () => {
    const h = harness();
    expect(changeSetting(h.draft, "FlashMode", true, GUEST).values.FlashMode).toBe(true);
  });

  it("미노출 키는 어떤 컨텍스트에서도 바뀌지 않는다", () => {
    const h = harness();
    const next = changeSetting(h.draft, "DisplayMode", "Fullscreen", OPERATOR);
    expect(next).toBe(h.draft);
  });
});

describe("buildSavePatch — 패치 가드(M10 ③)", () => {
  it("게스트 draft를 강제로 변조해도 제한 11키가 패치에 없다", () => {
    const h = harness();
    // 렌더·액션 가드를 모두 우회해 draft를 직접 오염시킨다(개발자 도구 시나리오).
    const tampered: SettingsDraft = {
      ...h.draft,
      values: {
        ...h.draft.values,
        MirrorMode: false,
        RetakeEnabled: true,
        RetakeLimit: 3,
        FilterGrayscale: false,
        FilterBrightness: false,
        FilterBeauty: false,
        EnableQrDelivery: false,
        SendPhoto: false,
        SendTimelapse: false,
        HostingBaseUrl: "https://evil.example",
        StorageBucket: "evil-bucket",
      },
    };

    const patch = buildSavePatch(tampered, GUEST);
    for (const key of GUEST_LOCKED_KEYS) {
      expect(Object.hasOwn(patch, key), `${key}가 패치에 남았다`).toBe(false);
    }
  });

  it("미노출 4키는 어떤 컨텍스트에서도 패치에 없다", () => {
    const h = harness();
    for (const ctx of [OPERATOR, GUEST]) {
      const patch = buildSavePatch(h.draft, ctx);
      for (const key of SETTINGS_HIDDEN_KEYS) {
        expect(Object.hasOwn(patch, key), key).toBe(false);
      }
    }
  });

  it("TempUser 한도 초과에서는 QR 4키만 빠지고 나머지는 남는다", () => {
    const h = harness();
    const patch = buildSavePatch(h.draft, { isGuest: false, qrBlocked: true });
    for (const key of ["EnableQrDelivery", "SendPhoto", "SendTimelapse", "RetentionHours"]) {
      expect(Object.hasOwn(patch, key), key).toBe(false);
    }
    expect(patch.MirrorMode).toBe(DEFAULT_SETTINGS.MirrorMode);
    expect(patch.CutCount).toBe(DEFAULT_SETTINGS.CutCount);
  });
});

describe("applyQrToggle — 재활성 규칙(analysis/41 §2.4)", () => {
  it("QR off → on이면 하위 둘이 함께 켜진다", () => {
    const h = harness({ EnableQrDelivery: false, SendPhoto: false, SendTimelapse: false });
    const next = applyQrToggle(h.draft, "EnableQrDelivery", true, OPERATOR);
    expect(next.values.EnableQrDelivery).toBe(true);
    expect(next.values.SendPhoto).toBe(true);
    expect(next.values.SendTimelapse).toBe(true);
  });

  it("이미 on인 상태의 재설정은 하위 토글을 건드리지 않는다", () => {
    const h = harness({ EnableQrDelivery: true, SendPhoto: true, SendTimelapse: false });
    const next = applyQrToggle(h.draft, "EnableQrDelivery", true, OPERATOR);
    expect(next.values.SendTimelapse).toBe(false);
  });

  it("QR을 끄면 하위 값은 보존된다(재활성 때 복원하기 위함)", () => {
    const h = harness({ EnableQrDelivery: true, SendPhoto: true, SendTimelapse: true });
    const next = applyQrToggle(h.draft, "EnableQrDelivery", false, OPERATOR);
    expect(next.values.SendPhoto).toBe(true);
    expect(next.values.SendTimelapse).toBe(true);
  });

  it("하위 토글 변경은 그 키만 바꾼다", () => {
    const h = harness();
    const next = applyQrToggle(h.draft, "SendPhoto", false, OPERATOR);
    expect(next.values.SendPhoto).toBe(false);
    expect(next.values.EnableQrDelivery).toBe(DEFAULT_SETTINGS.EnableQrDelivery);
  });

  it("게스트는 QR 토글을 바꿀 수 없다(재활성도 일어나지 않는다)", () => {
    const h = harness({ EnableQrDelivery: false, SendPhoto: false, SendTimelapse: false });
    expect(applyQrToggle(h.draft, "EnableQrDelivery", true, GUEST)).toBe(h.draft);
  });
});

describe("saveSettings — 저장 절차(03 §12.4)", () => {
  it("성공하면 성공 토스트 + 보정된 값 재반영", () => {
    const h = harness();
    // 컷 수 7은 허용값이 아니다 → 저장 시 6으로 보정된다.
    const draft = { ...h.draft, values: { ...h.draft.values, CutCount: 7 } };

    const result = saveWith(h, draft, OPERATOR);

    expect(result.ok).toBe(true);
    expect(h.toasts).toEqual([{ kind: "success", message: STRINGS.save.succeeded }]);
    // ★ 재반영이 없으면 7이 6으로 바뀐 사실이 화면에 보이지 않는다.
    expect(h.resets).toHaveLength(1);
    expect(h.resets[0]!.values.CutCount).toBe(6);
  });

  it("자동 컷 수(sentinel 0)는 저장 왕복에서 소멸하지 않는다(WD19)", () => {
    const h = harness();
    const draft = { ...h.draft, values: { ...h.draft.values, CutCount: 0 } };
    saveWith(h, draft, OPERATOR);
    expect(h.resets[0]!.values.CutCount).toBe(0);
  });

  it("QR 정규화(하위 둘 다 off → QR off)가 재반영으로 화면에 보인다", () => {
    const h = harness();
    const draft = {
      ...h.draft,
      values: {
        ...h.draft.values,
        EnableQrDelivery: true,
        SendPhoto: false,
        SendTimelapse: false,
      },
    };

    saveWith(h, draft, OPERATOR);
    expect(h.resets[0]!.values.EnableQrDelivery).toBe(false);
    // 하위 값은 보존된다.
    expect(h.resets[0]!.values.SendPhoto).toBe(false);
  });

  it("저장 실패는 실패 토스트다 — 성공으로 위장하지 않는다(M4)", () => {
    const h = harness();
    // 저장소 미연결 = 쓰기 실패 경로.
    attachSettingsRepo(null);

    const result = saveWith(h, h.draft, OPERATOR);

    expect(result.ok).toBe(false);
    expect(h.toasts).toEqual([{ kind: "error", message: STRINGS.save.failed }]);
  });

  it("저장 실패해도 사용자가 입력한 값은 화면에 남는다(다시 시도할 수 있게)", () => {
    const h = harness();
    attachSettingsRepo(null);
    const draft = { ...h.draft, values: { ...h.draft.values, CountdownSec: 10 } };

    saveWith(h, draft, OPERATOR);

    expect(h.resets[0]!.values.CountdownSec).toBe(10);
  });

  it("게스트 저장 후에도 운영자 값이 보존된다(E23)", () => {
    // 운영자가 거울모드 off · QR off로 설정해 둔 상태.
    const h = harness({ MirrorMode: false, EnableQrDelivery: false });
    saveWith(h, h.draft, OPERATOR);
    h.toasts.length = 0;
    h.resets.length = 0;

    // 게스트가 draft를 오염시킨 채 저장한다.
    const tampered: SettingsDraft = {
      ...h.draft,
      values: { ...h.draft.values, MirrorMode: true, EnableQrDelivery: true, FlashMode: true },
    };
    const result = saveWith(h, tampered, GUEST);

    expect(result.ok).toBe(true);
    const stored = JSON.parse(h.storage.data[SETTINGS_STORAGE_KEY]!) as {
      values: Record<string, unknown>;
    };
    // 제한 키는 운영자 값 그대로.
    expect(stored.values.MirrorMode).toBe(false);
    expect(stored.values.EnableQrDelivery).toBe(false);
    // 제한 밖 키는 게스트도 바꿀 수 있다.
    expect(stored.values.FlashMode).toBe(true);
    // 메모리·화면도 운영자 값으로 되돌아온다.
    expect(h.resets[0]!.values.MirrorMode).toBe(false);
  });

  it("미노출 키는 저장 왕복 후에도 보존된다(WD7·WD8)", () => {
    const h = harness({
      DisplayMode: "Fullscreen",
      ExternalCameraEnabled: true,
      PhotoPrinterEnabled: true,
      WindowBounds: { Left: 10, Top: 20, Width: 1600, Height: 900 },
    });
    saveWith(h, h.draft, OPERATOR);

    const stored = JSON.parse(h.storage.data[SETTINGS_STORAGE_KEY]!) as {
      values: Record<string, unknown>;
    };
    expect(stored.values.DisplayMode).toBe("Fullscreen");
    expect(stored.values.ExternalCameraEnabled).toBe(true);
    expect(stored.values.PhotoPrinterEnabled).toBe(true);
    expect(stored.values.WindowBounds).toEqual({
      Left: 10,
      Top: 20,
      Width: 1600,
      Height: 900,
    });
  });

  it("webExtras가 같은 저장 왕복에 실린다(저장 1회 · 결과 boolean 1개)", () => {
    const h = harness();
    const draft: SettingsDraft = {
      ...h.draft,
      webExtras: { ...h.draft.webExtras, CameraDeviceLabel: "Logi C920", CameraFacing: "environment" },
    };

    const result = saveWith(h, draft, OPERATOR);

    expect(result.ok).toBe(true);
    const stored = JSON.parse(h.storage.data[SETTINGS_STORAGE_KEY]!) as {
      webExtras: Record<string, unknown>;
    };
    expect(stored.webExtras.CameraDeviceLabel).toBe("Logi C920");
    expect(stored.webExtras.CameraFacing).toBe("environment");
    expect(useSettingsStore.getState().webExtras.CameraDeviceLabel).toBe("Logi C920");
  });
});
