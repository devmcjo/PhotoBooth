import { describe, expect, it, vi } from "vitest";
import {
  applyBrandingToDocument,
  BRANDING_TIMEOUT_MS,
  DEFAULT_BRANDING,
  loadBranding,
  parseBranding,
} from "@adapters/platform/branding";
import {
  describePersistState,
  freeRatio,
  isStorageLow,
  LOW_STORAGE_THRESHOLD,
  requestPersistentStorage,
  type StorageManagerLike,
} from "@adapters/platform/persistStorage";

function jsonResponse(body: unknown, status = 200): Response {
  return {
    ok: status >= 200 && status < 300,
    status,
    json: async () => body,
  } as unknown as Response;
}

describe("branding — 파싱·독립 폴백 (05 §8.1)", () => {
  it("두 값을 읽는다", () => {
    expect(parseBranding({ AppName: "우리부스", Subtitle: "즐거운 촬영" })).toEqual({
      appName: "우리부스",
      subtitle: "즐거운 촬영",
    });
  });

  it("두 값이 독립적으로 폴백한다", () => {
    expect(parseBranding({ AppName: "우리부스" })).toEqual({
      appName: "우리부스",
      subtitle: DEFAULT_BRANDING.subtitle,
    });
    expect(parseBranding({ Subtitle: "부제만" })).toEqual({
      appName: DEFAULT_BRANDING.appName,
      subtitle: "부제만",
    });
  });

  it("빈 문자열·공백·타입 오류는 기본값으로 본다", () => {
    expect(parseBranding({ AppName: "", Subtitle: "   " })).toEqual(DEFAULT_BRANDING);
    expect(parseBranding({ AppName: 123, Subtitle: null })).toEqual(DEFAULT_BRANDING);
    expect(parseBranding(null)).toEqual(DEFAULT_BRANDING);
    expect(parseBranding("nope")).toEqual(DEFAULT_BRANDING);
  });

  it("한글 이름을 그대로 쓴다(UTF-8)", () => {
    expect(parseBranding({ AppName: "엠씨포토 부스" }).appName).toBe("엠씨포토 부스");
  });
});

describe("branding — 로드 실패 처리", () => {
  it("정상 응답을 채택한다", async () => {
    const result = await loadBranding(async () => jsonResponse({ AppName: "부스A" }));
    expect(result.branding.appName).toBe("부스A");
    expect(result.usedFallback).toBe(false);
  });

  it("404면 기본값으로 폴백한다", async () => {
    const result = await loadBranding(async () => jsonResponse(null, 404));
    expect(result.branding).toEqual(DEFAULT_BRANDING);
    expect(result.usedFallback).toBe(true);
    expect(result.reason).toContain("404");
  });

  it("네트워크 오류·JSON 손상에서 크래시하지 않는다", async () => {
    const netFail = await loadBranding(async () => {
      throw new TypeError("Failed to fetch");
    });
    expect(netFail.branding).toEqual(DEFAULT_BRANDING);
    expect(netFail.reason).toContain("Failed to fetch");

    const badJson = await loadBranding(
      async () =>
        ({
          ok: true,
          status: 200,
          json: async () => {
            throw new SyntaxError("Unexpected token");
          },
        }) as unknown as Response,
    );
    expect(badJson.branding).toEqual(DEFAULT_BRANDING);
  });

  it("800ms를 넘으면 타임아웃 폴백이다", async () => {
    vi.useFakeTimers();
    try {
      const promise = loadBranding(
        (_input, init) =>
          new Promise<Response>((_resolve, reject) => {
            init?.signal?.addEventListener("abort", () =>
              reject(new DOMException("aborted", "AbortError")),
            );
          }),
        BRANDING_TIMEOUT_MS,
      );
      await vi.advanceTimersByTimeAsync(BRANDING_TIMEOUT_MS + 1);
      const result = await promise;
      expect(result.usedFallback).toBe(true);
      expect(result.reason).toContain("타임아웃");
    } finally {
      vi.useRealTimers();
    }
  });

  it("문서 타이틀에 적용한다", () => {
    const doc = { title: "" } as Document;
    applyBrandingToDocument({ appName: "부스B", subtitle: "s" }, doc);
    expect(doc.title).toBe("부스B");
  });
});

describe("persistStorage — 영속 요청 (05 §5.5)", () => {
  it("이미 승인돼 있으면 다시 요청하지 않는다(프롬프트 반복 방지)", async () => {
    const persist = vi.fn(async () => true);
    const manager: StorageManagerLike = {
      persist,
      persisted: async () => true,
      estimate: async () => ({ usage: 100, quota: 1000 }),
    };
    const status = await requestPersistentStorage(manager);
    expect(status.persistState).toBe("granted");
    expect(persist).not.toHaveBeenCalled();
  });

  it("미승인이면 요청하고 결과를 반영한다", async () => {
    const granted = await requestPersistentStorage({
      persist: async () => true,
      persisted: async () => false,
    });
    expect(granted.persistState).toBe("granted");

    const denied = await requestPersistentStorage({
      persist: async () => false,
      persisted: async () => false,
    });
    expect(denied.persistState).toBe("denied");
  });

  it("API가 없으면 unsupported다(Safari)", async () => {
    expect((await requestPersistentStorage(undefined)).persistState).toBe("unsupported");
    expect((await requestPersistentStorage({})).persistState).toBe("unsupported");
  });

  it("예외를 던져도 부팅을 막지 않는다", async () => {
    const status = await requestPersistentStorage({
      persist: async () => {
        throw new Error("blocked");
      },
      persisted: async () => {
        throw new Error("blocked");
      },
    });
    expect(status.persistState).toBe("denied");
  });

  it("estimate 실패는 무시하고 null로 둔다", async () => {
    const status = await requestPersistentStorage({
      persist: async () => true,
      persisted: async () => false,
      estimate: async () => {
        throw new Error("nope");
      },
    });
    expect(status.usage).toBeNull();
    expect(status.quota).toBeNull();
  });

  it("사용량·할당량을 읽는다", async () => {
    const status = await requestPersistentStorage({
      persist: async () => true,
      persisted: async () => true,
      estimate: async () => ({ usage: 250, quota: 1000 }),
    });
    expect(status.usage).toBe(250);
    expect(status.quota).toBe(1000);
  });
});

describe("persistStorage — 여유 판정", () => {
  it("여유 비율을 계산한다", () => {
    expect(freeRatio({ persistState: "granted", usage: 250, quota: 1000 })).toBeCloseTo(0.75, 10);
    expect(freeRatio({ persistState: "granted", usage: null, quota: 1000 })).toBeNull();
    expect(freeRatio({ persistState: "granted", usage: 1, quota: 0 })).toBeNull();
  });

  it("10% 미만이면 경고 대상이다", () => {
    expect(isStorageLow({ persistState: "granted", usage: 950, quota: 1000 })).toBe(true);
    expect(isStorageLow({ persistState: "granted", usage: 900, quota: 1000 })).toBe(false);
    expect(LOW_STORAGE_THRESHOLD).toBe(0.1);
  });

  it("알 수 없으면 경고하지 않는다(거짓 경보 금지)", () => {
    expect(isStorageLow({ persistState: "unsupported", usage: null, quota: null })).toBe(false);
  });

  it("진단 문구가 미승인 위험을 정직하게 밝힌다", () => {
    expect(describePersistState("granted")).toBe("영속 승인됨");
    expect(describePersistState("denied")).toContain("삭제될 수 있음");
    expect(describePersistState("unsupported")).toContain("삭제될 수 있음");
  });
});
