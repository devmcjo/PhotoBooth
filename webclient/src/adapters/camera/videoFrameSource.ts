import { logger } from "@adapters/storage/logStore";
import type {
  FramePayload,
  FrameSource,
  FrameSourceAttachResult,
  FrameTransferMode,
  ProcessedSize,
} from "./cameraTypes";

/**
 * `<video>` + 프레임 도착 루프 — 04 §2.2·§2.4
 *
 * ⚠️ **`playsinline`이 없으면 iOS에서 전체화면 재생으로 강제 전환**되어 파이프라인이 깨진다.
 * ⚠️ `<video>`는 **숨긴다.** 프리뷰는 가공된 canvas를 보여준다(WM1) — `<video>`를 직접
 *    보여주면 거울·크롭이 반영되지 않는다.
 * ⚠️ `rVFC`가 없으면 rAF로 폴백하되 **`mediaTime`/`currentTime` 중복을 스킵**한다
 *    (rAF는 프레임이 안 바뀌어도 호출된다 — 같은 프레임을 두 번 가공하면 fps 계산이 부풀고
 *     타임랩스에 중복 프레임이 들어간다).
 */

/**
 * `requestVideoFrameCallback`을 **옵셔널로** 다룬다.
 * TS DOM lib은 이것을 필수 멤버로 선언하지만 Safari 15.4 미만에는 실제로 없다 —
 * 타입을 믿고 분기를 빼면 그 기기에서 프레임 루프가 시작되지 않는다(런타임 감지가 진실원).
 */
interface VideoFrameCallbackApi {
  requestVideoFrameCallback?: (
    callback: (now: number, metadata: { mediaTime: number }) => void,
  ) => number;
  cancelVideoFrameCallback?: (handle: number) => void;
}

/**
 * 숨겨진 `<video>`를 만든다. 규격 속성이 빠지면 모바일에서 깨지므로 한 곳에서 만든다.
 *
 * ⚠️ **`display: none`을 쓰지 마라**(2026-08-06 교정 · 정적 검사 CAM-3).
 *    WebKit은 렌더링 트리에서 빠진 `<video>`에 대해 `requestVideoFrameCallback`을 발화하지
 *    않는 경우가 있다. 그러면 프레임이 **한 장도 오지 않아** 8초 뒤 Ready 타임아웃이 나고,
 *    화면에는 권한 문제와 구분되지 않는 실패 문구만 뜬다.
 *    대신 **1×1 투명 고정 배치**로 "레이아웃에는 있으나 보이지 않게" 만든다 — 렌더링 트리에
 *    남아 있어야 프레임 콜백이 돈다.
 */
export function createHiddenVideoElement(doc: Document = document): HTMLVideoElement {
  const video = doc.createElement("video");
  video.autoplay = true;
  video.muted = true;
  video.playsInline = true;
  video.setAttribute("playsinline", ""); // 구형 iOS는 속성 형태만 인식한다
  video.setAttribute("disablepictureinpicture", "");
  // 손님·스크린리더에게는 존재하지 않는 요소다(프리뷰는 가공된 canvas가 보여준다 — WM1).
  video.setAttribute("aria-hidden", "true");
  video.style.position = "fixed";
  video.style.top = "0";
  video.style.left = "0";
  video.style.width = "1px";
  video.style.height = "1px";
  video.style.opacity = "0";
  video.style.pointerEvents = "none";
  video.style.zIndex = "-1";
  return video;
}

/**
 * `VideoFrame` 전달 경로의 상태 — **모듈 레벨**이며 전이는 단방향이다(2026-08-07 신설).
 *
 * ```
 *   unprobed ──프로브 true──▶ videoFrame ──런타임 실패 1회──▶ imageBitmapDemoted
 *       │                                                          │
 *       └──── false ────▶ imageBitmap              (되돌아가지 않는다)
 * ```
 *
 * ⚠️ **모듈 레벨이어야 한다.** `createVideoFrameSource()`는 카메라를 열 때마다 새로 불리므로
 *    소스 인스턴스에 두면 강등이 재시작마다 초기화된다 — 못 하던 기기가 갑자기 하게 된다.
 *    `frameThumbnails.ts`의 resize 프로브 캐시가 같은 선례다.
 * ⚠️ **되돌아가는 전이가 없다** = "프레임마다 재시도해서 매번 실패"가 구조적으로 불가능하다.
 */
