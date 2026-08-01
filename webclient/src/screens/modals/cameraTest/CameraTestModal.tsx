import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { getCameraService } from "@adapters/camera/cameraService";
import { listCameras, displayLabel, type CameraDevice } from "@adapters/camera/deviceEnumerator";
import { useSettingsStore } from "@shell/settingsStore";
import { shellStore } from "@shell/shellStore";
import { Button, Modal } from "@ui/components";
import { STRINGS } from "@ui/strings";
import { CameraPreview, CameraStatsCaption } from "@ui/views/CameraPreview";
import { createCameraTestPresenter, FLASH_DURATION_MS } from "./cameraTestPresenter";
import styles from "./cameraTest.module.css";

/**
 * 카메라 테스트 모달 — 03 §15.1
 *
 * 모달을 **먼저 표시**하고(로딩 오버레이는 `CameraPreview`가 담당) 그 뒤에 카메라를 시작한다.
 * 순서를 뒤집으면 권한 프롬프트가 뜨는 동안 화면이 비어 사용자가 무슨 일인지 알 수 없다.
 */
export function CameraTestModal() {
  const values = useSettingsStore((s) => s.values);
  const webExtras = useSettingsStore((s) => s.webExtras);
  const presenter = useMemo(() => createCameraTestPresenter(), []);

  const [devices, setDevices] = useState<CameraDevice[]>([]);
  const [flashing, setFlashing] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);
  /** StrictMode의 이중 마운트에서 카메라를 두 번 열지 않게 한다. */
  const openedRef = useRef(false);

  const showFlash = useCallback((durationMs: number): Promise<void> => {
    setFlashing(true);
    return new Promise<void>((resolve) => {
      setTimeout(() => {
        setFlashing(false);
        resolve();
      }, durationMs);
    });
  }, []);

  useEffect(() => {
    let cancelled = false;

    void listCameras().then((found) => {
      if (!cancelled) setDevices(found);
    });

    if (!openedRef.current) {
      openedRef.current = true;
      void presenter.open({
        deviceId: values.CameraDevice.length > 0 ? values.CameraDevice : null,
        mirror: values.MirrorMode,
        flash: values.FlashMode,
      });
    }

    return () => {
      cancelled = true;
      // 닫힐 때 반드시 정지한다(LED가 켜진 채 남는 것을 막는다).
      presenter.close();
      openedRef.current = false;
    };
  }, [presenter, values.CameraDevice, values.MirrorMode, values.FlashMode]);

  async function onShutter(): Promise<void> {
    const ok = await presenter.shoot(showFlash);
    // 성공이든 실패든 결과물은 남지 않는다 — 그 사실을 문구로 밝힌다.
    setNotice(ok ? STRINGS.camera.testNotSaved : STRINGS.camera.failed);
  }

  async function onSwitchDevice(deviceId: string): Promise<void> {
    setNotice(null);
    await presenter.open({
      deviceId,
      mirror: values.MirrorMode,
      flash: values.FlashMode,
    });
  }

  return (
    <Modal
      id="cameraTest"
      title="카메라 테스트"
      actions={
        <>
          <Button onClick={() => shellStore.getState().popModal("cameraTest")}>
            {STRINGS.common.close}
          </Button>
          <Button variant="primary" onClick={() => void onShutter()}>
            셔터
          </Button>
        </>
      }
    >
      <div className={styles.previewWrap}>
        <CameraPreview
          overlay={
            // 플래시는 DOM 오버레이다 — canvas에 그리면 합성 픽셀에 섞인다(04 §4.2).
            flashing ? <div className={styles.flash} aria-hidden="true" /> : undefined
          }
          // 장치 부재·점유 실패는 조건이 바뀌면 성공할 수 있다 → 같은 인자로 다시 연다.
          onRetry={() =>
            void presenter.open({
              deviceId: values.CameraDevice.length > 0 ? values.CameraDevice : null,
              mirror: values.MirrorMode,
              flash: values.FlashMode,
            })
          }
        />
      </div>

      <CameraStatsCaption />
      <p className={styles.mirrorNote}>
        거울모드 {values.MirrorMode ? "적용" : "해제"} · 플래시{" "}
        {values.FlashMode ? `${FLASH_DURATION_MS}ms` : "off"} · 전면/후면 {webExtras.CameraFacing}
      </p>

      {notice !== null && (
        <p className={styles.notice} role="status">
          {notice}
        </p>
      )}

      {devices.length > 1 && (
        <div className={styles.deviceList}>
          {devices.map((device, index) => (
            <Button
              key={device.deviceId.length > 0 ? device.deviceId : String(index)}
              variant={device.deviceId === getCameraService().settings()?.deviceId ? "primary" : "secondary"}
              onClick={() => void onSwitchDevice(device.deviceId)}
            >
              {displayLabel(device, index)}
            </Button>
          ))}
        </div>
      )}
    </Modal>
  );
}
