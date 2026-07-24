/**
 * JWT 발급·검증(HS256) — 로그인 성공 시 단기 토큰 발급, 이후 요청 인증.
 *
 * claims: sub(계정 id), role(UserRole), iat, exp. 서명 시크릿은 서버 env/Secret Manager(설계 §7.2, §8.2).
 * jsonwebtoken 래퍼. 발급/검증 로직 자체는 시크릿을 인자로 받아 순수 테스트 가능하게 둔다.
 */
import jwt from "jsonwebtoken";
import { isUserRole, UserRole } from "./roles";

/** 검증 성공 시 도출되는 인증 주체(요청 컨텍스트에 실림). */
export interface AuthPrincipal {
  id: string;
  role: UserRole;
}

/** 토큰 검증 실패 사유(호출측이 401 매핑에 사용). */
export class TokenError extends Error {
  constructor(message: string) {
    super(message);
    this.name = "TokenError";
  }
}

/**
 * JWT 발급. sub=id, role 클레임, exp=now+expiresInSeconds.
 * @returns 서명된 토큰 문자열.
 */
export function issueToken(
  principal: AuthPrincipal,
  secret: string,
  expiresInSeconds: number
): string {
  if (!secret) throw new Error("JWT 시크릿이 비어 있습니다(서버 구성 오류).");
  return jwt.sign({ role: principal.role }, secret, {
    subject: principal.id,
    expiresIn: expiresInSeconds,
    algorithm: "HS256",
  });
}

/**
 * JWT 검증. 서명·만료·역할 클레임을 확인하고 AuthPrincipal을 도출한다.
 * 실패(서명 불일치·만료·형식 오류·역할 누락) 시 TokenError.
 */
export function verifyToken(token: string, secret: string): AuthPrincipal {
  if (!secret) throw new Error("JWT 시크릿이 비어 있습니다(서버 구성 오류).");
  let decoded: unknown;
  try {
    decoded = jwt.verify(token, secret, { algorithms: ["HS256"] });
  } catch (err) {
    throw new TokenError(
      err instanceof Error ? `토큰 검증 실패: ${err.message}` : "토큰 검증 실패"
    );
  }
  if (typeof decoded !== "object" || decoded === null) {
    throw new TokenError("토큰 페이로드가 올바르지 않습니다.");
  }
  const payload = decoded as { sub?: unknown; role?: unknown };
  if (typeof payload.sub !== "string" || payload.sub.length === 0) {
    throw new TokenError("토큰에 계정 식별자(sub)가 없습니다.");
  }
  if (!isUserRole(payload.role)) {
    throw new TokenError("토큰에 유효한 역할(role) 클레임이 없습니다.");
  }
  return { id: payload.sub, role: payload.role };
}

/**
 * `Authorization: Bearer <token>` 헤더에서 토큰만 추출. 형식 불일치면 null.
 */
export function extractBearer(headerValue: string | undefined): string | null {
  if (!headerValue) return null;
  const m = /^Bearer\s+(.+)$/i.exec(headerValue.trim());
  return m ? m[1].trim() : null;
}
