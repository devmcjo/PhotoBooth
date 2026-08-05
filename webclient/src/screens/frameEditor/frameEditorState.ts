import {
  DEFAULT_REGISTER_TO_SERVER,
  DEFAULT_SCALE_PERCENT,
  MAX_SCALE_PERCENT,
  MIN_SCALE_PERCENT,
  type FrameSessionSource,
} from "@domain/frames/frameSavePolicy";
import {
  DEFAULT_SLOT_ASPECT,
  slotAspectToRatio,
  type SlotAspect,
} from "@domain/frames/slotAspect";
import {
  autoArrange,
  clampToFrame,
  MAX_SLOTS,
  MIN_SLOTS,
  rescaleSlots,
  scaleSlots,
} from "@domain/frames/slotLayout";
import type { FrameTemplate, ImageSize, Slot } from "@domain/frames/types";
import { clamp } from "@domain/mathCompat";
import { formatCount, STRINGS } from "@ui/strings";

/**
 * 프레임 편집기 상태 reducer — **순수**(React 무관 · node에서 통째로 검증된다 — 15 §3.1)
 *
 * ⚠️ Windows는 `_suppressArrange` 플래그로 "값 대입이 자동 배치를 유발하는" 문제를 막지만,
 *    여기서는 자동 배치를 **액션 안에서 명시적으로** 하므로 억제 플래그가 필요 없다 — 만들지 마라.
 * ⚠️ 배율은 **항상 `baseSlots`에서** 계산한다(누적 오차 0). 현재 `slots`에 다시 곱하지 마라.
 * ⚠️ 두 오버레이는 **상호배타 단일 필드**다(03 §790) — boolean 2개로 쪼개면 동시에 뜨는 상태가
 *    타입 수준에서 표현 가능해진다.
 */

export type EditorOverlay = "none" | "picker" | "serverRegister";

export type FramePickerPhase = "loading" | "ready" | "failed";

export interface FramePickerState {
  readonly phase: FramePickerPhase;
  readonly frames: readonly FrameTemplate[];
  readonly notice: string;
  /** ⚠️ 자동 선택하지 않는다 — 적용이 파괴적이라 오조작 시 작업이 날아간다(설계 §7.4). */
  readonly selectedId: string | null;
}

/** 부분 갱신(러너가 만든다). 정의된 키만 덮는다. */
export interface FramePickerPatch {
  readonly phase?: FramePickerPhase;
  readonly frames?: readonly FrameTemplate[];
  readonly notice?: string;
  readonly selectedId?: string | null;
}

export const INITIAL_PICKER_STATE: FramePickerState = {
  phase: "loading",
  frames: [],
  notice: "",
  selectedId: null,
};

export interface FrameEditorState {
  /** 세션 정체성 축. 배너·이름 제안·서버 등록·fork 저장을 **전부** 이 값이 결정한다. */
  readonly sessionSource: FrameSessionSource;
  /** fork 원본 이름(④ 가드) 또는 피커로 불러온 원본 이름(안내 캡션). 없으면 "". */
  readonly sourceName: string;
  readonly name: string;
  /** 저장될 PNG 바이트. `null`이면 저장 검증 ③에서 막힌다. */
  readonly png: Blob | null;
  readonly imageSize: ImageSize;
  readonly slotCount: number;
  readonly aspect: SlotAspect;
  readonly scalePercent: number;
  /** 배율 계산의 **기준**. 드래그는 중심을 맞춰 함께 갱신한다(§8.4). */
  readonly baseSlots: readonly Slot[];
  readonly slots: readonly Slot[];
  readonly pickedSourceNotice: string;
  readonly status: string;
  /** 진입 로드·저장 중. 폼을 잠근다. */
  readonly busy: boolean;
  readonly overlay: EditorOverlay;
  readonly registerToServer: boolean;
  readonly picker: FramePickerState;
}

export interface FrameEditorInit {
  readonly sessionSource: FrameSessionSource;
  readonly sourceName: string;
  /** 편집 진입은 이미지·슬롯을 비동기로 준비하므로 폼을 잠근 채 시작한다. */
  readonly busy: boolean;
}

export function initialFrameEditorState(init: Partial<FrameEditorInit> = {}): FrameEditorState {
  return {
    sessionSource: init.sessionSource ?? "New",
    sourceName: init.sourceName ?? "",
    name: "",
    png: null,
    imageSize: { width: 0, height: 0 },
    slotCount: 4,
    aspect: DEFAULT_SLOT_ASPECT,
    scalePercent: DEFAULT_SCALE_PERCENT,
    baseSlots: [],
    slots: [],
    pickedSourceNotice: "",
    status: "",
    busy: init.busy ?? false,
    overlay: "none",
    registerToServer: DEFAULT_REGISTER_TO_SERVER,
    picker: INITIAL_PICKER_STATE,
  };
}

