import { describe, expect, it } from "vitest";
import type { TempUserLimits } from "@domain/accounts/tempUserLimitsPolicy";
import {
  loadTempUserLimits,
  saveTempUserLimits,
} from "@screens/account/adminLimitsForm";

/**
 * 전역 무료 한도 — 03 §13.2 · analysis/31 §4.9
 *
 * ⚠️ 조회 실패를 **기본값(48/30)으로 위장하지 않는다** — admin이 서버 값을 오독한다.
 * ⚠️ 범위 밖·무변경은 **서버로 보내지 않는다**(400을 받을 요청을 만들지 않는다).
 */

const CURRENT: TempUserLimits = { qrHours: 48, qrCount: 30 };

describe("loadTempUserLimits", () => {
  it("admin이 아니면 첫 실행문에서 차단되고 **서버를 부르지 않는다**", async () => {
    for (const role of [null, "temp_user", "user", "advanced_user", "manager"] as const) {
      let called = 0;
      const view = await loadTempUserLimits({
        role,
        get: async () => {
          called++;
          return CURRENT;
        },
      });
      expect(view).toEqual({ kind: "forbidden" });
      expect(called).toBe(0);
    }
  });

  it("admin은 서버 값을 그대로 돌려받는다", async () => {
    const view = await loadTempUserLimits({ role: "admin", get: async () => CURRENT });
    expect(view).toEqual({ kind: "ready", current: CURRENT });
  });

  it("조회 실패는 `failed`다(**기본값으로 위장하지 않는다**)", async () => {
    const view = await loadTempUserLimits({
      role: "admin",
      get: async () => {
        throw new Error("500");
      },
    });
    expect(view).toEqual({ kind: "failed" });
  });
});

describe("saveTempUserLimits", () => {
  it("admin이 아니면 첫 실행문에서 차단되고 서버를 부르지 않는다", async () => {
    let called = 0;
    const result = await saveTempUserLimits({
      role: "manager",
      draft: { qrHours: 24, qrCount: 10 },
      current: CURRENT,
      update: async () => {
        called++;
        return CURRENT;
      },
    });
    expect(result).toEqual({ kind: "forbidden" });
    expect(called).toBe(0);
  });

  it("범위 밖 값은 **서버로 전송되지 않는다**", async () => {
    let called = 0;
    const result = await saveTempUserLimits({
      role: "admin",
      draft: { qrHours: 0, qrCount: 10 },
      current: CURRENT,
      update: async () => {
        called++;
        return CURRENT;
      },
    });
    expect(result).toEqual({ kind: "rejected", reason: "qrHours-range" });
    expect(called).toBe(0);
  });

  it("변경이 없으면 전송하지 않는다", async () => {
    let called = 0;
    const result = await saveTempUserLimits({
      role: "admin",
      draft: { qrHours: 48, qrCount: 30 },
      current: CURRENT,
      update: async () => {
        called++;
        return CURRENT;
      },
    });
    expect(result).toEqual({ kind: "rejected", reason: "no-change" });
    expect(called).toBe(0);
  });

  it("달라진 키만 patch로 보낸다", async () => {
    const sent: Partial<TempUserLimits>[] = [];
    await saveTempUserLimits({
      role: "admin",
      draft: { qrHours: 24, qrCount: 30 },
      current: CURRENT,
      update: async (patch) => {
        sent.push(patch);
        return { qrHours: 24, qrCount: 30 };
      },
    });
    expect(sent).toEqual([{ qrHours: 24 }]);
  });

  it("성공 시 **서버가 돌려준 전체 값**을 반환한다(화면이 재반영한다)", async () => {
    const serverValue: TempUserLimits = { qrHours: 24, qrCount: 30 };
    const result = await saveTempUserLimits({
      role: "admin",
      draft: { qrHours: 24, qrCount: 30 },
      current: CURRENT,
      update: async () => serverValue,
    });
    expect(result).toEqual({ kind: "ok", current: serverValue });
  });

  it("서버 오류는 `failed`다(예외를 전파하지 않는다)", async () => {
    const result = await saveTempUserLimits({
      role: "admin",
      draft: { qrHours: 24, qrCount: 30 },
      current: CURRENT,
      update: async () => {
        throw new Error("403");
      },
    });
    expect(result).toEqual({ kind: "failed" });
  });
});
