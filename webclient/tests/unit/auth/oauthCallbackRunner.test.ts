import { afterEach, beforeEach, describe, expect, it } from "vitest";
import {
  applyOauthCallbackOutcome,
  captureOauthCallback,
  runOauthCallback,
  type ApplyDeps,
  type CaptureDeps,
  type RunDeps,
} from "@screens/oauthCallback/oauthCallbackRunner";
import type { OauthPendingState } from "@domain/auth/oauthCallbackPolicy";
import type { AppState } from "@domain/navigation/appState";
import type {
  GoogleExchangeOutcome,
  GoogleExchangeRequest,
  GoogleLoginResult,
} from "@adapters/auth/googleSignIn";
import type { LoginFailureReason } from "@domain/auth/loginFailure";
import {
  attachLogStore,
  createLogStore,
  createMemoryLogSink,
  detachLogStore,
  type LogStore,
} from "@adapters/storage/logStore";

/**
 * 콜백 러너 — 07 §2.2 5단계 · 설계 §4.3
 *
 * 고정하는 것: **호출 순서**(`search → takePending → scrubUrl` **before** `exchange`) ·
 * abort면 교환 0회 · **2회 호출 시 2번째가 no-pending**(StrictMode 방어).
 */

const PENDING: OauthPendingState = {
  codeVerifier: "v".repeat(43),
  state: "state-abc",
  nonce: "nonce-abc",
  returnTo: "FrameSelect",
  startedAt: 1_000_000,
};

const USER = {
  id: "devmcjo",
  role: "admin",
  createdAt: "2026-01-01T00:00:00.000Z",
  email: null,
  authMethod: "google",
  hasPin: true,
} as const;

const RESULT: GoogleLoginResult = { token: "jwt-1", expiresInSeconds: 28_800, user: USER };

let logStore: LogStore;

beforeEach(() => {
  logStore = createLogStore({ sink: createMemoryLogSink(), now: () => 0 });
  attachLogStore(logStore);
});

afterEach(() => {
  detachLogStore();
});

// ──────────────────────────── captureOauthCallback ────────────────────────────

interface CaptureHarness {
  readonly deps: CaptureDeps;
  readonly order: string[];
  /** 저장소를 흉내낸다 — `takePending`은 **읽고 지운다**. */
  slot: OauthPendingState | null;
}

function captureHarness(
  search: string,
  pending: OauthPendingState | null,
  now = PENDING.startedAt + 1_000,
): CaptureHarness {
  const order: string[] = [];
  const harness: CaptureHarness = {
    order,
    slot: pending,
    deps: {
      search: () => {
        order.push("search");
        return search;
      },
      takePending: () => {
        order.push("takePending");
        const value = harness.slot;
        harness.slot = null;
        return value;
      },
      now: () => now,
      scrubUrl: () => {
        order.push("scrubUrl");
      },
    },
  };
  return harness;
}

describe("captureOauthCallback — 순서 계약", () => {
  it("search → takePending → scrubUrl 순서로 부른다", () => {
    const h = captureHarness(`?code=c1&state=${PENDING.state}`, PENDING);
    captureOauthCallback(h.deps);
    expect(h.order).toEqual(["search", "takePending", "scrubUrl"]);
  });

  it("성공 판정이 pending의 비밀값과 clamp된 returnTo를 실어 준다", () => {
    const h = captureHarness(`?code=c1&state=${PENDING.state}`, PENDING);
    expect(captureOauthCallback(h.deps)).toEqual({
      kind: "exchange",
      code: "c1",
      codeVerifier: PENDING.codeVerifier,
      nonce: PENDING.nonce,
      returnTo: "FrameSelect",
    });
  });

  it("★ 실패 판정에서도 scrubUrl을 부른다(주소창에 code를 남기지 않는다)", () => {
    const h = captureHarness("?code=c1&state=wrong", PENDING);
    expect(captureOauthCallback(h.deps).kind).toBe("abort");
    expect(h.order).toContain("scrubUrl");
  });

  it("★ 연속 2회 호출 시 2번째는 no-pending이다(StrictMode 이중 effect 방어)", () => {
    const h = captureHarness(`?code=c1&state=${PENDING.state}`, PENDING);
    expect(captureOauthCallback(h.deps).kind).toBe("exchange");
    expect(captureOauthCallback(h.deps)).toEqual({ kind: "abort", reason: "no-pending" });
  });

  it("중단 사유를 abortReason 키로 남긴다(사유 축을 이름으로 구분)", async () => {
    const h = captureHarness("", null);
    captureOauthCallback(h.deps);
    const text = await logStore.exportText();
    expect(text).toContain("Google 로그인 중단");
    expect(text).toContain("no-pending");
  });

  it("code·state를 로그에 남기지 않는다", async () => {
    const h = captureHarness(`?code=secret-code&state=${PENDING.state}`, PENDING);
    captureOauthCallback(h.deps);
    const text = await logStore.exportText();
    expect(text).not.toContain("secret-code");
    expect(text).not.toContain(PENDING.state);
  });

  it("3분 초과는 timeout 판정이다(주입한 now로 검증 — 페이크 타이머 없음)", () => {
    const h = captureHarness(
      `?code=c1&state=${PENDING.state}`,
      PENDING,
      PENDING.startedAt + 180_001,
    );
    expect(captureOauthCallback(h.deps)).toEqual({ kind: "abort", reason: "timeout" });
  });
});

// ───────────────────────────── runOauthCallback ─────────────────────────────

