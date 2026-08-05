import { afterEach, beforeEach, describe, expect, it } from "vitest";
import {
  DEFAULT_TOKEN_EXPIRES_IN_SECONDS,
  exchangeGoogleCode,
  OAUTH_CLIENT_KIND,
  startGoogleSignIn,
  type StartSignInDeps,
} from "@adapters/auth/googleSignIn";
import type { BackendClient, RequestOptions } from "@adapters/http/backendClient";
import {
  BackendError,
  NetworkError,
  NotAuthenticatedError,
  SsoNotConfiguredError,
} from "@adapters/http/errors";
import type { OauthPendingState } from "@domain/auth/oauthCallbackPolicy";
import {
  attachLogStore,
  createLogStore,
  createMemoryLogSink,
  detachLogStore,
  type LogStore,
} from "@adapters/storage/logStore";

/**
 * Google SSO 어댑터 — 07 §2.2·§2.6
 *
 * 고정하는 것: **`clientKind:"web"`** · **`auth:"none"`** · 오류 5매핑 ·
 * 실패 경로에서 `assign` **0회**.
 */

let logStore: LogStore;

beforeEach(() => {
  logStore = createLogStore({ sink: createMemoryLogSink(), now: () => 0 });
  attachLogStore(logStore);
});

afterEach(() => {
  detachLogStore();
});

// ──────────────────────────── startGoogleSignIn ────────────────────────────

interface StartHarness {
  readonly deps: StartSignInDeps;
  readonly assigned: string[];
  readonly saved: OauthPendingState[];
}

function startHarness(overrides: Partial<StartSignInDeps> = {}): StartHarness {
  const assigned: string[] = [];
  const saved: OauthPendingState[] = [];
  let tokenSeq = 0;
  const deps: StartSignInDeps = {
    clientId: "client-1.apps.googleusercontent.com",
    origin: "https://kiosk.example.app",
    createPkce: () =>
      Promise.resolve({ codeVerifier: "v".repeat(43), codeChallenge: "c".repeat(43) }),
    randomToken: () => `token-${++tokenSeq}`,
    savePending: (state) => {
      saved.push(state);
      return true;
    },
    assign: (url) => assigned.push(url),
    now: () => 1_700_000_000_000,
    ...overrides,
  };
  return { deps, assigned, saved };
}

describe("startGoogleSignIn — 성공 경로", () => {
  it("pending을 저장하고 authorize URL로 이동한다", async () => {
    const h = startHarness();
    await expect(startGoogleSignIn({ returnTo: "FrameSelect" }, h.deps)).resolves.toEqual({
      ok: true,
    });

    expect(h.saved).toHaveLength(1);
    expect(h.saved[0]).toEqual({
      codeVerifier: "v".repeat(43),
      state: "token-1",
      nonce: "token-2",
      returnTo: "FrameSelect",
      startedAt: 1_700_000_000_000,
    });
    expect(h.assigned).toHaveLength(1);
  });

  it("이동 URL에 **저장한 state·nonce가 그대로** 실린다(대조가 성립하려면)", async () => {
    const h = startHarness();
    await startGoogleSignIn({ returnTo: "Home" }, h.deps);
    expect(h.assigned[0]).toContain(`state=${h.saved[0]!.state}`);
    expect(h.assigned[0]).toContain(`nonce=${h.saved[0]!.nonce}`);
  });

  it("redirect_uri가 origin + /oauth2callback이다", async () => {
    const h = startHarness();
    await startGoogleSignIn({ returnTo: "Home" }, h.deps);
    expect(h.assigned[0]).toContain(
      `redirect_uri=${encodeURIComponent("https://kiosk.example.app/oauth2callback")}`,
    );
  });

  it("state·nonce·verifier·URL을 로그에 남기지 않고 returnTo만 남긴다", async () => {
    const h = startHarness();
    await startGoogleSignIn({ returnTo: "Settings" }, h.deps);

    const text = await logStore.exportText();
    expect(text).toContain("Google 로그인 리디렉트");
    expect(text).toContain("Settings");
    expect(text).not.toContain("token-1");
    expect(text).not.toContain("token-2");
    expect(text).not.toContain("v".repeat(43));
    expect(text).not.toContain("accounts.google.com");
  });
});