export type FrameEditorAction =
  /** 편집 진입 로드 시작(폼 잠금). */
  | { readonly type: "entryStarted" }
  /** [선택 편집] 진입 완료 — **자동 배치를 하지 않는다**(원본 슬롯을 그대로 쓴다 — §9.3). */
  | {
      readonly type: "editSessionReady";
      readonly name: string;
      readonly png: Blob;
      readonly imageSize: ImageSize;
      readonly slots: readonly Slot[];
    }
  /** 진입 로드 실패 — 이미지 없이 폼만 연다(저장은 ③에서 막힌다). */
  | { readonly type: "entryFailed"; readonly status: string }
  /** 파일에서 이미지 교체 — 자동 배치 + `pickedSourceNotice` **비움**(사실과 어긋나므로). */
  | { readonly type: "imageLoaded"; readonly png: Blob; readonly imageSize: ImageSize }
  /** 피커 적용 — 세션 축을 **바꾸지 않고** 슬롯을 좌표계 환산한다(§7.3). */
  | {
      readonly type: "pickedApplied";
      readonly png: Blob;
      readonly imageSize: ImageSize;
      readonly sourceName: string;
      readonly sourceSlots: readonly Slot[];
      readonly sourceWidth: number;
    }
  | { readonly type: "setName"; readonly name: string }
  | { readonly type: "setSlotCount"; readonly slotCount: number }
  | { readonly type: "setAspect"; readonly aspect: SlotAspect }
  | { readonly type: "setScale"; readonly scalePercent: number }
  | { readonly type: "dragSlot"; readonly index: number; readonly x: number; readonly y: number }
  | { readonly type: "setStatus"; readonly status: string }
  | { readonly type: "setBusy"; readonly busy: boolean }
  /** 오버레이 열기. `serverRegister`는 **열 때마다** 체크박스를 기본값으로 리셋한다(03 §11.4). */
  | { readonly type: "openOverlay"; readonly overlay: Exclude<EditorOverlay, "none"> }
  | { readonly type: "closeOverlay" }
  | { readonly type: "setRegisterToServer"; readonly registerToServer: boolean }
  | { readonly type: "pickerPatch"; readonly patch: FramePickerPatch }
  | { readonly type: "pickerSelect"; readonly id: string };

/** `{...prev, ...patch}`는 선택 필드의 `undefined`가 값을 지운다 — 정의된 키만 덮는다. */
function mergePicker(prev: FramePickerState, patch: FramePickerPatch): FramePickerState {
  return {
    phase: patch.phase ?? prev.phase,
    frames: patch.frames ?? prev.frames,
    notice: patch.notice ?? prev.notice,
    // `selectedId`는 **null이 유효한 값**이라 `??`로 접으면 안 된다.
    selectedId: "selectedId" in patch ? (patch.selectedId ?? null) : prev.selectedId,
  };
}

/** 자동 배치 + 현재 배율 적용. 슬롯 개수·종횡비·파일 이미지 교체가 공유한다. */
function arrange(
  slotCount: number,
  aspect: SlotAspect,
  scalePercent: number,
  size: ImageSize,
): { baseSlots: Slot[]; slots: Slot[] } {
  if (size.width <= 0 || size.height <= 0) return { baseSlots: [], slots: [] };
  const baseSlots = autoArrange(slotCount, size.width, size.height, slotAspectToRatio(aspect));
  return {
    baseSlots,
    slots: scaleSlots(baseSlots, scalePercent / 100, size.width, size.height),
  };
}

