import { resultFolderName, resultFolderNameFromSessionId } from "@domain/results/resultNaming";
import { planResultSave, type ResultSavePlan } from "@domain/results/resultSavePlan";
import type { OutputFormat } from "@domain/settings/appSettings";
import { getDirHandleRepo, type DirHandleRepo, type DirFileToWrite } from "./dirHandleRepo";
import { getOpfsClient, type OpfsClient } from "./opfsClient";
import { logger } from "./logStore";
import { OPFS_DIRS } from "./opfsProtocol";
import { getResultsStore, type ResultsStore } from "./resultsStore";

/**
 * 결과물 로컬 보관 오케스트레이션 — **M6-W 본체** (05 §5.1)
 *
 * ```
 * ① results/{folder}/final.{ext}   ← 필수. 전용 Worker 경계를 지난다(VF-14).
 * ① results/{folder}/timelapse.mp4 ← 타임랩스가 있을 때만(없는 것은 정상 — VF-6)
 * ② 사용자 지정 폴더에도 복사       ← Chromium 데스크톱만. 권한이 granted일 때만
 * ③ 보존 정책 집행(2GB / 200세션)
 * ```
 *
 * ⚠️ **순서가 불변식이다**: 합성 → 로컬 보관 → 업로드 분기. 보관이 업로드보다 먼저다.
 * ⚠️ **절대 throw하지 않는다.** 모든 실패가 `status`로 표현된다 — 저장 실패를 성공으로
 *    오인시켜서도(M4), 실패로 촬영 흐름을 멈춰서도(키오스크에 손님이 갇힌다) 안 된다.
 * ⚠️ 이 파일은 저장소 API를 직접 부르지 않는다. 모든 기록이 `OpfsClient`(전용 Worker RPC)를 지난다.
 */

export type ResultSaveStatus = "saved" | "partial" | "failed" | "skipped";

export type FolderCopyStatus =
  /** 브라우저에 ② 능력이 없다(Safari·Firefox·모바일). */
  | "unsupported"
  /** 운영자가 폴더를 지정하지 않았다. */
  | "no-handle"
  /** 핸들은 있는데 권한이 granted가 아니다 → **자동 요청하지 않는다**(제스처 필요). */
  | "permission-required"
  | "copied"
  | "failed";

export interface ResultSaveInput {
  /** `useResultCompose().currentBlob()`. null이면 `skipped`. */
  readonly finalBlob: Blob | null;
  readonly format: OutputFormat;
  /** `getTimelapseService().current()?.blob ?? null`. **null은 합법**(VF-6). */
  readonly timelapseBlob: Blob | null;
  /** 설정 `SaveLocalCopy`. */
  readonly saveLocalCopy: boolean;
  /** 폴더명 기준. 촬영 시작 시각을 담고 있다. */
  readonly sessionId: string | null;
  /** `sessionId`가 없거나 형식이 깨졌을 때의 폴백 시각(어댑터 경계에서 주입). */
  readonly localTime: Date;
  /** 충돌 999회 소진 시 접미(32자 hex). */
  readonly fallbackToken: string;
}

export interface ResultSaveOutcome {
  readonly status: ResultSaveStatus;
  /** ① 보관 위치에 만든 폴더명. */
  readonly folderName: string | null;
  /** **M6-W 충족 여부**와 같다. */
  readonly finalSaved: boolean;
  readonly timelapseSaved: boolean;
  readonly hadTimelapse: boolean;
  readonly folderCopy: FolderCopyStatus;
  /** ② 폴더에 만든 이름(①과 다를 수 있다). */
  readonly folderCopyName: string | null;
  /** 보존 정책이 삭제한 폴더 수. */
  readonly evicted: number;
  readonly bytes: number;
  readonly elapsedMs: number;
}

export interface ResultSaverDeps {
  readonly opfs?: OpfsClient;
  readonly results?: ResultsStore;
  readonly dirHandles?: DirHandleRepo;
  readonly now?: () => number;
}

function blobFor(input: ResultSaveInput, kind: "final" | "timelapse"): Blob | null {
  return kind === "final" ? input.finalBlob : input.timelapseBlob;
}

/** ② 계층. ① 실패와 **무관하게** 시도한다 — 보관 기회를 버리지 않는다. */
async function copyToUserFolder(
  repo: DirHandleRepo,
  baseFolderName: string,
  files: readonly DirFileToWrite[],
): Promise<{ status: FolderCopyStatus; folderName: string | null }> {
  try {
    if (!repo.isSupported()) return { status: "unsupported", folderName: null };

    const handle = await repo.load();
    if (handle === null) return { status: "no-handle", folderName: null };

    // ⚠️ granted가 아니면 여기서 `request()`를 부르지 않는다 — 사용자 제스처가 필요하고,
    //    손님 화면에서 운영자용 권한 대화상자를 띄우면 흐름이 막힌다.
    const permission = await repo.query(handle);
    if (permission !== "granted") return { status: "permission-required", folderName: null };

    const written = await repo.writeFolder(handle, baseFolderName, files);
    return {
      status: written.ok ? "copied" : "failed",
      folderName: written.folderName,
    };
  } catch {
    return { status: "failed", folderName: null };
  }
}

