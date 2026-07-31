import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import {
  createBackendClient,
  GATE_KEY_HEADER,
  REQUEST_TIMEOUT_MS,
  type BackendClient,
} from "@adapters/http/backendClient";
import {
  BackendError,
  isConflict,
  isForbidden,
  isNotFound,
  isUnauthorized,
  NetworkError,
  NotAuthenticatedError,
  parseErrorEnvelope,
  SsoNotConfiguredError,
  TempUserLimitError,
  toBackendError,
} from "@adapters/http/errors";
import { createHealthService } from "@adapters/http/healthService";
import { createQrUsageService, isTempUserBlocked } from "@adapters/http/qrUsageService";
import { createUploadGateway } from "@adapters/http/uploadGateway";
import { createFrameRepository, parseFrame } from "@adapters/http/frameRepository";
import { createAccountService } from "@adapters/http/accountService";
import { createTempUserLimitsService } from "@adapters/http/tempUserLimitsService";
import {
  attachLogStore,
  createLogStore,
  createMemoryLogSink,
  detachLogStore,
  type LogStore,
} from "@adapters/storage/logStore";

/** 요청을 기록하는 가짜 fetch. */
interface Recorded {
  url: string;
  init: RequestInit;
}

function fakeFetch(
  handler: (recorded: Recorded) => Response | Promise<Response> | never,
): { impl: typeof fetch; calls: Recorded[] } {
  const calls: Recorded[] = [];
  const impl = (async (input: RequestInfo | URL, init: RequestInit = {}) => {
    const recorded = { url: String(input), init };
    calls.push(recorded);
    return handler(recorded);
  }) as unknown as typeof fetch;
  return { impl, calls };
}

function json(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}

const BASE = "https://api.example.com/api/";
let logStore: LogStore;

function client(
  handler: (recorded: Recorded) => Response | Promise<Response>,
  token: string | null = null,
): { client: BackendClient; calls: Recorded[] } {
  const { impl, calls } = fakeFetch(handler);
  return {
    client: createBackendClient({
      fetchImpl: impl,
      baseUrl: BASE,
      gateKey: "web-gate-key",
      tokenProvider: () => token,
      now: () => 0,
    }),
    calls,
  };
}

beforeEach(() => {
  logStore = createLogStore({ sink: createMemoryLogSink(), now: () => 0 });
  attachLogStore(logStore);
});

afterEach(() => {
  detachLogStore();
});

describe("backendClient — 헤더 조립", () => {
  it("게이트 키를 모든 호출에 부착한다", async () => {
    const { client: c, calls } = client(() => json({ ok: true }));
    await c.request({ path: "health" });
    expect((calls[0]!.init.headers as Record<string, string>)[GATE_KEY_HEADER]).toBe("web-gate-key");
  });

  it("게이트 키가 비면 헤더를 붙이지 않는다(빈 헤더 전송 금지)", async () => {
    const { impl, calls } = fakeFetch(() => json({}));
    const c = createBackendClient({
      fetchImpl: impl,
      baseUrl: BASE,
      gateKey: "",
      tokenProvider: () => null,
    });
    await c.request({ path: "health" });
    expect(GATE_KEY_HEADER in (calls[0]!.init.headers as Record<string, string>)).toBe(false);
  });

  it("auth: none이면 토큰이 있어도 Bearer를 붙이지 않는다", async () => {
    const { client: c, calls } = client(() => json({}), "jwt-abc");
    await c.request({ path: "frames/default", auth: "none" });
    expect("Authorization" in (calls[0]!.init.headers as Record<string, string>)).toBe(false);
  });

  it("auth: optional은 토큰이 있을 때만 붙인다(게스트 업로드 경로)", async () => {
    const withToken = client(() => json({}), "jwt-abc");
    await withToken.client.request({ path: "uploads/prepare", method: "POST", auth: "optional" });
    expect((withToken.calls[0]!.init.headers as Record<string, string>).Authorization).toBe(
      "Bearer jwt-abc",
    );

    const guest = client(() => json({}), null);
    await guest.client.request({ path: "uploads/prepare", method: "POST", auth: "optional" });
    expect("Authorization" in (guest.calls[0]!.init.headers as Record<string, string>)).toBe(false);
  });

  it("auth: required + 토큰 없음이면 **요청을 보내지 않고** NotAuthenticatedError다", async () => {
    const { client: c, calls } = client(() => json({}), null);
    await expect(c.request({ path: "accounts", auth: "required" })).rejects.toBeInstanceOf(
      NotAuthenticatedError,
    );
    expect(calls).toHaveLength(0);
  });

  it("쿠키를 보내지 않는다(Bearer 전용)", async () => {
    const { client: c, calls } = client(() => json({}));
    await c.request({ path: "health" });
    expect(calls[0]!.init.credentials).toBe("omit");
  });

  it("base URL과 상대 경로를 결합하고 쿼리를 붙인다", async () => {
    const { client: c, calls } = client(() => json({}), "t");
    await c.request({ path: "frames", query: { userId: "me", skip: undefined }, auth: "required" });
    expect(calls[0]!.url).toBe("https://api.example.com/api/frames?userId=me");
  });

  it("선행 슬래시가 있어도 base 경로를 잃지 않는다", async () => {
    const { client: c, calls } = client(() => json({}));
    await c.request({ path: "/health" });
    expect(calls[0]!.url).toBe("https://api.example.com/api/health");
  });

  it("본문이 있으면 Content-Type을 붙이고 JSON으로 직렬화한다", async () => {
    const { client: c, calls } = client(() => json({}), "t");
    await c.request({ method: "POST", path: "x", body: { a: 1 }, auth: "required" });
    expect((calls[0]!.init.headers as Record<string, string>)["Content-Type"]).toBe(
      "application/json",
    );
    expect(calls[0]!.init.body).toBe('{"a":1}');
  });
});

