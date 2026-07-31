import type { SessionUser } from "@domain/accounts/sessionUser";
import { parseSessionUser } from "@domain/accounts/sessionUser";
import type { UserRole } from "@domain/roles/userRole";
import { getBackendClient, type BackendClient } from "./backendClient";

/**
 * 계정 API — analysis/31 §4.3~4.7 · 06 §2
 *
 * ⚠️ PIN 3종의 본문을 생략하면 401이 난다(06 §2.0):
 *    - `POST /accounts/me/pin/verify` → `{pin}` · 불일치 401 · **미설정은 409**(최초 설정 플로우)
 *    - `PUT /accounts/me/pin` → `{newPin, currentPin?}` · **기존 PIN 보유 시 `currentPin` 필수**
 *      (verify를 통과했더라도 `currentPin` 없는 PUT은 **별개 검증이라 401**이다)
 *    - `PUT /accounts/{id}/pin` → `{newPin}` (power + 엄격히 낮은 위계)
 */

export interface AccountService {
  /** power 전용. 실패는 예외 — **빈 목록으로 표시하지 않는다**(03 §14). */
  list(): Promise<SessionUser[]>;
  verifyMyPin(pin: string): Promise<void>;
  /** 최초 설정이면 `currentPin`을 생략한다. */
  setMyPin(newPin: string, currentPin?: string): Promise<void>;
  deleteAccount(id: string): Promise<void>;
  setRole(id: string, role: UserRole): Promise<void>;
  /** 타 계정 PIN 재설정(power + 엄격히 낮은 위계). 자기 자신은 서버가 400으로 거부한다. */
  resetOtherPin(id: string, newPin: string): Promise<void>;
}

export function createAccountService(client: BackendClient = getBackendClient()): AccountService {
  return {
    async list() {
      const raw = await client.request<unknown>({ path: "accounts", auth: "required" });
      const items = Array.isArray(raw) ? raw : [];
      // 파싱 실패 항목은 버린다(역할이 이상하면 최소 권한으로 떨어진다 — parseSessionUser).
      return items
        .map(parseSessionUser)
        .filter((user): user is SessionUser => user !== null);
    },

    async verifyMyPin(pin) {
      await client.request<unknown>({
        method: "POST",
        path: "accounts/me/pin/verify",
        body: { pin },
        auth: "required",
      });
    },

    async setMyPin(newPin, currentPin) {
      await client.request<unknown>({
        method: "PUT",
        path: "accounts/me/pin",
        // 최초 설정(미보유)일 때만 currentPin을 생략한다.
        body: currentPin === undefined ? { newPin } : { newPin, currentPin },
        auth: "required",
      });
    },

    async deleteAccount(id) {
      await client.request<unknown>({
        method: "DELETE",
        path: `accounts/${encodeURIComponent(id)}`,
        auth: "required",
      });
    },

    async setRole(id, role) {
      await client.request<unknown>({
        method: "PATCH",
        path: `accounts/${encodeURIComponent(id)}/role`,
        body: { role },
        auth: "required",
      });
    },

    async resetOtherPin(id, newPin) {
      await client.request<unknown>({
        method: "PUT",
        path: `accounts/${encodeURIComponent(id)}/pin`,
        body: { newPin },
        auth: "required",
      });
    },
  };
}
