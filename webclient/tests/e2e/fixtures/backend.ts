import type { Page, Route } from "@playwright/test";
import { ACCOUNT_LIST, USERS, type MockUser } from "./users";

/**
 * 백엔드 목 — 설계 §4.4의 라우트 표 + 호출 레코더 + **미등록 경로 501 가드**
 *
 * ⚠️ base URL은 **같은 오리진**(`/__mock-api/`)이다(`playwright.config.ts`).
 *    교차 오리진이면 `X-MCPhoto-Client`·`Authorization` 때문에 CORS preflight(OPTIONS)가 먼저
 *    나가는데 `page.route`는 preflight를 가로채지 못한다(설계 §4.2).
 *    그 대가로 **`OPTIONS 204`는 E2E에서 관측할 수 없다** — 그 확인은 실측 V20-1이 소유한다.
 *
 * ⚠️ 서명 PUT의 `putUrl`도 같은 이유로 같은 오리진(`/__mock-storage/…`)으로 발급한다.
 */

export const MOCK_API_PREFIX = "/__mock-api/";
export const MOCK_STORAGE_PREFIX = "/__mock-storage/";

/** 기본 토큰. E3b가 계정별로 다른 토큰을 요구하므로 `setUser`가 덮어쓴다. */
export const E2E_TOKEN = "e2e-token-A";

export interface RecordedCall {
  readonly method: string;
  /** base 상대 경로(`uploads/prepare`) 또는 `__mock-storage/...`. */
  readonly path: string;
  /** Playwright가 소문자 이름으로 준다. */
  readonly headers: Readonly<Record<string, string>>;
  /** JSON 본문(파싱 실패·바이너리는 null). */
  readonly bodyJson: unknown;
  readonly bodyBytes: number;
  /** 라우트 표에 없는 경로였는가(501로 응답했다). */
  readonly unhandled: boolean;
}

interface FailureOverride {
  readonly status: number;
  readonly body: unknown;
}

export interface QrUsageResponse {
  readonly role: string;
  readonly blocked: boolean;
  readonly reason: "ok" | "time" | "count";
  readonly remainingMs: number;
  readonly remainingCount: number;
  readonly limits: { readonly qrHours: number; readonly qrCount: number };
}

export interface MockBackend {
  /** 관측된 모든 호출(순서 보존). */
  readonly calls: readonly RecordedCall[];
  /** 경로 접두사로 필터한다. */
  callsTo(prefix: string): RecordedCall[];
  clearCalls(): void;
  /** `POST /auth/google` 응답을 세팅한다. `fakeLogin`이 부른다. */
  setUser(user: MockUser, token: string): void;
  /** 현재 세팅된 토큰(단언용). */
  currentToken(): string;
  setFrames(frames: readonly unknown[]): void;
  setAccounts(accounts: readonly MockUser[]): void;
  setQrUsage(usage: QrUsageResponse): void;
  /** 특정 경로를 실패시킨다(경로는 base 상대). */
  fail(path: string, status: number, body?: unknown): void;
  clearFail(path: string): void;
  /**
   * 백엔드 도달 자체를 끊는다(응답이 아니라 **연결 실패**).
   *
   * ⚠️ `context.setOffline(true)`만으로는 부족하다 — `page.route`가 가로챈 요청은 네트워크
   *    스택을 타지 않아 그대로 성공한다. 오프라인을 실제로 재현하려면 라우트에서
   *    `abort("internetdisconnected")`를 해야 한다.
   */
  setNetworkDown(down: boolean): void;
  /**
   * `uploads/prepare` 응답 **직전**에 부르는 훅. E8이 여기서 OPFS를 열거해
   * "보관이 업로드보다 먼저"(M6-W)를 실브라우저에서 증명한다.
   */
  onBeforePrepare(hook: (() => Promise<void>) | null): void;
}

export function okQrUsage(role = "user"): QrUsageResponse {
  return {
    role,
    blocked: false,
    reason: "ok",
    remainingMs: 3_600_000,
    remainingCount: 10,
    limits: { qrHours: 48, qrCount: 30 },
  };
}

export function blockedQrUsage(reason: "time" | "count" = "count"): QrUsageResponse {
  return {
    role: "temp_user",
    blocked: true,
    reason,
    remainingMs: 0,
    remainingCount: 0,
    limits: { qrHours: 48, qrCount: 30 },
  };
}

function parseBody(route: Route): { json: unknown; bytes: number } {
  const raw = route.request().postData();
  if (raw === null) return { json: null, bytes: 0 };
  try {
    return { json: JSON.parse(raw), bytes: raw.length };
  } catch {
    // 서명 PUT의 이미지·영상 본문이다. 크기만 남긴다.
    return { json: null, bytes: raw.length };
  }
}

