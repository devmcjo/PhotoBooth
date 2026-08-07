import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { isSessionActive } from "@domain/navigation/stateMachine";
import { getCameraService } from "@adapters/camera/cameraService";
import { readCameraPermission } from "@adapters/camera/cameraPermission";
import { listCameras } from "@adapters/camera/deviceEnumerator";
import { getTimelapseService } from "@adapters/encode/timelapseService";
import { createHealthService } from "@adapters/http/healthService";
import { isStandaloneDisplay } from "@adapters/platform/appInstall";
import { copyText } from "@adapters/platform/clipboard";
import { readStorageStatus } from "@adapters/platform/persistStorage";
import { defaultLogExportDeps, exportLogs } from "@adapters/storage/exportImport";
import { getFrameStore } from "@adapters/storage/frameStore";
import { getLogStore } from "@adapters/storage/logStore";
import { getOpfsClient } from "@adapters/storage/opfsClient";
import { OPFS_DIRS } from "@adapters/storage/opfsProtocol";
import { getResultsStore } from "@adapters/storage/resultsStore";
import { loginStore } from "@shell/loginStore";
import { sessionStore } from "@shell/sessionStore";
import { shellStore, useShellStore } from "@shell/shellStore";
import {
  applyWaitingUpdate,
  checkForUpdate,
  useSwState,
} from "@shell/swUpdate";
import { Button, Modal, Spinner } from "@ui/components";
import { STRINGS } from "@ui/strings";
import { env } from "../../../env";
import {
  collectDiagnostics,
  type DiagnosticsDeps,
  type DiagnosticsSnapshot,
} from "./diagnosticsPresenter";
import styles from "./diagnostics.module.css";

/**
 * 진단·상태 모달 — 03 §15.2
 *
 * ⚠️ **카메라를 열지 않는다.** 상태를 읽기만 한다.
 * ⚠️ **저장소 권한 창을 띄우지 않는다** — `readStorageStatus`(요청하지 않는 조회)를 쓴다.
 * ⚠️ 로그 내보내기 실패는 **모달을 닫지 않는다**. 토스트만 낸다.
 * ⚠️ 언마운트에서 `AbortController.abort()` — 결과는 폐기한다.
 */

/*
 * ⚠️ 카메라 권한 조회는 **`adapters/camera/cameraPermission.ts`로 옮겼다**(2026-08-01).
 *    Guide 화면도 같은 조회를 쓰는데, 복사본이 생기면 폴백 규칙(Safari 미지원·Firefox throw →
 *    `null`)이 두 벌로 갈라진다. 진단은 **조회만** 한다 — 여는 것만으로 LED가 켜지면 안 되므로
 *    `requestCameraPermission`을 부르지 않는다.
 */

function formatTimestamp(ms: number): string {
  const date = new Date(ms);
  if (!Number.isFinite(date.getTime())) return STRINGS.account.unknown;
  const pad = (value: number): string => String(value).padStart(2, "0");
  return (
    `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())} ` +
    `${pad(date.getHours())}:${pad(date.getMinutes())}`
  );
}

