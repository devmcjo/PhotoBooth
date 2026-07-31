import { readFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { afterEach, describe, expect, it } from "vitest";
import {
  createDirHandleRepo,
  DIR_HANDLE_DB_NAME,
  getDirHandleRepo,
  setDirHandleRepoForTests,
  type DirFileToWrite,
  type DirHandleRepo,
  type DirPermissionStatus,
} from "@adapters/storage/dirHandleRepo";
import { LOG_DB_NAME } from "@adapters/storage/logStore";
import { UNSUPPORTED_OPFS_CLIENT, type OpfsClient } from "@adapters/storage/opfsClient";
import type { OpfsUsage } from "@adapters/storage/opfsProtocol";
import {
  createResultsStore,
  getResultsStore,
  setResultsStoreForTests,
  type ResultsStore,
} from "@adapters/storage/resultsStore";
import {
  saveResultLocally,
  type ResultSaveInput,
  type ResultSaverDeps,
} from "@adapters/storage/resultSaver";
import { RESULTS_MAX_SESSIONS } from "@domain/results/resultsRetention";

/**
 * 결과물 로컬 보관 어댑터 — 05 §5 (M6-W)
 *
 * Worker RPC 자체는 `opfs.test.ts`가 이미 고정한다. 여기서는 **`OpfsClient` 인터페이스에 목을 주입**해
 * 보관 로직만 검증한다(`purgeSessionLeftovers` 테스트와 같은 방식).
 */

const TOKEN32 = "0123456789abcdef0123456789abcdef";
const SESSION_ID = "20260720_144500_8f14e45f-ceea-467a-9f0c-1a2b3c4d5e6f";
const FOLDER = "mcphoto_260720_1445";

afterEach(() => {
  setResultsStoreForTests(null);
  setDirHandleRepoForTests(null);
});

// ───────────────────────────── 목 ─────────────────────────────

interface OpfsMock {
  readonly client: OpfsClient;
  readonly writes: { path: string; bytes: number }[];
  readonly removes: string[];
  readonly listed: string[];
}

function opfsMock(options: {
  folders?: string[];
  usage?: OpfsUsage;
  writeOk?: (path: string) => boolean;
  removeOk?: (path: string) => boolean;
}): OpfsMock {
  const writes: { path: string; bytes: number }[] = [];
  const removes: string[] = [];
  const listed: string[] = [];
  const client: OpfsClient = {
    ...UNSUPPORTED_OPFS_CLIENT,
    async list(path) {
      listed.push(path);
      return options.folders ?? [];
    },
    async write(path, bytes) {
      writes.push({ path, bytes: bytes instanceof Blob ? bytes.size : 0 });
      return options.writeOk?.(path) ?? true;
    },
    async remove(path) {
      removes.push(path);
      return options.removeOk?.(path) ?? true;
    },
    async usage() {
      return options.usage ?? { totalBytes: 0, entries: [] };
    },
  };
  return { client, writes, removes, listed };
}

function resultsStoreMock(overrides: Partial<ResultsStore> = {}): {
  store: ResultsStore;
  calls: string[];
} {
  const calls: string[] = [];
  const store: ResultsStore = {
    async listFolders() {
      calls.push("listFolders");
      return [];
    },
    async usage() {
      calls.push("usage");
      return { totalBytes: 0, folders: [] };
    },
    async removeFolder() {
      calls.push("removeFolder");
      return true;
    },
    async readFile() {
      calls.push("readFile");
      return null;
    },
    async enforceRetention() {
      calls.push("enforceRetention");
      return 0;
    },
    ...overrides,
  };
  return { store, calls };
}

function dirRepoMock(options: {
  supported?: boolean;
  handle?: FileSystemDirectoryHandle | null;
  permission?: DirPermissionStatus;
  writeResult?: { ok: boolean; folderName: string | null };
  writeThrows?: boolean;
}): { repo: DirHandleRepo; calls: string[]; written: DirFileToWrite[][]; baseNames: string[] } {
  const calls: string[] = [];
  const written: DirFileToWrite[][] = [];
  const baseNames: string[] = [];
  const repo: DirHandleRepo = {
    isSupported() {
      calls.push("isSupported");
      return options.supported ?? false;
    },
    async pick() {
      calls.push("pick");
      return null;
    },
    async load() {
      calls.push("load");
      return options.handle ?? null;
    },
    async store() {
      calls.push("store");
      return true;
    },
    async clear() {
      calls.push("clear");
      return true;
    },
    async query() {
      calls.push("query");
      return options.permission ?? "granted";
    },
    async request() {
      calls.push("request");
      return "granted";
    },
    async writeFolder(_handle, baseFolderName, files) {
      calls.push("writeFolder");
      baseNames.push(baseFolderName);
      written.push([...files]);
      if (options.writeThrows === true) throw new Error("폴더 쓰기 폭발");
      return options.writeResult ?? { ok: true, folderName: baseFolderName };
    },
  };
  return { repo, calls, written, baseNames };
}

const FAKE_HANDLE = { kind: "directory", name: "photos" } as unknown as FileSystemDirectoryHandle;

function saveInput(overrides: Partial<ResultSaveInput> = {}): ResultSaveInput {
  return {
    finalBlob: new Blob([new Uint8Array(10)]),
    format: "Jpg",
    timelapseBlob: new Blob([new Uint8Array(20)]),
    saveLocalCopy: true,
    sessionId: SESSION_ID,
    localTime: new Date(2026, 6, 20, 14, 45, 0),
    fallbackToken: TOKEN32,
    ...overrides,
  };
}

function deps(overrides: ResultSaverDeps): ResultSaverDeps {
  let clock = 0;
  return { now: () => (clock += 5), ...overrides };
}

// ───────────────────────────── resultsStore ─────────────────────────────

describe("resultsStore — 보관본 라이브러리", () => {
  it("폴더 목록을 오름차순(오래된 순)으로 돌려준다", async () => {
    const opfs = opfsMock({ folders: ["mcphoto_260720_1600", "mcphoto_260720_1400"] });
    expect(await createResultsStore(opfs.client).listFolders()).toEqual([
      "mcphoto_260720_1400",
      "mcphoto_260720_1600",
    ]);
    expect(opfs.listed).toEqual(["results"]);
  });

  it("목록 조회가 던져도 빈 배열이다(예외 전파 금지)", async () => {
    const store = createResultsStore({
      ...UNSUPPORTED_OPFS_CLIENT,
      async list() {
        throw new Error("worker dead");
      },
    });
    expect(await store.listFolders()).toEqual([]);
  });

  it("usage는 디렉터리 항목만 폴더 용량으로 접는다", async () => {
    const store = createResultsStore(
      opfsMock({
        usage: {
          totalBytes: 350,
          entries: [
            { name: "mcphoto_260720_1600", kind: "directory", bytes: 200, fileCount: 2 },
            { name: "mcphoto_260720_1400", kind: "directory", bytes: 100, fileCount: 1 },
            { name: "stray.txt", kind: "file", bytes: 50, fileCount: 1 },
          ],
        },
      }).client,
    );
    expect(await store.usage()).toEqual({
      totalBytes: 350,
      folders: [
        { name: "mcphoto_260720_1400", bytes: 100 },
        { name: "mcphoto_260720_1600", bytes: 200 },
      ],
    });
  });

  it("규약 밖 이름은 삭제를 거부하고 remove를 부르지 않는다", async () => {
    const opfs = opfsMock({});
    const store = createResultsStore(opfs.client);
    expect(await store.removeFolder("frames")).toBe(false);
    expect(await store.removeFolder("sessions")).toBe(false);
    expect(await store.removeFolder("../x")).toBe(false);
    expect(await store.removeFolder("")).toBe(false);
    expect(opfs.removes).toEqual([]);
  });

  it("규약 이름은 재귀 삭제한다", async () => {
    const opfs = opfsMock({});
    expect(await createResultsStore(opfs.client).removeFolder(FOLDER)).toBe(true);
    expect(opfs.removes).toEqual([`results/${FOLDER}`]);
  });

  it("보관본 파일 읽기는 규약 이름·안전한 파일명만 통과시킨다", async () => {
    const read: string[] = [];
    const store = createResultsStore({
      ...UNSUPPORTED_OPFS_CLIENT,
      async readFile(path) {
        read.push(path);
        return null;
      },
    });
    await store.readFile(FOLDER, "final.jpg");
    await store.readFile("frames", "final.jpg");
    await store.readFile(FOLDER, "../secret");
    expect(read).toEqual([`results/${FOLDER}/final.jpg`]);
  });

  it("201세션이면 가장 오래된 1개만 지우고 1을 돌려준다", async () => {
    const entries = Array.from({ length: RESULTS_MAX_SESSIONS + 1 }, (_, i) => ({
      name: `mcphoto_2607${String(1 + Math.floor(i / 24)).padStart(2, "0")}_${String(i % 24).padStart(2, "0")}00`,
      kind: "directory" as const,
      bytes: 10,
      fileCount: 1,
    }));
    const opfs = opfsMock({ usage: { totalBytes: 2010, entries } });
    const removed = await createResultsStore(opfs.client).enforceRetention();
    expect(removed).toBe(1);
    expect(opfs.removes).toEqual(["results/mcphoto_260701_0000"]);
  });

  it("정리할 것이 없으면 0이고 아무것도 지우지 않는다", async () => {
    const opfs = opfsMock({});
    expect(await createResultsStore(opfs.client).enforceRetention()).toBe(0);
    expect(opfs.removes).toEqual([]);
  });

  it("삭제 실패는 개수에 세지 않는다(정직한 보고)", async () => {
    const entries = Array.from({ length: 4 }, (_, i) => ({
      name: `mcphoto_260720_${String(i).padStart(4, "0")}`,
      kind: "directory" as const,
      bytes: 10,
      fileCount: 1,
    }));
    const opfs = opfsMock({
      usage: { totalBytes: 40, entries },
      removeOk: (path) => path.endsWith("0000"),
    });
    const removed = await createResultsStore(opfs.client).enforceRetention({
      maxBytes: 15,
      maxSessions: 200,
    });
    expect(opfs.removes.length).toBe(3);
    expect(removed).toBe(1);
  });

  it("싱글턴을 테스트에서 갈아끼울 수 있다", () => {
    const { store } = resultsStoreMock();
    setResultsStoreForTests(store);
    expect(getResultsStore()).toBe(store);
  });
});

// ───────────────────────────── dirHandleRepo (② 계층) ─────────────────────────────

interface FakeWriteLog {
  folder: string;
  file: string;
  bytes: number;
  closed: boolean;
}

function makeUserDirHandle(options: {
  existing?: string[];
  keysThrows?: boolean;
  noKeys?: boolean;
  createWritableThrows?: boolean;
  writeThrows?: boolean;
}): { handle: FileSystemDirectoryHandle; writes: FakeWriteLog[]; created: string[] } {
  const writes: FakeWriteLog[] = [];
  const created: string[] = [];

  function childDir(folder: string): unknown {
    return {
      kind: "directory",
      name: folder,
      async getFileHandle(name: string) {
        return {
          kind: "file",
          name,
          async createWritable() {
            if (options.createWritableThrows === true) throw new Error("쓰기 스트림 불가");
            const entry: FakeWriteLog = { folder, file: name, bytes: 0, closed: false };
            writes.push(entry);
            return {
              async write(blob: Blob) {
                if (options.writeThrows === true) throw new Error("디스크 가득참");
                entry.bytes = blob.size;
              },
              async close() {
                entry.closed = true;
              },
            };
          },
        };
      },
    };
  }

  const handle: Record<string, unknown> = {
    kind: "directory",
    name: "photos",
    async getDirectoryHandle(name: string) {
      created.push(name);
      return childDir(name);
    },
  };
  if (options.noKeys !== true) {
    handle.keys = () => {
      if (options.keysThrows === true) throw new Error("열거 차단");
      const names = options.existing ?? [];
      return (async function* () {
        for (const name of names) yield name;
      })();
    };
  }
  return { handle: handle as unknown as FileSystemDirectoryHandle, writes, created };
}

function installPickerGlobals(options: {
  picker?: (() => Promise<unknown>) | null;
  createWritable?: boolean;
}): () => void {
  const host = globalThis as unknown as Record<string, unknown>;
  const hadPicker = "showDirectoryPicker" in host;
  const hadHandle = "FileSystemFileHandle" in host;
  const prevPicker = host.showDirectoryPicker;
  const prevHandle = host.FileSystemFileHandle;

  if (options.picker === null || options.picker === undefined) delete host.showDirectoryPicker;
  else host.showDirectoryPicker = options.picker;

  host.FileSystemFileHandle = {
    prototype: options.createWritable === false ? {} : { createWritable: () => undefined },
  };

  return () => {
    if (hadPicker) host.showDirectoryPicker = prevPicker;
    else delete host.showDirectoryPicker;
    if (hadHandle) host.FileSystemFileHandle = prevHandle;
    else delete host.FileSystemFileHandle;
  };
}

describe("dirHandleRepo — 기능 감지", () => {
  it("showDirectoryPicker가 없으면 미지원이고 pick이 null이다(예외 없음)", async () => {
    const restore = installPickerGlobals({ picker: null });
    try {
      const repo = createDirHandleRepo();
      expect(repo.isSupported()).toBe(false);
      await expect(repo.pick()).resolves.toBeNull();
      await expect(repo.load()).resolves.toBeNull();
      await expect(repo.query(FAKE_HANDLE)).resolves.toBe("unsupported");
      await expect(repo.request(FAKE_HANDLE)).resolves.toBe("unsupported");
    } finally {
      restore();
    }
  });

  it("createWritable이 없으면 미지원이다(A3 — 두 능력을 각각 감지)", () => {
    const restore = installPickerGlobals({
      picker: async () => FAKE_HANDLE,
      createWritable: false,
    });
    try {
      expect(createDirHandleRepo().isSupported()).toBe(false);
    } finally {
      restore();
    }
  });

  it("두 능력이 다 있으면 지원이다", () => {
    const restore = installPickerGlobals({ picker: async () => FAKE_HANDLE });
    try {
      expect(createDirHandleRepo().isSupported()).toBe(true);
    } finally {
      restore();
    }
  });

  it("pick은 사용자 제스처 경로에서만 호출된다 — load·query는 피커를 열지 않는다", async () => {
    let opened = 0;
    const restore = installPickerGlobals({
      picker: async () => {
        opened++;
        return FAKE_HANDLE;
      },
    });
    try {
      const repo = createDirHandleRepo();
      await repo.load();
      await repo.query(FAKE_HANDLE);
      expect(opened).toBe(0);
      await repo.pick();
      expect(opened).toBe(1);
    } finally {
      restore();
    }
  });

  it("사용자가 취소하면(AbortError) null이다", async () => {
    const restore = installPickerGlobals({
      picker: async () => {
        throw new DOMException("사용자 취소", "AbortError");
      },
    });
    try {
      await expect(createDirHandleRepo().pick()).resolves.toBeNull();
    } finally {
      restore();
    }
  });

  it("IndexedDB가 없으면 store·clear가 false, load가 null이다", async () => {
    const restore = installPickerGlobals({ picker: async () => FAKE_HANDLE });
    try {
      const repo = createDirHandleRepo();
      expect(typeof indexedDB).toBe("undefined"); // node 환경 전제
      await expect(repo.store(FAKE_HANDLE)).resolves.toBe(false);
      await expect(repo.clear()).resolves.toBe(false);
      await expect(repo.load()).resolves.toBeNull();
    } finally {
      restore();
    }
  });

  it("핸들 DB는 로그 DB와 이름이 다르다(같은 DB 버전업의 영구 blocked 회피)", () => {
    expect(DIR_HANDLE_DB_NAME).not.toBe(LOG_DB_NAME);
  });

  it("싱글턴을 테스트에서 갈아끼울 수 있다", () => {
    const { repo } = dirRepoMock({});
    setDirHandleRepoForTests(repo);
    expect(getDirHandleRepo()).toBe(repo);
  });
});

describe("dirHandleRepo — 권한", () => {
  it("권한 API가 없으면 granted로 낙관하지 않고 prompt다", async () => {
    const restore = installPickerGlobals({ picker: async () => FAKE_HANDLE });
    try {
      const repo = createDirHandleRepo();
      await expect(repo.query(FAKE_HANDLE)).resolves.toBe("prompt");
      await expect(repo.request(FAKE_HANDLE)).resolves.toBe("prompt");
    } finally {
      restore();
    }
  });

  it("권한 API 응답을 그대로 좁힌다", async () => {
    const restore = installPickerGlobals({ picker: async () => FAKE_HANDLE });
    try {
      const repo = createDirHandleRepo();
      const handleFor = (state: string): FileSystemDirectoryHandle =>
        ({
          kind: "directory",
          queryPermission: async () => state,
          requestPermission: async () => state,
        }) as unknown as FileSystemDirectoryHandle;

      expect(await repo.query(handleFor("granted"))).toBe("granted");
      expect(await repo.query(handleFor("denied"))).toBe("denied");
      expect(await repo.query(handleFor("prompt"))).toBe("prompt");
      expect(await repo.query(handleFor("무슨-값"))).toBe("prompt");
      expect(await repo.request(handleFor("granted"))).toBe("granted");
    } finally {
      restore();
    }
  });

  it("권한 조회가 던지면 prompt로 축소된다", async () => {
    const restore = installPickerGlobals({ picker: async () => FAKE_HANDLE });
    try {
      const handle = {
        kind: "directory",
        queryPermission: async () => {
          throw new Error("권한 API 폭발");
        },
      } as unknown as FileSystemDirectoryHandle;
      await expect(createDirHandleRepo().query(handle)).resolves.toBe("prompt");
    } finally {
      restore();
    }
  });
});

describe("dirHandleRepo — writeFolder", () => {
  const files: DirFileToWrite[] = [
    { name: "final.jpg", blob: new Blob([new Uint8Array(10)]) },
    { name: "timelapse.mp4", blob: new Blob([new Uint8Array(20)]) },
  ];

  it("폴더를 만들고 파일을 쓰고 반드시 close한다", async () => {
    const fake = makeUserDirHandle({});
    const result = await createDirHandleRepo().writeFolder(fake.handle, FOLDER, files);
    expect(result).toEqual({ ok: true, folderName: FOLDER });
    expect(fake.created).toEqual([FOLDER]);
    expect(fake.writes.map((w) => w.file)).toEqual(["final.jpg", "timelapse.mp4"]);
    expect(fake.writes.every((w) => w.closed)).toBe(true);
    expect(fake.writes[1]!.bytes).toBe(20);
  });

  it("같은 이름이 있으면 -2를 붙인다(기존 폴더를 덮어쓰지 않는다)", async () => {
    const fake = makeUserDirHandle({ existing: [FOLDER] });
    const result = await createDirHandleRepo().writeFolder(fake.handle, FOLDER, files);
    expect(result.folderName).toBe(`${FOLDER}-2`);
    expect(fake.created).toEqual([`${FOLDER}-2`]);
  });

  it("열거가 던지면 base 이름으로 진행한다(A2 폴백)", async () => {
    const fake = makeUserDirHandle({ keysThrows: true });
    const result = await createDirHandleRepo().writeFolder(fake.handle, FOLDER, files);
    expect(result).toEqual({ ok: true, folderName: FOLDER });
  });

  it("열거 API 자체가 없어도 base 이름으로 진행한다", async () => {
    const fake = makeUserDirHandle({ noKeys: true });
    const result = await createDirHandleRepo().writeFolder(fake.handle, FOLDER, files);
    expect(result.ok).toBe(true);
  });

  it("쓰기 실패는 예외가 아니라 ok:false다 — 그래도 close는 불린다", async () => {
    const fake = makeUserDirHandle({ writeThrows: true });
    const result = await createDirHandleRepo().writeFolder(fake.handle, FOLDER, files);
    expect(result).toEqual({ ok: false, folderName: null });
    expect(fake.writes[0]!.closed).toBe(true);
  });

  it("쓰기 스트림을 못 열어도 예외를 전파하지 않는다", async () => {
    const fake = makeUserDirHandle({ createWritableThrows: true });
    await expect(
      createDirHandleRepo().writeFolder(fake.handle, FOLDER, files),
    ).resolves.toEqual({ ok: false, folderName: null });
  });
});

// ───────────────────────────── resultSaver (①·②·③) ─────────────────────────────

describe("resultSaver — ① 보관 위치 기록", () => {
  it("final이 **먼저**, timelapse가 뒤 순서로 기록된다", async () => {
    const opfs = opfsMock({});
    const { store } = resultsStoreMock();
    const { repo } = dirRepoMock({ supported: false });
    const outcome = await saveResultLocally(
      saveInput(),
      deps({ opfs: opfs.client, results: store, dirHandles: repo }),
    );

    expect(opfs.writes.map((w) => w.path)).toEqual([
      `results/${FOLDER}/final.jpg`,
      `results/${FOLDER}/timelapse.mp4`,
    ]);
    expect(outcome.status).toBe("saved");
    expect(outcome.folderName).toBe(FOLDER);
    expect(outcome.finalSaved).toBe(true);
    expect(outcome.timelapseSaved).toBe(true);
    expect(outcome.bytes).toBe(30);
    expect(outcome.elapsedMs).toBeGreaterThan(0);
  });

  it("타임랩스가 null이면 1회만 쓰고도 saved다(VF-6 — 실패가 아니다)", async () => {
    const opfs = opfsMock({});
    const outcome = await saveResultLocally(
      saveInput({ timelapseBlob: null }),
      deps({ opfs: opfs.client, results: resultsStoreMock().store, dirHandles: dirRepoMock({}).repo }),
    );
    expect(opfs.writes).toHaveLength(1);
    expect(outcome.status).toBe("saved");
    expect(outcome.hadTimelapse).toBe(false);
    expect(outcome.timelapseSaved).toBe(false);
  });

  it("final 기록이 실패하면 status:failed이고 throw하지 않는다", async () => {
    const opfs = opfsMock({ writeOk: (path) => !path.endsWith("final.jpg") });
    const outcome = await saveResultLocally(
      saveInput(),
      deps({ opfs: opfs.client, results: resultsStoreMock().store, dirHandles: dirRepoMock({}).repo }),
    );
    expect(outcome.status).toBe("failed");
    expect(outcome.finalSaved).toBe(false);
  });

  it("타임랩스만 실패하면 partial이다", async () => {
    const opfs = opfsMock({ writeOk: (path) => !path.endsWith("timelapse.mp4") });
    const outcome = await saveResultLocally(
      saveInput(),
      deps({ opfs: opfs.client, results: resultsStoreMock().store, dirHandles: dirRepoMock({}).repo }),
    );
    expect(outcome.status).toBe("partial");
    expect(outcome.finalSaved).toBe(true);
  });

  it("Png 설정이면 final.png로 쓴다", async () => {
    const opfs = opfsMock({});
    await saveResultLocally(
      saveInput({ format: "Png", timelapseBlob: null }),
      deps({ opfs: opfs.client, results: resultsStoreMock().store, dirHandles: dirRepoMock({}).repo }),
    );
    expect(opfs.writes[0]!.path).toBe(`results/${FOLDER}/final.png`);
  });

  it("같은 폴더가 이미 있으면 경로에 -2가 붙는다", async () => {
    const opfs = opfsMock({});
    const { store } = resultsStoreMock({ async listFolders() { return [FOLDER]; } });
    const outcome = await saveResultLocally(
      saveInput({ timelapseBlob: null }),
      deps({ opfs: opfs.client, results: store, dirHandles: dirRepoMock({}).repo }),
    );
    expect(outcome.folderName).toBe(`${FOLDER}-2`);
    expect(opfs.writes[0]!.path).toBe(`results/${FOLDER}-2/final.jpg`);
  });

  it("sessionId가 깨졌으면 localTime으로 폴더명을 만든다", async () => {
    const opfs = opfsMock({});
    const outcome = await saveResultLocally(
      saveInput({ sessionId: "쓰레기", timelapseBlob: null }),
      deps({ opfs: opfs.client, results: resultsStoreMock().store, dirHandles: dirRepoMock({}).repo }),
    );
    expect(outcome.folderName).toBe(FOLDER);
  });

  it("SaveLocalCopy가 꺼져 있으면 아무것도 하지 않는다(목록 조회도 안 한다)", async () => {
    const opfs = opfsMock({});
    const { store, calls } = resultsStoreMock();
    const { repo, calls: repoCalls } = dirRepoMock({ supported: true });
    const outcome = await saveResultLocally(
      saveInput({ saveLocalCopy: false }),
      deps({ opfs: opfs.client, results: store, dirHandles: repo }),
    );
    expect(outcome.status).toBe("skipped");
    expect(opfs.writes).toEqual([]);
    expect(calls).toEqual([]);
    expect(repoCalls).toEqual([]);
  });

  it("합성 이미지가 없으면 skipped다", async () => {
    const opfs = opfsMock({});
    const outcome = await saveResultLocally(
      saveInput({ finalBlob: null }),
      deps({ opfs: opfs.client, results: resultsStoreMock().store, dirHandles: dirRepoMock({}).repo }),
    );
    expect(outcome.status).toBe("skipped");
    expect(opfs.writes).toEqual([]);
  });

  it("어떤 예외가 새어 나와도 throw하지 않고 failed로 축소된다", async () => {
    const outcome = await saveResultLocally(
      saveInput(),
      deps({
        opfs: {
          ...UNSUPPORTED_OPFS_CLIENT,
          async write() {
            throw new Error("worker dead");
          },
        },
        results: resultsStoreMock().store,
        dirHandles: dirRepoMock({}).repo,
      }),
    );
    expect(outcome.status).toBe("failed");
  });
});

describe("resultSaver — ② 사용자 지정 폴더", () => {
  it("미지원 브라우저면 unsupported이고 load·pick을 부르지 않는다", async () => {
    const { repo, calls } = dirRepoMock({ supported: false });
    const outcome = await saveResultLocally(
      saveInput(),
      deps({ opfs: opfsMock({}).client, results: resultsStoreMock().store, dirHandles: repo }),
    );
    expect(outcome.folderCopy).toBe("unsupported");
    expect(calls).toEqual(["isSupported"]);
  });

  it("핸들이 없으면 no-handle이다", async () => {
    const { repo } = dirRepoMock({ supported: true, handle: null });
    const outcome = await saveResultLocally(
      saveInput(),
      deps({ opfs: opfsMock({}).client, results: resultsStoreMock().store, dirHandles: repo }),
    );
    expect(outcome.folderCopy).toBe("no-handle");
  });

  it("권한이 granted가 아니면 permission-required이고 **request를 부르지 않는다**", async () => {
    const { repo, calls } = dirRepoMock({
      supported: true,
      handle: FAKE_HANDLE,
      permission: "prompt",
    });
    const outcome = await saveResultLocally(
      saveInput(),
      deps({ opfs: opfsMock({}).client, results: resultsStoreMock().store, dirHandles: repo }),
    );
    expect(outcome.folderCopy).toBe("permission-required");
    expect(calls).not.toContain("request");
    expect(calls).not.toContain("writeFolder");
  });

  it("granted면 ①과 같은 파일 목록을 복사한다", async () => {
    const { repo, written, baseNames } = dirRepoMock({ supported: true, handle: FAKE_HANDLE });
    const outcome = await saveResultLocally(
      saveInput(),
      deps({ opfs: opfsMock({}).client, results: resultsStoreMock().store, dirHandles: repo }),
    );
    expect(outcome.folderCopy).toBe("copied");
    expect(written[0]!.map((f) => f.name)).toEqual(["final.jpg", "timelapse.mp4"]);
    // ②는 자기 위치의 기존 이름으로 독립 해석한다 — base 이름을 넘긴다.
    expect(baseNames).toEqual([FOLDER]);
  });

  it("②가 ①과 다른 폴더명을 만들면 그 값이 folderCopyName에 담긴다", async () => {
    const { repo } = dirRepoMock({
      supported: true,
      handle: FAKE_HANDLE,
      writeResult: { ok: true, folderName: `${FOLDER}-7` },
    });
    const outcome = await saveResultLocally(
      saveInput(),
      deps({ opfs: opfsMock({}).client, results: resultsStoreMock().store, dirHandles: repo }),
    );
    expect(outcome.folderName).toBe(FOLDER);
    expect(outcome.folderCopyName).toBe(`${FOLDER}-7`);
  });

  it("②가 던져도 ①의 status를 바꾸지 않는다", async () => {
    const { repo } = dirRepoMock({ supported: true, handle: FAKE_HANDLE, writeThrows: true });
    const outcome = await saveResultLocally(
      saveInput({ timelapseBlob: null }),
      deps({ opfs: opfsMock({}).client, results: resultsStoreMock().store, dirHandles: repo }),
    );
    expect(outcome.folderCopy).toBe("failed");
    expect(outcome.status).toBe("saved");
  });

  it("① 기록이 실패해도 ②는 시도한다(보관 기회를 버리지 않는다)", async () => {
    const opfs = opfsMock({ writeOk: () => false });
    const { repo, calls } = dirRepoMock({ supported: true, handle: FAKE_HANDLE });
    const outcome = await saveResultLocally(
      saveInput(),
      deps({ opfs: opfs.client, results: resultsStoreMock().store, dirHandles: repo }),
    );
    expect(outcome.status).toBe("failed");
    expect(outcome.folderCopy).toBe("copied");
    expect(calls).toContain("writeFolder");
  });
});

describe("resultSaver — ③ 보존 정책", () => {
  it("삭제된 폴더 수를 evicted로 보고한다", async () => {
    const { store } = resultsStoreMock({ async enforceRetention() { return 3; } });
    const outcome = await saveResultLocally(
      saveInput({ timelapseBlob: null }),
      deps({ opfs: opfsMock({}).client, results: store, dirHandles: dirRepoMock({}).repo }),
    );
    expect(outcome.evicted).toBe(3);
  });

  it("정리가 던져도 status가 바뀌지 않는다(보관의 성패와 무관하다)", async () => {
    const { store } = resultsStoreMock({
      async enforceRetention() {
        throw new Error("정리 폭발");
      },
    });
    const outcome = await saveResultLocally(
      saveInput({ timelapseBlob: null }),
      deps({ opfs: opfsMock({}).client, results: store, dirHandles: dirRepoMock({}).repo }),
    );
    expect(outcome.status).toBe("saved");
    expect(outcome.evicted).toBe(0);
  });

  it("skip 경로에서는 정리도 하지 않는다", async () => {
    const { store, calls } = resultsStoreMock();
    await saveResultLocally(
      saveInput({ saveLocalCopy: false }),
      deps({ opfs: opfsMock({}).client, results: store, dirHandles: dirRepoMock({}).repo }),
    );
    expect(calls).not.toContain("enforceRetention");
  });
});

// ─────────────────────── 정적 불변식(15 §3.4 관례) ───────────────────────

describe("보관 계층 정적 불변식", () => {
  const STORAGE_DIR = join(
    dirname(fileURLToPath(import.meta.url)),
    "..",
    "..",
    "..",
    "src",
    "adapters",
    "storage",
  );

  function source(name: string): string {
    return readFileSync(join(STORAGE_DIR, name), "utf8");
  }

  /**
   * VF-14: 브라우저 내부 저장소(OPFS) 쓰기는 전용 Worker만 한다.
   * 메인 스레드에서 쓰면 iOS/iPadOS Safari에서 **전 저장 경로가 실패**한다.
   */
  it.each(["resultSaver.ts", "resultsStore.ts"])(
    "%s — 메인 스레드에서 내부 저장소를 직접 만지지 않는다",
    (name) => {
      const code = source(name);
      expect(code.includes("navigator.storage")).toBe(false);
      expect(code.includes("createWritable")).toBe(false);
      expect(code.includes("createSyncAccessHandle")).toBe(false);
      expect(code.includes("getDirectory(")).toBe(false);
    },
  );

  it("resultSaver.ts는 OpfsClient를 통해서만 쓰고 Worker를 직접 import하지 않는다", () => {
    const code = source("resultSaver.ts");
    expect(code.includes("OpfsClient")).toBe(true);
    expect(code.includes("opfsWriter.worker")).toBe(false);
  });

  /**
   * ⚠️ `dirHandleRepo.ts`는 이 검사에서 `createWritable`이 **의도적으로 제외**된다.
   *    대상이 내부 저장소가 아니라 **사용자가 고른 디렉터리**이고, 전용 Worker는 그 핸들에
   *    닿을 수조차 없다. Safari에는 `showDirectoryPicker`가 없어 이 계층 자체가 꺼진다(05 §5.3).
   *    대신 이 파일이 내부 저장소를 건드리지 않는다는 것을 여기서 고정한다.
   *    (`getDirectoryHandle`은 사용자 디렉터리 API라 `getDirectory(` 검사에 걸리지 않는다.)
   */
  it("dirHandleRepo.ts는 내부 저장소를 건드리지 않는다", () => {
    const code = source("dirHandleRepo.ts");
    expect(code.includes("navigator.storage")).toBe(false);
    expect(code.includes("getDirectory(")).toBe(false);
    expect(code.includes("OPFS_DIRS")).toBe(false);
    expect(code.includes("createSyncAccessHandle")).toBe(false);
  });

  it("resultsStore.ts는 results/ 밖의 상단 디렉터리를 다루지 않는다", () => {
    const code = source("resultsStore.ts");
    expect(code.includes("sessions/")).toBe(false);
    expect(code.includes("frames/")).toBe(false);
  });

  it.each(["resultSaver.ts", "resultsStore.ts", "dirHandleRepo.ts"])(
    "%s — console.*를 쓰지 않는다(logger만)",
    (name) => {
      expect(/\bconsole\s*\./.test(source(name))).toBe(false);
    },
  );

  it("opfsWriter.worker.ts의 usage 핸들러는 쓰기·삭제 경로를 만들지 않는다", () => {
    const code = readFileSync(join(STORAGE_DIR, "opfsWriter.worker.ts"), "utf8");
    const start = code.indexOf("async function usage(");
    const end = code.indexOf("async function handle(");
    expect(start).toBeGreaterThan(0);
    expect(end).toBeGreaterThan(start);
    const usageBody = code.slice(start, end);
    expect(usageBody.includes("createSyncAccessHandle")).toBe(false);
    expect(usageBody.includes("createWritable")).toBe(false);
    expect(usageBody.includes("removeEntry")).toBe(false);
  });
});
