/**
 * 계정 서비스 — Firestore users 조작(로그인/CRUD/역할).
 * WPF `AccountService`(C#)의 서버 이식. 역할 위계·비번 해시는 서버가 강제(설계 §5.2, §7).
 *
 * 근거: src/MCPhoto.Firebase/AccountService.cs, src/MCPhoto.Core/Accounts/IAccountService.cs
 */
import { randomBytes } from "node:crypto";
import { Timestamp } from "firebase-admin/firestore";
import { loadConfig } from "../config";
import { db } from "../firebase";
import { deriveAccountId } from "../domain/accountId";
import { hashPassword, verifyPassword } from "../domain/password";
import {
  canCreate,
  canManage,
  canSetRole,
  parseRole,
  UserRole,
} from "../domain/roles";
import {
  evaluateQrGate,
  QrGateReason,
  TempUserLimits,
} from "../domain/tempUserLimit";
import {
  RESET_TTL_SECONDS,
  VERIFY_TTL_SECONDS,
} from "../domain/tokens";
import { HttpError } from "../http/errors";
import { loadTempUserLimits } from "./config";
import { UserDoc, UserResponse } from "./dto";
import { getEmailSender } from "./email";
import { deleteAllFramesByUser } from "./frames";
import { consumeByCode, consumeByToken, issueToken } from "./tokens";

const COLLECTION = "users";

/**
 * 이메일 유일성 초과 메시지(설계 §3.4 규칙 C4). 생성/변경 시점(verified 충돌)과
 * verify 시점(taken) 모두 이 정확한 문구를 사용한다. 변경 금지.
 */
const EMAIL_LIMIT_EXCEEDED_MESSAGE = "해당 이메일로 생성 가능한 계정 수를 초과하였습니다.";

/** UserDoc → 응답(비밀번호/해시·토큰 제거, email·emailVerified 포함, §8.5). */
function toResponse(doc: UserDoc): UserResponse {
  return {
    id: doc.id,
    role: parseRole(doc.role),
    createdAt: doc.createdAt.toDate().toISOString(),
    email: doc.email ?? null,
    emailVerified: doc.emailVerified === true,
  };
}

/**
 * verify/reset 링크 URL 조립. hostingBaseUrl 미설정(dev)이면 상대 경로만.
 * 링크 방식(웹 verify 페이지)은 후속이지만, 이메일 본문에 실릴 링크는 지금부터 조립해 둔다(설계 §5.2·§5.3).
 */
function buildLink(kind: "verify" | "reset", token: string): string {
  const base = (loadConfig().hostingBaseUrl ?? "").replace(/\/+$/, "");
  const path = kind === "verify" ? "verify" : "reset";
  return `${base}/${path}?token=${encodeURIComponent(token)}`;
}

/**
 * verify 토큰 발급 + 인증 메일 발송(발송 실패는 삼켜 로그만 — 가용성·열거 방지, §5.2·§10.1).
 * 계정 생성/이메일 등록·변경 성공 직후 호출한다.
 */
async function issueAndSendVerification(userId: string, email: string): Promise<void> {
  const issued = await issueToken(userId, "verify_email", email, VERIFY_TTL_SECONDS);
  try {
    const sender = getEmailSender(loadConfig());
    await sender.sendVerification(email, {
      link: buildLink("verify", issued.token),
      code: issued.code,
      accountId: userId,
    });
  } catch (err) {
    // 발송 실패는 계정 생성/변경을 롤백하지 않는다. 재발송 경로로 복구(§5.2).
    console.error(`인증 메일 발송 실패(account=${userId}):`, err);
  }
}

/** 로그인 성공 결과(토큰 발급에 필요한 최소 정보). */
export interface LoginResult {
  id: string;
  role: UserRole;
  user: UserResponse;
}

/**
 * 로그인: users/{id} 로드 → 비번 검증(해시 또는 레거시 평문). 실패 시 null(현행 계약).
 * 레거시 평문 매칭 시 즉시 bcrypt 해시로 교체 저장(지연 마이그레이션, 설계 §7.1-b).
 */
export async function login(id: string, password: string): Promise<LoginResult | null> {
  const ref = db().collection(COLLECTION).doc(id);
  const snap = await ref.get();
  if (!snap.exists) return null;

  const doc = snap.data() as UserDoc;
  const { matched, needsMigration } = await verifyPassword(password, doc.password ?? "");
  if (!matched) return null;

  if (needsMigration) {
    // 평문 → 해시 교체(로그인 성공 경로에서 1회). 실패해도 로그인은 성공 처리(다음 로그인 재시도).
    try {
      const newHash = await hashPassword(password);
      await ref.update({ password: newHash });
    } catch {
      // 마이그레이션 실패는 로그인 자체를 막지 않는다(가용성 우선).
    }
  }

  const role = parseRole(doc.role);
  return { id: doc.id, role, user: toResponse(doc) };
}