describe("startGoogleSignIn — 실패 경로에서는 assign을 부르지 않는다", () => {
  it("clientId가 비면 clientNotConfigured다", async () => {
    const h = startHarness({ clientId: "" });
    await expect(startGoogleSignIn({ returnTo: "Home" }, h.deps)).resolves.toEqual({
      ok: false,
      reason: "clientNotConfigured",
    });
    expect(h.assigned).toHaveLength(0);
    expect(h.saved).toHaveLength(0);
  });

  it("PKCE 생성 실패(null)는 network이고 저장·이동이 없다", async () => {
    const h = startHarness({ createPkce: () => Promise.resolve(null) });
    await expect(startGoogleSignIn({ returnTo: "Home" }, h.deps)).resolves.toEqual({
      ok: false,
      reason: "network",
    });
    expect(h.assigned).toHaveLength(0);
    expect(h.saved).toHaveLength(0);
  });

  it("난수가 빈 문자열이면 network다(빈 state로 대조하면 항상 통과한다)", async () => {
    const h = startHarness({ randomToken: () => "" });
    await expect(startGoogleSignIn({ returnTo: "Home" }, h.deps)).resolves.toEqual({
      ok: false,
      reason: "network",
    });
    expect(h.assigned).toHaveLength(0);
  });

  it("pending 저장 실패는 network다(저장 없이 Google에 다녀오면 무조건 취소로 끝난다)", async () => {
    const h = startHarness({ savePending: () => false });
    await expect(startGoogleSignIn({ returnTo: "Home" }, h.deps)).resolves.toEqual({
      ok: false,
      reason: "network",
    });
    expect(h.assigned).toHaveLength(0);
  });

  it("어느 실패에서도 예외가 밖으로 나가지 않는다", async () => {
    for (const overrides of [
      { clientId: "" },
      { createPkce: () => Promise.resolve(null) },
      { savePending: () => false },
      { randomToken: () => "" },
    ] satisfies Partial<StartSignInDeps>[]) {
      const h = startHarness(overrides);
      await expect(startGoogleSignIn({ returnTo: "Home" }, h.deps)).resolves.toMatchObject({
        ok: false,
      });
    }
  });
});

// ──────────────────────────── exchangeGoogleCode ────────────────────────────

interface ExchangeHarness {
  readonly client: BackendClient;
  readonly calls: RequestOptions[];
}

function exchangeClient(handler: (options: RequestOptions) => unknown): ExchangeHarness {
  const calls: RequestOptions[] = [];
  return {
    calls,
    client: {
      async request<T>(options: RequestOptions): Promise<T> {
        calls.push(options);
        return handler(options) as T;
      },
    },
  };
}

const REQ = {
  code: "auth-code-1",
  codeVerifier: "v".repeat(43),
  redirectUri: "https://kiosk.example.app/oauth2callback",
  nonce: "nonce-1",
} as const;

const OK_BODY = {
  token: "jwt-token-value",
  expiresIn: 28_800,
  user: {
    id: "devmcjo",
    role: "admin",
    createdAt: "2026-01-01T00:00:00.000Z",
    email: "devmcjo@example.com",
    authMethod: "google",
    hasPin: true,
  },
};

describe("exchangeGoogleCode — 요청 형태", () => {
  it("POST auth/google · auth:none · 본문에 clientKind:\"web\"", async () => {
    const h = exchangeClient(() => OK_BODY);
    await exchangeGoogleCode(REQ, h.client);

    expect(h.calls).toHaveLength(1);
    const call = h.calls[0]!;
    expect(call.method).toBe("POST");
    expect(call.path).toBe("auth/google");
    expect(call.auth).toBe("none");
    expect(call.body).toEqual({
      code: REQ.code,
      codeVerifier: REQ.codeVerifier,
      redirectUri: REQ.redirectUri,
      nonce: REQ.nonce,
      clientKind: "web",
    });
    expect(OAUTH_CLIENT_KIND).toBe("web");
  });

  it("성공 응답을 파싱한다", async () => {
    const h = exchangeClient(() => OK_BODY);
    const outcome = await exchangeGoogleCode(REQ, h.client);
    expect(outcome).toEqual({
      ok: true,
      result: {
        token: "jwt-token-value",
        expiresInSeconds: 28_800,
        user: {
          id: "devmcjo",
          role: "admin",
          createdAt: "2026-01-01T00:00:00.000Z",
          email: "devmcjo@example.com",
          authMethod: "google",
          hasPin: true,
        },
      },
    });
  });

  it("토큰·code·verifier를 로그에 남기지 않는다", async () => {
    const h = exchangeClient(() => OK_BODY);
    await exchangeGoogleCode(REQ, h.client);

    const text = await logStore.exportText();
    expect(text).toContain("로그인 성공");
    expect(text).toContain("devmcjo");
    expect(text).not.toContain("jwt-token-value");
    expect(text).not.toContain("auth-code-1");
    expect(text).not.toContain("v".repeat(43));
    // email은 개인정보다 — 표시에만 쓴다.
    expect(text).not.toContain("devmcjo@example.com");
  });
});

