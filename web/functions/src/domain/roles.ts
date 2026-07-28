/**
 * 계정 역할 위계 — WPF `MCPhoto.Core.Models.UserRole` + `UserRoleExtensions`(C#)의 순수 이식.
 *
 * 서버가 클라이언트 UI와 무관하게 이 규칙으로 인가를 재검증한다(설계 §5.2). 위계 temp_user < user < manager < admin.
 * 근거: src/MCPhoto.Core/Models/UserRole.cs
 *
 * it13(임시 유저): "temp_user"를 최하위 역할로 추가한다(설계 §3.4). 관리 위계 비교는 enum 서수가 아닌
 * MANAGE_RANK(명시 배치)로 판정 — 역할 추가 시 여기만 갱신하면 되어 서수 재배치 붕괴가 없다.
 */

/** Firestore `users.role` 저장 문자열과 1:1 대응. it13: "temp_user" 추가(최하위). */
export type UserRole = "temp_user" | "user" | "manager" | "admin";

/**
 * 관리 위계 랭크(서수 아님 — canManage 전용). C# ManageRank와 1:1(설계 §3.2·§3.4).
 * temp_user(0) < user(1) < manager(2) < admin(3). 저장은 문자열이므로 이 배치값 변경은 저장 계약에 무해.
 */
const MANAGE_RANK: Record<UserRole, number> = {
  temp_user: 0,
  user: 1,
  manager: 2,
  admin: 3,
};

/** 허용된 역할 문자열인지 판정(입력 검증용 화이트리스트). */
export function isUserRole(value: unknown): value is UserRole {
  return (
    value === "temp_user" ||
    value === "user" ||
    value === "manager" ||
    value === "admin"
  );
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
    case "temp_user":
      return "temp_user";
    default:
      return "user"; // "user" 및 미지원값(오탈자 시 최소 권한)
  }
}

/**
 * power 계정(사용자 관리·공용 기본 프레임 관리 권한) 여부.
 * temp_user는 power 아님(설계 §3.4).
 * 근거: UserRoleExtensions.IsPower (UserRole.cs:35)
 */
export function isPower(role: UserRole): boolean {
  return role === "manager" || role === "admin";
}

/**
 * actingRole이 생성할 수 있는 역할 목록: admin→[temp_user,user,manager], manager→[temp_user,user], 그 외→[].
 * (admin→admin 불가: 최종 1인 규칙). it13: temp_user를 user와 동일 위계에 추가(설계 §3.4).
 * 근거: UserRoleExtensions.CreatableRoles (UserRole.cs:41-46)
 */
export function creatableRoles(actingRole: UserRole): UserRole[] {
  switch (actingRole) {
    case "admin":
      return ["temp_user", "user", "manager"];
    case "manager":
      return ["temp_user", "user"];
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
 * 예) manager는 admin 관리 불가, admin은 전부 관리 가능. temp_user는 최하위(누구나 관리 가능).
 * 서수가 아닌 MANAGE_RANK로 비교 — 역할 추가에도 안전(설계 §3.2).
 * 근거: UserRoleExtensions.CanManage (UserRole.cs:56-57)
 */
export function canManage(actingRole: UserRole, targetRole: UserRole): boolean {
  return MANAGE_RANK[targetRole] <= MANAGE_RANK[actingRole];
}

/**
 * it13: 역할 변경(setRole) 권한 매트릭스(순수). 서버가 강제하며 클라 전달값을 신뢰하지 않는다.
 * actorRole이 currentRole 계정을 targetRole로 바꿀 수 있는지 판정한다.
 *
 * 규칙(설계 확정):
 *   - 승격(랭크 상승) = **admin 전용**.
 *   - user → temp_user 강등 = admin + manager.
 *   - **Admin**: target ∈ {temp_user, user, manager}. admin 지정 불가(최종 1인 규칙),
 *                admin 대상 변경 불가. 그 외 임의 전환 허용(current 랭크 무관).
 *   - **Manager**: 오직 `현재=user → 목표=temp_user` 강등만. 그 외 전부 거부
 *                  (승격 금지, manager/admin 대상 금지, temp_user 대상 금지).
 *   - **그 외(user/temp_user)**: 전부 거부.
 *
 * no-op(current===target)도 명시 규칙에 없으면 거부한다(라우트가 무의미한 변경을 통과시키지 않도록).
 */
export function canSetRole(
  actorRole: UserRole,
  currentRole: UserRole,
  targetRole: UserRole
): boolean {
  // admin 지정은 누구도 불가(최종 1인 규칙). admin 대상 변경도 불가.
  if (targetRole === "admin") return false;
  if (currentRole === "admin") return false;

  if (actorRole === "admin") {
    // target은 위에서 admin 제외됨 → temp_user/user/manager 중 하나. 허용.
    return true;
  }
  if (actorRole === "manager") {
    // 오직 user → temp_user 강등만.
    return currentRole === "user" && targetRole === "temp_user";
  }
  return false;
}
