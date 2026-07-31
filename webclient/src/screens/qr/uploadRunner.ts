import type { AppSettingsValues } from "@domain/settings/appSettings";
import {
  downloadPageUrl,
  finalImageContentType,
  isValidSessionId,
  newSessionId,
  TIMELAPSE_CONTENT_TYPE,
} from "@domain/upload/uploadContract";
import {
  activeStages,
  overallProgress,
  resolveUploadTargets,
  type UploadStage,
} from "@domain/upload/uploadOrchestration";
import { isConflict, NetworkError, TempUserLimitError } from "@adapters/http/errors";
import {
  createUploadGateway,
  type PrepareFileRequest,
  type UploadGateway,
  type UploadKind,
} from "@adapters/http/uploadGateway";
import { getTimelapseService } from "@adapters/encode/timelapseService";
import { logger } from "@adapters/storage/logStore";
import { currentSettings } from "@shell/settingsStore";
import { sessionStore, type FinalImageArtifact } from "@shell/sessionStore";
import { STRINGS } from "@ui/strings";

/**
 * 업로드 3단계 오케스트레이션 — 06 §4 · 03 §9.1
 *
 * ```
 * ① POST /uploads/prepare   (파일당 1회)
 * ② PUT {putUrl}            (XHR · requiredHeaders 전량 순회 · 자격 증명 0건 · 진행률)
 * ③ POST /uploads/commit    (prepare의 downloadUrl 그대로 · downloadPageUrl = P1 도메인)
 * ```
 *
 * 이 파일은 **React를 import하지 않는다**(`runResultNext`와 같은 형태) — 순서가 불변식인데
 * 컴포넌트 안에 있으면 node 테스트가 닿지 못한다(15 §3.1).
 *
 * ⚠️ **M7**: 올릴 것이 하나도 없으면 요청을 **아예 보내지 않는다**(빈 commit 금지).
 * ⚠️ **M8**: 어느 파일이든 PUT이 실패하면 **commit을 호출하지 않는다.** "타임랩스는 실패했지만
 *    사진만 commit"을 하면 P1이 `timelapseUrl: null`을 "옵션 꺼짐"으로 표시해 실패를 은폐한다.
 * ⚠️ `downloadPageUrl`은 **P1 다운로드 페이지 도메인**이어야 한다. kiosk 도메인을 넣으면
 *    QR이 손님 폰에서 키오스크 앱을 연다.
 */

export type UploadFailureReason =
  /** 403 `TEMP_USER_TIME_EXCEEDED` */
  | "temp-user-time"
  /** 403 `TEMP_USER_COUNT_EXCEEDED` */
  | "temp-user-count"
  /** 응답 없음(타임아웃·CORS 차단 포함) */
  | "network"
  /** 409 — 같은 세션 재commit(이중 실행 의심) */
  | "conflict"
  /** 그 밖의 서버 오류 · PUT 4xx/5xx · 예기치 못한 예외 */
  | "server";

export type UploadPhase =
  | { readonly kind: "idle" }
  /** 전송 대상 0 — 요청 0건(M7). */
  | { readonly kind: "nothing" }
  | {
      readonly kind: "uploading";
      readonly stage: UploadStage;
      /** `null`이면 불확정(indeterminate) — 06 §4.5의 초기 상태다. */
      readonly progress: number | null;
    }
  | {
      readonly kind: "succeeded";
      readonly downloadPageUrl: string;
      readonly retentionHours: number;
    }
  | { readonly kind: "failed"; readonly reason: UploadFailureReason };

export interface UploadRunOutcome {
  readonly phase: UploadPhase;
  /** 취소(화면 이탈·재시도로 교체)로 끝났는가. true면 phase를 화면에 반영하지 않는다. */
  readonly aborted: boolean;
}

export interface UploadRunDeps {
  readonly gateway: UploadGateway;
  readonly finalImage: () => FinalImageArtifact | null;
  readonly timelapse: () => Blob | null;
  readonly settings: () => AppSettingsValues;
  /** 촬영 세션 ID(`sessionStore.sessionId`). */
  readonly captureSessionId: () => string | null;
  /** 0 = 최초, 1↑ = [재시도] 횟수. 세션 ID 결정에 쓰인다. */
  readonly attempt: number;
  readonly onPhase: (phase: UploadPhase) => void;
  readonly now: () => Date;
  readonly uuid: () => string;
  readonly signal?: AbortSignal;
}

/**
 * 최초 시도는 촬영 세션 ID를 **재사용**하고 [재시도]는 **새로 만든다**(06 §4.4).
 * 같은 ID로 재commit하면 서버가 409로 막는다(이중집계 차단 장치라 정상 동작이다).
 */
