import {
  ENCODE_FLUSH_TIMEOUT_MS,
  type EncodeJob,
  type EncodeStats,
  type TimelapseEncodeConfig,
} from "./encodeProtocol";
import { MEDIARECORDER_MP4_MIME } from "./encoderSupport";

/**
 * 경로 A 인코딩 — `MediaRecorder` + `canvas.captureStream(0)` (04 §7.3a)
 *
 * ⚠️ **메인 스레드 전용이다.** `MediaRecorder`·`HTMLCanvasElement.captureStream`은 Window
 *    전용 인터페이스라 Worker에 없다. 이 경로는 [04 §10] "타임랩스 인코딩 = Worker" 규약의
 *    **명시된 예외**다.
 * ⚠️ **출력 길이만큼 실제 시간이 걸린다**(최대 15초). 스풀 프레임을 화면 밖 캔버스에
 *    실시간으로 재생하며 녹화하기 때문이다. 지원 매트릭스상 도달하지 않는 예비 경로다
 *    ([04 §7.3] 이유 ③).
 */

/** 캔버스+레코더 묶음. 브라우저 구현과 node 가짜가 같은 표면을 만족한다. */
export interface CanvasRecorderPort {
  /** 녹화 시작. */
  start(): void;
  /** JPEG 1장을 캔버스에 그리고 `track.requestFrame()`. 실패는 false. */
  pushFrame(blob: Blob): Promise<boolean>;
  /** 정지 후 mp4 Blob. 타임아웃·실패는 null. */
  stop(timeoutMs: number): Promise<Blob | null>;
  /** 트랙 stop + 참조 해제. **성공·실패 무관하게 반드시 호출**한다. */
  dispose(): void;
}

export interface MediaRecorderMp4Deps {
  readonly loadFrame: (name: string) => Promise<Blob | null>;
  readonly createPort: (config: TimelapseEncodeConfig) => CanvasRecorderPort | null;
  readonly now: () => number;
  readonly delay: (ms: number) => Promise<void>;
  readonly stopTimeoutMs?: number;
}

export type MediaRecorderMp4Result =
  | { readonly ok: true; readonly blob: Blob; readonly stats: EncodeStats }
  | { readonly ok: false; readonly reason: string; readonly stats: EncodeStats };

export async function encodeWithMediaRecorder(
  job: EncodeJob,
  deps: MediaRecorderMp4Deps,
): Promise<MediaRecorderMp4Result> {
  const startedAt = deps.now();
  let encoded = 0;
  let skipped = 0;

  const stats = (): EncodeStats => ({
    encodedFrames: encoded,
    // 경로 A에는 인코더 큐가 노출되지 않아 백프레셔 드롭 개념이 없다.
    droppedFrames: 0,
    skippedFrames: skipped,
    elapsedMs: Math.round(deps.now() - startedAt),
  });

  const port = deps.createPort(job.config);
  if (port === null) {
    return { ok: false, reason: "캔버스 녹화를 시작할 수 없습니다", stats: stats() };
  }

  try {
    port.start();
    // 페이싱 기준점은 녹화 시작 직후다. 프레임 로드 시간이 여기에 포함돼야
    // 실경과와 타임라인이 어긋나지 않는다.
    const pacingStartedAt = deps.now();

    for (let i = 0; i < job.names.length; i++) {
      const blob = await deps.loadFrame(job.names[i]!);
      if (blob === null) {
        skipped++;
        continue;
      }
      if (!(await port.pushFrame(blob))) {
        skipped++;
        continue;
      }
      encoded++;

      // **실경과 기준 페이싱**(WM3와 동종) — tick을 누적하면 탭 스로틀에서 길이가 어긋난다.
      // 뒤처졌으면 기다리지 않고 곧바로 다음 프레임으로 간다.
      const nextUs = job.timestampsUs[i + 1] ?? (job.timestampsUs[i] ?? 0) + job.frameDurationUs;
      const wait = pacingStartedAt + nextUs / 1000 - deps.now();
      if (wait > 0) await deps.delay(wait);
    }

    if (encoded === 0) {
      return { ok: false, reason: "녹화된 프레임이 없습니다", stats: stats() };
    }

    const out = await port.stop(deps.stopTimeoutMs ?? ENCODE_FLUSH_TIMEOUT_MS);
    if (out === null) {
      return { ok: false, reason: "레코더 정지 실패", stats: stats() };
    }
    return { ok: true, blob: out, stats: stats() };
  } catch (err) {
    return {
      ok: false,
      reason: err instanceof Error ? err.message : String(err),
      stats: stats(),
    };
  } finally {
    // 트랙을 놓지 않으면 캔버스 스트림이 살아 있어 다음 세션에서 자원이 샌다.
    port.dispose();
  }
}

