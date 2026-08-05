import {
  exceedsLocalFrameLimit,
  LOCAL_FRAME_LIMIT,
} from "@domain/frames/frameStorePolicy";
import { nextCopyName, validateFrameName } from "@domain/frames/frameNaming";
import { rescaleSlots } from "@domain/frames/slotLayout";
import { parseSlotsFile, serializeSlotsFile } from "@domain/frames/slotsFile";
import type { FrameTemplate, ImageSize, Slot } from "@domain/frames/types";
import { canWriteFrames, type UserRole } from "@domain/roles/userRole";
import { exportBlob } from "@adapters/platform/fileExport";
import { getFrameStore } from "./frameStore";
import { getLogStore, logger } from "./logStore";
import { buildStoreZip, parseZipEntries, type ZipInputEntry } from "./zipStore";

/**
 * 로그 · 프레임 내보내기 / 가져오기 — 05 §2.5·§4.6·§7
 *
 * 설정 내보내기(`screens/settings/settingsTransfer.ts`)와 **같은 형태**다:
 * 파싱 → 미리보기 → [적용]. 즉시 덮어쓰지 않는다.
 *
 * ⚠️ 어댑터 규약: 예외를 전파하지 않는다. 실패는 `false`·판별 유니온이다(M4 성공 오인 금지).
 * ⚠️ 문구를 담지 않는다 — 실패·경고는 **키**로 돌려주고 화면(`screens/settings/frameTransfer.ts`)이
 *    `STRINGS`로 옮긴다(어댑터는 문구 카탈로그를 import하지 않는 것이 이 저장소 관례다).
 * ⚠️ **`fetch(`를 쓰지 않는다.** 프레임 이미지는 OPFS에서 직접 읽는다(설계 §9.2 · A1).
 */

function pad2(value: number): string {
  return String(value).padStart(2, "0");
}

/** `{YYMMDD_HHMM}` — 로컬 시각 성분(운영자가 시각으로 찾는다 · `settingsExportFileName`과 동형). */
function stamp(localTime: Date): string {
  return (
    pad2(localTime.getFullYear() % 100) +
    pad2(localTime.getMonth() + 1) +
    pad2(localTime.getDate()) +
    "_" +
    pad2(localTime.getHours()) +
    pad2(localTime.getMinutes())
  );
}

// ───────────────────────────── 로그 내보내기 ─────────────────────────────

export function logExportFileName(localTime: Date): string {
  return `mcphoto-log-${stamp(localTime)}.log`;
}

export interface LogExportDeps {
  readonly exportText: () => Promise<string>;
  readonly write: (blob: Blob, fileName: string) => boolean;
  readonly now: () => Date;
}

/**
 * `.log` 내보내기. **실패해도 크래시 없이 `false`** 다(진단 모달이 닫히지 않는다).
 * `exportText()`가 던져도 여기서 접는다(`exportBlob`은 원래 던지지 않는다).
 */
export async function exportLogs(deps: LogExportDeps): Promise<boolean> {
  let text: string;
  try {
    text = await deps.exportText();
  } catch (err) {
    logger.warn("로그 내보내기 실패", {
      reason: err instanceof Error ? err.message : String(err),
    });
    return false;
  }

  const blob = new Blob([text], { type: "text/plain;charset=utf-8" });
  const ok = deps.write(blob, logExportFileName(deps.now()));
  if (!ok) logger.warn("로그 내보내기 실패");
  return ok;
}

export function defaultLogExportDeps(overrides: Partial<LogExportDeps> = {}): LogExportDeps {
  return {
    exportText: async () => (await getLogStore()?.exportText()) ?? "",
    write: (blob, fileName) => exportBlob(blob, fileName),
    now: () => new Date(),
    ...overrides,
  };
}

// ───────────────────────────── 프레임 내보내기 ─────────────────────────────

export function frameZipFileName(localTime: Date): string {
  return `mcphoto-frames-${stamp(localTime)}.zip`;
}

