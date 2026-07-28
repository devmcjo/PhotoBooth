/**
 * 인증·인가 미들웨어 (설계 §1.3, §6.2).
 *
 * - requireApiKey: 게스트 가능 엔드포인트 게이트(배포별 API 키, X-MCPhoto-Client 헤더).
 * - requireBearer: 로그인 JWT Bearer 검증 → req.principal에 {id, role} 주입.
 * - requirePower/requireAdmin: 역할 게이트(서버 재검증, 클라 신뢰 금지).
 *
 * 두 인증은 독립: API 키가 필요한 경로도 있고, Bearer가 필요한 경로도 있다.
 */
import { NextFunction, Request, RequestHandler, Response } from "express";
import { loadConfig } from "../config";
import { AuthPrincipal, extractBearer, TokenError, verifyToken } from "../domain/jwt";
import { isPower } from "../domain/roles";
import { HttpError } from "./errors";

/** API 키 헤더명(설계 §6.1 예시). */
export const API_KEY_HEADER = "x-mcphoto-client";

/** Express Request에 인증 주체를 얹는다(타입 확장). */
declare module "express-serve-static-core" {
  interface Request {
    principal?: AuthPrincipal;
  }
}

/** 헤더 값이 string 배열일 수 있어 첫 값만 취한다. */
function headerValue(v: string | string[] | undefined): string | undefined {
  if (Array.isArray(v)) return v[0];
  return v;
}

/**
 * 배포 API 키 검증. 유효 키 목록에 포함되지 않으면 401.
 * 게스트 흐름(프레임 조회·업로드·로그인)이 이 게이트를 통과한다.
 */
export function requireApiKey(): RequestHandler {
  return (req: Request, _res: Response, next: NextFunction) => {
    const cfg = loadConfig();
    const key = headerValue(req.headers[API_KEY_HEADER]);
    if (!key || !cfg.clientApiKeys.includes(key)) {
      return next(HttpError.unauthorized("유효한 클라이언트 키가 필요합니다."));
    }
    next();
  };
}

/**
 * JWT Bearer 검증 → req.principal 주입. 없거나 무효면 401.
 */
export function requireBearer(): RequestHandler {
  return (req: Request, _res: Response, next: NextFunction) => {
    const cfg = loadConfig();
    const token = extractBearer(headerValue(req.headers.authorization));
    if (!token) {
      return next(HttpError.unauthorized("Bearer 토큰이 필요합니다."));
    }
    try {
      req.principal = verifyToken(token, cfg.jwtSecret);
      next();
    } catch (err) {
      if (err instanceof TokenError) {
        return next(HttpError.unauthorized(err.message));
      }
      next(err);
    }
  };
}

/**
 * 선택적 Bearer 검증(업로드 신원화, 설계 §5.1). it13:
 *   - Authorization 헤더 없음 → principal 미주입, 그대로 통과(게스트 익명 흐름 유지).
 *   - 유효 Bearer → req.principal 주입(TempUser 한도 강제의 계정 신원).
 *   - 무효 Bearer(서명 불일치·만료·형식 오류) → 401(위조 토큰 거부).
 *
 * requireBearer와 달리 "토큰 없음"은 401이 아니다 — 게스트 촬영 업로드가 그대로 동작해야 하기 때문.
 */
export function optionalBearer(): RequestHandler {
  return (req: Request, _res: Response, next: NextFunction) => {
    const token = extractBearer(headerValue(req.headers.authorization));
    if (!token) {
      // 무토큰 = 게스트. principal 없이 통과(한도 미적용).
      return next();
    }
    const cfg = loadConfig();
    try {
      req.principal = verifyToken(token, cfg.jwtSecret);
      next();
    } catch (err) {
      if (err instanceof TokenError) {
        // 토큰이 제시됐으나 무효 = 위조 시도. 익명 폴백 대신 거부(과금 우회 차단).
        return next(HttpError.unauthorized(err.message));
      }
      next(err);
    }
  };
}

/** requireBearer 이후 사용. power(manager/admin)만 통과. */
export function requirePower(): RequestHandler {
  return (req: Request, _res: Response, next: NextFunction) => {
    const p = req.principal;
    if (!p) return next(HttpError.unauthorized());
    if (!isPower(p.role)) {
      return next(HttpError.forbidden("파워 계정(manager/admin) 권한이 필요합니다."));
    }
    next();
  };
}

/** requireBearer 이후 사용. admin만 통과(역할 지정 등). */
export function requireAdmin(): RequestHandler {
  return (req: Request, _res: Response, next: NextFunction) => {
    const p = req.principal;
    if (!p) return next(HttpError.unauthorized());
    if (p.role !== "admin") {
      return next(HttpError.forbidden("admin 권한이 필요합니다."));
    }
    next();
  };
}
