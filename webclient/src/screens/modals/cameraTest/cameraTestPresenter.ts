import { getCameraService, type CameraService } from "@adapters/camera/cameraService";
import { logger } from "@adapters/storage/logStore";
import { FLASH_DURATION_MS } from "@domain/capture/captureTiming";
import { slotAspectToRatio, DEFAULT_SLOT_ASPECT } from "@domain/frames/slotAspect";

/**
 * 카메라 테스트 모달 로직 — 03 §15.1 · analysis/13 §9.1
 *
 * 목적: **실촬영과 동일한** 프리뷰·플래시·셔터를 재현하고 **저장하지 않는다.**
 *
 * ⚠️ 절차 순서가 규격이다: 모달을 **먼저 표시**(로딩) → `stop()` → `start()` → Ready 대기.
 *    `start()`는 멱등이라 이미 열려 있으면 무시되므로, **지정 장치로 바꾸려면 먼저 정지**해야 한다.
 * ⚠️ 닫을 때 **카메라를 확실히 정지**한다 — 빠뜨리면 LED가 켜진 채 남는다.
 */

// 실촬영과 **같은 상수**를 쓴다 — 테스트 모달의 목적이 동일 재현이므로 값이 갈라지면 안 된다.
export { FLASH_DURATION_MS };

export interface CameraTestOptions {
  readonly deviceId: string | null;
  readonly mirror: boolean;
  readonly flash: boolean;
  /** 대표 슬롯 종횡비. 프레임 미선택 상태에서는 기본값(3:4)을 쓴다. */
  readonly targetAspect?: number;
}

export interface CameraTestPresenter {
  /** 모달 진입. 정지 후 지정 장치로 재시작하고 Ready를 기다린다. */
  open(options: CameraTestOptions): Promise<boolean>;
  /**
   * 셔터. 플래시를 재현하고 스틸을 만든 뒤 **즉시 버린다**.
   * @returns 캡처가 성공했는가(결과물은 돌려주지 않는다 — 저장 경로를 만들지 않기 위함)
   */
  shoot(showFlash: (durationMs: number) => Promise<void>): Promise<boolean>;
  /** 모달 종료. 카메라를 확실히 정지한다. */
  close(): void;
}

export function createCameraTestPresenter(
  camera: CameraService = getCameraService(),
): CameraTestPresenter {
  let flashEnabled = false;

  return {
    async open(options) {
      flashEnabled = options.flash;

      // 이미 다른 장치로 열려 있을 수 있다 — start()는 멱등이라 무시되므로 먼저 정지한다.
      camera.stop();

      const started = await camera.start({
        deviceId: options.deviceId,
        targetAspect: options.targetAspect ?? slotAspectToRatio(DEFAULT_SLOT_ASPECT),
        mirror: options.mirror,
      });
      if (!started) {
        logger.warn("카메라 테스트: 시작 실패");
        return false;
      }
      logger.info("카메라 테스트 시작", { deviceId: options.deviceId, mirror: options.mirror });
      return true;
    },

    async shoot(showFlash) {
      // 플래시는 설정이 on일 때만 재현한다(실촬영과 동일).
      if (flashEnabled) await showFlash(FLASH_DURATION_MS);

      const blob = await camera.captureStill();
      if (blob === null) {
        logger.warn("카메라 테스트: 셔터 실패");
        return false;
      }
      // **결과를 버린다.** 테스트 모달은 저장 경로를 갖지 않는다(03 §15.1).
      logger.info("카메라 테스트 셔터(결과 폐기)", { bytes: blob.size });
      return true;
    },

    close() {
      camera.stop();
      logger.info("카메라 테스트 종료(카메라 정지)");
    },
  };
}
