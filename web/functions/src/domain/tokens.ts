/**
 * 토큰 순수 로직 — 이메일 인증/비밀번호 재설정 토큰의 생성·해시·비교·만료 판정(설계 §4.2·§4.3·§12).
 *
 * selector.verifier 패턴: 클라/이메일에 전달되는 값 = `{tokenId}.{secret}`.
 *   - tokenId(selector)는 비밀이 아님 → Firestore 문서 O(1) 조회 키.
 *   - secret(verifier)은 고엔트로피 난수 → sha256 해시만 저장, confirm 시 상수시간 비교.
 * 6자리 코드는 키오스크 수기 입력용(저엔트로피 → 시도 횟수 제한과 함께 사용, §12).
 *
 * 이 모듈은 **순수**(Firestore·시계 부수효과 없음). 만료 판정은 now를 인자로 받는다.
 * 외부 의존 0 — Node 표준 `crypto`만 사용.
 */
import { createHash, randomBytes, randomInt, randomUUID, timingSafeEqual } from "node:crypto";

/** 토큰 용도. Firestore 문서 purpose 필드와 confirm 시 대조. */
export type TokenPurpose = "verify_email" | "password_reset";

/** 새 토큰 생성 결과(비밀은 평문·해시 모두 여기서만 다뤄지고, 평문은 이메일 본문에만 실린다). */
export interface GeneratedToken {
  /** 문서 ID(selector). 조회 키. 비밀 아님. */
  tokenId: string;
  /** verifier 평문. 이메일 링크에 실린다. 저장 금지(해시만 저장). */
  secret: string;
  /** 이메일/앱에 노출되는 결합 토큰 값 `{tokenId}.{secret}`. */
  token: string;
  /** 6자리 수기 코드 평문. 이메일 본문에 실린다. 저장 금지(해시만 저장). */
  code: string;
  /** secret의 sha256 해시(저장용). */
  secretHash: string;
  /** code의 sha256 해시(저장용). */
  codeHash: string;
}

/** secret 바이트 수(16바이트=128비트 엔트로피, base64url ≈ 22자). */
const SECRET_BYTES = 16;

/** 값을 sha256 소문자 hex로 해시. 토큰은 고엔트로피라 느린 해시(bcrypt) 불요(§4.3). */
export function hashToken(value: string): string {
  return createHash("sha256").update(value, "utf8").digest("hex");
}

/** 6자리 숫자 코드 생성(선행 0 포함, 항상 6자리 문자열). */
export function generateCode(): string {
  // randomInt(0, 1_000_000) → 균등분포, 6자리로 zero-pad.
  return String(randomInt(0, 1_000_000)).padStart(6, "0");
}

/**
 * 새 토큰 일습 생성: tokenId(UUIDv4) + secret(난수) + code(6자리) + 각 해시.
 * 평문(secret·code·token)은 호출측이 이메일 발송에만 쓰고 저장하지 않는다.
 */
export function generateToken(): GeneratedToken {
  const tokenId = randomUUID();
  const secret = randomBytes(SECRET_BYTES).toString("base64url");
  const code = generateCode();
  return {
    tokenId,
    secret,
    token: `${tokenId}.${secret}`,
    code,
    secretHash: hashToken(secret),
    codeHash: hashToken(code),
  };
}

/**
 * 결합 토큰 문자열 `{tokenId}.{secret}`을 분해. 형식 위반이면 null.
 * tokenId·secret 모두 비어있지 않아야 하고, secret에는 '.'이 포함될 수 있으므로 첫 '.'만 기준으로 분리.
 */
export function parseToken(value: unknown): { tokenId: string; secret: string } | null {
  if (typeof value !== "string") return null;
  const dot = value.indexOf(".");
  if (dot <= 0 || dot >= value.length - 1) return null;
  const tokenId = value.slice(0, dot);
  const secret = value.slice(dot + 1);
  if (tokenId.length === 0 || secret.length === 0) return null;
  return { tokenId, secret };
}

/**
 * 후보 평문(secret 또는 code)이 저장된 해시와 일치하는지 상수시간 비교(§12 타이밍 공격 방어).
 * 후보를 먼저 sha256 해시한 뒤, 저장 해시와 동일 길이 버퍼로 timingSafeEqual.
 */
export function verifyTokenHash(candidatePlain: string, storedHash: string): boolean {
  const candidateHash = hashToken(candidatePlain);
  const a = Buffer.from(candidateHash, "utf8");
  const b = Buffer.from(storedHash, "utf8");
  // 길이가 다르면 timingSafeEqual이 throw하므로 사전 방어(길이 자체는 sha256 hex라 항상 64자여야 함).
  if (a.length !== b.length) return false;
  return timingSafeEqual(a, b);
}

/** 만료 판정: expiresAt(ms) <= now(ms)면 만료. now는 인자로 받아 순수하게 유지. */
export function isExpired(expiresAtMs: number, nowMs: number): boolean {
  return expiresAtMs <= nowMs;
}

/** 만료 시각(ms) 계산: now + ttlSeconds. */
export function computeExpiresAtMs(nowMs: number, ttlSeconds: number): number {
  return nowMs + ttlSeconds * 1000;
}

/** verify 토큰 TTL(초): 5분(§3.3 규칙 C3 — 브루트포스 창 축소, MAX_CODE_ATTEMPTS와 결합). */
export const VERIFY_TTL_SECONDS = 5 * 60;

/** reset 토큰 TTL(초): 1시간(§5.4). */
export const RESET_TTL_SECONDS = 60 * 60;

/** 코드 최대 시도 횟수(초과 시 토큰 무효화, §12). */
export const MAX_CODE_ATTEMPTS = 5;
