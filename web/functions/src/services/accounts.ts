/**
 * 계정 서비스 — Firestore users 조작(Google SSO 로그인/목록/삭제/역할/PIN).
 *
 * it15: ID/PW 로그인·회원가입·계정 생성·비번 변경·이메일 인증/재설정을 전량 제거했다.
 * 자격증명은 ① Google SSO(신원, 서버가 id_token 검증) + ② pinHash(설정·계정 관리 진입 게이트) 두 가지뿐이다.
 * 역할 위계·PIN 해시는 서버가 최종 강제(설계 §5.1·§5.6).
 */
import { Timestamp } from "firebase-admin/firestore";
import { db } from "../firebase";
import { deriveAccountId } from "../domain/accountId";
import { hashPassword, verifyHash } from "../domain/password";
import {
  canManage,
  canResetPin,
  canSetRole,
  parseRole,
  UserRole,
} from "../domain/roles";
import {
  evaluateQrGate,
  QrGateReason,
  TempUserLimits,
} from "../domain/tempUserLimit";
import { HttpError } from "../http/errors";
import { loadTempUserLimits } from "./config";
import { UserDoc, UserResponse } from "./dto";
import { deleteAllFramesByUser } from "./frames";

const COLLECTION = "users";

/** UserDoc → 응답(해시 제거). 와이어 형식은 it15 설계 §9.1에서 동결. */
function toResponse(doc: UserDoc): UserResponse {
  return {
    id: doc.id,
    role: parseRole(doc.role),
    createdAt: doc.createdAt.toDate().toISOString(),
    email: doc.email ?? null,
    // D2: 저장값 그대로 노출(클라가 파싱). 미설정 레거시는 "google" 폴백
    // — 마이그레이션 후에는 도달 불가하나 방어값으로 남긴다.
    authMethod:
      typeof doc.authMethod === "string" && doc.authMethod.length > 0
        ? doc.authMethod
        : "google",
    hasPin: typeof doc.pinHash === "string",
  };
}

/** 로그인 성공 결과(토큰 발급에 필요한 최소 정보). */
export interface LoginResult {
  id: string;
  role: UserRole;
  user: UserResponse;
}

/** 전체 계정 목록(파워 전용). 비밀번호/해시 제거. */
export async function listAccounts(): Promise<UserResponse[]> {
  const snap = await db().collection(COLLECTION).get();
  return snap.docs.map((d) => toResponse(d.data() as UserDoc));
}

/** 특정 계정의 역할 조회(위계 판정용). 없으면 404. */
async function getRole(id: string): Promise<UserRole> {
  const snap = await db().collection(COLLECTION).doc(id).get();
  if (!snap.exists) throw HttpError.notFound(`계정을 찾을 수 없습니다: ${id}`);
  return parseRole((snap.data() as UserDoc).role);
}

// ─────────────────────────────────────────────────────────────────────────────
// it14: 설정 진입 PIN — 검증(게이트)/본인 설정·변경/타 계정 재설정 (설계 §4.4)
// PIN 해시·검증은 password.ts(bcrypt) 재사용. 권한은 canManage(roles.ts) 재사용.
// ─────────────────────────────────────────────────────────────────────────────

/**
 * PIN 게이트 검증 결과(3-값). 라우트가 상태코드로 매핑:
 *   - ok:true → 200(게이트 통과).
 *   - reason "mismatch" → 401(PIN 불일치).
 *   - reason "unset" → 409(PIN 미설정 — 클라가 최초 설정 플로우로 유도).
 */
export type VerifyPinResult =
  | { ok: true }
  | { ok: false; reason: "mismatch" | "unset" };

/**
 * 설정 진입 PIN 검증(본인, E1). principal.id의 pinHash와 대조.
 *   - pinHash 미설정 → {ok:false, reason:"unset"}(클라가 설정 필요 신호로 해석).
 *   - 일치 → {ok:true}, 불일치 → {ok:false, reason:"mismatch"}.
 * 서버 무잠금(it15 §5.6 — 계정 단위 잠금은 DoS 도입 위험으로 채택하지 않음).
 * 계정 문서 부재도 unset로 처리(최초 설정 유도와 동형).
 */
