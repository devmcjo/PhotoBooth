import { describe, expect, it } from "vitest";
import type { SessionUser } from "@domain/accounts/sessionUser";
import type { UserRole } from "@domain/roles/userRole";
import { BackendError, NetworkError } from "@adapters/http/errors";
import { loadUserList } from "@screens/userMgmt/userListRunner";

/**
 * 사용자 목록 로드 — 03 §14
 *
 * ⚠️ 가장 중요한 고정: **실패가 빈 목록으로 위장되지 않는다.** 403이 "계정 0명"으로 보이면
 *    운영자가 데이터가 사라졌다고 믿는다.
 */

function user(id: string, role: UserRole, createdAt = "2026-01-01T00:00:00.000Z"): SessionUser {
  return { id, role, createdAt, email: null, authMethod: "google", hasPin: true };
}

const ACTOR = user("mgr", "manager");

describe("loadUserList — 권한 가드", () => {
  it.each([null, "temp_user", "user", "advanced_user"] as const)(
    "%s는 첫 실행문에서 차단되고 **서버를 부르지 않는다**",
    async (role) => {
      let called = 0;
      const view = await loadUserList({
        actor: role === null ? null : user("x", role),
        list: async () => {
          called++;
          return [];
        },
      });
      expect(view).toEqual({ kind: "failed", reason: "forbidden" });
      expect(called).toBe(0);
    },
  );
});

describe("loadUserList — 결과", () => {
  it("성공하면 정렬된 행과 총계를 돌려준다", async () => {
    const view = await loadUserList({
      actor: ACTOR,
      list: async () => [user("t", "temp_user"), user("a", "admin")],
    });
    expect(view.kind).toBe("ready");
    if (view.kind !== "ready") return;
    expect(view.rows.map((row) => row.user.id)).toEqual(["a", "t"]);
    expect(view.total).toBe(2);
  });

  it("행 정책이 actor 기준으로 계산된다(manager → admin 행은 액션 없음)", async () => {
    const view = await loadUserList({ actor: ACTOR, list: async () => [user("a", "admin")] });
    expect(view.kind).toBe("ready");
    if (view.kind !== "ready") return;
    expect(view.rows[0]!.canDelete).toBe(false);
    expect(view.rows[0]!.canResetPin).toBe(false);
  });

  it("403은 `failed:'forbidden'`이다(**빈 배열이 아니다**)", async () => {
    const view = await loadUserList({
      actor: ACTOR,
      list: async () => {
        throw new BackendError("forbidden", 403, "forbidden");
      },
    });
    expect(view).toEqual({ kind: "failed", reason: "forbidden" });
  });

  it("네트워크 실패는 `failed:'network'`다", async () => {
    const view = await loadUserList({
      actor: ACTOR,
      list: async () => {
        throw new NetworkError("연결 실패");
      },
    });
    expect(view).toEqual({ kind: "failed", reason: "network" });
  });

  it("알 수 없는 실패는 `failed:'unknown'`이다", async () => {
    const view = await loadUserList({
      actor: ACTOR,
      list: async () => {
        throw new Error("무엇인가");
      },
    });
    expect(view).toEqual({ kind: "failed", reason: "unknown" });
  });
});

describe("loadUserList — 취소", () => {
  it("시작 전에 abort되면 `cancelled`이고 서버를 부르지 않는다", async () => {
    const controller = new AbortController();
    controller.abort();
    let called = 0;
    const view = await loadUserList(
      {
        actor: ACTOR,
        list: async () => {
          called++;
          return [];
        },
      },
      controller.signal,
    );
    expect(view).toEqual({ kind: "cancelled" });
    expect(called).toBe(0);
  });

  it("응답 도중 abort되면 결과를 **버린다**", async () => {
    const controller = new AbortController();
    const view = await loadUserList(
      {
        actor: ACTOR,
        list: async () => {
          controller.abort();
          return [user("t", "temp_user")];
        },
      },
      controller.signal,
    );
    expect(view).toEqual({ kind: "cancelled" });
  });

  it("실패 도중 abort돼도 `cancelled`다(언마운트 뒤 오류 토스트 금지)", async () => {
    const controller = new AbortController();
    const view = await loadUserList(
      {
        actor: ACTOR,
        list: async () => {
          controller.abort();
          throw new NetworkError("연결 실패");
        },
      },
      controller.signal,
    );
    expect(view).toEqual({ kind: "cancelled" });
  });
});
