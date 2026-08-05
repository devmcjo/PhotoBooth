import { logger } from "@adapters/storage/logStore";

/**
 * Screen Wake Lock — WR2
 *
 * 촬영 중 화면이 꺼지면 세션이 중단된다. Wake Lock을 요청하되 **미지원·거부는 정상 경로**다 —
 * 진짜 방어선은 **OS 전원 설정**이며 운영 필수 조건으로 문서화한다(09 §4).
 *
 * 어댑터 규약: 실패를 예외로 던지지 않는다(01 §2.1).
 */

interface WakeLockSentinelLike {
  released: boolean;
  release(): Promise<void>;
  addEventListener(type: "release", listener: () => void): void;
}

interface WakeLockLike {
  request(type: "screen"): Promise<WakeLockSentinelLike>;
}

let sentinel: WakeLockSentinelLike | null = null;

function api(): WakeLockLike | undefined {
  if (typeof navigator === "undefined") return undefined;
  return (navigator as Navigator & { wakeLock?: WakeLockLike }).wakeLock;
}

export function isWakeLockSupported(): boolean {
  return api() !== undefined;
}

/**
 * Wake Lock 요청. 이미 잡혀 있으면 아무 것도 하지 않는다.
 * @returns 잠금을 확보했는가
 */
export async function requestWakeLock(): Promise<boolean> {
  const wakeLock = api();
  if (wakeLock === undefined) return false;
  if (sentinel !== null && !sentinel.released) return true;

  try {
    sentinel = await wakeLock.request("screen");
    // 브라우저가 임의로 해제할 수 있다(탭 전환 등) — 상태를 정직하게 반영한다.
    sentinel.addEventListener("release", () => {
      logger.info("Wake Lock 해제됨(브라우저)");
    });
    return true;
  } catch (err) {
    logger.warn("Wake Lock 요청 실패", {
      reason: err instanceof Error ? err.message : String(err),
    });
    sentinel = null;
    return false;
  }
}

export async function releaseWakeLock(): Promise<void> {
  if (sentinel === null) return;
  try {
    if (!sentinel.released) await sentinel.release();
  } catch {
    // 해제 실패는 무해하다(페이지 종료 시 자동 해제된다).
  }
  sentinel = null;
}

export function isWakeLockHeld(): boolean {
  return sentinel !== null && !sentinel.released;
}
