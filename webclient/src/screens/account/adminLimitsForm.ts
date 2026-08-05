import { canEditGlobalLimits } from "@domain/accounts/accountAdminPolicy";
import {
  buildLimitsPatch,
  validateTempUserLimits,
  type LimitsRejection,
  type TempUserLimits,
  type TempUserLimitsDraft,
} from "@domain/accounts/tempUserLimitsPolicy";
import type { UserRole } from "@domain/roles/userRole";
import { createTempUserLimitsService } from "@adapters/http/tempUserLimitsService";
import { logger } from "@adapters/storage/logStore";

/**
 * 전역 무료 한도 조회·저장 — 03 §13.2 · analysis/31 §4.9 (React 무관)
 *
 * ⚠️ **첫 실행문이 권한 가드**다(ACC-2). 가드가 뒤로 밀리면 서버 왕복이 먼저 일어난다.
 * ⚠️ 조회 실패를 `DEFAULT_TEMP_USER_LIMITS`(48/30)로 위장하지 않는다 — admin이 그 값을
 *    "현재 서버 값"으로 오독하면 실제와 다른 한도를 저장한다.
 * ⚠️ 범위를 벗어난 값을 서버로 보내지 않는다(서버가 400으로 거부할 요청을 만들지 않는다).
 */

export type LimitsView =
  | { readonly kind: "loading" }
  | { readonly kind: "ready"; readonly current: TempUserLimits }
  | { readonly kind: "forbidden" }
  | { readonly kind: "failed" };

export interface LimitsLoadDeps {
  readonly role: UserRole | null;
  readonly get: () => Promise<TempUserLimits>;
}

export async function loadTempUserLimits(deps: LimitsLoadDeps): Promise<LimitsView> {
  if (!canEditGlobalLimits(deps.role)) return { kind: "forbidden" };

  try {
    return { kind: "ready", current: await deps.get() };
  } catch (err) {
    logger.warn("전역 무료 한도 조회 실패", {
      reason: err instanceof Error ? err.message : String(err),
    });
    return { kind: "failed" };
  }
}

export type LimitsSaveResult =
  /** 서버가 돌려준 **갱신된 전체 한도**. 화면은 이 값으로 draft를 재반영한다(03 §12.4 4단). */
  | { readonly kind: "ok"; readonly current: TempUserLimits }
  | { readonly kind: "forbidden" }
  | { readonly kind: "rejected"; readonly reason: LimitsRejection }
  | { readonly kind: "failed" };

export interface LimitsSaveDeps {
  readonly role: UserRole | null;
  readonly draft: TempUserLimitsDraft;
  readonly current: TempUserLimits;
  readonly update: (patch: Partial<TempUserLimits>) => Promise<TempUserLimits>;
}

export async function saveTempUserLimits(deps: LimitsSaveDeps): Promise<LimitsSaveResult> {
  if (!canEditGlobalLimits(deps.role)) {
    logger.warn("전역 무료 한도 저장 거부(권한 없음)");
    return { kind: "forbidden" };
  }

  const validation = validateTempUserLimits(deps.draft, deps.current);
  if (!validation.ok) {
    // 서버 왕복 없이 끝난다 — `reason`이 없을 수 없지만 타입상 optional이라 폴백을 둔다.
    return { kind: "rejected", reason: validation.reason ?? "no-change" };
  }

  const patch = buildLimitsPatch(deps.draft, deps.current);
  try {
    return { kind: "ok", current: await deps.update(patch) };
  } catch (err) {
    logger.warn("전역 무료 한도 저장 실패", {
      reason: err instanceof Error ? err.message : String(err),
    });
    return { kind: "failed" };
  }
}

/** 실제 배선. 싱글턴은 **호출 시점**에 해석한다(모듈 로드 부작용 0). */
export function defaultLimitsLoadDeps(role: UserRole | null): LimitsLoadDeps {
  return { role, get: () => createTempUserLimitsService().get() };
}

export function defaultLimitsSaveDeps(
  role: UserRole | null,
  draft: TempUserLimitsDraft,
  current: TempUserLimits,
): LimitsSaveDeps {
  return {
    role,
    draft,
    current,
    update: (patch) => createTempUserLimitsService().update(patch),
  };
}
