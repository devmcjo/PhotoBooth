import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { FrameCatalogProgress } from "@domain/frames/frameCatalogProgress";
import type { FrameTemplate } from "@domain/frames/types";
import {
  createFrameCatalog,
  FrameLoadCancelledError,
  type FrameCatalog,
  type FrameCatalogDeps,
} from "@adapters/frames/frameCatalog";
import type { FrameStore, SaveFrameInput } from "@adapters/storage/frameStore";
import {
  attachLogStore,
  createLogStore,
  createMemoryLogSink,
  detachLogStore,
} from "@adapters/storage/logStore";

/**
 * 카탈로그 로더 — 설계 §4 C1~C15 (it20의 심장)
 *
 * 고정하는 성질: **줄 서지 않는다**(단일 비행 + replay) · **취소는 호출자별**(공유 작업은 캐시를
 * 완성한다) · **서버 실패는 삼킨다**(오프라인이 `Ready`인 이유 — E20) · **개인 프레임에 서버를
 * 부르지 않는다**(401 → 세션 해제 방지).
 */

function frame(overrides: Partial<FrameTemplate> = {}): FrameTemplate {
  const name = overrides.name ?? "A";
  return {
    id: "srv-1",
    userId: null,
    isDefault: true,
    imageUrl: `https://cdn.example.com/${name}.png`,
    imageSize: { width: 1200, height: 1600 },
    slots: [{ index: 0, x: 0, y: 0, width: 10, height: 10 }],
    createdAt: "2026-07-01T00:00:00.000Z",
    ...overrides,
    name,
  };
}

/** `let x: (() => void) | null`은 콜백 대입을 TS가 `never`로 좁혀버린다 — 명시 deferred를 쓴다. */
function deferred(): { promise: Promise<void>; resolve: () => void } {
  let resolve: () => void = () => undefined;
  const promise = new Promise<void>((r) => {
    resolve = r;
  });
  return { promise, resolve };
}

interface Harness {
  readonly catalog: FrameCatalog;
  readonly calls: {
    getDefaultFrames: number;
    download: string[];
    cache: string[];
    listPublic: number;
    listPersonal: string[];
    getUserFrames: number;
  };
  /** OPFS 캐시를 흉내낸 공용 목록(캐시 성공 시 여기에 쌓인다). */
  readonly cached: FrameTemplate[];
}

function harness(
  overrides: {
    server?: () => Promise<FrameTemplate[]>;
    download?: (url: string) => Promise<Blob | null>;
    bundle?: () => Promise<FrameTemplate[]>;
    fallback?: () => Promise<FrameTemplate>;
    initialCache?: FrameTemplate[];
    personal?: FrameTemplate[];
    cacheFails?: boolean;
    listPublicDelayMs?: number;
  } = {},
): Harness {
  const cached: FrameTemplate[] = [...(overrides.initialCache ?? [])];
  const calls: Harness["calls"] = {
    getDefaultFrames: 0,
    download: [],
    cache: [],
    listPublic: 0,
    listPersonal: [],
    getUserFrames: 0,
  };

  const store: FrameStore = {
    async listPublic() {
      calls.listPublic++;
      if (overrides.listPublicDelayMs !== undefined) {
        await new Promise((r) => setTimeout(r, overrides.listPublicDelayMs));
      }
      return [...cached];
    },
    async listPersonal(userId) {
      calls.listPersonal.push(userId);
      return [...(overrides.personal ?? [])];
    },
    async scopeFrameNames() {
      // 카탈로그는 이름 열거를 쓰지 않는다(저장 경로 전용 — 설계 §10.1).
      return [];
    },
    async cacheServerFrame(f) {
      calls.cache.push(f.name);
      if (overrides.cacheFails === true) return null;
      const stored = { ...f, imageUrl: `blob:frames/${f.name}.png` };
      cached.push(stored);
      return stored;
    },
    async saveLocal(_input: SaveFrameInput) {
      return null;
    },
    async deleteLocal() {
      return true;
    },
    async readImageBytes() {
      // 카탈로그는 원본 바이트를 읽지 않는다(내보내기 전용 — Step 16).
      return null;
    },
    async countPersonal() {
      return 0;
    },
    async usageBytes() {
      return 0;
    },
  };

  const deps: FrameCatalogDeps = {
    store,
    repository: {
      async getDefaultFrames() {
        calls.getDefaultFrames++;
        // ⚠️ 이 가짜 저장소에는 `getUserFrames`가 **없다**. 카탈로그가 그것을 부르면 타입·런타임
        //    양쪽에서 즉시 드러난다(C14).
        return overrides.server === undefined ? [] : await overrides.server();
      },
    },
    download:
      overrides.download ??
      (async (url) => {
        calls.download.push(url);
        return new Blob(["png"]);
      }),
    bundle: overrides.bundle ?? (async () => []),
    fallback:
      overrides.fallback ??
      (async () => frame({ id: "fallback", name: "기본 프레임", imageUrl: "blob:fallback" })),
  };

  // download 래핑(주입형에도 호출 기록이 남게).
  const originalDownload = deps.download;
  const wrapped: FrameCatalogDeps = {
    ...deps,
    download: async (url) => {
      if (overrides.download !== undefined) calls.download.push(url);
      return originalDownload(url);
    },
  };

  return { catalog: createFrameCatalog(wrapped), calls, cached };
}

