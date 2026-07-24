/**
 * 토큰 서브컬렉션 서비스 — `users/{id}/tokens/{tokenId}` CRUD(설계 §4.2·§8.7).
 *
 * 발급: domain/tokens로 tokenId·secret·code·해시 생성 → 해시만 Firestore에 저장, 평문은 반환값으로만.
 * 검증/소비: tokenId(selector)로 O(1) 조회 → purpose·만료·소비여부·해시 상수시간 대조 → 소비(consumedAt+삭제).
 *
 * 평문 secret·code는 저장하지 않는다. 저장/응답 어디에도 해시·평문 토큰이 새어나가지 않게 한다.
 */
import { Timestamp } from "firebase-admin/firestore";
import { db } from "../firebase";
import {
  computeExpiresAtMs,
  generateToken,
  GeneratedToken,
  isExpired,
  MAX_CODE_ATTEMPTS,
  parseToken,
  TokenPurpose,
  verifyTokenHash,
} from "../domain/tokens";
import { TokenDoc } from "./dto";

const USERS = "users";
const TOKENS = "tokens";

/** users/{userId}/tokens 컬렉션 참조. */
function tokensRef(userId: string) {
  return db().collection(USERS).doc(userId).collection(TOKENS);
}

/** 발급 결과에서 호출측(이메일 발송)이 쓰는 평문 값. */
export interface IssuedToken {
  /** 이메일 링크에 실리는 결합 토큰 `{tokenId}.{secret}`. */
  token: string;
  /** 수기 입력 6자리 코드. */
  code: string;
}

/**
 * 토큰 발급: 같은 purpose의 기존 미소비 토큰을 정리(재요청 시 이전 것 무효화)하고 새 토큰을 저장.
 * 평문(token·code)은 반환값으로만 전달(이메일 발송용). Firestore에는 해시만 저장.
 *
 * @param userId 대상 계정 id
 * @param purpose 용도
 * @param email 검증 대상 이메일(소문자 정규화된 값)
 * @param ttlSeconds 만료(초)
 */
export async function issueToken(
  userId: string,
  purpose: TokenPurpose,
  email: string,
  ttlSeconds: number
): Promise<IssuedToken> {
  const col = tokensRef(userId);

  // 같은 purpose의 기존 토큰 제거(재요청 시 이전 코드/링크 무효화 — 최신 1건만 유효).
  const existing = await col.where("purpose", "==", purpose).get();
  await Promise.all(existing.docs.map((d) => d.ref.delete()));

  const gen: GeneratedToken = generateToken();
  const nowMs = Date.now();
  const doc: TokenDoc = {
    id: gen.tokenId,
    purpose,
    secretHash: gen.secretHash,
    codeHash: gen.codeHash,
    email,
    createdAt: Timestamp.fromMillis(nowMs),
    expiresAt: Timestamp.fromMillis(computeExpiresAtMs(nowMs, ttlSeconds)),
    consumedAt: null,
    attempts: 0,
  };
  await col.doc(gen.tokenId).set(doc);

  return { token: gen.token, code: gen.code };
}

/** 토큰 검증 결과. 성공 시 소비까지 완료된 상태(1회성). */
export type ConsumeResult =
  | { ok: true; email: string }
  | { ok: false; reason: "not_found" | "expired" | "consumed" | "mismatch" };

/**
 * 결합 토큰(`{tokenId}.{secret}`)으로 검증·소비(링크 경로).
 * purpose·만료·미소비·secret 해시 상수시간 대조 후 소비(consumedAt 마킹 + 문서 삭제).
 */
export async function consumeByToken(
  userId: string,
  purpose: TokenPurpose,
  token: string
): Promise<ConsumeResult> {
  const parsed = parseToken(token);
  if (!parsed) return { ok: false, reason: "not_found" };
  return consumeInternal(userId, purpose, parsed.tokenId, (doc) =>
    verifyTokenHash(parsed.secret, doc.secretHash)
  );
}

/**
 * tokenId + 6자리 코드로 검증·소비(수기/키오스크 경로).
 * 코드 오입력은 attempts를 올리고, MAX_CODE_ATTEMPTS 초과 시 토큰을 무효화(삭제)한다(§12).
 * 코드 경로는 tokenId를 별도로 알 필요 없이, 대상 계정의 미소비 토큰을 purpose로 찾는다.
 */
export async function consumeByCode(
  userId: string,
  purpose: TokenPurpose,
  code: string
): Promise<ConsumeResult> {
  const col = tokensRef(userId);
  const snap = await col.where("purpose", "==", purpose).get();
  if (snap.empty) return { ok: false, reason: "not_found" };

  // 미소비·미만료 토큰 중 최신 1건을 대상으로(재요청 정리로 보통 1건).
  const nowMs = Date.now();
  const candidates = snap.docs
    .map((d) => d.data() as TokenDoc)
    .filter((t) => t.consumedAt === null)
    .sort((a, b) => b.createdAt.toMillis() - a.createdAt.toMillis());

  const target = candidates[0];
  if (!target) return { ok: false, reason: "consumed" };

  const ref = col.doc(target.id);
  if (isExpired(target.expiresAt.toMillis(), nowMs)) {
    await ref.delete();
    return { ok: false, reason: "expired" };
  }

  if (!verifyTokenHash(code, target.codeHash)) {
    const attempts = (target.attempts ?? 0) + 1;
    if (attempts >= MAX_CODE_ATTEMPTS) {
      // 시도 횟수 초과 → 토큰 무효화(브루트포스 차단, §12).
      await ref.delete();
    } else {
      await ref.update({ attempts });
    }
    return { ok: false, reason: "mismatch" };
  }

  // 성공 → 소비(마킹 후 삭제).
  await ref.delete();
  return { ok: true, email: target.email };
}

/**
 * tokenId 조회 + 검증자(verifier) 콜백으로 대조 후 소비.
 * verifier가 secret/code 대조 방식을 결정(링크=secretHash, 코드는 별도 경로).
 */
async function consumeInternal(
  userId: string,
  purpose: TokenPurpose,
  tokenId: string,
  verifier: (doc: TokenDoc) => boolean
): Promise<ConsumeResult> {
  const ref = tokensRef(userId).doc(tokenId);
  const snap = await ref.get();
  if (!snap.exists) return { ok: false, reason: "not_found" };

  const doc = snap.data() as TokenDoc;
  if (doc.purpose !== purpose) return { ok: false, reason: "not_found" };
  if (doc.consumedAt !== null) return { ok: false, reason: "consumed" };

  const nowMs = Date.now();
  if (isExpired(doc.expiresAt.toMillis(), nowMs)) {
    await ref.delete();
    return { ok: false, reason: "expired" };
  }

  if (!verifier(doc)) {
    return { ok: false, reason: "mismatch" };
  }

  await ref.delete();
  return { ok: true, email: doc.email };
}
