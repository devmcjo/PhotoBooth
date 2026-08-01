import type { UserRole } from "@domain/roles/userRole";

/**
 * E2E 계정 픽스처 — `POST /auth/google` 응답의 `user` 객체와 같은 형태다
 * (`parseSessionUser`가 읽는 필드만 담는다).
 *
 * ⚠️ 여기 값은 **목 서버가 돌려주는 값**이지 하네스가 세션에 직접 꽂는 값이 아니다.
 *    로그인은 언제나 실제 `oauthCallbackRunner`를 거친다(설계 §5.4).
 */
export interface MockUser {
  readonly id: string;
  readonly role: UserRole;
  readonly createdAt: string;
  readonly email: string | null;
  readonly authMethod: string;
  readonly hasPin: boolean;
}

function user(
  id: string,
  role: UserRole,
  overrides: Partial<MockUser> = {},
): MockUser {
  return {
    id,
    role,
    createdAt: "2026-01-02T03:04:05.000Z",
    email: `${id}@example.test`,
    authMethod: "google",
    hasPin: true,
    ...overrides,
  };
}

export const USERS = {
  user: user("e2e-user", "user"),
  tempUser: user("e2e-temp", "temp_user"),
  advanced: user("e2e-advanced", "advanced_user"),
  manager: user("e2e-manager", "manager"),
  admin: user("e2e-admin", "admin"),
  /** PIN 미설정 계정(최초 설정 플로우 — `verify`가 409를 준다). */
  noPin: user("e2e-nopin", "manager", { hasPin: false }),
} as const;

/** `GET /accounts` 목록 목(자기 자신 포함 — E18이 "자기 행에는 액션이 없다"를 본다). */
export const ACCOUNT_LIST: readonly MockUser[] = [
  USERS.manager,
  USERS.admin,
  user("e2e-other-manager", "manager"),
  USERS.advanced,
  USERS.user,
  USERS.tempUser,
];
