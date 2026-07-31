import { beforeEach, describe, expect, it } from "vitest";
import {
  initialPinAttemptState,
  MAX_PIN_FAILS,
  PIN_LOCK_MS,
  type PinAttemptState,
  type PinLockRecord,
} from "@domain/auth/pinGatePolicy";
import { BackendError, NetworkError, NotAuthenticatedError } from "@adapters/http/errors";
import type { PinLockRepo } from "@adapters/storage/pinLockRepo";
import { runPinAttempt, type PinAttemptDeps } from "@screens/modals/pinPrompt/pinPromptRunner";
import { sessionStore } from "@shell/sessionStore";
import type { SessionUser } from "@domain/accounts/sessionUser";

/**
 * PIN 제출 1회의 전 경로 — 07 §6.2 · 06 §2.0 · A5
 *
 * 특히 **401의 의미가 모드마다 다르다**:
 *   verify 401 = 불일치(실패 +1) / setup 401(currentPin 미전송) = 서버에 이미 PIN 있음(실패 0)
 */

const NOW = 1_700_000_000_000;

interface Harness {
  readonly deps: PinAttemptDeps;
  readonly calls: string[];
  readonly written: PinLockRecord[];
  readonly cleared: { count: number };
  readonly markedPinSet: { count: number };
  readonly sentBodies: { newPin: string; currentPin: string | undefined }[];
}

function lockRepo(harness: {
  written: PinLockRecord[];
  cleared: { count: number };
}): PinLockRepo {
  return {
    read: () => null,
    write: (record) => {
      harness.written.push(record);
      return true;
    },
    clear: () => {
      harness.cleared.count++;
    },
  };
}

function harness(overrides: Partial<PinAttemptDeps> = {}): Harness {
  const calls: string[] = [];
  const written: PinLockRecord[] = [];
  const cleared = { count: 0 };
  const markedPinSet = { count: 0 };
  const sentBodies: { newPin: string; currentPin: string | undefined }[] = [];

  const deps: PinAttemptDeps = {
    mode: "verify",
    verifyPin: async () => {
      calls.push("verify");
    },
    setPin: async (newPin, currentPin) => {
      calls.push("set");
      sentBodies.push({ newPin, currentPin });
    },
    now: () => NOW,
    lock: lockRepo({ written, cleared }),
    markPinSet: () => {
      markedPinSet.count++;
    },
    ...overrides,
  };

  return { deps, calls, written, cleared, markedPinSet, sentBodies };
}

function rejectWith(err: unknown): () => Promise<void> {
  return () => Promise.reject(err);
}

describe("runPinAttempt — verify 모드", () => {
  it("200이면 granted이고 기기 잠금을 지운다", async () => {
    const h = harness();
    const result = await runPinAttempt(initialPinAttemptState(), "1234", h.deps);

    expect(result).toEqual({ kind: "granted" });
    expect(h.calls).toEqual(["verify"]); // 서버 왕복은 정확히 1회
    expect(h.cleared.count).toBe(1);
  });

  it("401은 retry이고 실패 카운트·쿨다운이 오른다", async () => {
    const h = harness({ verifyPin: rejectWith(new BackendError("불일치", 401, "unauthorized")) });
    const result = await runPinAttempt(initialPinAttemptState(), "1234", h.deps);

    expect(result.kind).toBe("retry");
    if (result.kind !== "retry") return;
    expect(result.state.fails).toBe(1);
    expect(result.state.cooldownUntilMs).toBeGreaterThan(NOW);
    expect(result.message).toBe("mismatch");
    expect(h.written).toHaveLength(0); // 아직 잠금 없음
  });

  it("401 × 5회면 exhausted이고 `lock.write`가 정확히 1회다", async () => {
    const h = harness({ verifyPin: rejectWith(new BackendError("불일치", 401, "unauthorized")) });

    let state: PinAttemptState = initialPinAttemptState();
    let last = await runPinAttempt(state, "1234", h.deps);
    for (let i = 1; i < MAX_PIN_FAILS; i++) {
      if (last.kind === "retry") state = last.state;
      last = await runPinAttempt(state, "1234", h.deps);
    }

    expect(last.kind).toBe("exhausted");
    if (last.kind !== "exhausted") return;
    expect(last.state.fails).toBe(MAX_PIN_FAILS);
    expect(h.written).toEqual([{ until: NOW + PIN_LOCK_MS, fails: MAX_PIN_FAILS }]);
  });

  it("409는 switchToSetup이고 **실패로 세지 않는다**", async () => {
    const h = harness({ verifyPin: rejectWith(new BackendError("미설정", 409, "conflict")) });
    const result = await runPinAttempt(initialPinAttemptState(), "1234", h.deps);

    expect(result).toEqual({ kind: "switchToSetup" });
    expect(h.written).toHaveLength(0);
  });

  it("네트워크 실패는 unavailable이고 카운트·잠금이 움직이지 않는다", async () => {
    const h = harness({ verifyPin: rejectWith(new NetworkError("끊김")) });

    let state = initialPinAttemptState();
    for (let i = 0; i < MAX_PIN_FAILS + 2; i++) {
      const result = await runPinAttempt(state, "1234", h.deps);
      expect(result).toEqual({ kind: "unavailable", message: "unavailable" });
      // 상태를 갱신할 필드가 없다 — 몇 번을 시도해도 잠기지 않고 통과도 하지 않는다.
      state = initialPinAttemptState();
    }
    expect(h.written).toHaveLength(0);
    expect(h.cleared.count).toBe(0);
  });

  it("토큰 부재(NotAuthenticatedError)도 unavailable로 접는다(게이트를 열지 않는다)", async () => {
    const h = harness({ verifyPin: rejectWith(new NotAuthenticatedError()) });
    const result = await runPinAttempt(initialPinAttemptState(), "1234", h.deps);
    expect(result).toEqual({ kind: "unavailable", message: "unavailable" });
  });

  it.each([400, 403, 500, 503])("%s는 unavailable이다", async (status) => {
    const h = harness({ verifyPin: rejectWith(new BackendError("오류", status, "x")) });
    const result = await runPinAttempt(initialPinAttemptState(), "1234", h.deps);
    expect(result.kind).toBe("unavailable");
    expect(h.written).toHaveLength(0);
  });

  it("형식이 틀린 입력은 서버로 보내지 않는다", async () => {
    const h = harness();
    const result = await runPinAttempt(initialPinAttemptState(), "12a4", h.deps);
    expect(result).toEqual({ kind: "unavailable", message: "invalidFormat" });
    expect(h.calls).toEqual([]);
  });
});

