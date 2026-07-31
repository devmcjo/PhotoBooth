/// <reference lib="webworker" />
/**
 * OPFS 쓰기 단일 경계(전용 Worker) — 05 §3.1
 *
 * ⚠️ **이 파일이 앱의 유일한 OPFS 쓰기 지점이다.** 메인 스레드에서 OPFS에 쓰려고 하면
 *    iOS/iPadOS Safari 17에서 전 저장 경로가 실패한다(`createSyncAccessHandle`은 Worker 전용,
 *    Safari 17에는 `createWritable`이 없다).
 *
 * ⚠️ `SyncAccessHandle`은 **파일당 배타 잠금**이다. 쓰기 후 반드시 `flush()` → `close()` 한다.
 *    닫지 않으면 같은 파일의 다음 쓰기가 `NoModificationAllowedError`로 실패한다.
 */
import {
  OPFS_USAGE_MAX_DEPTH,
  splitOpfsPath,
  splitParentAndName,
  type OpfsRequest,
  type OpfsResponse,
  type OpfsUsage,
  type OpfsUsageEntry,
  type OpfsWriteCapability,
} from "./opfsProtocol";

/** 쓰기 능력 판정 결과(첫 판정 후 캐시). */
let capability: OpfsWriteCapability | null = null;

async function getRoot(): Promise<FileSystemDirectoryHandle> {
  return navigator.storage.getDirectory();
}

/** 경로의 디렉터리 체인을 확보한다. `create=false`면 부재 시 예외. */
async function resolveDir(
  dirs: readonly string[],
  create: boolean,
): Promise<FileSystemDirectoryHandle> {
  let dir = await getRoot();
  for (const name of dirs) {
    dir = await dir.getDirectoryHandle(name, { create });
  }
  return dir;
}

/**
 * 쓰기 능력 판정. 실제로 임시 파일에 써 보고 결정한다 —
 * 기능 존재(`typeof`)만 보면 Safari의 부분 구현에서 오판한다(10 §6.2 "실제 성공 여부").
 */
async function probeCapability(): Promise<OpfsWriteCapability> {
  if (capability !== null) return capability;

  const probeName = ".mcphoto-probe";
  try {
    const root = await getRoot();
    const handle = await root.getFileHandle(probeName, { create: true });

    // 1순위: createSyncAccessHandle (Safari 17 포함 전 대상 브라우저).
    const withSync = handle as FileSystemFileHandle & {
      createSyncAccessHandle?: () => Promise<FileSystemSyncAccessHandle>;
    };
    if (typeof withSync.createSyncAccessHandle === "function") {
      const access = await withSync.createSyncAccessHandle();
      try {
        access.write(new Uint8Array([1]), { at: 0 });
        access.flush();
      } finally {
        access.close();
      }
      capability = "sync-access-handle";
    } else if (typeof handle.createWritable === "function") {
      // 2순위: Worker 안에서의 createWritable (Chromium·Firefox).
      const writable = await handle.createWritable();
      await writable.write(new Uint8Array([1]));
      await writable.close();
      capability = "writable-stream";
    } else {
      capability = "none";
    }

    await root.removeEntry(probeName).catch(() => undefined);
  } catch {
    capability = "none";
  }
  return capability;
}

async function writeFile(path: string, bytes: ArrayBuffer): Promise<void> {
  const { dirs, name } = splitParentAndName(path);
  const dir = await resolveDir(dirs, true);
  const handle = await dir.getFileHandle(name, { create: true });
  const mode = await probeCapability();

  if (mode === "sync-access-handle") {
    const withSync = handle as FileSystemFileHandle & {
      createSyncAccessHandle: () => Promise<FileSystemSyncAccessHandle>;
    };
    const access = await withSync.createSyncAccessHandle();
    try {
      // 덮어쓰기: 이전 내용이 남아 뒤에 붙는 것을 막는다(같은 이름 재기록 시 파일이 커진다).
      access.truncate(0);
      access.write(new Uint8Array(bytes), { at: 0 });
      access.flush();
    } finally {
      // 배타 잠금 해제 — 예외가 나도 반드시 닫는다.
      access.close();
    }
    return;
  }

  if (mode === "writable-stream") {
    const writable = await handle.createWritable();
    await writable.write(bytes);
    await writable.close();
    return;
  }

  throw new Error("이 브라우저에서는 OPFS에 쓸 수 없습니다.");
}

async function removeEntry(path: string, recursive: boolean): Promise<void> {
  const { dirs, name } = splitParentAndName(path);
  // 부모 디렉터리가 없으면 지울 것도 없다(잔재 정리에서 흔한 정상 경로).
  let dir: FileSystemDirectoryHandle;
  try {
    dir = await resolveDir(dirs, false);
  } catch {
    return;
  }
  await dir.removeEntry(name, { recursive }).catch((err: unknown) => {
    if (err instanceof DOMException && err.name === "NotFoundError") return;
    throw err;
  });
}

