import { afterEach, beforeEach, describe, expect, it } from "vitest";
import type { SessionUser } from "@domain/accounts/sessionUser";
import type { UserRole } from "@domain/roles/userRole";
import {
  QR_USAGE_FAIL_OPEN,
  type QrUsage,
  type QrUsageService,
} from "@adapters/http/qrUsageService";
import {
  attachLogStore,
  createLogStore,
  createMemoryLogSink,
  detachLogStore,
} from "@adapters/storage/logStore";
import {
  installQrUsageLifecycle,
  isTempUserQrBlocked,
  qrUsageSnapshot,
  tempUserQrReason,
  uninstallQrUsageLifecycle,
} from "@shell/qrUsageStore";

/**
 * TempUser 한도 캐시 — 07 §7 · M9(fail-open)
 *
 * Windows `AppShellViewModel`과 같은 형태다: 계정 변경 1회 조회 → 캐시 → **동기 판정**.
 */

function user(role: UserRole, id = "u1"): SessionUser {
  return { id, role, createdAt: "2026-07-31T00:00:00Z", email: null, authMethod: "google", hasPin: false };
}

function usage(overrides: Partial<QrUsage> = {}): QrUsage {
  return {
    role: "temp_user",
    blocked: true,
    reason: "count",
    remainingMs: 0,
    remainingCount: 0,
    limits: { qrHours: 24, qrCount: 10 },
    ...overrides,
  };
}

/** 응답 시점을 테스트가 제어하는 가짜 서비스. */
function fakeService(): {
  service: QrUsageService;
  calls: number;
  resolveWith(value: QrUsage): Promise<void>;
} {
  const pending: ((value: QrUsage) => void)[] = [];
  const state = {
    service: {
      fetch: () =>
        new Promise<QrUsage>((resolve) => {
          state.calls++;
          pending.push(resolve);
        }),
    },
    calls: 0,
    async resolveWith(value: QrUsage) {
      const resolve = pending.shift();
      resolve?.(value);
      // then 콜백이 마이크로태스크로 흘러가도록 한 틱 양보한다.
      await Promise.resolve();
      await Promise.resolve();
    },
  };
  return state;
}

/** `sessionStore` 대신 쓰는 구독 하네스. */
function fakeSubscribe(): {
  subscribe: (listener: (u: SessionUser | null) => void) => () => void;
  emit(u: SessionUser | null): void;
  listeners: number;
} {
  let listener: ((u: SessionUser | null) => void) | null = null;
  return {
    subscribe(next) {
      listener = next;
      return () => {
        listener = null;
      };
    },
    emit(u) {
      listener?.(u);
    },
    get listeners() {
      return listener === null ? 0 : 1;
    },
  };
}

beforeEach(() => {
  detachLogStore();
  attachLogStore(createLogStore({ sink: createMemoryLogSink(), now: () => 0 }));
});

afterEach(() => {
  uninstallQrUsageLifecycle();
});

