import {
  frameImagePath,
  frameStoreKey,
  isFrameRecord,
  recordToTemplate,
  templateToRecord,
  type FrameRecord,
  type FrameScope,
} from "@domain/frames/frameStorePolicy";
import type { FrameTemplate, ImageSize, Slot } from "@domain/frames/types";
import { frameImageUrl, revokeFrameImage } from "@adapters/frames/frameImageCache";
import { getOpfsClient, type OpfsClient } from "./opfsClient";
import { logger } from "./logStore";
import { OPFS_DIRS } from "./opfsProtocol";

/**
 * 로컬 프레임 저장소 — IndexedDB 메타 + OPFS PNG (05 §4)
 *
 * ⚠️ 이 파일은 저장소를 **직접** 만지지 않는다. 모든 OPFS 쓰기·삭제·열거가 `OpfsClient`(전용 Worker
 *    RPC)를 지나야 한다 — 메인 스레드에서 직접 쓰면 iOS/iPadOS Safari에서 전 저장 경로가 실패한다
 *    (VF-14). 정적 검사 FR-1이 이 파일에 `navigator.storage`·`createWritable`·
 *    `createSyncAccessHandle`·`getDirectory(`가 0건임을 고정한다.
 * ⚠️ 어댑터 규약: 예외를 전파하지 않는다. 실패는 `null`·`false`·빈 목록이다(M4 성공 오인 금지).
 */

// ───────────────────────────── IndexedDB — **별 DB**를 쓴다 ─────────────────────────────

/**
 * ⚠️ 로그 DB(`mcphoto` v1)·폴더 핸들 DB(`mcphoto-handles` v1)와 **다른 DB**다.
 *
 * `05 §4.2`는 DB `mcphoto`의 store `frames`를 적었지만 현실이 다르다: 로그 스토어가 그 연결을
 * **앱 수명 내내 붙들고 있고 `onversionchange` 핸들러가 없다**(`logStore.ts`). 여기서 v2로 올리면
 * 업그레이드가 **영구 blocked** 되어 프레임 로딩이 응답하지 않고, 상한 타이머가 30초 뒤 `Degraded`를
 * 띄운다 — 원인을 알 수 없는 만성 결함이 된다. Step 10의 `dirHandleRepo`가 같은 이유로 별 DB를 썼다.
 */
export const FRAME_DB_NAME = "mcphoto-frames";
export const FRAME_DB_VERSION = 1;
export const FRAME_STORE_NAME = "frames";
export const FRAME_INDEX_SCOPE = "by_scope";
export const FRAME_INDEX_OWNER = "by_owner";
export const FRAME_INDEX_NAME = "by_name";

function openFrameDb(): Promise<IDBDatabase> {
  return new Promise((resolve, reject) => {
    if (typeof indexedDB === "undefined") {
      reject(new Error("이 브라우저에는 IndexedDB가 없습니다."));
      return;
    }
    const request = indexedDB.open(FRAME_DB_NAME, FRAME_DB_VERSION);
    request.onupgradeneeded = () => {
      const db = request.result;
      if (!db.objectStoreNames.contains(FRAME_STORE_NAME)) {
        const store = db.createObjectStore(FRAME_STORE_NAME, { keyPath: "key" });
        store.createIndex(FRAME_INDEX_SCOPE, "scope");
        store.createIndex(FRAME_INDEX_OWNER, "ownerId");
        store.createIndex(FRAME_INDEX_NAME, "name");
      }
    };
    request.onsuccess = () => {
      const db = request.result;
      // 다른 탭이 버전을 올릴 때 이 연결이 blocked를 만들지 않게 한다(로그 DB가 빠진 함정).
      db.onversionchange = () => db.close();
      resolve(db);
    };
    request.onerror = () => reject(request.error ?? new Error("프레임 DB를 열 수 없습니다."));
  });
}

