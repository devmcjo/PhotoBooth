import { useEffect, useRef, useState } from "react";
import {
  cameraFailureMessageKey,
  formatCameraFailureCode,
  isCameraRetryable,
  type CameraFailure,
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
  const [state, setState] = useState<CameraState>(() => getCameraService().state());
  /**
   * 사유 + 상세를 **한 값으로** 들고 있는다. 사유와 코드를 따로 폴링하면 서로 다른 시점의
   * `lastFailure`를 읽어 문구와 코드가 어긋난다.
   */
  const [failure, setFailure] = useState<CameraFailure | null>(() => getCameraService().failure());
  const [size, setSize] = useState<ProcessedSize | null>(null);
  /**
   * 캔버스 세대. **카메라가 새로 열릴 때마다 증가**해 `<canvas>` DOM 노드를 갈아 끼운다.
   *
   * ⚠️ 이것이 없으면 [다시 시도] 이후 화면이 **영구히 검은색**이 된다(2026-08-06 수정).
   *    `transferControlToOffscreen()`은 캔버스당 1회뿐이고, 한 번 이관된 캔버스는 메인에서
   *    `getContext("2d")`조차 실패한다 — 즉 새 가공기는 이관도 비트맵 폴백도 쓸 수 없다.
   *    유일한 복구는 **새 캔버스 노드**다.
   */
  const [generation, setGeneration] = useState(0);
  /** 이 세대를 이미 붙였는가. 세대당 1회만 시도한다. */
  const boundGenerationRef = useRef(-1);

  useEffect(() => {
    const camera = getCameraService();
    setState(camera.state());
    setFailure(camera.failure());
    const offState = camera.onState((next) => {
      setState(next);
      // 사유는 상태와 **같은 통지**에서 읽는다 — 따로 폴링하면 두 값이 어긋난다.
      setFailure(camera.failure());
      // 새 가공기가 생기는 전이는 `Starting` 진입뿐이다(stop → start · 재시도 · 장치 변경).
      if (next === "Starting") setGeneration((current) => current + 1);
    });
    const offFrame = camera.onProcessedFrame(setSize);
    return () => {
      offState();
      offFrame();
    };
  }, []);

  /**
   * 프리뷰 연결. **방식은 가공기가 정한다**(이관 → 비트맵 → 직접 렌더).
   * 여기서는 "언제 붙일지"만 책임진다.
   */
  useEffect(() => {
    if (boundGenerationRef.current === generation) return;
    const canvas = canvasRef.current;
    if (canvas === null) return;
    if (state !== "Starting" && state !== "Ready") return;
    if (getCameraService().bindPreview(canvas)) boundGenerationRef.current = generation;
  }, [state, generation]);

  return (
    <div className={styles.stage}>
      <canvas
        // 세대가 바뀌면 React가 노드를 새로 만든다 — 이관된 캔버스를 재사용하지 않기 위함이다.
        key={generation}
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
            {failedMessage ??
              STRINGS.camera.errors[cameraFailureMessageKey(failure?.reason ?? "unknown")]}
          </p>
          {onRetry !== undefined && isCameraRetryable(failure?.reason ?? "unknown") && (
            <Button variant="primary" onClick={onRetry}>
              {STRINGS.camera.retry}
            </Button>
          )}
          {/*
            오류 코드 캡션 — 손님에게는 의미 없는 문자열이지만, 진단 모달이 로그인 전용이고
            로그가 기기 IndexedDB에만 쌓이는 지금 **현장 운영자·테스터가 원인을 보고할 수 있는
            유일한 창구**다. 값은 `DETAIL_PATTERN`을 통과한 것뿐이라 게이트 키·토큰·email·
            기기 label·예외 메시지가 원리적으로 섞일 수 없다.
            ⚠️ JSX 텍스트 노드만 쓴다 — `innerHTML`/`dangerouslySetInnerHTML` 금지.
          */}
          {failure !== null && (
            <p className={styles.failureCode}>
              {STRINGS.camera.failureCodeLabel} {formatCameraFailureCode(failure)}
            </p>
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
