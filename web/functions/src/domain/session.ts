/**
 * 업로드 계약 조립 — WPF `MCPhoto.Core.Upload.UploadContract`(C#)의 순수 이식.
 *
 * Storage 경로·다운로드 토큰 URL·downloadPageUrl·sessionId·expiresAt 규약을 정확히 준수해야
 * 웹 다운로드 페이지가 결과물을 읽을 수 있다(firebase-contract §3·§4).
 * 근거: src/MCPhoto.Core/Upload/UploadContract.cs
 */
import { randomUUID } from "node:crypto";

/** 출력 이미지 포맷(WPF OutputFormat 대응). */
export type OutputFormat = "jpg" | "png";

/**
 * 세션 폴더/문서 ID 앞에 붙일 날짜_시간(초) prefix: `yyyyMMdd_HHmmss`.
 *
 * WPF는 **로컬 시간**으로 생성하지만(UploadContract.StampPrefix), 서버는 어느 시계로
 * 만들든 계약 형식만 지키면 된다(웹은 prefix 형식만 소비, 값 검증 안 함). 서버는 UTC 기준으로
 * 안정적으로 생성한다(현행 sessionId를 클라가 만들지 않고 서버가 만드는 방향 B 취지).
 * 근거: UploadContract.StampPrefix (UploadContract.cs:18)
 */
export function stampPrefix(date: Date): string {
  const pad = (n: number, w = 2): string => String(n).padStart(w, "0");
  const yyyy = pad(date.getUTCFullYear(), 4);
  const mm = pad(date.getUTCMonth() + 1);
  const dd = pad(date.getUTCDate());
  const hh = pad(date.getUTCHours());
  const mi = pad(date.getUTCMinutes());
  const ss = pad(date.getUTCSeconds());
  return `${yyyy}${mm}${dd}_${hh}${mi}${ss}`;
}

/**
 * 새 세션 ID = `{yyyyMMdd_HHmmss}_{uuidv4}`. 이 값이 곧 results/ 하위 폴더명 · Firestore 문서 ID · URL 토큰.
 * 앞의 날짜_시간으로 폴더가 시각순 정렬·검색되고, 뒤의 UUIDv4(122비트)로 추측 불가 유지.
 * 근거: UploadContract.NewSessionId (UploadContract.cs:25)
 */
export function newSessionId(date: Date = new Date()): string {
  return `${stampPrefix(date)}_${randomUUID()}`;
}

/**
 * sessionId 형식 검증: `{8자리 날짜}_{6자리 시각}_{UUIDv4}`.
 * 클라가 prepare에 보낸 sessionId를 신뢰하기 전에 형식을 강제(경로 인젝션·열거 방어).
 */
const SESSION_ID_RE =
  /^\d{8}_\d{6}_[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/;

export function isValidSessionId(value: unknown): value is string {
  return typeof value === "string" && SESSION_ID_RE.test(value);
}

/** 최종 이미지 Storage 경로: results/{sessionId}/final.{ext}. §4.2 */
export function finalImagePath(sessionId: string, format: OutputFormat): string {
  return `results/${sessionId}/final.${format === "png" ? "png" : "jpg"}`;
}

/** 타임랩스 Storage 경로: results/{sessionId}/timelapse.mp4. §4.2 */
export function timelapsePath(sessionId: string): string {
  return `results/${sessionId}/timelapse.mp4`;
}

/**
 * Firebase 다운로드 토큰 URL 조립. §4.3
 * https://firebasestorage.googleapis.com/v0/b/{bucket}/o/{urlEncodedPath}?alt=media&token={downloadToken}
 * 근거: UploadContract.TokenDownloadUrl (UploadContract.cs:39-43)
 */
export function tokenDownloadUrl(
  bucket: string,
  storagePath: string,
  downloadToken: string
): string {
  const encoded = encodeURIComponent(storagePath); // 슬래시 → %2F 포함
  return `https://firebasestorage.googleapis.com/v0/b/${bucket}/o/${encoded}?alt=media&token=${downloadToken}`;
}

/**
 * downloadPageUrl 조립(쿼리형 기본안). §3.5
 * {hostingBaseUrl}/?s={token} — 트레일링 슬래시 제거 후 조립.
 * 근거: UploadContract.DownloadPageUrl (UploadContract.cs:49-53)
 */
export function downloadPageUrl(hostingBaseUrl: string, token: string): string {
  const baseUrl = (hostingBaseUrl ?? "").replace(/\/+$/, "");
  return `${baseUrl}/?s=${token}`;
}

/** expiresAt = createdAt + retentionHours. §2.3 */
export function computeExpiresAt(createdAt: Date, retentionHours: number): Date {
  return new Date(createdAt.getTime() + retentionHours * 3600 * 1000);
}
