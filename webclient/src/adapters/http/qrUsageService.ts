import type { UserRole } from "@domain/roles/userRole";
import { parseRole } from "@domain/roles/userRole";
import { logger } from "@adapters/storage/logStore";
import { getBackendClient, type BackendClient } from "./backendClient";

/**
 * TempUser 무료 한도 상태 — `GET /accounts/me/qr-usage` (analysis/31 §4.4)
 *
 * ⚠️ **조회 실패는 fail-open이다**(허용하고 진행 — M9). 과금 안전은 업로드 단계의 **서버 거부**가
 *    담보하므로, 조회 실패로 촬영을 막으면 잃는 것만 있다.
 *
 * ⚠️ **`role`을 먼저 봐야 한다.** non-TempUser는 서버가 계정 문서를 읽지 않고
 *    `remainingMs: 0, remainingCount: 0`을 돌려준다 — 이 0은 "소진"이 아니라 **"무제한"**이다.
 */

export type QrUsageReason = "ok" | "time" | "count";

export interface QrUsage {
  readonly role: UserRole;
  readonly blocked: boolean;
  readonly reason: QrUsageReason;
  readonly remainingMs: number;
  readonly remainingCount: number;
  readonly limits: { readonly qrHours: number; readonly qrCount: number };
}

/** 조회 실패 시 쓰는 fail-open 기본값. */
export const QR_USAGE_FAIL_OPEN: QrUsage = {
  role: "user",
  blocked: false,
  reason: "ok",
  remainingMs: 0,
  remainingCount: 0,
  limits: { qrHours: 0, qrCount: 0 },
};

export interface QrUsageService {
  /** 실패해도 예외를 던지지 않는다(fail-open). */
  fetch(): Promise<QrUsage>;
}

function parseUsage(raw: unknown): QrUsage {
  if (typeof raw !== "object" || raw === null) return QR_USAGE_FAIL_OPEN;
  const record = raw as Record<string, unknown>;
  const limits = (record.limits ?? {}) as Record<string, unknown>;
  const reason = record.reason;

  return {
    role: parseRole(typeof record.role === "string" ? record.role : null),
    blocked: record.blocked === true,
    reason: reason === "time" || reason === "count" ? reason : "ok",
    remainingMs: typeof record.remainingMs === "number" ? record.remainingMs : 0,
    remainingCount: typeof record.remainingCount === "number" ? record.remainingCount : 0,
    limits: {
      qrHours: typeof limits.qrHours === "number" ? limits.qrHours : 0,
      qrCount: typeof limits.qrCount === "number" ? limits.qrCount : 0,
    },
  };
}

/**
 * TempUser이고 한도를 초과했는가 — `qrEffectivePolicy`의 `isTempUserBlocked` 입력.
 * **역할이 TempUser가 아니면 항상 false**다(0으로 채워진 remaining을 소진으로 오해하지 않는다).
 */
export function isTempUserBlocked(usage: QrUsage): boolean {
  return usage.role === "temp_user" && usage.blocked;
}

export function createQrUsageService(client: BackendClient = getBackendClient()): QrUsageService {
  return {
    async fetch() {
      try {
        return parseUsage(
          await client.request<unknown>({ path: "accounts/me/qr-usage", auth: "required" }),
        );
      } catch (err) {
        // fail-open: 허용하고 진행한다. 서버가 업로드에서 최종 판정한다.
        logger.warn("무료 한도 조회 실패 — 허용하고 진행(fail-open)", {
          reason: err instanceof Error ? err.message : String(err),
        });
        return QR_USAGE_FAIL_OPEN;
      }
    },
  };
}