describe("qrUsageStore — 조회 시점", () => {
  it("temp_user로 바뀔 때만 1회 조회하고 결과를 반영한다", async () => {
    const svc = fakeService();
    const sub = fakeSubscribe();
    installQrUsageLifecycle({ service: svc.service, subscribe: sub.subscribe });

    expect(isTempUserQrBlocked()).toBe(false); // 설치 직후는 미조회

    sub.emit(user("temp_user"));
    expect(svc.calls).toBe(1);
    expect(qrUsageSnapshot().loading).toBe(true);
    expect(isTempUserQrBlocked()).toBe(false); // 응답 전에는 허용(fail-open)

    await svc.resolveWith(usage({ blocked: true, reason: "time" }));

    expect(isTempUserQrBlocked()).toBe(true);
    expect(tempUserQrReason()).toBe("time");
    expect(qrUsageSnapshot().loading).toBe(false);
  });

  it("비TempUser·게스트에게는 요청하지 않는다(remaining 0 = 무제한)", () => {
    const svc = fakeService();
    const sub = fakeSubscribe();
    installQrUsageLifecycle({ service: svc.service, subscribe: sub.subscribe });

    for (const role of ["user", "advanced_user", "manager", "admin"] as const) {
      sub.emit(user(role));
      expect(svc.calls).toBe(0);
      expect(isTempUserQrBlocked()).toBe(false);
    }

    sub.emit(null);
    expect(svc.calls).toBe(0);
    expect(isTempUserQrBlocked()).toBe(false);
  });

  it("로그아웃하면 캐시가 비고 추가 조회가 없다", async () => {
    const svc = fakeService();
    const sub = fakeSubscribe();
    installQrUsageLifecycle({ service: svc.service, subscribe: sub.subscribe });

    sub.emit(user("temp_user"));
    await svc.resolveWith(usage());
    expect(isTempUserQrBlocked()).toBe(true);

    sub.emit(null);
    expect(isTempUserQrBlocked()).toBe(false);
    expect(qrUsageSnapshot().usage).toBeNull();
    expect(svc.calls).toBe(1);
  });

  it("조회 중 계정이 바뀌면 늦게 온 응답을 폐기한다", async () => {
    const svc = fakeService();
    const sub = fakeSubscribe();
    installQrUsageLifecycle({ service: svc.service, subscribe: sub.subscribe });

    sub.emit(user("temp_user", "temp-a"));
    expect(svc.calls).toBe(1);

    // 응답 전에 일반 사용자로 교체 → 늦게 도착한 temp-a의 blocked가 반영되면 안 된다.
    sub.emit(user("user", "normal-b"));
    await svc.resolveWith(usage({ blocked: true }));

    expect(isTempUserQrBlocked()).toBe(false);
    expect(qrUsageSnapshot().usage).toBeNull();
  });
});

describe("qrUsageStore — fail-open (M9)", () => {
  it("조회 실패 기본값(QR_USAGE_FAIL_OPEN)은 미차단이다", async () => {
    const svc = fakeService();
    const sub = fakeSubscribe();
    installQrUsageLifecycle({ service: svc.service, subscribe: sub.subscribe });

    sub.emit(user("temp_user"));
    await svc.resolveWith(QR_USAGE_FAIL_OPEN);

    expect(isTempUserQrBlocked()).toBe(false);
    expect(tempUserQrReason()).toBe("ok");
  });

  it("역할이 temp_user가 아닌 응답은 blocked여도 차단하지 않는다", async () => {
    const svc = fakeService();
    const sub = fakeSubscribe();
    installQrUsageLifecycle({ service: svc.service, subscribe: sub.subscribe });

    sub.emit(user("temp_user"));
    await svc.resolveWith(usage({ role: "user", blocked: true }));

    expect(isTempUserQrBlocked()).toBe(false);
  });

  it("blocked:false면 사유가 있어도 ok다", async () => {
    const svc = fakeService();
    const sub = fakeSubscribe();
    installQrUsageLifecycle({ service: svc.service, subscribe: sub.subscribe });

    sub.emit(user("temp_user"));
    await svc.resolveWith(usage({ blocked: false, reason: "count" }));

    expect(isTempUserQrBlocked()).toBe(false);
    expect(tempUserQrReason()).toBe("ok");
  });
});

describe("qrUsageStore — 수명", () => {
  it("이중 설치해도 구독은 하나다", () => {
    const svc = fakeService();
    const sub = fakeSubscribe();
    const first = installQrUsageLifecycle({ service: svc.service, subscribe: sub.subscribe });
    const second = installQrUsageLifecycle({ service: svc.service, subscribe: sub.subscribe });

    expect(second).toBe(first);
    expect(sub.listeners).toBe(1);
  });

  it("해제하면 계정이 바뀌어도 조회하지 않는다", () => {
    const svc = fakeService();
    const sub = fakeSubscribe();
    const dispose = installQrUsageLifecycle({ service: svc.service, subscribe: sub.subscribe });

    dispose();
    sub.emit(user("temp_user"));

    expect(svc.calls).toBe(0);
    expect(sub.listeners).toBe(0);
    expect(isTempUserQrBlocked()).toBe(false);
  });
});
