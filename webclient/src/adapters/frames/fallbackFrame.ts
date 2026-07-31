import {
  FALLBACK_FRAME_ID,
  FALLBACK_FRAME_NAME,
  FALLBACK_HEIGHT,
  FALLBACK_WIDTH,
  fallbackFrameSlots,
} from "@domain/frames/fallbackFrameSpec";
import type { FrameTemplate } from "@domain/frames/types";
import { logger } from "@adapters/storage/logStore";

/**
 * 코드 생성 fallback 프레임 — analysis/14 §4.7 · WBS Step 7(프레임 공급 선순환 해소)
 *
 * 서버·번들 프레임이 하나도 없어도 **촬영이 가능해야 한다**. 좌표는 도메인이 계산하고
 * (Windows와 동일한 정수 연산), 여기서는 하양 배경 이미지만 만든다.
 *
 * Step 14가 서버 카탈로그·OPFS 캐시로 이 함수를 대체하는 것이 아니라 **최종 폴백으로 남긴다**.
 */

let cachedUrl: string | null = null;

/** 하양 배경 PNG를 만들어 object URL을 돌려준다(1회 생성 후 캐시). */
export async function ensureFallbackImageUrl(): Promise<string> {
  if (cachedUrl !== null) return cachedUrl;

  try {
    const canvas =
      typeof OffscreenCanvas !== "undefined"
        ? new OffscreenCanvas(FALLBACK_WIDTH, FALLBACK_HEIGHT)
        : Object.assign(document.createElement("canvas"), {
            width: FALLBACK_WIDTH,
            height: FALLBACK_HEIGHT,
          });

    const ctx = (canvas as OffscreenCanvas).getContext("2d") as
      | OffscreenCanvasRenderingContext2D
      | CanvasRenderingContext2D
      | null;
    if (ctx === null) throw new Error("2D 컨텍스트를 만들 수 없습니다.");

    ctx.fillStyle = "#ffffff";
    ctx.fillRect(0, 0, FALLBACK_WIDTH, FALLBACK_HEIGHT);

    const blob =
      "convertToBlob" in canvas
        ? await (canvas as OffscreenCanvas).convertToBlob({ type: "image/png" })
        : await new Promise<Blob>((resolve, reject) => {
            (canvas as HTMLCanvasElement).toBlob(
              (result) => (result === null ? reject(new Error("toBlob 실패")) : resolve(result)),
              "image/png",
            );
          });

    cachedUrl = URL.createObjectURL(blob);
    return cachedUrl;
  } catch (err) {
    logger.warn("fallback 프레임 이미지 생성 실패 — 빈 URL로 진행", {
      reason: err instanceof Error ? err.message : String(err),
    });
    return "";
  }
}

/**
 * fallback 프레임 템플릿. 이미지 URL은 비동기로 생성되므로 인자로 받는다
 * (도메인은 URL을 만들지 않고, 이 함수는 좌표를 만들지 않는다).
 */
export function createFallbackFrame(imageUrl: string, createdAt: string): FrameTemplate {
  return {
    id: FALLBACK_FRAME_ID,
    userId: null,
    isDefault: true,
    name: FALLBACK_FRAME_NAME,
    imageUrl,
    imageSize: { width: FALLBACK_WIDTH, height: FALLBACK_HEIGHT },
    slots: fallbackFrameSlots(),
    createdAt,
  };
}

/** 화면 이탈 시 object URL을 해제한다(메모리 누수 방지). */
export function releaseFallbackImage(): void {
  if (cachedUrl !== null) {
    URL.revokeObjectURL(cachedUrl);
    cachedUrl = null;
  }
}
