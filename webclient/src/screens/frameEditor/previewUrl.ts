/**
 * 편집기 미리보기 object URL의 **단일 소유자** (설계 §9.4)
 *
 * ⚠️ `frameImageCache`가 아니다. 저쪽은 **저장된** 프레임의 URL 소유자이고 해제 시점이
 *    "프레임 삭제"뿐이다(선택 프레임의 URL이 `Result` 합성까지 살아야 하기 때문). 편집기
 *    미리보기는 세션 자원이라 **편집기가 직접 해제**해야 한다(F-11).
 * ⚠️ `createObjectURL`/`revokeObjectURL`을 주입받는 이유는 node에서 **"만든 수 == 해제한 수"** 를
 *    단위 테스트로 고정하기 위함이다(누수 0 증명).
 */

export interface PreviewUrlHolder {
  /** 이전 URL을 **먼저 해제**하고 새 URL을 만든다. `null`이면 해제만 하고 빈 문자열을 돌려준다. */
  set(blob: Blob | null): string;
  current(): string;
  /** 언마운트에서 반드시 부른다(멱등). */
  dispose(): void;
}

export interface PreviewUrlDeps {
  createObjectURL?: (blob: Blob) => string;
  revokeObjectURL?: (url: string) => void;
}

export function createPreviewUrlHolder(deps: PreviewUrlDeps = {}): PreviewUrlHolder {
  const create =
    deps.createObjectURL ?? ((blob: Blob): string => URL.createObjectURL(blob));
  const revoke = deps.revokeObjectURL ?? ((url: string): void => URL.revokeObjectURL(url));

  let url = "";

  function release(): void {
    if (url.length === 0) return;
    revoke(url);
    url = "";
  }

  return {
    set(blob) {
      // 해제가 **먼저**다 — 새 URL을 만든 뒤 옛 것을 지우면 예외 경로에서 둘 다 살아남는다.
      release();
      if (blob === null) return "";
      url = create(blob);
      return url;
    },
    current() {
      return url;
    },
    dispose() {
      release();
    },
  };
}
