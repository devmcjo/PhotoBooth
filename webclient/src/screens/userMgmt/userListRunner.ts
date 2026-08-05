import {
  buildUserRows,
  canOpenUserMgmt,
  type UserRowPolicy,
} from "@domain/accounts/accountAdminPolicy";
import type { SessionUser } from "@domain/accounts/sessionUser";
import { createAccountService } from "@adapters/http/accountService";
import { isForbidden, NetworkError, NotAuthenticatedError } from "@adapters/http/errors";
import { logger } from "@adapters/storage/logStore";
import { sessionStore } from "@shell/sessionStore";

/**
 * 사용자 목록 로드 — 03 §14 (React 무관)
 *
 * ⚠️ **실패를 빈 목록으로 위장하지 않는다.** `accountService.list()`는 예외를 던지고, 여기서
 *    판별 유니온으로 접는다. 403이 "계정 0명"으로 보이면 운영자가 데이터가 사라졌다고 믿는다.
 * ⚠️ 취소는 **결과 폐기** 방식이다(`serverStatusPanel.loadServerStatus`와 같은 형태) —
 *    `accountService`에 `AbortSignal`을 뚫지 않는다(이 Step의 범위 밖이고 목록 조회는 짧다).
 */

export type UserListFailure = "forbidden" | "network" | "unknown";

export type UserListView =
  | { readonly kind: "loading" }
  | {
      readonly kind: "ready";
      readonly rows: readonly UserRowPolicy[];
      readonly total: number;
    }
  | { readonly kind: "failed"; readonly reason: UserListFailure }
  /** 화면을 떠나 결과를 버렸다(언마운트 후 setState 금지). */
  | { readonly kind: "cancelled" };

export interface UserListDeps {
  readonly actor: SessionUser | null;
  readonly list: () => Promise<SessionUser[]>;
}

export async function loadUserList(
  deps: UserListDeps,
  signal?: AbortSignal,
): Promise<UserListView> {
  // 첫 실행문이 권한 가드다(M10 ② — 서버 왕복 전에 막는다).
  if (deps.actor === null || !canOpenUserMgmt(deps.actor.role)) {
    logger.warn("사용자 목록 조회 거부(권한 없음)");
    return { kind: "failed", reason: "forbidden" };
  }
  const actor = deps.actor;

  // 함수로 감싼다 — 인라인 비교는 `await` 뒤에도 TS 좁힘이 남아 두 번째 검사가 죽는다.
  const aborted = (): boolean => signal?.aborted === true;
  if (aborted()) return { kind: "cancelled" };

  let users: SessionUser[];
  try {
    users = await deps.list();
  } catch (err) {
    if (aborted()) return { kind: "cancelled" };
    return { kind: "failed", reason: classifyListFailure(err) };
  }

  if (aborted()) return { kind: "cancelled" };
  const rows = buildUserRows(actor, users);
  return { kind: "ready", rows, total: rows.length };
}

function classifyListFailure(err: unknown): UserListFailure {
  if (isForbidden(err)) {
    logger.warn("사용자 목록 조회 거부(서버 403)");
    return "forbidden";
  }
  if (err instanceof NetworkError || err instanceof NotAuthenticatedError) {
    logger.warn("사용자 목록 조회 실패(네트워크)", { reason: err.message });
    return "network";
  }
  logger.warn("사용자 목록 조회 실패", {
    reason: err instanceof Error ? err.message : String(err),
  });
  return "unknown";
}

/** 실제 배선. 싱글턴은 **호출 시점**에 해석한다(모듈 로드 부작용 0). */
export function defaultUserListDeps(overrides: Partial<UserListDeps> = {}): UserListDeps {
  return {
    actor: sessionStore.getState().currentUser,
    list: () => createAccountService().list(),
    ...overrides,
  };
}
