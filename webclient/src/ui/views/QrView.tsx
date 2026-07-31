import { useEffect, useRef, useState } from "react";
import { exportFileName } from "@domain/upload/exportFileName";
import { canExportFile, exportBlob } from "@adapters/platform/fileExport";
import { createQrMatrix, drawQrToCanvas, QR_TARGET_PX } from "@adapters/qr/qrService";
import { getTimelapseService } from "@adapters/encode/timelapseService";
import { useUploadRun } from "@screens/qr/useUploadRun";
import { uploadFailureMessage, uploadStageLabel } from "@screens/qr/uploadRunner";
import { useSessionStore } from "@shell/sessionStore";
import { useSettingsStore } from "@shell/settingsStore";
import { shellStore } from "@shell/shellStore";
import { Button, Spinner } from "@ui/components";
import { formatCount, STRINGS } from "@ui/strings";
import styles from "./screens.module.css";

/**
 * `Qr` 화면 — 업로드 3단계 + QR 표시 (03 §9)
 *
 * 진입 전제: `Result`의 effective QR 판정이 `true`다 → **로그인 상태이고 TempUser 한도 초과가
 * 아니다**(VF-11). 이 화면 안에서 로그인 여부를 다시 분기하지 않는다.
 *
 * ⚠️ **QR은 업로드 성공 후에만 렌더한다**(M5).
 * ⚠️ 실패해도 [완료]로 진행할 수 있어야 한다 — 결과물은 이미 로컬에 있다(M6-W).
 */

/** 업로드 성공 시에만 마운트된다. `text`가 바뀌면 다시 그린다. */
function QrCanvas({ text }: { readonly text: string }) {
  const canvasRef = useRef<HTMLCanvasElement>(null);
  const [failed, setFailed] = useState(false);

  useEffect(() => {
    const canvas = canvasRef.current;
    if (canvas === null) return;

    const matrix = createQrMatrix(text);
    const drawn = matrix !== null && drawQrToCanvas(canvas, matrix, QR_TARGET_PX);
    setFailed(!drawn);
  }, [text]);

  return (
    <>
      {/* ⚠️ `text`(다운로드 페이지 URL)를 화면에 문자로 노출하지 않는다 — 옆 사람이 읽어 갈 수 있다. */}
      <canvas
        ref={canvasRef}
        className={styles.qrCanvas}
        role="img"
        aria-label={STRINGS.upload.qrAltText}
        hidden={failed}
      />
      {failed && (
        <p className={styles.note} role="alert">
          {STRINGS.upload.qrRenderFailed}
        </p>
      )}
    </>
  );
}

export function QrView() {
  const run = useUploadRun();
  const settings = useSettingsStore((s) => s.values);
  const finalImage = useSessionStore((s) => s.finalImage);
  const sessionId = useSessionStore((s) => s.sessionId);

  // 타임랩스는 싱글턴 서비스가 들고 있다(홈 복귀까지 살아 있다 — 04 §7.2).
  const timelapse = getTimelapseService().current();
  const exportable = canExportFile();

  function finish(): void {
    run.cancel();
    shellStore.getState().go("Done");
  }

  function saveToDevice(kind: "final" | "timelapse"): void {
    const blob = kind === "final" ? (finalImage?.blob ?? null) : (timelapse?.blob ?? null);
    if (blob === null) return; // 커맨드 가드(버튼 비활성과 별개 — M10)

    const name = exportFileName(sessionId, kind, finalImage?.format ?? settings.OutputFormat);
    if (!exportBlob(blob, name)) {
      shellStore.getState().toast("error", STRINGS.save.failed);
    }
  }

  const { phase } = run;

  return (
    <main className={styles.screen}>
      <div className={styles.qrStage} aria-live="polite">
        {(phase.kind === "idle" || phase.kind === "uploading") && (
          <>
            <Spinner
              label={
                phase.kind === "uploading"
                  ? uploadStageLabel(phase.stage)
                  : STRINGS.upload.inProgress
              }
            />
            {/* 진행률이 null이면 `value`를 주지 않는다 = 불확정 표시(06 §4.5). */}
            <progress
              className={styles.uploadProgress}
              max={1}
              {...(phase.kind === "uploading" && phase.progress !== null
                ? { value: phase.progress }
                : {})}
            />
          </>
        )}

        {phase.kind === "nothing" && (
          <p className={styles.note} role="alert">
            {STRINGS.upload.nothingToSend}
          </p>
        )}

        {phase.kind === "succeeded" && (
          <>
            <QrCanvas text={phase.downloadPageUrl} />
            <p className={styles.subtitle}>
              {formatCount(STRINGS.upload.retentionNotice, phase.retentionHours)}
            </p>
          </>
        )}

        {phase.kind === "failed" && (
          <p className={styles.note} role="alert">
            {uploadFailureMessage(phase.reason, settings.SaveLocalCopy)}
          </p>
        )}
      </div>

      {/* 파일이 2개면 버튼도 2개다 — 다중 자동 다운로드는 브라우저가 차단한다(03 §9.3). */}
      {exportable && (
        <div className={styles.actions}>
          {finalImage !== null && (
            <Button onClick={() => saveToDevice("final")}>
              {STRINGS.upload.saveToDevicePhoto}
            </Button>
          )}
          {timelapse !== null && (
            <Button onClick={() => saveToDevice("timelapse")}>
              {STRINGS.upload.saveToDeviceVideo}
            </Button>
          )}
        </div>
      )}

      <div className={styles.actions}>
        {phase.kind === "failed" && (
          <Button onClick={() => run.retry()}>{STRINGS.common.retry}</Button>
        )}
        <Button variant="primary" onClick={() => finish()}>
          {STRINGS.common.done}
        </Button>
      </div>
    </main>
  );
}
