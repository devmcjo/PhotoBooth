import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { createBackendClient } from "@adapters/http/backendClient";
import { createAccountService } from "@adapters/http/accountService";
import {
  getToken,
  installTokenLifecycle,
  resetAuthForTests,
  setToken,
  uninstallTokenLifecycle,
} from "@shell/authStore";
import { handleSessionExpired } from "@shell/sessionExpiry";
import { sessionStore, type CapturedCut } from "@shell/sessionStore";
import { shellStore } from "@shell/shellStore";
import { createEmptySession } from "@domain/capture/captureSession";
import type { SessionUser } from "@domain/accounts/sessionUser";
import { STRINGS } from "@ui/strings";
import {
  attachLogStore,
  createLogStore,
  createMemoryLogSink,
  detachLogStore,
  type LogStore,
} from "@adapters/storage/logStore";

/**
 * 401 → 세션 해제 (C10 · 07 §4.3 · 02 §5.2)
 *
 * 고정하는 것 4개:
 *   ① Bearer가 붙은 401 → 사용자 해제 + 토큰 폐기(**M1 경유**) + **촬영 데이터 유지**
 *   ② PIN 검증의 401 → 세션 **불변**(E17)
 *   ③ 토큰 미부착 401 → 세션 불변
 *   ④ 이미 게스트면 멱등(토스트 0건)
 */

const USER: SessionUser = {
  id: "devmcjo",
  role: "admin",
  createdAt: "2026-01-01T00:00:00.000Z",
  email: "devmcjo@example.com",
  authMethod: "google",
  hasPin: true,
};

const BASE = "https://api.example.com/api/";

function json(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}

/** 항상 401을 돌려주는 가짜 fetch. */
function unauthorizedFetch(): typeof fetch {
  return (async () => json({ error: { code: "unauthorized", message: "권한 없음" } }, 401)) as unknown as typeof fetch;
}

function client(fetchImpl: typeof fetch) {
  return createBackendClient({
    fetchImpl,
    baseUrl: BASE,
    gateKey: "web-gate-key",
    // 실제 토큰 홀더를 쓴다 — M1 구독을 경유하는지까지 관측해야 한다.
    tokenProvider: getToken,
    now: () => 0,
  });
}

/** 촬영 데이터가 살아 있는 로그인 세션을 만든다. */
function loggedInWithCaptureData(): void {
  const cut: CapturedCut = { index: 0, fileName: "cut1.jpg" };
  sessionStore.getState().login(USER);
  setToken("jwt-abc", 28_800, 0);
  sessionStore.setState({
    session: { ...createEmptySession<CapturedCut>(), cuts: [cut] },
    sessionId: "20260731_120000_uuid",
  });
  sessionStore.getState().setFinalImage({ blob: new Blob(["x"]), format: "Jpg" });
}

let logStore: LogStore;

beforeEach(() => {
  logStore = createLogStore({ sink: createMemoryLogSink(), now: () => 0 });
  attachLogStore(logStore);
  sessionStore.setState({
    currentUser: null,
    session: createEmptySession<CapturedCut>(),
    sessionId: null,
    selectedFilter: "None",
    finalImage: null,
  });
  shellStore.setState({ screen: "Home", overlayReturnTo: null, modals: [], toasts: [] });
  resetAuthForTests();
  installTokenLifecycle();
});

afterEach(() => {
  uninstallTokenLifecycle();
  detachLogStore();
});

describe("① auth:required 401 → 세션 해제 + 촬영 데이터 유지", () => {
  it("사용자가 해제되고 토큰이 M1 구독을 통해 폐기된다", async () => {
    loggedInWithCaptureData();
    await client(unauthorizedFetch())
      .request({ path: "accounts", auth: "required" })
      .catch(() => undefined);

    expect(sessionStore.getState().currentUser).toBeNull();
    expect(getToken()).toBeNull();
  });

  it("★ 촬영 데이터(cuts·sessionId·finalImage)는 **그대로** 남는다(02 §5.2)", async () => {
    loggedInWithCaptureData();
    await client(unauthorizedFetch())
      .request({ path: "accounts", auth: "required" })
      .catch(() => undefined);

    // 여기서 폐기되면 `Qr`의 [기기에 저장]까지 죽는다.
    expect(sessionStore.getState().session.cuts).toHaveLength(1);
    expect(sessionStore.getState().sessionId).toBe("20260731_120000_uuid");
    expect(sessionStore.getState().finalImage).not.toBeNull();
  });

  it("만료 토스트가 규격 문구로 1건 뜬다", async () => {
    loggedInWithCaptureData();
    await client(unauthorizedFetch())
      .request({ path: "accounts", auth: "required" })
      .catch(() => undefined);

    const toasts = shellStore.getState().toasts;
    expect(toasts).toHaveLength(1);
    expect(toasts[0]!.kind).toBe("error");
    expect(toasts[0]!.message).toBe(STRINGS.error.sessionExpired);
  });

  it("진단 로그에 사유와 경로가 남는다", async () => {
    loggedInWithCaptureData();
    await client(unauthorizedFetch())
      .request({ path: "accounts", auth: "required" })
      .catch(() => undefined);

    const text = await logStore.exportText();
    expect(text).toContain("세션 만료 감지(401) — 세션 해제");
    expect(text).toContain("accounts");
    expect(text).not.toContain("jwt-abc");
  });

  it("업로드(auth:optional · 로그인 상태)의 401도 만료다(F5 — 서버가 무효 토큰을 거부한다)", async () => {
    loggedInWithCaptureData();
    await client(unauthorizedFetch())
      .request({ method: "POST", path: "uploads/prepare", auth: "optional" })
      .catch(() => undefined);

    expect(sessionStore.getState().currentUser).toBeNull();
    // 결과물은 남는다 — 규격은 "로컬에 남아 있음을 알린다"다.
    expect(sessionStore.getState().finalImage).not.toBeNull();
  });
});