interface RunHarness {
  readonly deps: RunDeps;
  readonly exchanges: GoogleExchangeRequest[];
  readonly applied: GoogleLoginResult[];
}

function runHarness(outcome: GoogleExchangeOutcome): RunHarness {
  const exchanges: GoogleExchangeRequest[] = [];
  const applied: GoogleLoginResult[] = [];
  return {
    exchanges,
    applied,
    deps: {
      redirectUri: "https://kiosk.example.app/oauth2callback",
      exchange: (req) => {
        exchanges.push(req);
        return Promise.resolve(outcome);
      },
      applySession: (result) => applied.push(result),
      now: () => 0,
    },
  };
}

describe("runOauthCallback", () => {
  it("abort면 exchange를 **0회** 부르고 사유를 cancelled로 접는다", async () => {
    for (const reason of [
      "no-pending",
      "state-mismatch",
      "provider-error",
      "timeout",
      "no-code",
    ] as const) {
      const h = runHarness({ ok: true, result: RESULT });
      await expect(runOauthCallback({ kind: "abort", reason }, h.deps)).resolves.toEqual({
        kind: "failed",
        reason: "cancelled",
      });
      expect(h.exchanges, reason).toHaveLength(0);
      expect(h.applied, reason).toHaveLength(0);
    }
  });

  it("성공 시 applySession이 1회 불리고 clamp된 returnTo를 돌려준다", async () => {
    const h = runHarness({ ok: true, result: RESULT });
    await expect(
      runOauthCallback(
        {
          kind: "exchange",
          code: "c1",
          codeVerifier: PENDING.codeVerifier,
          nonce: PENDING.nonce,
          returnTo: "Settings",
        },
        h.deps,
      ),
    ).resolves.toEqual({ kind: "success", returnTo: "Settings" });

    expect(h.applied).toEqual([RESULT]);
  });

  it("교환 요청에 deps의 redirectUri가 실린다(개시 때와 같은 문자열이어야 한다)", async () => {
    const h = runHarness({ ok: true, result: RESULT });
    await runOauthCallback(
      {
        kind: "exchange",
        code: "c1",
        codeVerifier: PENDING.codeVerifier,
        nonce: PENDING.nonce,
        returnTo: "Home",
      },
      h.deps,
    );
    expect(h.exchanges[0]).toEqual({
      code: "c1",
      codeVerifier: PENDING.codeVerifier,
      redirectUri: "https://kiosk.example.app/oauth2callback",
      nonce: PENDING.nonce,
    });
  });

  it("교환 실패는 사유를 그대로 보존한다(400과 네트워크를 뭉개지 않는다)", async () => {
    for (const reason of [
      "rejected",
      "notConfigured",
      "redirectRejected",
      "network",
    ] satisfies LoginFailureReason[]) {
      const h = runHarness({ ok: false, reason });
      await expect(
        runOauthCallback(
          {
            kind: "exchange",
            code: "c1",
            codeVerifier: PENDING.codeVerifier,
            nonce: PENDING.nonce,
            returnTo: "Home",
          },
          h.deps,
        ),
      ).resolves.toEqual({ kind: "failed", reason });
      expect(h.applied, reason).toHaveLength(0);
    }
  });
});

// ─────────────────────── applyOauthCallbackOutcome ───────────────────────

function applyHarness(): {
  readonly deps: ApplyDeps;
  readonly went: AppState[];
  readonly failed: LoginFailureReason[];
} {
  const went: AppState[] = [];
  const failed: LoginFailureReason[] = [];
  return {
    went,
    failed,
    deps: { go: (to) => went.push(to), fail: (reason) => failed.push(reason) },
  };
}

describe("applyOauthCallbackOutcome", () => {
  it("성공은 복귀 화면으로 간다(오류를 세우지 않는다)", () => {
    const h = applyHarness();
    applyOauthCallbackOutcome({ kind: "success", returnTo: "FrameSelect" }, h.deps);
    expect(h.went).toEqual(["FrameSelect"]);
    expect(h.failed).toEqual([]);
  });

  it("실패는 오류를 먼저 싣고 Login으로 간다", () => {
    const h = applyHarness();
    applyOauthCallbackOutcome({ kind: "failed", reason: "rejected" }, h.deps);
    expect(h.failed).toEqual(["rejected"]);
    expect(h.went).toEqual(["Login"]);
  });
});

// ─────────────────────────── 통합(순서 전체) ───────────────────────────

describe("capture → run 통합 — 스크럽이 교환보다 먼저다", () => {
  it("호출 순서가 search → takePending → scrubUrl → exchange다", async () => {
    const order: string[] = [];
    let slot: OauthPendingState | null = PENDING;

    const captureDeps: CaptureDeps = {
      search: () => {
        order.push("search");
        return `?code=c1&state=${PENDING.state}`;
      },
      takePending: () => {
        order.push("takePending");
        const value = slot;
        slot = null;
        return value;
      },
      now: () => PENDING.startedAt + 1_000,
      scrubUrl: () => order.push("scrubUrl"),
    };

    const runDeps: RunDeps = {
      redirectUri: "https://kiosk.example.app/oauth2callback",
      exchange: () => {
        order.push("exchange");
        return Promise.resolve({ ok: true, result: RESULT });
      },
      applySession: () => order.push("applySession"),
      now: () => 0,
    };

    const decision = captureOauthCallback(captureDeps);
    await runOauthCallback(decision, runDeps);

    expect(order).toEqual(["search", "takePending", "scrubUrl", "exchange", "applySession"]);
  });
});
