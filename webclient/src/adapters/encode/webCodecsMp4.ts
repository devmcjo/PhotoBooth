import {
  TIMELAPSE_ENCODE_QUEUE_LIMIT,
  TIMELAPSE_OUTPUT_FPS,
} from "@domain/capture/timelapsePlan";
import {
  ENCODE_FLUSH_TIMEOUT_MS,
  type EncodeJob,
  type EncodeStats,
  type TimelapseEncodeConfig,
} from "./encodeProtocol";

/**
 * 경로 B 인코딩 코어 — 04 §7.3c·§7.5
 *
 * ⚠️ **이 파일은 브라우저 API를 하나도 직접 부르지 않는다.** 디코딩·인코딩·muxing·파일 읽기가
 *    전부 포트로 들어온다. 그래야 타임스탬프·백프레셔·자원 해제·실패 경로를 node에서 전량
 *    검증할 수 있다(15 §3.1 "순수 코어 + 얇은 래퍼").
 * ⚠️ MP4 muxer 패키지를 **import하지 않는다.** 번들링 위험을 `encode.worker.ts` 한 파일에 가둔다.
 * ⚠️ 이 코어는 Worker 안에서 돈다 → **로그를 남기지 않는다.** Worker의 로그는 진단 스토어에
 *    영원히 도달하지 않는다. 사유를 `reason`으로 돌려주고 기록은 메인이 한다.
 * ⚠️ 어떤 입력·실패에도 **throw하지 않는다**(01 §2.1).
 */

/** 인코더에 넣을 프레임 1장. 실제 타입은 브라우저의 `VideoFrame`이다. */
export interface EncodableFrame {
  close(): void;
}

export interface VideoEncoderLike {
  readonly encodeQueueSize: number;
  /** `"unconfigured" | "configured" | "closed"`. */
  readonly state: string;
  configure(config: VideoEncoderConfig): void;
  encode(frame: EncodableFrame, options?: { keyFrame?: boolean }): void;
  flush(): Promise<void>;
  close(): void;
}

export interface Mp4MuxerLike {
  addVideoChunk(chunk: unknown, meta?: unknown): void;
  finalize(): void;
  /** `finalize()` 후의 완성 버퍼. */
  buffer(): ArrayBuffer;
}

export interface WebCodecsMp4Deps {
  /** 스풀 프레임 1장을 읽어온다. 부재·실패는 null. */
  readonly loadFrame: (name: string) => Promise<Blob | null>;
  /**
   * Blob → 인코딩 가능한 프레임. **짝수 크기로 맞춰 그리는 책임이 여기 있다.**
   * 실패는 null(예외 금지).
   */
  readonly createFrame: (
    blob: Blob,
    init: { timestampUs: number; durationUs: number; width: number; height: number },
  ) => Promise<EncodableFrame | null>;
  readonly createEncoder: (handlers: {
    output: (chunk: unknown, meta: unknown) => void;
    error: (reason: string) => void;
  }) => VideoEncoderLike;
  readonly createMuxer: (config: TimelapseEncodeConfig) => Mp4MuxerLike;
  /** 경과 계측. 시각은 도메인과 같은 규칙으로 **주입**한다(15 §3.2). */
  readonly now: () => number;
  readonly flushTimeoutMs?: number;
}

export interface WebCodecsMp4Output {
  readonly buffer: ArrayBuffer;
  readonly stats: EncodeStats;
}

export type WebCodecsMp4Result =
  | { readonly ok: true; readonly output: WebCodecsMp4Output }
  | { readonly ok: false; readonly reason: string; readonly stats: EncodeStats };

function describe(err: unknown): string {
  return err instanceof Error ? err.message : String(err);
}