/**
 * 이메일 유일성 완화 검사(설계 §3.4 규칙 C4). 생성/변경 시점에는
 * **이미 인증 완료(emailVerified===true)한 다른 계정**이 같은 email을 가질 때만 409.
 * 미인증(emailVerified!==true) 중복은 허용한다(인증 시점에 최종 강제 — markEmailVerified).
 * @param excludeId 자기 자신은 제외(email 변경 시 동일 계정 재검사 방지).
 */
async function ensureEmailNotVerifiedElsewhere(email: string, excludeId?: string): Promise<void> {
  const snap = await db().collection(COLLECTION).where("email", "==", email).get();
  const conflict = snap.docs.find(
    (d) => d.id !== excludeId && (d.data() as UserDoc).emailVerified === true
  );
  if (conflict) {
    throw HttpError.conflict(EMAIL_LIMIT_EXCEEDED_MESSAGE);
  }
}

/**
 * 계정 생성. actingRole(토큰에서 도출)이 role을 생성할 권한이 없으면 403.
 * 중복 id면 409. 비번은 해시로 저장. email이 주어지면 유일성 검사 후 unverified로 저장하고
 * 인증 메일을 발송한다(설계 §5.1·§5.2·§8.1).
 * 근거: AccountService.CreateAsync (AccountService.cs:54-70)
 */
