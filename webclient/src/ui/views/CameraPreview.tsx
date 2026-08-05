import { useEffect, useRef, useState } from "react";
import {
  cameraFailureMessageKey,
  isCameraRetryable,
  type CameraFailureReason,
} from "@domain/capture/cameraFailure";
import { getCameraService } from "@adapters/camera/cameraService";
import type { CameraState, ProcessedSize } from "@adapters/camera/cameraTypes";
import { Button, Spinner } from "@ui/components";
import { STRINGS } from "@ui/strings";
import styles from "./cameraPreview.module.css";

/**
 * 프리뷰 — **가공 결과 canvas**를 그린다 (04 §4.2 · WM1)
 *
 * ⚠️ `<video>`를 직접 보여주지 않는다. `<video>`는 숨겨져 있고 거울·크롭이 적용되지 않은
 *    원본이다 — 그것을 보여주면 손님이 본 구도와 저장 결과가 어긋난다.
 * ⚠️ **CSS `transform: scaleX(-1)`을 쓰지 않는다.** 반전은 Worker의 픽셀 파이프라인이 한다.
 * ⚠️ 플래시·카운트다운은 **DOM 오버레이**로 겹친다(canvas에 그리면 합성 픽셀에 섞인다).
 */

export interface CameraPreviewProps {
  /** 프리뷰 위에 겹칠 오버레이(카운트다운·플래시 등). */
  readonly overlay?: React.ReactNode;
  /** 실패 시 표시할 문구. 기본은 **사유별 규격 문구**(03 §6.3). */
  readonly failedMessage?: string;
  /**
   * 재시도 진입점. 주면 **재시도 가능한 사유에서만** [다시 시도]가 렌더된다.
   * ⚠️ `permissionDenied`·`insecureContext`에는 나타나지 않는다 — 같은 조건에서 다시 눌러도
   *    반드시 실패해 손님을 헛돌게 한다(`isCameraRetryable`).
   */
  readonly onRetry?: () => void;
}

export function CameraPreview({ overlay, failedMessage, onRetry }: CameraPreviewProps) {
  const canvasRef = useRef<HTMLCanvasElement>(null);
  /** 제어권 이관은 캔버스당 **1회만** 가능하다 — 재이관 시도를 막는다. */
  const boundRef = useRef(false);
  const [state, setState] = useState<CameraState>(() => getCameraService().state());
  const [reason, setReason] = useState<CameraFailureReason | null>(() =>
    getCameraService().failureReason(),
  );
  const [size, setSize] = useState<ProcessedSize | null>(null);

  useEffect(() => {
    const camera = getCameraService();
    setState(camera.state());
    setReason(camera.failureReason());
    const offState = camera.onState((next) => {
      setState(next);
      // 사유는 상태와 **같은 통지**에서 읽는다 — 따로 폴링하면 두 값이 어긋난다.
      setReason(camera.failureReason());
    });
    const offFrame = camera.onProcessedFrame(setSize);
    return () => {
      offState();
      offFrame();
    };
  }, []);

  // Worker에 캔버스 제어권을 넘긴다(zero-copy). 실패하면 Worker가 비트맵을 보내는 경로로 동작한다.
  useEffect(() => {
    if (boundRef.current) return;
    const canvas = canvasRef.current;
    if (canvas === null) return;
    if (state !== "Starting" && state !== "Ready") return;
    boundRef.current = getCameraService().bindPreview(canvas);
  }, [state]);

  return (
    <div className={styles.stage}>
      <canvas
        ref={canvasRef}
        className={styles.canvas}
        // 실제 픽셀 크기는 Worker가 정한다. 여기 값은 첫 프레임 전 레이아웃 안정용이다.
        width={size?.width ?? 1080}
        height={size?.height ?? 1440}
        aria-label="카메라 프리뷰"
        role="img"
      />

      {state === "Starting" && (
        <div className={styles.overlay}>
          <Spinner label={STRINGS.camera.notReady} />
          <p className={styles.overlayText}>{STRINGS.camera.notReady}</p>
        </div>
      )}

      {state === "Failed" && (
        <div className={styles.overlay} role="alert">
          <p className={styles.overlayText}>
            {failedMessage ?? STRINGS.camera.errors[cameraFailureMessageKey(reason ?? "unknown")]}
          </p>
          {onRetry !== undefined && isCameraRetryable(reason ?? "unknown") && (
            <Button variant="primary" onClick={onRetry}>
              {STRINGS.camera.retry}
            </Button>
          )}
        </div>
      )}

      {overlay}
    </div>
  );
}

/** 실제 획득값 표시(WC2 대응 — 요청값과 다를 수 있음을 정직하게 보인다). */
export function CameraStatsCaption() {
  const [text, setText] = useState("");

  useEffect(() => {
    const camera = getCameraService();
    function refresh(): void {
      const settings = camera.settings();
      const processed = camera.processedSize();
      if (settings === null) {
        setText("");
        return;
      }
      const parts = [
        `${settings.width}×${settings.height}`,
        settings.frameRate === null ? "fps 미보고" : `${Math.round(settings.frameRate)}fps 요청`,
        `실측 ${camera.fps()}fps`,
      ];
      if (processed !== null) parts.push(`크롭 ${processed.width}×${processed.height}`);
      setText(parts.join(" · "));
    }

    refresh();
    const off = camera.onProcessedFrame(refresh);
    // 실측 fps는 프레임이 멈춰도 갱신돼야 한다(0으로 떨어지는 것을 보여준다).
    const timer = setInterval(refresh, 500);
    return () => {
      off();
      clearInterval(timer);
    };
  }, []);

  return text.length === 0 ? null : <p className={styles.stats}>{text}</p>;
}