beforeEach(() => {
  attachLogStore(createLogStore({ sink: createMemoryLogSink(), now: () => 0 }));
});

afterEach(() => {
  detachLogStore();
});

describe("C1·C5: 단일 비행", () => {
  it("C1: 동시 2회 호출에서 서버 조회·다운로드가 각각 1회다", async () => {
    const h = harness({
      server: async () => [frame({ id: "s1", name: "A" }), frame({ id: "s2", name: "B" })],
      listPublicDelayMs: 5,
    });

    const [a, b] = await Promise.all([h.catalog.loadPublic(), h.catalog.loadPublic()]);
    expect(h.calls.getDefaultFrames).toBe(1);
    expect(h.calls.download).toHaveLength(2);
    expect(a).toBe(b); // 같은 작업의 같은 결과 객체
    expect(a.frames.map((f) => f.name)).toEqual(["A", "B"]);
  });

  it("C5: 앞 패스가 끝난 뒤 재호출하면 새 패스를 시작한다(함정 A 회귀)", async () => {
    const h = harness({ server: async () => [] });
    await h.catalog.loadPublic();
    await h.catalog.loadPublic();
    expect(h.calls.getDefaultFrames).toBe(2);
  });

  it("공유 작업이 내부에서 즉시 실패해도 inFlight가 풀린다", async () => {
    let hit = 0;
    const h = harness({
      server: async () => {
        hit++;
        throw new Error("boom");
      },
    });
    await h.catalog.loadPublic();
    await h.catalog.loadPublic();
    expect(hit).toBe(2);
  });
});

describe("C2·C3: 진행 replay", () => {
  it("C2: 늦게 합류한 구독자가 최근 보고를 동기 1회 즉시 받는다", async () => {
    const gate = deferred();
    const h = harness({
      server: async () => {
        await gate.promise;
        return [];
      },
    });

    const first: FrameCatalogProgress[] = [];
    const pending = h.catalog.loadPublic({ onProgress: (p) => first.push(p) });
    // 첫 구독자는 이미 QueryingServer까지 봤다.
    await Promise.resolve();
    await Promise.resolve();
    expect(first.map((p) => p.phase)).toContain("QueryingServer");

    const late: FrameCatalogProgress[] = [];
    const joined = h.catalog.loadPublic({ onProgress: (p) => late.push(p) });
    // 합류 즉시(동기) 최근 국면 1건이 들어와 있다 — 문구 공백 구간이 없다.
    expect(late[0]?.phase).toBe("QueryingServer");

    gate.resolve();
    await Promise.all([pending, joined]);
  });

  it("C3: 새 패스의 첫 replay는 Completed가 아니라 ResolvingLocal이다", async () => {
    const h = harness({ server: async () => [] });
    const firstRun: FrameCatalogProgress[] = [];
    await h.catalog.loadPublic({ onProgress: (p) => firstRun.push(p) });
    expect(firstRun.at(-1)?.phase).toBe("Completed");

    const secondRun: FrameCatalogProgress[] = [];
    await h.catalog.loadPublic({ onProgress: (p) => secondRun.push(p) });
    // 홈 왕복 후 재진입의 첫 문구가 "정리하는 중…"이면 거짓이다.
    expect(secondRun[0]?.phase).toBe("ResolvingLocal");
  });

  it("구독자 예외가 로딩을 깨지 않는다", async () => {
    const h = harness({ server: async () => [] });
    const result = await h.catalog.loadPublic({
      onProgress: () => {
        throw new Error("구독자 폭발");
      },
    });
    expect(result.frames.length).toBeGreaterThan(0);
  });
});

