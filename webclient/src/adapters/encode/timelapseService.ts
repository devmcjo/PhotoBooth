import {
  decimatedInterval,
  planDecimation,
  TIMELAPSE_SPOOL_INTERVAL_MS,
  TIMELAPSE_SPOOL_MAX_FRAMES,
} from "@domain/capture/timelapseSpool";
import { getCameraService, type CameraService } from "@adapters/camera/cameraService";
import { SPOOL_JPEG_QUALITY } from "@adapters/camera/frameProcessorProtocol";
import { logger } from "@adapters/storage/logStore";
import type { SessionWorkspace } from "@adapters/storage/sessionWorkspace";
import { getEncodeClient, type EncodeClient } from "./encodeClient";
import { lastEncoderProbe, type EncoderProbe } from "./encoderSupport";
import { encodeTimelapse, type TimelapseResult } from "./timelapseEncoder";

/**
 * 타임랩스 수집 수명 + 결과 보관 — 04 §7.2 · 03 §8.1
 *
 * ```
 * 촬영 시퀀스 시작  → startCollection()   (스풀 채널 on)
 * 마지막 컷 직후    → stopCollection()    (실경과 확정 · 스풀 off)
 * Result [다음] 1단계 → finish()          (선별 + 인코딩)
 * 홈 복귀           → stop()              (수집 중단 + 인코딩 중단 + 결과 폐기)
 * ```
 *
 * ⚠️ **결과 Blob을 `sessionStore`에 넣지 않는다.** `discardCaptureData()`가 지우는 대상은
 *    컷·프레임이다. mp4는 이 서비스가 들고 있다가 `stop()`(홈 복귀)에서 폐기한다.
 * ⚠️ 가공 크기(`size`)는 **마지막 스풀 프레임에서 기억한다.** `Result` 시점에는 이미
 *    `camera.stop()` 이후라 `processedSize()`가 null이다.
 */

export interface TimelapseStats {
  readonly collecting: boolean;
  /** OPFS에 실제로 기록된 수(솎아내기 반영). */
  readonly spooled: number;
  /** 쓰기 지연·실패로 버린 수. */
  readonly droppedSpool: number;
  readonly decimations: number;
  readonly intervalMs: number;
  readonly elapsedSec: number | null;
  readonly size: { width: number; height: number } | null;
}

export interface TimelapseService {
  /** 촬영 시퀀스 **직전**에 호출. 이 시점부터 종료까지만 수집한다. 멱등. */
  startCollection(workspace: SessionWorkspace): void;
  /** 마지막 컷 직후 호출. 실경과를 확정한다. 멱등. */
  stopCollection(): void;
  /** 선별 + 인코딩. **멱등**(이미 만들었으면 그대로 돌려준다). 실패·미지원은 null. */
  finish(): Promise<TimelapseResult | null>;
  /** Step 10(로컬 보관)·Step 11(업로드)이 읽는다. */
  current(): TimelapseResult | null;
  /** 수집 중단 + 진행 중 인코딩 중단 + 결과 폐기. 셸 `stopEncoder` 훅이 부른다. */
  stop(): void;
  stats(): TimelapseStats;
  /** 진단(Step 16). */
  encoderProbe(): EncoderProbe | null;
}

export interface TimelapseServiceDeps {
  readonly camera?: Pick<CameraService, "configureTimelapseSpool" | "onTimelapseFrame">;
  readonly encode?: typeof encodeTimelapse;
  /**
   * **`stop()`의 `abort()` 전용이다.** `finish()`는 이 값을 `encode`에 넘기지 않는다 —
   * `encodeTimelapse`가 내부에서 `getEncodeClient()` 싱글턴을 잡고, `abort()`는 **그와 같은
   * 인스턴스**를 끊어야 하기 때문이다(싱글턴으로 수렴하므로 실제 대상은 일치한다).
   * 테스트에서 인코딩 경로를 갈아끼울 때는 `client`가 아니라 `encode`를 주입한다.
   */
  readonly client?: EncodeClient;
  readonly now?: () => number;
}

function round1(value: number): number {
  return Math.round(value * 10) / 10;
}