/** 트랜잭션 1회를 열고 반드시 닫는다. 연결을 붙들지 않아 다른 탭의 업그레이드를 막지 않는다. */
async function withFrameMetaStore<T>(
  mode: IDBTransactionMode,
  run: (store: IDBObjectStore) => IDBRequest,
): Promise<T> {
  const db = await openFrameDb();
  try {
    return await new Promise<T>((resolve, reject) => {
      const tx = db.transaction(FRAME_STORE_NAME, mode);
      const request = run(tx.objectStore(FRAME_STORE_NAME));
      tx.oncomplete = () => resolve(request.result as T);
      tx.onerror = () => reject(tx.error ?? new Error("프레임 메타 트랜잭션 실패"));
      tx.onabort = () => reject(tx.error ?? new Error("프레임 메타 트랜잭션 중단"));
    });
  } finally {
    db.close();
  }
}

/**
 * 메타 계층. node에 IndexedDB가 없으므로 인터페이스로 분리해 메모리 구현으로 검증한다
 * (`LogSink`/`createMemoryLogSink` 선례).
 */
export interface FrameMetaStore {
  /** 전량. 실패는 `[]`. */
  all(): Promise<FrameRecord[]>;
  put(record: FrameRecord): Promise<boolean>;
  delete(key: string): Promise<boolean>;
}

export function createIndexedDbFrameMeta(): FrameMetaStore {
  return {
    async all() {
      try {
        const raw = await withFrameMetaStore<unknown>("readonly", (store) => store.getAll());
        if (!Array.isArray(raw)) return [];
        // 다른 버전의 앱이 쓴 값일 수 있다 — 경계에서 검증하고 손상 레코드는 버린다.
        return raw.filter(isFrameRecord);
      } catch (err) {
        logger.warn("프레임 메타 조회 실패", {
          reason: err instanceof Error ? err.message : String(err),
        });
        return [];
      }
    },

    async put(record) {
      try {
        await withFrameMetaStore<unknown>("readwrite", (store) => store.put(record));
        return true;
      } catch (err) {
        logger.warn("프레임 메타 기록 실패", {
          key: record.key,
          reason: err instanceof Error ? err.message : String(err),
        });
        return false;
      }
    },

    async delete(key) {
      try {
        await withFrameMetaStore<unknown>("readwrite", (store) => store.delete(key));
        return true;
      } catch (err) {
        logger.warn("프레임 메타 삭제 실패", {
          key,
          reason: err instanceof Error ? err.message : String(err),
        });
        return false;
      }
    },
  };
}

/**
 * 메모리 메타(테스트·IndexedDB 부재 폴백). 프레임이 **세션 동안만** 유지되는 축소 동작이며
 * 앱이 죽지는 않는다.
 */
export function createMemoryFrameMeta(seed: readonly FrameRecord[] = []): FrameMetaStore {
  const records = new Map<string, FrameRecord>();
  for (const record of seed) records.set(record.key, record);
  return {
    async all() {
      return [...records.values()];
    },
    async put(record) {
      records.set(record.key, record);
      return true;
    },
    async delete(key) {
      records.delete(key);
      return true;
    },
  };
}

// ───────────────────────────── 저장소 본체 ─────────────────────────────

/** `saveLocal` 입력(Step 15의 저장 경로가 쓴다 — 지금은 구현만 두고 호출자가 없다). */
export interface SaveFrameInput {
  readonly scope: FrameScope;
  /** `scope="user"`일 때 소유자. 공용은 null. */
  readonly ownerId: string | null;
  readonly name: string;
  /** 서버 문서 id(power 등록분). 사본·개인 저장분은 null. */
  readonly dbId: string | null;
  readonly imageSize: ImageSize;
  readonly slots: readonly Slot[];
  readonly bytes: Blob;
}