async function runSave(
  input: ResultSaveInput,
  deps: ResultSaverDeps,
  startedAt: number,
  now: () => number,
): Promise<ResultSaveOutcome> {
  const opfs = deps.opfs ?? getOpfsClient();
  const results = deps.results ?? getResultsStore();
  const dirHandles = deps.dirHandles ?? getDirHandleRepo();

  const hadTimelapse = input.timelapseBlob !== null;
  // 폴더 시각은 `sessionId`(촬영 시작 시각)를 우선한다 — 로컬 폴더와 서버 세션이 짝지어져야 한다.
  const baseFolderName =
    (input.sessionId === null ? null : resultFolderNameFromSessionId(input.sessionId)) ??
    resultFolderName(input.localTime);

  function planWith(existingFolders: readonly string[]): ResultSavePlan {
    return planResultSave({
      saveLocalCopy: input.saveLocalCopy,
      hasFinalImage: input.finalBlob !== null,
      hasTimelapse: hadTimelapse,
      format: input.format,
      baseFolderName,
      existingFolders,
      fallbackToken: input.fallbackToken,
    });
  }

  function skipped(reason: string): ResultSaveOutcome {
    logger.info("결과물 로컬 보관 건너뜀", { reason, hadTimelapse });
    return {
      status: "skipped",
      folderName: null,
      finalSaved: false,
      timelapseSaved: false,
      hadTimelapse,
      folderCopy: "unsupported",
      folderCopyName: null,
      evicted: 0,
      bytes: 0,
      elapsedMs: now() - startedAt,
    };
  }

  // 게이트를 먼저 본다. skip이면 목록 왕복(Worker RPC)조차 하지 않는다.
  // 게이트 자체는 도메인(`planResultSave`)이 소유해 진입점이 늘어도 규칙이 흩어지지 않는다.
  const gate = planWith([]);
  if (gate.kind === "skip") return skipped(gate.reason);

  const existing = await results.listFolders();
  const plan = planWith(existing);
  // 같은 입력이라 도달하지 않지만 판별 유니온을 좁힌다(컴파일러 계약).
  if (plan.kind === "skip") return skipped(plan.reason);

  // ① 보관 위치 기록 — final이 **먼저**, 있으면 timelapse가 뒤다.
  let finalSaved = false;
  let timelapseSaved = false;
  let bytes = 0;
  const files: DirFileToWrite[] = [];
  for (const target of plan.targets) {
    const blob = blobFor(input, target.kind);
    if (blob === null) continue;
    files.push({ name: target.fileName, blob });

    const ok = await opfs.write(
      `${OPFS_DIRS.results}/${plan.folderName}/${target.fileName}`,
      blob,
    );
    if (!ok) continue;
    bytes += blob.size;
    if (target.kind === "final") finalSaved = true;
    else timelapseSaved = true;
  }

  // ② 사용자 지정 폴더 복사. ①이 할당량으로 실패해도 ②는 성공할 수 있다.
  const copy = await copyToUserFolder(dirHandles, baseFolderName, files);

  // ③ 보존 정책. 실패해도 status를 바꾸지 않는다(정리는 보관의 성패와 무관하다).
  let evicted = 0;
  try {
    evicted = await results.enforceRetention();
  } catch {
    evicted = 0;
  }

  const status: ResultSaveStatus = !finalSaved
    ? "failed"
    : hadTimelapse && !timelapseSaved
      ? "partial"
      : "saved";

  const outcome: ResultSaveOutcome = {
    status,
    folderName: plan.folderName,
    finalSaved,
    timelapseSaved,
    hadTimelapse,
    folderCopy: copy.status,
    folderCopyName: copy.folderName,
    evicted,
    bytes,
    elapsedMs: now() - startedAt,
  };

  const ctx = { ...outcome };
  if (status === "failed") logger.error("결과물 로컬 보관", ctx);
  else if (status === "partial") logger.warn("결과물 로컬 보관", ctx);
  else logger.info("결과물 로컬 보관", ctx);

  return outcome;
}

/** ⚠️ **절대 throw하지 않는다.** 모든 실패가 `status`로 표현된다. */
export async function saveResultLocally(
  input: ResultSaveInput,
  deps: ResultSaverDeps = {},
): Promise<ResultSaveOutcome> {
  const now = deps.now ?? (() => Date.now());
  const startedAt = now();
  try {
    return await runSave(input, deps, startedAt, now);
  } catch (err) {
    // 여기 도달하면 어댑터 규약이 깨진 것이다 — 그래도 화면을 멈추지 않는다.
    logger.error("결과물 로컬 보관 중 예외", {
      reason: err instanceof Error ? err.message : String(err),
    });
    return {
      status: "failed",
      folderName: null,
      finalSaved: false,
      timelapseSaved: false,
      hadTimelapse: input.timelapseBlob !== null,
      folderCopy: "failed",
      folderCopyName: null,
      evicted: 0,
      bytes: 0,
      elapsedMs: now() - startedAt,
    };
  }
}