export async function verifyPin(actorId: string, pin: string): Promise<VerifyPinResult> {
  const snap = await db().collection(COLLECTION).doc(actorId).get();
  if (!snap.exists) return { ok: false, reason: "unset" };
  const doc = snap.data() as UserDoc;
  if (typeof doc.pinHash !== "string") return { ok: false, reason: "unset" };
  const matched = await verifyHash(pin, doc.pinHash);
  return matched ? { ok: true } : { ok: false, reason: "mismatch" };
}

/**
 * 본인 PIN 설정/변경(E2). 이미 PIN이 있으면 currentPin 확인 필수(불일치 401),
 * 미설정이면 최초 설정(currentPin 불요). 새 PIN은 라우트에서 validatePin 통과값.
 *   - 계정 문서 부재 → 404.
 *   - 기존 PIN 있음 + currentPin null/불일치 → HttpError 401.
 * 근거: 설계 §4.4 setOwnPin.
 */
export async function setOwnPin(
  actorId: string,
  currentPin: string | null,
  newPin: string
): Promise<void> {
  const ref = db().collection(COLLECTION).doc(actorId);
  const snap = await ref.get();
  if (!snap.exists) throw HttpError.notFound(`계정을 찾을 수 없습니다: ${actorId}`);
  const doc = snap.data() as UserDoc;

  if (typeof doc.pinHash === "string") {
    // 기존 PIN 보유 → 현재 PIN 확인 필수(본인 재인증).
    if (currentPin === null || !(await verifyHash(currentPin, doc.pinHash))) {
      throw HttpError.unauthorized("현재 PIN이 올바르지 않습니다.");
    }
  }
  await ref.update({ pinHash: await hashPassword(newPin) });
}

/**
 * 타 계정 PIN 재설정(E3, 권한 기반). 대상 현재 PIN 불요.
 *   - 대상 계정 없음 → 404.
 *   - canResetPin(actor.role, targetRole) 위반 → 403.
 * 자기 자신 대상 차단은 라우트에서(E2 사용 유도, 400). 근거: 설계 §4.4 resetOtherPin.
 *
 * ⚠️ 판정은 canManage가 아니라 **canResetPin**(엄격히 낮은 위계)이다 — manager가 다른 manager의
 *    PIN을 재설정하던 과대 권한을 막는다(manager PIN은 admin 전용). 삭제와 공유되는 canManage로
 *    되돌리지 말 것.
 */
export async function resetOtherPin(
  targetId: string,
  newPin: string,
  actor: { id: string; role: UserRole }
): Promise<void> {
  const targetRole = await getRole(targetId); // 없으면 404
  if (!canResetPin(actor.role, targetRole)) {
    throw HttpError.forbidden(
      "해당 계정의 PIN을 재설정할 권한이 없습니다(동급·상위 역할 대상 불가 — manager PIN은 admin 전용)."
    );
  }
  await db()
    .collection(COLLECTION)
    .doc(targetId)
    .update({ pinHash: await hashPassword(newPin) });
}

/**
 * 계정 삭제 + 소유 프레임 cascade. 위계 재검증(자신과 같거나 낮은 역할만).
 * 근거: AccountService.DeleteAsync (AccountService.cs:86-94), F5 cascade
 */
export async function deleteAccount(
  targetId: string,
  actor: { id: string; role: UserRole }
): Promise<void> {
  const targetRole = await getRole(targetId);
  if (!canManage(actor.role, targetRole)) {
    throw HttpError.forbidden("해당 계정을 삭제할 권한이 없습니다.");
  }
  // cascade: 소유 프레임(문서 + Storage) 먼저 정리(실패해도 계정 삭제는 진행).
  await deleteAllFramesByUser(targetId);
  await db().collection(COLLECTION).doc(targetId).delete();
}

