import {
  evenDimensions,
  planTimelapse,
  timelapseBitrate,
  TIMELAPSE_MIN_FRAMES,
  TIMELAPSE_OUTPUT_FPS,
} from "@domain/capture/timelapsePlan";
import { logger } from "@adapters/storage/logStore";
import {
  timelapseDirPath,
  type SessionWorkspace,
} from "@adapters/storage/sessionWorkspace";
import type { EncodeJob, EncoderPath, EncodeStats } from "./encodeProtocol";
import { getEncodeClient, type EncodeClient } from "./encodeClient";
import { detectEncoderPath } from "./encoderSupport";
import {
  createCanvasRecorderPort,
  encodeWithMediaRecorder,
} from "./mediaRecorderMp4";

/**
 * 타임랩스 인코딩 오케스트레이터(메인 스레드) — 04 §7.2·§7.3·§7.5
 *
 * 선별(도메인) → 경로 판정 → 인코딩(Worker 또는 메인) → `Blob | null`.
 *
 * ⚠️ **로그를 남기는 유일한 지점이다.** Worker에는 로그 스토어가 붙지 않아 거기서 남긴 로그는
 *    진단·내보내기에 도달하지 않는다(05 §7.2).
 * ⚠️ 로그 키 이름 주의(15 §4 함정 #1): `code`·`token`·`state`·`nonce`·`pin`은 `[masked]`가 된다.
 *    **코덱 문자열은 반드시 `codecName`으로 담는다.**
 * ⚠️ 어떤 실패·미지원도 `null`이다. **절대 throw하지 않는다**(VF-6).
 */

export interface TimelapseResult {
  readonly blob: Blob;
  readonly path: EncoderPath;
  readonly width: number;
  readonly height: number;
  /** 실제 인코딩된 프레임 수. */
  readonly frameCount: number;
  /** 계획된 출력 길이(초). */
  readonly durationSec: number;
  readonly speedFactor: number;
  readonly bytes: number;
  readonly elapsedMs: number;
}

export interface EncodeTimelapseInput {
  readonly workspace: SessionWorkspace;
  /** 촬영 시퀀스 실경과(초). */
  readonly actualSeconds: number;
  /** 스풀된 가공 프레임 크기(짝수 클램프 전). */
  readonly size: { width: number; height: number };
}

export interface EncodeTimelapseDeps {
  readonly detect?: typeof detectEncoderPath;
  readonly client?: EncodeClient;
  readonly runMediaRecorder?: typeof encodeWithMediaRecorder;
  readonly now?: () => number;
  readonly delay?: (ms: number) => Promise<void>;
}

function round1(value: number): number {
  return Math.round(value * 10) / 10;
}

export async function encodeTimelapse(
  input: EncodeTimelapseInput,
  deps: EncodeTimelapseDeps = {},
): Promise<TimelapseResult | null> {
  // 최후 방어선. 여기서 예외가 새면 `Result`의 [다음]이 막혀 손님이 갇힌다.
  try {
    return await runEncode(input, deps);
  } catch (err) {
    logger.warn("타임랩스 생성 중 예외(타임랩스 없이 계속)", {
      reason: err instanceof Error ? err.message : String(err),
    });
    return null;
  }
}

