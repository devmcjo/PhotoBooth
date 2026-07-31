import {
  buildPinLockRecord,
  parsePinLockRecord,
  type PinLockRecord,
} from "@domain/auth/pinGatePolicy";
import type { StorageLike } from "./settingsRepo";
import { logger } from "./logStore";

/**
 * 기기 단위 PIN 잠금 저장소 — WD16 · 07 §6.3 · 12 C9
 *
 * 연속 5회 불일치 시 **5분간 PIN 입력을 차단**한다. 앱을 새로 열어도 유지되어야 하므로
 * `localStorage`를 쓴다.
 *
 * ⚠️ **M2(JWT 메모리 전용)와 충돌하지 않는다** — 저장하는 값은 `{ until, fails }`,
 *    즉 epoch ms와 정수 하나이고 자격증명이 아니다. 이 파일을 `authInvariants.test.ts`의
 *    `AUTH_FILES`에 넣지 않는 이유이며, 대신 정적 불변식 **PIN-3**이 키 문자열의 단일 소유를 고정한다.
 * ⚠️ 어댑터 규약: **예외를 전파하지 않는다**(15 §2). 읽기 실패는 `null`, 쓰기 실패는 `false`다.
 * ⚠️ 쓰기 실패 시 **잠금 없이 진행**한다(fail-open). 잠금은 강화 장치이고 세션 내 5회 제한은
 *    여전히 살아 있다 — 프라이빗 모드에서 설정 자체를 못 여는 것이 더 나쁘다.
 * ⚠️ 로그에 PIN을 남기지 않는다. 컨텍스트는 `lockUntil`·`failCount`만 쓴다.
 */

/** ⚠️ 이 문자열은 **이 파일에만** 있어야 한다(정적 불변식 PIN-3). */
export const PIN_LOCK_STORAGE_KEY = "mcphoto.pinLock.v1";

export interface PinLockRepo {
  /** 유효한 잠금이면 레코드, 없거나 만료·손상·읽기 실패면 `null`. */
  read(nowMs: number): PinLockRecord | null;
  /** 저장 실패는 `false`(잠금 없이 진행). */
  write(record: PinLockRecord): boolean;
  clear(): void;
}

/** 브라우저 `localStorage`. 접근 자체가 던지는 환경(차단된 서드파티 컨텍스트)도 있다. */
function browserStorage(): StorageLike | null {
  try {
    return typeof localStorage === "undefined" ? null : localStorage;
  } catch {
    return null;
  }
}

export function createPinLockRepo(storage: StorageLike | null = browserStorage()): PinLockRepo {
  return {
    read(nowMs) {
      if (storage === null) return null;
      let text: string | null;
      try {
        text = storage.getItem(PIN_LOCK_STORAGE_KEY);
      } catch {
        return null;
      }
      if (text === null) return null;

      let parsed: unknown;
      try {
        parsed = JSON.parse(text);
      } catch {
        return null; // 손상 → 잠금 없음
      }
      return parsePinLockRecord(parsed, nowMs);
    },

    write(record) {
      if (storage === null) return false;
      try {
        storage.setItem(PIN_LOCK_STORAGE_KEY, JSON.stringify(record));
        logger.warn("PIN 연속 실패로 기기 잠금", {
          lockUntil: record.until,
          failCount: record.fails,
        });
        return true;
      } catch {
        // QuotaExceededError·프라이빗 모드 등. 잠금 없이 계속한다(fail-open).
        return false;
      }
    },

    clear() {
      if (storage === null) return;
      try {
        storage.removeItem(PIN_LOCK_STORAGE_KEY);
      } catch {
        // 지우지 못해도 만료 시각이 지나면 자연히 풀린다.
      }
    },
  };
}

/** 5회 소진 시각으로 레코드를 만들어 기록한다(호출부가 도메인 함수를 다시 조립하지 않게). */
export function writePinLock(repo: PinLockRepo, nowMs: number, fails: number): boolean {
  return repo.write(buildPinLockRecord(nowMs, fails));
}

let singleton: PinLockRepo | null = null;

export function getPinLockRepo(): PinLockRepo {
  singleton ??= createPinLockRepo();
  return singleton;
}

export function setPinLockRepoForTests(repo: PinLockRepo | null): void {
  singleton = repo;
}
