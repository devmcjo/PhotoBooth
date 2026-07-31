import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import type { AppSettingsValues, WebExtras } from "@domain/settings/appSettings";
import type { SettingsEditContext } from "@domain/settings/settingsEditPolicy";
import {
  listCameras,
  onDeviceChange,
  type CameraDevice,
} from "@adapters/camera/deviceEnumerator";
import { getDirHandleRepo } from "@adapters/storage/dirHandleRepo";
import {
  readStorageStatus,
  requestPersistentStorage,
  type StorageStatus,
} from "@adapters/platform/persistStorage";
import { logger } from "@adapters/storage/logStore";
import { canWriteFrames } from "@domain/roles/userRole";
import { isTempUserQrBlocked } from "@shell/qrUsageStore";
import { checkForUpdate } from "@shell/swUpdate";
import { useSessionStore } from "@shell/sessionStore";
import { useSettingsStore } from "@shell/settingsStore";
import { shellStore } from "@shell/shellStore";
import { STRINGS } from "@ui/strings";
import { selectCamera, selectFacing } from "./cameraDevicePanel";
import {
  applyQrToggle,
  changeSetting,
  createDraft,
  saveSettings,
  type QrToggleKey,
  type SettingsDraft,
} from "./settingsForm";
import {
  defaultServerStatusDeps,
  loadServerStatus,
  type ServerStatusView,
} from "./serverStatusPanel";
import {
  defaultStoredResultsDeps,
  describeRemoveAll,
  EMPTY_STORED_RESULTS,
  loadStoredResults,
  removeAllStoredResults,
  removeStoredResult,
  type StoredResultsView,
} from "./storedResultsPanel";
import {
  applyImport,
  buildExport,
  defaultSettingsExportDeps,
  previewImport,
  type ImportPreview,
} from "./settingsTransfer";
import {
  applyFramePreview,
  frameImportDoneMessage,
  frameImportRejectionMessage,
  runFrameExport,
  startFrameImport,
  type FrameImportPreview,
} from "./frameTransfer";

/**
 * 설정 화면 상태를 묶는 **얇은** 훅 — 판정·조립은 전부 위 모듈들이 한다(15 §3.1).
 *
 * ⚠️ 이 파일에 판정 로직을 넣지 마라. jsdom이 없어 훅은 테스트에서 호출할 수 없다 —
 *    여기 들어간 규칙은 영원히 검증되지 않는다.
 * ⚠️ 자원 해제: `devicechange` 구독 · 서버 프로브 · 패널 조회 전부 cleanup을 건다(설계 §6.4).
 */

function toast(kind: "success" | "error" | "info", message: string): void {
  shellStore.getState().toast(kind, message);
}

