import { describe, expect, it } from "vitest";
import type { PinLockRepo } from "@adapters/storage/pinLockRepo";
import { BackendError, NetworkError } from "@adapters/http/errors";
import { runPinChange, type PinChangeDeps } from "@screens/account/pinChangeRunner";

/**
 * 내 PIN 변경 — 03 §13.1 (설계 §5.3)
 *
 * 가장 중요한 고정: **확인 불일치는 서버 왕복이 0회**이고, 401은 로그아웃이 아니라
 * `currentWrong`이다(`runPinAttempt` 재사용으로 `unauthorized:"reject"`가 보장된다 — E17).
 */

const NOW = 1_700_000_000_000;

interface Harness {
  readonly deps: PinChangeDeps;
  readonly calls: { setPin: { newPin: string; currentPin?: string }[]; markPinSet: number };
  readonly lockCleared: () => number;
}

function harness(overrides: Partial<PinChangeDeps> = {}): Harness {
  const calls = { setPin: [] as { newPin: string; currentPin?: string }[], markPinSet: 0 };
  let cleared = 0;

  const lock: PinLockRepo = {
    read: () => null,
    write: () => true,
    clear: () => {
      cleared++;
    },
  };

  const deps: PinChangeDeps = {
    hasPin: true,
    currentPin: "1111",
    newPin: "2222",
    confirmPin: "2222",
    setPin: async (newPin, currentPin) => {
      calls.setPin.push(currentPin === undefined ? { newPin } : { newPin, currentPin });
    },
    markPinSet: () => {
      calls.markPinSet++;
    },
    now: () => NOW,
    lock,
    ...overrides,
  };

  return { deps, calls, lockCleared: () => cleared };
}

describe("runPinChange — 형식·일치 검사(서버 왕복 없음)", () => {
  it("확인 불일치는 `confirmMismatch`이고 **서버를 부르지 않는다**", async () => {
    const h = harness({ confirmPin: "3333" });
    expect(await runPinChange(h.deps)).toEqual({ kind: "confirmMismatch" });
    expect(h.calls.setPin).toHaveLength(0);
  });

  it("새 PIN 형식이 틀리면 `invalidFormat`이고 서버를 부르지 않는다", async () => {
    const h = harness({ newPin: "12", confirmPin: "12" });
    expect(await runPinChange(h.deps)).toEqual({ kind: "invalidFormat" });
    expect(h.calls.setPin).toHaveLength(0);
  });

  it("hasPin인데 현재 PIN이 없거나 형식이 틀리면 서버를 부르지 않는다", async () => {
    for (const currentPin of [undefined, "12", "abcd"]) {
      const h = harness({ currentPin });
      expect(await runPinChange(h.deps)).toEqual({ kind: "invalidFormat" });
      expect(h.calls.setPin).toHaveLength(0);
    }
  });

  it("전각 숫자는 거부된다(서버 정규식과 같은 축)", async () => {
    const h = harness({ newPin: "１２３４", confirmPin: "１２３４" });
    expect(await runPinChange(h.deps)).toEqual({ kind: "invalidFormat" });
    expect(h.calls.setPin).toHaveLength(0);
  });
});

describe("runPinChange — 서버 왕복", () => {
  it("성공하면 `ok` · `markPinSet` 1회 · 잠금 클리어 1회다", async () => {
    const h = harness();
    expect(await runPinChange(h.deps)).toEqual({ kind: "ok" });
    expect(h.calls.setPin).toEqual([{ newPin: "2222", currentPin: "1111" }]);
    expect(h.calls.markPinSet).toBe(1);
    expect(h.lockCleared()).toBe(1);
  });

  it("401은 `currentWrong`이다(로그아웃이 아니다)", async () => {
    const h = harness({
      setPin: async () => {
        throw new BackendError("unauthorized", 401, "unauthorized");
      },
    });
    expect(await runPinChange(h.deps)).toEqual({ kind: "currentWrong" });
  });

  it("네트워크 실패는 `unavailable`이다(변경되지 않았다)", async () => {
    const h = harness({
      setPin: async () => {
        throw new NetworkError("연결 실패");
      },
    });
    expect(await runPinChange(h.deps)).toEqual({ kind: "unavailable" });
    expect(h.calls.markPinSet).toBe(0);
  });

  it("서버 400(형식)은 `invalidFormat`으로 접힌다", async () => {
    const h = harness({
      setPin: async () => {
        throw new BackendError("invalid", 400, "invalid_argument");
      },
    });
    expect(await runPinChange(h.deps)).toEqual({ kind: "invalidFormat" });
  });

  it("hasPin=false면 `currentPin` 없이 보낸다(최초 설정 경로)", async () => {
    const h = harness({ hasPin: false, currentPin: undefined });
    expect(await runPinChange(h.deps)).toEqual({ kind: "ok" });
    expect(h.calls.setPin).toEqual([{ newPin: "2222" }]);
  });

  it("hasPin=false인데 서버에 PIN이 있으면(401) `currentWrong`으로 안내한다", async () => {
    const h = harness({
      hasPin: false,
      currentPin: undefined,
      setPin: async () => {
        throw new BackendError("unauthorized", 401, "unauthorized");
      },
    });
    expect(await runPinChange(h.deps)).toEqual({ kind: "currentWrong" });
  });

  it("**연속 실패가 기기를 잠그지 않는다**(진입 게이트의 방어이지 변경 화면의 것이 아니다)", async () => {
    let written = 0;
    const lock: PinLockRepo = {
      read: () => null,
      write: () => {
        written++;
        return true;
      },
      clear: () => undefined,
    };
    for (let attempt = 0; attempt < 6; attempt++) {
      const h = harness({
        lock,
        setPin: async () => {
          throw new BackendError("unauthorized", 401, "unauthorized");
        },
      });
      expect(await runPinChange(h.deps)).toEqual({ kind: "currentWrong" });
    }
    expect(written).toBe(0);
  });
});
