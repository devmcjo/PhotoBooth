import type { FrameTemplate, ImageSize, Slot } from "./types";

/**
 * 프레임 저장소 레코드 규약 — 05 §4.2~§4.4 · analysis/41 §3
 *
 * 저장 구조는 **IndexedDB 메타 + OPFS PNG**다. 이 파일은 그 사이의 **순수 변환·검증**만 담당한다
 * (실제 IO는 `adapters/storage/frameStore.ts`). Windows는 파일명 접두(`{계정}_{이름}`)로 공용/개인을
 * 구분하지만 웹은 **명시 필드(`scope`·`ownerId`)** 로 구분한다 — 05 §4.3이 허용한 대체 방식이다.
 *
 * ⚠️ 이름은 전부 **한정형**이다(`frameStoreKey`·`frameImagePath` …). `domain/index.ts`가 평면
 *    `export *` 배럴이라 `storeKey` 같은 일반명은 다른 모듈과 재수출 충돌을 만든다.
 */

/** 저장 스코프. 공용 = 전원 노출, 개인 = 소유자에게만 노출(05 §4.3). */
export const FRAME_SCOPES = ["public", "user"] as const;
export type FrameScope = (typeof FRAME_SCOPES)[number];

/**
 * OPFS 프레임 이미지 디렉터리.
 * ⚠️ `adapters/storage/opfsProtocol.ts`의 `OPFS_DIRS.frames`와 **같은 값**이어야 한다. 도메인은
 *    어댑터를 import할 수 없으므로 값을 복제하고, 두 값이 같음을 단위 테스트가 고정한다.
 */
export const FRAME_IMAGE_DIR = "frames";

/** 계정당 로컬 프레임 상한(05 §4.8). 게이트는 저장 경로를 만드는 Step 15가 건다. */
export const LOCAL_FRAME_LIMIT = 10;

/** `local:` id 접두(05 §4.4). 서버 문서 id와 로컬 저장분을 가르는 유일한 표식이다. */
export const LOCAL_ID_PREFIX = "local:";

/**
 * IndexedDB `mcphoto-frames` / store `frames`의 레코드 1건(05 §4.2).
 *
 * ⚠️ `name`은 **원문 그대로** 저장한다(정규화·트림 금지). 내보낸 `.slots`/PNG가 Windows `Frame\`에서
 *    그대로 인식되어야 하기 때문이다(WD4).
 */
export interface FrameRecord {
  /** `scope:owner:name` — 유일 키(keyPath). */
  readonly key: string;
  readonly scope: FrameScope;
  /** `scope="user"`일 때만 채운다. 공용은 null. */
  readonly ownerId: string | null;
  readonly name: string;
  /** 출처 판정 근거가 되는 프레임 id(05 §4.4). */
  readonly id: string;
  /** 서버 문서 id(공용 캐시·power 등록분만). 사본·개인 저장분은 null(연결이 끊긴다 — 의도). */
  readonly dbId: string | null;
  /** OPFS 상대 경로(`frames/{token}.png`). */
  readonly imageFile: string;
  readonly imageSize: ImageSize;
  readonly slots: readonly Slot[];
  /** ISO 8601. 도메인은 시각을 만들지 않으므로 호출자가 넣는다. */
  readonly createdAt: string;
  readonly updatedAt: string;
}

/**
 * 저장 키. 공용은 `public:{name}`, 개인은 `user:{owner}:{name}`이다.
 *
 * ⚠️ 이름에 `:`가 들어와도 키는 깨지지 않는다 — **이름은 항상 마지막 세그먼트이고 나머지를 전부
 *    포함한다**(앞 1~2개 세그먼트만 고정 의미를 갖는다). 이름을 키에서 되읽지 않고 `name` 필드를
 *    진실원으로 쓰는 이유이기도 하다. 키는 유일성만 보장하면 된다.
 */
export function frameStoreKey(scope: FrameScope, ownerId: string | null, name: string): string {
  return scope === "public" ? `public:${name}` : `user:${ownerId ?? ""}:${name}`;
}

/**
 * 프레임 id. 서버 문서 id가 있으면 **그것을 그대로** 쓰고(출처 = DbDefault), 없으면 `local:{key}`다
 * (출처 = UserLocal). 05 §4.4의 `dbId` 유무 규약과 1:1이다.
 */
export function frameIdFor(
  scope: FrameScope,
  ownerId: string | null,
  name: string,
  dbId: string | null,
): string {
  if (dbId !== null && dbId.length > 0) return dbId;
  return `${LOCAL_ID_PREFIX}${frameStoreKey(scope, ownerId, name)}`;
}

/**
 * 토큰 → OPFS 이미지 경로. **경로 조작 1차 방어**로 구분자·상대 참조·빈 토큰을 거부한다(`null`).
 * (2차 방어는 `splitOpfsPath`가 Worker 경계에서 한 번 더 한다.)
 */
