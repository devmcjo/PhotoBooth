import { describe, expect, it } from "vitest";
import type { SessionUser } from "@domain/accounts/sessionUser";
import type { UserRole } from "@domain/roles/userRole";
import { BackendError } from "@adapters/http/errors";
import { runPinReset } from "@screens/userMgmt/pinResetRunner";

/**
 * 타 계정 PIN 재설정 — 03 §14 · analysis/31 §4.7
 *
 * ⚠️ **동급은 차단**된다(삭제는 동급 허용 — 비대칭이 규격이다).
 * ⚠️ **PIN 값이 결과에 실리지 않는다**(로그는 PIN-1 정적 검사가 따로 고정한다).
 */

const PIN = "4821";

function user(id: string, role: UserRole): SessionUser {
  return {
    id,
    role,
    createdAt: "2026-01-01T00:00:00.000Z",
    email: null,
    authMethod: "google",
    hasPin: true,
  };
}

describe("runPinReset — 권한", () => {
  it("**동급 대상은 차단**되고 서버를 부르지 않는다", async () => {
    let called = 0;
    const result = await runPinReset({
      actor: user("m1", "manager"),
      target: user("m2", "manager"),
      first: PIN,
      second: PIN,
      resetOtherPin: async () => {
        called++;
      },
    });
    expect(result).toEqual({ kind: "forbidden" });
    expect(called).toBe(0);
  });

  it("자기 자신은 차단된다", async () => {
    let called = 0;
    const me = user("me", "admin");
    const result = await runPinReset({
      actor: me,
      target: me,
      first: PIN,
      second: PIN,
      resetOtherPin: async () => {
        called++;
      },
    });
    expect(result).toEqual({ kind: "forbidden" });
    expect(called).toBe(0);
  });

  it("비power는 차단된다", async () => {
    let called = 0;
    const result = await runPinReset({
      actor: user("u", "advanced_user"),
      target: user("t", "temp_user"),
      first: PIN,
      second: PIN,
      resetOtherPin: async () => {
        called++;
      },
    });
    expect(result).toEqual({ kind: "forbidden" });
    expect(called).toBe(0);
  });

  it("admin은 manager의 PIN을 재설정할 수 있다(엄격히 낮은 위계)", async () => {
    const sent: { id: string }[] = [];
    const result = await runPinReset({
      actor: user("a", "admin"),
      target: user("m", "manager"),
      first: PIN,
      second: PIN,
      resetOtherPin: async (id) => {
        sent.push({ id });
      },
    });
    expect(result).toEqual({ kind: "ok" });
    expect(sent).toEqual([{ id: "m" }]);
  });
});

describe("runPinReset — 형식·일치", () => {
  it("형식이 틀리면 서버를 부르지 않는다", async () => {
    let called = 0;
    const result = await runPinReset({
      actor: user("a", "admin"),
      target: user("t", "temp_user"),
      first: "12",
      second: "12",
      resetOtherPin: async () => {
        called++;
      },
    });
    expect(result).toEqual({ kind: "invalidFormat" });
    expect(called).toBe(0);
  });

  it("2회 불일치는 서버를 부르지 않는다", async () => {
    let called = 0;
    const result = await runPinReset({
      actor: user("a", "admin"),
      target: user("t", "temp_user"),
      first: PIN,
      second: "9999",
      resetOtherPin: async () => {
        called++;
      },
    });
    expect(result).toEqual({ kind: "confirmMismatch" });
    expect(called).toBe(0);
  });
});

describe("runPinReset — 결과에 PIN이 없다", () => {
  it("성공·실패 어느 결과에도 PIN 문자열이 담기지 않는다", async () => {
    const results = [
      await runPinReset({
        actor: user("a", "admin"),
        target: user("t", "temp_user"),
        first: PIN,
        second: PIN,
        resetOtherPin: async () => undefined,
      }),
      await runPinReset({
        actor: user("a", "admin"),
        target: user("t", "temp_user"),
        first: PIN,
        second: PIN,
        resetOtherPin: async () => {
          throw new BackendError("not found", 404, "not_found");
        },
      }),
      await runPinReset({
        actor: user("m", "manager"),
        target: user("m2", "manager"),
        first: PIN,
        second: PIN,
        resetOtherPin: async () => undefined,
      }),
    ];

    for (const result of results) {
      expect(JSON.stringify(result)).not.toContain(PIN);
    }
  });

  it("서버 403·404를 구분한다", async () => {
    const forbidden = await runPinReset({
      actor: user("a", "admin"),
      target: user("t", "temp_user"),
      first: PIN,
      second: PIN,
      resetOtherPin: async () => {
        throw new BackendError("forbidden", 403, "forbidden");
      },
    });
    expect(forbidden).toEqual({ kind: "forbidden" });

    const notFound = await runPinReset({
      actor: user("a", "admin"),
      target: user("t", "temp_user"),
      first: PIN,
      second: PIN,
      resetOtherPin: async () => {
        throw new BackendError("not found", 404, "not_found");
      },
    });
    expect(notFound).toEqual({ kind: "notFound" });
  });
});
