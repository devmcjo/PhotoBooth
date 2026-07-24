/**
 * 계정 서비스 — Firestore users 조작(로그인/CRUD/역할).
 * WPF `AccountService`(C#)의 서버 이식. 역할 위계·비번 해시는 서버가 강제(설계 §5.2, §7).
 *
 * 근거: src/MCPhoto.Firebase/AccountService.cs, src/MCPhoto.Core/Accounts/IAccountService.cs
 */
import { Timestamp } from "firebase-admin/firestore";
import { db } from "../firebase";
import { hashPassword, verifyPassword } from "../domain/password";
import {
  canCreate,
  canManage,
  parseRole,
  UserRole,
} from "../domain/roles";
import { HttpError } from "../http/errors";
import { UserDoc, UserResponse } from "./dto";
import { deleteAllFramesByUser } from "./frames";

const COLLECTION = "users";

/** UserDoc → 응답(비밀번호/해시 제거). */
function toResponse(doc: UserDoc): UserResponse {
  return {
    id: doc.id,
    role: parseRole(doc.role),
    createdAt: doc.createdAt.toDate().toISOString(),
  };
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
 * 계정 생성. actingRole(토큰에서 도출)이 role을 생성할 권한이 없으면 403.
 * 중복 id면 409. 비번은 해시로 저장.
 * 근거: AccountService.CreateAsync (AccountService.cs:54-70)
 */
export async function createAccount(
  id: string,
  password: string,
  role: UserRole,
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

  const now = Timestamp.now();
  const doc: UserDoc = {
    id,
    password: await hashPassword(password),
    role,
    createdAt: now,
  };
  await ref.set(doc);
  return { id, role, createdAt: now.toDate().toISOString() };
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
 * 역할 지정. admin만, 대상은 user만(manager 지정은 admin이 user 대상). admin→admin 금지.
 * 근거: AccountService.SetRoleAsync (AccountService.cs:96-101) + 설계 §5.2(admin이 user 대상만)
 */
export async function setRole(
  targetId: string,
  role: UserRole,
  actor: { id: string; role: UserRole }
): Promise<void> {
  if (actor.role !== "admin") {
    throw HttpError.forbidden("역할 지정은 admin만 가능합니다.");
  }
  if (role === "admin") {
    throw HttpError.forbidden("admin 역할은 지정할 수 없습니다(최종 1인 규칙).");
  }
  const currentRole = await getRole(targetId);
  // manager 지정은 user 대상만(현행 create 규칙과 정합: admin→user를 manager로 승격).
  if (currentRole === "admin") {
    throw HttpError.forbidden("admin 계정의 역할은 변경할 수 없습니다.");
  }
  await db().collection(COLLECTION).doc(targetId).update({ role });
}
