import { logger } from "@adapters/storage/logStore";
import type { FramePayload, FrameSource, ProcessedSize } from "./cameraTypes";

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

/** 숨겨진 `<video>`를 만든다. 규격 속성이 빠지면 모바일에서 깨지므로 한 곳에서 만든다. */
export function createHiddenVideoElement(doc: Document = document): HTMLVideoElement {
  const video = doc.createElement("video");
  video.autoplay = true;
  video.muted = true;
  video.playsInline = true;
  video.setAttribute("playsinline", ""); // 구형 iOS는 속성 형태만 인식한다
  video.setAttribute("disablepictureinpicture", "");
  video.style.display = "none";
  return video;
}

/** `VideoFrame`(WebCodecs)이 있으면 zero-copy로 넘긴다. 없으면 `createImageBitmap` 폴백. */
function hasVideoFrame(): boolean {
  return typeof VideoFrame !== "undefined";
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
      if (hasVideoFrame()) {
        emit(new VideoFrame(video));
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
    async attach(stream) {
      video.srcObject = stream;
      if (video.parentNode === null) {
        // DOM에 붙어 있어야 일부 브라우저에서 재생이 시작된다.
        doc.body.appendChild(video);
      }
      try {
        // `autoplay`가 실패할 수 있으므로 명시 호출한다.
        await video.play();
      } catch (err) {
        logger.warn("video.play() 실패 — 제스처 컨텍스트에서 재시도 필요", {
          reason: err instanceof Error ? err.message : String(err),
        });
        return false;
      }

      running = true;
      lastMediaTime = -1;
      if (withCallback.requestVideoFrameCallback !== undefined) loopWithRvfc();
      else loopWithRaf();
      return true;
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
  };
}