/**
 * 브라우저 구현. 미지원·예외는 **null**(호출측이 타임랩스를 포기한다).
 *
 * 캔버스를 `document`에 붙이지 않는다 — 레이아웃·페인트 비용을 0으로 두기 위해서다.
 */
export function createCanvasRecorderPort(
  config: TimelapseEncodeConfig,
): CanvasRecorderPort | null {
  if (typeof document === "undefined" || typeof MediaRecorder === "undefined") return null;

  let canvas: HTMLCanvasElement | null = null;
  let stream: MediaStream | null = null;
  let recorder: MediaRecorder | null = null;
  const chunks: Blob[] = [];

  try {
    canvas = document.createElement("canvas");
    canvas.width = config.width;
    canvas.height = config.height;
    const ctx = canvas.getContext("2d", { alpha: false });
    if (ctx === null) return null;

    // fps 0 = 자동 발행 없음. 프레임마다 `requestFrame()`으로 우리가 시점을 정한다.
    stream = canvas.captureStream(0);
    const track = stream.getVideoTracks()[0] as (CanvasCaptureMediaStreamTrack | undefined);
    if (track === undefined) return null;

    recorder = new MediaRecorder(stream, {
      mimeType: MEDIARECORDER_MP4_MIME,
      videoBitsPerSecond: config.bitrate,
    });
    recorder.addEventListener("dataavailable", (event) => {
      if (event.data.size > 0) chunks.push(event.data);
    });

    const activeRecorder = recorder;
    const activeStream = stream;

    return {
      start() {
        activeRecorder.start();
      },

      async pushFrame(blob) {
        let bitmap: ImageBitmap | null = null;
        try {
          bitmap = await createImageBitmap(blob);
          // 소스가 config보다 크면 우/하단이 잘린다(짝수 클램프와 같은 결과).
          ctx.drawImage(bitmap, 0, 0);
          track.requestFrame();
          return true;
        } catch {
          return false;
        } finally {
          bitmap?.close();
        }
      },

      stop(timeoutMs) {
        return new Promise<Blob | null>((resolve) => {
          if (activeRecorder.state === "inactive") {
            resolve(chunks.length > 0 ? new Blob(chunks, { type: "video/mp4" }) : null);
            return;
          }
          // 정지 실패는 강제 종료한다(04 §7.5) — 영구 대기를 만들지 않는다.
          const timer = setTimeout(() => resolve(null), timeoutMs);
          activeRecorder.addEventListener(
            "stop",
            () => {
              clearTimeout(timer);
              resolve(chunks.length > 0 ? new Blob(chunks, { type: "video/mp4" }) : null);
            },
            { once: true },
          );
          try {
            activeRecorder.stop();
          } catch {
            clearTimeout(timer);
            resolve(null);
          }
        });
      },

      dispose() {
        try {
          if (activeRecorder.state !== "inactive") activeRecorder.stop();
        } catch {
          // 이미 멈춘 레코더 — 무해하다.
        }
        try {
          activeStream.getTracks().forEach((t) => t.stop());
        } catch {
          // 트랙 해제 실패도 흐름을 막지 않는다.
        }
        chunks.length = 0;
        canvas = null;
        stream = null;
        recorder = null;
      },
    };
  } catch {
    // 생성 도중 어느 단계가 실패해도 자원을 남기지 않는다.
    stream?.getTracks().forEach((t) => t.stop());
    canvas = null;
    stream = null;
    recorder = null;
    return null;
  }
}
