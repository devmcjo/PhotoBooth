import { logger } from "@adapters/storage/logStore";

/**
 * 프레임 이미지 다운로드 — 06 §6 (WM2)
 *
 * 서버 프레임 이미지는 `firebasestorage.googleapis.com`(다른 오리진)에 있다. **CORS-clean하게 받지
 * 않으면 canvas가 오염되어 `convertToBlob`이 `SecurityError`를 던지고 합성이 전면 실패한다** —
 * 그리고 손님은 6컷을 다 찍은 뒤에야 그것을 안다. 받은 Blob은 OPFS에 캐시되어 이후 same-origin이 된다.
 *
 * ⚠️ 어댑터 규약: 실패·비200·빈 본문은 전부 `null`이고 예외를 전파하지 않는다.
 */

/** 한 장이 영원히 매달려 무진행 30초 예산을 태우지 않게 한다. */
export const FRAME_DOWNLOAD_TIMEOUT_MS = 15_000;

export async function downloadFrameImage(
  url: string,
  timeoutMs: number = FRAME_DOWNLOAD_TIMEOUT_MS,
): Promise<Blob | null> {
  if (url.trim().length === 0) return null;

  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), timeoutMs);
  try {
    // ⚠️ `mode: "cors"`를 생략하면 서버가 헤더를 줘도 canvas가 오염된다 — 절대 빼지 마라(WM2).
    // ⚠️ 게이트 키·Bearer를 **붙이지 않는다**. Storage 다운로드 토큰 URL은 인증이 불요하고
    //    (analysis/31 §4.10), 자격 증명을 붙이면 `ACAO: *`와 충돌해 오히려 차단된다.
    const response = await fetch(url, {
      mode: "cors",
      credentials: "omit",
      cache: "force-cache",
      signal: controller.signal,
    });

    if (!response.ok) {
      // 이미지 없는 문서가 존재할 수 있다(서버가 문서를 먼저 만들고 PUT은 나중 — analysis/31 §4.10).
      // 404는 **정상 경로**다.
      logger.warn("프레임 이미지 다운로드 실패(HTTP)", { status: response.status });
      return null;
    }

    const blob = await response.blob();
    if (blob.size === 0) {
      logger.warn("프레임 이미지 본문이 비어 있습니다");
      return null;
    }
    return blob;
  } catch (err) {
    // CORS 차단은 브라우저가 `TypeError`로만 알려준다 — 구분이 불가능하므로 사유를 그대로 남긴다.
    logger.warn("프레임 이미지 다운로드 실패(네트워크 또는 CORS 차단 가능)", {
      reason: err instanceof Error ? err.message : String(err),
    });
    return null;
  } finally {
    clearTimeout(timer);
  }
}