type VideoFramePathState = "unprobed" | FrameTransferMode;

let videoFramePath: VideoFramePathState = "unprobed";

/** 테스트용 상태 리셋. 운영 경로에서는 부르지 않는다(`resetThumbnailProbeForTests`와 같은 형태). */
export function resetVideoFramePathForTests(): void {
  videoFramePath = "unprobed";
}

/**
 * `VideoFrame`을 1×1 캔버스로 **실제로 하나 만들어** 본다 —
 * 대조군은 `frameProcessorClient.isWorkerPipelineSupported()`다(존재 검사만으로는 부족하다).
 *
 * ⚠️ **`<video>`로 프로브하지 마라.** 재생 시작 전 `<video>`로 `VideoFrame`을 만들면 지원하는
 *    브라우저에서도 던져 **거짓 음성**이 되고 zero-copy 경로를 영구히 잃는다.
 *    캔버스가 유일하게 안전한 입력이다(캔버스 소스는 `timestamp`가 필수다).
 */
function probeVideoFrame(doc: Document): boolean {
  if (typeof VideoFrame === "undefined") return false;
  try {
    const canvas = doc.createElement("canvas");
    canvas.width = 1;
    canvas.height = 1;
    const frame = new VideoFrame(canvas, { timestamp: 0 });
    // ⚠️ `VideoFrame`은 GC 대상이 아니다 — 프로브가 만든 것도 반드시 닫는다(04 §2.4).
    frame.close();
    return true;
  } catch {
    return false;
  }
}

/** 이 프레임을 `VideoFrame`으로 넘길 수 있는가. 첫 호출에서 실증 프로브가 돈다. */
function videoFramePathUsable(doc: Document): boolean {
  if (videoFramePath === "unprobed") {
    videoFramePath = probeVideoFrame(doc) ? "videoFrame" : "imageBitmap";
    if (videoFramePath === "imageBitmap") {
      logger.info("VideoFrame 실증 프로브 실패 — ImageBitmap 경로로 시작한다");
    }
  }
  return videoFramePath === "videoFrame";
}

/**
 * 런타임 실패 1회에서 **영구 강등**한다(생성자는 통과했는데 Worker transfer가 터지는 경우 포함).
 *
 * 로그는 **1회만** 남긴다 — 초당 30회 경고가 로그 링버퍼를 태우면 정작 필요한 기록이 밀려난다.
 */
function demoteVideoFramePath(err: unknown): void {
  if (videoFramePath === "imageBitmapDemoted") return;
  videoFramePath = "imageBitmapDemoted";
  logger.warn("VideoFrame 전달 실패 — ImageBitmap 경로로 영구 강등", {
    name: err instanceof Error ? err.name : "",
  });
}

/**
 * transfer에 성공한 프레임은 detach되어 `close()`가 던질 수 있다 — 삼킨다.
 * 실패해 남은 프레임을 닫지 않으면 그대로 누수다(`VideoFrame`은 GC 대상이 아니다).
 */
function closeQuietly(frame: VideoFrame | null): void {
  try {
    frame?.close();
  } catch {
    /* 이미 detach됐다 */
  }
}

