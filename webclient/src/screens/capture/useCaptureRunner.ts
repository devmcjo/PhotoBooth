import { useCallback, useEffect, useRef, useState } from "react";
import { addCut, slotCount as slotCountOf } from "@domain/capture/captureSession";
import { slotAspectRatio } from "@domain/frames/types";
import { getCameraService } from "@adapters/camera/cameraService";
import { getTimelapseService } from "@adapters/encode/timelapseService";
import { playShutterSound } from "@adapters/platform/shutterSound";
import { cutFileName } from "@adapters/storage/sessionWorkspace";
import { logger } from "@adapters/storage/logStore";
import { createWorkspace, newSessionArgs } from "@shell/captureSessionController";
import { sessionStore, type CapturedCut } from "@shell/sessionStore";
import { useSettingsStore } from "@shell/settingsStore";
import { configureShell, shellStore } from "@shell/shellStore";
import { STRINGS } from "@ui/strings";
import {
  CaptureCancelledError,
  createCaptureSequence,
  type CaptureSequence,
} from "./captureSequence";

/**
 * `Capture` 화면 배선 — 03 §6.1의 진입 절차 7단계
 *
 * ```
 * 1 카메라 시작  2 Ready 대기(≤8s)  3 세션 작업 공간  4 타임랩스 수집(Step 9)
 * 5 컷 루프      6 수집 종료         7 → CutSelect
 * ```
 *
 * ⚠️ **Ready 이후에만 시퀀스를 시작한다.** 준비 전에 셔터를 누르면 빈 컷이 생긴다.
 * ⚠️ 취소·탭 hidden은 `shellStore.returnHome`이 훅으로 시퀀스를 끊는다(WM4).
 */

export interface CaptureRunner {
  readonly countdown: number;
  readonly flashing: boolean;
  readonly capturedCount: number;
  readonly canShootNow: boolean;
  shootNow(): void;
}

