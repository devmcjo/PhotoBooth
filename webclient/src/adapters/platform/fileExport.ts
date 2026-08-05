import { logger } from "@adapters/storage/logStore";

/**
 * [기기에 저장] — 브라우저 다운로드 내보내기 (WD3 3계층 중 ③ · 03 §9.3)
 *
 * ⚠️ `URL.createObjectURL`을 만들면 **반드시 revoke**한다. 다만 `click()` 직후 즉시 revoke하면
 *    일부 브라우저가 다운로드를 취소하므로 **다음 태스크로 미룬다**.
 * ⚠️ 파일이 2개면 **버튼도 2개**다(03 §9.3). 한 클릭에 여러 파일을 자동 다운로드하면
 *    브라우저가 차단한다.
 * ⚠️ 어댑터는 예외를 전파하지 않는다(15 §2) — 실패는 `false`다.
 */

export interface FileExportDeps {
  /** 기본 전역 `document`. `null`이면 미지원으로 본다(SSR·Worker). */
  readonly doc?: Pick<Document, "createElement"> | null;
  readonly createObjectUrl?: (blob: Blob) => string;
  readonly revokeObjectUrl?: (url: string) => void;
  /** revoke 지연. 기본 `setTimeout(fn, 0)`. */
  readonly defer?: (fn: () => void) => void;
}

function resolveDoc(deps: FileExportDeps): Pick<Document, "createElement"> | null {
  if (deps.doc !== undefined) return deps.doc;
  return typeof document === "undefined" ? null : document;
}

/**
 * `<a download>` 지원 여부. ⚠️ **타입을 믿지 말고 런타임 감지**한다(15 §4 함정 #2) —
 * TS DOM lib은 항상 있다고 선언하지만 구형 브라우저·WebView에는 없다.
 */
export function canExportFile(deps: FileExportDeps = {}): boolean {
  const doc = resolveDoc(deps);
  if (doc === null) return false;
  try {
    return "download" in doc.createElement("a");
  } catch {
    return false;
  }
}

/** 내보내기. 성공 여부를 돌려주고 **던지지 않는다**. */
export function exportBlob(blob: Blob, fileName: string, deps: FileExportDeps = {}): boolean {
  const doc = resolveDoc(deps);
  if (doc === null || !canExportFile(deps)) {
    logger.warn("기기에 저장 미지원 — 내보내기를 건너뜀", { bytes: blob.size });
    return false;
  }

  const createObjectUrl = deps.createObjectUrl ?? ((b: Blob) => URL.createObjectURL(b));
  const revokeObjectUrl = deps.revokeObjectUrl ?? ((u: string) => URL.revokeObjectURL(u));
  const defer = deps.defer ?? ((fn: () => void) => void setTimeout(fn, 0));

  let objectUrl: string | null = null;
  try {
    objectUrl = createObjectUrl(blob);
    const anchor = doc.createElement("a");
    anchor.href = objectUrl;
    anchor.download = fileName;
    anchor.rel = "noopener";
    anchor.click();
    // DOM에 붙이지 않았으므로 제거할 것이 없다(붙이면 레이아웃이 흔들린다).

    const created = objectUrl;
    defer(() => revokeObjectUrl(created));
    logger.info("기기에 저장", { fileName, bytes: blob.size });
    return true;
  } catch (err) {
    // 실패 경로에서도 URL을 흘리지 않는다.
    if (objectUrl !== null) revokeObjectUrl(objectUrl);
    logger.error("기기에 저장 실패", {
      fileName,
      reason: err instanceof Error ? err.message : String(err),
    });
    return false;
  }
}