/**
 * 역할 지정(it16 권한 매트릭스, 서버 강제 — 클라 전달 actor 무시, actor는 JWT에서 도출).
 *   - Admin: target ∈ {temp_user, user, advanced_user, manager}. admin 지정/admin 대상 불가.
 *   - Manager: 하위 3역할 대역(temp_user·user·advanced_user) 내에서만 자유 지정(승격 포함).
 *              manager·admin 지정과 manager·admin 대상 변경은 admin 전용 → 403.
 *   - 그 외 actor(하위 대역 전원): 전부 403(라우트의 power 게이트가 이미 조기 차단).
 * 판정은 순수 함수 canSetRole(roles.ts)에 위임. 대상 계정 없으면 getRole이 404.
 * 근거: AccountService.SetRoleAsync (AccountService.cs:96-101) + 설계 it16 §3.3 전수 표.
 */
export async function setRole(
  targetId: string,
  role: UserRole,
  actor: { id: string; role: UserRole }
): Promise<void> {
  const currentRole = await getRole(targetId); // 없으면 404
  if (!canSetRole(actor.role, currentRole, role)) {
    // 특수 케이스는 정확한 사유 문구(기존 계약 보존), 그 외는 매트릭스 위반 일반 문구.
    if (role === "admin") {
      throw HttpError.forbidden("admin 역할은 지정할 수 없습니다(최종 1인 규칙).");
    }
    if (currentRole === "admin") {
      throw HttpError.forbidden("admin 계정의 역할은 변경할 수 없습니다.");
    }
    throw HttpError.forbidden("해당 역할 변경을 수행할 권한이 없습니다.");
  }
  await db().collection(COLLECTION).doc(targetId).update({ role });
}

// ─────────────────────────────────────────────────────────────────────────────
// Google SSO 로그인 — 유일한 인증 경로 (it15 §5.5)
// ─────────────────────────────────────────────────────────────────────────────

/**
 * email로만 계정을 조회(자동 생성/매핑 분기용). id 조회는 하지 않는다(SSO는 email 신원만 신뢰).
 * 소문자 정규화된 email 필드로 비교.
 */
async function findByEmailField(email: string): Promise<UserDoc | null> {
  const snap = await db().collection(COLLECTION).where("email", "==", email).limit(1).get();
  if (snap.empty) return null;
  return snap.docs[0].data() as UserDoc;
}

/**
 * Google SSO 로그인(설계 §2.2 B-BE-1 / D-B1·D-B2, it15 §5.5).
 * 검증된 Google email(소문자)로 로그인한다. Google이 email 소유를 이미 증명했으므로:
 *   - 계정 없음 → **자동 생성**.
 *   - 기존 계정 → 그대로 로그인(role·authMethod 불변 — 강등 없음, P3).
 * 동시 첫 로그인 경합: create 원자 시도(문서 부재 시에만) 실패 시 재조회 후 로그인(§2.2 경합 방지).
 *
 * @param email verifyGoogleCodeAndGetEmail이 반환한, 이미 소문자 정규화된 email.
 */
export async function loginWithGoogleEmail(email: string): Promise<LoginResult | null> {
  const normalized = email.trim().toLowerCase();
  if (normalized.length === 0) return null;

  const existing = await findByEmailField(normalized);
  if (existing) {
    return loginExistingGoogleAccount(existing, normalized);
  }

  // 계정 없음 → 자동 생성(경합 대비 create 원자 시도).
  const created = await createGoogleAccount(normalized);
  if (created) {
    return created;
  }

  // create 경합(동시 첫 로그인이 방금 계정을 만듦) → 재조회 후 로그인.
  const retried = await findByEmailField(normalized);
  if (retried) {
    return loginExistingGoogleAccount(retried, normalized);
  }
  return null;
}

/**
 * 기존 계정으로 SSO 로그인. **DB write 없는 순수 읽기 경로**(it15 §5.5).
 * role·authMethod는 절대 건드리지 않는다 — 승격된 계정이 재로그인으로 강등되지 않는다(P3).
 */