export interface FrameStore {
  /** 공용 캐시(번들 제외). 이미지 파일이 실제로 없는 레코드는 **건너뛴다**(반쪽 프레임 미노출). */
  listPublic(): Promise<FrameTemplate[]>;
  listPersonal(userId: string): Promise<FrameTemplate[]>;
  /**
   * 저장 스코프의 기존 이름들(저장 전 검증 ⑦ · fork 이름 제안). **메타만 읽는다**
   * (OPFS 존재 확인·object URL 생성 없음).
   *
   * ⚠️ `listPublic()`로 대신하지 마라: ① 목록 조회는 이미지가 없는 레코드를 **건너뛰지만**
   *    저장 키는 그 레코드가 여전히 점유하고 있어 덮어쓰기가 일어난다 — 가드가 뚫린다.
   *    ② 이름 하나 보려고 프레임 전체의 object URL을 만들 이유가 없다.
   * 실패는 **빈 배열**이다(⑦이 조용히 꺼진다 — 03 §11.3 규격).
   */
  scopeFrameNames(scope: FrameScope, ownerId: string | null): Promise<readonly string[]>;
  /** 서버 프레임을 캐시한다. **OPFS 쓰기 성공 후에만** 메타를 기록한다. 실패는 `null`. */
  cacheServerFrame(frame: FrameTemplate, bytes: Blob): Promise<FrameTemplate | null>;
  /** Step 15가 쓰는 저장 경로. */
  saveLocal(input: SaveFrameInput): Promise<FrameTemplate | null>;
  /** 05 §4.7. 성공 판정은 **실제 부재 확인**이다. */
  deleteLocal(frame: FrameTemplate): Promise<boolean>;
  /**
   * 목록의 템플릿 → 저장된 PNG 바이트(Step 16 프레임 내보내기). 없거나 실패면 `null`.
   *
   * ⚠️ **`fetch(frame.imageUrl)`로 blob URL을 읽지 마라.** ① kiosk CSP `connect-src`가 `blob:`을
   *    덮는지 브라우저별로 갈린다 ② 이미 디스크에 있는 바이트를 메모리로 한 번 더 왕복시킨다.
   *    여기는 레코드의 `imageFile`을 알고 있고 `opfs.readFile`은 메인 스레드 읽기가 허용된다(05 §3.1).
   */
  readImageBytes(frame: FrameTemplate): Promise<Blob | null>;
  /** 개인 프레임 개수(10개 상한 판정 — Step 15가 쓴다). */
  countPersonal(userId: string): Promise<number>;
  /** `frames/` OPFS 사용량(Step 16 진단). 실패는 0. */
  usageBytes(): Promise<number>;
}

export interface FrameStoreDeps {
  readonly meta: FrameMetaStore;
  readonly opfs: OpfsClient;
  /** 이미지 파일명 토큰 생성기(어댑터가 난수를 만든다 — 01 §8). */
  readonly newToken: () => string;
  readonly now: () => Date;
  /**
   * OPFS 파일 → object URL. 기본은 `frameImageCache`(소유·재사용·해제).
   * 주입 가능한 이유: node 테스트에 `File`·`URL.createObjectURL` 왕복을 강요하지 않기 위함이다.
   */
  readonly imageUrl?: (path: string, source: Blob) => string;
  /** 삭제 성공 시 URL 해제. 기본은 `frameImageCache.revokeFrameImage`. */
  readonly releaseImage?: (path: string) => void;
}

function describe(err: unknown): string {
  return err instanceof Error ? err.message : String(err);
}