async function runEncode(
  input: EncodeTimelapseInput,
  deps: EncodeTimelapseDeps,
): Promise<TimelapseResult | null> {
  const detect = deps.detect ?? detectEncoderPath;
  const now = deps.now ?? (() => performance.now());
  const startedAt = now();

  // 1. 스풀 목록(파일명이 0 패딩이라 문자열 정렬 = 시간 정렬이다).
  const names = await input.workspace.listTimelapseFrames();
  if (names.length === 0) {
    logger.warn("타임랩스 스풀 프레임이 없습니다", {
      sessionId: input.workspace.sessionId,
      actualSeconds: round1(input.actualSeconds),
    });
    return null;
  }

  // 2. 선별 계획(도메인 · 순수).
  const plan = planTimelapse({
    spoolFrameCount: names.length,
    actualSeconds: input.actualSeconds,
  });
  if (plan === null) {
    logger.warn("타임랩스를 만들지 않음(선별 프레임 부족)", {
      spooled: names.length,
      actualSeconds: round1(input.actualSeconds),
      minFrames: TIMELAPSE_MIN_FRAMES,
    });
    return null;
  }

  // 3. 경로 판정(런타임 기능 감지 · 1회).
  const even = evenDimensions(input.size.width, input.size.height);
  const probe = await detect(input.size);
  logger.info("타임랩스 인코더 경로 판정", {
    path: probe.path,
    codecName: probe.codec,
    reason: probe.reason,
  });
  if (probe.path === "none") {
    logger.warn("타임랩스 미제공(브라우저 H.264 인코더 없음)", {
      width: even.width,
      height: even.height,
    });
    return null;
  }

  // 4. 인코딩 지시(선별은 이미 끝났다).
  const job: EncodeJob = {
    dirPath: timelapseDirPath(input.workspace.sessionId),
    names: plan.selectedIndices.map((index) => names[index]!),
    timestampsUs: plan.timestampsUs,
    frameDurationUs: plan.frameDurationUs,
    config: {
      codec: probe.codec ?? "avc1.42001E",
      width: even.width,
      height: even.height,
      bitrate: timelapseBitrate(even.width, even.height),
      framerate: TIMELAPSE_OUTPUT_FPS,
    },
  };

  // 5. 실행. **경로 B가 실패해도 A로 재시도하지 않는다** — 판정은 앞에서 1회다.
  //    B 실패는 통상 인코더 자체의 문제이고, A는 최대 15초를 더 쓰면서 결과 보장도 없다.
  const outcome =
    probe.path === "webcodecs"
      ? await runWorker(job, deps)
      : await runMainThread(job, input.workspace, deps, now);

  if (outcome.blob === null) {
    logger.warn("타임랩스 생성 실패", {
      path: probe.path,
      codecName: probe.codec,
      reason: outcome.reason,
      spooled: names.length,
      selected: job.names.length,
      ...statsFields(outcome.stats),
    });
    return null;
  }

  const elapsedMs = Math.round(now() - startedAt);
  logger.info("타임랩스 생성", {
    path: probe.path,
    codecName: probe.codec,
    width: even.width,
    height: even.height,
    spooled: names.length,
    selected: job.names.length,
    ...statsFields(outcome.stats),
    speedFactor: round1(plan.speedFactor),
    durationSec: round1(plan.outputSeconds),
    bytes: outcome.blob.size,
    elapsedMs,
  });

  return {
    blob: outcome.blob,
    path: probe.path,
    width: even.width,
    height: even.height,
    frameCount: outcome.stats?.encodedFrames ?? job.names.length,
    durationSec: plan.outputSeconds,
    speedFactor: plan.speedFactor,
    bytes: outcome.blob.size,
    elapsedMs,
  };
}

interface Outcome {
  readonly blob: Blob | null;
  readonly reason: string | null;
  readonly stats: EncodeStats | null;
}

/** 경로 B — Worker. 완성 mp4를 ArrayBuffer transfer로 받는다. */
async function runWorker(job: EncodeJob, deps: EncodeTimelapseDeps): Promise<Outcome> {
  const client = deps.client ?? getEncodeClient();
  const result = await client.run(job);
  if ("error" in result) {
    return { blob: null, reason: result.error, stats: result.stats };
  }
  return { blob: result.blob, reason: null, stats: result.stats };
}

/** 경로 A — 메인 스레드(예비). 출력 길이만큼 실제 시간이 걸린다. */
async function runMainThread(
  job: EncodeJob,
  workspace: SessionWorkspace,
  deps: EncodeTimelapseDeps,
  now: () => number,
): Promise<Outcome> {
  const run = deps.runMediaRecorder ?? encodeWithMediaRecorder;
  const result = await run(job, {
    // 읽기는 Worker 경계를 요구하지 않는다(05 §3.1) — 메인에서 바로 읽는다.
    loadFrame: (name) => workspace.readFile(`tl/${name}`),
    createPort: createCanvasRecorderPort,
    now,
    delay: deps.delay ?? ((ms) => new Promise((resolve) => setTimeout(resolve, ms))),
  });
  if (!result.ok) {
    return { blob: null, reason: result.reason, stats: result.stats };
  }
  return { blob: result.blob, reason: null, stats: result.stats };
}

function statsFields(stats: EncodeStats | null): Record<string, number> {
  if (stats === null) return {};
  return {
    encodedFrames: stats.encodedFrames,
    droppedFrames: stats.droppedFrames,
    skippedFrames: stats.skippedFrames,
    encodeMs: stats.elapsedMs,
  };
}
