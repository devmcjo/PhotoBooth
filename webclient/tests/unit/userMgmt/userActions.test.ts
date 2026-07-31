import { describe, expect, it } from "vitest";
import type { SessionUser } from "@domain/accounts/sessionUser";
import type { UserRole } from "@domain/roles/userRole";
import { BackendError, NetworkError } from "@adapters/http/errors";
import { runDeleteAccount, runSetRole } from "@screens/userMgmt/userActions";

/**
 * 사용자 관리 행 액션 — 삭제 · 역할 변경 (03 §14)
 *
 * ⚠️ ACC-2 경로: 권한이 없으면 **서버 함수가 호출되지 않는다**(가드가 첫 실행문이다).
 */

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

describe("runDeleteAccount", () => {
  it("권한이 없으면 `deleteAccount`를 **부르지 않는다**", async () => {
    let called = 0;
    const result = await runDeleteAccount({
      actor: user("u", "user"),
      target: user("t", "temp_user"),
      deleteAccount: async () => {
        called++;
      },
    });
    expect(result).toEqual({ kind: "forbidden" });
    expect(called).toBe(0);
  });

  it("자기 자신은 삭제할 수 없다", async () => {
    let called = 0;
    const me = user("me", "admin");
    const result = await runDeleteAccount({
      actor: me,
      target: me,
      deleteAccount: async () => {
        called++;
      },
    });
    expect(result).toEqual({ kind: "forbidden" });
    expect(called).toBe(0);
  });

  it("manager는 **동급 manager를 삭제할 수 있다**(canManage 동급 허용)", async () => {
    const ids: string[] = [];
    const result = await runDeleteAccount({
      actor: user("m1", "manager"),
      target: user("m2", "manager"),
      deleteAccount: async (id) => {
        ids.push(id);
      },
    });
    expect(result).toEqual({ kind: "ok" });
    expect(ids).toEqual(["m2"]);
  });

  it("manager는 admin을 삭제할 수 없다", async () => {
    let called = 0;
    const result = await runDeleteAccount({
      actor: user("m", "manager"),
      target: user("a", "admin"),
      deleteAccount: async () => {
        called++;
      },
    });
    expect(result).toEqual({ kind: "forbidden" });
    expect(called).toBe(0);
  });

  it("서버 403·404·기타를 구분해 접는다", async () => {
    const cases = [
      [new BackendError("forbidden", 403, "forbidden"), "forbidden"],
      [new BackendError("not found", 404, "not_found"), "notFound"],
      [new NetworkError("연결 실패"), "failed"],
    ] as const;

    for (const [error, expected] of cases) {
      const result = await runDeleteAccount({
        actor: user("a", "admin"),
        target: user("t", "temp_user"),
        deleteAccount: async () => {
          throw error;
        },
      });
      expect(result.kind).toBe(expected);
    }
  });
});

describe("runSetRole", () => {
  it("지정 불가 역할은 **서버로 보내지 않는다**", async () => {
    let called = 0;
    const result = await runSetRole({
      actor: user("m", "manager"),
      target: user("u", "user"),
      // manager는 하위 대역만 지정할 수 있다.
      nextRole: "manager",
      setRole: async () => {
        called++;
      },
    });
    expect(result).toEqual({ kind: "forbidden" });
    expect(called).toBe(0);
  });

  it("**no-op 역할 변경은 전송하지 않는다**", async () => {
    let called = 0;
    const result = await runSetRole({
      actor: user("a", "admin"),
      target: user("u", "user"),
      nextRole: "user",
      setRole: async () => {
        called++;
      },
    });
    expect(result).toEqual({ kind: "noop" });
    expect(called).toBe(0);
  });

  it("값이 실제로 달라질 때만 서버로 보낸다", async () => {
    const sent: { id: string; role: UserRole }[] = [];
    const result = await runSetRole({
      actor: user("a", "admin"),
      target: user("u", "user"),
      nextRole: "advanced_user",
      setRole: async (id, role) => {
        sent.push({ id, role });
      },
    });
    expect(result).toEqual({ kind: "ok" });
    expect(sent).toEqual([{ id: "u", role: "advanced_user" }]);
  });

  it("자기 행은 역할을 바꿀 수 없다", async () => {
    let called = 0;
    const me = user("me", "admin");
    const result = await runSetRole({
      actor: me,
      target: me,
      nextRole: "manager",
      setRole: async () => {
        called++;
      },
    });
    expect(result).toEqual({ kind: "forbidden" });
    expect(called).toBe(0);
  });

  it("admin 대상은 아무도 바꿀 수 없다", async () => {
    let called = 0;
    const result = await runSetRole({
      actor: user("a1", "admin"),
      target: user("a2", "admin"),
      nextRole: "manager",
      setRole: async () => {
        called++;
      },
    });
    expect(result).toEqual({ kind: "forbidden" });
    expect(called).toBe(0);
  });
});
