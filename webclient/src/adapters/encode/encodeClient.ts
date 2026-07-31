import {
  ENCODE_WORKER_TIMEOUT_MS,
  type EncodeJob,
  type EncodeRequest,
  type EncodeResponse,
  type EncodeStats,
} from "./encodeProtocol";

/**
 * 인코딩 Worker RPC 클라이언트(메인 스레드) — 04 §7.3
 *
 * ⚠️ **작업마다 Worker를 새로 띄우고 `finally`에서 `terminate()`** 한다. 가공 Worker처럼
 *    상주시키면 하드웨어 인코더를 붙든 채 대기해 다음 촬영의 카메라·인코더와 경합한다.
 * ⚠️ 실패·타임아웃·미지원은 전부 `{ error }`다. 예외를 밖으로 던지지 않는다(01 §2.1).
 */

/** Worker와 주고받을 최소 표면. 테스트에서 가짜 Worker를 주입한다. */
export interface WorkerLike {
  postMessage(message: EncodeRequest): void;
  addEventListener(
    type: "message",
    listener: (event: MessageEvent<EncodeResponse>) => void,
  ): void;
  terminate(): void;
}

export type EncodeOutcome =
  | { readonly blob: Blob; readonly stats: EncodeStats }
  | { readonly error: string; readonly stats: EncodeStats | null };

export interface EncodeClient {
  /** 1회성 인코딩. 실패·타임아웃·미지원은 `{ error }`. */
  run(job: EncodeJob): Promise<EncodeOutcome>;
  /** 진행 중인 작업을 즉시 끊는다(화면 이탈). **멱등**. */
  abort(): void;
}

type Settled =
  | { readonly kind: "response"; readonly response: EncodeResponse }
  | { readonly kind: "timeout" }
  | { readonly kind: "aborted" };

function defaultSpawn(): WorkerLike {
  const worker = new Worker(new URL("./encode.worker.ts", import.meta.url), {
    type: "module",
    name: "mcphoto-timelapse-encoder",
  });
  return worker as unknown as WorkerLike;
}

export function createEncodeClient(spawn?: () => WorkerLike): EncodeClient {
  const spawnWorker = spawn ?? defaultSpawn;
  let nextId = 1;
  let activeWorker: WorkerLike | null = null;
  let settleActive: ((settled: Settled) => void) | null = null;

  function disposeActive(): void {
    const worker = activeWorker;
    activeWorker = null;
    settleActive = null;
    if (worker === null) return;
    try {
      worker.terminate();
    } catch {
      // 이미 죽은 Worker를 종료하면 던질 수 있다 — 무해하게 넘긴다.
    }
  }

  return {
    async run(job) {
      // 이중 방어: 경로 판정에서 이미 걸러지지만 Worker가 없으면 여기서도 끝낸다.
      if (spawn === undefined && typeof Worker === "undefined") {
        return { error: "이 브라우저는 Worker를 지원하지 않습니다", stats: null };
      }
      if (activeWorker !== null) {
        return { error: "인코딩이 이미 진행 중입니다", stats: null };
      }

      let worker: WorkerLike;
      try {
        worker = spawnWorker();
      } catch (err) {
        return {
          error: `인코딩 Worker를 시작할 수 없습니다: ${err instanceof Error ? err.message : String(err)}`,
          stats: null,
        };
      }

      activeWorker = worker;
      const id = nextId++;

      try {
        const settled = await new Promise<Settled>((resolve) => {
          const timer = setTimeout(() => resolve({ kind: "timeout" }), ENCODE_WORKER_TIMEOUT_MS);
          // `abort()`가 이 Promise를 깨울 수 있게 해소자를 보관한다.
          settleActive = (value) => {
            clearTimeout(timer);
            resolve(value);
          };
          worker.addEventListener("message", (event) => {
            const response = event.data;
            if (response === undefined || response.id !== id) return;
            clearTimeout(timer);
            resolve({ kind: "response", response });
          });
          try {
            worker.postMessage({ type: "encode", id, job });
          } catch (err) {
            clearTimeout(timer);
            resolve({
              kind: "response",
              response: {
                type: "failed",
                id,
                reason: err instanceof Error ? err.message : String(err),
                stats: { encodedFrames: 0, droppedFrames: 0, skippedFrames: 0, elapsedMs: 0 },
              },
            });
          }
        });

        if (settled.kind === "timeout") return { error: "인코딩 타임아웃", stats: null };
        if (settled.kind === "aborted") return { error: "중단됨", stats: null };

        const response = settled.response;
        if (response.type === "failed") {
          return { error: response.reason, stats: response.stats };
        }
        return {
          blob: new Blob([response.buffer], { type: "video/mp4" }),
          stats: response.stats,
        };
      } finally {
        disposeActive();
      }
    },

    abort() {
      settleActive?.({ kind: "aborted" });
      disposeActive();
    },
  };
}

let singleton: EncodeClient | null = null;

/** 앱 전역 인코딩 클라이언트. 결과 폐기(`stop()`)가 같은 인스턴스를 끊어야 하므로 싱글턴이다. */
export function getEncodeClient(): EncodeClient {
  singleton ??= createEncodeClient();
  return singleton;
}
