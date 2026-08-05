import type { UserRole } from "./userRole";

/**
 * 역할 변경(setRole) 권한 매트릭스 — Windows `Models/RoleChangePolicy.cs` 이식 (analysis/60 §1.4)
 *
 * 서버 setRole 매트릭스와 **1:1 대칭**이어야 한다(계약 드리프트 방지). 클라이언트는 1차 방어이고
 * 최종 강제는 서버다(M10).
 */

/** 하위 3역할 대역 — manager가 이 안에서는 자유 지정(승격·강등)할 수 있다. 서버 `LOWER_BAND`와 동일. */
const LOWER_BAND: readonly UserRole[] = ["temp_user", "user", "advanced_user"];

/**
 * actor가 현재 역할 `currentRole`인 대상에게 지정 가능한 역할 목록(콤보 필터).
 * 빈 목록이면 역할 변경 UI를 렌더하지 않는다. 반환 순서는 **위계 오름차순**으로 고정한다.
 *
 * 규칙:
 *   - 대상이 admin → 빈 목록(누구도 admin 대상 변경 불가)
 *   - actor admin → admin 제외 전부(승격·강등)
 *   - actor manager && 대상이 하위 대역 → 하위 3역할
 *   - 그 외(manager의 manager 대상 · 비power) → 빈 목록
 */
export function assignableRoles(actorRole: UserRole, currentRole: UserRole): readonly UserRole[] {
  if (currentRole === "admin") return [];
  if (actorRole === "admin") return ["temp_user", "user", "advanced_user", "manager"];
  if (actorRole === "manager" && LOWER_BAND.includes(currentRole)) {
    return ["temp_user", "user", "advanced_user"];
  }
  return [];
}