describe("runPinAttempt — setup 모드", () => {
  it("204면 granted이고 markPinSet을 부른다(§3.6 데드락 방지)", async () => {
    const h = harness({ mode: "setup" });
    const result = await runPinAttempt(initialPinAttemptState(), "5678", h.deps);

    expect(result).toEqual({ kind: "granted" });
    expect(h.calls).toEqual(["set"]);
    // 최초 설정이므로 currentPin을 보내지 않는다(06 §2.0).
    expect(h.sentBodies).toEqual([{ newPin: "5678", currentPin: undefined }]);
    expect(h.markedPinSet.count).toBe(1);
    expect(h.cleared.count).toBe(1);
  });

  it("401(currentPin 미전송)은 switchToVerify이고 실패로 세지 않는다(A5)", async () => {
    const h = harness({
      mode: "setup",
      setPin: rejectWith(new BackendError("현재 PIN이 올바르지 않습니다.", 401, "unauthorized")),
    });
    const result = await runPinAttempt(initialPinAttemptState(), "5678", h.deps);

    expect(result).toEqual({ kind: "switchToVerify" });
    expect(h.written).toHaveLength(0);
    expect(h.markedPinSet.count).toBe(0);
  });

  it("401(currentPin 전송)은 불일치로 세어 retry다", async () => {
    const h = harness({
      mode: "setup",
      currentPin: "1111",
      setPin: rejectWith(new BackendError("현재 PIN이 올바르지 않습니다.", 401, "unauthorized")),
    });
    const result = await runPinAttempt(initialPinAttemptState(), "5678", h.deps);

    expect(result.kind).toBe("retry");
    if (result.kind !== "retry") return;
    expect(result.state.fails).toBe(1);
  });

  it("400은 invalidFormat이고 markPinSet을 부르지 않는다", async () => {
    const h = harness({
      mode: "setup",
      setPin: rejectWith(new BackendError("형식 오류", 400, "invalid_argument")),
    });
    const result = await runPinAttempt(initialPinAttemptState(), "5678", h.deps);

    expect(result).toEqual({ kind: "unavailable", message: "invalidFormat" });
    expect(h.markedPinSet.count).toBe(0);
  });

  it("네트워크 실패는 unavailable이고 세션을 건드리지 않는다", async () => {
    const h = harness({ mode: "setup", setPin: rejectWith(new NetworkError("끊김")) });
    const result = await runPinAttempt(initialPinAttemptState(), "5678", h.deps);

    expect(result).toEqual({ kind: "unavailable", message: "unavailable" });
    expect(h.markedPinSet.count).toBe(0);
  });
});

describe("sessionStore.markPinSet — hasPin 갱신(버그 ②)", () => {
  const user: SessionUser = {
    id: "user-1",
    role: "admin",
    createdAt: "2026-01-01T00:00:00.000Z",
    email: "a@b.c",
    authMethod: "google",
    hasPin: false,
  };

  beforeEach(() => {
    sessionStore.setState({ currentUser: null });
  });

  it("hasPin=false 사용자를 true로 바꾼다", () => {
    sessionStore.setState({ currentUser: user });
    sessionStore.getState().markPinSet();
    expect(sessionStore.getState().currentUser?.hasPin).toBe(true);
    // 다른 필드는 보존된다(로그인 응답이 유일한 출처다).
    expect(sessionStore.getState().currentUser?.id).toBe("user-1");
    expect(sessionStore.getState().currentUser?.email).toBe("a@b.c");
  });

  it("멱등이다 — 이미 true면 객체 참조까지 그대로다(불필요한 구독 통지 금지)", () => {
    sessionStore.setState({ currentUser: { ...user, hasPin: true } });
    const before = sessionStore.getState().currentUser;
    sessionStore.getState().markPinSet();
    expect(sessionStore.getState().currentUser).toBe(before);
  });

  it("게스트에서는 아무 일도 하지 않는다(currentUser를 만들지 않는다)", () => {
    sessionStore.getState().markPinSet();
    expect(sessionStore.getState().currentUser).toBeNull();
  });

  it("currentUser를 null로 만들지 않는다(M1 구독 무영향)", () => {
    sessionStore.setState({ currentUser: user });
    let sawNull = false;
    const unsubscribe = sessionStore.subscribe(
      (s) => s.currentUser,
      (next) => {
        if (next === null) sawNull = true;
      },
    );
    sessionStore.getState().markPinSet();
    unsubscribe();
    expect(sawNull).toBe(false);
  });
});
