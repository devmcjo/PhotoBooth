import type { OutputFormat } from "../settings/appSettings";
import { isValidSessionId } from "./uploadContract";

/**
 * [기기에 저장] 파일명 — **P1 다운로드 페이지와 같은 규칙**이다
 * (`docs/design/web-it17-download-share-design.md §6`).
 *
 * 같은 규칙을 쓰는 이유: 손님이 QR로 받은 파일과 키오스크에서 내보낸 파일이
 * **같은 이름**이어야 중복·혼동이 없다.
 */

/** 파일명 접두. P1 페이지와 **같은 값**을 쓴다. */
export const EXPORT_FILE_PREFIX = "MCPhoto";

/** 스탬프 `yyyyMMdd_HHmmss` = 세션 ID 앞 15자. */
const STAMP_LENGTH = 15;

/**
 * `MCPhoto_{yyyyMMdd}_{HHmmss}.{jpg|png}` / `MCPhoto_{yyyyMMdd}_{HHmmss}_timelapse.mp4`
 * 세션 ID 형식이 아니면(방어) `MCPhoto.jpg` / `MCPhoto_timelapse.mp4`.
 *
 * ⚠️ **UUID 부분을 파일명에 넣지 않는다.** 세션 ID의 UUID는 다운로드 페이지의 `?s=` 토큰과
 *    같은 값이라, 파일명으로 새어 나가면 링크가 새어 나가는 것과 같다(web-it17 §6의 보안 판정).
 */
export function exportFileName(
  sessionId: string | null,
  kind: "final" | "timelapse",
  format: OutputFormat,
): string {
  const stamp =
    sessionId !== null && isValidSessionId(sessionId) ? sessionId.slice(0, STAMP_LENGTH) : null;
  const suffix =
    kind === "timelapse" ? "_timelapse.mp4" : `.${format === "Png" ? "png" : "jpg"}`;

  return stamp === null ? `${EXPORT_FILE_PREFIX}${suffix}` : `${EXPORT_FILE_PREFIX}_${stamp}${suffix}`;
}