describe("C4·C6: 호출자별 취소", () => {
  it("C4: A가 abort해도 B는 정상 결과를 받고 캐시 쓰기가 완료된다", async () => {
    const gate = deferred();
    const h = harness({
      server: async () => {
        await gate.promise;
        return [frame({ id: "s1", name: "A" })];
      },
    });

    const controller = new AbortController();
    const aborted = h.catalog.loadPublic({ signal: controller.signal });
    const other = h.catalog.loadPublic();
    controller.abort();

    await expect(aborted).rejects.toBeInstanceOf(FrameLoadCancelledError);

    gate.resolve();
    const result = await other;
    expect(result.frames.map((f) => f.name)).toEqual(["A"]);
    // 취소된 호출자와 무관하게 공유 작업이 캐시를 완성했다.
    expect(h.cached.map((f) => f.name)).toEqual(["A"]);
  });

  it("이미 abort된 신호로 부르면 즉시 취소 예외다(공유 작업은 계속)", async () => {
    const h = harness({ server: async () => [] });
    const controller = new AbortController();
    controller.abort();
    await expect(h.catalog.loadPublic({ signal: controller.signal })).rejects.toBeInstanceOf(
      FrameLoadCancelledError,
    );
  });

  it("C6: 정상 완료에서도 abort 리스너를 제거한다(함정 B)", async () => {
    const h = harness({ server: async () => [] });
    const controller = new AbortController();
    const remove = vi.spyOn(controller.signal, "removeEventListener");
    await h.catalog.loadPublic({ signal: controller.signal });
    expect(remove).toHaveBeenCalledWith("abort", expect.any(Function));
  });
});

describe("C7·C15: 실패를 삼킨다(E20)", () => {
  it("C7: 서버 조회 실패에서 reject하지 않고 캐시 결과를 돌려준다", async () => {
    const cachedFrame = frame({ id: "local:public:C", name: "C", imageUrl: "blob:c" });
    const h = harness({
      initialCache: [cachedFrame],
      server: async () => {
        throw new TypeError("Failed to fetch");
      },
    });

    const result = await h.catalog.loadPublic();
    expect(result.frames.map((f) => f.name)).toEqual(["C"]);
    expect(result.source).toBe("LocalCache");
  });

  it("C15: 어떤 내부 예외에서도 reject하지 않는다", async () => {
    const h = harness({
      fallback: async () => {
        throw new Error("fallback도 폭발");
      },
    });
    const result = await h.catalog.loadPublic();
    expect(result.frames).toEqual([]);
    expect(result.source).toBe("Fallback");
  });
});