async function loginExistingGoogleAccount(
  doc: UserDoc,
  normalized: string
): Promise<LoginResult | null> {
  // 방어: email 필드가 조회 email과 다르면(정규화 불일치 등) 로그인 거부.
  if ((doc.email ?? null) !== normalized) return null;

  const role = parseRole(doc.role);
  return { id: doc.id, role, user: toResponse(doc) };
}

/**
 * 계정 없음 → **temp_user**로 자동 생성(원자적, it15 §5.5). create는 문서 부재 시에만 성공.
 * 경합으로 방금 동일 id가 만들어졌다면 create가 throw → null 반환(호출측이 재조회).
 * 반환 null이 곧 "생성 실패(경합)"를 의미한다.
 */
async function createGoogleAccount(normalized: string): Promise<LoginResult | null> {
  const id = await deriveAccountId(normalized, async (candidate) => {
    const snap = await db().collection(COLLECTION).doc(candidate).get();
    return snap.exists;
  });

  const doc: UserDoc = {
    id,
    role: "temp_user", // it15: 신규 SSO 계정은 무조건 최소 권한. 승격은 관리자가 수행(지시 2).
    createdAt: Timestamp.now(),
    email: normalized,
    authMethod: "google", // D2
  };

  try {
    // create: 이미 존재하면 throw(경합). set과 달리 덮어쓰지 않는다.
    await db().collection(COLLECTION).doc(id).create(doc);
  } catch {
    // 경합(동시 생성) — 호출측이 재조회로 로그인 처리.
    return null;
  }
  return { id: doc.id, role: "temp_user", user: toResponse(doc) };
}

// ─────────────────────────────────────────────────────────────────────────────
// it13: TempUser QR 사용량 조회 (설계 §5.3)
// ─────────────────────────────────────────────────────────────────────────────

/** GET /accounts/me/qr-usage 응답. 비TempUser는 blocked:false, reason:"ok"(무제한). */
export interface QrUsageResponse {
  role: UserRole;
  blocked: boolean;
  reason: QrGateReason;
  /** 시간 잔여(ms). 비TempUser는 서버 표기상 무제한(아주 큰 값)이 아니라 0을 넘겨도 클라가 role로 무제한 처리. */
  remainingMs: number;
  remainingCount: number;
  limits: TempUserLimits;
}

/**
 * 로그인 계정의 QR 사용 게이트 상태(설계 §5.3). principal.id로 users doc 로드 → createdAt·qrUsedCount +
 * 전역 config로 evaluateQrGate 실행. 비TempUser(user/manager/admin)는 한도 없음 → blocked:false.
 * now는 서버 UTC(§8.4). 계정 문서 부재면 404.
 */
export async function getQrUsage(actor: {
  id: string;
  role: UserRole;
}): Promise<QrUsageResponse> {
  const limits = await loadTempUserLimits();

  // 비TempUser는 한도 없음 — 계정 문서를 읽지 않고 무제한 응답(클라는 role로 무제한 처리).
  if (actor.role !== "temp_user") {
    return {
      role: actor.role,
      blocked: false,
      reason: "ok",
      remainingMs: 0,
      remainingCount: 0,
      limits,
    };
  }

  const snap = await db().collection(COLLECTION).doc(actor.id).get();
  if (!snap.exists) throw HttpError.notFound(`계정을 찾을 수 없습니다: ${actor.id}`);
  const doc = snap.data() as UserDoc;
  const createdAtMs = doc.createdAt.toDate().getTime();
  const usedCount = typeof doc.qrUsedCount === "number" ? doc.qrUsedCount : 0;
  const gate = evaluateQrGate(Date.now(), createdAtMs, usedCount, limits);
  return {
    role: "temp_user",
    blocked: gate.blocked,
    reason: gate.reason,
    remainingMs: gate.remainingMs,
    remainingCount: gate.remainingCount,
    limits,
  };
}
