import type { TempUserLimits } from "@domain/accounts/tempUserLimitsPolicy";
import { getBackendClient, type BackendClient } from "./backendClient";

/**
 * 전역 무료 한도 — `GET/PATCH /config/temp-user-limits` (analysis/31 §4.8·§4.9)
 * 조회는 모든 로그인 사용자, 수정은 **admin 전용**(서버가 강제한다).
 *
 * ⚠️ 값 타입의 정본은 **도메인**(`accounts/tempUserLimitsPolicy`)이다. 여기서 재수출만 하므로
 *    범위 상수·검증과 필드가 갈라지지 않는다(기존 import 경로는 그대로 유효하다).
 */

export type { TempUserLimits };

/** 서버 설정 문서가 없을 때의 폴백(서버와 동일 값). */
export const DEFAULT_TEMP_USER_LIMITS: TempUserLimits = { qrHours: 48, qrCount: 30 };

export interface TempUserLimitsService {
  get(): Promise<TempUserLimits>;
  /** 둘 다 선택이지만 **최소 1개**는 있어야 한다(서버 검증). */
  update(patch: Partial<TempUserLimits>): Promise<TempUserLimits>;
}

function parseLimits(raw: unknown): TempUserLimits {
  if (typeof raw !== "object" || raw === null) return DEFAULT_TEMP_USER_LIMITS;
  const record = raw as Record<string, unknown>;
  return {
    qrHours:
      typeof record.qrHours === "number" ? record.qrHours : DEFAULT_TEMP_USER_LIMITS.qrHours,
    qrCount:
      typeof record.qrCount === "number" ? record.qrCount : DEFAULT_TEMP_USER_LIMITS.qrCount,
  };
}

export function createTempUserLimitsService(
  client: BackendClient = getBackendClient(),
): TempUserLimitsService {
  return {
    async get() {
      return parseLimits(
        await client.request<unknown>({ path: "config/temp-user-limits", auth: "required" }),
      );
    },

    async update(patch) {
      if (patch.qrHours === undefined && patch.qrCount === undefined) {
        // 서버가 400으로 거부할 요청을 보내지 않는다.
        throw new Error("변경할 한도 값이 없습니다.");
      }
      return parseLimits(
        await client.request<unknown>({
          method: "PATCH",
          path: "config/temp-user-limits",
          body: patch,
          auth: "required",
        }),
      );
    },
  };
}
