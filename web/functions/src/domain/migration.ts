/**
 * it15 Google-only 계정 마이그레이션의 **순수 계획 로직**(설계 §8).
 *
 * Firestore·Storage I/O는 `scripts/migrate-google-only-accounts.mjs`가 담당하고,
 * "무엇을 바꿀지" 판정은 전부 이 모듈에 모아 jest로 단위 검증한다(네트워크·Admin SDK 무의존).
 * 스크립트는 컴파일 산출물 `lib/domain/migration.js`를 import한다.
 *
 * 재실행 안전성(멱등)의 핵심 규칙이 여기 있다:
 *   - 변경할 것이 없으면 `null`을 반환해 호출측이 write를 발행하지 않는다(§8.4 규칙 4).
 *   - 판정은 전부 문서 현재 상태만 보고 결정한다(외부 상태·시각 무의존).
 */

/** users 문서의 원시 형태(마이그레이션 전이라 레거시 필드가 섞여 있을 수 있다). */
export type RawUser = Record<string, unknown>;

/** CLI 인자 파싱 결과. */
export interface MigrationArgs {
  /** Firebase 프로젝트 ID(오조작 방지 위해 필수). */
  project: string;
  /** true면 실제 반영. false(기본)면 dry-run — 어떤 write도 하지 않는다. */
  apply: boolean;
  /** admin으로 승격할 Google 이메일(D3). */
  adminEmail: string;
  /** 최종 admin 문서 ID(D3). */
  adminId: string;
  /** 로그인 불가 계정 + 소유 프레임 삭제(D4). 파괴적이라 별도 옵트인. */
  deleteOrphans: boolean;
  /** 지정 시 해당 계정의 pinHash만 삭제하고 다른 단계는 실행하지 않는다(§5.6 admin PIN 복구). */
  clearPin: string | null;
  /** Storage 버킷명(orphan 프레임 이미지 삭제용). 미지정 시 env STORAGE_BUCKET. */
  bucket: string;
  /** 문서 단위 상세 로그. */
  verbose: boolean;
}

export type ParseResult<T> = { ok: true; value: T } | { ok: false; error: string };

/** §8.2 기본값. 사용자 원문에서 확정된 값이라 코드 기본값으로 둔다. */
export const DEFAULT_ADMIN_EMAIL = "devmcjo@gmail.com";
export const DEFAULT_ADMIN_ID = "devmcjo";

/** Firestore WriteBatch 상한은 500. 여유를 두고 400건씩 커밋한다(§8.4). */
export const BATCH_SIZE = 400;

const FLAGS_WITH_VALUE = new Set([
  "--project",
  "--admin-email",
  "--admin-id",
  "--clear-pin",
  "--bucket",
]);

/**
 * CLI 인자 파싱(순수). `--apply`가 없으면 dry-run이 기본이다 — 이 기본값은 절대 뒤집지 말 것.
 * 알 수 없는 인자는 오타로 간주하고 실패시킨다(오조작 방지: `--aply`가 조용히 dry-run으로 넘어가면 안 된다).
 */
export function parseArgs(argv: readonly string[]): ParseResult<MigrationArgs> {
  const out: Record<string, string> = {};
  let apply = false;
  let deleteOrphans = false;
  let verbose = false;

  for (let i = 0; i < argv.length; i++) {
    const a = argv[i];
    if (a === "--apply") {
      apply = true;
    } else if (a === "--delete-orphans") {
      deleteOrphans = true;
    } else if (a === "--verbose") {
      verbose = true;
    } else if (FLAGS_WITH_VALUE.has(a)) {
      const v = argv[i + 1];
      if (v === undefined || v.startsWith("--")) {
        return { ok: false, error: `${a} 에 값이 필요합니다.` };
      }
      out[a] = v;
      i++;
    } else {
      return { ok: false, error: `알 수 없는 인자입니다: ${a}` };
    }
  }

  const project = (out["--project"] ?? "").trim();
  if (project.length === 0) {
    return { ok: false, error: "--project <id> 는 필수입니다(오조작 방지)." };
  }

  const adminEmail = (out["--admin-email"] ?? DEFAULT_ADMIN_EMAIL).trim().toLowerCase();
  const adminId = (out["--admin-id"] ?? DEFAULT_ADMIN_ID).trim();
  if (adminId.length === 0) {
    return { ok: false, error: "--admin-id 가 비어 있습니다." };
  }

  const clearPinRaw = out["--clear-pin"];
  const clearPin = clearPinRaw === undefined ? null : clearPinRaw.trim();
  if (clearPin !== null && clearPin.length === 0) {
    return { ok: false, error: "--clear-pin 에 계정 id가 필요합니다." };
  }

  return {
    ok: true,
    value: {
      project,
      apply,
      adminEmail,
      adminId,
      deleteOrphans,
      clearPin,
      bucket: (out["--bucket"] ?? "").trim(),
      verbose,
    },
  };
}

/** email 필드 정규화(소문자·트림). 값이 문자열이 아니면 빈 문자열. */
export function normalizeEmail(value: unknown): string {
  return typeof value === "string" ? value.trim().toLowerCase() : "";
}

