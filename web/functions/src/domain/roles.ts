/**
 * 계정 역할 위계 — WPF `MCPhoto.Core.Models.UserRole` + `UserRoleExtensions`(C#)의 순수 이식.
 *
 * 서버가 클라이언트 UI와 무관하게 이 규칙으로 인가를 재검증한다(설계 §5.2). 위계 temp_user < user < manager < admin.
 * 근거: src/MCPhoto.Core/Models/UserRole.cs
 *
 * it13(임시 유저): "temp_user"를 최하위 역할로 추가한다(설계 §3.4). 관리 위계 비교는 enum 서수가 아닌
 * MANAGE_RANK(명시 배치)로 판정 — 역할 추가 시 여기만 갱신하면 되어 서수 재배치 붕괴가 없다.
 *
 * it16(고급 유저): "advanced_user"를 user와 manager 사이(랭크 2)에 추가한다(설계 §3.2 동결표).
 * 위계 temp_user < user < advanced_user < manager < admin. **power 축은 확장하지 않는다** —
 * 프레임 저작 권한은 클라 측 별개 축(C# CanWriteFrames)이며 서버 power(manager/admin)와 섞지 않는다(설계 §5.2).
 */

/** Firestore `users.role` 저장 문자열과 1:1 대응. it13: "temp_user" 추가. it16: "advanced_user" 추가. */
export type UserRole =
  | "temp_user"
  | "user"
  | "advanced_user"
  | "manager"
  | "admin";

/**
 * 관리 위계 랭크(서수 아님 — canManage 전용). C# ManageRank와 1:1(설계 §3.2 동결표).
 * temp_user(0) < user(1) < advanced_user(2) < manager(3) < admin(4).
 * 저장은 문자열이므로 이 배치값 변경은 저장 계약에 무해(it16에서 manager·admin이 2·3 → 3·4로 이동).
 */
const MANAGE_RANK: Record<UserRole, number> = {
  temp_user: 0,
  user: 1,
  advanced_user: 2,
  manager: 3,
  admin: 4,
};

/** 허용된 역할 문자열인지 판정(입력 검증용 화이트리스트). */
export function isUserRole(value: unknown): value is UserRole {
  return (
    value === "temp_user" ||
    value === "user" ||
    value === "advanced_user" ||
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
    case "advanced_user":
      return "advanced_user";
    case "temp_user":
      return "temp_user";
    default:
      return "user"; // "user" 및 미지원값(오탈자 시 최소 권한)
  }
}

/**
 * power 계정(사용자 관리·공용 기본 프레임 관리 권한) 여부.
 * temp_user는 power 아님(설계 §3.4).
 *
 * ⚠️ it16: 고급 유저(랭크 2)는 **power가 아니다**(설계 §3.2 동결표 · §5.2). 이 함수에 새 역할을
 *    추가하면 `requirePower` 뒤의 프레임 쓰기 라우트가 열려 설계 계약이 깨진다 —
 *    회귀는 `__tests__/authGates.test.ts`가 잡는다.
 * 근거: UserRoleExtensions.IsPower (UserRole.cs:35)
 */
export function isPower(role: UserRole): boolean {
  return role === "manager" || role === "admin";
}

/**
 * actingRole이 생성할 수 있는 역할 목록(위계 오름차순): admin→[temp_user,user,advanced_user,manager],
 * manager→[temp_user,user,advanced_user], 그 외→[]. (admin→admin 불가: 최종 1인 규칙)
 *
 * it15에서 계정 생성 라우트가 폐지되어 프로덕션 호출자는 없다. 삭제하지 않고 목록만 갱신하는 이유:
 * 새 매트릭스(§3.3)와 어긋난 규칙이 훗날 되살아나는 드리프트를 막는다(설계 §3.6).
 * 근거: UserRoleExtensions.CreatableRoles (UserRole.cs:41-46)
 */
export function creatableRoles(actingRole: UserRole): UserRole[] {
  switch (actingRole) {
    case "admin":
      return ["temp_user", "user", "advanced_user", "manager"];
    case "manager":
      return ["temp_user", "user", "advanced_user"];
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
 * it16: manager가 자유 지정할 수 있는 하위 3역할 대역(설계 §3.3 규칙 4).
 * 이 대역 안에서는 승격·강등·no-op이 모두 허용된다(멱등 write).
 */
const LOWER_BAND: readonly UserRole[] = ["temp_user", "user", "advanced_user"];

/**
 * 역할 변경(setRole) 권한 매트릭스(순수). 서버가 강제하며 클라 전달값을 신뢰하지 않는다.
 * actorRole이 currentRole 계정을 targetRole로 바꿀 수 있는지 판정한다.
 *
 * 규칙(설계 §3.3 전수 표 — it16에서 it13의 "승격=admin 전용"이 하위 대역에서 완화된다):
 *   1) target === admin  → 거부(최종 1인 규칙)
 *   2) current === admin → 거부(admin 대상 변경 불가)
 *   3) actor === admin   → 허용(target ∈ 하위 대역 ∪ {manager})
 *   4) actor === manager → current·target 둘 다 LOWER_BAND일 때만 허용(승격 포함).
 *                          manager·admin 지정은 admin 전용, manager·admin 대상 변경도 불가.
 *   5) 그 외 actor(하위 대역 전원) → 전부 거부
 *
 * no-op(current===target)은 규칙 3·4에 포함되므로 허용된다(멱등 write, 설계 §3.3).
 * 클라이언트는 무변경을 서버로 보내지 않는다.
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
    // target은 위에서 admin 제외됨 → 하위 대역 또는 manager. 허용.
    return true;
  }
  if (actorRole === "manager") {
    // 하위 대역 내 자유 지정(manager 지정·manager 대상은 위 조건에서 제외되지 않으므로 여기서 거부).
    return LOWER_BAND.includes(currentRole) && LOWER_BAND.includes(targetRole);
  }
  return false;
}
