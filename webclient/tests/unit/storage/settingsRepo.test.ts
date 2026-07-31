import { beforeEach, describe, expect, it } from "vitest";
import {
  createSettingsRepo,
  SETTINGS_SCHEMA_VERSION,
  SETTINGS_STORAGE_KEY,
  type StorageLike,
} from "@adapters/storage/settingsRepo";
import { DEFAULT_SETTINGS, GUEST_LOCKED_KEYS } from "@domain/settings/appSettings";

/** 실패를 주입할 수 있는 가짜 localStorage. */
class FakeStorage implements StorageLike {
  readonly map = new Map<string, string>();
  failOnSet = false;
  failOnGet = false;

  getItem(key: string): string | null {
    if (this.failOnGet) throw new DOMException("blocked", "SecurityError");
    return this.map.get(key) ?? null;
  }
  setItem(key: string, value: string): void {
    if (this.failOnSet) throw new DOMException("quota", "QuotaExceededError");
    this.map.set(key, value);
  }
  removeItem(key: string): void {
    this.map.delete(key);
  }
}

const CONNECTION_DEFAULTS = {
  backendBaseUrl: "https://build/api/",
  hostingBaseUrl: "https://build.web.app",
  storageBucket: "build.bucket",
  googleClientId: "build-client-id",
};

let storage: FakeStorage;
function repo() {
  return createSettingsRepo(storage, CONNECTION_DEFAULTS);
}
function stored(): Record<string, unknown> {
  return JSON.parse(storage.map.get(SETTINGS_STORAGE_KEY)!) as Record<string, unknown>;
}
function storedValues(): Record<string, unknown> {
  return stored().values as Record<string, unknown>;
}

beforeEach(() => {
  storage = new FakeStorage();
});

describe("settingsRepo — 로드", () => {
  it("최초 실행은 기본값 + firstRun", () => {
    const result = repo().load();
    expect(result.firstRun).toBe(true);
    expect(result.warnings).toEqual([]);
    expect(result.values.CutCount).toBe(6);
    expect(result.values.MirrorMode).toBe(true);
  });

  it("손상된 JSON에서도 크래시 없이 기본값 + 경고로 뜬다", () => {
    storage.map.set(SETTINGS_STORAGE_KEY, "{not json");
    const result = repo().load();
    expect(result.values.CutCount).toBe(6);
    expect(result.warnings.length).toBe(1);
    expect(result.warnings[0]).toContain("손상");
    expect(result.firstRun).toBe(false);
  });

  it("배열·문자열 등 잘못된 최상위 형식도 기본값으로 복구한다", () => {
    storage.map.set(SETTINGS_STORAGE_KEY, "[1,2,3]");
    expect(repo().load().values.CutCount).toBe(6);
    storage.map.set(SETTINGS_STORAGE_KEY, '"nope"');
    expect(repo().load().warnings.length).toBe(1);
  });

  it("읽기 자체가 예외를 던져도(프라이빗 모드) 기본값으로 동작한다", () => {
    storage.failOnGet = true;
    const result = repo().load();
    expect(result.values.CutCount).toBe(6);
    expect(result.warnings[0]).toContain("읽을 수 없습니다");
  });

  it("타입이 틀린 키만 기본값으로 되돌리고 경고한다", () => {
    storage.map.set(
      SETTINGS_STORAGE_KEY,
      JSON.stringify({
        schemaVersion: 1,
        values: { CutCount: "여덟", CountdownSec: 8, MirrorMode: "yes" },
      }),
    );
    const result = repo().load();
    expect(result.values.CutCount).toBe(6); // 문자열 → 기본값
    expect(result.values.CountdownSec).toBe(8); // 정상 값은 유지
    expect(result.values.MirrorMode).toBe(true);
    expect(result.warnings.length).toBe(2);
  });

  it("로드 시 clamp가 적용된다", () => {
    storage.map.set(
      SETTINGS_STORAGE_KEY,
      JSON.stringify({ values: { CutCount: 7, RetentionHours: 999, CountdownSec: 5 } }),
    );
    const result = repo().load();
    expect(result.values.CutCount).toBe(6);
    expect(result.values.RetentionHours).toBe(72);
    expect(result.values.CountdownSec).toBe(6);
  });

  it("자동 컷 수 sentinel(0)은 로드에서도 보존된다 — WD19", () => {
    storage.map.set(SETTINGS_STORAGE_KEY, JSON.stringify({ values: { CutCount: 0 } }));
    expect(repo().load().values.CutCount).toBe(0);
  });

  it("접속 구성이 빈 문자열이면 빌드 주입값으로 대체된다", () => {
    storage.map.set(
      SETTINGS_STORAGE_KEY,
      JSON.stringify({ values: { GoogleClientId: "", HostingBaseUrl: "" } }),
    );
    const result = repo().load();
    expect(result.values.GoogleClientId).toBe(CONNECTION_DEFAULTS.googleClientId);
    expect(result.values.HostingBaseUrl).toBe(CONNECTION_DEFAULTS.hostingBaseUrl);
  });

  it("더 새 스키마 버전은 경고하되 읽을 수 있는 값은 쓴다", () => {
    storage.map.set(
      SETTINGS_STORAGE_KEY,
      JSON.stringify({ schemaVersion: 99, values: { CountdownSec: 8 } }),
    );
    const result = repo().load();
    expect(result.values.CountdownSec).toBe(8);
    expect(result.warnings.some((w) => w.includes("더 새 버전"))).toBe(true);
  });

  it("webExtras를 병합하고 알 수 없는 값은 무시한다", () => {
    storage.map.set(
      SETTINGS_STORAGE_KEY,
      JSON.stringify({
        values: {},
        webExtras: { CameraDeviceLabel: "FaceTime HD", Bogus: 1, CameraFacing: "environment" },
      }),
    );
    const result = repo().load();
    expect(result.webExtras.CameraDeviceLabel).toBe("FaceTime HD");
    expect(result.webExtras.CameraFacing).toBe("environment");
    expect("Bogus" in result.webExtras).toBe(false);
  });
});

