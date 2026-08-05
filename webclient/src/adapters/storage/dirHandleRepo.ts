import { resolveResultFolderName } from "@domain/results/resultNaming";
import { logger } from "./logStore";

/**
 * 사용자 지정 폴더 계층(②) — 05 §5.3 · 12 C1 (Chromium 데스크톱 전용)
 *
 * 운영자가 폴더를 1회 지정하면 결과물이 그 폴더에도 그대로 생겨 Windows 앱과 동등해진다.
 * `showDirectoryPicker`와 "폴더 핸들의 IndexedDB 영속"은 **둘 다 Chromium에만** 있으므로
 * 기능 감지 하나로 이 계층 전체를 켜고 끈다.
 *
 * ⚠️ **이 파일에서만 메인 스레드 `createWritable()`이 허용된다.** VF-14("메인에서 쓰면 iOS에서
 *    전 저장 실패")는 브라우저 내부 저장소(OPFS) 경로의 규칙이고, 여기 대상은 **사용자가 고른
 *    디렉터리**다. 전용 Worker는 그 핸들에 닿을 수조차 없고, Safari에는 `showDirectoryPicker`가
 *    없어 이 계층 자체가 꺼진다.
 * ⚠️ 반대로 이 파일은 브라우저 내부 저장소를 **절대 건드리지 않는다**(정적 검사가 고정한다).
 * ⚠️ 어댑터 규약: 예외를 전파하지 않는다. 모든 실패가 `null`·`false`다.
 */

export type DirPermissionStatus = "granted" | "prompt" | "denied" | "unsupported";

export interface DirFolderWriteResult {
  readonly ok: boolean;
  /** 실제로 만든 폴더명. ①과 다를 수 있다(충돌 해석이 위치마다 독립이다). */
  readonly folderName: string | null;
}

export interface DirFileToWrite {
  readonly name: string;
  readonly blob: Blob;
}

export interface DirHandleRepo {
  /** `showDirectoryPicker`·`createWritable`을 **런타임 감지**한다(함정 #2 — 타입 선언을 믿지 않는다). */
  isSupported(): boolean;
  /** ⚠️ **사용자 제스처에서만** 호출한다. 취소·실패는 `null`. */
  pick(): Promise<FileSystemDirectoryHandle | null>;
  load(): Promise<FileSystemDirectoryHandle | null>;
  store(handle: FileSystemDirectoryHandle): Promise<boolean>;
  clear(): Promise<boolean>;
  /** 권한 **조회**만 한다(제스처 불요). */
  query(handle: FileSystemDirectoryHandle): Promise<DirPermissionStatus>;
  /** 권한 **요청**. ⚠️ 사용자 버튼에서만 호출한다. */
  request(handle: FileSystemDirectoryHandle): Promise<DirPermissionStatus>;
  /** 폴더를 만들고 파일들을 쓴다. 실패는 `{ ok: false, folderName: null }`. */
  writeFolder(
    handle: FileSystemDirectoryHandle,
    baseFolderName: string,
    files: readonly DirFileToWrite[],
  ): Promise<DirFolderWriteResult>;
}

/**
 * 로그 DB(`mcphoto` v1)와 **다른 DB**를 쓴다.
 *
 * 로그 스토어는 앱 수명 내내 `mcphoto` 연결을 붙들고 있는데 그 연결에 `onversionchange`가 없다.
 * 여기서 같은 DB를 v2로 열면 업그레이드가 **영구 blocked** 되어 폴더 지정이 조용히 멈춘다.
 * 부수 이점: 진단의 [로그 지우기]가 폴더 지정을 날리지 않는다.
 */
export const DIR_HANDLE_DB_NAME = "mcphoto-handles";
export const DIR_HANDLE_DB_VERSION = 1;
export const DIR_HANDLE_STORE = "handles";
export const DIR_HANDLE_KEY = "localSaveDir";