/** 성공하면 버퍼, 어떤 실패든 `{ ok:false, reason, stats }`. **절대 throw하지 않는다.** */
export async function encodeWithWebCodecs(
  job: EncodeJob,
  deps: WebCodecsMp4Deps,
): Promise<WebCodecsMp4Result> {
  const startedAt = deps.now();
  let encoded = 0;
  let dropped = 0;
  let skipped = 0;

  const stats = (): EncodeStats => ({
    encodedFrames: encoded,
    droppedFrames: dropped,
    skippedFrames: skipped,
    elapsedMs: Math.round(deps.now() - startedAt),
  });

  const { config } = job;

  let muxer: Mp4MuxerLike;
  try {
    muxer = deps.createMuxer(config);
  } catch (err) {
    return { ok: false, reason: `muxer 생성 실패: ${describe(err)}`, stats: stats() };
  }

  /** 비동기 오류 콜백·muxing 실패를 담는다. **처음 것만** 남긴다(원인이 첫 실패이므로). */
  let failure: string | null = null;

  let encoder: VideoEncoderLike;
  try {
    encoder = deps.createEncoder({
      output: (chunk, meta) => {
        try {
          muxer.addVideoChunk(chunk, meta);
        } catch (err) {
          failure ??= `muxing 실패: ${describe(err)}`;
        }
      },
      error: (reason) => {
        failure ??= reason;
      },
    });
  } catch (err) {
    return { ok: false, reason: `인코더 생성 실패: ${describe(err)}`, stats: stats() };
  }

  try {
    try {
      encoder.configure({
        codec: config.codec,
        width: config.width,
        height: config.height,
        bitrate: config.bitrate,
        framerate: config.framerate,
        latencyMode: "quality",
        avc: { format: "avc" },
      });
    } catch (err) {
      return { ok: false, reason: `인코더 설정 거부: ${describe(err)}`, stats: stats() };
    }

    for (let i = 0; i < job.names.length; i++) {
      // 비동기 오류가 한 번이라도 나면 남은 프레임을 밀어 넣어도 결과가 깨진다.
      if (failure !== null) break;

      const name = job.names[i]!;
      const blob = await deps.loadFrame(name);
      if (blob === null) {
        skipped++;
        continue;
      }

      const frame = await deps.createFrame(blob, {
        timestampUs: job.timestampsUs[i] ?? i * job.frameDurationUs,
        durationUs: job.frameDurationUs,
        width: config.width,
        height: config.height,
      });
      if (frame === null) {
        skipped++;
        continue;
      }

      try {
        // 백프레셔(04 §7.5) — 큐가 밀리면 이 프레임을 버린다. 드롭해도 **출력 길이는 유지된다**:
        // 타임스탬프가 인덱스로 고정돼 있어 직전 프레임의 표시 시간이 그만큼 늘어날 뿐이다.
        if (encoder.encodeQueueSize > TIMELAPSE_ENCODE_QUEUE_LIMIT) {
          dropped++;
          continue;
        }
        // 1초마다 키프레임. **첫 프레임은 반드시 키프레임**이어야 재생이 시작된다.
        encoder.encode(frame, { keyFrame: encoded % TIMELAPSE_OUTPUT_FPS === 0 });
        encoded++;
      } catch (err) {
        failure ??= `인코딩 실패: ${describe(err)}`;
      } finally {
        // `VideoFrame`은 GC 대상이 아니다(WR8) — 드롭·예외 경로에서도 반드시 닫는다.
        frame.close();
      }
    }

    // 원인을 먼저 본다. 오류로 루프가 끊겨 인코딩 수가 0이 된 경우 "프레임이 없습니다"로
    // 보고하면 **진짜 사유가 사라진다**(05 §7.2가 요구하는 "실패 사유"가 무의미해진다).
    if (failure !== null) {
      return { ok: false, reason: failure, stats: stats() };
    }
    if (encoded === 0) {
      return { ok: false, reason: "인코딩된 프레임이 없습니다", stats: stats() };
    }

    const flushFailure = await flushWithTimeout(
      encoder,
      deps.flushTimeoutMs ?? ENCODE_FLUSH_TIMEOUT_MS,
    );
    if (flushFailure !== null) {
      return { ok: false, reason: flushFailure, stats: stats() };
    }
    if (failure !== null) {
      return { ok: false, reason: failure, stats: stats() };
    }

    // `finalize()`는 **반드시 `flush()` 뒤**다. 앞서면 마지막 chunk들이 빠진 채 moov가 닫힌다.
    muxer.finalize();
    return { ok: true, output: { buffer: muxer.buffer(), stats: stats() } };
  } catch (err) {
    return { ok: false, reason: describe(err), stats: stats() };
  } finally {
    // 성공·실패·타임아웃 무관하게 인코더를 놓는다(하드웨어 인코더 점유 해제).
    if (encoder.state !== "closed") {
      try {
        encoder.close();
      } catch {
        // 이미 닫힌 인코더를 닫으면 던지는 구현이 있다 — 무해하게 넘긴다.
      }
    }
  }
}

/** `flush()`를 타임아웃과 경주시킨다. 성공이면 null, 실패면 사유 문자열. */
function flushWithTimeout(encoder: VideoEncoderLike, timeoutMs: number): Promise<string | null> {
  let pending: Promise<void>;
  try {
    pending = encoder.flush();
  } catch (err) {
    return Promise.resolve(`인코더 flush 실패: ${describe(err)}`);
  }

  return new Promise<string | null>((resolve) => {
    const timer = setTimeout(() => resolve("인코더 flush 타임아웃"), timeoutMs);
    pending.then(
      () => {
        clearTimeout(timer);
        resolve(null);
      },
      (err: unknown) => {
        clearTimeout(timer);
        resolve(`인코더 flush 실패: ${describe(err)}`);
      },
    );
  });
}