/**
 * 로그인 가능한 email을 가졌는가. email이 있으면 그 주소로 Google 로그인 시
 * `loginWithGoogleEmail`이 매핑하므로 계정을 살린다(§8.3 Step 5).
 */
export function hasLoginEmail(user: RawUser): boolean {
  return normalizeEmail(user.email).length > 0;
}

/**
 * 로그인 불가(orphan) 계정 판정 — email이 없거나 빈 문자열이면 Google 로그인 경로가 존재할 수 없다.
 * D4 삭제 대상(§8.3 Step 5). admin 관련 문서 제외는 호출측 책임.
 */
export function isOrphanAccount(user: RawUser): boolean {
  return !hasLoginEmail(user);
}

/** Step 4 필드 정리 계획. 바꿀 것이 없으면 `planFieldCleanup`이 null을 반환한다. */
export interface FieldCleanupPlan {
  /** FieldValue.delete() 대상 필드명. */
  deleteFields: string[];
  /** 설정할 authMethod 값. 변경 불요면 null. */
  setAuthMethod: string | null;
}

/**
 * Step 4: 전 계정 필드 정리 계획(§8.3).
 *   - `password`·`emailVerified` 키가 있으면 삭제.
 *   - authMethod가 "sso"/미설정/빈 값 → "google".
 *   - authMethod === "password" → 로그인 가능한 email이 있으면 "google", 없으면 **미변경**
 *     (Step 5 삭제 대상이므로 여기서 손대지 않는다).
 *   - 그 외 값("google" 포함, 미래의 "kakao" 등)은 미변경 — 알 수 없는 provider를 덮어쓰지 않는다.
 *
 * @returns 변경할 것이 없으면 null(멱등 — 재실행 시 write 0).
 */
export function planFieldCleanup(user: RawUser): FieldCleanupPlan | null {
  const deleteFields: string[] = [];
  if (Object.prototype.hasOwnProperty.call(user, "password")) deleteFields.push("password");
  if (Object.prototype.hasOwnProperty.call(user, "emailVerified")) {
    deleteFields.push("emailVerified");
  }

  let setAuthMethod: string | null = null;
  const am = user.authMethod;
  if (am === "sso" || am === undefined || am === null || am === "") {
    setAuthMethod = "google";
  } else if (am === "password" && hasLoginEmail(user)) {
    setAuthMethod = "google";
  }

  if (deleteFields.length === 0 && setAuthMethod === null) return null;
  return { deleteFields, setAuthMethod };
}

/**
 * Step 2: 신규 admin 문서 본문 조립(§8.3). `createdAt`은 원본 가입일을 그대로 승계하고,
 * `pinHash`·`qrUsedCount`는 있을 때만 옮긴다(부재 필드를 undefined로 만들지 않는다 — Firestore가 거부).
 *
 * @param source admin-email로 찾은 원본 계정 문서(예: `devmcjo-2`).
 * @param adminId 최종 admin 문서 ID(예: `devmcjo`).
 * @param createdAt 원본 createdAt 값(Timestamp — 이 모듈은 값을 해석하지 않고 그대로 전달만 한다).
 */
export function buildAdminDoc(
  source: RawUser,
  adminId: string,
  createdAt: unknown
): RawUser {
  const doc: RawUser = {
    id: adminId,
    role: "admin",
    createdAt,
    email: normalizeEmail(source.email),
    authMethod: "google",
  };
  if (typeof source.pinHash === "string" && source.pinHash.length > 0) {
    doc.pinHash = source.pinHash;
  }
  if (typeof source.qrUsedCount === "number") {
    doc.qrUsedCount = source.qrUsedCount;
  }
  return doc;
}

/**
 * 목표 admin 문서와 현재 문서가 실질적으로 같은가(멱등 판정).
 * Timestamp는 `isEqual`을 가진 객체일 수 있어 비교 훅을 주입받는다.
 *
 * @param sameCreatedAt 두 createdAt 값이 같은지 판정하는 콜백(Timestamp 비교는 호출측 몫).
 */
export function adminDocMatches(
  current: RawUser | null,
  target: RawUser,
  sameCreatedAt: (a: unknown, b: unknown) => boolean
): boolean {
  if (current === null) return false;
  const keys = new Set([...Object.keys(current), ...Object.keys(target)]);
  for (const k of keys) {
    if (k === "createdAt") {
      if (!sameCreatedAt(current.createdAt, target.createdAt)) return false;
      continue;
    }
    if (current[k] !== target[k]) return false;
  }
  return true;
}

/** 배열을 size 단위로 나눈다(Firestore WriteBatch 상한 대응, §8.4). size<=0이면 통째로 1묶음. */
export function chunk<T>(items: readonly T[], size: number): T[][] {
  if (size <= 0) return items.length > 0 ? [[...items]] : [];
  const out: T[][] = [];
  for (let i = 0; i < items.length; i += size) {
    out.push(items.slice(i, i + size));
  }
  return out;
}

/** Storage 삭제 대상 prefix(계정 소유 프레임 폴더). `frames/{userId}/` 규약(services/frames.ts). */
export function frameStoragePrefix(userId: string): string {
  return `frames/${userId}/`;
}
