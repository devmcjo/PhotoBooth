import type { FrameTemplate } from "@domain/frames/types";

/**
 * `FrameSelect → FrameEditor` 인계 채널 (설계 §13)
 *
 * 셸 스토어에 넣지 않는 이유: 화면 상태가 아니라 **다음 진입의 인자**이고, zustand 구독을 붙이면
 * 편집기 밖의 컴포넌트가 프레임 객체 변경에 리렌더된다. 모듈 지역 변수 하나로 충분하다.
 */

export type FrameEditorIntent =
  | { readonly kind: "new" }
  | { readonly kind: "edit"; readonly frame: FrameTemplate };

const NEW_INTENT: FrameEditorIntent = { kind: "new" };

let pending: FrameEditorIntent = NEW_INTENT;

/** `go("FrameEditor")` **직전에** 부른다. */
export function setFrameEditorIntent(intent: FrameEditorIntent): void {
  pending = intent;
}

/**
 * ⚠️ **비파괴 읽기**다. 소비형(consume)으로 만들면 `<StrictMode>`의 이중 마운트에서 2회차가
 *    `new`로 떨어져 편집 세션이 조용히 신규 생성으로 바뀐다(Step 12·13에서 같은 함정을 밟았다).
 */
export function readFrameEditorIntent(): FrameEditorIntent {
  return pending;
}

/** 편집기를 떠날 때(저장 성공·취소·홈 복귀) 부른다. 다음 진입의 기본값은 `new`다. */
export function clearFrameEditorIntent(): void {
  pending = NEW_INTENT;
}
