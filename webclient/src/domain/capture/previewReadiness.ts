/**
 * 카메라 프리뷰 "안정적 실사용 가능" 판정 — Windows `Capture/PreviewReadiness.cs` 이식 (analysis/14 §2.3)
 *
 * 첫 프레임 1회로는 부족하다: **가공 완료 프레임 누적 N개 AND 최소 경과 시간 AND fps>0** 세 조건을
 * 모두 충족해야 Ready다. 시간·프레임은 어댑터가 주입하고 판정 규칙만 여기 둔다.
 *
 * ⚠️ `frameCount`는 "이번 창에서 누적된" 프레임 수다. 카메라를 다시 열면 새 상태를 만든다.
 */

export interface PreviewReadinessState {
  readonly requiredFrames: number;
  readonly minElapsedMs: number;
  readonly frameCount: number;
  readonly isReady: boolean;
}

export const DEFAULT_REQUIRED_FRAMES = 8;
export const DEFAULT_MIN_ELAPSED_MS = 500;

export function createPreviewReadiness(
  requiredFrames: number = DEFAULT_REQUIRED_FRAMES,
  minElapsedMs: number = DEFAULT_MIN_ELAPSED_MS,
): PreviewReadinessState {
  return {
    requiredFrames: Math.max(1, requiredFrames),
    minElapsedMs: Math.max(0, minElapsedMs),
    frameCount: 0,
    isReady: false,
  };
}

export interface PreviewFrameResult {
  readonly state: PreviewReadinessState;
  /** 이 프레임으로 Ready에 도달했는가(전이 시 1회만 true). */
  readonly becameReady: boolean;
}

/**
 * 가공 완료 프레임 1개 수신 반영.
 * @param elapsedMs 대기 시작 이후 누적 경과(실경과 — `performance.now()` 차이)
 * @param currentFps 현재 fps(0이면 스트림 미흐름)
 */
export function onFrame(
  state: PreviewReadinessState,
  elapsedMs: number,
  currentFps: number,
): PreviewFrameResult {
  if (state.isReady) return { state, becameReady: false }; // 이미 준비됨(중복 방지)

  const frameCount = state.frameCount + 1;
  const enoughFrames = frameCount >= state.requiredFrames;
  const enoughElapsed = elapsedMs >= state.minElapsedMs;
  const streaming = currentFps > 0;
  const isReady = enoughFrames && enoughElapsed && streaming;

  return {
    state: { ...state, frameCount, isReady },
    becameReady: isReady,
  };
}