describe("exchangeGoogleCode — 오류 매핑(던지지 않는다)", () => {
  const cases: readonly [string, unknown, string][] = [
    ["501 → notConfigured", new SsoNotConfiguredError("미구성", 501, "not_implemented"), "notConfigured"],
    ["401 → rejected", new BackendError("거부", 401, "unauthorized"), "rejected"],
    ["400 → redirectRejected", new BackendError("형식", 400, "invalid_argument"), "redirectRejected"],
    ["네트워크 → network", new NetworkError("연결 불가"), "network"],
    ["타임아웃 → network", new NetworkError("시간 초과", true), "network"],
    ["500 → network", new BackendError("서버", 500, "internal"), "network"],
    ["403 → network", new BackendError("권한", 403, "forbidden"), "network"],
    ["예상 밖 예외 → network", new NotAuthenticatedError(), "network"],
  ];

  it.each(cases)("%s", async (_label, thrown, expected) => {
    const h = exchangeClient(() => {
      throw thrown;
    });
    await expect(exchangeGoogleCode(REQ, h.client)).resolves.toEqual({
      ok: false,
      reason: expected,
    });
  });

  it("400에서 운영자용 진단을 errorCode 키로 남긴다(`code`면 마스킹된다)", async () => {
    const h = exchangeClient(() => {
      throw new BackendError("redirectUri 거부", 400, "invalid_argument");
    });
    await exchangeGoogleCode(REQ, h.client);

    const text = await logStore.exportText();
    expect(text).toContain("서버가 redirectUri를 거부했다(B1 미적용 가능)");
    expect(text).toContain("invalid_argument");
  });
});

describe("exchangeGoogleCode — 200이지만 계약과 다른 응답", () => {
  it("token이 없으면 network + 형식 오류 로그다", async () => {
    const h = exchangeClient(() => ({ expiresIn: 100, user: OK_BODY.user }));
    await expect(exchangeGoogleCode(REQ, h.client)).resolves.toEqual({
      ok: false,
      reason: "network",
    });
    expect(await logStore.exportText()).toContain("로그인 응답 형식 오류");
  });

  it("token이 공백뿐이면 거부한다(빈 Bearer로 401 루프를 만들지 않는다)", async () => {
    const h = exchangeClient(() => ({ ...OK_BODY, token: "   " }));
    expect((await exchangeGoogleCode(REQ, h.client)).ok).toBe(false);
  });

  it("user가 파싱 불가면 network다", async () => {
    for (const user of [undefined, null, {}, { role: "admin" }, 7]) {
      const h = exchangeClient(() => ({ ...OK_BODY, user }));
      expect((await exchangeGoogleCode(REQ, h.client)).ok, JSON.stringify(user)).toBe(false);
    }
  });

  it("본문이 객체가 아니면 network다", async () => {
    for (const body of [null, "text", 42, []]) {
      const h = exchangeClient(() => body);
      expect((await exchangeGoogleCode(REQ, h.client)).ok).toBe(false);
    }
  });

  it("expiresIn이 양수가 아니면 기본값(8시간)으로 폴백하고 경고한다", async () => {
    for (const expiresIn of [undefined, 0, -1, "3600", Number.NaN]) {
      const h = exchangeClient(() => ({ ...OK_BODY, expiresIn }));
      const outcome = await exchangeGoogleCode(REQ, h.client);
      expect(outcome.ok && outcome.result.expiresInSeconds, String(expiresIn)).toBe(
        DEFAULT_TOKEN_EXPIRES_IN_SECONDS,
      );
    }
    expect(await logStore.exportText()).toContain("expiresIn이 유효하지 않아");
  });

  it("소수 expiresIn은 내림한다", async () => {
    const h = exchangeClient(() => ({ ...OK_BODY, expiresIn: 100.9 }));
    const outcome = await exchangeGoogleCode(REQ, h.client);
    expect(outcome.ok && outcome.result.expiresInSeconds).toBe(100);
  });

  it("알 수 없는 역할은 최소 권한으로 떨어진다(권한 상승 방지)", async () => {
    const h = exchangeClient(() => ({ ...OK_BODY, user: { ...OK_BODY.user, role: "nonsense" } }));
    const outcome = await exchangeGoogleCode(REQ, h.client);
    expect(outcome.ok && outcome.result.user.role).toBe("user");
  });
});