async function listDir(path: string): Promise<string[]> {
  let dir: FileSystemDirectoryHandle;
  try {
    dir = await resolveDir(path === "" ? [] : splitOpfsPath(path), false);
  } catch {
    return []; // 디렉터리 부재 = 빈 목록(정상)
  }
  const names: string[] = [];
  for await (const name of (dir as unknown as { keys(): AsyncIterable<string> }).keys()) {
    names.push(name);
  }
  return names;
}

async function exists(path: string): Promise<boolean> {
  const { dirs, name } = splitParentAndName(path);
  try {
    const dir = await resolveDir(dirs, false);
    try {
      await dir.getFileHandle(name);
      return true;
    } catch {
      await dir.getDirectoryHandle(name);
      return true;
    }
  } catch {
    return false;
  }
}

/** `entries()`는 TS DOM lib 선언에 없다(런타임에는 있다) — `keys()`와 같은 방식으로 좁힌다. */
type DirEntries = { entries(): AsyncIterable<[string, FileSystemHandle]> };

/**
 * 디렉터리 하위 전체 크기. 읽기 전용 walk다.
 *
 * ⚠️ **`createSyncAccessHandle().getSize()`를 쓰지 않는다.** 그것은 파일당 배타 잠금을 잡아
 *    같은 파일의 다음 쓰기를 `NoModificationAllowedError`로 실패시킨다. 크기는 `getFile().size`로 읽는다.
 */
async function directoryUsage(
  dir: FileSystemDirectoryHandle,
  depth: number,
): Promise<{ bytes: number; fileCount: number }> {
  if (depth > OPFS_USAGE_MAX_DEPTH) return { bytes: 0, fileCount: 0 };

  let bytes = 0;
  let fileCount = 0;
  for await (const [, entry] of (dir as unknown as DirEntries).entries()) {
    if (entry.kind === "file") {
      const file = await (entry as FileSystemFileHandle).getFile();
      bytes += file.size;
      fileCount++;
    } else {
      const sub = await directoryUsage(entry as FileSystemDirectoryHandle, depth + 1);
      bytes += sub.bytes;
      fileCount += sub.fileCount;
    }
  }
  return { bytes, fileCount };
}

/** 경로의 직속 자식별 용량. 디렉터리 부재는 오류가 아니라 **빈 결과**다(첫 실행에 `results/`가 없다). */
async function usage(path: string): Promise<OpfsUsage> {
  let dir: FileSystemDirectoryHandle;
  try {
    dir = await resolveDir(path === "" ? [] : splitOpfsPath(path), false);
  } catch {
    return { totalBytes: 0, entries: [] };
  }

  const entries: OpfsUsageEntry[] = [];
  let totalBytes = 0;
  for await (const [name, entry] of (dir as unknown as DirEntries).entries()) {
    if (entry.kind === "file") {
      const file = await (entry as FileSystemFileHandle).getFile();
      entries.push({ name, kind: "file", bytes: file.size, fileCount: 1 });
      totalBytes += file.size;
    } else {
      const sub = await directoryUsage(entry as FileSystemDirectoryHandle, 2);
      entries.push({ name, kind: "directory", bytes: sub.bytes, fileCount: sub.fileCount });
      totalBytes += sub.bytes;
    }
  }
  return { totalBytes, entries };
}

async function handle(request: OpfsRequest): Promise<unknown> {
  switch (request.op) {
    case "write":
      await writeFile(request.path, request.bytes);
      return undefined;
    case "remove":
      await removeEntry(request.path, request.recursive);
      return undefined;
    case "list":
      return listDir(request.path);
    case "exists":
      return exists(request.path);
    case "usage":
      return usage(request.path);
    case "probe":
      return probeCapability();
    default: {
      const never: never = request;
      throw new Error(`알 수 없는 OPFS 요청: ${JSON.stringify(never)}`);
    }
  }
}

self.addEventListener("message", (event: MessageEvent<OpfsRequest>) => {
  const request = event.data;
  void handle(request).then(
    (value) => {
      const response: OpfsResponse = { id: request.id, ok: true, value };
      self.postMessage(response);
    },
    (err: unknown) => {
      // 어댑터는 예외를 전파하지 않는다 — 문자열 사유만 넘기고 상위가 상태로 표현한다(01 §2.1).
      const response: OpfsResponse = {
        id: request.id,
        ok: false,
        error: err instanceof Error ? err.message : String(err),
      };
      self.postMessage(response);
    },
  );
});
