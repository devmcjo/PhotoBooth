import { describe, expect, it } from "vitest";
import {
  DEFAULT_SETTINGS,
  GUEST_LOCKED_KEYS,
  type AppSettingsValues,
} from "@domain/settings/appSettings";
import {
  displaySettingValue,
  isSettingEditable,
  omittedSaveKeys,
  QR_RELATED_KEYS,
  settingLockReason,
  SETTINGS_HIDDEN_KEYS,
  type SettingsEditContext,
} from "@domain/settings/settingsEditPolicy";
import { diffImportedSettings, parseSettingsFile } from "@domain/settings/settingsImport";
import { formatBytes } from "@domain/results/byteFormat";

/**
 * 설정 편집 정책·가져오기·용량 표기(순수) — analysis/41 §2.3 · 05 §2.5
 */

const OPERATOR: SettingsEditContext = { isGuest: false, qrBlocked: false };
const GUEST: SettingsEditContext = { isGuest: true, qrBlocked: false };
const TEMP_BLOCKED: SettingsEditContext = { isGuest: false, qrBlocked: true };

const ALL_KEYS = Object.keys(DEFAULT_SETTINGS) as (keyof AppSettingsValues)[];

describe("게스트 편집 제한 — GUEST_LOCKED_KEYS 11개 전수", () => {
  it("제한 키 목록이 규격(analysis/41 §2.3)과 같다", () => {
    expect([...GUEST_LOCKED_KEYS].sort()).toEqual(
      [
        "EnableQrDelivery",
        "FilterBeauty",
        "FilterBrightness",
        "FilterGrayscale",
        "HostingBaseUrl",
        "MirrorMode",
        "RetakeEnabled",
        "RetakeLimit",
        "SendPhoto",
        "SendTimelapse",
        "StorageBucket",
      ].sort(),
    );
    expect(GUEST_LOCKED_KEYS).toHaveLength(11);
  });

  it.each(GUEST_LOCKED_KEYS)("게스트는 %s를 편집할 수 없다", (key) => {
    expect(isSettingEditable(key, GUEST)).toBe(false);
    expect(settingLockReason(key, GUEST)).toBe("guest");
  });

  it("로그인 사용자는 미노출 키를 제외한 전 키를 편집할 수 있다", () => {
    for (const key of ALL_KEYS) {
      const hidden = SETTINGS_HIDDEN_KEYS.includes(key);
      expect(isSettingEditable(key, OPERATOR), key).toBe(!hidden);
    }
  });

  it("미노출 4키는 어떤 컨텍스트에서도 편집 대상이 아니다(값은 보존 — WD7·WD8)", () => {
    expect([...SETTINGS_HIDDEN_KEYS]).toEqual([
      "DisplayMode",
      "WindowBounds",
      "ExternalCameraEnabled",
      "PhotoPrinterEnabled",
    ]);
    for (const key of SETTINGS_HIDDEN_KEYS) {
      expect(isSettingEditable(key, OPERATOR)).toBe(false);
      expect(isSettingEditable(key, GUEST)).toBe(false);
      // 잠금 배지를 붙일 사유는 없다(애초에 렌더하지 않는다).
      expect(settingLockReason(key, OPERATOR)).toBeNull();
    }
  });
});

describe("TempUser 한도 초과 — QR 4키만 추가 차단", () => {
  it.each(QR_RELATED_KEYS)("%s가 잠긴다", (key) => {
    expect(isSettingEditable(key, TEMP_BLOCKED)).toBe(false);
    expect(settingLockReason(key, TEMP_BLOCKED)).toBe("qrLimit");
  });

  it("QR 밖의 키는 그대로 편집 가능하다", () => {
    for (const key of ALL_KEYS) {
      if (QR_RELATED_KEYS.includes(key) || SETTINGS_HIDDEN_KEYS.includes(key)) continue;
      expect(isSettingEditable(key, TEMP_BLOCKED), key).toBe(true);
    }
  });

  it("게스트 사유가 QR 사유보다 먼저다(안내 문구가 '로그인 필요'여야 한다)", () => {
    expect(settingLockReason("EnableQrDelivery", { isGuest: true, qrBlocked: true })).toBe("guest");
  });
});