describe("backendClient — 에러 매핑(화면은 타입으로 분기한다)", () => {
  const cases: [number, string, (err: unknown) => boolean][] = [
    [400, "invalid_argument", (e) => e instanceof BackendError && e.status === 400],
    [401, "unauthorized", isUnauthorized],
    [403, "forbidden", isForbidden],
    [404, "not_found", isNotFound],
    [409, "conflict", isConflict],
    [500, "internal", (e) => e instanceof BackendError && e.status === 500],
    [501, "not_implemented", (e) => e instanceof SsoNotConfiguredError],
  ];

  it.each(cases)("%i %s 가 고유 타입으로 매핑된다", async (status, code, predicate) => {
    const { client: c } = client(() => json({ error: { code, message: "실패" } }, status));
    await expect(c.request({ path: "x" })).rejects.toSatisfy(predicate);
  });

  it("TEMP_USER_* 403은 권한 오류가 아니라 한도 오류다", async () => {
    for (const [code, reason] of [
      ["TEMP_USER_TIME_EXCEEDED", "time"],
      ["TEMP_USER_COUNT_EXCEEDED", "count"],
    ] as const) {
      const { client: c } = client(() =>
        json({ error: { code, message: "무료 사용이 끝났습니다." } }, 403),
      );
      const error = await c.request({ path: "uploads/prepare" }).catch((e: unknown) => e);
      expect(error).toBeInstanceOf(TempUserLimitError);
      expect((error as TempUserLimitError).reason).toBe(reason);
      // 권한 오류로 분류되지 않아야 한다(문구가 다르다).
      expect(isForbidden(error)).toBe(false);
    }
  });

  it("네트워크 실패는 상태코드 오류와 섞이지 않는다", async () => {
    const { client: c } = client(() => {
      throw new TypeError("Failed to fetch");
    });
    const error = await c.request({ path: "health" }).catch((e: unknown) => e);
    expect(error).toBeInstanceOf(NetworkError);
    expect(error).not.toBeInstanceOf(BackendError);
    // CORS 차단 가능성을 로그에 남긴다(브라우저가 구분해 주지 않는다).
    expect((error as NetworkError).message).toContain("CORS");
  });

  it("타임아웃은 AbortError → NetworkError(timedOut)다", async () => {
    vi.useFakeTimers();
    try {
      const { impl } = fakeFetch(
        (recorded) =>
          new Promise<Response>((_resolve, reject) => {
            recorded.init.signal?.addEventListener("abort", () =>
              reject(new DOMException("aborted", "AbortError")),
            );
          }),
      );
      const c = createBackendClient({
        fetchImpl: impl,
        baseUrl: BASE,
        gateKey: "k",
        tokenProvider: () => null,
      });
      const promise = c.request({ path: "health" }).catch((e: unknown) => e);
      await vi.advanceTimersByTimeAsync(REQUEST_TIMEOUT_MS + 1);
      const error = await promise;
      expect(error).toBeInstanceOf(NetworkError);
      expect((error as NetworkError).timedOut).toBe(true);
    } finally {
      vi.useRealTimers();
    }
  });

  it("오류 본문이 없거나 형식이 달라도 상태코드로 코드를 만든다", async () => {
    const { client: c } = client(() => new Response("<html>502</html>", { status: 502 }));
    const error = await c.request({ path: "x" }).catch((e: unknown) => e);
    expect(error).toBeInstanceOf(BackendError);
    expect((error as BackendError).code).toBe("http_502");
  });

  it("parseErrorEnvelope가 상태별 폴백 코드를 낸다", () => {
    expect(parseErrorEnvelope(null, 401).code).toBe("unauthorized");
    expect(parseErrorEnvelope({}, 409).code).toBe("conflict");
    expect(parseErrorEnvelope({ error: { code: "custom" } }, 400).code).toBe("custom");
  });

  it("toBackendError는 알 수 없는 코드도 BackendError로 준다", () => {
    expect(toBackendError(418, { code: "teapot", message: "…" })).toBeInstanceOf(BackendError);
  });

  it("204는 본문 없이 성공한다", async () => {
    const { client: c } = client(() => new Response(null, { status: 204 }), "t");
    await expect(
      c.request({ method: "DELETE", path: "accounts/x", auth: "required" }),
    ).resolves.toBeUndefined();
  });

  it("자동 재시도를 하지 않는다(비멱등 호출 중복 집계 방지)", async () => {
    const { client: c, calls } = client(() => json({ error: { code: "internal" } }, 500), "t");
    await c.request({ method: "POST", path: "uploads/commit", auth: "optional" }).catch(() => undefined);
    expect(calls).toHaveLength(1);
  });
});

