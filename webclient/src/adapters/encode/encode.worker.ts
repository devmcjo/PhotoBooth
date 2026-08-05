/// <reference lib="webworker" />
/**
 * 타임랩스 인코딩 Worker(경로 B) — 04 §7.3c·§10
 *
 * 375프레임 JPEG 디코딩을 메인에서 하면 결과 화면이 수 초간 얼어붙는다. 그래서 디코딩·
 * 인코딩·muxing 전부를 여기서 한다.
 *
 * ⚠️ **여기가 `mp4-muxer`를 import하는 유일한 파일이다.** 코어(`webCodecsMp4.ts`)는 포트만
 *    받으므로 node에서 전량 검증되고, muxer 교체 비용도 이 파일에 갇힌다.
 * ⚠️ **OPFS를 읽기만 한다.** 쓰기는 `opfsWriter` Worker 전용이다 — 다른 곳에서 쓰면 iOS에서
 *    전 저장 경로가 실패한다(05 §3.1).
 * ⚠️ **로그를 남기지 않는다.** Worker에는 로그 스토어가 붙지 않아 여기서 남긴 로그는
 *    진단·내보내기에 영원히 도달하지 않는다. 사유는 `failed.reason`으로 메인에 넘긴다.
 */
import { ArrayBufferTarget, Muxer } from "mp4-muxer";
import {
  type EncodeRequest,
  type EncodeResponse,
  type EncodeStats,
  type TimelapseEncodeConfig,
} from "./encodeProtocol";
import {
  encodeWithWebCodecs,
  type EncodableFrame,
  type Mp4MuxerLike,
  type VideoEncoderLike,
} from "./webCodecsMp4";

/** 인코딩용 캔버스 1개를 **재사용**한다(프레임마다 만들면 GC 압력으로 느려진다). */
let canvas: OffscreenCanvas | null = null;
let ctx: OffscreenCanvasRenderingContext2D | null = null;

function ensureCanvas(width: number, height: number): OffscreenCanvasRenderingContext2D | null {
  if (canvas === null) {
    canvas = new OffscreenCanvas(width, height);
    ctx = canvas.getContext("2d", { alpha: false });
  } else if (canvas.width !== width || canvas.height !== height) {
    canvas.width = width;
    canvas.height = height;
  }
  return ctx;
}

/** 스풀 디렉터리 핸들을 **1회만** 연다(프레임마다 루트부터 걸으면 375회 순회가 된다). */
async function openSpoolDir(dirPath: string): Promise<FileSystemDirectoryHandle | null> {
  try {
    let dir = await navigator.storage.getDirectory();
    for (const segment of dirPath.split("/").filter((s) => s.length > 0)) {
      dir = await dir.getDirectoryHandle(segment);
    }
    return dir;
  } catch {
    return null;
  }
}

function createBrowserEncoder(handlers: {
  output: (chunk: unknown, meta: unknown) => void;
  error: (reason: string) => void;
}): VideoEncoderLike {
  const encoder = new VideoEncoder({
    output: (chunk, meta) => handlers.output(chunk, meta),
    error: (err: unknown) => handlers.error(err instanceof Error ? err.message : String(err)),
  });
  return {
    get encodeQueueSize() {
      return encoder.encodeQueueSize;
    },
    get state() {
      return encoder.state as string;
    },
    configure: (config) => encoder.configure(config),
    encode: (frame, options) => encoder.encode(frame as unknown as VideoFrame, options),
    flush: () => encoder.flush(),
    close: () => encoder.close(),
  };
}

function createBrowserMuxer(config: TimelapseEncodeConfig): Mp4MuxerLike {
  const target = new ArrayBufferTarget();
  const muxer = new Muxer({
    target,
    // ⚠️ `frameRate`를 **넘기지 않는다.** 넘기면 muxer가 타임스탬프를 그 격자로 반올림하는데,
    //    스풀이 부족한 세션의 프레임 간격은 33333μs가 아니라서 여러 프레임이 같은 격자로
    //    뭉개져 컨테이너 길이가 망가진다.
    video: { codec: "avc", width: config.width, height: config.height },
    fastStart: "in-memory",
  });
  return {
    addVideoChunk: (chunk, meta) =>
      muxer.addVideoChunk(
        chunk as EncodedVideoChunk,
        meta as EncodedVideoChunkMetadata | undefined,
      ),
    finalize: () => muxer.finalize(),
    buffer: () => target.buffer,
  };
}

async function run(request: EncodeRequest): Promise<EncodeResponse> {
  const { id, job } = request;
  const emptyStats: EncodeStats = {
    encodedFrames: 0,
    droppedFrames: 0,
    skippedFrames: 0,
    elapsedMs: 0,
  };

  if (typeof VideoEncoder === "undefined") {
    return { type: "failed", id, reason: "이 환경에 VideoEncoder가 없습니다", stats: emptyStats };
  }

  const dir = await openSpoolDir(job.dirPath);
  if (dir === null) {
    return { type: "failed", id, reason: "스풀 디렉터리를 열 수 없습니다", stats: emptyStats };
  }

  const result = await encodeWithWebCodecs(job, {
    async loadFrame(name) {
      try {
        const handle = await dir.getFileHandle(name);
        return await handle.getFile();
      } catch {
        // 솎아내기와 경쟁해 파일이 사라졌을 수 있다 — 그 프레임만 건너뛴다.
        return null;
      }
    },

    async createFrame(blob, init) {
      let bitmap: ImageBitmap | null = null;
      try {
        bitmap = await createImageBitmap(blob);
        const context = ensureCanvas(init.width, init.height);
        if (context === null) return null;
        // 소스가 config보다 1px 큰 경우 우/하단이 잘린다 — Windows의
        // `crop=trunc(iw/2)*2:trunc(ih/2)*2`와 같은 결과다(좌상단 기준).
        context.drawImage(bitmap, 0, 0);
        return new VideoFrame(canvas as unknown as CanvasImageSource, {
          timestamp: init.timestampUs,
          duration: init.durationUs,
        }) as unknown as EncodableFrame;
      } catch {
        return null;
      } finally {
        // `ImageBitmap`은 GC 대상이 아니다 — 반드시 닫는다.
        bitmap?.close();
      }
    },

    createEncoder: createBrowserEncoder,
    createMuxer: createBrowserMuxer,
    now: () => performance.now(),
  });

  if (!result.ok) {
    return { type: "failed", id, reason: result.reason, stats: result.stats };
  }
  return { type: "done", id, buffer: result.output.buffer, stats: result.output.stats };
}

self.addEventListener("message", (event: MessageEvent<EncodeRequest>) => {
  const request = event.data;
  if (request.type !== "encode") return;

  void run(request).then(
    (response) => {
      // 완성 mp4는 **소유권을 이전**해 복사를 피한다(수 MB).
      if (response.type === "done") {
        self.postMessage(response, { transfer: [response.buffer] });
      } else {
        self.postMessage(response);
      }
    },
    (err: unknown) => {
      // `run`은 던지지 않도록 짜여 있지만, 던지면 메인이 60초 타임아웃까지 기다리게 된다.
      const failed: EncodeResponse = {
        type: "failed",
        id: request.id,
        reason: err instanceof Error ? err.message : String(err),
        stats: { encodedFrames: 0, droppedFrames: 0, skippedFrames: 0, elapsedMs: 0 },
      };
      self.postMessage(failed);
    },
  );
});