export function DiagnosticsModal() {
  const sw = useSwState();
  const screen = useShellStore((s) => s.screen);
  const [snapshot, setSnapshot] = useState<DiagnosticsSnapshot | null>(null);
  const [loading, setLoading] = useState(true);
  const [runId, setRunId] = useState(0);
  const mountedRef = useRef(true);

  useEffect(() => {
    mountedRef.current = true;
    return () => {
      mountedRef.current = false;
    };
  }, []);

  // ⚠️ `deps`는 매 수집마다 새로 만든다 — 싱글턴을 **호출 시점**에 해석한다(모듈 로드 부작용 0).
  const buildDeps = useCallback(
    (swStatus: typeof sw.status): DiagnosticsDeps => ({
      listCameras: () => listCameras(),
      cameraState: () => getCameraService().state(),
      cameraSettings: () => getCameraService().settings(),
      processedSize: () => getCameraService().processedSize(),
      cameraFps: () => getCameraService().fps(),
      cameraPermission: readCameraPermission,
      cameraFailure: () => getCameraService().failure(),
      pipelineMode: () => getCameraService().pipelineMode(),
      previewMode: () => getCameraService().previewMode(),
      frameTransferMode: () => getCameraService().frameTransferMode(),
      constraintStep: () => getCameraService().constraintStep(),
      lastLoginFailure: () => loginStore.getState().lastFailure,
      encoderProbe: () => getTimelapseService().encoderProbe(),
      serverProbe: () => createHealthService().probe(),
      storageBucket: env.storageBucket,
      accountId: sessionStore.getState().currentUser?.id ?? null,
      logStats: async () => (await getLogStore()?.stats()) ?? null,
      storageStatus: () =>
        readStorageStatus(typeof navigator === "undefined" ? undefined : navigator.storage),
      sessionLeftovers: async () => (await getOpfsClient().list(OPFS_DIRS.sessions)).length,
      storedResults: async () => {
        const usage = await getResultsStore().usage();
        return { totalBytes: usage.totalBytes, folderCount: usage.folders.length };
      },
      frameCacheBytes: () => getFrameStore().usageBytes(),
      appVersion: env.appVersion,
      buildDate: env.buildDate,
      swStatus,
      standalone: isStandaloneDisplay(),
      formatTimestamp,
    }),
    [],
  );

  /*
   * SW 상태는 **수집을 다시 돌리는 트리거가 아니다** — 스토어 구독으로 이미 즉시 리렌더되고,
   * 프로브를 다시 돌리면 모달을 열어둔 채 네트워크가 반복된다. 최신 값만 ref로 읽는다.
   */
  const swStatusRef = useRef(sw.status);
  swStatusRef.current = sw.status;

  useEffect(() => {
    const controller = new AbortController();
    setLoading(true);
    void collectDiagnostics(buildDeps(swStatusRef.current), controller.signal).then((next) => {
      if (next.cancelled || !mountedRef.current) return;
      setSnapshot(next);
      setLoading(false);
    });
    // 진행 중인 프로브는 결과 폐기로 취소한다(언마운트 후 setState 금지).
    return () => controller.abort();
  }, [runId, buildDeps]);

  const developerEmail = STRINGS.diagnostics.developerEmail;

  const copyEmail = useCallback((): void => {
    void copyText(developerEmail).then((ok) => {
      shellStore
        .getState()
        .toast(
          ok ? "success" : "error",
          ok ? STRINGS.diagnostics.copied : STRINGS.diagnostics.copyFailed,
        );
    });
  }, [developerEmail]);

  const doExportLogs = useCallback((): void => {
    void exportLogs(defaultLogExportDeps()).then((ok) => {
      shellStore
        .getState()
        .toast(
          ok ? "success" : "error",
          ok ? STRINGS.diagnostics.exportLogsDone : STRINGS.diagnostics.exportLogsFailed,
        );
    });
  }, []);

  const doCheckUpdate = useCallback((): void => {
    void checkForUpdate().then((found) => {
      shellStore
        .getState()
        .toast("info", found ? STRINGS.pwa.updateFound : STRINGS.pwa.upToDate);
    });
  }, []);

  const doApplyUpdate = useCallback((): void => {
    void applyWaitingUpdate().then((ok) => {
      if (!ok) shellStore.getState().toast("info", STRINGS.pwa.applyBlocked);
    });
  }, []);

  const sections = useMemo(() => snapshot?.sections ?? [], [snapshot]);

  return (
    <Modal
      id="diagnostics"
      title={STRINGS.diagnostics.title}
      dismissible
      actions={
        <>
          <Button onClick={() => setRunId((value) => value + 1)}>
            {STRINGS.diagnostics.recheck}
          </Button>
          <Button
            variant="ghost"
            onClick={() => shellStore.getState().popModal("diagnostics")}
          >
            {STRINGS.common.close}
          </Button>
        </>
      }
    >
      <div className={styles.body}>
        {loading && <Spinner />}

        {sections.map((section) => (
          <section key={section.id} className={styles.section}>
            <h3 className={styles.sectionTitle}>{section.title}</h3>
            <dl className={styles.rows}>
              {section.rows.map((row) => (
                <div key={`${section.id}-${row.label}`} style={{ display: "contents" }}>
                  <dt className={styles.key}>{row.label}</dt>
                  <dd className={[styles.value, styles[row.tone]].join(" ")}>{row.value}</dd>
                </div>
              ))}
            </dl>

            {section.id === "logStorage" && (
              <div className={styles.actions}>
                <Button onClick={() => doExportLogs()}>{STRINGS.diagnostics.exportLogs}</Button>
              </div>
            )}

            {section.id === "contact" && (
              <div className={styles.actions}>
                <Button onClick={() => copyEmail()}>{STRINGS.diagnostics.copy}</Button>
              </div>
            )}

            {section.id === "app" && (
              <div className={styles.actions}>
                <Button onClick={() => doCheckUpdate()}>{STRINGS.pwa.checkUpdate}</Button>
                {/* 촬영 중에는 **버튼 자체를 렌더하지 않는다**(액션 가드와 2중 — M10). */}
                {sw.status === "waiting" && !isSessionActive(screen) && (
                  <Button variant="primary" onClick={() => doApplyUpdate()}>
                    {STRINGS.pwa.applyNow}
                  </Button>
                )}
                {/* ⚠️ 상시 캡션이다 — 누르기 전에 결과(로그인 해제)를 알려야 한다. */}
                <p className={styles.caption}>{STRINGS.pwa.applyCaption}</p>
              </div>
            )}
          </section>
        ))}
      </div>
    </Modal>
  );
}
