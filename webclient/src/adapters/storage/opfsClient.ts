import {
  OPFS_DIRS,
  type OpfsRequest,
  type OpfsRequestWithoutId,
  type OpfsResponse,
  type OpfsWriteCapability,
} from "./opfsProtocol";

/**
 * OPFS Worker RPC 클라이언트(메인 스레드) — 05 §3.1
 *
 * 쓰기·삭제·열거는 전부 Worker로 보낸다. **읽기(`getFile()`)는 메인 스레드에서도 되므로**
 * Worker를 거치지 않는다(§3.1 규칙).
 *
 * 어댑터 규약: 실패를 **예외로 전파하지 않고** `false`/`null`로 돌려준다(01 §2.1).
 * 상위(화면)가 그것을 상태·토스트로 표현한다(M4 성공 오인 금지).
 */

/** Worker와 주고받을 최소 표면. 테스트에서 가짜 Worker를 주입한다. */
export interface WorkerLike {
  postMessage(message: OpfsRequest, transfer?: Transferable[]): void;
  addEventListener(type: "message", listener: (event: MessageEvent<OpfsResponse>) => void): void;
  terminate?(): void;
}

export interface OpfsClient {
  /** 성공 여부를 돌려준다(예외 없음). */
  write(path: string, bytes: ArrayBuffer | Uint8Array | Blob): Promise<boolean>;
  remove(path: string, options?: { recursive?: boolean }): Promise<boolean>;
  list(path: string): Promise<string[]>;
  exists(path: string): Promise<boolean>;
  /** 쓰기 능력. `none`이면 OPFS 미지원으로 취급한다(촬영 전 경고 — 10 §6.2). */
  capability(): Promise<OpfsWriteCapability>;
  /** 메인 스레드 읽기(Worker 불요). 부재·실패는 `null`. */
  readFile(path: string): Promise<File | null>;
}

const REQUEST_TIMEOUT_MS = 15_000;

class WorkerOpfsClient implements OpfsClient {
  private nextId = 1;
  private readonly pending = new Map<number, (response: OpfsResponse) => void>();

  constructor(private readonly worker: WorkerLike) {
    this.worker.addEventListener("message", (event) => {
      const response = event.data;
      const resolve = this.pending.get(response.id);
      if (resolve) {
        this.pending.delete(response.id);
        resolve(response);
      }
    });
  }

  private send(request: OpfsRequestWithoutId, transfer?: Transferable[]): Promise<OpfsResponse> {
    const id = this.nextId++;
    const full = { ...request, id } as OpfsRequest;
    return new Promise<OpfsResponse>((resolve) => {
      // 타임아웃: Worker가 죽거나 응답이 사라져도 촬영 흐름이 영구 대기하지 않게 한다.
      const timer = setTimeout(() => {
        if (this.pending.delete(id)) {
          resolve({ id, ok: false, error: "OPFS 작업이 응답하지 않습니다(타임아웃)." });
        }
      }, REQUEST_TIMEOUT_MS);

      this.pending.set(id, (response) => {
        clearTimeout(timer);
        resolve(response);
      });

      try {
        this.worker.postMessage(full, transfer);
      } catch (err) {
        clearTimeout(timer);
        this.pending.delete(id);
        resolve({ id, ok: false, error: err instanceof Error ? err.message : String(err) });
      }
    });
  }

  async write(path: string, bytes: ArrayBuffer | Uint8Array | Blob): Promise<boolean> {
    let buffer: ArrayBuffer;
    if (bytes instanceof Blob) {
      buffer = await bytes.arrayBuffer();
    } else if (bytes instanceof Uint8Array) {
      // Worker로 전송할 독립 버퍼를 만든다(원본 뷰의 offset·공유 버퍼 문제를 피한다).
      buffer = bytes.slice().buffer as ArrayBuffer;
    } else {
      buffer = bytes;
    }
    // 버퍼 소유권을 넘겨 복사를 피한다(대용량 JPEG·mp4).
    const response = await this.send({ op: "write", path, bytes: buffer }, [buffer]);
    return response.ok;
  }

  async remove(path: string, options?: { recursive?: boolean }): Promise<boolean> {
    const response = await this.send({
      op: "remove",
      path,
      recursive: options?.recursive ?? false,
    });
    return response.ok;
  }

  async list(path: string): Promise<string[]> {
    const response = await this.send({ op: "list", path });
    return response.ok && Array.isArray(response.value) ? (response.value as string[]) : [];
  }

  async exists(path: string): Promise<boolean> {
    const response = await this.send({ op: "exists", path });
    return response.ok && response.value === true;
  }

  async capability(): Promise<OpfsWriteCapability> {
    const response = await this.send({ op: "probe" });
    return response.ok ? (response.value as OpfsWriteCapability) : "none";
  }

  async readFile(path: string): Promise<File | null> {
    try {
      const segments = path.split("/").filter((s) => s.length > 0);
      const name = segments.pop();
      if (name === undefined) return null;
      let dir = await navigator.storage.getDirectory();
      for (const segment of segments) {
        dir = await dir.getDirectoryHandle(segment);
      }
      const handle = await dir.getFileHandle(name);
      return await handle.getFile();
    } catch {
      return null;
    }
  }
}

/** OPFS 자체가 없는 브라우저용 무동작 클라이언트. 모든 쓰기가 `false`다(조용한 성공 금지). */
export const UNSUPPORTED_OPFS_CLIENT: OpfsClient = {
  write: async () => false,
  remove: async () => false,
  list: async () => [],
  exists: async () => false,
  capability: async () => "none",
  readFile: async () => null,
};

export function createOpfsClient(worker: WorkerLike): OpfsClient {
  return new WorkerOpfsClient(worker);
}

let singleton: OpfsClient | null = null;

/**
 * 앱 전역 OPFS 클라이언트. Worker는 **1개만** 만든다(파일당 배타 잠금이라 인스턴스가 늘면 충돌한다).
 * OPFS·Worker가 없는 환경에서는 무동작 클라이언트를 돌려준다.
 */
export function getOpfsClient(): OpfsClient {
  if (singleton) return singleton;

  const hasOpfs = typeof navigator !== "undefined" && typeof navigator.storage?.getDirectory === "function";
  if (!hasOpfs || typeof Worker === "undefined") {
    singleton = UNSUPPORTED_OPFS_CLIENT;
    return singleton;
  }

  const worker = new Worker(new URL("./opfsWriter.worker.ts", import.meta.url), {
    type: "module",
    name: "mcphoto-opfs-writer",
  });
  singleton = createOpfsClient(worker as unknown as WorkerLike);
  return singleton;
}

/** 테스트·재초기화용. */
export function setOpfsClientForTests(client: OpfsClient | null): void {
  singleton = client;
}

/**
 * 세션 작업 공간 잔재 일괄 삭제 — 규격(analysis/41 §4). **앱 시작 시 1회만** 호출한다.
 *
 * ⚠️ `sessions/`만 지운다. `results/`(결과물 보관)·`frames/`(프레임 캐시)·로그는 **건드리지 않는다**.
 * @returns 삭제된 세션 폴더 수(로그·진단 표시용)
 */
export async function purgeSessionLeftovers(client: OpfsClient): Promise<number> {
  const names = await client.list(OPFS_DIRS.sessions);
  let removed = 0;
  for (const name of names) {
    if (await client.remove(`${OPFS_DIRS.sessions}/${name}`, { recursive: true })) removed++;
  }
  return removed;
}