/** 표준 DOM lib에 없는 능력들. 선언이 아니라 **런타임 존재**로 판정한다. */
interface DirectoryPickerOptions {
  readonly mode?: "read" | "readwrite";
  readonly startIn?: string;
}
type PickerHost = {
  showDirectoryPicker?: (options?: DirectoryPickerOptions) => Promise<FileSystemDirectoryHandle>;
  FileSystemFileHandle?: { prototype?: { createWritable?: unknown } };
};
type PermissionCapableHandle = FileSystemDirectoryHandle & {
  queryPermission?: (descriptor: { mode: "read" | "readwrite" }) => Promise<string>;
  requestPermission?: (descriptor: { mode: "read" | "readwrite" }) => Promise<string>;
};
type KeyEnumerableHandle = { keys?: () => AsyncIterable<string> };

function pickerHost(): PickerHost {
  return globalThis as unknown as PickerHost;
}

function normalizePermission(state: string): DirPermissionStatus {
  return state === "granted" || state === "denied" ? state : "prompt";
}

/** 충돌 999회 소진 시 접미. 도메인은 난수를 만들지 않으므로 어댑터가 주입한다(01 §8). */
function newFallbackToken(): string {
  const source = globalThis.crypto;
  if (typeof source?.randomUUID === "function") return source.randomUUID().replace(/-/g, "");
  // 극단 폴백 — 32자 hex 모양을 유지해야 규약 판정(`isResultFolderName`)을 통과한다.
  return Date.now().toString(16).padStart(32, "0").slice(0, 32);
}

function openHandleDb(): Promise<IDBDatabase> {
  return new Promise((resolve, reject) => {
    if (typeof indexedDB === "undefined") {
      reject(new Error("이 브라우저에는 IndexedDB가 없습니다."));
      return;
    }
    const request = indexedDB.open(DIR_HANDLE_DB_NAME, DIR_HANDLE_DB_VERSION);
    request.onupgradeneeded = () => {
      const db = request.result;
      if (!db.objectStoreNames.contains(DIR_HANDLE_STORE)) db.createObjectStore(DIR_HANDLE_STORE);
    };
    request.onsuccess = () => {
      const db = request.result;
      // 다른 탭이 버전을 올릴 때 이 연결이 blocked를 만들지 않게 한다(로그 DB가 빠진 함정).
      db.onversionchange = () => db.close();
      resolve(db);
    };
    request.onerror = () => reject(request.error ?? new Error("폴더 핸들 DB를 열 수 없습니다."));
  });
}

/** 트랜잭션 1회를 열고 반드시 닫는다. 연결을 붙들지 않아 다른 탭의 업그레이드를 막지 않는다. */
async function withHandleStore<T>(
  mode: IDBTransactionMode,
  run: (store: IDBObjectStore) => IDBRequest,
): Promise<T> {
  const db = await openHandleDb();
  try {
    return await new Promise<T>((resolve, reject) => {
      const tx = db.transaction(DIR_HANDLE_STORE, mode);
      const request = run(tx.objectStore(DIR_HANDLE_STORE));
      tx.oncomplete = () => resolve(request.result as T);
      tx.onerror = () => reject(tx.error ?? new Error("폴더 핸들 트랜잭션 실패"));
      tx.onabort = () => reject(tx.error ?? new Error("폴더 핸들 트랜잭션 중단"));
    });
  } finally {
    db.close();
  }
}

