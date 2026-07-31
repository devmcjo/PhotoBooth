import { readFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { afterEach, beforeEach, describe, expect, it } from "vitest";
import type { FrameRecord } from "@domain/frames/frameStorePolicy";
import type { FrameTemplate } from "@domain/frames/types";
import {
  createFrameStore,
  createMemoryFrameMeta,
  FRAME_DB_NAME,
  FRAME_STORE_NAME,
  type FrameMetaStore,
  type FrameStore,
} from "@adapters/storage/frameStore";
import { DIR_HANDLE_DB_NAME } from "@adapters/storage/dirHandleRepo";
import {
  attachLogStore,
  createLogStore,
  createMemoryLogSink,
  detachLogStore,
  LOG_DB_NAME,
} from "@adapters/storage/logStore";
import type { OpfsClient } from "@adapters/storage/opfsClient";
import type { OpfsUsage } from "@adapters/storage/opfsProtocol";

/**
 * 프레임 저장소 — 설계 §7 S1~S10 (05 §4)
 *
 * 여기서 고정하는 것은 **순서**와 **정직한 실패**다: 이미지를 쓰기 전에 메타를 기록하면 반쪽
 * 프레임이 목록에 오르고, 삭제 결과를 확인하지 않으면 지워지지 않은 카드가 사라진 척한다(M4).
 */

const SRC = join(dirname(fileURLToPath(import.meta.url)), "..", "..", "..", "src");

/** 가짜 OPFS. 파일 내용은 경로→바이트 맵이고, 각 연산의 성공 여부를 개별로 강제할 수 있다. */
function fakeOpfs(options: {
  writeOk?: boolean;
  removeOk?: boolean;
  /** remove가 성공을 보고해도 파일이 남는 상황(권한·잠금 흉내). */
  removeLeavesFile?: boolean;
  readOk?: boolean;
  usage?: OpfsUsage | (() => never);
} = {}): OpfsClient & { files: Map<string, Blob>; calls: string[] } {
  const files = new Map<string, Blob>();
  const calls: string[] = [];
  const client: OpfsClient & { files: Map<string, Blob>; calls: string[] } = {
    files,
    calls,
    async write(path, bytes) {
      calls.push(`write:${path}`);
      if (options.writeOk === false) return false;
      files.set(path, bytes instanceof Blob ? bytes : new Blob([bytes]));
      return true;
    },
    async remove(path) {
      calls.push(`remove:${path}`);
      if (options.removeOk === false) return false;
      if (options.removeLeavesFile !== true) files.delete(path);
      return true;
    },
    async list() {
      return [];
    },
    async exists(path) {
      calls.push(`exists:${path}`);
      return files.has(path);
    },
    async usage() {
      calls.push("usage");
      const value = options.usage;
      if (typeof value === "function") return value();
      return value ?? { totalBytes: 0, entries: [] };
    },
    async capability() {
      return "sync-access-handle";
    },
    async readFile(path) {
      calls.push(`readFile:${path}`);
      if (options.readOk === false) return null;
      const blob = files.get(path);
      if (blob === undefined) return null;
      return blob as unknown as File;
    },
  };
  return client;
}

let tokenCounter = 0;

function store(
  meta: FrameMetaStore,
  opfs: OpfsClient,
  extra: { released?: string[] } = {},
): FrameStore {
  return createFrameStore({
    meta,
    opfs,
    newToken: () => `tok${++tokenCounter}`,
    now: () => new Date("2026-08-01T00:00:00.000Z"),
    // node에 File·URL 왕복을 강요하지 않는다 — URL 소유는 frameImageCache가 별도로 검증한다.
    imageUrl: (path) => `blob:${path}`,
    releaseImage: (path) => extra.released?.push(path),
  });
}

function serverFrame(overrides: Partial<FrameTemplate> = {}): FrameTemplate {
  return {
    id: "srv-1",
    userId: null,
    isDefault: true,
    name: "베이직 4컷",
    imageUrl: "https://cdn.example.com/a.png",
    imageSize: { width: 1200, height: 1600 },
    slots: [{ index: 0, x: 10, y: 20, width: 100, height: 200 }],
    createdAt: "2026-07-01T00:00:00.000Z",
    ...overrides,
  };
}

function record(overrides: Partial<FrameRecord> = {}): FrameRecord {
  return {
    key: "public:베이직 4컷",
    scope: "public",
    ownerId: null,
    name: "베이직 4컷",
    id: "srv-1",
    dbId: "srv-1",
    imageFile: "frames/seed.png",
    imageSize: { width: 1200, height: 1600 },
    slots: [{ index: 0, x: 10, y: 20, width: 100, height: 200 }],
    createdAt: "2026-07-01T00:00:00.000Z",
    updatedAt: "2026-07-01T00:00:00.000Z",
    ...overrides,
  };
}

beforeEach(() => {
  tokenCounter = 0;
  attachLogStore(createLogStore({ sink: createMemoryLogSink(), now: () => 0 }));
});

afterEach(() => {
  detachLogStore();
});

describe("S1: cacheServerFrame — OPFS 쓰기 성공 후에만 메타를 기록한다", () => {
  it("성공하면 파일과 메타가 모두 생기고 템플릿을 돌려준다", async () => {
    const meta = createMemoryFrameMeta();
    const opfs = fakeOpfs();
    const cached = await store(meta, opfs).cacheServerFrame(serverFrame(), new Blob(["png"]));

    expect(cached).not.toBeNull();
    expect(cached?.id).toBe("srv-1");
    expect(cached?.imageUrl).toBe("blob:frames/tok1.png");
    expect(opfs.files.has("frames/tok1.png")).toBe(true);
    const stored = await meta.all();
    expect(stored).toHaveLength(1);
    expect(stored[0]!.dbId).toBe("srv-1");
    expect(stored[0]!.key).toBe("public:베이직 4컷");
  });

  it("쓰기가 실패하면 메타가 0건이고 null이다(반쪽 레코드 금지)", async () => {
    const meta = createMemoryFrameMeta();
    const opfs = fakeOpfs({ writeOk: false });
    expect(await store(meta, opfs).cacheServerFrame(serverFrame(), new Blob(["x"]))).toBeNull();
    expect(await meta.all()).toHaveLength(0);
  });

  it("메타 기록이 실패하면 방금 쓴 파일을 지운다(고아 바이트 금지)", async () => {
    const opfs = fakeOpfs();
    const failing: FrameMetaStore = {
      all: async () => [],
      put: async () => false,
      delete: async () => true,
    };
    expect(await store(failing, opfs).cacheServerFrame(serverFrame(), new Blob(["x"]))).toBeNull();
    expect(opfs.files.size).toBe(0);
  });

  it("쓰기가 메타 기록보다 **먼저** 일어난다(호출 순서)", async () => {
    const order: string[] = [];
    const opfs = fakeOpfs();
    const originalWrite = opfs.write.bind(opfs);
    opfs.write = async (path, bytes) => {
      order.push("write");
      return originalWrite(path, bytes);
    };
    const meta: FrameMetaStore = {
      all: async () => [],
      put: async () => {
        order.push("put");
        return true;
      },
      delete: async () => true,
    };
    await store(meta, opfs).cacheServerFrame(serverFrame(), new Blob(["x"]));
    expect(order).toEqual(["write", "put"]);
  });
});

describe("S2·S3·S7: 목록", () => {
  it("S2: 이미지 파일이 없는 레코드를 건너뛴다(반쪽 프레임 미노출)", async () => {
    const meta = createMemoryFrameMeta([
      record({ key: "public:A", name: "A", id: "a", imageFile: "frames/a.png" }),
      record({ key: "public:B", name: "B", id: "b", imageFile: "frames/missing.png" }),
    ]);
    const opfs = fakeOpfs();
    opfs.files.set("frames/a.png", new Blob(["a"]));

    const frames = await store(meta, opfs).listPublic();
    expect(frames.map((f) => f.name)).toEqual(["A"]);
  });

  it("S3: listPersonal은 타인 소유·공용을 제외한다", async () => {
    const meta = createMemoryFrameMeta([
      record({ key: "public:P", name: "P", id: "p", imageFile: "frames/p.png" }),
      record({
        key: "user:me:M",
        scope: "user",
        ownerId: "me",
        name: "M",
        id: "local:user:me:M",
        dbId: null,
        imageFile: "frames/m.png",
      }),
      record({
        key: "user:other:O",
        scope: "user",
        ownerId: "other",
        name: "O",
        id: "local:user:other:O",
        dbId: null,
        imageFile: "frames/o.png",
      }),
    ]);
    const opfs = fakeOpfs();
    for (const path of ["frames/p.png", "frames/m.png", "frames/o.png"]) {
      opfs.files.set(path, new Blob(["x"]));
    }

    const s = store(meta, opfs);
    expect((await s.listPersonal("me")).map((f) => f.name)).toEqual(["M"]);
    expect((await s.listPublic()).map((f) => f.name)).toEqual(["P"]);
    expect(await s.listPersonal("")).toEqual([]);
    expect(await s.countPersonal("me")).toBe(1);
    expect(await s.countPersonal("nobody")).toBe(0);
  });

  it("S7: 손상된 메타 레코드가 섞여 있어도 나머지를 돌려준다", async () => {
    // IndexedDB 어댑터가 `isFrameRecord`로 거른 뒤를 흉내낸다 — 여기서는 목록 루프가
    // 개별 실패(파일 없음·읽기 실패)에도 끝까지 도는지를 본다.
    const meta = createMemoryFrameMeta([
      record({ key: "public:A", name: "A", id: "a", imageFile: "frames/a.png" }),
      record({ key: "public:BAD", name: "BAD", id: "bad", imageFile: "frames/nope.png" }),
      record({ key: "public:C", name: "C", id: "c", imageFile: "frames/c.png" }),
    ]);
    const opfs = fakeOpfs();
    opfs.files.set("frames/a.png", new Blob(["a"]));
    opfs.files.set("frames/c.png", new Blob(["c"]));

    expect((await store(meta, opfs).listPublic()).map((f) => f.name)).toEqual(["A", "C"]);
  });

  it("IndexedDB 메타 조회 실패는 빈 목록으로 축퇴한다(예외 미전파)", async () => {
    const throwing: FrameMetaStore = {
      all: async () => {
        throw new Error("boom");
      },
      put: async () => true,
      delete: async () => true,
    };
    expect(await store(throwing, fakeOpfs()).listPublic()).toEqual([]);
    expect(await store(throwing, fakeOpfs()).countPersonal("me")).toBe(0);
  });
});

describe("S4·S5·S6: deleteLocal — 성공 판정은 실제 부재 확인이다", () => {
  it("S4: 메타·파일이 모두 사라지고 true", async () => {
    const meta = createMemoryFrameMeta([record({ imageFile: "frames/a.png" })]);
    const opfs = fakeOpfs();
    opfs.files.set("frames/a.png", new Blob(["a"]));
    const released: string[] = [];

    const frames = await store(meta, opfs, { released }).listPublic();
    expect(await store(meta, opfs, { released }).deleteLocal(frames[0]!)).toBe(true);
    expect(await meta.all()).toHaveLength(0);
    expect(opfs.files.has("frames/a.png")).toBe(false);
    expect(released).toContain("frames/a.png");
  });

  it("S5: remove가 성공을 보고해도 파일이 남으면 false다(M4)", async () => {
    const meta = createMemoryFrameMeta([record({ imageFile: "frames/a.png" })]);
    const opfs = fakeOpfs({ removeLeavesFile: true });
    opfs.files.set("frames/a.png", new Blob(["a"]));
    const released: string[] = [];

    const s = store(meta, opfs, { released });
    const frames = await s.listPublic();
    expect(await s.deleteLocal(frames[0]!)).toBe(false);
    // 카드가 재스캔으로 돌아오므로 URL을 해제하지 않는다(썸네일이 깨지지 않게).
    expect(released).toEqual([]);
  });

  it("S6: 파일이 애초에 없으면 메타를 지우고 true다(설계 이탈 ④)", async () => {
    const meta = createMemoryFrameMeta([record({ imageFile: "frames/gone.png" })]);
    const opfs = fakeOpfs();
    const released: string[] = [];

    const template: FrameTemplate = { ...serverFrame(), imageUrl: "blob:x" };
    expect(await store(meta, opfs, { released }).deleteLocal(template)).toBe(true);
    expect(await meta.all()).toHaveLength(0);
    expect(released).toContain("frames/gone.png");
  });

  it("레코드가 없으면 false다(번들·fallback을 지운 척하지 않는다)", async () => {
    const meta = createMemoryFrameMeta();
    const template: FrameTemplate = {
      ...serverFrame({ id: "bundle:베이직", name: "베이직" }),
      imageUrl: "/frames/basic.png",
    };
    expect(await store(meta, fakeOpfs()).deleteLocal(template)).toBe(false);
  });

  it("id가 달라도 스코프+이름 키로 레코드를 찾는다", async () => {
    const meta = createMemoryFrameMeta([record({ id: "다른id", imageFile: "frames/a.png" })]);
    const opfs = fakeOpfs();
    opfs.files.set("frames/a.png", new Blob(["a"]));
    expect(await store(meta, opfs).deleteLocal(serverFrame())).toBe(true);
  });
});

describe("S8: usageBytes", () => {
  it("OpfsClient.usage(\"frames\")를 쓴다", async () => {
    const opfs = fakeOpfs({ usage: { totalBytes: 4096, entries: [] } });
    expect(await store(createMemoryFrameMeta(), opfs).usageBytes()).toBe(4096);
    expect(opfs.calls).toContain("usage");
  });

  it("실패는 0이다(예외 미전파)", async () => {
    const opfs = fakeOpfs({
      usage: () => {
        throw new Error("boom");
      },
    });
    expect(await store(createMemoryFrameMeta(), opfs).usageBytes()).toBe(0);
  });
});

describe("saveLocal — Step 15가 쓰는 저장 경로", () => {
  it("개인 스코프는 local: id를 갖고 소유자에게만 보인다", async () => {
    const meta = createMemoryFrameMeta();
    const opfs = fakeOpfs();
    const saved = await store(meta, opfs).saveLocal({
      scope: "user",
      ownerId: "devmcjo",
      name: "내프레임",
      dbId: null,
      imageSize: { width: 100, height: 200 },
      slots: [{ index: 0, x: 0, y: 0, width: 10, height: 10 }],
      bytes: new Blob(["png"]),
    });

    expect(saved?.id).toBe("local:user:devmcjo:내프레임");
    expect(saved?.userId).toBe("devmcjo");
    expect(saved?.isDefault).toBe(false);
    expect((await store(meta, opfs).listPersonal("devmcjo")).map((f) => f.name)).toEqual([
      "내프레임",
    ]);
  });
});

describe("S9·S10: 정적 불변식", () => {
  it("S9: frameStore.ts에 OPFS 직접 접근이 0건이다(VF-14)", () => {
    const source = readFileSync(join(SRC, "adapters/storage/frameStore.ts"), "utf8")
      .replace(/\/\*[\s\S]*?\*\//g, "")
      .replace(/(^|[^:])\/\/.*$/gm, "$1");
    for (const forbidden of [
      "navigator.storage",
      "createWritable",
      "createSyncAccessHandle",
      "getDirectory(",
    ]) {
      expect(source.includes(forbidden), `frameStore.ts: ${forbidden} 금지`).toBe(false);
    }
  });

  it("S10: 프레임 DB 이름이 로그 DB·폴더 핸들 DB와 다르다", () => {
    // 같으면 로그 스토어의 상시 연결 때문에 업그레이드가 영구 blocked 된다(F-6).
    expect(FRAME_DB_NAME).toBe("mcphoto-frames");
    expect(FRAME_DB_NAME).not.toBe(LOG_DB_NAME);
    expect(FRAME_DB_NAME).not.toBe(DIR_HANDLE_DB_NAME);
    expect(new Set([FRAME_DB_NAME, LOG_DB_NAME, DIR_HANDLE_DB_NAME]).size).toBe(3);
    expect(FRAME_STORE_NAME).toBe("frames");
  });
});