function delay(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

/** 썸네일 축소 — `resizeWidth` 미실효 브라우저를 위해 결과 폭을 확인한다(OA-10). */
async function makeThumbnail(blob: Blob): Promise<ImageBitmap | undefined> {
  try {
    const bitmap = await createImageBitmap(blob, { resizeWidth: 320, resizeQuality: "medium" });
    if (bitmap.width <= 400) return bitmap;
    // resize 옵션이 무시된 경우 — 원본이 그대로 왔다. 메모리를 아끼려 닫고 원본을 쓴다.
    bitmap.close();
    return await createImageBitmap(blob);
  } catch {
    return undefined;
  }
}

export function useCaptureRunner(): CaptureRunner {
  const values = useSettingsStore((s) => s.values);
  const [countdown, setCountdown] = useState(0);
  const [flashing, setFlashing] = useState(false);
  const [capturedCount, setCapturedCount] = useState(0);
  const [ready, setReady] = useState(false);
  const sequenceRef = useRef<CaptureSequence | null>(null);
  /** StrictMode 이중 마운트에서 두 번 촬영하지 않게 한다. */
  const startedRef = useRef(false);

  const shootNow = useCallback(() => {
    sequenceRef.current?.skipCountdown();
  }, []);

  useEffect(() => {
    if (startedRef.current) return;
    startedRef.current = true;

    const camera = getCameraService();
    const timelapse = getTimelapseService();
    const shell = shellStore.getState();
    const session = sessionStore.getState().session;
    const frame = session.frame;
    if (frame === null) {
      logger.error("촬영 진입에 프레임이 없다 — 홈으로 복귀");
      void shell.returnHome("프레임 미확정");
      return;
    }

    const firstSlot = frame.slots[0];
    const targetAspect = firstSlot === undefined ? 0.75 : slotAspectRatio(firstSlot);

    let disposed = false;

    async function run(): Promise<void> {
      // 1. 카메라 시작 — 실패하면 시퀀스를 시작하지 않는다.
      const started = await camera.start({
        deviceId: values.CameraDevice.length > 0 ? values.CameraDevice : null,
        targetAspect,
        mirror: values.MirrorMode,
      });
      if (!started || disposed) {
        if (!started) shell.toast("error", STRINGS.camera.failed);
        return;
      }

      // 2. Ready 대기(카메라 서비스가 8초 타임아웃을 강제한다).
      const isReady = await waitForReady(camera);
      if (disposed) return;
      if (!isReady) {
        shell.toast("error", STRINGS.camera.failed);
        return;
      }
      setReady(true);

      // 3. 세션 작업 공간(OPFS sessions/{id}/).
      const args = newSessionArgs();
      const workspace = createWorkspace(args.now, args.uuid);

      // 4. 타임랩스 프레임 수집 시작(WD2 — 녹화가 아니라 샘플링).
      //    실경과는 여기부터 `stopCollection()`까지로 잰다.
      timelapse.startCollection(workspace);

      // 5. 컷 루프
      const sequence = createCaptureSequence({
        captureStill: () => camera.captureStill(),
        writeCut: (index, bytes) => workspace.writeCut(index, bytes),
        cutFileName,
        makeThumbnail,
        now: () => performance.now(),
        delay,
        onCountdown: setCountdown,
        onFlash: setFlashing,
        onCutCaptured: (cut) => {
          const current = sessionStore.getState().session;
          const captured: CapturedCut = {
            index: cut.index,
            fileName: cut.fileName,
            ...(cut.thumbnail === undefined ? {} : { thumbnail: cut.thumbnail }),
          };
          sessionStore.getState().setSession(addCut(current, captured));
          setCapturedCount(sessionStore.getState().session.cuts.length);
        },
        playShutter: playShutterSound,
      });
      sequenceRef.current = sequence;

      // 이탈·탭 hidden·유휴 만료에서 시퀀스를 먼저 끊는다(02 §2.5 1단계).
      configureShell({
        // ⚠️ 여기서도 수집을 멈춘다. `returnHome`은 `cancelCaptureSequence` →
        //    `cleanupWorkspace`(폴더 삭제) → `stopEncoder` 순이라(02 §2.5),
        //    `stopEncoder`에서만 끊으면 **삭제 후 도착한 스풀 쓰기가 `tl/`을 되살린다**.
        cancelCaptureSequence: () => {
          sequence.cancel();
          timelapse.stopCollection();
        },
        stopEncoder: () => timelapse.stop(),
        stopCamera: () => camera.stop(),
      });

      try {
        const cuts = await sequence.run(session.cutCount, {
          countdownSec: values.CountdownSec,
          flash: values.FlashMode,
          shutterSound: values.ShutterSound,
        });
        // 6. 수집 종료 — **마지막 컷 직후**에 실경과를 확정한다.
        timelapse.stopCollection();
        if (disposed) return;

        // 7. 컷 선택
        if (cuts.length < slotCountOf(sessionStore.getState().session)) {
          // 슬롯 수만큼 고를 수 없으면 진행해도 합성이 불가능하다(M12).
          logger.error("촬영된 컷이 슬롯 수보다 적다", { cuts: cuts.length });
          shell.toast("error", "촬영에 실패한 컷이 있어 다시 시도해 주세요.");
          void shell.returnHome("컷 부족");
          return;
        }
        shell.go("CutSelect");
      } catch (err) {
        if (err instanceof CaptureCancelledError) {
          logger.info("촬영 시퀀스 취소됨");
          return;
        }
        throw err;
      } finally {
        // 예외·취소 경로에서도 수집을 반드시 끊는다(멱등이라 정상 경로의 중복 호출은 무해하다).
        timelapse.stopCollection();
      }
    }

    void run();

    return () => {
      disposed = true;
      sequenceRef.current?.cancel();
      sequenceRef.current = null;
      startedRef.current = false;
      // 카메라를 놓기 **전에** 수집을 끊는다. 순서가 반대면 스풀 프레임이 카메라 정지 뒤에
      // 도착해 실경과가 늘어난다(결과 mp4가 실제보다 느려진다).
      timelapse.stopCollection();
      // 화면을 벗어나면 카메라를 놓는다(다음 화면이 다시 빌린다).
      camera.stop();
    };
    // 설정은 진입 시점 값으로 고정한다 — 촬영 중 설정이 바뀌어도 세션은 흔들리지 않는다.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  return {
    countdown,
    flashing,
    capturedCount,
    canShootNow: ready,
    shootNow,
  };
}

/** 카메라가 Ready 또는 Failed로 확정될 때까지 기다린다(타임아웃은 서비스가 강제한다). */
function waitForReady(camera: ReturnType<typeof getCameraService>): Promise<boolean> {
  if (camera.state() === "Ready") return Promise.resolve(true);
  if (camera.state() === "Failed") return Promise.resolve(false);

  return new Promise<boolean>((resolve) => {
    const off = camera.onState((state) => {
      if (state === "Ready") {
        off();
        resolve(true);
      } else if (state === "Failed" || state === "Idle") {
        off();
        resolve(false);
      }
    });
  });
}
