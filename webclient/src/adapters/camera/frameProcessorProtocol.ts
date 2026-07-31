/** 프레임 가공 Worker 메시지 프로토콜 — 04 §4 */

export type FrameProcessorRequest =
  | { readonly type: "configure"; readonly targetAspect: number; readonly mirror: boolean }
  | { readonly type: "frame"; readonly payload: ImageBitmap | VideoFrame }
  | { readonly type: "bindPreview"; readonly canvas: OffscreenCanvas }
  | { readonly type: "requestStill"; readonly id: number; readonly quality: number }
  | { readonly type: "reset" };

export type FrameProcessorResponse =
  | { readonly type: "processed"; readonly width: number; readonly height: number }
  | {
      readonly type: "still";
      readonly id: number;
      readonly blob: Blob | null;
      readonly error?: string;
    };

/** 스틸 JPEG 품질 — 04 §5.1(0.95 고정). */
export const STILL_JPEG_QUALITY = 0.95;