export function createVideoFrameSource(
  video: HTMLVideoElement = createHiddenVideoElement(),
  doc: Document = document,
): FrameSource {
  const withCallback = video as unknown as VideoFrameCallbackApi;
  const listeners = new Set<(payload: FramePayload) => void>();
  let running = false;
  let rafHandle: number | null = null;
  let rvfcHandle: number | null = null;
  let lastMediaTime = -1;
  /** 이전 프레임 변환이 끝나지 않았으면 새 프레임을 만들지 않는다(큐 폭주 방지). */
  let converting = false;

  function emit(payload: FramePayload): void {
    if (listeners.size === 0) {
      payload.close();
      return;
    }
    // 소비자는 하나(가공 Worker)를 전제한다 — 여러 소비자가 소유권을 나눌 수 없다.
    for (const listener of listeners) {
      listener(payload);
      break;
    }
  }

  async function grab(mediaTime: number): Promise<void> {
    if (converting) return;
    // 같은 프레임을 두 번 가공하지 않는다.
    if (mediaTime === lastMediaTime) return;
    lastMediaTime = mediaTime;

    converting = true;
    try {
      if (videoFramePathUsable(doc)) {
        /*
         * ⚠️ `emit()` 안에서 던질 수 있다(Worker `postMessage(..., {transfer})` 실패).
         *    예전에는 그 예외가 아래 catch로 흘러 warn만 남기고 **매 프레임 같은 실패를
         *    반복**했다 — `createImageBitmap` 폴백으로 절대 내려가지 않았다.
         *    게다가 만들어진 프레임이 닫히지 않아 소유권까지 샜다.
         */
        let frame: VideoFrame | null = null;
        try {
          frame = new VideoFrame(video);
          emit(frame);
          frame = null; // 정상: 소유권이 소비자에게 넘어갔다
        } catch (err) {
          closeQuietly(frame);
          demoteVideoFramePath(err);
          return; // 이 프레임 1장은 버린다 — 다음 프레임부터 비트맵 경로
        }
      } else {
        emit(await createImageBitmap(video));
      }
    } catch (err) {
      // 트랙이 끊긴 직후 등. 루프를 죽이지 않는다.
      logger.warn("프레임 획득 실패", {
        reason: err instanceof Error ? err.message : String(err),
      });
    } finally {
      converting = false;
    }
  }

  function loopWithRvfc(): void {
    if (!running || withCallback.requestVideoFrameCallback === undefined) return;
    rvfcHandle = withCallback.requestVideoFrameCallback((_now, metadata) => {
      void grab(metadata.mediaTime);
      loopWithRvfc();
    });
  }

  function loopWithRaf(): void {
    if (!running) return;
    rafHandle = requestAnimationFrame(() => {
      void grab(video.currentTime);
      loopWithRaf();
    });
  }

  return {
    async attach(stream): Promise<FrameSourceAttachResult> {
      video.srcObject = stream;
      if (video.parentNode === null) {
        // DOM에 붙어 있어야 일부 브라우저에서 재생이 시작된다.
        doc.body.appendChild(video);
      }
      try {
        // `autoplay`가 실패할 수 있으므로 명시 호출한다.
        await video.play();
      } catch (err) {
        /*
         * ⚠️ **`name`을 돌려준다.** 전에는 `message`만 로그로 남기고 `name`을 버려서,
         *    호출측이 이 실패를 권한 실패와 같은 `unknown`으로 보고할 수밖에 없었다.
         *    iOS에서 실제로 나오는 것: `NotAllowedError`(자동재생 정책) · `AbortError`(로드 중단).
         */
        const errorName = err instanceof Error ? err.name : "";
        logger.warn("video.play() 실패 — 제스처 컨텍스트에서 재시도 필요", {
          name: errorName,
          reason: err instanceof Error ? err.message : String(err),
        });
        return { ok: false, errorName };
      }

      running = true;
      lastMediaTime = -1;
      if (withCallback.requestVideoFrameCallback !== undefined) loopWithRvfc();
      else loopWithRaf();
      return { ok: true };
    },

    onFrame(listener) {
      listeners.add(listener);
      return () => listeners.delete(listener);
    },

    detach() {
      running = false;
      if (rvfcHandle !== null && withCallback.cancelVideoFrameCallback !== undefined) {
        withCallback.cancelVideoFrameCallback(rvfcHandle);
      }
      if (rafHandle !== null) cancelAnimationFrame(rafHandle);
      rvfcHandle = null;
      rafHandle = null;
      listeners.clear();
      video.srcObject = null;
      video.remove();
    },

    size(): ProcessedSize {
      return { width: video.videoWidth, height: video.videoHeight };
    },

    /**
     * 아직 프로브 전이면 `imageBitmap`으로 **보수적으로** 보고한다 —
     * 프레임이 한 장도 오지 않은 상태에서 zero-copy를 주장하면 진단이 거짓을 말한다.
     */
    transferMode(): FrameTransferMode {
      return videoFramePath === "unprobed" ? "imageBitmap" : videoFramePath;
    },
  };
}