/**
 * zip 안의 base 이름 — 공용 `{이름}` / 개인 `{계정}_{이름}`.
 * Windows `Frame\` 폴더 규약과 1:1이다(05 §4.3·§4.6).
 */
export function frameEntryBaseName(frame: FrameTemplate): string {
  return frame.userId === null || frame.userId.length === 0
    ? frame.name
    : `${frame.userId}_${frame.name}`;
}

/** 같은 base가 겹치면 `-2`, `-3`… 을 붙인다. zip 안에 중복 경로를 만들지 않는다. */
export function dedupeEntryNames(baseNames: readonly string[]): string[] {
  const taken = new Set<string>();
  return baseNames.map((base) => {
    if (!taken.has(base)) {
      taken.add(base);
      return base;
    }
    let index = 2;
    let candidate = `${base}-${index}`;
    while (taken.has(candidate)) {
      index++;
      candidate = `${base}-${index}`;
    }
    taken.add(candidate);
    return candidate;
  });
}

export interface FrameExportOutcome {
  readonly ok: boolean;
  readonly exported: number;
  /** 이미지 바이트를 못 읽어 건너뛴 프레임 수. **성공으로 집계하지 않는다**(M4). */
  readonly skipped: number;
}

export interface FrameExportDeps {
  /** 게스트는 `null`. 개인 프레임을 모으지 않는다. */
  readonly userId: string | null;
  readonly listPublic: () => Promise<FrameTemplate[]>;
  readonly listPersonal: (userId: string) => Promise<FrameTemplate[]>;
  readonly readImageBytes: (frame: FrameTemplate) => Promise<Blob | null>;
  readonly write: (blob: Blob, fileName: string) => boolean;
  readonly now: () => Date;
}

/**
 * 저장소의 공용·개인 프레임을 store-zip으로 내보낸다.
 * **번들·fallback 프레임은 제외**된다(저장소에 없어 `readImageBytes`가 `null`이다).
 */
export async function exportFrames(deps: FrameExportDeps): Promise<FrameExportOutcome> {
  let frames: FrameTemplate[];
  try {
    const publicFrames = await deps.listPublic();
    const personal =
      deps.userId === null || deps.userId.length === 0
        ? []
        : await deps.listPersonal(deps.userId);
    frames = [...publicFrames, ...personal];
  } catch (err) {
    logger.warn("프레임 내보내기: 목록 조회 실패", {
      reason: err instanceof Error ? err.message : String(err),
    });
    return { ok: false, exported: 0, skipped: 0 };
  }

  if (frames.length === 0) return { ok: false, exported: 0, skipped: 0 };

  const baseNames = dedupeEntryNames(frames.map(frameEntryBaseName));
  const entries: ZipInputEntry[] = [];
  let skipped = 0;

  for (let index = 0; index < frames.length; index++) {
    const frame = frames[index]!;
    const base = baseNames[index]!;
    let bytes: Blob | null = null;
    try {
      bytes = await deps.readImageBytes(frame);
    } catch {
      bytes = null;
    }
    if (bytes === null) {
      skipped++;
      continue;
    }

    const png = new Uint8Array(await bytes.arrayBuffer());
    // `.slots` 본문은 도메인 직렬화가 정본이다(`#imagesize` · `\n` · UTF-8 — Windows 상호 이동).
    // `#dbid`는 쓰지 않는다(사본은 서버 문서와 연결을 끊는다 — 05 §4.4).
    const slotsText = serializeSlotsFile({
      imageSize: frame.imageSize,
      slots: frame.slots,
      dbId: null,
    });
    entries.push({ path: `${base}.png`, bytes: png });
    entries.push({ path: `${base}.slots`, bytes: new TextEncoder().encode(slotsText) });
  }

  if (entries.length === 0) return { ok: false, exported: 0, skipped };

  const zip = buildStoreZip(entries);
  const blob = new Blob([zip], { type: "application/zip" });
  const ok = deps.write(blob, frameZipFileName(deps.now()));
  const exported = entries.length / 2;
  if (!ok) logger.warn("프레임 내보내기 실패(파일 쓰기)");
  else logger.info("프레임 내보내기", { exported, skipped });
  return { ok, exported: ok ? exported : 0, skipped };
}