function json(route: Route, status: number, body: unknown): Promise<void> {
  return route.fulfill({
    status,
    contentType: "application/json",
    body: JSON.stringify(body ?? null),
  });
}

/**
 * 목 백엔드를 설치한다. **`page.goto` 전에** 불러야 한다.
 *
 * 라우트 핸들러가 아래 가변 상태를 **호출 시점에** 읽으므로, 설치 후에도 spec이
 * `setUser`·`fail`·`setQrUsage`로 응답을 바꿀 수 있다.
 */
export async function mockBackend(page: Page): Promise<MockBackend> {
  const calls: RecordedCall[] = [];
  const failures = new Map<string, FailureOverride>();

  let currentUser: MockUser = USERS.user;
  let currentToken = E2E_TOKEN;
  let frames: readonly unknown[] = [];
  let accounts: readonly MockUser[] = ACCOUNT_LIST;
  let qrUsage: QrUsageResponse = okQrUsage();
  let beforePrepare: (() => Promise<void>) | null = null;
  let networkDown = false;

  function record(path: string, route: Route, unhandled: boolean): void {
    const body = parseBody(route);
    calls.push({
      method: route.request().method(),
      path,
      headers: route.request().headers(),
      bodyJson: body.json,
      bodyBytes: body.bytes,
      unhandled,
    });
  }

  /** `frames/{id}` 처럼 세그먼트가 붙는 경로를 매칭한다. */
  function segments(path: string): string[] {
    return path.split("/").filter((s) => s.length > 0);
  }

  async function handleApi(path: string, method: string, route: Route): Promise<boolean> {
    const seg = segments(path);
    const body = parseBody(route).json as Record<string, unknown> | null;

    if (path === "auth/google" && method === "POST") {
      // ★ 하네스가 값을 지어내지 않는다는 증거 — 앱이 조립한 교환 요청을 여기서 검사한다.
      //    실패해도 라우트는 200을 주고, 단언은 spec이 `calls`로 한다(핸들러에서 throw하면
      //    Playwright가 라우트 오류로 삼켜 진단이 어려워진다).
      await json(route, 200, {
        token: currentToken,
        expiresIn: 3600,
        user: currentUser,
      });
      return true;
    }

    if (path === "health" && method === "GET") {
      await json(route, 200, { ok: true, deployedAt: "2026-08-01T00:00:00.000Z" });
      return true;
    }

    if (path === "frames/default" && method === "GET") {
      await json(route, 200, frames);
      return true;
    }

    if (path === "frames" && method === "GET") {
      await json(route, 200, []);
      return true;
    }

    if (path === "frames" && method === "POST") {
      const name = typeof body?.name === "string" ? body.name : "frame";
      await json(route, 201, {
        frame: {
          id: `srv-${encodeURIComponent(name)}`,
          userId: null,
          isDefault: true,
          name,
          imageUrl: `${MOCK_STORAGE_PREFIX}frames/${encodeURIComponent(name)}.png`,
          imageSize: body?.imageSize ?? { width: 1200, height: 1600 },
          slots: body?.slots ?? [],
          createdAt: "2026-08-01T00:00:00.000Z",
        },
        upload: {
          putUrl: `${MOCK_STORAGE_PREFIX}frames/${encodeURIComponent(name)}.png`,
          downloadUrl: `${MOCK_STORAGE_PREFIX}frames/${encodeURIComponent(name)}.png?t=e2e`,
          requiredHeaders: {
            "Content-Type": typeof body?.contentType === "string" ? body.contentType : "image/png",
            "x-goog-meta-firebaseStorageDownloadTokens": "e2e-frame-token",
          },
        },
      });
      return true;
    }

    if (seg.length === 2 && seg[0] === "frames" && method === "DELETE") {
      await json(route, 200, { deleted: true });
      return true;
    }

    if (path === "accounts" && method === "GET") {
      await json(route, 200, accounts);
      return true;
    }

    if (path === "accounts/me/pin/verify" && method === "POST") {
      await route.fulfill({ status: 204, body: "" });
      return true;
    }

    if (path === "accounts/me/pin" && method === "PUT") {
      await route.fulfill({ status: 204, body: "" });
      return true;
    }

    if (path === "accounts/me/qr-usage" && method === "GET") {
      await json(route, 200, qrUsage);
      return true;
    }

    if (seg.length === 2 && seg[0] === "accounts" && method === "DELETE") {
      await route.fulfill({ status: 204, body: "" });
      return true;
    }

    if (seg.length === 3 && seg[0] === "accounts" && seg[2] === "role" && method === "PATCH") {
      await route.fulfill({ status: 204, body: "" });
      return true;
    }

    if (seg.length === 3 && seg[0] === "accounts" && seg[2] === "pin" && method === "PUT") {
      await route.fulfill({ status: 204, body: "" });
      return true;
    }

    if (path === "config/temp-user-limits" && (method === "GET" || method === "PUT")) {
      await json(route, 200, { qrHours: 48, qrCount: 30 });
      return true;
    }

    if (path === "uploads/prepare" && method === "POST") {
      if (beforePrepare !== null) await beforePrepare();
      const sessionId = typeof body?.sessionId === "string" ? body.sessionId : "unknown";
      const files = Array.isArray(body?.files) ? (body.files as Record<string, unknown>[]) : [];
      await json(route, 200, {
        bucket: "e2e-bucket.firebasestorage.app",
        uploads: files.map((file) => {
          const kind = typeof file.kind === "string" ? file.kind : "final";
          const ext = typeof file.ext === "string" ? file.ext : "jpg";
          const base = `${MOCK_STORAGE_PREFIX}${sessionId}/${kind}.${ext}`;
          return {
            kind,
            putUrl: base,
            downloadUrl: `${base}?token=e2e-download-token`,
            // ★ M14: 실서버와 같은 형태로 **2개**를 준다. E2가 PUT 요청에 둘 다 있는지 본다.
            requiredHeaders: {
              "Content-Type":
                typeof file.contentType === "string" ? file.contentType : "image/jpeg",
              "x-goog-meta-firebaseStorageDownloadTokens": "e2e-storage-token",
            },
          };
        }),
      });
      return true;
    }

    if (path === "uploads/commit" && method === "POST") {
      const pageUrl = typeof body?.downloadPageUrl === "string" ? body.downloadPageUrl : "";
      await json(route, 200, {
        id: typeof body?.sessionId === "string" ? body.sessionId : "e2e-session",
        finalImageUrl: body?.finalImageUrl ?? null,
        timelapseUrl: body?.timelapseUrl ?? null,
        createdAt: "2026-08-01T00:00:00.000Z",
        expiresAt: "2026-08-02T00:00:00.000Z",
        downloadPageUrl: pageUrl,
      });
      return true;
    }

    return false;
  }

  await page.route(`**${MOCK_API_PREFIX}**`, async (route) => {
    const path = new URL(route.request().url()).pathname.slice(MOCK_API_PREFIX.length);
    const method = route.request().method();

    if (networkDown) {
      record(path, route, false);
      await route.abort("internetdisconnected");
      return;
    }

    const failure = failures.get(path);
    if (failure !== undefined) {
      record(path, route, false);
      await json(route, failure.status, failure.body);
      return;
    }

    const handled = await handleApi(path, method, route);
    record(path, route, !handled);
    if (handled) return;

    // ★ 미등록 경로 가드. 앱이 새 엔드포인트를 부르기 시작하면 조용히 통과하지 않는다.
    //   (가드가 없으면 Vite SPA 폴백이 index.html을 200으로 돌려주어 오해가 생긴다.)
    await json(route, 501, {
      error: "E2E_UNHANDLED_ROUTE",
      message: `라우트 표에 없는 경로: ${method} ${path}`,
    });
  });

  await page.route(`**${MOCK_STORAGE_PREFIX}**`, async (route) => {
    const path = new URL(route.request().url()).pathname.slice(1);
    record(path, route, false);
    const failure = failures.get(path);
    if (failure !== undefined) {
      await json(route, failure.status, failure.body);
      return;
    }
    await route.fulfill({ status: 200, body: "" });
  });

  return {
    calls,
    callsTo(prefix) {
      return calls.filter((call) => call.path.startsWith(prefix));
    },
    clearCalls() {
      calls.length = 0;
    },
    setUser(user, token) {
      currentUser = user;
      currentToken = token;
    },
    currentToken() {
      return currentToken;
    },
    setFrames(next) {
      frames = next;
    },
    setAccounts(next) {
      accounts = next;
    },
    setQrUsage(next) {
      qrUsage = next;
    },
    fail(path, status, body) {
      failures.set(path, { status, body: body ?? { error: "E2E_FORCED_FAILURE" } });
    },
    clearFail(path) {
      failures.delete(path);
    },
    setNetworkDown(down) {
      networkDown = down;
    },
    onBeforePrepare(hook) {
      beforePrepare = hook;
    },
  };
}
