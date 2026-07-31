/**
 * 백엔드 오류 타입 — 06 §3 · analysis/31 §3
 *
 * ⚠️ **화면은 예외 타입으로 분기하고 상태코드를 직접 비교하지 않는다.**
 *    403이 "권한 없음"과 "무료 한도 초과" 둘 다이므로(`error.code`로만 구분된다)
 *    상태코드 비교를 화면에 흩뿌리면 잘못된 문구가 나간다.
 *
 * ⚠️ **네트워크 실패는 401/403과 절대 섞지 않는다.** 브라우저는 CORS 차단을 구분해 알려주지
 *    않으므로 `TypeError: Failed to fetch`는 "네트워크 또는 CORS 차단 가능"으로 로그한다.
 */

/** 서버 오류 봉투 `{ error: { code, message } }`. */
export interface ErrorEnvelope {
  readonly code: string;
  readonly message: string;
}

/** 서버가 응답한 오류(상태코드 있음). */
export class BackendError extends Error {
  constructor(
    override readonly message: string,
    readonly status: number,
    readonly code: string,
  ) {
    super(message);
    this.name = "BackendError";
  }
}

/** 응답 자체가 없는 실패(연결 실패·DNS·타임아웃·CORS 차단). */
export class NetworkError extends Error {
  constructor(
    override readonly message: string,
    /** 타임아웃(AbortError)인가. 안내 문구가 다르다. */
    readonly timedOut: boolean = false,
  ) {
    super(message);
    this.name = "NetworkError";
  }
}

/** Bearer 필수 호출인데 토큰이 없다. **요청을 보내지 않는다.** */
export class NotAuthenticatedError extends Error {
  constructor(override readonly message = "로그인이 필요합니다.") {
    super(message);
    this.name = "NotAuthenticatedError";
  }
}

export type TempUserLimitReason = "time" | "count";

/** TempUser 무료 한도 초과(403 + `TEMP_USER_*`). 권한 문제가 아니다. */
export class TempUserLimitError extends BackendError {
  constructor(
    message: string,
    status: number,
    code: string,
    readonly reason: TempUserLimitReason,
  ) {
    super(message, status, code);
    this.name = "TempUserLimitError";
  }
}

/** Google SSO 미구성(501). 자격 실패·네트워크와 구분해 안내한다. */
export class SsoNotConfiguredError extends BackendError {
  constructor(message: string, status: number, code: string) {
    super(message, status, code);
    this.name = "SsoNotConfiguredError";
  }
}

export const TEMP_USER_TIME_EXCEEDED = "TEMP_USER_TIME_EXCEEDED";
export const TEMP_USER_COUNT_EXCEEDED = "TEMP_USER_COUNT_EXCEEDED";

/** 응답 본문에서 오류 봉투를 뽑는다. 본문이 없거나 형식이 다르면 상태코드로 대체 코드를 만든다. */
export function parseErrorEnvelope(body: unknown, status: number): ErrorEnvelope {
  if (typeof body === "object" && body !== null) {
    const error = (body as { error?: unknown }).error;
    if (typeof error === "object" && error !== null) {
      const record = error as { code?: unknown; message?: unknown };
      return {
        code: typeof record.code === "string" ? record.code : fallbackCode(status),
        message:
          typeof record.message === "string" && record.message.length > 0
            ? record.message
            : `요청이 실패했습니다(HTTP ${status}).`,
      };
    }
  }
  return { code: fallbackCode(status), message: `요청이 실패했습니다(HTTP ${status}).` };
}

function fallbackCode(status: number): string {
  switch (status) {
    case 400:
      return "invalid_argument";
    case 401:
      return "unauthorized";
    case 403:
      return "forbidden";
    case 404:
      return "not_found";
    case 409:
      return "conflict";
    case 500:
      return "internal";
    case 501:
      return "not_implemented";
    default:
      return `http_${status}`;
  }
}

/** 상태·코드를 예외 타입으로 매핑한다. **분기의 유일한 지점**이다. */
export function toBackendError(status: number, envelope: ErrorEnvelope): BackendError {
  if (status === 403 && envelope.code === TEMP_USER_TIME_EXCEEDED) {
    return new TempUserLimitError(envelope.message, status, envelope.code, "time");
  }
  if (status === 403 && envelope.code === TEMP_USER_COUNT_EXCEEDED) {
    return new TempUserLimitError(envelope.message, status, envelope.code, "count");
  }
  if (status === 501) {
    return new SsoNotConfiguredError(envelope.message, status, envelope.code);
  }
  return new BackendError(envelope.message, status, envelope.code);
}

/** `fetch` rejection을 네트워크 오류로 변환한다. */
export function toNetworkError(err: unknown): NetworkError {
  if (err instanceof DOMException && err.name === "AbortError") {
    return new NetworkError("백엔드에 연결할 수 없습니다(응답 시간 초과).", true);
  }
  // 브라우저는 CORS 차단을 `TypeError: Failed to fetch`로만 알려준다 — 구분이 불가능하다.
  const detail = err instanceof Error ? err.message : String(err);
  return new NetworkError(`백엔드에 연결할 수 없습니다(네트워크 또는 CORS 차단 가능): ${detail}`);
}

/** 오류 종류 판정 헬퍼(화면 분기용). */
export function isUnauthorized(err: unknown): boolean {
  return err instanceof BackendError && err.status === 401;
}

export function isForbidden(err: unknown): boolean {
  return err instanceof BackendError && err.status === 403 && !(err instanceof TempUserLimitError);
}

export function isConflict(err: unknown): boolean {
  return err instanceof BackendError && err.status === 409;
}

export function isNotFound(err: unknown): boolean {
  return err instanceof BackendError && err.status === 404;
}