export function defaultFrameExportDeps(
  userId: string | null,
  overrides: Partial<FrameExportDeps> = {},
): FrameExportDeps {
  return {
    userId,
    listPublic: () => getFrameStore().listPublic(),
    listPersonal: (id) => getFrameStore().listPersonal(id),
    readImageBytes: (frame) => getFrameStore().readImageBytes(frame),
    write: (blob, fileName) => exportBlob(blob, fileName),
    now: () => new Date(),
    ...overrides,
  };
}

// ───────────────────────────── 프레임 가져오기 ─────────────────────────────

export type FrameImportRejection =
  | "not-logged-in"
  | "no-write-permission"
  | "malformed-zip"
  | "no-entries"
  | "limit-reached"
  | "compression-unsupported";

/**
 * 미리보기 경고. **문구가 아니라 키**다 — 화면이 `STRINGS`로 옮긴다.
 * (어댑터는 문구 카탈로그를 import하지 않는 것이 이 저장소 관례다.)
 */
export type FrameImportWarning =
  | { readonly kind: "invalid-name"; readonly sourceName: string }
  | { readonly kind: "missing-slots"; readonly sourceName: string }
  | { readonly kind: "decode-failed"; readonly sourceName: string }
  | { readonly kind: "limit-reached" };

export interface FrameImportCandidate {
  /** 저장될 이름(충돌 회피 적용 후). */
  readonly name: string;
  /** zip 안의 base 이름. */
  readonly sourceName: string;
  readonly imageSize: ImageSize;
  readonly slots: readonly Slot[];
  readonly renamed: boolean;
  readonly bytes: Blob;
}

export interface FrameImportPreview {
  readonly candidates: readonly FrameImportCandidate[];
  readonly warnings: readonly FrameImportWarning[];
}

export type FrameImportPreviewResult =
  | { readonly ok: true; readonly preview: FrameImportPreview }
  | { readonly ok: false; readonly reason: FrameImportRejection };

export interface FrameImportDeps {
  readonly role: UserRole | null;
  readonly userId: string | null;
  /** 현재 개인 프레임 이름들(충돌 판정). */
  readonly existingNames: readonly string[];
  /** 현재 개인 프레임 개수(10개 상한 판정). */
  readonly personalCount: number;
  /** 충돌 회피 8자 접미 생성기(도메인은 난수를 만들지 않는다 — 01 §8). */
  readonly uniqueSuffix: () => string;
  /** PNG 실제 크기. 실패는 `null`. `ImageBitmap`은 구현이 반드시 `close()` 한다. */
  readonly decodeImageSize: (bytes: Blob) => Promise<ImageSize | null>;
  /** deflate 해제. 미지원이면 `null`을 돌려주는 것이 아니라 **함수 자체가 null**이다. */
  readonly inflateRaw: ((bytes: Uint8Array) => Promise<Uint8Array | null>) | null;
}

interface ZipPair {
  readonly base: string;
  png?: Uint8Array;
  slots?: Uint8Array;
  /** 하나라도 deflate면 해제가 필요하다. */
  needsInflate: boolean;
}

/** zip 항목을 base 이름으로 묶는다. 확장자는 소문자 비교다(탐색기가 대문자로 만들 수 있다). */
function groupEntries(
  bytes: Uint8Array,
): { readonly pairs: ZipPair[]; readonly sawUnsupported: boolean } {
  const map = new Map<string, ZipPair>();
  let sawUnsupported = false;

  for (const entry of parseZipEntries(bytes)) {
    // 하위 폴더가 있으면 마지막 세그먼트만 쓴다(탐색기 압축은 폴더를 포함한다).
    const fileName = entry.path.split("/").pop() ?? entry.path;
    const dot = fileName.lastIndexOf(".");
    if (dot <= 0) continue;
    const base = fileName.slice(0, dot);
    const ext = fileName.slice(dot + 1).toLowerCase();
    if (ext !== "png" && ext !== "slots") continue;

    const pair = map.get(base) ?? { base, needsInflate: false };
    if (entry.method === 8) {
      pair.needsInflate = true;
      sawUnsupported = true;
    }
    if (ext === "png") pair.png = entry.data;
    else pair.slots = entry.data;
    map.set(base, pair);
  }

  return { pairs: [...map.values()], sawUnsupported };
}

