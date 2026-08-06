import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { getCameraService } from "@adapters/camera/cameraService";
import {
  listCameras,
  displayLabel,
  resolveStartDeviceId,
  type CameraDevice,
} from "@adapters/camera/deviceEnumerator";
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

  /** WC3 폴백 매칭 입력(3개 키를 한 객체로). 실촬영과 **같은 해석**을 쓴다. */
  const storedRef = useMemo(
    () => ({
      deviceId: values.CameraDevice,
      label: webExtras.CameraDeviceLabel,
      groupId: webExtras.CameraDeviceGroupId,
    }),
    [values.CameraDevice, webExtras.CameraDeviceLabel, webExtras.CameraDeviceGroupId],
  );

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

    // ⚠️ 열거 **뒤에** 연다. 저장된 deviceId를 그대로 쓰면 WC3 폴백이 돌지 않아 실촬영과
    //    다른 장치가 열릴 수 있고, 그러면 "동일 재현"이라는 이 모달의 목적이 무너진다.
    void listCameras().then((found) => {
      if (cancelled) return;
      setDevices(found);
      if (openedRef.current) return;
      openedRef.current = true;
      void presenter.open({
        deviceId: resolveStartDeviceId(found, storedRef).deviceId,
        facing: webExtras.CameraFacing,
        mirror: values.MirrorMode,
        flash: values.FlashMode,
      });
    });

    return () => {
      cancelled = true;
      // 닫힐 때 반드시 정지한다(LED가 켜진 채 남는 것을 막는다).
      presenter.close();
      openedRef.current = false;
    };
  }, [presenter, storedRef, values.MirrorMode, values.FlashMode, webExtras.CameraFacing]);

  async function onShutter(): Promise<void> {
    const ok = await presenter.shoot(showFlash);
    // 성공이든 실패든 결과물은 남지 않는다 — 그 사실을 문구로 밝힌다.
    setNotice(ok ? STRINGS.camera.testNotSaved : STRINGS.camera.failed);
  }

  async function onSwitchDevice(deviceId: string): Promise<void> {
    setNotice(null);
    await presenter.open({
      deviceId,
      facing: webExtras.CameraFacing,
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
              // 재시도도 같은 해석을 탄다 — 장치가 그새 바뀌었으면 폴백이 다시 건져낸다.
              deviceId: resolveStartDeviceId(devices, storedRef).deviceId,
              facing: webExtras.CameraFacing,
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
