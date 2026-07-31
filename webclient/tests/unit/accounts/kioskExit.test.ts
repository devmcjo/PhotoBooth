import { describe, expect, it } from "vitest";
import { runKioskExit, type KioskExitDeps } from "@screens/account/kioskExit";
import { STRINGS } from "@ui/strings";

/**
 * [키오스크 종료] — 앱 종료 대체 (WD5 · 03 §13.2)
 *
 * ⚠️ **`window.close()`를 부르지 않는다.** 스크립트가 연 창에서만 동작하므로 키오스크의 첫 탭에서는
 *    조용히 실패한다 → "버튼이 안 먹는다"가 된다. 대신 안내 토스트로 마무리한다.
 */

interface Harness {
  readonly deps: KioskExitDeps;
  readonly order: string[];
  readonly toasts: { kind: string; message: string }[];
}

function harness(overrides: Partial<KioskExitDeps> = {}): Harness {
  const order: string[] = [];
  const toasts: { kind: string; message: string }[] = [];

  const deps: KioskExitDeps = {
    role: "manager",
    exitFullscreen: async () => {
      order.push("fullscreen");
    },
    logout: () => {
      order.push("logout");
    },
    returnHome: async (reason) => {
      order.push(`home:${reason}`);
    },
    toast: (kind, message) => {
      order.push("toast");
      toasts.push({ kind, message });
    },
    ...overrides,
  };

  return { deps, order, toasts };
}

describe("runKioskExit", () => {
  it.each([null, "temp_user", "user", "advanced_user"] as const)(
    "%s는 거부되고 **부수효과가 0**이다",
    async (role) => {
      const h = harness({ role });
      expect(await runKioskExit(h.deps)).toBe(false);
      expect(h.order).toEqual([]);
    },
  );

  it.each(["manager", "admin"] as const)("%s는 규격 순서로 실행한다", async (role) => {
    const h = harness({ role });
    expect(await runKioskExit(h.deps)).toBe(true);
    expect(h.order).toEqual(["fullscreen", "logout", "home:키오스크 종료", "toast"]);
  });

  it("마지막 안내는 규격 문구다(탭을 직접 닫으라고 알린다)", async () => {
    const h = harness();
    await runKioskExit(h.deps);
    expect(h.toasts).toEqual([{ kind: "info", message: STRINGS.kiosk.exitNotice }]);
  });

  it("전체화면 해제가 실패해도 종료는 계속된다(키오스크가 갇히지 않는다)", async () => {
    const h = harness({
      exitFullscreen: async () => {
        throw new Error("전체화면 API 없음");
      },
    });
    expect(await runKioskExit(h.deps)).toBe(true);
    expect(h.order).toEqual(["logout", "home:키오스크 종료", "toast"]);
  });
});
