import { logger } from "@adapters/storage/logStore";

/**
 * Keyboard Lock — WR4 (Chromium best-effort)
 *
 * ⚠️ **Fullscreen API는 ESC 탈출을 막을 수 없다.** Chromium의 `navigator.keyboard.lock`은
 *    전체화면 중 ESC·F11을 가로챌 수 있지만 표준이 아니고 Safari·Firefox에는 없다.
 *    진짜 락다운은 **브라우저 키오스크 모드**가 담당한다(09 §2) — 여기서 실패해도 정상이다.
 */

interface KeyboardLike {
  lock(keyCodes?: string[]): Promise<void>;
  unlock(): void;
}

function api(): KeyboardLike | undefined {
  if (typeof navigator === "undefined") return undefined;
  return (navigator as Navigator & { keyboard?: KeyboardLike }).keyboard;
}

export function isKeyboardLockSupported(): boolean {
  return typeof api()?.lock === "function";
}

/** ESC·F11 가로채기 시도. 미지원·거부는 조용히 false다(사용자에게 알릴 것이 없다). */
export async function lockEscapeKeys(): Promise<boolean> {
  const keyboard = api();
  if (keyboard === undefined || typeof keyboard.lock !== "function") return false;
  try {
    await keyboard.lock(["Escape", "F11"]);
    logger.info("키보드 잠금(Escape·F11) 적용");
    return true;
  } catch (err) {
    logger.info("키보드 잠금 미적용", {
      reason: err instanceof Error ? err.message : String(err),
    });
    return false;
  }
}

export function unlockKeys(): void {
  try {
    api()?.unlock();
  } catch {
    // 무해
  }
}