describe("② PIN 검증의 401은 불일치다(E17)", () => {
  it("`unauthorized:\"reject\"`가 붙어 세션이 불변이다", async () => {
    loggedInWithCaptureData();
    await createAccountService(client(unauthorizedFetch()))
      .verifyMyPin("1234")
      .catch(() => undefined);

    expect(sessionStore.getState().currentUser).not.toBeNull();
    expect(getToken()).toBe("jwt-abc");
    expect(shellStore.getState().toasts).toHaveLength(0);
  });

  it("PIN을 3번 틀려도 로그인 상태가 유지된다", async () => {
    loggedInWithCaptureData();
    const service = createAccountService(client(unauthorizedFetch()));
    for (let i = 0; i < 3; i++) {
      await service.verifyMyPin("0000").catch(() => undefined);
    }
    expect(sessionStore.getState().currentUser).not.toBeNull();
  });

  it("명시 reject는 다른 경로에서도 세션을 건드리지 않는다", async () => {
    loggedInWithCaptureData();
    await client(unauthorizedFetch())
      .request({ path: "accounts", auth: "required", unauthorized: "reject" })
      .catch(() => undefined);

    expect(sessionStore.getState().currentUser).not.toBeNull();
  });
});

describe("③ 토큰이 붙지 않은 401은 세션 문제가 아니다", () => {
  it("auth:none 401(예: /auth/google 계정 거부)에서 세션이 불변이다", async () => {
    loggedInWithCaptureData();
    await client(unauthorizedFetch())
      .request({ method: "POST", path: "auth/google", auth: "none" })
      .catch(() => undefined);

    expect(sessionStore.getState().currentUser).not.toBeNull();
    expect(shellStore.getState().toasts).toHaveLength(0);
  });

  it("게스트의 auth:optional 401(게이트 키 문제)에서도 불변이다", async () => {
    // 로그인하지 않은 상태 — 토큰이 없다.
    await client(unauthorizedFetch())
      .request({ method: "POST", path: "uploads/prepare", auth: "optional" })
      .catch(() => undefined);

    expect(sessionStore.getState().currentUser).toBeNull();
    expect(shellStore.getState().toasts).toHaveLength(0);
  });
});

describe("④ 401이 아닌 실패는 세션을 건드리지 않는다", () => {
  const statuses = [400, 403, 404, 409, 500, 501] as const;

  it.each(statuses)("%i 응답에서 세션이 불변이다", async (status) => {
    loggedInWithCaptureData();
    const fetchImpl = (async () =>
      json({ error: { code: "x", message: "y" } }, status)) as unknown as typeof fetch;

    await client(fetchImpl)
      .request({ path: "accounts", auth: "required" })
      .catch(() => undefined);

    expect(sessionStore.getState().currentUser).not.toBeNull();
    expect(getToken()).toBe("jwt-abc");
  });

  it("네트워크 실패(응답 없음)에서도 세션이 불변이다", async () => {
    loggedInWithCaptureData();
    const fetchImpl = (() => {
      throw new TypeError("Failed to fetch");
    }) as unknown as typeof fetch;

    await client(fetchImpl)
      .request({ path: "accounts", auth: "required" })
      .catch(() => undefined);

    expect(sessionStore.getState().currentUser).not.toBeNull();
  });
});

describe("⑤ handleSessionExpired는 멱등이다", () => {
  it("이미 게스트면 토스트도 로그도 남기지 않는다", async () => {
    handleSessionExpired("accounts");
    expect(shellStore.getState().toasts).toHaveLength(0);
    expect(await logStore.exportText()).not.toContain("세션 만료 감지");
  });

  it("연속 호출에도 토스트는 1건이다(동시 요청이 함께 401을 맞아도 안전하다)", () => {
    loggedInWithCaptureData();
    handleSessionExpired("accounts");
    handleSessionExpired("frames");
    handleSessionExpired();
    expect(shellStore.getState().toasts).toHaveLength(1);
  });

  it("expireSession은 logout과 달리 촬영 데이터를 지우지 않는다(핵심 차이)", () => {
    loggedInWithCaptureData();
    sessionStore.getState().expireSession();
    expect(sessionStore.getState().currentUser).toBeNull();
    expect(sessionStore.getState().sessionId).not.toBeNull();

    // 대조군: logout()은 지운다.
    sessionStore.getState().login(USER);
    sessionStore.getState().logout();
    expect(sessionStore.getState().sessionId).toBeNull();
    expect(sessionStore.getState().finalImage).toBeNull();
  });
});

describe("⑥ onSessionExpired 주입(테스트 격리 지점)", () => {
  it("주입하면 그것만 불리고 실 스토어는 건드리지 않는다", async () => {
    loggedInWithCaptureData();
    const paths: (string | undefined)[] = [];

    const injected = createBackendClient({
      fetchImpl: unauthorizedFetch(),
      baseUrl: BASE,
      gateKey: "k",
      tokenProvider: getToken,
      now: () => 0,
      onSessionExpired: (path) => paths.push(path),
    });
    await injected.request({ path: "frames", auth: "required" }).catch(() => undefined);

    expect(paths).toEqual(["frames"]);
    expect(sessionStore.getState().currentUser).not.toBeNull();
  });
});
