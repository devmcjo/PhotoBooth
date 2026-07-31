import { buildUserRow } from "@domain/accounts/accountAdminPolicy";
import type { SessionUser } from "@domain/accounts/sessionUser";
import type { UserRole } from "@domain/roles/userRole";
import { isForbidden, isNotFound } from "@adapters/http/errors";
import { logger } from "@adapters/storage/logStore";

/**
 * 사용자 관리 행 액션 — 삭제 · 역할 변경 (03 §14 · analysis/31 §4.5~§4.6, React 무관)
 *
 * ⚠️ **각 함수의 첫 실행문이 도메인 판정 가드**다(정적 검사 ACC-2). 가드가 뒤로 밀리면
 *    권한 없는 요청이 먼저 서버로 나간다.
 * ⚠️ 삭제는 **동급 허용**(`canManage`), PIN 재설정은 **동급 차단**이다(`pinResetRunner`).
 *    비대칭이 규격이다 — analysis/60 §1.3.1.
 * ⚠️ 성공 뒤에는 **목록을 다시 조회**한다(호출측 책임). 로컬 배열을 손으로 갱신하면 서버의
 *    cascade·역할 폴백과 화면이 갈라진다.
 */

export type UserActionResult =
  | { readonly kind: "ok" }
  | { readonly kind: "forbidden" }
  | { readonly kind: "notFound" }
  | { readonly kind: "failed" };

export interface DeleteAccountDeps {
  readonly actor: SessionUser;
  readonly target: SessionUser;
  readonly deleteAccount: (id: string) => Promise<void>;
}

export async function runDeleteAccount(deps: DeleteAccountDeps): Promise<UserActionResult> {
  if (!buildUserRow(deps.actor, deps.target).canDelete) {
    logger.warn("계정 삭제 거부(권한 없음)", { targetId: deps.target.id });
    return { kind: "forbidden" };
  }

  try {
    await deps.deleteAccount(deps.target.id);
    logger.info("계정 삭제", { targetId: deps.target.id });
    return { kind: "ok" };
  } catch (err) {
    return toActionResult(err, "계정 삭제 실패", deps.target.id);
  }
}

export type SetRoleResult = UserActionResult | { readonly kind: "noop" };

export interface SetRoleDeps {
  readonly actor: SessionUser;
  readonly target: SessionUser;
  readonly nextRole: UserRole;
  readonly setRole: (id: string, role: UserRole) => Promise<void>;
}

export async function runSetRole(deps: SetRoleDeps): Promise<SetRoleResult> {
  if (!buildUserRow(deps.actor, deps.target).assignableRoles.includes(deps.nextRole)) {
    logger.warn("역할 변경 거부(권한 없음)", { targetId: deps.target.id });
    return { kind: "forbidden" };
  }

  // 값이 달라지지 않으면 **서버로 보내지 않는다**(무의미한 왕복·감사 로그 오염 방지).
  if (deps.nextRole === deps.target.role) return { kind: "noop" };

  try {
    await deps.setRole(deps.target.id, deps.nextRole);
    logger.info("역할 변경", { targetId: deps.target.id, nextRole: deps.nextRole });
    return { kind: "ok" };
  } catch (err) {
    return toActionResult(err, "역할 변경 실패", deps.target.id);
  }
}

/** 예외 → 판별 유니온. 403은 목록을 비우지 않고 토스트만 낸다(호출측 규약). */
export function toActionResult(
  err: unknown,
  message: string,
  targetId: string,
): UserActionResult {
  if (isForbidden(err)) {
    logger.warn(`${message}(서버 403)`, { targetId });
    return { kind: "forbidden" };
  }
  if (isNotFound(err)) {
    logger.warn(`${message}(대상 없음)`, { targetId });
    return { kind: "notFound" };
  }
  logger.warn(message, {
    targetId,
    reason: err instanceof Error ? err.message : String(err),
  });
  return { kind: "failed" };
}