export async function createAccount(
  id: string,
  password: string,
  role: UserRole,
  email: string | null,
  actingRole: UserRole
): Promise<UserResponse> {
  if (!canCreate(actingRole, role)) {
    throw HttpError.forbidden(`${actingRole} 권한으로 ${role} 계정을 생성할 수 없습니다.`);
  }

  const ref = db().collection(COLLECTION).doc(id);
  const existing = await ref.get();
  if (existing.exists) {
    throw HttpError.conflict(`이미 존재하는 아이디입니다: ${id}`);
  }

  if (email) {
    await ensureEmailNotVerifiedElsewhere(email);
  }

  const now = Timestamp.now();
  const doc: UserDoc = {
    id,
    password: await hashPassword(password),
    role,
    createdAt: now,
    email: email ?? null,
    emailVerified: false,
  };
  await ref.set(doc);

  // email이 있으면 인증 메일 발송(발송 실패는 삼켜지고 계정 생성은 유지, §5.2).
  if (email) {
    await issueAndSendVerification(id, email);
  }

  return toResponse(doc);
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

/**
 * 비밀번호 변경. 본인이거나 파워가 대상을 관리할 수 있어야 한다(위계 재검증).
 * 근거: AccountService.ChangePasswordAsync (AccountService.cs:72-77) + 설계 §6.2(본인/파워)
 */
export async function changePassword(
  targetId: string,
  newPassword: string,
  actor: { id: string; role: UserRole }
): Promise<void> {
  const targetRole = await getRole(targetId);
  const isSelf = actor.id === targetId;
  if (!isSelf && !canManage(actor.role, targetRole)) {
    throw HttpError.forbidden("해당 계정의 비밀번호를 변경할 권한이 없습니다.");
  }
  await db()
    .collection(COLLECTION)
    .doc(targetId)
    .update({ password: await hashPassword(newPassword) });
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
 * 역할 지정(it13 권한 매트릭스, 서버 강제 — 클라 전달 actor 무시, actor는 JWT에서 도출).
 *   - Admin: target ∈ {temp_user, user, manager}. admin 지정/admin 대상 불가.
 *   - Manager: 오직 현재=user → 목표=temp_user 강등만. 그 외 403.
 *   - 승격(랭크 상승)은 admin 전용, user→temp_user 강등은 admin+manager.
 * 판정은 순수 함수 canSetRole(roles.ts)에 위임. 대상 계정 없으면 getRole이 404.
 * 근거: AccountService.SetRoleAsync (AccountService.cs:96-101) + 설계 it13 §3.
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
// item1a: 이메일 인증 + 비밀번호 재설정 (설계 §5·§6·§8)
// ─────────────────────────────────────────────────────────────────────────────

/**
 * id 우선, 없으면 email로 계정 조회. 없으면 null(열거 방지 위해 호출측이 사유 노출 금지).
 *
 * BE-4(§3.4)로 email 유일성이 완화되어 **미인증 중복 email**이 존재할 수 있다. email 경로 조회 시
 * 그 email을 **소유(verified)한 계정**을 우선 반환한다(없으면 임의의 미인증 계정 1건). 이래야
 * password-reset/verify-request가 올바른 계정으로 라우팅된다(limit(1)은 순서 미보장이라 사용 금지).
 */
async function findByIdOrEmail(idOrEmail: string): Promise<UserDoc | null> {
  const byId = await db().collection(COLLECTION).doc(idOrEmail).get();
  if (byId.exists) return byId.data() as UserDoc;

  // email 조회(소문자 정규화된 값으로 저장돼 있으므로 소문자로 비교).
  const email = idOrEmail.trim().toLowerCase();
  const snap = await db().collection(COLLECTION).where("email", "==", email).get();
  if (snap.empty) return null;
  const docs = snap.docs.map((d) => d.data() as UserDoc);
  // verified 계정 우선(email 소유자). 없으면 첫 미인증 계정.
  return docs.find((d) => d.emailVerified === true) ?? docs[0];
}

/**
 * SSO 전용 자동 생성 계정의 비밀번호: 아무도 모르는 랜덤 값의 bcrypt 해시(로그인 불가 sentinel).
 * id/pw 로그인은 이 해시와 절대 매칭되지 않으므로 SSO 경로로만 진입 가능(USER-DECISION D-B2, §2.2).
 */
async function makeSentinelPasswordHash(): Promise<string> {
  return hashPassword(randomBytes(32).toString("base64url"));
}

/**
 * email로만 계정을 조회(자동 생성/승격 분기용). id 조회는 하지 않는다(SSO는 email 신원만 신뢰).
 * 소문자 정규화된 email 필드로 비교.
 */
async function findByEmailField(email: string): Promise<UserDoc | null> {
  const snap = await db().collection(COLLECTION).where("email", "==", email).limit(1).get();
  if (snap.empty) return null;
  return snap.docs[0].data() as UserDoc;
}

/**
 * item1b: Google SSO 로그인(설계 §2.2 B-BE-1 / D-B1·D-B2, BE-2 자동 생성 재설계).
 * 검증된 Google email(소문자)로 로그인한다. Google이 email 소유를 이미 증명했으므로:
 *   - 계정 없음 → user 역할로 **자동 생성**(emailVerified=true, pw=랜덤 sentinel 해시로 id/pw 로그인 불가).
 *   - 미검증 기존 계정(emailVerified!==true) → **승격**(emailVerified=true) 후 로그인(role/pw 불변, 권한 상승 없음).
 *   - 검증된 기존 계정 → 기존대로 로그인(변경 없음).
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

/** 기존 계정으로 SSO 로그인. 미검증이면 emailVerified=true로 승격 후 로그인(role/pw 불변). */
async function loginExistingGoogleAccount(
  doc: UserDoc,
  normalized: string
): Promise<LoginResult | null> {
  // 방어: email 필드가 조회 email과 다르면(정규화 불일치 등) 로그인 거부.
  if ((doc.email ?? null) !== normalized) return null;

  let effective = doc;
  if (doc.emailVerified !== true) {
    // 미검증 기존 계정 승격(Google이 email 소유 증명). role/password는 건드리지 않는다.
    await db().collection(COLLECTION).doc(doc.id).update({ emailVerified: true });
    effective = { ...doc, emailVerified: true };
  }
  const role = parseRole(effective.role);
  return { id: effective.id, role, user: toResponse(effective) };
}

/**
 * 계정 없음 → user 역할 자동 생성(원자적). create는 문서 부재 시에만 성공.
 * 경합으로 방금 동일 id가 만들어졌다면 create가 throw → null 반환(호출측이 재조회).
 * 반환 null이 곧 "생성 실패(경합)"를 의미한다.
 */
async function createGoogleAccount(normalized: string): Promise<LoginResult | null> {
  const id = await deriveAccountId(normalized, async (candidate) => {
    const snap = await db().collection(COLLECTION).doc(candidate).get();
    return snap.exists;
  });

  const now = Timestamp.now();
  const doc: UserDoc = {
    id,
    password: await makeSentinelPasswordHash(),
    role: "user",
    createdAt: now,
    email: normalized,
    emailVerified: true,
  };

  try {
    // create: 이미 존재하면 throw(경합). set과 달리 덮어쓰지 않는다.
    await db().collection(COLLECTION).doc(id).create(doc);
  } catch {
    // 경합(동시 생성) — 호출측이 재조회로 로그인 처리.
    return null;
  }
  return { id: doc.id, role: "user", user: toResponse(doc) };
}

/**
 * BE-3: self-signup(비로그인 회원가입). role="user" **서버 강제**(클라 지정 불가 — 권한 상승 차단).
 * canCreate 게이트 없음(createAccount와 달리 자기 역할 생성 허용). id 중복이면 409.
 * email이 주어지면 verified 계정 충돌 검사(ensureEmailNotVerifiedElsewhere) 후 unverified 생성 + verify 메일.
 * 근거: 설계 §2.2 B-BE-2, §5(계약 요약).
 */
export async function registerSelf(
  id: string,
  password: string,
  email: string | null
): Promise<UserResponse> {
  const ref = db().collection(COLLECTION).doc(id);
  const existing = await ref.get();
  if (existing.exists) {
    throw HttpError.conflict(`이미 존재하는 아이디입니다: ${id}`);
  }

  if (email) {
    await ensureEmailNotVerifiedElsewhere(email);
  }

  const now = Timestamp.now();
  const doc: UserDoc = {
    id,
    password: await hashPassword(password),
    role: "user", // 서버 강제(body로 지정 불가).
    createdAt: now,
    email: email ?? null,
    emailVerified: false,
  };
  await ref.set(doc);

  // email이 있으면 인증 메일 발송(발송 실패는 삼켜지고 가입은 유지, §5.2와 동일).
  if (email) {
    await issueAndSendVerification(id, email);
  }

  return toResponse(doc);
}

/**
 * 이메일 등록/변경(본인/파워, 위계). email 변경 시 반드시 emailVerified=false로 리셋하고
 * 새 email 소유 재확인(verify 메일 발송) — 핵심 보안(설계 §7-2·§8.3).
 */
export async function setEmail(
  targetId: string,
  email: string,
  actor: { id: string; role: UserRole }
): Promise<void> {
  const targetRole = await getRole(targetId); // 없으면 404
  const isSelf = actor.id === targetId;
  if (!isSelf && !canManage(actor.role, targetRole)) {
    throw HttpError.forbidden("해당 계정의 이메일을 변경할 권한이 없습니다.");
  }

  await ensureEmailNotVerifiedElsewhere(email, targetId);

  await db()
    .collection(COLLECTION)
    .doc(targetId)
    .update({ email, emailVerified: false });

  // 소유 확인 메일 발송(관리자가 넣어도 자동 verified 아님, §7-2).
  await issueAndSendVerification(targetId, email);
}

/**
 * 이메일 인증 재발송 요청(열거 방지: 존재/상태 무관 동일 처리, 반환 void).
 * 계정이 있고 email이 있으며 아직 미인증이면 verify 토큰 재발급 + 메일. 이미 인증됐거나 없으면 no-op.
 */
export async function requestEmailVerification(idOrEmail: string): Promise<void> {
  const doc = await findByIdOrEmail(idOrEmail);
  if (!doc || !doc.email || doc.emailVerified === true) {
    return; // no-op(응답은 호출측에서 202로 동일)
  }
  await issueAndSendVerification(doc.id, doc.email);
}

/**
 * 이메일 인증 확인 결과(설계 §3.4 규칙 C4).
 *   - verified:true → 인증 성공(마킹 완료).
 *   - reason "mismatch" → 코드/토큰 무효·만료 또는 email 불일치(라우트가 401).
 *   - reason "taken" → 이미 다른 계정이 이 email을 verified(라우트가 409 + 초과 메시지).
 */
export type VerifyEmailResult =
  | { verified: true }
  | { verified: false; reason: "mismatch" | "taken" };

/** markEmailVerified 결과(3-값). */
export type MarkVerifiedResult =
  | { ok: true }
  | { ok: false; reason: "mismatch" | "taken" };

/**
 * verify 성공 시 emailVerified=true로 마킹(설계 §3.4-2). Firestore 트랜잭션으로
 * "다른 계정이 이미 이 email을 verified" 검사 + 마킹을 원자화한다(경합 방지, USER-DECISION D-C1).
 *   - 대상 계정 부재 또는 현재 email이 verifiedEmail과 불일치 → {ok:false, reason:"mismatch"}.
 *   - 다른 계정이 이미 이 email을 verified → {ok:false, reason:"taken"}(마킹 거부).
 *   - 그 외 → emailVerified=true 마킹 후 {ok:true}.
 */
async function markEmailVerified(
  userId: string,
  verifiedEmail: string
): Promise<MarkVerifiedResult> {
  const col = db().collection(COLLECTION);
  const targetRef = col.doc(userId);
  // 같은 email을 가진 계정들(대상 포함 가능). 트랜잭션에서 read로 재확정.
  const emailQuery = col.where("email", "==", verifiedEmail);

  return db().runTransaction(async (tx) => {
    const targetSnap = await tx.get(targetRef);
    if (!targetSnap.exists) return { ok: false, reason: "mismatch" };
    const doc = targetSnap.data() as UserDoc;
    // 토큰 발급 후 email이 다시 바뀐 경우는 검증 무효(현재 email과 대조).
    if ((doc.email ?? null) !== verifiedEmail) return { ok: false, reason: "mismatch" };

    // 다른 계정이 이미 이 email을 verified 했는지(원자적으로 재확인).
    const dupSnap = await tx.get(emailQuery);
    const takenByOther = dupSnap.docs.some(
      (d) => d.id !== userId && (d.data() as UserDoc).emailVerified === true
    );
    if (takenByOther) return { ok: false, reason: "taken" };

    tx.update(targetRef, { emailVerified: true });
    return { ok: true };
  });
}

/** markEmailVerified 결과를 라우트용 VerifyEmailResult로 변환(성공/실패 사유 유지). */
function toVerifyResult(marked: MarkVerifiedResult): VerifyEmailResult {
  return marked.ok ? { verified: true } : { verified: false, reason: marked.reason };
}

/** 이메일 인증 확인(링크 경로) — 결합 토큰. */
export async function confirmEmailVerificationByToken(
  userId: string,
  token: string
): Promise<VerifyEmailResult> {
  const res = await consumeByToken(userId, "verify_email", token);
  if (!res.ok) return { verified: false, reason: "mismatch" };
  return toVerifyResult(await markEmailVerified(userId, res.email));
}

/** 이메일 인증 확인(코드 경로, 키오스크) — id + 6자리 코드. */
export async function confirmEmailVerificationByCode(
  userId: string,
  code: string
): Promise<VerifyEmailResult> {
  const res = await consumeByCode(userId, "verify_email", code);
  if (!res.ok) return { verified: false, reason: "mismatch" };
  return toVerifyResult(await markEmailVerified(userId, res.email));
}

/**
 * 비밀번호 재설정 요청(열거 방지: 존재/상태 무관 동일 처리, 반환 void).
 * emailVerified=true + email!=null인 계정만 실제 reset 토큰 + 메일. 그 외 no-op(설계 §6.2·§8.4).
 */
export async function requestPasswordReset(idOrEmail: string): Promise<void> {
  const doc = await findByIdOrEmail(idOrEmail);
  if (!doc || !doc.email || doc.emailVerified !== true) {
    return; // no-op(응답은 호출측에서 202로 동일)
  }
  const issued = await issueToken(doc.id, "password_reset", doc.email, RESET_TTL_SECONDS);
  try {
    const sender = getEmailSender(loadConfig());
    await sender.sendPasswordReset(doc.email, {
      link: buildLink("reset", issued.token),
      code: issued.code,
      accountId: doc.id,
    });
  } catch (err) {
    // 발송 실패는 삼켜 로그만(열거 방지·가용성, §10.1).
    console.error(`재설정 메일 발송 실패(account=${doc.id}):`, err);
  }
}

/** 재설정된 새 비밀번호를 bcrypt 해시로 저장(토큰 소비는 호출 전 완료). */
async function applyNewPassword(userId: string, newPassword: string): Promise<void> {
  await db()
    .collection(COLLECTION)
    .doc(userId)
    .update({ password: await hashPassword(newPassword) });
}

/**
 * 비밀번호 재설정 확인(링크 경로) — 결합 토큰 + 새 비번.
 * 토큰 대조·만료·1회성 통과 시 새 비번 저장. 실패면 false(호출측이 400/401로 매핑).
 */
export async function confirmPasswordResetByToken(
  userId: string,
  token: string,
  newPassword: string
): Promise<boolean> {
  const res = await consumeByToken(userId, "password_reset", token);
  if (!res.ok) return false;
  await applyNewPassword(userId, newPassword);
  return true;
}

/**
 * 비밀번호 재설정 확인(코드 경로) — idOrEmail + 6자리 코드 + 새 비번(설계 §8.4).
 * idOrEmail로 계정을 조회(email 입력도 허용). 계정 없음·코드 불일치는 모두 false(사유 노출 최소화).
 */
export async function confirmPasswordResetByCode(
  idOrEmail: string,
  code: string,
  newPassword: string
): Promise<boolean> {
  const doc = await findByIdOrEmail(idOrEmail);
  if (!doc) return false;
  const res = await consumeByCode(doc.id, "password_reset", code);
  if (!res.ok) return false;
  await applyNewPassword(doc.id, newPassword);
  return true;
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
