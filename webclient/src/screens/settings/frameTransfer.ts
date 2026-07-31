import { canWriteFrames } from "@domain/roles/userRole";
import type { UserRole } from "@domain/roles/userRole";
import {
  applyFrameImport,
  decodePngSize,
  defaultFrameExportDeps,
  exportFrames,
  previewFrameImport,
  resolveInflateRaw,
  type FrameExportOutcome,
  type FrameImportOutcome,
  type FrameImportPreview,
  type FrameImportPreviewResult,
  type FrameImportRejection,
  type FrameImportWarning,
} from "@adapters/storage/exportImport";
import { getFrameStore } from "@adapters/storage/frameStore";
import { logger } from "@adapters/storage/logStore";
import { formatCount, STRINGS } from "@ui/strings";

/**
 * 설정 §6의 프레임 내보내기 / 가져오기 액션 — 05 §2.5·§4.6 (React 무관)
 *
 * 어댑터가 돌려준 **키**를 여기서 문구로 옮긴다(어댑터는 문구 카탈로그를 갖지 않는다).
 *
 * ⚠️ 내보내기는 `!isGuest`, 가져오기는 `canWriteFrames(role)`이다 — **축이 다르다**.
 *    게스트는 개인 프레임이 없고, 공용 백업은 운영자 작업이다.
 * ⚠️ 가져오기는 **파싱 → 미리보기 → [적용]**이다. 파일을 고르는 것만으로 저장되지 않는다.
 */

export type { FrameImportPreview, FrameImportPreviewResult };

/** 8자 난수 접미(충돌 회피 폴백). 도메인은 난수를 만들지 않으므로 여기서 만든다(01 §8). */
function newUniqueSuffix(): string {
  const source = globalThis.crypto;
  if (typeof source?.randomUUID === "function") {
    return source.randomUUID().replace(/-/g, "").slice(0, 8);
  }
  logger.warn("crypto.randomUUID 미지원 — 시각 기반 접미로 폴백");
  return Date.now().toString(16).slice(-8);
}

// ───────────────────────────── 내보내기 ─────────────────────────────

export interface FrameExportReport {
  readonly ok: boolean;
  readonly message: string;
}

/** 내보내기 1회. 부분 실패를 **개수로 정직하게** 보고한다(M4). */
export async function runFrameExport(userId: string | null): Promise<FrameExportReport> {
  // 액션 첫 줄 가드(렌더 가드와 2중). 게스트는 내보낼 개인 프레임이 없다.
  if (userId === null || userId.length === 0) {
    return { ok: false, message: STRINGS.transfer.notLoggedIn };
  }

  const outcome: FrameExportOutcome = await exportFrames(defaultFrameExportDeps(userId));
  if (!outcome.ok) {
    return {
      ok: false,
      message:
        outcome.exported === 0 && outcome.skipped === 0
          ? STRINGS.transfer.exportEmpty
          : STRINGS.transfer.exportFailed,
    };
  }
  return {
    ok: true,
    message:
      outcome.skipped > 0
        ? formatCount(STRINGS.transfer.exportedPartial, outcome.exported)
        : formatCount(STRINGS.transfer.exportedFrames, outcome.exported),
  };
}

// ───────────────────────────── 가져오기 ─────────────────────────────

export function frameImportRejectionMessage(reason: FrameImportRejection): string {
  switch (reason) {
    case "not-logged-in":
      return STRINGS.transfer.notLoggedIn;
    case "no-write-permission":
      return STRINGS.transfer.noWritePermission;
    case "malformed-zip":
      return STRINGS.transfer.malformedZip;
    case "no-entries":
      return STRINGS.transfer.noEntries;
    case "limit-reached":
      return STRINGS.frames.limitReached;
    case "compression-unsupported":
      return STRINGS.transfer.compressionUnsupported;
    default:
      return STRINGS.error.temporary;
  }
}

export function frameImportWarningMessage(warning: FrameImportWarning): string {
  switch (warning.kind) {
    case "invalid-name":
      return `${warning.sourceName}: ${STRINGS.frames.nameInvalidChars}`;
    case "missing-slots":
      return `${warning.sourceName}: ${STRINGS.transfer.noEntries}`;
    case "decode-failed":
      return `${warning.sourceName}: ${STRINGS.frameEditor.imageDecodeFailed}`;
    default:
      return STRINGS.frames.limitReached;
  }
}

/** 파일 → 미리보기. **첫 실행문이 권한 가드**이고 어댑터가 한 번 더 막는다(M10). */
export async function startFrameImport(
  file: File,
  role: UserRole | null,
  userId: string | null,
): Promise<FrameImportPreviewResult> {
  if (role === null || !canWriteFrames(role)) {
    return { ok: false, reason: "no-write-permission" };
  }
  if (userId === null || userId.length === 0) return { ok: false, reason: "not-logged-in" };

  const store = getFrameStore();
  const [existingNames, personalCount] = await Promise.all([
    store.scopeFrameNames("user", userId),
    store.countPersonal(userId),
  ]);

  return previewFrameImport(file, {
    role,
    userId,
    existingNames,
    personalCount,
    uniqueSuffix: newUniqueSuffix,
    decodeImageSize: (bytes) => decodePngSize(bytes),
    inflateRaw: resolveInflateRaw(),
  });
}

/** [지금 적용] — 저장은 여기서만 일어난다. 항상 개인 스코프다. */
export async function applyFramePreview(
  preview: FrameImportPreview,
  role: UserRole | null,
  userId: string | null,
): Promise<FrameImportOutcome | null> {
  if (role === null || !canWriteFrames(role)) {
    logger.warn("프레임 가져오기 적용 거부(권한 없음)");
    return null;
  }
  if (userId === null || userId.length === 0) return null;

  return applyFrameImport(preview, {
    userId,
    saveLocal: (input) => getFrameStore().saveLocal(input),
  });
}

/** 결과 문구. 부분 실패를 숨기지 않는다. */
export function frameImportDoneMessage(outcome: FrameImportOutcome): string {
  const base = formatCount(STRINGS.transfer.importDone, outcome.imported);
  return outcome.failed === 0
    ? base
    : `${base} ${formatCount(STRINGS.transfer.importPartial, outcome.failed)}`;
}