describe("backendClient — 로깅 규칙(analysis/41 §8)", () => {
  it("토큰·본문을 로그에 남기지 않는다", async () => {
    const { client: c } = client(() => json({ secretPayload: "should-not-log" }), "jwt-super-secret");
    await c.request({ method: "POST", path: "accounts/me/pin/verify", body: { pin: "1234" }, auth: "required" });

    const text = await logStore.exportText();
    expect(text).not.toContain("jwt-super-secret");
    expect(text).not.toContain("1234");
    expect(text).not.toContain("should-not-log");
    // 대신 메서드·경로·상태·소요가 남는다.
    expect(text).toContain("accounts/me/pin/verify");
    expect(text).toContain("status");
  });

  it("오류 응답도 코드만 남긴다", async () => {
    const { client: c } = client(() =>
      json({ error: { code: "unauthorized", message: "비밀 사유" } }, 401),
    );
    await c.request({ path: "x" }).catch(() => undefined);
    const text = await logStore.exportText();
    expect(text).toContain("unauthorized");
    expect(text).not.toContain("비밀 사유");
  });
});

describe("healthService — 두 프로브(06 §2.1)", () => {
  it("health 200 + frames 200이면 게이트 키가 유효하다", async () => {
    const { client: c } = client((recorded) =>
      recorded.url.includes("health")
        ? json({ status: "ok", deployedAt: "2026-07-30T00:00:00Z" })
        : json([]),
    );
    const result = await createHealthService(c).probe();
    expect(result.reachable).toBe(true);
    expect(result.deployedAt).toBe("2026-07-30T00:00:00Z");
    expect(result.gateKeyValid).toBe(true);
  });

  it("frames가 401이면 게이트 키가 무효다(health 200으로는 판정할 수 없다)", async () => {
    const { client: c } = client((recorded) =>
      recorded.url.includes("health")
        ? json({ status: "ok" })
        : json({ error: { code: "unauthorized" } }, 401),
    );
    const result = await createHealthService(c).probe();
    expect(result.reachable).toBe(true);
    expect(result.gateKeyValid).toBe(false);
  });

  it("서버에 도달하지 못하면 키 유효성은 '알 수 없음'(null)이다", async () => {
    const { client: c } = client(() => {
      throw new TypeError("Failed to fetch");
    });
    const result = await createHealthService(c).probe();
    expect(result.reachable).toBe(false);
    expect(result.gateKeyValid).toBeNull();
  });

  it("frames가 500이면 키 판정 근거가 되지 못한다(null)", async () => {
    const { client: c } = client((recorded) =>
      recorded.url.includes("health") ? json({ status: "ok" }) : json({ error: { code: "internal" } }, 500),
    );
    expect((await createHealthService(c).probe()).gateKeyValid).toBeNull();
  });
});

