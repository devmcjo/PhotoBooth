/**
 * 계정 역할 — Windows `Models/UserRole.cs` 이식 (analysis/60 §1)
 *
 * ⚠️ **문자열 유니온을 진실값으로 쓴다.** Windows는 enum + `ToFirestoreValue()`로 나뉘어 있지만
 *    웹은 서버·Firestore·로그가 쓰는 snake_case 문자열이 곧 값이다(직렬화 경계가 없어 드리프트 불가).
 * ⚠️ **위계 비교에 배열 순서를 쓰지 않는다.** `hierarchyRank`가 유일한 랭크 정의다.
 */

export const USER_ROLES = ["temp_user", "user", "advanced_user", "manager", "admin"] as const;
export type UserRole = (typeof USER_ROLES)[number];

/** 미지원·오탈자 값의 폴백(최소 권한). */
export const DEFAULT_ROLE: UserRole = "user";

/** 서버·저장값 문자열 → 역할. 알 수 없는 값은 최소 권한으로 폴백한다. */
export function parseRole(value: string | null | undefined): UserRole {
  if (typeof value !== "string") return DEFAULT_ROLE;
  return (USER_ROLES as readonly string[]).includes(value) ? (value as UserRole) : DEFAULT_ROLE;
}

/** 역할 한글 표시 라벨(analysis/13 §14 카탈로그). */
export function roleLabel(role: UserRole): string {
  switch (role) {
    case "temp_user":
      return "임시 유저";
    case "user":
      return "사용자";
    case "advanced_user":
      return "고급 유저";
    case "manager":
      return "매니저";
    case "admin":
      return "관리자";
    default:
      return "사용자";
  }
}

/**
 * 위계 랭크. 서버 `MANAGE_RANK`와 동일해야 한다(analysis/60 §1).
 * 공개 목적은 **목록 정렬**이다 — 권한 판정에 부등식으로 직접 쓰지 않는다.
 */
export function hierarchyRank(role: UserRole): number {
  switch (role) {
    case "temp_user":
      return 0;
    case "user":
      return 1;
    case "advanced_user":
      return 2;
    case "manager":
      return 3;
    case "admin":
      return 4;
    default:
      return 0;
  }
}

/**
 * power 계정(사용자 관리 · 공용 기본 프레임 관리).
 * ⚠️ `advanced_user`는 **포함되지 않는다** — 프레임 저작 권한은 별개 축인 `canWriteFrames`다.
 */
export function isPower(role: UserRole): boolean {
  return role === "manager" || role === "admin";
}

/**
 * 프레임 쓰기 권한(생성·편집·삭제). `advanced_user` 이상.
 * ⚠️ `isPower`와 **별개 축**이다. `hierarchyRank` 부등식으로 쓰지 않는다 — 관리 위계에 역할이
 *    끼어들 때 저작 권한이 조용히 따라 움직이는 것을 막기 위해 명시 열거를 유지한다.
 */
export function canWriteFrames(role: UserRole): boolean {
  return role === "advanced_user" || role === "manager" || role === "admin";
}

/**
 * 관리(삭제 등) 가능 여부: **자신과 같거나 낮은 위계**.
 * ⚠️ 이 판정만으로는 비power도 통과한다 — 관리 액션 게이트는 `isPower`와 **함께** 쓴다.
 */
export function canManage(actingRole: UserRole, targetRole: UserRole): boolean {
  return hierarchyRank(targetRole) <= hierarchyRank(actingRole);
}

/**
 * 타 계정 PIN 재설정 권한. 관리 액션 중 **유일하게 "엄격히 낮은 위계"**만 허용한다.
 * power + 대상 위계가 자신보다 낮을 때만: admin→manager ○, manager→manager ✕(admin 전용).
 *
 * 왜 `canManage`가 아닌가: PIN은 설정·계정 진입의 유일한 자격증명이므로 동급끼리 서로의 진입 자격을
 * 갈아치우는 것은 과대 권한이다. 서버 `domain/roles.ts canResetPin`과 1:1이다.
 */
export function canResetPin(actingRole: UserRole, targetRole: UserRole): boolean {
  return isPower(actingRole) && hierarchyRank(targetRole) < hierarchyRank(actingRole);
}

/**
 * 생성 가능한 역할 목록(analysis/60 §1.4).
 * ⚠️ it15의 계정 생성 폐지로 프로덕션 호출자가 없다 — 규칙만 보존한다(부활 시 모순 방지).
 */
export function creatableRoles(actingRole: UserRole): readonly UserRole[] {
  switch (actingRole) {
    case "admin":
      return ["temp_user", "user", "advanced_user", "manager"];
    case "manager":
      return ["temp_user", "user", "advanced_user"];
    default:
      return [];
  }
}

export function canCreate(actingRole: UserRole, role: UserRole): boolean {
  return creatableRoles(actingRole).includes(role);
}
