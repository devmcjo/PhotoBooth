/**
 * 계정 역할 위계 — WPF `MCPhoto.Core.Models.UserRole` + `UserRoleExtensions`(C#)의 순수 이식.
 *
 * 서버가 클라이언트 UI와 무관하게 이 규칙으로 인가를 재검증한다(설계 §5.2). 순서 User < Manager < Admin.
 * 근거: src/MCPhoto.Core/Models/UserRole.cs
 */

/** Firestore `users.role` 저장 문자열과 1:1 대응. */
export type UserRole = "user" | "manager" | "admin";

/** 역할 위계 서수(enum 순서 User=0 < Manager=1 < Admin=2). CanManage 비교의 기준. */
const RANK: Record<UserRole, number> = {
  user: 0,
  manager: 1,
  admin: 2,
};

/** 허용된 역할 문자열인지 판정(입력 검증용 화이트리스트). */
export function isUserRole(value: unknown): value is UserRole {
  return value === "user" || value === "manager" || value === "admin";
}

/**
 * Firestore 저장값 → UserRole. 미지원 값은 최소 권한(user)으로 폴백.
 * 근거: UserRoleExtensions.ParseRole (UserRole.cs:27-32)
 */
export function parseRole(value: string | null | undefined): UserRole {
  switch (value) {
    case "admin":
      return "admin";
    case "manager":
      return "manager";
    default:
      return "user";
  }
}

/**
 * power 계정(사용자 관리·공용 기본 프레임 관리 권한) 여부.
 * 근거: UserRoleExtensions.IsPower (UserRole.cs:35)
 */
export function isPower(role: UserRole): boolean {
  return role === "manager" || role === "admin";
}

/**
 * actingRole이 생성할 수 있는 역할 목록: admin→[user,manager], manager→[user], 그 외→[].
 * (admin→admin 불가: 최종 1인 규칙)
 * 근거: UserRoleExtensions.CreatableRoles (UserRole.cs:41-46)
 */
export function creatableRoles(actingRole: UserRole): UserRole[] {
  switch (actingRole) {
    case "admin":
      return ["user", "manager"];
    case "manager":
      return ["user"];
    default:
      return [];
  }
}

/**
 * actingRole이 role 계정을 생성할 권한이 있는지(생성 게이트).
 * 근거: UserRoleExtensions.CanCreate (UserRole.cs:49-50)
 */
export function canCreate(actingRole: UserRole, role: UserRole): boolean {
  return creatableRoles(actingRole).includes(role);
}

/**
 * actingRole이 targetRole 계정을 관리(삭제·비번초기화 등)할 수 있는지: **자신과 같거나 낮은 역할만**.
 * 예) manager는 admin 관리 불가, admin은 전부 관리 가능.
 * 근거: UserRoleExtensions.CanManage (UserRole.cs:56-57)
 */
export function canManage(actingRole: UserRole, targetRole: UserRole): boolean {
  return RANK[targetRole] <= RANK[actingRole];
}