describe("qrUsageService — fail-open(M9)", () => {
  it("정상 응답을 파싱한다", async () => {
    const { client: c } = client(() =>
      json({
        role: "temp_user",
        blocked: true,
        reason: "count",
        remainingMs: 0,
        remainingCount: 0,
        limits: { qrHours: 48, qrCount: 30 },
      }),
      "t",
    );
    const usage = await createQrUsageService(c).fetch();
    expect(usage.role).toBe("temp_user");
    expect(usage.blocked).toBe(true);
    expect(isTempUserBlocked(usage)).toBe(true);
  });

  it("조회 실패는 허용으로 떨어진다(촬영을 막지 않는다)", async () => {
    const { client: c } = client(() => json({ error: { code: "not_found" } }, 404), "t");
    const usage = await createQrUsageService(c).fetch();
    expect(usage.blocked).toBe(false);
    expect(isTempUserBlocked(usage)).toBe(false);
  });

  it("non-TempUser의 remaining 0은 '무제한'이지 소진이 아니다", async () => {
    const { client: c } = client(() =>
      json({ role: "admin", blocked: false, reason: "ok", remainingMs: 0, remainingCount: 0 }),
      "t",
    );
    const usage = await createQrUsageService(c).fetch();
    expect(usage.remainingCount).toBe(0);
    expect(isTempUserBlocked(usage)).toBe(false);
  });

  it("blocked인데 역할이 TempUser가 아니면 차단으로 보지 않는다", async () => {
    const { client: c } = client(() => json({ role: "user", blocked: true, reason: "time" }), "t");
    expect(isTempUserBlocked(await createQrUsageService(c).fetch())).toBe(false);
  });
});

describe("uploadGateway — M14 요구 헤더 보존", () => {
  it("requiredHeaders를 응답 그대로 보존한다(키를 골라 담지 않는다)", async () => {
    const headers = {
      "Content-Type": "image/jpeg",
      "x-goog-meta-firebaseStorageDownloadTokens": "tok",
      "x-future-header": "keep",
    };
    const { client: c } = client(() =>
      json({
        bucket: "b",
        uploads: [{ kind: "final", putUrl: "https://put", downloadUrl: "https://dl", requiredHeaders: headers }],
      }),
    );
    const result = await createUploadGateway(c).prepare({
      sessionId: "20260730_120000_3f2a1b4c-5d6e-4f70-8a9b-0c1d2e3f4a5b",
      files: [{ kind: "final", ext: "jpg", contentType: "image/jpeg" }],
    });
    expect(result.uploads[0]!.requiredHeaders).toEqual(headers);
  });

  it("형식이 깨진 upload 항목은 버린다(putUrl 없는 항목으로 PUT을 시도하지 않는다)", async () => {
    const { client: c } = client(() =>
      json({
        bucket: "b",
        uploads: [
          { kind: "final", downloadUrl: "https://dl" },
          { kind: "bogus", putUrl: "https://put", downloadUrl: "https://dl" },
          { kind: "timelapse", putUrl: "https://put", downloadUrl: "https://dl" },
        ],
      }),
    );
    const result = await createUploadGateway(c).prepare({ sessionId: "s", files: [] });
    expect(result.uploads.map((u) => u.kind)).toEqual(["timelapse"]);
  });

  it("commit은 optional Bearer로 나간다(게스트 경로에 Authorization이 없다)", async () => {
    const { client: c, calls } = client(
      () => json({ id: "s", finalImageUrl: null, timelapseUrl: null, createdAt: "", expiresAt: "", downloadPageUrl: "" }, 201),
      null,
    );
    await createUploadGateway(c).commit({
      sessionId: "s",
      finalImageUrl: "https://dl",
      timelapseUrl: null,
      retentionHours: 24,
      downloadPageUrl: "https://page",
    });
    expect("Authorization" in (calls[0]!.init.headers as Record<string, string>)).toBe(false);
  });
});