export function createTimelapseService(deps: TimelapseServiceDeps = {}): TimelapseService {
  const camera = deps.camera ?? getCameraService();
  const encode = deps.encode ?? encodeTimelapse;
  const now = deps.now ?? (() => performance.now());

  let workspace: SessionWorkspace | null = null;
  let collecting = false;
  let startedAt = 0;
  let elapsedSec: number | null = null;
  let nextIndex = 0;
  let spooled = 0;
  let droppedSpool = 0;
  let decimations = 0;
  let intervalMs = TIMELAPSE_SPOOL_INTERVAL_MS;
  let size: { width: number; height: number } | null = null;
  let unsubscribe: (() => void) | null = null;
  /** OPFS 쓰기 인플라이트 1개 제한. 초과분은 드롭한다(프리뷰·촬영 우선). */
  let writeInFlight = false;
  /** 솎아내기 재진입 금지. */
  let decimating = false;
  let result: TimelapseResult | null = null;
  let finishing: Promise<TimelapseResult | null> | null = null;

  function applySpoolConfig(enabled: boolean): void {
    try {
      camera.configureTimelapseSpool({ enabled, intervalMs, quality: SPOOL_JPEG_QUALITY });
    } catch {
      // 카메라가 이미 멈춰 프로세서가 없을 수 있다 — 무해하게 넘긴다.
    }
  }

  async function maybeDecimate(): Promise<void> {
    if (decimating || workspace === null) return;
    if (spooled < TIMELAPSE_SPOOL_MAX_FRAMES) return;

    decimating = true;
    try {
      const names = await workspace.listTimelapseFrames();
      const plan = planDecimation(names);
      if (plan === null) return;

      for (const name of plan.remove) {
        if (await workspace.removeTimelapseFrame(name)) spooled--;
      }
      intervalMs = decimatedInterval(intervalMs);
      decimations++;
      applySpoolConfig(true);
      logger.info("타임랩스 스풀 솎아내기", {
        removed: plan.remove.length,
        kept: spooled,
        intervalMs: Math.round(intervalMs),
      });
    } finally {
      decimating = false;
    }
  }

  function onSpoolFrame(frame: { blob: Blob; width: number; height: number }): void {
    if (!collecting || workspace === null) return;
    // 인코딩 시점에는 카메라가 꺼져 있다 — 크기를 여기서 기억해 둔다.
    size = { width: frame.width, height: frame.height };

    // 백프레셔: OPFS가 못 따라가면 최신 프레임도 버린다(수집이 촬영을 방해하면 안 된다).
    if (writeInFlight) {
      droppedSpool++;
      return;
    }
    writeInFlight = true;
    const index = nextIndex++;
    const target = workspace;
    void target
      .writeTimelapseFrame(index, frame.blob)
      .then((ok) => {
        if (ok) spooled++;
        else droppedSpool++;
      })
      .catch(() => {
        // 어댑터는 던지지 않지만 이중 방어.
        droppedSpool++;
      })
      .finally(() => {
        writeInFlight = false;
        void maybeDecimate();
      });
  }

  function stopCollection(): void {
    if (!collecting) return;
    collecting = false;
    elapsedSec = (now() - startedAt) / 1000;
    unsubscribe?.();
    unsubscribe = null;
    applySpoolConfig(false);
    logger.info("타임랩스 수집 종료", {
      spooled,
      droppedSpool,
      decimations,
      elapsedSec: round1(elapsedSec),
    });
  }

  return {
    startCollection(next) {
      // 멱등 — StrictMode 이중 마운트에서 두 번 시작하지 않는다.
      if (collecting) return;

      workspace = next;
      collecting = true;
      startedAt = now();
      elapsedSec = null;
      nextIndex = 0;
      spooled = 0;
      droppedSpool = 0;
      decimations = 0;
      intervalMs = TIMELAPSE_SPOOL_INTERVAL_MS;
      size = null;
      result = null;
      writeInFlight = false;
      decimating = false;

      unsubscribe = camera.onTimelapseFrame(onSpoolFrame);
      applySpoolConfig(true);
      logger.info("타임랩스 수집 시작", {
        intervalMs: Math.round(intervalMs),
        maxFrames: TIMELAPSE_SPOOL_MAX_FRAMES,
      });
    },

    stopCollection,

    finish() {
      // [다음] 이중 클릭·동시 호출을 하나로 합류시킨다.
      if (result !== null) return Promise.resolve(result);
      if (finishing !== null) return finishing;
      if (collecting) stopCollection();

      const target = workspace;
      const seconds = elapsedSec;
      const frameSize = size;
      if (target === null || seconds === null || frameSize === null) {
        logger.warn("타임랩스 생성 건너뜀(수집 정보 없음)", {
          hasWorkspace: target !== null,
          hasElapsed: seconds !== null,
          hasSize: frameSize !== null,
        });
        return Promise.resolve(null);
      }

      finishing = encode({ workspace: target, actualSeconds: seconds, size: frameSize })
        .then((encoded) => {
          result = encoded;
          return encoded;
        })
        .catch((err: unknown) => {
          // 인코딩 실패가 [다음]을 막으면 안 된다(VF-6).
          logger.warn("타임랩스 생성 중 예외(무시하고 계속)", {
            reason: err instanceof Error ? err.message : String(err),
          });
          return null;
        })
        .finally(() => {
          finishing = null;
        });
      return finishing;
    },

    current: () => result,

    stop() {
      stopCollection();
      (deps.client ?? getEncodeClient()).abort();
      result = null;
      workspace = null;
      size = null;
      elapsedSec = null;
    },

    stats: () => ({
      collecting,
      spooled,
      droppedSpool,
      decimations,
      intervalMs,
      elapsedSec,
      size,
    }),

    encoderProbe: () => lastEncoderProbe(),
  };
}

let singleton: TimelapseService | null = null;

/** 앱 전역 타임랩스 서비스. 촬영 화면과 결과 화면이 **같은 인스턴스**를 봐야 한다. */
export function getTimelapseService(): TimelapseService {
  singleton ??= createTimelapseService();
  return singleton;
}

export function setTimelapseServiceForTests(service: TimelapseService | null): void {
  singleton = service;
}