describe("C8·C9·C10·C11·C12: 조립 규칙", () => {
  it("C8: 로컬에 같은 이름이 있으면 다운로드하지 않고 (n/m) 분모에서도 빠진다", async () => {
    const h = harness({
      initialCache: [frame({ id: "local:public:A", name: "A", imageUrl: "blob:a" })],
      server: async () => [frame({ id: "s1", name: "A" }), frame({ id: "s2", name: "B" })],
    });

    const progress: FrameCatalogProgress[] = [];
    const result = await h.catalog.loadPublic({ onProgress: (p) => progress.push(p) });

    expect(h.calls.download).toEqual(["https://cdn.example.com/B.png"]);
    expect(h.calls.cache).toEqual(["B"]);
    const downloads = progress.filter((p) => p.phase === "DownloadingImage");
    expect(downloads).toHaveLength(1);
    expect(downloads[0]?.total).toBe(1);
    expect(result.frames.map((f) => f.name)).toEqual(["A", "B"]);
  });

  it("C9: 캐시 0 + 서버 0 + 번들 1 → 번들 / 전부 0 → fallback 1개", async () => {
    const bundled = frame({ id: "bundle:B", name: "번들", imageUrl: "/frames/b.png" });
    const withBundle = harness({ bundle: async () => [bundled] });
    const bundleResult = await withBundle.catalog.loadPublic();
    expect(bundleResult.source).toBe("Bundle");
    expect(bundleResult.frames.map((f) => f.name)).toEqual(["번들"]);

    const empty = harness();
    const fallbackResult = await empty.catalog.loadPublic();
    expect(fallbackResult.source).toBe("Fallback");
    expect(fallbackResult.frames).toHaveLength(1);
  });

  it("C10: 다운로드 실패 프레임은 unavailable에만 들어간다(설계 이탈 ③)", async () => {
    const h = harness({
      server: async () => [frame({ id: "s1", name: "A" }), frame({ id: "s2", name: "B" })],
      download: async (url) => (url.endsWith("A.png") ? null : new Blob(["png"])),
    });

    const result = await h.catalog.loadPublic();
    expect(result.unavailable.map((u) => u.name)).toEqual(["A"]);
    expect(result.frames.map((f) => f.name)).toEqual(["B"]);
    // 카드는 원격 URL로 썸네일만 보여준다(선택 불가).
    expect(result.unavailable[0]?.imageUrl).toBe("https://cdn.example.com/A.png");
  });

  it("캐시 쓰기 실패도 unavailable로 떨어진다", async () => {
    const h = harness({ server: async () => [frame({ name: "A" })], cacheFails: true });
    const result = await h.catalog.loadPublic();
    expect(result.unavailable.map((u) => u.name)).toEqual(["A"]);
  });

  it("C11: 빈 URL 프레임은 frames에 없다(hasUsableImage 필터)", async () => {
    const h = harness({
      initialCache: [
        frame({ id: "local:public:A", name: "A", imageUrl: "blob:a" }),
        frame({ id: "local:public:빈", name: "빈", imageUrl: "" }),
      ],
    });
    const result = await h.catalog.loadPublic();
    expect(result.frames.map((f) => f.name)).toEqual(["A"]);
  });

  it("C12: `_` 포함 공용 프레임은 경고만 남기고 동작은 유지한다", async () => {
    const sink = createMemoryLogSink();
    const store = createLogStore({ sink, now: () => 0 });
    attachLogStore(store);

    const h = harness({ server: async () => [frame({ id: "s1", name: "a_b" })] });
    const result = await h.catalog.loadPublic();
    expect(result.frames.map((f) => f.name)).toEqual(["a_b"]);

    await store.flush();
    const text = await store.exportText();
    expect(text).toContain("'_'");
  });
});

describe("C13·C14: 백엔드를 부르지 않는 경로", () => {
  it("C13: loadLocalOnly가 백엔드를 한 번도 부르지 않는다", async () => {
    const h = harness({
      initialCache: [frame({ id: "local:public:A", name: "A", imageUrl: "blob:a" })],
      server: async () => [frame({ id: "s9", name: "서버" })],
    });

    const result = await h.catalog.loadLocalOnly();
    expect(h.calls.getDefaultFrames).toBe(0);
    expect(h.calls.download).toEqual([]);
    expect(result.frames.map((f) => f.name)).toEqual(["A"]);
  });

  it("loadLocalOnly는 단일 비행에 합류하지 않는다(진행 중 작업을 기다리지 않는다)", async () => {
    const gate = deferred();
    const h = harness({
      initialCache: [frame({ id: "local:public:A", name: "A", imageUrl: "blob:a" })],
      server: async () => {
        await gate.promise;
        return [];
      },
    });

    const pending = h.catalog.loadPublic();
    // 상한을 넘긴 그 작업을 다시 기다리면 상한이 무의미해진다 — 곧바로 결과가 나와야 한다.
    const local = await h.catalog.loadLocalOnly();
    expect(local.frames.map((f) => f.name)).toEqual(["A"]);

    gate.resolve();
    await pending;
  });

  it("C14: loadPersonal이 서버를 조회하지 않는다(설계 이탈 ⑤ · 401 세션 해제 방지)", async () => {
    const personal = frame({
      id: "local:user:me:내것",
      userId: "me",
      isDefault: false,
      name: "내것",
      imageUrl: "blob:me",
    });
    const h = harness({ personal: [personal, frame({ name: "빈", imageUrl: "" })] });

    const frames = await h.catalog.loadPersonal("me");
    expect(frames.map((f) => f.name)).toEqual(["내것"]);
    expect(h.calls.listPersonal).toEqual(["me"]);
    expect(h.calls.getDefaultFrames).toBe(0);
    expect(h.calls.getUserFrames).toBe(0);
  });
});
