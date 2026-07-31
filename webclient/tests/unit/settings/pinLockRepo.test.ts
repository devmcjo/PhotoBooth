import { describe, expect, it } from "vitest";
import { MAX_PIN_FAILS, PIN_LOCK_MS, buildPinLockRecord } from "@domain/auth/pinGatePolicy";
import {
  createPinLockRepo,
  PIN_LOCK_STORAGE_KEY,
  writePinLock,
} from "@adapters/storage/pinLockRepo";
import type { StorageLike } from "@adapters/storage/settingsRepo";

/**
 * 기기 잠금 저장소 — WD16
 *
 * 어댑터 규약(15 §2)의 핵심: **어떤 실패에서도 던지지 않는다.** 프라이빗 모드에서 설정
 * 화면 자체를 못 여는 것이 잠금이 없는 것보다 나쁘다(fail-open).
 */

const NOW = 1_700_000_000_000;

function memoryStorage(initial: Record<string, string> = {}): StorageLike & {
  readonly data: Record<string, string>;
} {
  const data: Record<string, string> = { ...initial };
  return {
    data,
    getItem: (key) => data[key] ?? null,
    setItem: (key, value) => {
      data[key] = value;
    },
    removeItem: (key) => {
      delete data[key];
    },
  };
}

describe("pinLockRepo", () => {
  it("쓰고 읽는 왕복이 성립한다", () => {
    const storage = memoryStorage();
    const repo = createPinLockRepo(storage);

    expect(repo.write(buildPinLockRecord(NOW, MAX_PIN_FAILS))).toBe(true);
    expect(repo.read(NOW)).toEqual({ until: NOW + PIN_LOCK_MS, fails: MAX_PIN_FAILS });
    // 저장 키는 규격이다(PIN-3).
    expect(Object.keys(storage.data)).toEqual([PIN_LOCK_STORAGE_KEY]);
  });

  it("`writePinLock` 헬퍼가 도메인 레코드를 그대로 기록한다", () => {
    const storage = memoryStorage();
    const repo = createPinLockRepo(storage);
    expect(writePinLock(repo, NOW, MAX_PIN_FAILS)).toBe(true);
    expect(repo.read(NOW)?.until).toBe(NOW + PIN_LOCK_MS);
  });

  it("만료된 잠금은 null이다(앱을 다시 열었을 때 자동 해제)", () => {
    const repo = createPinLockRepo(memoryStorage());
    repo.write(buildPinLockRecord(NOW, MAX_PIN_FAILS));
    expect(repo.read(NOW + PIN_LOCK_MS)).toBeNull();
    expect(repo.read(NOW + PIN_LOCK_MS + 1)).toBeNull();
  });

  it("clear()가 잠금을 지운다(성공 시 카운터 초기화)", () => {
    const storage = memoryStorage();
    const repo = createPinLockRepo(storage);
    repo.write(buildPinLockRecord(NOW, MAX_PIN_FAILS));
    repo.clear();
    expect(repo.read(NOW)).toBeNull();
    expect(storage.data[PIN_LOCK_STORAGE_KEY]).toBeUndefined();
  });

  it("손상 JSON은 null이다(던지지 않는다)", () => {
    const repo = createPinLockRepo(memoryStorage({ [PIN_LOCK_STORAGE_KEY]: "{ not json" }));
    expect(() => repo.read(NOW)).not.toThrow();
    expect(repo.read(NOW)).toBeNull();
  });

  it("JSON이지만 형식이 다르면 null이다", () => {
    const repo = createPinLockRepo(memoryStorage({ [PIN_LOCK_STORAGE_KEY]: '"locked"' }));
    expect(repo.read(NOW)).toBeNull();
  });

  it("until이 미래로 크게 어긋난 레코드는 상한으로 clamp된다(A3)", () => {
    const repo = createPinLockRepo(
      memoryStorage({
        [PIN_LOCK_STORAGE_KEY]: JSON.stringify({ until: NOW + 10 * PIN_LOCK_MS, fails: 5 }),
      }),
    );
    expect(repo.read(NOW)?.until).toBe(NOW + PIN_LOCK_MS);
  });

  it("setItem이 던지는 저장소에서 write는 false다(예외 전파 금지 · fail-open)", () => {
    const throwing: StorageLike = {
      getItem: () => null,
      setItem: () => {
        throw new DOMException("QuotaExceededError");
      },
      removeItem: () => undefined,
    };
    const repo = createPinLockRepo(throwing);
    expect(() => repo.write(buildPinLockRecord(NOW, 5))).not.toThrow();
    expect(repo.write(buildPinLockRecord(NOW, 5))).toBe(false);
  });

  it("getItem이 던져도 read는 null이다", () => {
    const throwing: StorageLike = {
      getItem: () => {
        throw new Error("storage blocked");
      },
      setItem: () => undefined,
      removeItem: () => undefined,
    };
    const repo = createPinLockRepo(throwing);
    expect(() => repo.read(NOW)).not.toThrow();
    expect(repo.read(NOW)).toBeNull();
  });

  it("removeItem이 던져도 clear는 조용하다", () => {
    const throwing: StorageLike = {
      getItem: () => null,
      setItem: () => undefined,
      removeItem: () => {
        throw new Error("storage blocked");
      },
    };
    expect(() => createPinLockRepo(throwing).clear()).not.toThrow();
  });

  it("저장소가 없는 환경(SSR·차단)에서는 읽기 null · 쓰기 false다", () => {
    const repo = createPinLockRepo(null);
    expect(repo.read(NOW)).toBeNull();
    expect(repo.write(buildPinLockRecord(NOW, 5))).toBe(false);
    expect(() => repo.clear()).not.toThrow();
  });
});