describe("frameRepository", () => {
  it("PUT /frames/{id} 함수를 노출하지 않는다(편집은 로컬 전용)", () => {
    const repo = createFrameRepository(client(() => json([])).client);
    expect(Object.keys(repo).sort()).toEqual([
      "createFrame",
      "deleteFrame",
      "getDefaultFrames",
      "getUserFrames",
    ]);
  });

  it("프레임 DTO를 파싱하고 잘못된 슬롯을 버린다", () => {
    const frame = parseFrame({
      id: "db1",
      name: "베이직 4컷",
      isDefault: true,
      imageUrl: "https://img",
      imageSize: { width: 1200, height: 1600 },
      slots: [
        { index: 0, x: 1, y: 2, width: 3, height: 4 },
        { index: 1, x: "bad", y: 2, width: 3, height: 4 },
      ],
      createdAt: "2026-01-01T00:00:00Z",
    });
    expect(frame?.slots).toHaveLength(1);
    expect(frame?.userId).toBeNull();
  });

  it("id·name이 없으면 null이다", () => {
    expect(parseFrame({ name: "이름만" })).toBeNull();
    expect(parseFrame({ id: "x" })).toBeNull();
    expect(parseFrame(null)).toBeNull();
  });

  it("배열·{frames:[]} 두 형태를 모두 읽는다", async () => {
    const asArray = client(() => json([{ id: "a", name: "A" }]));
    expect(await createFrameRepository(asArray.client).getDefaultFrames()).toHaveLength(1);

    const wrapped = client(() => json({ frames: [{ id: "b", name: "B" }] }));
    expect(await createFrameRepository(wrapped.client).getDefaultFrames()).toHaveLength(1);
  });

  it("공용 프레임 조회는 Bearer 없이 게이트 키만으로 나간다", async () => {
    const { client: c, calls } = client(() => json([]), "jwt");
    await createFrameRepository(c).getDefaultFrames();
    expect("Authorization" in (calls[0]!.init.headers as Record<string, string>)).toBe(false);
  });
});

describe("accountService — PIN 본문 규칙(06 §2.0)", () => {
  it("최초 설정은 currentPin을 생략하고, 변경은 포함한다", async () => {
    const first = client(() => json({}), "t");
    await createAccountService(first.client).setMyPin("1234");
    expect(first.calls[0]!.init.body).toBe('{"newPin":"1234"}');

    const change = client(() => json({}), "t");
    await createAccountService(change.client).setMyPin("5678", "1234");
    expect(change.calls[0]!.init.body).toBe('{"newPin":"5678","currentPin":"1234"}');
  });

  it("목록 조회 실패는 예외다 — 빈 목록으로 표시하지 않는다", async () => {
    const { client: c } = client(() => json({ error: { code: "forbidden" } }, 403), "t");
    await expect(createAccountService(c).list()).rejects.toBeInstanceOf(BackendError);
  });

  it("목록의 잘못된 항목만 버리고 나머지는 살린다", async () => {
    const { client: c } = client(
      () => json([{ id: "a", role: "admin" }, { role: "user" }, { id: "b", role: "nonsense" }]),
      "t",
    );
    const users = await createAccountService(c).list();
    expect(users.map((u) => u.id)).toEqual(["a", "b"]);
    // 알 수 없는 역할은 최소 권한으로 떨어진다(권한 상승 방지).
    expect(users[1]!.role).toBe("user");
  });

  it("계정 id를 URL 인코딩한다", async () => {
    const { client: c, calls } = client(() => new Response(null, { status: 204 }), "t");
    await createAccountService(c).deleteAccount("a b/c");
    expect(calls[0]!.url).toContain("accounts/a%20b%2Fc");
  });
});

describe("tempUserLimitsService", () => {
  it("서버 값을 읽고 누락 필드는 기본값으로 채운다", async () => {
    const { client: c } = client(() => json({ qrHours: 72 }), "t");
    expect(await createTempUserLimitsService(c).get()).toEqual({ qrHours: 72, qrCount: 30 });
  });

  it("빈 패치는 서버에 보내지 않는다(400이 될 요청)", async () => {
    const { client: c, calls } = client(() => json({}), "t");
    await expect(createTempUserLimitsService(c).update({})).rejects.toThrow();
    expect(calls).toHaveLength(0);
  });
});