export function frameEditorReducer(
  state: FrameEditorState,
  action: FrameEditorAction,
): FrameEditorState {
  switch (action.type) {
    case "entryStarted":
      return { ...state, busy: true, status: "" };

    case "editSessionReady": {
      // ⚠️ `autoArrange`를 부르지 않는다 — 원본 슬롯 좌표를 그대로 보존해야 WYSIWYG가 성립한다.
      const slots = [...action.slots];
      return {
        ...state,
        busy: false,
        status: "",
        name: action.name,
        png: action.png,
        imageSize: action.imageSize,
        slotCount: clamp(slots.length, MIN_SLOTS, MAX_SLOTS),
        scalePercent: DEFAULT_SCALE_PERCENT,
        baseSlots: slots,
        slots,
      };
    }

    case "entryFailed":
      return { ...state, busy: false, status: action.status };

    case "imageLoaded": {
      const arranged = arrange(state.slotCount, state.aspect, DEFAULT_SCALE_PERCENT, action.imageSize);
      return {
        ...state,
        busy: false,
        status: "",
        png: action.png,
        imageSize: action.imageSize,
        scalePercent: DEFAULT_SCALE_PERCENT,
        // 직접 고른 파일이므로 "불러온 원본" 캡션은 사실과 어긋난다 → 비운다(03 §11.5).
        pickedSourceNotice: "",
        sourceName: state.sessionSource === "New" ? "" : state.sourceName,
        ...arranged,
      };
    }

    case "pickedApplied": {
      const size = action.imageSize;
      const factor = action.sourceWidth > 0 ? size.width / action.sourceWidth : 0;
      const usable = action.sourceSlots.length > 0 && factor > 0;
      const slotCount = usable
        ? clamp(action.sourceSlots.length, MIN_SLOTS, MAX_SLOTS)
        : state.slotCount;
      const baseSlots = usable
        ? rescaleSlots(action.sourceSlots.slice(0, slotCount), factor, size.width, size.height)
        : arrange(slotCount, state.aspect, DEFAULT_SCALE_PERCENT, size).baseSlots;

      return {
        ...state,
        busy: false,
        status: "",
        overlay: "none",
        png: action.png,
        imageSize: size,
        slotCount,
        scalePercent: DEFAULT_SCALE_PERCENT,
        baseSlots,
        slots: scaleSlots(baseSlots, 1, size.width, size.height),
        // ⚠️ 세션 축(`sessionSource`)과 `name`은 **건드리지 않는다** — 불러오기는 사본이 아니라
        //    신규 생성이고, 사용자가 이미 타이핑한 이름을 보존해야 한다(03 §11.5).
        sourceName: action.sourceName,
        pickedSourceNotice: formatCount(
          STRINGS.frameEditor.pickedSourceNotice,
          action.sourceName,
        ),
      };
    }

    case "setName":
      return { ...state, name: action.name };

    case "setSlotCount": {
      const slotCount = clamp(action.slotCount, MIN_SLOTS, MAX_SLOTS);
      return {
        ...state,
        slotCount,
        ...arrange(slotCount, state.aspect, state.scalePercent, state.imageSize),
      };
    }

    case "setAspect":
      return {
        ...state,
        aspect: action.aspect,
        ...arrange(state.slotCount, action.aspect, state.scalePercent, state.imageSize),
      };

    case "setScale": {
      const scalePercent = clamp(
        Math.round(action.scalePercent),
        MIN_SCALE_PERCENT,
        MAX_SCALE_PERCENT,
      );
      if (state.imageSize.width <= 0 || state.imageSize.height <= 0) {
        return { ...state, scalePercent };
      }
      return {
        ...state,
        scalePercent,
        // ★ 항상 `baseSlots`에서 — 70→130→100이 원래 값으로 정확히 복귀한다(누적 오차 0).
        slots: scaleSlots(
          state.baseSlots,
          scalePercent / 100,
          state.imageSize.width,
          state.imageSize.height,
        ),
      };
    }

    case "dragSlot": {
      const target = state.slots[action.index];
      const base = state.baseSlots[action.index];
      if (target === undefined || base === undefined) return state;
      const fw = state.imageSize.width;
      const fh = state.imageSize.height;
      if (fw <= 0 || fh <= 0) return state;

      const clamped = clampToFrame(
        {
          index: target.index,
          x: Math.round(action.x),
          y: Math.round(action.y),
          width: target.width,
          height: target.height,
        },
        fw,
        fh,
      );

      // 스케일 기준 슬롯도 **중심을 맞춰** 갱신한다(원본 크기 유지).
      // 하지 않으면 드래그 뒤 배율 슬라이더를 건드리는 순간 슬롯이 원래 자리로 튄다(§8.4).
      const cx = clamped.x + clamped.width / 2;
      const cy = clamped.y + clamped.height / 2;
      const nextBase = clampToFrame(
        {
          index: base.index,
          x: Math.round(cx - base.width / 2),
          y: Math.round(cy - base.height / 2),
          width: base.width,
          height: base.height,
        },
        fw,
        fh,
      );

      const slots = [...state.slots];
      const baseSlots = [...state.baseSlots];
      slots[action.index] = clamped;
      baseSlots[action.index] = nextBase;
      return { ...state, slots, baseSlots };
    }

    case "setStatus":
      return { ...state, status: action.status };

    case "setBusy":
      return { ...state, busy: action.busy };

    case "openOverlay":
      return {
        ...state,
        overlay: action.overlay,
        // 열 때마다 기본값으로 리셋한다(직전 선택 잔존 금지 — 03 §11.4).
        registerToServer: DEFAULT_REGISTER_TO_SERVER,
        picker: action.overlay === "picker" ? INITIAL_PICKER_STATE : state.picker,
      };

    case "closeOverlay":
      return { ...state, overlay: "none", registerToServer: DEFAULT_REGISTER_TO_SERVER };

    case "setRegisterToServer":
      return { ...state, registerToServer: action.registerToServer };

    case "pickerPatch":
      return { ...state, picker: mergePicker(state.picker, action.patch) };

    case "pickerSelect":
      return { ...state, picker: { ...state.picker, selectedId: action.id } };

    default:
      return state;
  }
}
