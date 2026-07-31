import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { canDeleteFrame } from "@domain/frames/frameEditPolicy";
import {
  DEFAULT_FRAME_LOAD_PHASE,
  isFrameListInteractive,
  type FrameLoadPhase,
} from "@domain/frames/frameLoadPolicy";
import { CATALOG_START_LABEL } from "@domain/frames/frameCatalogProgress";
import type { FrameTemplate } from "@domain/frames/types";
import { getFrameCatalog, type UnavailableFrame } from "@adapters/frames/frameCatalog";
import { createFrameRepository } from "@adapters/http/frameRepository";
import { getFrameStore } from "@adapters/storage/frameStore";
import { fixFrameAndResolveCutCount } from "@shell/captureSessionController";
import { setFrameEditorIntent } from "@shell/frameEditorIntent";
import { sessionStore, useSessionStore } from "@shell/sessionStore";
import { useSettingsStore } from "@shell/settingsStore";
import { shellStore } from "@shell/shellStore";
import { defaultLoadDeadline } from "./frameLoadDeadline";
import {
  runFrameLoad,
  type FrameLoadDeps,
  type FrameLoadReason,
  type FrameSelectPatch,
} from "./frameLoadRunner";
import {
  canEditSelected as canEditSelectedFrame,
  canOpenDelete,
  frameSelectPermissions,
  guardInteractive,
  resolveNext,
  runFrameDelete,
} from "./frameSelectActions";

/**
 * `FrameSelect` 화면 배선 — **얇은 훅** (설계 §10)
 *
 * ⚠️ 여기에 **판정을 넣지 않는다.** 이 저장소에는 jsdom이 없어 훅이 테스트에서 호출되지 않는다
 *    (15 §3.1). 상태 보관 + 세대 카운터 + 위 모듈 호출만 한다.
 * ⚠️ 진입 effect cleanup의 순서가 규격이다: **① 세대 증가 → ② `abort()`**. 반대로 하면 취소
 *    예외가 stale이 아닌 상태에서 잡혀 국면을 덮어쓴다.
 */

interface FrameSelectState {
  readonly phase: FrameLoadPhase;
  readonly loadingMessage: string;
  readonly notice: string;
  readonly frames: readonly FrameTemplate[];
  readonly unavailable: readonly UnavailableFrame[];
  readonly selectedId: string | null;
}

const INITIAL_STATE: FrameSelectState = {
  // 첫 페인트에 "빈 목록 + 활성 [다음]"이 나타나지 않게 대기로 시작한다(03 §4.1 완료 기준).
  phase: DEFAULT_FRAME_LOAD_PHASE,
  loadingMessage: CATALOG_START_LABEL,
  notice: "",
  frames: [],
  unavailable: [],
  selectedId: null,
};

/** 정의된 키만 덮는다 — `{...prev, ...patch}`는 선택 필드의 `undefined`가 값을 지운다. */
function mergePatch(prev: FrameSelectState, patch: FrameSelectPatch): FrameSelectState {
  return {
    phase: patch.phase ?? prev.phase,
    loadingMessage: patch.loadingMessage ?? prev.loadingMessage,
    notice: patch.notice ?? prev.notice,
    frames: patch.frames ?? prev.frames,
    unavailable: patch.unavailable ?? prev.unavailable,
    // `selectedId`는 **null이 유효한 값**이라 `??`로 접으면 안 된다.
    selectedId: "selectedId" in patch ? (patch.selectedId ?? null) : prev.selectedId,
  };
}

export interface FrameSelectViewModel {
  readonly phase: FrameLoadPhase;
  readonly loadingMessage: string;
  readonly notice: string;
  readonly frames: readonly FrameTemplate[];
  readonly unavailable: readonly UnavailableFrame[];
  readonly selectedId: string | null;
  readonly selected: FrameTemplate | null;
  /** `isFrameListInteractive(phase)` — 렌더 가드와 액션 가드가 같은 값을 쓴다. */
  readonly interactive: boolean;
  readonly canCreateFrame: boolean;
  readonly canDeleteFrames: boolean;
  readonly isPower: boolean;
  readonly canEditSelected: boolean;
  /** 카드별 ✕ 노출 판정(출처 축). */
  canDelete(frame: FrameTemplate): boolean;
  readonly deleteTarget: FrameTemplate | null;
  readonly deleteAlsoServer: boolean;
  readonly deleteBusy: boolean;
  readonly deleteNotice: string;
  select(id: string): void;
  retry(): void;
  skipWait(): void;
  requestDelete(frame: FrameTemplate): void;
  toggleDeleteServer(value: boolean): void;
  confirmDelete(): void;
  cancelDelete(): void;
  createFrame(): void;
  editSelected(): void;
  goNext(): void;
  cancel(): void;
}

