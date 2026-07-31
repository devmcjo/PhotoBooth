import type { OutputFormat } from "../settings/appSettings";

/**
 * 업로드 계약 조립 — Windows `Upload/UploadContract.cs` 이식 (analysis/31 §7)
 *
 * ⚠️ 이 값들이 어긋나면 **다운로드 페이지(P1)가 결과물을 못 읽는다.** 형식을 바꾸지 않는다.
 * ⚠️ 도메인은 시각·난수를 만들지 않는다 — 어댑터가 `new Date()`·`crypto.randomUUID()`를 주입한다(01 §8).
 */

/** 세션 ID 형식 `{yyyyMMdd}_{HHmmss}_{UUIDv4}` (M13). */
export const SESSION_ID_PATTERN =
  /^\d{8}_\d{6}_[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/;

function pad(value: number, length: number): string {
  return String(value).padStart(length, "0");
}

/**
 * 세션 폴더·문서 ID 앞에 붙는 **로컬 시각** prefix `yyyyMMdd_HHmmss`.
 * 로컬 시각인 이유: 운영자가 결과물 폴더를 시각으로 정렬·검색한다.
 */
export function stampPrefix(localTime: Date): string {
  return (
    `${pad(localTime.getFullYear(), 4)}${pad(localTime.getMonth() + 1, 2)}${pad(localTime.getDate(), 2)}` +
    `_${pad(localTime.getHours(), 2)}${pad(localTime.getMinutes(), 2)}${pad(localTime.getSeconds(), 2)}`
  );
}

/**
 * 세션 ID = `{yyyyMMdd_HHmmss}_{uuid}`.
 * 이 값이 곧 **Storage 폴더명 · Firestore 문서 ID · 다운로드 토큰**이다(세 곳이 같아야 자동삭제가 정합).
 *
 * @param uuid UUIDv4 문자열(어댑터가 `crypto.randomUUID()`로 주입)
 */
export function newSessionId(localTime: Date, uuid: string): string {
  return `${stampPrefix(localTime)}_${uuid}`;
}

export function isValidSessionId(sessionId: string): boolean {
  return SESSION_ID_PATTERN.test(sessionId);
}

/** 최종 이미지 Storage 경로 `results/{sessionId}/final.{ext}`. */
export function finalImagePath(sessionId: string, format: OutputFormat): string {
  return `results/${sessionId}/final.${format === "Png" ? "png" : "jpg"}`;
}

/** 타임랩스 Storage 경로 `results/{sessionId}/timelapse.mp4`. */
export function timelapsePath(sessionId: string): string {
  return `results/${sessionId}/timelapse.mp4`;
}

/** 최종 이미지 MIME. */
export function finalImageContentType(format: OutputFormat): string {
  return format === "Png" ? "image/png" : "image/jpeg";
}

export const TIMELAPSE_CONTENT_TYPE = "video/mp4";

/**
 * Firebase 다운로드 토큰 URL.
 * `https://firebasestorage.googleapis.com/v0/b/{bucket}/o/{encodedPath}?alt=media&token={token}`
 * (경로의 `/`가 `%2F`로 인코딩돼야 한다 — `encodeURIComponent`가 그렇게 한다.)
 */
export function tokenDownloadUrl(
  bucket: string,
  storagePath: string,
  downloadToken: string,
): string {
  const encoded = encodeURIComponent(storagePath);
  return `https://firebasestorage.googleapis.com/v0/b/${bucket}/o/${encoded}?alt=media&token=${downloadToken}`;
}

/**
 * 다운로드 페이지 URL `{hostingBaseUrl}/?s={token}`.
 * ⚠️ 이 URL은 **P1 사이트 도메인**을 가리켜야 한다(키오스크 사이트가 아니다).
 */
export function downloadPageUrl(hostingBaseUrl: string, token: string): string {
  const baseUrl = hostingBaseUrl.replace(/\/+$/, "");
  return `${baseUrl}/?s=${token}`;
}

/** `expiresAt = createdAt + retentionHours`. */
export function computeExpiresAt(createdAt: Date, retentionHours: number): Date {
  return new Date(createdAt.getTime() + retentionHours * 3600_000);
}
