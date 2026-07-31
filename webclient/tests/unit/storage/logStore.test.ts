import { afterEach, describe, expect, it, vi } from "vitest";
import {
  formatLogLine,
  formatLogText,
  isForbiddenCtxKey,
  LOG_FLUSH_COUNT,
  LOG_MAX_AGE_MS,
  LOG_MAX_ENTRIES,
  MASK,
  maskCtx,
  maskMessage,
  pruneEntries,
  sanitizeEntry,
  shouldFlush,
  type LogEntry,
} from "@adapters/storage/logPolicy";
import {
  attachLogStore,
  createLogStore,
  createMemoryLogSink,
  detachLogStore,
  logger,
} from "@adapters/storage/logStore";

const T0 = Date.UTC(2026, 6, 30, 12, 0, 0);

function entry(overrides: Partial<LogEntry> = {}): LogEntry {
  return { ts: T0, level: "info", msg: "메시지", ...overrides };
}

afterEach(() => {
  detachLogStore();
});

describe("logPolicy — 금지 항목 마스킹 (analysis/41 §8)", () => {
  it("금지 키를 이름 변형과 무관하게 잡는다", () => {
    for (const key of [
      "token",
      "Token",
      "accessToken",
      "access_token",
      "ID-TOKEN",
      "jwt",
      "apiKey",
      "api_key",
      "codeVerifier",
      "state",
      "nonce",
      "pin",
      "newPin",
      "password",
      "clientSecret",
      "putUrl",
    ]) {
      expect(isForbiddenCtxKey(key), key).toBe(true);
    }
  });

  it("일반 키는 마스킹하지 않는다", () => {
    for (const key of ["sessionId", "cutCount", "screen", "status", "durationMs", "frameId"]) {
      expect(isForbiddenCtxKey(key), key).toBe(false);
    }
  });

  it("컨텍스트의 금지 키 값을 가린다", () => {
    const masked = maskCtx({ sessionId: "s1", token: "eyJabc.def.ghi", pin: "1234" });
    expect(masked.sessionId).toBe("s1");
    expect(masked.token).toBe(MASK);
    expect(masked.pin).toBe(MASK);
  });

  it("중첩 객체·배열도 재귀 마스킹한다", () => {
    const masked = maskCtx({
      req: { headers: { authorization: "Bearer abc" }, path: "/uploads" },
      items: [{ pin: "1111" }, "plain"],
    });
    expect((masked.req as Record<string, unknown>).path).toBe("/uploads");
    expect(
      ((masked.req as Record<string, unknown>).headers as Record<string, unknown>).authorization,
    ).toBe(MASK);
    expect((masked.items as Record<string, unknown>[])[0]!.pin).toBe(MASK);
  });

  it("순환 참조에서 죽지 않는다(로깅이 앱을 죽이면 안 된다)", () => {
    const cyclic: Record<string, unknown> = { name: "a" };
    cyclic.self = cyclic;
    expect(() => maskCtx(cyclic)).not.toThrow();
    expect(maskCtx(cyclic).self).toBe("[circular]");
  });

  it("메시지 본문의 JWT·Bearer·서명 URL 토큰을 가린다", () => {
    expect(maskMessage("Authorization: Bearer abc.def")).toContain(`Bearer ${MASK}`);
    expect(maskMessage("token=eyJhbGciOiJIUzI1NiIs.payload.sig 실패")).not.toContain("eyJhbGci");
    const signed = maskMessage(
      "PUT https://storage.googleapis.com/b/o?X-Goog-Signature=deadbeefcafe&x=1",
    );
    expect(signed).not.toContain("deadbeefcafe");
    expect(signed).toContain("x=1");
  });

  it("sanitizeEntry가 메시지·컨텍스트 양쪽을 정리한다", () => {
    const clean = sanitizeEntry(
      entry({ msg: "Bearer secret-token 로 요청", ctx: { apiKey: "k", screen: "Qr" } }),
    );
    expect(clean.msg).toContain(MASK);
    expect(clean.ctx?.apiKey).toBe(MASK);
    expect(clean.ctx?.screen).toBe("Qr");
  });

  it("컨텍스트가 없으면 ctx 키를 만들지 않는다", () => {
    expect("ctx" in sanitizeEntry(entry())).toBe(false);
  });
});