/** 자기 계정 접두(`{myId}_`)만 제거한다 — 남의 접두는 이름의 일부일 수 있다. */
function stripOwnPrefix(base: string, userId: string): string {
  const prefix = `${userId}_`;
  return base.startsWith(prefix) ? base.slice(prefix.length) : base;
}

export async function previewFrameImport(
  file: File,
  deps: FrameImportDeps,
): Promise<FrameImportPreviewResult> {
  // 첫 실행문이 권한 가드다(렌더 가드와 2중 — M10).
  if (deps.userId === null || deps.userId.length === 0) return { ok: false, reason: "not-logged-in" };
  if (deps.role === null || !canWriteFrames(deps.role)) {
    return { ok: false, reason: "no-write-permission" };
  }
  if (exceedsLocalFrameLimit(deps.personalCount)) return { ok: false, reason: "limit-reached" };

  let raw: Uint8Array;
  try {
    raw = new Uint8Array(await file.arrayBuffer());
  } catch (err) {
    logger.warn("프레임 zip 읽기 실패", {
      reason: err instanceof Error ? err.message : String(err),
    });
    return { ok: false, reason: "malformed-zip" };
  }

  const { pairs, sawUnsupported } = groupEntries(raw);
  if (pairs.length === 0) {
    // EOCD가 없거나 우리가 아는 항목이 하나도 없다. 둘을 구분할 근거가 없으므로 손상으로 본다.
    return { ok: false, reason: raw.length === 0 ? "malformed-zip" : "no-entries" };
  }
  if (sawUnsupported && deps.inflateRaw === null) {
    return { ok: false, reason: "compression-unsupported" };
  }

  const candidates: FrameImportCandidate[] = [];
  const warnings: FrameImportWarning[] = [];
  const taken = new Set<string>(deps.existingNames);

  for (const pair of pairs) {
    if (pair.png === undefined) continue;
    if (pair.slots === undefined) {
      warnings.push({ kind: "missing-slots", sourceName: pair.base });
      continue;
    }

    // 상한은 **한 건마다** 재평가한다(적용 도중 넘지 않게).
    if (exceedsLocalFrameLimit(deps.personalCount + candidates.length)) {
      warnings.push({ kind: "limit-reached" });
      break;
    }

    const pngBytes = await maybeInflate(pair.png, pair.needsInflate, deps);
    const slotsBytes = await maybeInflate(pair.slots, pair.needsInflate, deps);
    if (pngBytes === null || slotsBytes === null) {
      warnings.push({ kind: "decode-failed", sourceName: pair.base });
      continue;
    }

    const desired = stripOwnPrefix(pair.base, deps.userId);
    if (!validateFrameName(desired).ok) {
      warnings.push({ kind: "invalid-name", sourceName: pair.base });
      continue;
    }

    const renamed = taken.has(desired);
    const name = renamed ? nextCopyName(desired, taken, deps.uniqueSuffix) : desired;
    taken.add(name);

    const blob = new Blob([pngBytes], { type: "image/png" });
    const actual = await deps.decodeImageSize(blob);
    if (actual === null || actual.width <= 0 || actual.height <= 0) {
      warnings.push({ kind: "decode-failed", sourceName: pair.base });
      taken.delete(name);
      continue;
    }

    const parsed = parseSlotsFile(new TextDecoder().decode(slotsBytes));
    // `#imagesize`와 실제 PNG 크기가 다르면 좌표계를 환산한다(F24 — `rescaleSlots`가 정본).
    const needsRescale =
      parsed.imageSize.width > 0 && parsed.imageSize.width !== actual.width;
    const slots = needsRescale
      ? rescaleSlots(parsed.slots, actual.width / parsed.imageSize.width, actual.width, actual.height)
      : parsed.slots;

    candidates.push({
      name,
      sourceName: pair.base,
      imageSize: actual,
      slots,
      renamed,
      bytes: blob,
    });
  }

  if (candidates.length === 0) {
    return { ok: false, reason: warnings.length > 0 ? "no-entries" : "no-entries" };
  }
  return { ok: true, preview: { candidates, warnings } };
}

