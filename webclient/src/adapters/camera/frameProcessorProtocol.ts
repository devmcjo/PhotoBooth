/** 프레임 가공 Worker 메시지 프로토콜 — 04 §4 */

export type FrameProcessorRequest =
  | { readonly type: "configure"; readonly targetAspect: number; readonly mirror: boolean }
  | { readonly type: "frame"; readonly payload: ImageBitmap | VideoFrame }
  | { readonly type: "bindPreview"; readonly canvas: OffscreenCanvas }
  | {
      /**
       * 프리뷰 **비트맵 채널** on/off — 2026-08-06 신설(04 §2.3.1의 미구현 폴백).
       *
       * `transferControlToOffscreen()`이 없거나 던지는 브라우저에서는 캔버스 이관이 불가하다.
       * 그때 Worker가 프레임마다 비트맵을 메인으로 보내고 메인이 `drawImage`로 그린다.
       *
       * ⚠️ **이관에 성공했으면 켜지 않는다.** 둘 다 켜면 같은 프레임을 두 번 그리고
       *    비트맵 복사 비용만 늘어난다.
       */
      readonly type: "previewChannel";
      readonly enabled: boolean;
    }
  | { readonly type: "requestStill"; readonly id: number; readonly quality: number }
  | {
      /**
       * 타임랩스 스풀 채널 on/off — 04 §7.2
       *
       * ⚠️ **스틸 채널을 15fps로 재사용하면 안 된다.** `pendingStill`은 1개짜리 덮어쓰기 슬롯이라
       *    컷 촬영 요청과 한 프레임 간격 안에서 충돌하면 **먼저 온 요청이 소멸**하고, 그것이 컷이면
       *    5초 타임아웃 뒤 `null`이 되어 세션이 홈으로 강제 복귀한다. 전용 채널로 분리한다.
       */
      readonly type: "configureSpool";
      readonly enabled: boolean;
      readonly intervalMs: number;
      readonly quality: number;
    }
  | { readonly type: "reset" };

export type FrameProcessorResponse =
  | { readonly type: "processed"; readonly width: number; readonly height: number }
  | {
      readonly type: "still";
      readonly id: number;
      readonly blob: Blob | null;
      readonly error?: string;
    }
  | {
      /** 스풀 프레임 1장. 메인이 OPFS(`opfsWriter` 경유)에 기록한다. */
      readonly type: "spoolFrame";
      readonly blob: Blob;
      readonly width: number;
      readonly height: number;
    }
  | {
      /**
       * 프리뷰 1프레임(비트맵 폴백 경로). 메인이 캔버스에 그린 뒤 **반드시 `close()`** 한다 —
       * `ImageBitmap`은 GC 대상이 아니다.
       */
      readonly type: "previewFrame";
      readonly bitmap: ImageBitmap;
    };

/** 스틸 JPEG 품질 — 04 §5.1(0.95 고정). */
export const STILL_JPEG_QUALITY = 0.95;

/** 타임랩스 스풀 JPEG 품질 — 04 §7.2(0.8). 900장을 OPFS에 담아야 하므로 스틸보다 낮다. */
export const SPOOL_JPEG_QUALITY = 0.8;
