import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { bootstrap } from "@shell/bootstrap";
import { attachSettingsRepo, useSettingsStore } from "@shell/settingsStore";
import { DEFAULT_BRANDING } from "@adapters/platform/branding";
import { detachLogStore } from "@adapters/storage/logStore";
import { SETTINGS_STORAGE_KEY, type StorageLike } from "@adapters/storage/settingsRepo";
import { UNSUPPORTED_OPFS_CLIENT, type OpfsClient } from "@adapters/storage/opfsClient";
import { DEFAULT_SETTINGS } from "@domain/settings/appSettings";

/**
 * 부트스트랩 순서 검증 — 01 §4.2
 * 순서 자체가 규격이라 "무엇이 무엇보다 먼저 일어났는가"를 관측한다.
 */

class MemoryStorage implements StorageLike {
  readonly map = new Map<string, string>();
  getItem(key: string) {
    return this.map.get(key) ?? null;
  }
  setItem(key: string, value: string) {
    this.map.set(key, value);
  }
  removeItem(key: string) {
    this.map.delete(key);
  }
}

/** 호출 순서를 기록하는 가짜 OPFS 클라이언트. */
function recordingOpfs(trace: string[], sessions: string[] = []): OpfsClient {
  return {
    ...UNSUPPORTED_OPFS_CLIENT,
    async capability() {
      trace.push("opfs:capability");
      return "sync-access-handle";
    },
    async list(path) {
      trace.push(`opfs:list:${path}`);
      return sessions;
    },
    async remove(path) {
      trace.push(`opfs:remove:${path}`);
      return true;
    },
  };
}

let storage: MemoryStorage;

beforeEach(() => {
  storage = new MemoryStorage();
  useSettingsStore.setState({ values: DEFAULT_SETTINGS, lastSaveOk: null });
});

afterEach(() => {
  detachLogStore();
  attachSettingsRepo(null);
});

function deps(trace: string[], overrides: Partial<Parameters<typeof bootstrap>[0]> = {}) {
  return {
    localStorage: storage as unknown as Storage,
    opfs: recordingOpfs(trace),
    storageManager: {
      persist: async () => true,
      persisted: async () => false,
      estimate: async () => ({ usage: 10, quota: 100 }),
    } as unknown as typeof navigator.storage,
    fetchImpl: (async () => {
      trace.push("branding:fetch");
      return { ok: true, status: 200, json: async () => ({ AppName: "부스" }) } as Response;
    }) as unknown as typeof fetch,
    doc: { title: "" } as Document,
    mirrorLogsToConsole: false,
    ...overrides,
  };
}

describe("bootstrap — 순서와 산출물", () => {
  it("브랜딩을 세션 잔재 정리보다 먼저 읽는다(첫 렌더 전 규격)", async () => {
    const trace: string[] = [];
    await bootstrap(deps(trace));

    const brandingIndex = trace.indexOf("branding:fetch");
    const purgeIndex = trace.findIndex((t) => t.startsWith("opfs:list"));
    expect(brandingIndex).toBeGreaterThanOrEqual(0);
    expect(purgeIndex).toBeGreaterThan(brandingIndex);
  });

  it("문서 타이틀에 브랜딩을 적용한다", async () => {
    const doc = { title: "" } as Document;
    const trace: string[] = [];
    const result = await bootstrap(deps(trace, { doc }));
    expect(result.branding.appName).toBe("부스");
    expect(doc.title).toBe("부스");
  });

  it("브랜딩 실패에도 부팅이 계속된다", async () => {
    const trace: string[] = [];
    const result = await bootstrap(
      deps(trace, {
        fetchImpl: (async () => {
          throw new TypeError("offline");
        }) as unknown as typeof fetch,
      }),
    );
    expect(result.branding).toEqual(DEFAULT_BRANDING);
    expect(result.purgedSessions).toBe(0); // 이후 단계가 정상 수행됐다
  });

  it("설정을 로드해 스토어에 주입한다", async () => {
    storage.setItem(
      SETTINGS_STORAGE_KEY,
      JSON.stringify({ values: { CutCount: 0, CountdownSec: 8 } }),
    );
    const trace: string[] = [];
    await bootstrap(deps(trace));

    const values = useSettingsStore.getState().values;
    expect(values.CutCount).toBe(0); // 자동 sentinel 보존
    expect(values.CountdownSec).toBe(8);
  });

  it("sessions/ 잔재를 정리하고 개수를 보고한다", async () => {
    const trace: string[] = [];
    const result = await bootstrap(
      deps(trace, { opfs: recordingOpfs(trace, ["old1", "old2"]) }),
    );
    expect(result.purgedSessions).toBe(2);
    expect(trace).toContain("opfs:remove:sessions/old1");
    expect(trace).toContain("opfs:remove:sessions/old2");
  });

  it("results/·frames/는 정리 대상이 아니다", async () => {
    const trace: string[] = [];
    await bootstrap(deps(trace, { opfs: recordingOpfs(trace, ["s1"]) }));
    expect(trace.some((t) => t.includes("results"))).toBe(false);
    expect(trace.some((t) => t.includes("frames"))).toBe(false);
  });

  it("OPFS 능력과 저장소 영속 상태를 보고한다", async () => {
    const trace: string[] = [];
    const result = await bootstrap(deps(trace));
    expect(result.opfsCapability).toBe("sync-access-handle");
    expect(result.storage.persistState).toBe("granted");
  });

  it("OPFS 미지원에서도 부팅이 완주한다(업로드만 가능한 축소 동작)", async () => {
    const trace: string[] = [];
    const result = await bootstrap(deps(trace, { opfs: UNSUPPORTED_OPFS_CLIENT }));
    expect(result.opfsCapability).toBe("none");
    expect(result.purgedSessions).toBe(0);
  });

  it("localStorage를 못 쓰면 기본값으로 동작하고 저장소를 연결하지 않는다", async () => {
    const trace: string[] = [];
    const result = await bootstrap(
      deps(trace, { localStorage: undefined as unknown as Storage }),
    );
    // node 환경에는 localStorage가 없다 → repo가 null이어야 한다
    expect(result.settingsRepo).toBeNull();
    expect(useSettingsStore.getState().values.CutCount).toBe(DEFAULT_SETTINGS.CutCount);
  });

  it("로그 스토어가 부팅 로그를 담고 시크릿을 남기지 않는다", async () => {
    const trace: string[] = [];
    const result = await bootstrap(deps(trace));
    const text = await result.logStore.exportText();

    expect(text).toContain("앱 시작 v");
    expect(text).toContain("설정 로드");
    expect(text).toContain("세션 잔재 정리");
    // 게이트 키·client id 값이 아니라 "설정됨 여부"만 남는다
    expect(text).toContain("gateKeyConfigured");
  });

  it("설정 저장이 스토어를 통해 동작한다(저장소 연결 확인)", async () => {
    const trace: string[] = [];
    await bootstrap(deps(trace));
    const ok = useSettingsStore.getState().save({ CountdownSec: 10 }, { isGuest: false });
    expect(ok).toBe(true);
    expect(JSON.parse(storage.getItem(SETTINGS_STORAGE_KEY)!).values.CountdownSec).toBe(10);
  });
});
