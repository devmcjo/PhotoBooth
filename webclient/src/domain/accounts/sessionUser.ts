import { parseRole, type UserRole } from "../roles/userRole";

/**
 * 로그인 사용자 DTO — `POST /auth/google` 응답의 `user` (analysis/31 §4.2)
 *
 * ⚠️ **별도 "내 정보 조회" API가 없다**(analysis/31 §10). 로그인 응답의 이 객체가
 *    계정 화면 표시값의 **유일한 출처**이므로 전체를 보관한다(02 §5).
 */
export interface SessionUser {
  readonly id: string;
  readonly role: UserRole;
  /** ISO 8601. */
  readonly createdAt: string;
  readonly email: string | null;
  /** 로그인 방식. 알 수 없으면 null → 화면은 "알 수 없음"으로 표시한다. */
  readonly authMethod: string | null;
  readonly hasPin: boolean;
}

function asString(value: unknown): string | null {
  return typeof value === "string" && value.length > 0 ? value : null;
}

/**
 * 서버 응답을 방어적으로 파싱한다. 역할이 이상하면 **최소 권한**으로 떨어진다(권한 상승 방지).
 * `id`가 없으면 세션을 만들 수 없으므로 `null`이다.
 */
export function parseSessionUser(raw: unknown): SessionUser | null {
  if (typeof raw !== "object" || raw === null) return null;
  const record = raw as Record<string, unknown>;
  const id = asString(record.id);
  if (id === null) return null;

  return {
    id,
    role: parseRole(typeof record.role === "string" ? record.role : null),
    createdAt: asString(record.createdAt) ?? "",
    email: asString(record.email),
    authMethod: asString(record.authMethod),
    hasPin: record.hasPin === true,
  };
}

/** 상단바 계정 라벨. 게스트는 별 문구를 쓰므로 여기서는 사용자만 다룬다. */
export function accountLabel(user: SessionUser): string {
  return user.id;
}

/**
 * 로그인 방식 표시 라벨. 미지원 값은 "알 수 없음"으로 폴백한다(03 §13.1 · analysis/13 §10.1).
 *
 * ⚠️ 값은 Windows `AuthMethodExtensions.ToLabel()`(`Models/User.cs`)과 **문자열 일치**다:
 *    `"google"` → **"Google SSO"**, 그 외 전부 "알 수 없음".
 *    Step 16 이전에는 `"Google 계정"`이었으나 **호출자가 0이라 한 번도 렌더된 적이 없었다** —
 *    보호할 현행 동작이 없으므로 규격(analysis/13 §14 문구 카탈로그)에 맞췄다.
 * ⚠️ `"password"` 분기는 **삭제됐다**. it15에서 비밀번호 로그인 자체가 폐지되어, 남겨 두면
 *    서버가 그 값을 보낼 수 있다는 오해를 만든다.
 * ⚠️ 문구를 `ui/strings.ts`로 옮기지 않는다 — `roleLabel`과 같은 자리이며 카탈로그 중복을
 *    만들면 두 곳이 갈라진다.
 */
export function authMethodLabel(user: SessionUser): string {
  return user.authMethod === "google" ? "Google SSO" : "알 수 없음";
}