describe("settingsRepo — 저장", () => {
  it("성공 시 true, 실패 시 false를 돌려준다(성공 오인 금지 — M4)", () => {
    const r = repo();
    const snapshot = r.load();
    expect(r.save(snapshot)).toBe(true);

    storage.failOnSet = true;
    expect(r.save(snapshot)).toBe(false);
  });

  it("clamp된 값이 저장되고 왕복해도 유지된다", () => {
    const r = repo();
    const snapshot = r.load();
    r.save({ ...snapshot, values: { ...snapshot.values, CutCount: 9, RetentionHours: 0 } });

    expect(storedValues().CutCount).toBe(8);
    expect(storedValues().RetentionHours).toBe(1);

    const reloaded = r.load();
    expect(reloaded.values.CutCount).toBe(8);
    expect(reloaded.values.RetentionHours).toBe(1);
  });

  it("알 수 없는 키를 보존한다(다른 클라이언트가 쓴 값을 지우지 않는다)", () => {
    storage.map.set(
      SETTINGS_STORAGE_KEY,
      JSON.stringify({ values: { CutCount: 8, FutureKey: "keep-me" } }),
    );
    const r = repo();
    const snapshot = r.load();
    r.save(snapshot);
    expect(storedValues().FutureKey).toBe("keep-me");
  });

  it("BackendApiKey는 어떤 경로로도 저장되지 않는다(analysis/41 §2.5)", () => {
    storage.map.set(
      SETTINGS_STORAGE_KEY,
      JSON.stringify({ values: { BackendApiKey: "leaked-key" } }),
    );
    const r = repo();
    r.save(r.load());
    expect("BackendApiKey" in storedValues()).toBe(false);
    expect(storage.map.get(SETTINGS_STORAGE_KEY)).not.toContain("leaked-key");
  });

  it("schemaVersion을 기록한다", () => {
    const r = repo();
    r.save(r.load());
    expect(stored().schemaVersion).toBe(SETTINGS_SCHEMA_VERSION);
  });
});

describe("settingsRepo — 게스트 편집 제한(analysis/41 §2.3)", () => {
  it("omitKeys에 든 키는 기록되지 않아 관리자 값이 보존된다", () => {
    // 관리자가 거울모드 off·QR off로 저장해 둔 상태
    const r = repo();
    const admin = r.load();
    r.save({
      ...admin,
      values: { ...admin.values, MirrorMode: false, EnableQrDelivery: false, CountdownSec: 8 },
    });
    const before = { ...storedValues() };

    // 게스트가 제한 키를 바꿔 저장 시도
    const guest = r.load();
    const ok = r.save(
      {
        ...guest,
        values: { ...guest.values, MirrorMode: true, EnableQrDelivery: true, CountdownSec: 3 },
      },
      { omitKeys: GUEST_LOCKED_KEYS },
    );

    expect(ok).toBe(true);
    // 제한 키는 그대로
    expect(storedValues().MirrorMode).toBe(before.MirrorMode);
    expect(storedValues().EnableQrDelivery).toBe(before.EnableQrDelivery);
    // 제한 대상이 아닌 키는 반영된다
    expect(storedValues().CountdownSec).toBe(3);
  });

  it("게스트 제한 키 11개가 전부 보존된다", () => {
    const r = repo();
    const initial = r.load();
    r.save(initial);
    const before = { ...storedValues() };

    // 모든 제한 키를 반대값·다른값으로 바꿔 저장
    const flipped = { ...initial.values };
    for (const key of GUEST_LOCKED_KEYS) {
      const current = flipped[key];
      (flipped as Record<string, unknown>)[key] =
        typeof current === "boolean" ? !current : typeof current === "number" ? current + 1 : "changed";
    }
    r.save({ ...initial, values: flipped }, { omitKeys: GUEST_LOCKED_KEYS });

    for (const key of GUEST_LOCKED_KEYS) {
      expect(storedValues()[key], key).toEqual(before[key]);
    }
  });
});

describe("settingsRepo — 내보내기", () => {
  it("게이트 키를 포함하지 않는다", () => {
    storage.map.set(
      SETTINGS_STORAGE_KEY,
      JSON.stringify({ values: { BackendApiKey: "secret-key", CutCount: 8 } }),
    );
    const json = repo().exportJson();
    expect(json).not.toContain("secret-key");
    expect(json).not.toContain("BackendApiKey");
    expect(JSON.parse(json).values.CutCount).toBe(8);
  });

  it("저장값이 없어도 기본값 스냅샷을 낸다", () => {
    const parsed = JSON.parse(repo().exportJson());
    expect(parsed.values.CutCount).toBe(DEFAULT_SETTINGS.CutCount);
    expect(parsed.schemaVersion).toBe(SETTINGS_SCHEMA_VERSION);
  });
});