export function resolveUploadSessionId(
  captureSessionId: string | null,
  attempt: number,
  now: Date,
  uuid: string,
): string {
  if (attempt === 0 && captureSessionId !== null && isValidSessionId(captureSessionId)) {
    return captureSessionId;
  }
  return newSessionId(now, uuid);
}

/** 03 §9.2 문구표. 로컬 저장 **토글** 기준이다(실제 보관 성패가 아니다 — Windows와 동일). */
export function uploadFailureMessage(
  reason: UploadFailureReason,
  saveLocalCopy: boolean,
): string {
  if (reason === "temp-user-time") return STRINGS.upload.tempUserTimeExceeded;
  if (reason === "temp-user-count") return STRINGS.upload.tempUserCountExceeded;
  return saveLocalCopy ? STRINGS.upload.failedSaved : STRINGS.upload.failedNotSaved;
}

/** 진행률 단계 라벨(06 §4.5). ⚠️ 호출 **순서를 가정한 단언을 쓰지 않는다**. */
export function uploadStageLabel(stage: UploadStage): string {
  switch (stage) {
    case "Photo":
      return STRINGS.upload.stagePhoto;
    case "Timelapse":
      return STRINGS.upload.stageTimelapse;
    default:
      return STRINGS.upload.stageFinalizing;
  }
}

/** 오류 → 사유 매핑의 **유일한 지점**. 상태코드를 화면에 흩뿌리지 않는다. */
function classifyUploadError(err: unknown): UploadFailureReason {
  if (err instanceof TempUserLimitError) {
    return err.reason === "count" ? "temp-user-count" : "temp-user-time";
  }
  if (err instanceof NetworkError) return "network";
  if (isConflict(err)) return "conflict";
  return "server";
}

interface UploadJob {
  readonly stage: UploadStage;
  readonly file: PrepareFileRequest;
  readonly blob: Blob;
}

