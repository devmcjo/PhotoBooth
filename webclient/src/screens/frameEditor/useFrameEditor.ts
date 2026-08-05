import {
  useCallback,
  useEffect,
  useMemo,
  useReducer,
  useRef,
  useState,
  type KeyboardEvent as ReactKeyboardEvent,
  type PointerEvent as ReactPointerEvent,
  type RefObject,
} from "react";
import {
  canvasToFrame,
  computeEditorTransform,
  type EditorTransform,
} from "@domain/frames/editorTransform";
import { underscoreWarning } from "@domain/frames/frameNaming";
import {
  frameSaveScope,
  requiresServerRegisterPrompt,
  saveScopeNoticeKind,
  showsLocalOnlyBanner,
  validateFrameSave,
  type FrameSessionSource,
} from "@domain/frames/frameSavePolicy";
import type { SlotAspect } from "@domain/frames/slotAspect";
import type { ImageSize, Slot } from "@domain/frames/types";
import { canWriteFrames } from "@domain/roles/userRole";
import { getFrameCatalog } from "@adapters/frames/frameCatalog";
import {
  fetchFrameImageBytes,
  loadFrameImageFromFile,
  loadFrameImageFromUrl,
  probeFrameImageSize,
} from "@adapters/frames/frameImageLoader";
import { createFrameRepository } from "@adapters/http/frameRepository";
import { createUploadGateway } from "@adapters/http/uploadGateway";
import { getFrameStore } from "@adapters/storage/frameStore";
import { logger } from "@adapters/storage/logStore";
import {
  clearFrameEditorIntent,
  readFrameEditorIntent,
} from "@shell/frameEditorIntent";
import { sessionStore, useSessionStore } from "@shell/sessionStore";
import { shellStore } from "@shell/shellStore";
import { defaultLoadDeadline } from "@screens/frameSelect/frameLoadDeadline";
import {
  frameImageFailureMessage,
  frameSaveRejectionMessage,
  frameSaveScopeNotice,
  STRINGS,
} from "@ui/strings";
import {
  frameEditorInitFor,
  resolveEntryIntent,
  runEditorEntry,
} from "./frameEditorEntry";
import { runFrameSave } from "./frameEditorSave";
import {
  frameEditorReducer,
  initialFrameEditorState,
  type EditorOverlay,
  type FramePickerState,
} from "./frameEditorState";
import { runFramePickerLoad } from "./framePickerRunner";
import { createPreviewUrlHolder, type PreviewUrlHolder } from "./previewUrl";

/**
 * `FrameEditor` 화면 배선 — **얇은 훅** (설계 §14)
 *
 * ⚠️ 여기에 **판정을 넣지 않는다.** 이 저장소에는 jsdom이 없어 훅이 테스트에서 호출되지 않는다
 *    (15 §3.1). 상태 보관 + 세대 카운터 + `screens/frameEditor/*` 모듈 호출만 한다.
 * ⚠️ 진입 effect cleanup의 순서는 **① 세대 증가 → ② abort**다(Step 14와 같은 규격).
 * ⚠️ 표시·드래그·클램프가 **하나의 `EditorTransform`** 을 쓴다 — `transform` state가 유일한 출처다.
 */

const KEYBOARD_STEP = 1;
const KEYBOARD_STEP_FAST = 10;

interface GrabState {
  readonly index: number;
  readonly dx: number;
  readonly dy: number;
  readonly pointerId: number;
}

const INVALID_TRANSFORM: EditorTransform = {
  scale: 0,
  originX: 0,
  originY: 0,
  displayWidth: 0,
  displayHeight: 0,
};

export interface FrameEditorViewModel {
  /** 렌더 가드(M10 ①). false면 편집기 본문을 렌더하지 않는다. */
  readonly allowed: boolean;
  readonly title: string;
  readonly sessionSource: FrameSessionSource;
  /** 정책 배너는 **편집 세션 전용**이다(신규 생성 세션에는 문장이 거짓이 된다). */
  readonly showsBanner: boolean;
  readonly name: string;
  readonly previewUrl: string;
  readonly transform: EditorTransform;
  readonly imageSize: ImageSize;
  readonly slots: readonly Slot[];
  readonly slotCount: number;
  readonly aspect: SlotAspect;
  readonly scalePercent: number;
  readonly hasImage: boolean;
  readonly busy: boolean;
  readonly status: string;
  readonly pickedSourceNotice: string;
  readonly scopeNotice: string;
  readonly showsUnderscoreWarning: boolean;
  /** [기존 프레임에서 불러오기] 노출 — 생성 모드 전용. */
  readonly canPick: boolean;
  readonly overlay: EditorOverlay;
  readonly registerToServer: boolean;
  readonly picker: FramePickerState;
  readonly stageRef: RefObject<HTMLDivElement>;