export function frameImagePath(token: string): string | null {
  if (typeof token !== "string") return null;
  const trimmed = token.trim();
  if (trimmed.length === 0) return null;
  if (trimmed.includes("/") || trimmed.includes("\\")) return null;
  if (trimmed.includes("..") || trimmed === ".") return null;
  return `${FRAME_IMAGE_DIR}/${trimmed}.png`;
}

function isSlot(value: unknown): value is Slot {
  if (typeof value !== "object" || value === null) return false;
  const record = value as Record<string, unknown>;
  return (
    typeof record.index === "number" &&
    typeof record.x === "number" &&
    typeof record.y === "number" &&
    typeof record.width === "number" &&
    typeof record.height === "number"
  );
}

function isImageSize(value: unknown): value is ImageSize {
  if (typeof value !== "object" || value === null) return false;
  const record = value as Record<string, unknown>;
  return typeof record.width === "number" && typeof record.height === "number";
}

/**
 * 경계 검증 — IndexedDB에서 되읽은 값은 **다른 버전의 앱이 쓴 것일 수 있다**(구조화 복제라 타입이
 * 보장되지 않는다). 손상 레코드는 목록에서 건너뛰고, **예외를 던지지 않는다**(01 §2.1 어댑터 규약).
 */
export function isFrameRecord(value: unknown): value is FrameRecord {
  if (typeof value !== "object" || value === null) return false;
  const record = value as Record<string, unknown>;

  if (typeof record.key !== "string" || record.key.length === 0) return false;
  if (record.scope !== "public" && record.scope !== "user") return false;
  if (record.ownerId !== null && typeof record.ownerId !== "string") return false;
  if (typeof record.name !== "string" || record.name.length === 0) return false;
  if (typeof record.id !== "string" || record.id.length === 0) return false;
  if (record.dbId !== null && typeof record.dbId !== "string") return false;
  if (typeof record.imageFile !== "string" || record.imageFile.length === 0) return false;
  if (!isImageSize(record.imageSize)) return false;
  if (!Array.isArray(record.slots)) return false;
  if (!record.slots.every(isSlot)) return false;
  if (typeof record.createdAt !== "string") return false;
  if (typeof record.updatedAt !== "string") return false;
  return true;
}

/**
 * 레코드 → 화면이 쓰는 템플릿. `imageUrl`은 어댑터가 만든 object URL을 받는다
 * (도메인은 URL을 만들지 않는다 — 01 §8).
 *
 * `isDefault`는 **스코프에서 파생**한다: 공용 캐시는 게스트에게도 노출되는 기본 프레임이고,
 * 개인 저장분은 소유자에게만 보인다.
 */
export function recordToTemplate(record: FrameRecord, imageUrl: string): FrameTemplate {
  return {
    id: record.id,
    userId: record.scope === "user" ? record.ownerId : null,
    isDefault: record.scope === "public",
    name: record.name,
    imageUrl,
    imageSize: record.imageSize,
    slots: record.slots,
    createdAt: record.createdAt,
  };
}

/** `templateToRecord`가 템플릿에서 얻을 수 없는 저장 메타. */
export interface FrameRecordMeta {
  readonly scope: FrameScope;
  readonly ownerId: string | null;
  /** 서버 문서 id. 사본·개인 저장분은 null. */
  readonly dbId: string | null;
  /** OPFS 상대 경로(`frameImagePath`의 결과). */
  readonly imageFile: string;
  /** ISO 8601(어댑터가 시계에서 만든다). */
  readonly updatedAt: string;
  /** 템플릿의 `createdAt`이 비어 있을 때 쓸 값. */
  readonly createdAtFallback?: string;
}

/** 템플릿 + 저장 메타 → 레코드. `key`·`id`는 규약에서 **파생**한다(호출자가 지어내지 않는다). */
export function templateToRecord(frame: FrameTemplate, meta: FrameRecordMeta): FrameRecord {
  const key = frameStoreKey(meta.scope, meta.ownerId, frame.name);
  return {
    key,
    scope: meta.scope,
    ownerId: meta.scope === "user" ? meta.ownerId : null,
    name: frame.name,
    id: frameIdFor(meta.scope, meta.ownerId, frame.name, meta.dbId),
    dbId: meta.dbId,
    imageFile: meta.imageFile,
    imageSize: frame.imageSize,
    slots: frame.slots,
    createdAt:
      frame.createdAt.length > 0 ? frame.createdAt : (meta.createdAtFallback ?? meta.updatedAt),
    updatedAt: meta.updatedAt,
  };
}

/** 저장 시도 전 상한 판정. `count`는 **저장 전** 개인 프레임 개수다. */
export function exceedsLocalFrameLimit(count: number): boolean {
  return count >= LOCAL_FRAME_LIMIT;
}