async function maybeInflate(
  bytes: Uint8Array,
  needed: boolean,
  deps: FrameImportDeps,
): Promise<Uint8Array | null> {
  if (!needed) return bytes;
  if (deps.inflateRaw === null) return null;
  try {
    return await deps.inflateRaw(bytes);
  } catch {
    return null;
  }
}

export interface FrameImportApplyDeps {
  readonly userId: string;
  /** `frameStore.saveLocal` — **새 저장 경로를 만들지 않는다**. */
  readonly saveLocal: (input: {
    scope: "user";
    ownerId: string;
    name: string;
    dbId: null;
    imageSize: ImageSize;
    slots: readonly Slot[];
    bytes: Blob;
  }) => Promise<FrameTemplate | null>;
}

export interface FrameImportOutcome {
  readonly imported: number;
  readonly failed: number;
}

/** [적용]. **항상 개인 스코프**(`scope:"user"`)이고 `dbId`는 기록하지 않는다(05 §4.4). */
export async function applyFrameImport(
  preview: FrameImportPreview,
  deps: FrameImportApplyDeps,
): Promise<FrameImportOutcome> {
  let imported = 0;
  let failed = 0;

  for (const candidate of preview.candidates) {
    let saved: FrameTemplate | null = null;
    try {
      saved = await deps.saveLocal({
        scope: "user",
        ownerId: deps.userId,
        name: candidate.name,
        dbId: null,
        imageSize: candidate.imageSize,
        slots: candidate.slots,
        bytes: candidate.bytes,
      });
    } catch {
      saved = null;
    }
    if (saved === null) failed++;
    else imported++;
  }

  logger.info("프레임 가져오기 적용", { imported, failed, limit: LOCAL_FRAME_LIMIT });
  return { imported, failed };
}

// ───────────────────────────── 기본 배선 ─────────────────────────────

/**
 * `DecompressionStream("deflate-raw")` 런타임 감지. **타입을 믿지 않는다**(15 §4 함정 #2).
 * 미지원이면 `null`이고 호출측이 전용 안내를 낸다.
 */
export function resolveInflateRaw(): ((bytes: Uint8Array) => Promise<Uint8Array | null>) | null {
  const ctor = (globalThis as { DecompressionStream?: unknown }).DecompressionStream;
  if (typeof ctor !== "function") return null;

  return async (bytes: Uint8Array) => {
    try {
      const stream = new Blob([bytes]).stream().pipeThrough(new DecompressionStream("deflate-raw"));
      return new Uint8Array(await new Response(stream).arrayBuffer());
    } catch (err) {
      logger.warn("zip deflate 해제 실패", {
        reason: err instanceof Error ? err.message : String(err),
      });
      return null;
    }
  };
}

/** PNG 실제 크기. `ImageBitmap`을 반드시 `close()` 한다(WR8). */
export async function decodePngSize(bytes: Blob): Promise<ImageSize | null> {
  if (typeof createImageBitmap !== "function") return null;
  let bitmap: ImageBitmap | null = null;
  try {
    bitmap = await createImageBitmap(bytes);
    return { width: bitmap.width, height: bitmap.height };
  } catch {
    return null;
  } finally {
    bitmap?.close();
  }
}
