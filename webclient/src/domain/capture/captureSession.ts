import type { FrameTemplate } from "../frames/types";
import { isAutoCutCount, resolveCutCount } from "../settings/cutCountPolicy";

/**
 * 촬영 세션 상태 — Windows `Capture/CaptureSession.cs` 이식 (analysis/13 §4.5)
 *
 * Windows는 가변 클래스지만 웹은 **불변 값 + 순수 함수**로 이식한다(Zustand 스토어에 그대로 담는다).
 * 컷의 실제 표현(OPFS 파일명·썸네일)은 어댑터 관심사이므로 `TCut`으로 열어 둔다 —
 * 도메인은 **개수와 선택 순서**만 다룬다.
 *
 * 불변식:
 *   M11 프레임은 촬영 시작 전 확정되고 이후 변경되지 않는다(`beginSession`만 프레임을 설정한다).
 *   M12 선택은 정확히 슬롯 수, 선택 **순서 = 슬롯 순서**다.
 */

export interface CaptureSessionState<TCut = unknown> {
  /** 촬영 전 고정된 프레임. 세션이 없으면 null. */
  readonly frame: FrameTemplate | null;
  /** 실효 촬영 컷 수(`beginSession`이 의도를 해석한 결과). */
  readonly cutCount: number;
  /** 이 세션의 컷 수가 자동 모드로 산출됐는가 — `Guide`의 "(자동)" 배지 근거(WD19). */
  readonly isAutoCutCount: boolean;
  readonly cuts: readonly TCut[];
  /** 선택된 컷 인덱스. 순서가 곧 슬롯 순서다. */
  readonly selection: readonly number[];
  readonly fullRetakeCount: number;
}

export function createEmptySession<TCut = unknown>(): CaptureSessionState<TCut> {
  return {
    frame: null,
    cutCount: 0,
    isAutoCutCount: false,
    cuts: [],
    selection: [],
    fullRetakeCount: 0,
  };
}

/** 슬롯 수(= 선택해야 할 컷 수). */
export function slotCount(state: CaptureSessionState): number {
  return state.frame?.slots.length ?? 0;
}

/** 모든 컷 촬영 완료. */
export function isCaptureComplete(state: CaptureSessionState): boolean {
  return state.cuts.length >= state.cutCount;
}

/** 정확히 슬롯 수만큼 선택 완료([다음] 활성 조건 — M12). */
export function isSelectionComplete(state: CaptureSessionState): boolean {
  const slots = slotCount(state);
  return state.selection.length === slots && slots > 0;
}

/**
 * 세션 시작 — 프레임을 고정하고 **컷 수 의도를 해석**한다.
 * 슬롯 수가 확정되는 이 지점이 **유일한 해석 지점**이다(VF-12, WD19).
 *
 * @param configuredCutCount 설정의 의도값(6/8/10 또는 0=자동)
 */
export function beginSession<TCut = unknown>(
  frame: FrameTemplate,
  configuredCutCount: number,
): CaptureSessionState<TCut> {
  return {
    frame,
    cutCount: resolveCutCount(configuredCutCount, frame.slots.length),
    isAutoCutCount: isAutoCutCount(configuredCutCount),
    cuts: [],
    selection: [],
    fullRetakeCount: 0,
  };
}

/** 촬영된 컷 추가(셔터 시점). `cutCount`를 넘으면 무시한다. */
export function addCut<TCut>(
  state: CaptureSessionState<TCut>,
  cut: TCut,
): CaptureSessionState<TCut> {
  if (state.cuts.length >= state.cutCount) return state;
  return { ...state, cuts: [...state.cuts, cut] };
}

/**
 * 컷 선택 토글. 이미 선택돼 있으면 해제, 아니면 추가한다.
 * **슬롯 수를 초과해 선택할 수 없다**(M12). 범위 밖 인덱스는 무시한다.
 */
export function toggleSelection<TCut>(
  state: CaptureSessionState<TCut>,
  cutIndex: number,
): CaptureSessionState<TCut> {
  if (cutIndex < 0 || cutIndex >= state.cuts.length) return state;

  const position = state.selection.indexOf(cutIndex);
  if (position >= 0) {
    return {
      ...state,
      selection: state.selection.filter((i) => i !== cutIndex),
    };
  }

  if (state.selection.length >= slotCount(state)) return state; // 정확히 슬롯 수까지만
  return { ...state, selection: [...state.selection, cutIndex] };
}

/** 선택된 컷들을 **슬롯 순서대로** 반환(합성 입력). */
export function getSelectedCuts<TCut>(state: CaptureSessionState<TCut>): TCut[] {
  return state.selection.map((i) => state.cuts[i]!);
}

/** 전체 재촬영 가능 여부(상한 미도달). `limit`은 호출측이 설정에서 읽어 전달한다. */
export function canFullRetake(state: CaptureSessionState, limit: number): boolean {
  return state.fullRetakeCount < limit;
}

/** 전체 재촬영 실행: 컷·선택 폐기 + 카운터 증가(프레임·컷 수는 유지). */
export function beginFullRetake<TCut>(
  state: CaptureSessionState<TCut>,
): CaptureSessionState<TCut> {
  return {
    ...state,
    cuts: [],
    selection: [],
    fullRetakeCount: state.fullRetakeCount + 1,
  };
}

/**
 * 재촬영: 컷·선택만 폐기(카운터 미증가 — Windows의 레거시 경로 대응).
 * 전체 재촬영 상한을 소비하지 않아야 하는 경로에서만 쓴다.
 */
export function resetForRetake<TCut>(
  state: CaptureSessionState<TCut>,
): CaptureSessionState<TCut> {
  return { ...state, cuts: [], selection: [] };
}

/**
 * 세션 완전 폐기(취소·완료·유휴 만료).
 * 여기서 `cutCount = 0`은 "세션 없음"이라는 뜻이며 **자동 sentinel과 무관하다**.
 */
export function discardSession<TCut>(): CaptureSessionState<TCut> {
  return createEmptySession<TCut>();
}
