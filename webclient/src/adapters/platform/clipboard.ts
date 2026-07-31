import { logger } from "@adapters/storage/logStore";

/**
 * 클립보드 복사 — 진단 모달의 [복사](개발자 이메일) 전용
 *
 * ⚠️ **타입을 믿지 않고 런타임 감지**한다(15 §4 함정 #2). `navigator.clipboard`는 보안 컨텍스트가
 *    아니거나 권한이 없으면 없다(또는 `writeText`가 거부된다).
 * ⚠️ 어댑터 규약: 예외를 전파하지 않는다. 실패는 `false`이고 화면이 "길게 눌러 복사" 안내를 낸다.
 * ⚠️ `document.execCommand("copy")` 폴백을 만들지 않는다 — deprecated이고, 실패해도
 *    대체 안내(주소 노출 + 길게 누르기)가 이미 있다.
 */

export interface ClipboardLike {
  writeText(text: string): Promise<void>;
}

export interface ClipboardDeps {
  /** 기본 전역 `navigator.clipboard`. `null`이면 미지원으로 본다. */
  readonly clipboard?: ClipboardLike | null;
}

function resolveClipboard(deps: ClipboardDeps): ClipboardLike | null {
  if (deps.clipboard !== undefined) return deps.clipboard;
  if (typeof navigator === "undefined") return null;
  const candidate = navigator.clipboard as ClipboardLike | undefined;
  return typeof candidate?.writeText === "function" ? candidate : null;
}

/** 복사 성공 여부. **던지지 않는다.** */
export async function copyText(text: string, deps: ClipboardDeps = {}): Promise<boolean> {
  const clipboard = resolveClipboard(deps);
  if (clipboard === null) {
    logger.warn("클립보드 미지원 — 복사를 건너뜀");
    return false;
  }
  try {
    await clipboard.writeText(text);
    return true;
  } catch (err) {
    logger.warn("클립보드 복사 실패", {
      reason: err instanceof Error ? err.message : String(err),
    });
    return false;
  }
}