  setName(next: string): void;
  setSlotCount(next: number): void;
  setAspect(next: SlotAspect): void;
  setScale(next: number): void;
  chooseFile(file: File): void;

  openPicker(): void;
  closePicker(): void;
  selectPickerFrame(id: string): void;
  applyPicked(): void;

  requestSave(): void;
  toggleRegisterToServer(next: boolean): void;
  confirmRegisterSave(): void;
  cancelRegister(): void;
  cancel(): void;

  onSlotPointerDown(index: number, event: ReactPointerEvent<HTMLElement>): void;
  onSlotPointerMove(event: ReactPointerEvent<HTMLElement>): void;
  onSlotPointerEnd(event: ReactPointerEvent<HTMLElement>): void;
  onSlotKeyDown(index: number, event: ReactKeyboardEvent<HTMLElement>): void;
}

export function useFrameEditor(): FrameEditorViewModel {
  const user = useSessionStore((s) => s.currentUser);
  const role = user?.role ?? null;
  const userId = user?.id ?? null;
  const allowed = role !== null && canWriteFrames(role);

  // ⚠️ `readFrameEditorIntent`는 **비파괴**다 — 소비형이면 <StrictMode> 2회차가 `new`로 떨어진다.
  const resolved = useMemo(
    () => resolveEntryIntent(readFrameEditorIntent(), role, userId),
    [role, userId],
  );

  const [state, dispatch] = useReducer(frameEditorReducer, resolved, (initial) =>
    initialFrameEditorState(frameEditorInitFor(initial.intent)),
  );
  const [transform, setTransform] = useState<EditorTransform>(INVALID_TRANSFORM);
  const [previewUrl, setPreviewUrl] = useState("");

  const stageRef = useRef<HTMLDivElement>(null);
  const grabRef = useRef<GrabState | null>(null);
  /** 진입 로드 세대. `isStale()`의 유일한 근거다. */
  const entryRunRef = useRef(0);
  /** 피커 로드 세대 + 취소 핸들. */
  const pickerRunRef = useRef(0);
  const pickerAbortRef = useRef<(() => void) | null>(null);
  /** React 상태의 동기 사본 — 콜백이 오래된 클로저를 읽지 않게 한다. */
  const stateRef = useRef(state);
  stateRef.current = state;
  const transformRef = useRef(transform);
  transformRef.current = transform;
  /** 미리보기 object URL의 단일 소유자(누수 0 — §9.4). */
  const holderRef = useRef<PreviewUrlHolder | null>(null);
  holderRef.current ??= createPreviewUrlHolder();

  // ── 진입: [선택 편집] 세션의 이미지·슬롯·이름 제안 준비 ──
  useEffect(() => {
    entryRunRef.current += 1;
    const myRun = entryRunRef.current;
    if (resolved.blocked) {
      dispatch({ type: "setStatus", status: STRINGS.frameEditor.editNotAllowed });
    }
    void runEditorEntry(
      {
        scopeNames: () => scopeNamesFor(role, userId),
        uniqueSuffix: newUniqueSuffix,
        fetchBytes: (url) => fetchFrameImageBytes(url),
        probeSize: (blob) => probeFrameImageSize(blob),
        dispatch,
        isStale: () => entryRunRef.current !== myRun,
      },
      resolved.intent,
    );
    return () => {
      entryRunRef.current += 1;
    };
  }, [resolved, role, userId]);

  // ── 미리보기 URL: 이전 것을 반드시 해제한다(언마운트 포함) ──
  useEffect(() => {
    const holder = holderRef.current;
    if (holder === null) return undefined;
    setPreviewUrl(holder.set(state.png));
    return () => {
      holder.dispose();
    };
  }, [state.png]);

  // ── 스테이지 실측: **선언 크기 금지**(03 §11.7). `getBoundingClientRect()`만 쓴다 ──
  const frameW = state.imageSize.width;
  const frameH = state.imageSize.height;
  useEffect(() => {
    const stage = stageRef.current;
    if (stage === null) return undefined;

    const measure = (): void => {
      const rect = stage.getBoundingClientRect();
      setTransform(computeEditorTransform(rect.width, rect.height, frameW, frameH));
    };
    measure();

    if (typeof ResizeObserver === "undefined") return undefined;
    const observer = new ResizeObserver(measure);
    observer.observe(stage);
    return () => observer.disconnect();
  }, [frameW, frameH, allowed]);

  // ── 화면 이탈: 진행 중인 피커 로딩을 끊는다 ──
  useEffect(() => {
    return () => {
      // ① 세대 증가가 **먼저**다(Step 14와 같은 규격) — 그래야 취소로 생기는 늦은 보고가
      //    stale로 판정돼 폐기된 화면의 상태를 건드리지 않는다. ② 그다음 취소한다.
      //    공유 작업(카탈로그 단일 비행)은 계속 진행해 캐시를 완성한다.
      pickerRunRef.current += 1;
      pickerAbortRef.current?.();
      pickerAbortRef.current = null;
    };
  }, []);

  const leave = useCallback(() => {
    clearFrameEditorIntent();
    shellStore.getState().go("FrameSelect");
  }, []);

  // ─────────────── 편집 조작 ───────────────

  const setName = useCallback((next: string) => dispatch({ type: "setName", name: next }), []);
  const setSlotCount = useCallback(
    (next: number) => dispatch({ type: "setSlotCount", slotCount: next }),
    [],
  );
  const setAspect = useCallback(
    (next: SlotAspect) => dispatch({ type: "setAspect", aspect: next }),
    [],
  );
  const setScale = useCallback(
    (next: number) => dispatch({ type: "setScale", scalePercent: next }),
    [],
  );

  const chooseFile = useCallback(
    (file: File) => {
      if (!allowed) return; // 액션 첫 줄 가드(M10 ②)
      dispatch({ type: "setBusy", busy: true });
      void (async () => {
        const outcome = await loadFrameImageFromFile(file);
        if (!outcome.ok) {
          dispatch({ type: "setBusy", busy: false });
          dispatch({ type: "setStatus", status: frameImageFailureMessage(outcome.failure) });
          return;
        }
        dispatch({
          type: "imageLoaded",
          png: outcome.image.blob,
          imageSize: { width: outcome.image.width, height: outcome.image.height },
        });
      })();
    },
    [allowed],
  );

  // ─────────────── 피커 ───────────────

  const openPicker = useCallback(() => {
    if (!allowed) return;
    // 생성 모드 전용(03 §11.5) — 세션 축을 그대로 쓴다.
    if (stateRef.current.sessionSource !== "New") return;
    dispatch({ type: "openOverlay", overlay: "picker" });

    pickerRunRef.current += 1;
    const myRun = pickerRunRef.current;
    void runFramePickerLoad({
      loadPublic: (options) => getFrameCatalog().loadPublic(options),
      loadLocalOnly: () => getFrameCatalog().loadLocalOnly(),
      loadPersonal: (id) => getFrameCatalog().loadPersonal(id),
      currentUserId: () => sessionStore.getState().currentUser?.id ?? null,
      isStale: () => pickerRunRef.current !== myRun,
      apply: (patch) => dispatch({ type: "pickerPatch", patch }),
      createDeadline: (abort) => defaultLoadDeadline(abort),
      registerAbort: (abort) => {
        pickerAbortRef.current = abort;
      },
    });
  }, [allowed]);

  const closePicker = useCallback(() => {
    // ① 세대 증가가 **먼저**다 — 그래야 취소로 생기는 늦은 보고가 stale로 판정된다.
    pickerRunRef.current += 1;
    pickerAbortRef.current?.();
    pickerAbortRef.current = null;
    dispatch({ type: "closeOverlay" });
  }, []);

  const selectPickerFrame = useCallback(
    (id: string) => dispatch({ type: "pickerSelect", id }),
    [],
  );

  const applyPicked = useCallback(() => {
    if (!allowed) return;
    const current = stateRef.current;
    const source =
      current.picker.frames.find((f) => f.id === current.picker.selectedId) ?? null;
    if (source === null) return;

    dispatch({ type: "setBusy", busy: true });
    void (async () => {
      const outcome = await loadFrameImageFromUrl(source.imageUrl);
      if (!outcome.ok) {
        // 오버레이만 닫고 **편집기 상태는 무변경**이다(§7.3 ①).
        dispatch({ type: "setBusy", busy: false });
        dispatch({ type: "closeOverlay" });
        dispatch({ type: "setStatus", status: STRINGS.frameEditor.pickedImageMissing });
        return;
      }
      pickerRunRef.current += 1;
      pickerAbortRef.current?.();
      pickerAbortRef.current = null;
      dispatch({
        type: "pickedApplied",
        png: outcome.image.blob,
        imageSize: { width: outcome.image.width, height: outcome.image.height },
        sourceName: source.name,
        sourceSlots: source.slots,
        sourceWidth: source.imageSize.width,
      });
    })();
  }, [allowed]);

  // ─────────────── 저장 ───────────────

  const persist = useCallback(
    async (registerToServer: boolean): Promise<void> => {
      const current = stateRef.current;
      const repository = createFrameRepository();
      const gateway = createUploadGateway();
      dispatch({ type: "setBusy", busy: true });
      try {
        await runFrameSave(
          {
            scopeNames: () => scopeNamesFor(role, userId),
            personalCount: () =>
              userId === null ? Promise.resolve(0) : getFrameStore().countPersonal(userId),
            createServerFrame: (request) => repository.createFrame(request),
            putImage: (request) => gateway.put(request),
            deleteServerFrame: (id) => repository.deleteFrame(id),
            saveLocal: (input) => getFrameStore().saveLocal(input),
            setStatus: (message) => dispatch({ type: "setStatus", status: message }),
            goToFrameSelect: leave,
          },
          {
            role,
            userId,
            sessionSource: current.sessionSource,
            name: current.name,
            sourceName: current.sourceName,
            slots: current.slots,
            imageSize: current.imageSize,
            png: current.png,
            registerToServer,
          },
        );
      } finally {
        dispatch({ type: "setBusy", busy: false });
      }
    },
    [leave, role, userId],
  );

  const requestSave = useCallback(() => {
    if (!allowed) return; // 액션 첫 줄 가드(M10 ②) — 3차 게이트는 `runFrameSave`가 한다.
    if (stateRef.current.busy) return;

    void (async () => {
      // 선판정 조회(IndexedDB)도 비동기다 — 그 대기 구간에 [저장]이 다시 눌리면
      // `persist`가 병렬로 두 번 돈다. 다른 액션(`chooseFile`/`applyPicked`/`persist`)과
      // 같은 관례로 **await 이전에 동기적으로** 잠근다.
      dispatch({ type: "setBusy", busy: true });
      const current = stateRef.current;
      // 선판정: 실패하면 저장도 오버레이도 없다.
      const existingNames = await scopeNamesFor(role, userId);
      const personalCount =
        userId === null ? 0 : await getFrameStore().countPersonal(userId);
      const validation = validateFrameSave({
        role,
        sessionSource: current.sessionSource,
        hasImage: current.png !== null,
        slots: current.slots,
        frameWidth: current.imageSize.width,
        frameHeight: current.imageSize.height,
        name: current.name,
        sourceName: current.sourceName,
        existingNames,
        personalCount,
      });
      if (!validation.ok) {
        const reason = validation.reason ?? "invalid-slots";
        dispatch({ type: "setBusy", busy: false });
        dispatch({ type: "setStatus", status: frameSaveRejectionMessage(reason) });
        return;
      }

      // ★ 오버레이 노출 축 = 등록 분기 축(FR-11). 파생값을 쓰지 마라.
      if (requiresServerRegisterPrompt(role, current.sessionSource)) {
        // 이 시점에 **아무것도 저장하지 않는다**. 체크박스는 열 때마다 기본값으로 리셋된다.
        dispatch({ type: "setBusy", busy: false });
        dispatch({ type: "openOverlay", overlay: "serverRegister" });
        return;
      }
      // `persist`가 busy=true를 재설정하고 `finally`에서 해제한다(멱등).
      await persist(false);
    })();
  }, [allowed, persist, role, userId]);

  const toggleRegisterToServer = useCallback(
    (next: boolean) => dispatch({ type: "setRegisterToServer", registerToServer: next }),
    [],
  );

  const confirmRegisterSave = useCallback(() => {
    if (!allowed) return;
    if (stateRef.current.busy) return;
    // ★ 체크 상태를 **닫기 전에** 지역 값으로 확정한다 — 리셋이 먼저면 선택이 조용히 무시된다.
    const alsoServer = stateRef.current.registerToServer;
    dispatch({ type: "closeOverlay" });
    void persist(alsoServer);
  }, [allowed, persist]);

  const cancelRegister = useCallback(() => {
    // 저장·전환·저장소 모두 무변경.
    dispatch({ type: "closeOverlay" });
  }, []);

  const cancel = useCallback(() => {
    leave();
  }, [leave]);

  // ─────────────── 드래그(그랩 오프셋 기반 절대 위치) ───────────────

  /** 포인터 위치 → 프레임 좌표. 표시와 **같은 변환**을 쓴다(WYSIWYG의 근거). */
  const toFramePoint = useCallback((clientX: number, clientY: number) => {
    const stage = stageRef.current;
    if (stage === null) return null;
    const rect = stage.getBoundingClientRect();
    return canvasToFrame(transformRef.current, clientX - rect.left, clientY - rect.top);
  }, []);

  const onSlotPointerDown = useCallback(
    (index: number, event: ReactPointerEvent<HTMLElement>) => {
      const slot = stateRef.current.slots[index];
      if (slot === undefined) return;
      const point = toFramePoint(event.clientX, event.clientY);
      if (point === null) return;
      event.currentTarget.setPointerCapture(event.pointerId);
      grabRef.current = {
        index,
        dx: point.x - slot.x,
        dy: point.y - slot.y,
        pointerId: event.pointerId,
      };
    },
    [toFramePoint],
  );

  const onSlotPointerMove = useCallback(
    (event: ReactPointerEvent<HTMLElement>) => {
      const grab = grabRef.current;
      if (grab === null || grab.pointerId !== event.pointerId) return;
      const point = toFramePoint(event.clientX, event.clientY);
      if (point === null) return;
      // 매 이동마다 **절대 위치를 새로 계산**한다 — 델타를 누적하면 오차가 쌓인다.
      dispatch({
        type: "dragSlot",
        index: grab.index,
        x: point.x - grab.dx,
        y: point.y - grab.dy,
      });
    },
    [toFramePoint],
  );

  /** ⚠️ `pointerup`·`pointercancel`·`lostpointercapture` **셋 다** 이 함수를 부른다. */
  const onSlotPointerEnd = useCallback((event: ReactPointerEvent<HTMLElement>) => {
    if (grabRef.current?.pointerId !== event.pointerId) return;
    grabRef.current = null;
  }, []);

  const onSlotKeyDown = useCallback(
    (index: number, event: ReactKeyboardEvent<HTMLElement>) => {
      const slot = stateRef.current.slots[index];
      if (slot === undefined) return;
      const step = event.shiftKey ? KEYBOARD_STEP_FAST : KEYBOARD_STEP;
      let dx = 0;
      let dy = 0;
      switch (event.key) {
        case "ArrowLeft":
          dx = -step;
          break;
        case "ArrowRight":
          dx = step;
          break;
        case "ArrowUp":
          dy = -step;
          break;
        case "ArrowDown":
          dy = step;
          break;
        default:
          return;
      }
      event.preventDefault();
      dispatch({ type: "dragSlot", index, x: slot.x + dx, y: slot.y + dy });
    },
    [],
  );

  const scope = frameSaveScope(role);
  const scopeNotice = frameSaveScopeNotice(
    saveScopeNoticeKind(role, state.sessionSource),
    state.name,
  );

  return {
    allowed,
    title:
      state.sessionSource === "New"
        ? STRINGS.frameEditor.titleNew
        : STRINGS.frameEditor.titleEdit,
    sessionSource: state.sessionSource,
    showsBanner: showsLocalOnlyBanner(state.sessionSource),
    name: state.name,
    previewUrl,
    transform,
    imageSize: state.imageSize,
    slots: state.slots,
    slotCount: state.slotCount,
    aspect: state.aspect,
    scalePercent: state.scalePercent,
    hasImage: state.png !== null,
    busy: state.busy,
    status: state.status,
    pickedSourceNotice: state.pickedSourceNotice,
    scopeNotice,
    showsUnderscoreWarning: underscoreWarning(state.name, scope),
    canPick: state.sessionSource === "New",
    overlay: state.overlay,
    registerToServer: state.registerToServer,
    picker: state.picker,
    stageRef,
    setName,
    setSlotCount,
    setAspect,
    setScale,
    chooseFile,
    openPicker,
    closePicker,
    selectPickerFrame,
    applyPicked,
    requestSave,
    toggleRegisterToServer,
    confirmRegisterSave,
    cancelRegister,
    cancel,
    onSlotPointerDown,
    onSlotPointerMove,
    onSlotPointerEnd,
    onSlotKeyDown,
  };
}

/** 저장 스코프의 기존 이름. 실패는 어댑터가 이미 빈 배열로 축소한다(⑦ 비차단). */
function scopeNamesFor(
  role: Parameters<typeof frameSaveScope>[0],
  userId: string | null,
): Promise<readonly string[]> {
  const scope = frameSaveScope(role);
  return scope === "public"
    ? getFrameStore().scopeFrameNames("public", null)
    : getFrameStore().scopeFrameNames("user", userId);
}

/** 사본 이름 8자 접미. 도메인은 난수를 만들지 않으므로 화면 경계에서 만든다(01 §8). */
function newUniqueSuffix(): string {
  const source = globalThis.crypto;
  if (typeof source?.randomUUID === "function") {
    return source.randomUUID().replace(/-/g, "").slice(0, 8);
  }
  logger.warn("crypto.randomUUID 미지원 — 시각 기반 접미로 폴백");
  return Date.now().toString(16).slice(-8);
}