export function createFrameStore(deps: FrameStoreDeps): FrameStore {
  const toUrl = deps.imageUrl ?? frameImageUrl;
  const release = deps.releaseImage ?? revokeFrameImage;

  /** 레코드 → 템플릿. 이미지 파일이 없거나 읽히지 않으면 `null`(반쪽 프레임 미노출). */
  async function resolve(record: FrameRecord): Promise<FrameTemplate | null> {
    const exists = await deps.opfs.exists(record.imageFile);
    if (!exists) {
      logger.warn("프레임 이미지 파일이 없어 목록에서 제외", {
        key: record.key,
        imageFile: record.imageFile,
      });
      return null;
    }
    const file = await deps.opfs.readFile(record.imageFile);
    if (file === null) return null;
    const url = toUrl(record.imageFile, file);
    if (url.length === 0) return null;
    return recordToTemplate(record, url);
  }

  async function listByFilter(
    keep: (record: FrameRecord) => boolean,
  ): Promise<FrameTemplate[]> {
    try {
      const records = await deps.meta.all();
      const frames: FrameTemplate[] = [];
      for (const record of records) {
        if (!keep(record)) continue;
        const template = await resolve(record);
        if (template !== null) frames.push(template);
      }
      return frames;
    } catch (err) {
      logger.warn("프레임 목록 조회 실패", { reason: describe(err) });
      return [];
    }
  }

  /**
   * 바이트를 OPFS에 쓰고 메타를 기록한다. **쓰기 순서가 규격이다** — 이미지가 먼저다.
   * 반대로 하면 이미지 없는 레코드가 목록에 올라간다(Windows의 "png 먼저, `.slots` 나중"과 같은 성질).
   *
   * ⚠️ 같은 키를 **덮어쓸 때** 이전 PNG를 지운다(설계 이탈 ⑥). 지우지 않으면 편집 저장마다 고아
   *    파일이 쌓여 "프레임 1개 = 메타 1 + PNG 1"(05 §4) 불변식이 깨진다. **정리는 새 레코드를
   *    기록한 뒤**다 — 반대로 하면 메타 기록 실패 시 이미지 없는 프레임이 된다.
   */
  async function persist(
    record: (imageFile: string) => FrameRecord,
    bytes: Blob,
  ): Promise<FrameTemplate | null> {
    const imageFile = frameImagePath(deps.newToken());
    if (imageFile === null) {
      logger.warn("프레임 이미지 경로를 만들 수 없습니다(토큰 규약 위반)");
      return null;
    }

    const written = await deps.opfs.write(imageFile, bytes);
    if (!written) {
      logger.warn("프레임 이미지 쓰기 실패 — 메타를 기록하지 않는다", { imageFile });
      return null;
    }

    const target = record(imageFile);
    // 덮어쓰기 대상(같은 키)의 이전 이미지 경로. 메타 기록 **전에** 읽어야 값이 남아 있다.
    const previousImageFile = await findPreviousImageFile(target.key);

    const stored = await deps.meta.put(target);
    if (!stored) {
      // 고아 파일을 남기지 않는다(메타가 없으면 영원히 참조되지 않는 바이트가 된다).
      await deps.opfs.remove(imageFile);
      return null;
    }

    if (previousImageFile !== null && previousImageFile !== imageFile) {
      // 실패는 경고만 — 고아 1개가 저장 실패보다 낫다.
      try {
        await deps.opfs.remove(previousImageFile);
        release(previousImageFile); // 옛 경로의 object URL을 놓아준다.
      } catch (err) {
        logger.warn("이전 프레임 이미지 정리 실패(고아 파일이 남을 수 있음)", {
          key: target.key,
          imageFile: previousImageFile,
          reason: describe(err),
        });
      }
    }

    // 방금 쓴 파일을 되읽어 **디스크 백업 File**로 URL을 만든다(메모리에 바이트를 붙들지 않는다 — A-2).
    const file = await deps.opfs.readFile(imageFile);
    const url = toUrl(imageFile, file ?? bytes);
    if (url.length === 0) return null;
    return recordToTemplate(target, url);
  }

  /** 같은 키의 기존 레코드가 쓰던 이미지 경로. 없거나 조회 실패면 `null`(정리를 건너뛴다). */
  async function findPreviousImageFile(key: string): Promise<string | null> {
    try {
      const records = await deps.meta.all();
      return records.find((r) => r.key === key)?.imageFile ?? null;
    } catch (err) {
      logger.warn("이전 프레임 레코드 조회 실패(정리 생략)", { key, reason: describe(err) });
      return null;
    }
  }

  async function listPublic(): Promise<FrameTemplate[]> {
    return listByFilter((record) => record.scope === "public");
  }

  async function listPersonal(userId: string): Promise<FrameTemplate[]> {
    if (userId.length === 0) return [];
    // 타인 소유·공용은 제외한다(개인 프레임은 소유자에게만 보인다 — 05 §4.3).
    return listByFilter((record) => record.scope === "user" && record.ownerId === userId);
  }

  async function scopeFrameNames(
    scope: FrameScope,
    ownerId: string | null,
  ): Promise<readonly string[]> {
    try {
      const records = await deps.meta.all();
      return records
        .filter((r) => r.scope === scope && (scope !== "user" || r.ownerId === ownerId))
        .map((r) => r.name);
    } catch (err) {
      // ⚠️ 실패는 빈 배열이다 — ⑦ 가드가 조용히 꺼지고 ④가 2중 방어로 남는다(03 §11.3).
      logger.warn("스코프 프레임 이름 조회 실패(⑦ 가드 비활성)", {
        scope,
        reason: describe(err),
      });
      return [];
    }
  }

  async function cacheServerFrame(
    frame: FrameTemplate,
    bytes: Blob,
  ): Promise<FrameTemplate | null> {
    try {
      const updatedAt = deps.now().toISOString();
      return await persist(
        (imageFile) =>
          templateToRecord(frame, {
            scope: "public",
            ownerId: null,
            // 서버에서 온 프레임의 id가 곧 서버 문서 id다(05 §4.4 — `#dbid` 규약).
            dbId: frame.id.length > 0 ? frame.id : null,
            imageFile,
            updatedAt,
          }),
        bytes,
      );
    } catch (err) {
      logger.warn("서버 프레임 캐시 실패", { name: frame.name, reason: describe(err) });
      return null;
    }
  }

  async function saveLocal(input: SaveFrameInput): Promise<FrameTemplate | null> {
    try {
      const updatedAt = deps.now().toISOString();
      const template: FrameTemplate = {
        id: "",
        userId: input.scope === "user" ? input.ownerId : null,
        isDefault: input.scope === "public",
        name: input.name,
        imageUrl: "",
        imageSize: input.imageSize,
        slots: input.slots,
        createdAt: updatedAt,
      };
      return await persist(
        (imageFile) =>
          templateToRecord(template, {
            scope: input.scope,
            ownerId: input.ownerId,
            dbId: input.dbId,
            imageFile,
            updatedAt,
          }),
        input.bytes,
      );
    } catch (err) {
      logger.warn("로컬 프레임 저장 실패", { name: input.name, reason: describe(err) });
      return null;
    }
  }

  /** 목록의 템플릿 → 저장 레코드. id가 진실원이고, 없으면 스코프+이름 키로 한 번 더 찾는다. */
  async function findRecord(frame: FrameTemplate): Promise<FrameRecord | null> {
    const records = await deps.meta.all();
    const byId = records.find((record) => record.id === frame.id);
    if (byId !== undefined) return byId;
    const scope: FrameScope = frame.userId === null ? "public" : "user";
    const key = frameStoreKey(scope, frame.userId, frame.name);
    return records.find((record) => record.key === key) ?? null;
  }

  async function deleteLocal(frame: FrameTemplate): Promise<boolean> {
    try {
      const record = await findRecord(frame);
      if (record === null) {
        // 번들·fallback이거나 이미 사라진 레코드다. 지울 로컬 사본이 없으므로 성공이 아니다.
        logger.warn("삭제할 로컬 프레임 레코드를 찾지 못했습니다", {
          id: frame.id,
          name: frame.name,
        });
        return false;
      }

      // ① 이미지가 이미 없으면 메타만 지우고 **성공**으로 본다(설계 이탈 ④).
      //    실패로 보고하면 카드가 영원히 지워지지 않는다. 고아 레코드도 함께 없앤다.
      if (!(await deps.opfs.exists(record.imageFile))) {
        await deps.meta.delete(record.key);
        release(record.imageFile);
        logger.warn("프레임 이미지가 이미 없어 메타만 삭제", {
          key: record.key,
          imageFile: record.imageFile,
        });
        return true;
      }

      // ②③ 메타 → 파일 순서. ④ 성공 판정은 **실제 부재 확인**이다(M4 — 예외를 삼키고 성공 보고 금지).
      await deps.meta.delete(record.key);
      await deps.opfs.remove(record.imageFile);
      const stillThere = await deps.opfs.exists(record.imageFile);
      if (stillThere) {
        logger.warn("프레임 이미지 삭제 실패(파일이 남아 있음)", {
          key: record.key,
          imageFile: record.imageFile,
        });
        // URL은 해제하지 않는다 — 재스캔으로 카드가 돌아왔을 때 썸네일이 깨지지 않게.
        return false;
      }
      release(record.imageFile);
      return true;
    } catch (err) {
      logger.warn("프레임 삭제 실패", { id: frame.id, reason: describe(err) });
      return false;
    }
  }

  async function readImageBytes(frame: FrameTemplate): Promise<Blob | null> {
    try {
      const record = await findRecord(frame);
      if (record === null) {
        // 번들·fallback 프레임이다(저장소에 없다). 내보내기 대상이 아니다.
        return null;
      }
      return await deps.opfs.readFile(record.imageFile);
    } catch (err) {
      logger.warn("프레임 이미지 읽기 실패", { id: frame.id, reason: describe(err) });
      return null;
    }
  }

  async function countPersonal(userId: string): Promise<number> {
    if (userId.length === 0) return 0;
    try {
      const records = await deps.meta.all();
      return records.filter((r) => r.scope === "user" && r.ownerId === userId).length;
    } catch {
      return 0;
    }
  }

  async function usageBytes(): Promise<number> {
    try {
      const usage = await deps.opfs.usage(OPFS_DIRS.frames);
      return usage.totalBytes;
    } catch {
      return 0;
    }
  }

  return {
    listPublic,
    listPersonal,
    scopeFrameNames,
    cacheServerFrame,
    saveLocal,
    deleteLocal,
    readImageBytes,
    countPersonal,
    usageBytes,
  };
}