describe("logPolicy — 링버퍼 한도 (14일 / 5,000건)", () => {
  it("규격 상수를 갖는다", () => {
    expect(LOG_MAX_AGE_MS).toBe(14 * 24 * 60 * 60 * 1000);
    expect(LOG_MAX_ENTRIES).toBe(5000);
  });

  it("14일보다 오래된 항목을 버린다", () => {
    const old = entry({ ts: T0 - LOG_MAX_AGE_MS - 1, msg: "오래됨" });
    const fresh = entry({ ts: T0 - 1000, msg: "최근" });
    expect(pruneEntries([old, fresh], T0).map((e) => e.msg)).toEqual(["최근"]);
  });

  it("경계(정확히 14일)는 남긴다", () => {
    const boundary = entry({ ts: T0 - LOG_MAX_AGE_MS });
    expect(pruneEntries([boundary], T0)).toHaveLength(1);
  });

  it("건수 초과 시 오래된 것부터 버린다", () => {
    const entries = Array.from({ length: 10 }, (_, i) => entry({ ts: T0 + i, msg: `m${i}` }));
    const kept = pruneEntries(entries, T0 + 100, { maxEntries: 3 });
    expect(kept.map((e) => e.msg)).toEqual(["m7", "m8", "m9"]);
  });

  it("두 한도가 함께 적용된다", () => {
    const entries = [
      entry({ ts: T0 - LOG_MAX_AGE_MS - 1, msg: "old" }),
      entry({ ts: T0 - 3, msg: "a" }),
      entry({ ts: T0 - 2, msg: "b" }),
      entry({ ts: T0 - 1, msg: "c" }),
    ];
    expect(pruneEntries(entries, T0, { maxEntries: 2 }).map((e) => e.msg)).toEqual(["b", "c"]);
  });
});

describe("logPolicy — flush 조건·포맷", () => {
  it("20건 또는 1초에 flush한다", () => {
    expect(shouldFlush(0, 99999)).toBe(false); // 대기 없음
    expect(shouldFlush(1, 0)).toBe(false);
    expect(shouldFlush(LOG_FLUSH_COUNT, 0)).toBe(true);
    expect(shouldFlush(1, 1000)).toBe(true);
  });

  it("내보내기 한 줄에 시각·레벨·메시지·컨텍스트가 들어간다", () => {
    const line = formatLogLine(entry({ level: "warn", msg: "경고", ctx: { screen: "Home" } }));
    expect(line).toContain("2026-07-30T12:00:00.000Z");
    expect(line).toContain("[WARN]");
    expect(line).toContain("경고");
    expect(line).toContain('{"screen":"Home"}');
  });

  it("빈 목록은 빈 문자열이다(빈 파일에 개행만 남지 않게)", () => {
    expect(formatLogText([])).toBe("");
    expect(formatLogText([entry()]).endsWith("\n")).toBe(true);
  });
});

