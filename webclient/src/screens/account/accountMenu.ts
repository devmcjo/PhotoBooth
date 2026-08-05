import { canOpenUserMgmt } from "@domain/accounts/accountAdminPolicy";
import type { SessionUser } from "@domain/accounts/sessionUser";
import { STRINGS } from "@ui/strings";

/**
 * 상단바 계정 팝오버 항목 — 02 §5.1 (React 무관 · 순수)
 *
 * ⚠️ 권한 판정은 **여기 한 곳**이다. `TopBar`는 이 배열과 `onSelect`를 props로 받아 렌더만 한다
 *    (화면이 역할 문자열을 비교하지 않는다 — ACC-1과 같은 정신).
 * ⚠️ [로그아웃]이 **PIN 게이트 앞**에 있어야 하는 이유: `Account`는 게이트 뒤라, PIN을 잊은
 *    운영자가 로그아웃조차 못 하면 교대 시 계정이 그대로 남는다(PIN 분실은 앱 내 복구 불가 — 07 §6.5).
 */

export type AccountMenuItemId = "manage" | "adminTools" | "logout";

export interface AccountMenuItem {
  readonly id: AccountMenuItemId;
  readonly label: string;
}

/** 게스트는 **빈 배열**이다 — 호출측이 팝오버를 열지 않고 곧바로 `Login`으로 보낸다. */
export function buildAccountMenuItems(user: SessionUser | null): readonly AccountMenuItem[] {
  if (user === null) return [];

  const items: AccountMenuItem[] = [
    { id: "manage", label: STRINGS.account.title },
  ];
  if (canOpenUserMgmt(user.role)) {
    items.push({ id: "adminTools", label: STRINGS.account.adminTitle });
  }
  items.push({ id: "logout", label: STRINGS.common.logout });
  return items;
}
