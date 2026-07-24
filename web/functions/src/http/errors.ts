/**
 * HTTP 에러 표준형 — `{ error: { code, message } }` (설계 §6.1).
 *
 * 상태코드↔클라 예외 매핑(현행 계약 보존):
 *   401=로그인 실패, 403=권한 없음(UnauthorizedAccessException),
 *   409=중복(InvalidOperationException), 400=입력검증, 404=미존재, 5xx=서버.
 */
import { Response } from "express";

export type ErrorCode =
  | "unauthorized" // 401 인증 필요/실패
  | "forbidden" // 403 권한 없음(역할 위계)
  | "conflict" // 409 중복
  | "invalid_argument" // 400 입력 검증 실패
  | "not_found" // 404 미존재
  | "internal"; // 500 서버 오류

/** 라우트에서 throw해 상태코드·코드·메시지를 일관되게 응답으로 변환. */
export class HttpError extends Error {
  readonly status: number;
  readonly code: ErrorCode;

  constructor(status: number, code: ErrorCode, message: string) {
    super(message);
    this.name = "HttpError";
    this.status = status;
    this.code = code;
  }

  static unauthorized(message = "인증이 필요합니다.") {
    return new HttpError(401, "unauthorized", message);
  }
  static forbidden(message = "권한이 없습니다.") {
    return new HttpError(403, "forbidden", message);
  }
  static conflict(message = "이미 존재합니다.") {
    return new HttpError(409, "conflict", message);
  }
  static invalid(message = "입력이 올바르지 않습니다.") {
    return new HttpError(400, "invalid_argument", message);
  }
  static notFound(message = "찾을 수 없습니다.") {
    return new HttpError(404, "not_found", message);
  }
  static internal(message = "서버 오류가 발생했습니다.") {
    return new HttpError(500, "internal", message);
  }
}

/** 표준 에러 JSON을 응답으로 기록. */
export function sendError(res: Response, err: HttpError): void {
  res.status(err.status).json({ error: { code: err.code, message: err.message } });
}