export function useSettingsScreen() {
  const storedValues = useSettingsStore((s) => s.values);
  const storedWebExtras = useSettingsStore((s) => s.webExtras);
  const user = useSessionStore((s) => s.currentUser);
  const isGuest = user === null;
  const role = user?.role ?? null;
  const userId = user?.id ?? null;

  const ctx = useMemo<SettingsEditContext>(
    // TempUser 한도는 계정 변경 시 1회 캐시된 **동기 판정**이다(07 §7).
    () => ({ isGuest, qrBlocked: isTempUserQrBlocked() }),
    [isGuest],
  );

  const [draft, setDraft] = useState<SettingsDraft>(() =>
    createDraft(storedValues, storedWebExtras),
  );
  const [devices, setDevices] = useState<readonly CameraDevice[]>([]);
  const [storedResults, setStoredResults] = useState<StoredResultsView>({
    ...EMPTY_STORED_RESULTS,
    loading: true,
  });
  const [serverStatus, setServerStatus] = useState<ServerStatusView>({ kind: "loading" });
  const [storage, setStorage] = useState<StorageStatus | null>(null);
  const [importPreview, setImportPreview] = useState<ImportPreview | null>(null);
  const [importError, setImportError] = useState<string | null>(null);
  const [framePreview, setFramePreview] = useState<FrameImportPreview | null>(null);
  const [frameImportError, setFrameImportError] = useState<string | null>(null);
  const [confirmingDeleteAll, setConfirmingDeleteAll] = useState(false);

  const folderSupported = useMemo(() => getDirHandleRepo().isSupported(), []);
  const mountedRef = useRef(true);

  useEffect(() => {
    mountedRef.current = true;
    return () => {
      mountedRef.current = false;
    };
  }, []);

  // ── 카메라 장치 열거 + devicechange 구독 ──────────────────────────────
  const refreshCameras = useCallback((): void => {
    void listCameras().then((found) => {
      if (mountedRef.current) setDevices(found);
    });
  }, []);

  useEffect(() => {
    refreshCameras();
    // ⚠️ 반환된 해제 함수를 반드시 호출한다(USB 웹캠 착탈 구독 누수 방지).
    const unsubscribe = onDeviceChange(refreshCameras);
    return () => unsubscribe();
  }, [refreshCameras]);

  // ── 서버 연결 상태 ────────────────────────────────────────────────────
  const refreshServerStatus = useCallback((signal?: AbortSignal): void => {
    setServerStatus({ kind: "loading" });
    void loadServerStatus(defaultServerStatusDeps(), signal).then((view) => {
      if (view.kind === "cancelled" || !mountedRef.current) return;
      setServerStatus(view);
    });
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    refreshServerStatus(controller.signal);
    return () => controller.abort();
  }, [refreshServerStatus]);

  // ── 보관된 결과물 + 저장소 상태 ───────────────────────────────────────
  const refreshStoredResults = useCallback((): void => {
    setStoredResults((current) => ({ ...current, loading: true }));
    void loadStoredResults(defaultStoredResultsDeps()).then((view) => {
      if (mountedRef.current) setStoredResults(view);
    });
  }, []);

  const refreshStorage = useCallback((): void => {
    void readStorageStatus(
      typeof navigator === "undefined" ? undefined : navigator.storage,
    ).then((status) => {
      if (mountedRef.current) setStorage(status);
    });
  }, []);

  useEffect(() => {
    refreshStoredResults();
    refreshStorage();
  }, [refreshStoredResults, refreshStorage]);

  // ── 편집 ──────────────────────────────────────────────────────────────
  const change = useCallback(
    <K extends keyof AppSettingsValues>(key: K, value: AppSettingsValues[K]): void => {
      setDraft((current) => changeSetting(current, key, value, ctx));
    },
    [ctx],
  );

  const toggleQr = useCallback(
    (key: QrToggleKey, next: boolean): void => {
      setDraft((current) => applyQrToggle(current, key, next, ctx));
    },
    [ctx],
  );

  const chooseCamera = useCallback(
    (device: CameraDevice): void => {
      setDraft((current) => selectCamera(current, device, ctx));
    },
    [ctx],
  );

  const chooseFacing = useCallback((facing: WebExtras["CameraFacing"]): void => {
    setDraft((current) => selectFacing(current, facing));
  }, []);

  // ── 저장 ──────────────────────────────────────────────────────────────
  const readBack = useCallback((): SettingsDraft => {
    const state = useSettingsStore.getState();
    return createDraft(state.values, state.webExtras);
  }, []);

  const save = useCallback((): void => {
    saveSettings({
      draft,
      ctx,
      save: (patch, options) => useSettingsStore.getState().save(patch, options),
      readBack,
      resetDraft: setDraft,
      toast,
    });
  }, [draft, ctx, readBack]);

  const close = useCallback((): void => {
    shellStore.getState().closeOverlay();
  }, []);

  // ── 로컬 저장 폴더(② 계층) ────────────────────────────────────────────
  /**
   * ⚠️ 폴더 지정은 **즉시 저장**한다. 폴더 핸들 자체가 이 순간 IndexedDB에 들어가므로,
   *    표시값만 draft에 남겨 두면 [저장] 없이 화면을 닫았을 때 "표시는 비었는데 복사는 된다"는
   *    어긋남이 생긴다. 다른 편집 중인 draft 값은 건드리지 않는다.
   */
  const persistLocalSavePath = useCallback(
    (folderName: string): void => {
      const ok = useSettingsStore.getState().save({ LocalSavePath: folderName }, { isGuest });
      setDraft((current) => ({
        ...current,
        values: { ...current.values, LocalSavePath: folderName },
      }));
      toast(ok ? "success" : "error", ok ? STRINGS.save.succeeded : STRINGS.save.failed);
    },
    [isGuest],
  );

  const pickFolder = useCallback((): void => {
    // ⚠️ `showDirectoryPicker`는 **사용자 제스처**에서만 열린다 — effect에서 부르지 마라.
    void (async () => {
      const repo = getDirHandleRepo();
      const handle = await repo.pick();
      if (handle === null) return; // 취소는 정상 경로다(설정을 건드리지 않는다)
      if (!(await repo.store(handle))) {
        toast("error", STRINGS.save.failed);
        return;
      }
      if (mountedRef.current) persistLocalSavePath(handle.name);
    })();
  }, [persistLocalSavePath]);

  const clearFolder = useCallback((): void => {
    void (async () => {
      const ok = await getDirHandleRepo().clear();
      if (!mountedRef.current) return;
      if (!ok) {
        toast("error", STRINGS.save.failed);
        return;
      }
      persistLocalSavePath("");
    })();
  }, [persistLocalSavePath]);

  // ── 저장소 영속 ───────────────────────────────────────────────────────
  const requestPersist = useCallback((): void => {
    void requestPersistentStorage(
      typeof navigator === "undefined" ? undefined : navigator.storage,
    ).then((status) => {
      if (!mountedRef.current) return;
      setStorage(status);
      if (status.persistState !== "granted") toast("info", STRINGS.storage.persistDenied);
    });
  }, []);

  // ── 보관된 결과물 삭제 ────────────────────────────────────────────────
  const removeResult = useCallback(
    (name: string): void => {
      void removeStoredResult(defaultStoredResultsDeps(), name).then((ok) => {
        if (!mountedRef.current) return;
        if (!ok) toast("error", STRINGS.settings.storedResultsDeleteFailed);
        refreshStoredResults();
        refreshStorage();
      });
    },
    [refreshStoredResults, refreshStorage],
  );

  const removeAllResults = useCallback((): void => {
    const names = storedResults.folders.map((folder) => folder.name);
    setConfirmingDeleteAll(false);
    void removeAllStoredResults(defaultStoredResultsDeps(), names).then((outcome) => {
      if (!mountedRef.current) return;
      toast(outcome.failed === 0 ? "success" : "error", describeRemoveAll(outcome));
      refreshStoredResults();
      refreshStorage();
    });
  }, [storedResults.folders, refreshStoredResults, refreshStorage]);

  // ── 내보내기 / 가져오기 ───────────────────────────────────────────────
  const exportSettings = useCallback((): void => {
    const ok = buildExport(defaultSettingsExportDeps());
    if (!ok) toast("error", STRINGS.save.failed);
  }, []);

  const startImport = useCallback(
    (file: File): void => {
      setImportError(null);
      void file
        .text()
        .then((text) => {
          if (!mountedRef.current) return;
          const result = previewImport(text, draft.values);
          if (!result.ok) {
            setImportPreview(null);
            setImportError(
              result.reason === "tooNew"
                ? STRINGS.settings.importTooNew
                : STRINGS.settings.importMalformed,
            );
            return;
          }
          setImportPreview(result.preview);
        })
        .catch((err: unknown) => {
          logger.warn("설정 파일을 읽지 못했습니다", {
            reason: err instanceof Error ? err.message : String(err),
          });
          if (mountedRef.current) setImportError(STRINGS.settings.importMalformed);
        });
    },
    [draft.values],
  );

  const applyPreview = useCallback((): void => {
    if (importPreview === null) return;
    applyImport({
      preview: importPreview,
      draft,
      ctx,
      save: (patch, options) => useSettingsStore.getState().save(patch, options),
      readBack,
      resetDraft: setDraft,
      toast,
    });
    setImportPreview(null);
  }, [importPreview, draft, ctx, readBack]);

  const cancelImport = useCallback((): void => {
    setImportPreview(null);
    setImportError(null);
  }, []);

  const openCameraTest = useCallback((): void => {
    shellStore.getState().pushModal({ id: "cameraTest", dismissible: true });
  }, []);

  // ── 진단·상태 모달(로그인 전용) ───────────────────────────────────────
  const openDiagnostics = useCallback((): void => {
    // 렌더 가드 + 액션 첫 줄 가드 2중(M10). 게스트에게는 버튼이 없다.
    if (isGuest) {
      toast("error", STRINGS.settings.editBlocked);
      return;
    }
    shellStore.getState().pushModal({ id: "diagnostics", dismissible: true });
  }, [isGuest]);

  // ── 프레임 내보내기 / 가져오기 ────────────────────────────────────────
  const exportFrames = useCallback((): void => {
    void runFrameExport(userId).then((report) => {
      if (!mountedRef.current) return;
      toast(report.ok ? "success" : "error", report.message);
    });
  }, [userId]);

  const startFrameImportFile = useCallback(
    (file: File): void => {
      setFrameImportError(null);
      void startFrameImport(file, role, userId).then((result) => {
        if (!mountedRef.current) return;
        if (!result.ok) {
          setFramePreview(null);
          setFrameImportError(frameImportRejectionMessage(result.reason));
          return;
        }
        setFramePreview(result.preview);
      });
    },
    [role, userId],
  );

  const applyFrameImport = useCallback((): void => {
    const preview = framePreview;
    if (preview === null) return;
    setFramePreview(null);
    void applyFramePreview(preview, role, userId).then((outcome) => {
      if (!mountedRef.current) return;
      if (outcome === null) {
        toast("error", STRINGS.error.forbidden);
        return;
      }
      toast(outcome.failed === 0 ? "success" : "error", frameImportDoneMessage(outcome));
    });
  }, [framePreview, role, userId]);

  const cancelFrameImport = useCallback((): void => {
    setFramePreview(null);
    setFrameImportError(null);
  }, []);

  // ── 앱 업데이트 확인 ──────────────────────────────────────────────────
  const checkAppUpdate = useCallback((): void => {
    void checkForUpdate().then((found) => {
      if (!mountedRef.current) return;
      toast("info", found ? STRINGS.pwa.updateFound : STRINGS.pwa.upToDate);
    });
  }, []);

  return {
    draft,
    ctx,
    canWriteFrames: !isGuest && role !== null && canWriteFrames(role),
    framePreview,
    frameImportError,
    exportFrames,
    startFrameImportFile,
    applyFrameImport,
    cancelFrameImport,
    openDiagnostics,
    checkAppUpdate,
    devices,
    folderSupported,
    storedResults,
    serverStatus,
    storage,
    importPreview,
    importError,
    confirmingDeleteAll,
    setConfirmingDeleteAll,
    change,
    toggleQr,
    chooseCamera,
    chooseFacing,
    save,
    close,
    refreshCameras,
    refreshServerStatus,
    refreshStoredResults,
    openCameraTest,
    pickFolder,
    clearFolder,
    requestPersist,
    removeResult,
    removeAllResults,
    exportSettings,
    startImport,
    applyPreview,
    cancelImport,
  };
}