describe("displaySettingValue — 게스트 표시값(03 §12.3)", () => {
  it("게스트의 제한 boolean 키만 OFF로 접는다", () => {
    expect(displaySettingValue("MirrorMode", true, GUEST)).toBe(false);
    expect(displaySettingValue("EnableQrDelivery", true, GUEST)).toBe(false);
    expect(displaySettingValue("FilterBeauty", true, GUEST)).toBe(false);
  });

  it("제한 키라도 boolean이 아니면 값을 유지한다", () => {
    expect(displaySettingValue("RetakeLimit", 3, GUEST)).toBe(3);
    expect(displaySettingValue("HostingBaseUrl", "https://a", GUEST)).toBe("https://a");
  });

  it("제한 밖 키는 게스트에게도 그대로 보인다", () => {
    expect(displaySettingValue("FlashMode", true, GUEST)).toBe(true);
    expect(displaySettingValue("CountdownSec", 8, GUEST)).toBe(8);
  });

  it("TempUser 한도 초과는 값을 가리지 않는다(운영자 값 그대로)", () => {
    expect(displaySettingValue("EnableQrDelivery", true, TEMP_BLOCKED)).toBe(true);
  });

  it("로그인 사용자는 어떤 값도 접지 않는다", () => {
    for (const key of GUEST_LOCKED_KEYS) {
      const stored = DEFAULT_SETTINGS[key];
      expect(displaySettingValue(key, stored, OPERATOR), key).toEqual(stored);
    }
  });
});

describe("omittedSaveKeys — 합집합", () => {
  it("운영자는 미노출 4키만 뺀다", () => {
    expect([...omittedSaveKeys(OPERATOR)].sort()).toEqual([...SETTINGS_HIDDEN_KEYS].sort());
  });

  it("게스트는 미노출 4 + 제한 11 = 15키를 뺀다(중복 없음)", () => {
    const keys = omittedSaveKeys(GUEST);
    expect(new Set(keys).size).toBe(keys.length);
    expect(keys).toHaveLength(15);
    for (const key of GUEST_LOCKED_KEYS) expect(keys).toContain(key);
    for (const key of SETTINGS_HIDDEN_KEYS) expect(keys).toContain(key);
  });

  it("TempUser 한도 초과는 QR 4키를 더한다", () => {
    const keys = omittedSaveKeys(TEMP_BLOCKED);
    for (const key of QR_RELATED_KEYS) expect(keys).toContain(key);
    expect(keys).toHaveLength(SETTINGS_HIDDEN_KEYS.length + QR_RELATED_KEYS.length);
  });

  it("게스트 + 한도 초과에서도 중복이 생기지 않는다", () => {
    const keys = omittedSaveKeys({ isGuest: true, qrBlocked: true });
    expect(new Set(keys).size).toBe(keys.length);
    // 게스트 11 + 미노출 4 + QR 전용 추가(RetentionHours) 1 = 16
    expect(keys).toHaveLength(16);
  });
});

