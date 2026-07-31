import {
  formatLogText,
  LOG_FLUSH_COUNT,
  LOG_FLUSH_INTERVAL_MS,
  LOG_MAX_ENTRIES,
  pruneEntries,
  sanitizeEntry,
  type LogEntry,
  type LogLevel,
} from "./logPolicy";

/**
 * 로그 스토어(IndexedDB 링버퍼) — WD6 · 05 §7
 *
 * `console.*`을 직접 부르지 않는 이유(01 §8): 콘솔은 운영 기기에서 열 수 없다.
 * 현장 운영자가 **진단 모달에서 조회하고 `.log`로 내보낼 수 있어야** 한다.
 */

/** 영속 백엔드. IndexedDB가 없는 환경(테스트·프라이빗 모드)은 메모리 싱크로 축소한다. */
export interface LogSink {
  persist(entries: readonly LogEntry[]): Promise<void>;
  /** 시간 오름차순 전량. */
  readAll(): Promise<LogEntry[]>;
  /** 오래된 항목 폐기. */
  prune(now: number): Promise<number>;
  clear(): Promise<void>;
}

export interface LogStore {
  log(level: LogLevel, msg: string, ctx?: Record<string, unknown>): void;
  /** 대기 중인 항목을 즉시 기록한다(`pagehide`에서 호출). */
  flush(): Promise<void>;
  /** 최근 N건(시간 **내림차순** — 진단 모달 표시용). */
  recent(limit: number): Promise<LogEntry[]>;
  exportText(): Promise<string>;
  clear(): Promise<void>;
  /** 진단 표시용 통계. */
  stats(): Promise<{ count: number; oldestTs: number | null; newestTs: number | null }>;
}

export interface LogStoreOptions {
  readonly sink: LogSink;
  /** 시각 주입(테스트 결정성). 기본 `Date.now`. */
  readonly now?: () => number;
  /** 개발 빌드에서 콘솔 미러링. 운영은 false(05 §7.1). */
  readonly mirrorToConsole?: boolean;
}

export function createLogStore(options: LogStoreOptions): LogStore {
  const now = options.now ?? (() => Date.now());
  const sink = options.sink;
  let pending: LogEntry[] = [];
  let timer: ReturnType<typeof setTimeout> | null = null;
  let flushing: Promise<void> = Promise.resolve();

  function scheduleFlush(): void {
    if (timer !== null) return;
    timer = setTimeout(() => {
      timer = null;
      void flush();
    }, LOG_FLUSH_INTERVAL_MS);
  }

  async function flush(): Promise<void> {
    if (pending.length === 0) return;
    const batch = pending;
    pending = [];
    if (timer !== null) {
      clearTimeout(timer);
      timer = null;
    }
    // 순차 직렬화: 동시 flush가 겹쳐 순서가 섞이지 않게 한다.
    flushing = flushing.then(async () => {
      try {
        await sink.persist(batch);
      } catch {
        // 로깅 실패가 앱을 죽이면 안 된다. 이 실패는 어디에도 기록할 수 없다(로그가 대상이므로).
      }
    });
    await flushing;
  }

  return {
    log(level, msg, ctx) {
      const entry = sanitizeEntry({ ts: now(), level, msg, ...(ctx === undefined ? {} : { ctx }) });
      pending.push(entry);

      if (options.mirrorToConsole === true) {
        // 개발 빌드 전용 미러링. 마스킹된 항목만 내보낸다.
        // eslint-disable-next-line no-console
        console[level === "info" ? "log" : level === "warn" ? "warn" : "error"](
          entry.msg,
          entry.ctx ?? "",
        );
      }

      if (pending.length >= LOG_FLUSH_COUNT) void flush();
      else scheduleFlush();
    },

    flush,

    async recent(limit) {
      await flush();
      const all = await sink.readAll();
      return all.slice(Math.max(0, all.length - limit)).reverse();
    },

    async exportText() {
      await flush();
      return formatLogText(await sink.readAll());
    },

    async clear() {
      pending = [];
      await sink.clear();
    },

    async stats() {
      await flush();
      const all = await sink.readAll();
      return {
        count: all.length,
        oldestTs: all.length > 0 ? all[0]!.ts : null,
        newestTs: all.length > 0 ? all[all.length - 1]!.ts : null,
      };
    },
  };
}

// ───────────────────────────── 메모리 싱크(폴백·테스트) ─────────────────────────────

export function createMemoryLogSink(maxEntries: number = LOG_MAX_ENTRIES): LogSink {
  let entries: LogEntry[] = [];
  return {
    async persist(batch) {
      entries.push(...batch);
      if (entries.length > maxEntries) entries = entries.slice(entries.length - maxEntries);
    },
    async readAll() {
      return [...entries];
    },
    async prune(nowMs) {
      const before = entries.length;
      entries = pruneEntries(entries, nowMs, { maxEntries });
      return before - entries.length;
    },
    async clear() {
      entries = [];
    },
  };
}

// ───────────────────────────── IndexedDB 싱크 ─────────────────────────────