export function createDirHandleRepo(): DirHandleRepo {
  function isSupported(): boolean {
    const host = pickerHost();
    if (typeof host.showDirectoryPicker !== "function") return false;
    // A3 방어: 피커가 있다고 쓰기 능력까지 있다고 단정하지 않는다. 실제 프로토타입을 본다.
    return typeof host.FileSystemFileHandle?.prototype?.createWritable === "function";
  }

  async function pick(): Promise<FileSystemDirectoryHandle | null> {
    if (!isSupported()) return null;
    const open = pickerHost().showDirectoryPicker;
    if (open === undefined) return null;
    try {
      // 취소는 AbortError 예외로 온다 — 정상 경로이므로 조용히 null이다.
      return (await open({ mode: "readwrite", startIn: "documents" })) ?? null;
    } catch {
      return null;
    }
  }

  async function load(): Promise<FileSystemDirectoryHandle | null> {
    if (!isSupported()) return null;
    try {
      const stored = await withHandleStore<unknown>("readonly", (store) =>
        store.get(DIR_HANDLE_KEY),
      );
      // 구조화 복제로 되살아난 핸들인지 최소 확인한다(다른 값이 들어 있을 수 있다).
      if (
        stored !== null &&
        typeof stored === "object" &&
        (stored as FileSystemHandle).kind === "directory"
      ) {
        return stored as FileSystemDirectoryHandle;
      }
      return null;
    } catch {
      return null;
    }
  }

  async function store(handle: FileSystemDirectoryHandle): Promise<boolean> {
    try {
      await withHandleStore<unknown>("readwrite", (objectStore) =>
        objectStore.put(handle, DIR_HANDLE_KEY),
      );
      return true;
    } catch (err) {
      logger.warn("로컬 저장 폴더 기억 실패", {
        reason: err instanceof Error ? err.message : String(err),
      });
      return false;
    }
  }

  async function clear(): Promise<boolean> {
    try {
      await withHandleStore<unknown>("readwrite", (objectStore) =>
        objectStore.delete(DIR_HANDLE_KEY),
      );
      return true;
    } catch {
      return false;
    }
  }

  async function query(handle: FileSystemDirectoryHandle): Promise<DirPermissionStatus> {
    if (!isSupported()) return "unsupported";
    const capable = handle as PermissionCapableHandle;
    // 권한 API가 없으면 `"granted"`로 낙관하지 않는다 — 건너뛰는 편이 조용한 실패보다 정직하다.
    if (typeof capable.queryPermission !== "function") return "prompt";
    try {
      return normalizePermission(await capable.queryPermission({ mode: "readwrite" }));
    } catch {
      return "prompt";
    }
  }

  async function request(handle: FileSystemDirectoryHandle): Promise<DirPermissionStatus> {
    if (!isSupported()) return "unsupported";
    const capable = handle as PermissionCapableHandle;
    if (typeof capable.requestPermission !== "function") return "prompt";
    try {
      return normalizePermission(await capable.requestPermission({ mode: "readwrite" }));
    } catch {
      return "prompt";
    }
  }

  async function listExisting(handle: FileSystemDirectoryHandle): Promise<string[]> {
    const names: string[] = [];
    try {
      const enumerable = handle as unknown as KeyEnumerableHandle;
      if (typeof enumerable.keys !== "function") return [];
      for await (const name of enumerable.keys()) names.push(name);
      return names;
    } catch {
      // 열거가 막히면 충돌 검사를 포기하고 base 이름으로 진행한다(A2 폴백).
      return [];
    }
  }

  async function writeFolder(
    handle: FileSystemDirectoryHandle,
    baseFolderName: string,
    files: readonly DirFileToWrite[],
  ): Promise<DirFolderWriteResult> {
    try {
      const existing = await listExisting(handle);
      // ①과 **같은 도메인 함수**를 쓰되 결과는 다를 수 있다. 이름을 맞추려고 기존 폴더를
      // 덮어쓰면 사용자 파일이 사라진다 — 절대 하지 않는다.
      const folderName = resolveResultFolderName(baseFolderName, existing, newFallbackToken());
      const target = await handle.getDirectoryHandle(folderName, { create: true });

      for (const file of files) {
        const fileHandle = await target.getFileHandle(file.name, { create: true });
        const writable = await fileHandle.createWritable();
        try {
          await writable.write(file.blob);
        } finally {
          // ⚠️ close()를 빠뜨리면 데이터가 디스크에 도달하지 않는다.
          await writable.close();
        }
      }
      return { ok: true, folderName };
    } catch (err) {
      logger.warn("로컬 저장 폴더 쓰기 실패", {
        reason: err instanceof Error ? err.message : String(err),
      });
      return { ok: false, folderName: null };
    }
  }

  return { isSupported, pick, load, store, clear, query, request, writeFolder };
}

let singleton: DirHandleRepo | null = null;

export function getDirHandleRepo(): DirHandleRepo {
  singleton ??= createDirHandleRepo();
  return singleton;
}

export function setDirHandleRepoForTests(repo: DirHandleRepo | null): void {
  singleton = repo;
}