describe("parseSettingsFile — 가져오기 파싱(05 §2.5)", () => {
  it("정상 파일을 값·webExtras로 나눈다", () => {
    const result = parseSettingsFile(
      {
        schemaVersion: 1,
        values: { CutCount: 8, MirrorMode: false },
        webExtras: { CameraFacing: "environment" },
      },
      1,
    );

    expect(result.ok).toBe(true);
    if (!result.ok) return;
    expect(result.values).toEqual({ CutCount: 8, MirrorMode: false });
    expect(result.webExtras).toEqual({ CameraFacing: "environment" });
    expect(result.warnings).toHaveLength(0);
  });

  it("schemaVersion이 더 높으면 거부한다", () => {
    expect(parseSettingsFile({ schemaVersion: 2, values: {} }, 1)).toEqual({
      ok: false,
      reason: "tooNew",
    });
  });

  it("같거나 낮은 schemaVersion·누락은 허용한다", () => {
    expect(parseSettingsFile({ schemaVersion: 1, values: {} }, 1).ok).toBe(true);
    expect(parseSettingsFile({ values: {} }, 1).ok).toBe(true);
  });

  it.each([
    ["null", null],
    ["배열", []],
    ["문자열", "{}"],
    ["values 없음", { schemaVersion: 1 }],
    ["values가 배열", { schemaVersion: 1, values: [] }],
  ])("손상 입력(%s)은 malformed다", (_label, raw) => {
    expect(parseSettingsFile(raw, 1)).toEqual({ ok: false, reason: "malformed" });
  });

  it("형식이 틀린 값은 경고로 남기고 건너뛴다(예외 금지)", () => {
    const result = parseSettingsFile(
      { schemaVersion: 1, values: { CutCount: "여덟", MirrorMode: true } },
      1,
    );
    expect(result.ok).toBe(true);
    if (!result.ok) return;
    expect(result.values).toEqual({ MirrorMode: true });
    expect(result.warnings.join()).toContain("CutCount");
  });

  it("알 수 없는 키는 적용하지 않는다 — `BackendApiKey`도 여기에 걸린다", () => {
    const result = parseSettingsFile(
      { schemaVersion: 1, values: { BackendApiKey: "secret", FutureFlag: 1, FlashMode: true } },
      1,
    );
    expect(result.ok).toBe(true);
    if (!result.ok) return;
    expect(result.values).toEqual({ FlashMode: true });
    expect(JSON.stringify(result)).not.toContain("secret");
    expect(result.warnings).toHaveLength(2);
  });

  it("WindowBounds는 기본값 위에 병합한다(미노출이지만 값은 보존)", () => {
    const result = parseSettingsFile(
      { schemaVersion: 1, values: { WindowBounds: { Width: 1920 } } },
      1,
    );
    expect(result.ok).toBe(true);
    if (!result.ok) return;
    expect(result.values.WindowBounds).toEqual({
      ...DEFAULT_SETTINGS.WindowBounds,
      Width: 1920,
    });
  });

  it("webExtras의 문자열이 아닌 값·모르는 키는 버린다", () => {
    const result = parseSettingsFile(
      { schemaVersion: 1, values: {}, webExtras: { CameraDeviceLabel: 7, Nope: "x" } },
      1,
    );
    expect(result.ok).toBe(true);
    if (!result.ok) return;
    expect(result.webExtras).toEqual({});
  });
});

describe("diffImportedSettings — 미리보기", () => {
  it("현재와 다른 항목만 추린다", () => {
    const changes = diffImportedSettings(DEFAULT_SETTINGS, {
      CutCount: DEFAULT_SETTINGS.CutCount,
      CountdownSec: 10,
    });
    expect(changes).toEqual([
      { key: "CountdownSec", from: DEFAULT_SETTINGS.CountdownSec, to: 10 },
    ]);
  });

  it("객체 값은 내용 비교다(같으면 변경으로 보지 않는다)", () => {
    const same = diffImportedSettings(DEFAULT_SETTINGS, {
      WindowBounds: { ...DEFAULT_SETTINGS.WindowBounds },
    });
    expect(same).toHaveLength(0);

    const changed = diffImportedSettings(DEFAULT_SETTINGS, {
      WindowBounds: { ...DEFAULT_SETTINGS.WindowBounds, Width: 1920 },
    });
    expect(changed).toHaveLength(1);
  });
});

describe("formatBytes", () => {
  it.each([
    [0, "0 B"],
    [-1, "0 B"],
    [Number.NaN, "0 B"],
    [1, "1 B"],
    [1023, "1023 B"],
    [1024, "1 KB"],
    [12 * 1024, "12 KB"],
    [1536, "1.5 KB"],
    [340 * 1024 * 1024, "340 MB"],
    [1024 * 1024 * 1024, "1 GB"],
    [Math.round(1.2 * 1024 * 1024 * 1024), "1.2 GB"],
    [2 * 1024 * 1024 * 1024 * 1024, "2 TB"],
  ])("%s → %s", (bytes, expected) => {
    expect(formatBytes(bytes)).toBe(expected);
  });
});