export function useFrameSelect(): FrameSelectViewModel {
  const [state, setState] = useState<FrameSelectState>(INITIAL_STATE);
  const [run, setRun] = useState<{ key: number; reason: FrameLoadReason }>({
    key: 0,
    reason: "enter",
  });
  const [deleteTarget, setDeleteTarget] = useState<FrameTemplate | null>(null);
  const [deleteAlsoServer, setDeleteAlsoServer] = useState(false);
  const [deleteBusy, setDeleteBusy] = useState(false);
  const [deleteNotice, setDeleteNotice] = useState("");

  const user = useSessionStore((s) => s.currentUser);
  const configuredCutCount = useSettingsStore((s) => s.values.CutCount);

  /** 로딩 세대. `isStale()`의 유일한 근거다. */
  const runIdRef = useRef(0);
  /** 현재 로딩의 취소 핸들([기다리지 않고 시작]·언마운트가 쓴다). */
  const abortRef = useRef<(() => void) | null>(null);
  /** React 상태의 동기 사본 — 콜백이 오래된 클로저를 읽지 않게 한다. */
  const stateRef = useRef<FrameSelectState>(INITIAL_STATE);

  const apply = useCallback((patch: FrameSelectPatch) => {
    stateRef.current = mergePatch(stateRef.current, patch);
    setState((prev) => mergePatch(prev, patch));
  }, []);

  const runLoad = useCallback(
    async (reason: FrameLoadReason): Promise<void> => {
      runIdRef.current += 1;
      const myRun = runIdRef.current;
      const deps: FrameLoadDeps = {
        loadPublic: (options) => getFrameCatalog().loadPublic(options),
        loadLocalOnly: () => getFrameCatalog().loadLocalOnly(),
        loadPersonal: (userId) => getFrameCatalog().loadPersonal(userId),
        currentUserId: () => sessionStore.getState().currentUser?.id ?? null,
        initialPhase: () => stateRef.current.phase,
        initialFrameCount: () => stateRef.current.frames.length,
        isStale: () => runIdRef.current !== myRun,
        apply,
        createDeadline: (abort) => defaultLoadDeadline(abort),
        registerAbort: (abort) => {
          abortRef.current = abort;
        },
      };
      await runFrameLoad(deps, reason);
    },
    [apply],
  );

  useEffect(() => {
    void runLoad(run.reason);
    return () => {
      // ① 세대 증가가 **먼저**다 — 그래야 아래 취소로 생기는 예외가 stale로 판정돼
      //    폐기된 화면의 국면을 덮어쓰지 않는다.
      runIdRef.current += 1;
      // ② 이 호출자만 취소한다. 공유 작업은 계속 진행해 캐시를 완성하므로
      //    <StrictMode>의 이중 effect도 중복 다운로드를 만들지 않는다.
      abortRef.current?.();
      abortRef.current = null;
    };
  }, [run, runLoad]);

  const role = user?.role ?? null;
  const permissions = useMemo(() => frameSelectPermissions(role), [role]);
  const interactive = isFrameListInteractive(state.phase);
  const selected = useMemo(
    () => state.frames.find((f) => f.id === state.selectedId) ?? null,
    [state.frames, state.selectedId],
  );

  const select = useCallback((id: string) => {
    if (!guardInteractive(stateRef.current.phase)) return;
    apply({ selectedId: id });
  }, [apply]);

  const retry = useCallback(() => {
    setDeleteTarget(null);
    setRun((prev) => ({ key: prev.key + 1, reason: "retry" }));
  }, []);

  const skipWait = useCallback(() => {
    // ⚠️ **새 로딩을 시작하지 않는다.** 현재 대기만 접고 로컬 폴백으로 마감시킨다 —
    //    공유 작업은 계속 진행하므로 잠시 뒤 [다시 시도]가 성공할 가능성이 높다.
    if (stateRef.current.phase !== "Loading") return;
    abortRef.current?.();
  }, []);

  const canDelete = useCallback(
    // ⚠️ **2인자**다. `userId`를 넘기면 power가 fork 저장한 공용 로컬 프레임의 삭제 능력이 회귀한다.
    (frame: FrameTemplate) => permissions.canDeleteFrames && canDeleteFrame(frame, role),
    [permissions.canDeleteFrames, role],
  );

  const requestDelete = useCallback(
    (frame: FrameTemplate) => {
      if (!canOpenDelete(frame, role, stateRef.current.phase)) return;
      setDeleteNotice("");
      setDeleteAlsoServer(false); // 기본 off — 열 때마다 리셋한다.
      setDeleteTarget(frame);
    },
    [role],
  );

  const cancelDelete = useCallback(() => {
    setDeleteTarget(null);
    setDeleteAlsoServer(false);
  }, []);

  const confirmDelete = useCallback(() => {
    const frame = deleteTarget;
    if (frame === null || deleteBusy) return;
    // ⚠️ 체크 상태를 **오버레이가 닫히기 전에** 지역 값으로 확정한다.
    const alsoServer = deleteAlsoServer;
    setDeleteBusy(true);

    const repository = createFrameRepository();
    void runFrameDelete(
      {
        deleteLocal: (target) => getFrameStore().deleteLocal(target),
        deleteServer: (id) => repository.deleteFrame(id),
        serverFrames: () => repository.getDefaultFrames(),
        applyRemoved: (target) => {
          const next = stateRef.current.frames.filter((f) => f.id !== target.id);
          apply({
            frames: next,
            selectedId:
              stateRef.current.selectedId === target.id
                ? (next[0]?.id ?? null)
                : stateRef.current.selectedId,
          });
          setDeleteTarget(null);
          setDeleteAlsoServer(false);
        },
        setNotice: setDeleteNotice,
        reload: (reason) => runLoad(reason),
      },
      { frame, alsoServer, isPower: permissions.isPower },
    ).finally(() => setDeleteBusy(false));
  }, [apply, deleteAlsoServer, deleteBusy, deleteTarget, permissions.isPower, runLoad]);

  const createFrame = useCallback(() => {
    if (!guardInteractive(stateRef.current.phase)) return;
    if (!permissions.canCreateFrame) return;
    // ⚠️ 인계는 `go()` **직전**이다 — 순서를 뒤집으면 편집기가 이전 의도를 읽는다.
    setFrameEditorIntent({ kind: "new" });
    shellStore.getState().go("FrameEditor");
  }, [permissions.canCreateFrame]);

  const editSelected = useCallback(() => {
    if (!canEditSelectedFrame(selected, role, user?.id ?? null, stateRef.current.phase)) return;
    if (selected === null) return;
    setFrameEditorIntent({ kind: "edit", frame: selected });
    shellStore.getState().go("FrameEditor");
  }, [role, selected, user?.id]);

  const goNext = useCallback(() => {
    resolveNext({
      phase: stateRef.current.phase,
      selected,
      configuredCutCount,
      // ★ 컷 수 해석의 **유일한 지점**(VF-12 · WD19).
      fixFrame: (frame, cutCount) => fixFrameAndResolveCutCount(frame, cutCount),
      go: () => shellStore.getState().go("Guide"),
    });
  }, [configuredCutCount, selected]);

  const cancel = useCallback(() => {
    void shellStore.getState().returnHome("프레임 선택 취소");
  }, []);

  return {
    phase: state.phase,
    loadingMessage: state.loadingMessage,
    notice: state.notice,
    frames: state.frames,
    unavailable: state.unavailable,
    selectedId: state.selectedId,
    selected,
    interactive,
    canCreateFrame: permissions.canCreateFrame,
    canDeleteFrames: permissions.canDeleteFrames,
    isPower: permissions.isPower,
    canEditSelected: canEditSelectedFrame(selected, role, user?.id ?? null, state.phase),
    canDelete,
    deleteTarget,
    deleteAlsoServer,
    deleteBusy,
    deleteNotice,
    select,
    retry,
    skipWait,
    requestDelete,
    toggleDeleteServer: setDeleteAlsoServer,
    confirmDelete,
    cancelDelete,
    createFrame,
    editSelected,
    goNext,
    cancel,
  };
}
