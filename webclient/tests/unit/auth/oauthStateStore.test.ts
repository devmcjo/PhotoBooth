import { afterEach, beforeEach, describe, expect, it } from "vitest";
import {
  clearPendingOauth,
  OAUTH_PENDING_KEY,
  savePendingOauth,
  sessionStorageOrNull,
  takePendingOauth,
  type StorageLike,
} from "@adapters/auth/oauthStateStore";
import type { OauthPendingState } from "@domain/auth/oauthCallbackPolicy";
import {
  attachLogStore,
  createLogStore,
  createMemoryLogSink,
  detachLogStore,
} from "@adapters/storage/logStore";

/**
 * 로그인 임시 상태 저장소 — 07 §2.4
 *
 * 핵심 계약 2개: **take는 읽고 즉시 지운다**(2회째는 반드시 null) · **어떤 실패도 throw하지 않는다**.
 */

/** 실패를 주입할 수 있는 가짜 sessionStorage(`settingsRepo.test.ts`와 같은 형태). */
class FakeStorage implements StorageLike {
  readonly map = new Map<string, string>();
  failOnSet = false;
  failOnGet = false;
  failOnRemove = false;

  getItem(key: string): string | null {
    if (this.failOnGet) throw new DOMException("blocked", "SecurityError");
    return this.map.get(key) ?? null;
  }
  setItem(key: string, value: string): void {
    if (this.failOnSet) throw new DOMException("quota", "QuotaExceededError");
    this.map.set(key, value);
  }
  removeItem(key: string): void {
    if (this.failOnRemove) throw new DOMException("blocked", "SecurityError");
    this.map.delete(key);
  }
}

const PENDING: OauthPendingState = {
  codeVerifier: "v".repeat(43),
  state: "state-abc",
  nonce: "nonce-abc",
  returnTo: "Settings",
  startedAt: 1_700_000_000_000,
};

let store: FakeStorage;

beforeEach(() => {
  store = new FakeStorage();
  attachLogStore(createLogStore({ sink: createMemoryLogSink(), now: () => 0 }));
});

afterEach(() => {
  detachLogStore();
});

describe("save → take 왕복", () => {
  it("저장한 값을 그대로 되살린다", () => {
    expect(savePendingOauth(PENDING, store)).toBe(true);
    expect(takePendingOauth(store)).toEqual(PENDING);
  });

  it("키가 고정이라 매번 덮어쓴다(이탈한 손님의 pending이 쌓이지 않는다)", () => {
    savePendingOauth(PENDING, store);
    savePendingOauth({ ...PENDING, state: "state-2" }, store);
    expect(store.map.size).toBe(1);
    expect(takePendingOauth(store)?.state).toBe("state-2");
  });

  it("★ take 2회째는 null이다(원자적 소비 — 같은 code로 재교환 불가)", () => {
    savePendingOauth(PENDING, store);
    expect(takePendingOauth(store)).not.toBeNull();
    expect(takePendingOauth(store)).toBeNull();
    expect(store.map.has(OAUTH_PENDING_KEY)).toBe(false);
  });

  it("저장된 적이 없으면 null이다", () => {
    expect(takePendingOauth(store)).toBeNull();
  });
});

describe("실패 흡수 — 어떤 함수도 throw하지 않는다", () => {
  it("setItem이 던지면 false다", () => {
    store.failOnSet = true;
    expect(() => savePendingOauth(PENDING, store)).not.toThrow();
    expect(savePendingOauth(PENDING, store)).toBe(false);
  });

  it("getItem이 던지면 null이다", () => {
    savePendingOauth(PENDING, store);
    store.failOnGet = true;
    expect(takePendingOauth(store)).toBeNull();
  });

  it("removeItem이 던지면 null이다(값을 지울 수 없는 저장소로 교환하지 않는다)", () => {
    savePendingOauth(PENDING, store);
    store.failOnRemove = true;
    expect(takePendingOauth(store)).toBeNull();
  });

  it("clear는 실패해도 조용하다", () => {
    store.failOnRemove = true;
    expect(() => clearPendingOauth(store)).not.toThrow();
  });

  it("저장소 자체가 없으면 save=false · take=null · clear=no-op이다", () => {
    expect(savePendingOauth(PENDING, null)).toBe(false);
    expect(takePendingOauth(null)).toBeNull();
    expect(() => clearPendingOauth(null)).not.toThrow();
  });
});

describe("손상된 값 방어", () => {
  it("JSON이 깨져 있으면 null이고 **값은 사라진다**(삭제가 파싱보다 먼저)", () => {
    store.map.set(OAUTH_PENDING_KEY, "{not json");
    expect(takePendingOauth(store)).toBeNull();
    expect(store.map.has(OAUTH_PENDING_KEY)).toBe(false);
  });

  it("필드가 빠진 객체도 null이고 값은 사라진다", () => {
    store.map.set(OAUTH_PENDING_KEY, JSON.stringify({ state: "s" }));
    expect(takePendingOauth(store)).toBeNull();
    expect(store.map.has(OAUTH_PENDING_KEY)).toBe(false);
  });

  it("JSON 배열·문자열도 null이다", () => {
    for (const raw of ["[]", '"text"', "123", "null"]) {
      store.map.set(OAUTH_PENDING_KEY, raw);
      expect(takePendingOauth(store), raw).toBeNull();
    }
  });
});

describe("clearPendingOauth", () => {
  it("저장된 값을 지운다", () => {
    savePendingOauth(PENDING, store);
    clearPendingOauth(store);
    expect(takePendingOauth(store)).toBeNull();
  });
});

describe("기본 저장소 해석", () => {
  it("node 환경에서도 던지지 않는다(없으면 null)", () => {
    expect(() => sessionStorageOrNull()).not.toThrow();
  });

  it("키 이름이 버전을 포함한다(형식 변경 시 낡은 값을 읽지 않는다)", () => {
    expect(OAUTH_PENDING_KEY).toBe("mcphoto.oauth.pending.v1");
  });
});