export async function runUpload(deps: UploadRunDeps): Promise<UploadRunOutcome> {
  const startedMs = deps.now().getTime();
  const elapsed = (): number => deps.now().getTime() - startedMs;
  const isAborted = (): boolean => deps.signal?.aborted === true;

  let phase: UploadPhase = { kind: "idle" };
  const emit = (next: UploadPhase): void => {
    phase = next;
    deps.onPhase(next);
  };
  const stopped = (): UploadRunOutcome => ({ phase, aborted: true });

  if (isAborted()) return stopped();

  const settings = deps.settings();
  const finalImage = deps.finalImage();
  const timelapse = deps.timelapse();

  // ① 전송 대상 확정 = 설정 토글 AND 파일 존재.
  const targets = resolveUploadTargets({
    sendPhoto: settings.SendPhoto,
    sendTimelapse: settings.SendTimelapse,
    hasFinalImage: finalImage !== null,
    hasTimelapse: timelapse !== null,
  });

  // 설정 문제와 생성 실패를 로그에서 가른다(Windows와 동종).
  if (settings.SendPhoto && finalImage === null) {
    logger.warn("사진 전송 옵션 on 이지만 결과 이미지가 없어 전송에서 제외");
  }
  if (settings.SendTimelapse && timelapse === null) {
    logger.warn("타임랩스 전송 옵션 on 이지만 영상이 없어 전송에서 제외");
  }

  // ★ M7: 여기서 끝낸다. prepare조차 부르지 않는다.
  if (!targets.canUpload) {
    logger.info("전송할 결과물이 없어 업로드를 시작하지 않음", {
      sendPhoto: settings.SendPhoto,
      sendTimelapse: settings.SendTimelapse,
    });
    emit({ kind: "nothing" });
    return { phase, aborted: false };
  }

  const captureSessionId = deps.captureSessionId();
  const sessionId = resolveUploadSessionId(
    captureSessionId,
    deps.attempt,
    deps.now(),
    deps.uuid(),
  );
  logger.info("업로드 대상 확정", {
    uploadPhoto: targets.uploadPhoto,
    uploadTimelapse: targets.uploadTimelapse,
    attempt: deps.attempt,
    // ⚠️ 세션 ID 원문은 다운로드 페이지의 `?s=` 토큰과 같다 — 로그에 남기지 않는다.
    sameAsCaptureSession: sessionId === captureSessionId,
  });

  const jobs: UploadJob[] = [];
  if (targets.uploadPhoto && finalImage !== null) {
    jobs.push({
      stage: "Photo",
      blob: finalImage.blob,
      file: {
        kind: "final",
        // ⚠️ 합성 **당시** 포맷을 쓴다. 설정이 그 뒤에 바뀌었어도 바이트는 그대로다.
        ext: finalImage.format === "Png" ? "png" : "jpg",
        contentType: finalImageContentType(finalImage.format),
      },
    });
  }
  if (targets.uploadTimelapse && timelapse !== null) {
    jobs.push({
      stage: "Timelapse",
      blob: timelapse,
      file: { kind: "timelapse", ext: "mp4", contentType: TIMELAPSE_CONTENT_TYPE },
    });
  }

  // 초기 진행률은 **불확정**이다(06 §4.5).
  emit({ kind: "uploading", stage: activeStages(targets)[0] ?? "Finalizing", progress: null });

  const downloadUrls = new Map<UploadKind, string>();

  const fail = (reason: UploadFailureReason): UploadRunOutcome => {
    if (reason === "conflict") {
      logger.warn("업로드 commit 충돌(이중 실행 의심)", { attempt: deps.attempt });
    }
    logger.error("업로드 실패", { reason, attempt: deps.attempt, elapsedMs: elapsed() });
    emit({ kind: "failed", reason });
    return { phase, aborted: false };
  };

  try {
    for (const job of jobs) {
      if (isAborted()) return stopped();

      // ① prepare — 파일당 1회(06 §4.1).
      const prepared = await deps.gateway.prepare({ sessionId, files: [job.file] });
      logger.info("업로드 prepare", {
        kind: job.file.kind,
        attempt: deps.attempt,
        // 웹은 URL을 재조립하지 않으므로 설정 `StorageBucket`을 갱신하지 않는다 — 값만 남긴다.
        bucket: prepared.bucket,
      });

      const upload = prepared.uploads.find((item) => item.kind === job.file.kind);
      if (upload === undefined) {
        logger.error("prepare 응답에 요청한 파일이 없음", { kind: job.file.kind });
        return fail("server");
      }

      if (isAborted()) return stopped();

      // ② 서명 PUT — 어댑터가 던지지 않는다. 결과를 판별 유니온으로 받는다.
      const outcome = await deps.gateway.put({
        kind: job.file.kind,
        url: upload.putUrl,
        body: job.blob,
        headers: upload.requiredHeaders,
        signal: deps.signal,
        onProgress: (progress) => {
          if (isAborted()) return;
          emit({
            kind: "uploading",
            stage: job.stage,
            progress: overallProgress(
              targets,
              job.stage,
              progress.total > 0 ? progress.loaded / progress.total : 0,
            ),
          });
        },
      });

      if (!outcome.ok) {
        // ★ M8: 여기서 멈춘다. commit을 부르지 않는다.
        if (outcome.failure === "aborted") return stopped();
        return fail(
          outcome.failure === "network" || outcome.failure === "timeout" ? "network" : "server",
        );
      }
      downloadUrls.set(job.file.kind, upload.downloadUrl);
    }

    if (isAborted()) return stopped();
    emit({
      kind: "uploading",
      stage: "Finalizing",
      progress: overallProgress(targets, "Finalizing", 0),
    });

    // ③ commit — prepare가 준 `downloadUrl`을 **그대로** 넘긴다(서버가 버킷·경로 소속을 검증한다).
    const pageUrl = downloadPageUrl(settings.HostingBaseUrl, sessionId);
    const finalImageUrl = targets.uploadPhoto ? (downloadUrls.get("final") ?? null) : null;
    const timelapseUrl = targets.uploadTimelapse ? (downloadUrls.get("timelapse") ?? null) : null;

    const committed = await deps.gateway.commit({
      sessionId,
      finalImageUrl,
      timelapseUrl,
      retentionHours: settings.RetentionHours,
      downloadPageUrl: pageUrl,
    });

    if (isAborted()) return stopped();

    logger.info("업로드 commit 완료", {
      hasFinal: finalImageUrl !== null,
      hasTimelapse: timelapseUrl !== null,
      retentionHours: settings.RetentionHours,
      elapsedMs: elapsed(),
    });

    const served = committed.downloadPageUrl;
    emit({
      kind: "succeeded",
      downloadPageUrl: typeof served === "string" && served.length > 0 ? served : pageUrl,
      retentionHours: settings.RetentionHours,
    });
    return { phase, aborted: false };
  } catch (err) {
    // 취소로 인한 예외는 실패가 아니다(화면이 이미 사라졌다).
    if (isAborted()) return stopped();
    return fail(classifyUploadError(err));
  }
}

/**
 * 실제 배선. 전부 **호출 시점에 싱글턴을 해석하는 클로저**다 —
 * 모듈 로드 시 서비스를 잡으면 node 테스트가 브라우저 자원을 붙든다.
 */
export function defaultUploadRunDeps(): Omit<UploadRunDeps, "attempt" | "onPhase"> {
  return {
    gateway: createUploadGateway(),
    finalImage: () => sessionStore.getState().finalImage,
    timelapse: () => getTimelapseService().current()?.blob ?? null,
    settings: currentSettings,
    captureSessionId: () => sessionStore.getState().sessionId,
    now: () => new Date(),
    uuid: () => crypto.randomUUID(),
  };
}