let singleton: FrameStore | null = null;

/** 앱 전역 프레임 저장소. **첫 호출에서만** DB가 열린다(앱 부팅만으로 생성되지 않는다). */
export function getFrameStore(): FrameStore {
  if (singleton !== null) return singleton;

  const hasIndexedDb = typeof indexedDB !== "undefined";
  if (!hasIndexedDb) {
    logger.warn("IndexedDB가 없어 프레임 메타를 메모리로 축소합니다(세션 동안만 유지)");
  }
  singleton = createFrameStore({
    meta: hasIndexedDb ? createIndexedDbFrameMeta() : createMemoryFrameMeta(),
    opfs: getOpfsClient(),
    newToken: newImageToken,
    now: () => new Date(),
  });
  return singleton;
}

export function setFrameStoreForTests(store: FrameStore | null): void {
  singleton = null;
  if (store !== null) singleton = store;
}

/** 이미지 파일명 토큰. 도메인은 난수를 만들지 않으므로 어댑터 경계에서 만든다(01 §8). */
function newImageToken(): string {
  const source = globalThis.crypto;
  if (typeof source?.randomUUID === "function") return source.randomUUID().replace(/-/g, "");
  // 극단 폴백 — 경로 방어(`frameImagePath`)를 통과하는 문자만 쓴다.
  return `${Date.now().toString(16)}${Math.floor(Math.random() * 0xffffff).toString(16)}`;
}