describe("logStore — 배치·조회·내보내기", () => {
  function make() {
    const sink = createMemoryLogSink();
    let now = T0;
    const store = createLogStore({ sink, now: () => now });
    return { sink, store, advance: (ms: number) => (now += ms) };
  }

  it("20건이 차면 즉시 flush한다", async () => {
    const { sink, store } = make();
    for (let i = 0; i < LOG_FLUSH_COUNT; i++) store.log("info", `m${i}`);
    await store.flush();
    expect(await sink.readAll()).toHaveLength(LOG_FLUSH_COUNT);
  });

  it("1초 타이머로도 flush한다", async () => {
    vi.useFakeTimers();
    try {
      const { sink, store } = make();
      store.log("info", "하나");
      expect(await sink.readAll()).toHaveLength(0);
      await vi.advanceTimersByTimeAsync(1001);
      expect(await sink.readAll()).toHaveLength(1);
    } finally {
      vi.useRealTimers();
    }
  });

  it("recent는 최신순이다(진단 모달 표시용)", async () => {
    const { store, advance } = make();
    store.log("info", "첫째");
    advance(10);
    store.log("warn", "둘째");
    const recent = await store.recent(10);
    expect(recent.map((e) => e.msg)).toEqual(["둘째", "첫째"]);
  });

  it("recent(limit)는 최신 N건만 준다", async () => {
    const { store, advance } = make();
    for (let i = 0; i < 5; i++) {
      store.log("info", `m${i}`);
      advance(1);
    }
    expect((await store.recent(2)).map((e) => e.msg)).toEqual(["m4", "m3"]);
  });

  it("내보내기 텍스트에 마스킹된 값만 들어간다", async () => {
    const { store } = make();
    store.log("error", "업로드 실패", { token: "eyJsecret", sessionId: "s1" });
    const text = await store.exportText();
    expect(text).not.toContain("eyJsecret");
    expect(text).toContain(MASK);
    expect(text).toContain("s1");
  });

  it("stats가 건수·기간을 보고한다", async () => {
    const { store, advance } = make();
    store.log("info", "a");
    advance(5000);
    store.log("info", "b");
    const stats = await store.stats();
    expect(stats.count).toBe(2);
    expect(stats.oldestTs).toBe(T0);
    expect(stats.newestTs).toBe(T0 + 5000);
  });

  it("빈 스토어의 stats는 null 시각이다", async () => {
    const { store } = make();
    expect(await store.stats()).toEqual({ count: 0, oldestTs: null, newestTs: null });
  });

  it("clear가 대기분까지 버린다", async () => {
    const { sink, store } = make();
    store.log("info", "버려질 것");
    await store.clear();
    expect(await sink.readAll()).toHaveLength(0);
  });

  it("싱크가 던져도 로깅이 앱을 죽이지 않는다", async () => {
    const store = createLogStore({
      sink: {
        persist: () => Promise.reject(new Error("IDB 실패")),
        readAll: async () => [],
        prune: async () => 0,
        clear: async () => undefined,
      },
      now: () => T0,
    });
    store.log("info", "무언가");
    await expect(store.flush()).resolves.toBeUndefined();
  });

  it("메모리 싱크는 상한을 넘으면 오래된 것부터 버린다", async () => {
    const sink = createMemoryLogSink(3);
    await sink.persist([entry({ msg: "a" }), entry({ msg: "b" })]);
    await sink.persist([entry({ msg: "c" }), entry({ msg: "d" })]);
    expect((await sink.readAll()).map((e) => e.msg)).toEqual(["b", "c", "d"]);
  });

  it("메모리 싱크 prune이 버린 개수를 돌려준다", async () => {
    const sink = createMemoryLogSink();
    await sink.persist([entry({ ts: T0 - LOG_MAX_AGE_MS - 1 }), entry({ ts: T0 })]);
    expect(await sink.prune(T0)).toBe(1);
  });
});

describe("logger 파사드 — 부트스트랩 이전 로그 보존", () => {
  it("스토어 연결 전 로그가 버퍼링되어 연결 후 흘러 들어간다", async () => {
    logger.warn("env 경고: 게이트 키 없음");
    logger.info("아직 스토어 없음");

    const sink = createMemoryLogSink();
    const store = createLogStore({ sink, now: () => T0 });
    attachLogStore(store);
    await store.flush();

    const msgs = (await sink.readAll()).map((e) => e.msg);
    expect(msgs).toEqual(["env 경고: 게이트 키 없음", "아직 스토어 없음"]);
  });

  it("연결 후에는 곧바로 스토어로 간다", async () => {
    const sink = createMemoryLogSink();
    const store = createLogStore({ sink, now: () => T0 });
    attachLogStore(store);

    logger.error("연결 후 오류");
    await store.flush();
    expect((await sink.readAll()).map((e) => e.level)).toEqual(["error"]);
  });

  it("파사드도 마스킹을 지난다", async () => {
    const sink = createMemoryLogSink();
    const store = createLogStore({ sink, now: () => T0 });
    attachLogStore(store);

    logger.info("로그인", { token: "eyJsecret" });
    await store.flush();
    expect((await sink.readAll())[0]!.ctx?.token).toBe(MASK);
  });
});