export const LOG_DB_NAME = "mcphoto";
export const LOG_DB_VERSION = 1;
export const LOG_STORE_NAME = "logs";

function openDb(): Promise<IDBDatabase> {
  return new Promise((resolve, reject) => {
    const request = indexedDB.open(LOG_DB_NAME, LOG_DB_VERSION);
    request.onupgradeneeded = () => {
      const db = request.result;
      if (!db.objectStoreNames.contains(LOG_STORE_NAME)) {
        const store = db.createObjectStore(LOG_STORE_NAME, { autoIncrement: true });
        store.createIndex("by_ts", "ts");
      }
      // 프레임 메타 스토어는 Step 14(frameStore)가 같은 DB에 버전을 올려 추가한다.
    };
    request.onsuccess = () => resolve(request.result);
    request.onerror = () => reject(request.error ?? new Error("IndexedDB 열기 실패"));
  });
}

function txDone(tx: IDBTransaction): Promise<void> {
  return new Promise((resolve, reject) => {
    tx.oncomplete = () => resolve();
    tx.onerror = () => reject(tx.error ?? new Error("IndexedDB 트랜잭션 실패"));
    tx.onabort = () => reject(tx.error ?? new Error("IndexedDB 트랜잭션 중단"));
  });
}

/** IndexedDB 싱크. 열기 실패 시 `null`을 돌려주므로 호출측이 메모리 싱크로 축소한다. */
export async function createIndexedDbLogSink(): Promise<LogSink | null> {
  if (typeof indexedDB === "undefined") return null;

  let db: IDBDatabase;
  try {
    db = await openDb();
  } catch {
    return null;
  }

  async function readAll(): Promise<LogEntry[]> {
    const tx = db.transaction(LOG_STORE_NAME, "readonly");
    const index = tx.objectStore(LOG_STORE_NAME).index("by_ts");
    const request = index.getAll();
    await txDone(tx);
    return (request.result as LogEntry[]) ?? [];
  }

  return {
    async persist(entries) {
      const tx = db.transaction(LOG_STORE_NAME, "readwrite");
      const store = tx.objectStore(LOG_STORE_NAME);
      for (const entry of entries) store.add(entry);
      await txDone(tx);
    },

    readAll,

    async prune(nowMs) {
      const all = await readAll();
      const keep = pruneEntries(all, nowMs);
      if (keep.length === all.length) return 0;

      // 남길 수만큼 최신에서 세고, 그보다 오래된 키를 커서로 지운다.
      const dropCount = all.length - keep.length;
      const tx = db.transaction(LOG_STORE_NAME, "readwrite");
      const cursorRequest = tx.objectStore(LOG_STORE_NAME).index("by_ts").openCursor();
      let dropped = 0;
      cursorRequest.onsuccess = () => {
        const cursor = cursorRequest.result;
        if (!cursor || dropped >= dropCount) return;
        cursor.delete();
        dropped++;
        cursor.continue();
      };
      await txDone(tx);
      return dropped;
    },

    async clear() {
      const tx = db.transaction(LOG_STORE_NAME, "readwrite");
      tx.objectStore(LOG_STORE_NAME).clear();
      await txDone(tx);
    },
  };
}

// ───────────────────────────── 전역 logger 파사드 ─────────────────────────────

/**
 * 부트스트랩 2단계 이전에 발생한 로그를 잃지 않기 위한 버퍼.
 * `env.ts`의 경고(1단계)가 여기에 담겨 스토어가 붙은 뒤 흘러 들어간다.
 */
const earlyBuffer: { level: LogLevel; msg: string; ctx?: Record<string, unknown> }[] = [];
let activeStore: LogStore | null = null;

function emit(level: LogLevel, msg: string, ctx?: Record<string, unknown>): void {
  if (activeStore === null) {
    earlyBuffer.push({ level, msg, ...(ctx === undefined ? {} : { ctx }) });
    return;
  }
  activeStore.log(level, msg, ctx);
}

/** 앱 전역 로거. **`console.*` 대신 항상 이것을 쓴다**(01 §8). */
export const logger = {
  info: (msg: string, ctx?: Record<string, unknown>) => emit("info", msg, ctx),
  warn: (msg: string, ctx?: Record<string, unknown>) => emit("warn", msg, ctx),
  error: (msg: string, ctx?: Record<string, unknown>) => emit("error", msg, ctx),
  fatal: (msg: string, ctx?: Record<string, unknown>) => emit("fatal", msg, ctx),
};

/** 스토어를 연결하고 버퍼를 흘려보낸다(부트스트랩 2단계). */
export function attachLogStore(store: LogStore): void {
  activeStore = store;
  const buffered = earlyBuffer.splice(0, earlyBuffer.length);
  for (const item of buffered) store.log(item.level, item.msg, item.ctx);
}

export function getLogStore(): LogStore | null {
  return activeStore;
}

/** 테스트용 리셋. */
export function detachLogStore(): void {
  activeStore = null;
  earlyBuffer.length = 0;
}
