import type { SessionUser } from "./sessionUser";
import { assignableRoles } from "../roles/roleChangePolicy";
import {
  canManage,
  canResetPin,
  hierarchyRank,
  isPower,
  type UserRole,
} from "../roles/userRole";

/**
 * 계정 관리 화면의 권한 판정 — analysis/60 §1·§2 · analysis/31 §4.5~§4.7 (순수)
 *
 * ⚠️ **판정을 여기서 새로 만들지 않는다.** `isPower`·`canManage`·`canResetPin`·`assignableRoles`는
 *    이미 `roles/userRole.ts`·`roles/roleChangePolicy.ts`에 있고 `docs/spec-vectors/role-matrix.json`이
 *    양 플랫폼에서 고정한다. 이 파일은 **조합만** 한다.
 * ⚠️ 화면은 역할 문자열을 비교하지 않는다 — `buildUserRow`가 만든 객체만 보고 렌더한다
 *    (정적 검사 ACC-1). 비교가 화면으로 새면 서버 매트릭스와 조용히 갈라진다.
 */

/** 관리자 도구 진입(사용자 관리). analysis/60 §2 — power만. */
export function canOpenUserMgmt(role: UserRole | null): boolean {
  return role !== null && isPower(role);
}

/**
 * 전역 TempUser 한도 편집. **admin만**이다(analysis/60 §2 · 31 §4.9 `requireAdmin`).
 * ⚠️ `isPower`로 넓히지 마라 — manager가 눌러도 서버가 403을 준다.
 */
export function canEditGlobalLimits(role: UserRole | null): boolean {
  return role === "admin";
}

/** [키오스크 종료]. Windows `ExitApp`이 관리자 도구(power 전용) 안에 있는 것과 같은 게이트. */
export function canExitKiosk(role: UserRole | null): boolean {
  return canOpenUserMgmt(role);
}

/**
 * 목록 정렬 — **역할 위계 내림차순, 동급은 가입일 오름차순**(03 §14).
 *
 * ⚠️ `createdAt`은 서버가 빈 문자열로 줄 수 있다(`parseSessionUser`의 폴백). 빈 값은 **맨 뒤**로
 *    보낸다 — 문자열 비교로 두면 빈 값이 항상 "가장 먼저 가입"이 되어 admin 위로 올라간다.
 * ⚠️ 입력 배열을 변형하지 않는다(순수) — 복사본을 정렬해 돌려준다.
 */
export function sortManagedUsers(users: readonly SessionUser[]): SessionUser[] {
  return [...users].sort((a, b) => {
    const rank = hierarchyRank(b.role) - hierarchyRank(a.role);
    if (rank !== 0) return rank;

    const aEmpty = a.createdAt.length === 0;
    const bEmpty = b.createdAt.length === 0;
    if (aEmpty !== bEmpty) return aEmpty ? 1 : -1;
    if (!aEmpty && a.createdAt !== b.createdAt) return a.createdAt < b.createdAt ? -1 : 1;

    // 마지막 tiebreak는 id다 — 같은 입력이 항상 같은 순서를 내야 화면이 흔들리지 않는다.
    return a.id < b.id ? -1 : a.id > b.id ? 1 : 0;
  });
}

/** 행 1개의 능력. 화면은 이 객체만 보고 렌더한다. */
export interface UserRowPolicy {
  readonly user: SessionUser;
  readonly isSelf: boolean;
  /** power AND 동급 이하 AND 자기 아님. */
  readonly canDelete: boolean;
  /** power AND **엄격히 낮은 위계** AND 자기 아님. */
  readonly canResetPin: boolean;
  /** 빈 배열이면 콤보를 렌더하지 않는다(자기 행 포함). */
  readonly assignableRoles: readonly UserRole[];
}

/**
 * 행 정책 1건.
 *
 * ⚠️ `canDelete`에서 `isPower`를 **빼면 안 된다**. `canManage`는 동급을 허용하므로
 *    `temp_user`가 다른 `temp_user`를 "관리 가능"으로 계산한다(analysis/60 §1.3 경고).
 * ⚠️ 반대로 `canResetPin`에는 `isPower`가 **이미 들어 있다** — 중복해서 걸지 않는다.
 *    붙여도 결과는 같지만 두 축이 갈라졌을 때 어느 쪽이 진실원인지 모호해진다.
 * ⚠️ manager → manager는 `canDelete === true`인데 `canResetPin === false`다. 비대칭이 규격이며
 *    "일관성"을 이유로 고치지 마라(analysis/60 §1.3.1).
 */
export function buildUserRow(actor: SessionUser, target: SessionUser): UserRowPolicy {
  const isSelf = actor.id === target.id;
  return {
    user: target,
    isSelf,
    canDelete: isPower(actor.role) && canManage(actor.role, target.role) && !isSelf,
    canResetPin: canResetPin(actor.role, target.role) && !isSelf,
    assignableRoles: isSelf ? [] : assignableRoles(actor.role, target.role),
  };
}

/** 목록 전체. 내부에서 `sortManagedUsers`를 적용한다(정렬은 고정이다 — 03 §14). */
export function buildUserRows(
  actor: SessionUser,
  users: readonly SessionUser[],
): readonly UserRowPolicy[] {
  return sortManagedUsers(users).map((target) => buildUserRow(actor, target));
}
