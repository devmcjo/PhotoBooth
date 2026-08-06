import { logger } from "@adapters/storage/logStore";

/**
 * 카메라 장치 열거·매칭 — WC3
 *
 * ⚠️ **`deviceId`는 안정적이지 않다.** 브라우저·OS 재시작·권한 재부여로 바뀔 수 있고,
 *    시크릿 모드에서는 매번 다르다. 그래서 `{deviceId, label, groupId}` 3개를 저장하고
 *    **deviceId → label → groupId → 첫 장치** 순으로 폴백한다.
 *    이 폴백이 없으면 외장 웹캠 환경에서 "저장한 카메라가 사라졌다"가 반복된다.
 */

export interface CameraDevice {
  readonly deviceId: string;
  readonly label: string;
  readonly groupId: string;
}

/** 저장된 장치 식별 정보(설정의 `CameraDevice` + `webExtras`). */
export interface StoredDeviceRef {
  readonly deviceId: string;
  readonly label: string;
  readonly groupId: string;
}

export type DeviceMatchReason = "deviceId" | "label" | "groupId" | "first" | "none";

export interface DeviceMatch {
  readonly device: CameraDevice | null;
  readonly reason: DeviceMatchReason;
}

/**
 * 저장된 참조로 장치를 찾는다. **순서가 규격이다.**
 * 라벨은 권한 부여 전에는 빈 문자열이므로 **빈 값으로는 매칭하지 않는다**
 * (빈 라벨끼리 일치해 엉뚱한 카메라가 잡히는 것을 막는다).
 */
export function matchDevice(
  devices: readonly CameraDevice[],
  stored: StoredDeviceRef | null,
): DeviceMatch {
  if (devices.length === 0) return { device: null, reason: "none" };

  if (stored !== null) {
    if (stored.deviceId.length > 0) {
      const byId = devices.find((d) => d.deviceId === stored.deviceId);
      if (byId !== undefined) return { device: byId, reason: "deviceId" };
    }
    if (stored.label.length > 0) {
      const byLabel = devices.find((d) => d.label === stored.label);
      if (byLabel !== undefined) return { device: byLabel, reason: "label" };
    }
    if (stored.groupId.length > 0) {
      const byGroup = devices.find((d) => d.groupId === stored.groupId);
      if (byGroup !== undefined) return { device: byGroup, reason: "groupId" };
    }
  }

  return { device: devices[0]!, reason: "first" };
}

/** 저장된 참조가 하나라도 값을 갖는가. 전부 비었으면 "저장한 적 없음"이다. */
export function hasStoredDevice(stored: StoredDeviceRef | null): boolean {
  if (stored === null) return false;
  return (
    stored.deviceId.length > 0 || stored.label.length > 0 || stored.groupId.length > 0
  );
}

export interface StartDeviceResolution {
  /** `camera.start({deviceId})`에 넘길 값. `null`이면 `facingMode` 경로로 간다. */
  readonly deviceId: string | null;
  readonly reason: DeviceMatchReason;
}

/**
 * 촬영·테스트가 실제로 열 장치를 정한다 — **WC3 폴백을 실제로 쓰는 유일한 진입점**.
 *
 * ⚠️ 2026-08-06까지 `matchDevice`의 호출처가 **프로덕션에 없었다**(테스트만). 화면들이 저장된
 *    `CameraDevice` 문자열을 그대로 `start()`에 넘겼기 때문에, `deviceId → label → groupId`
 *    폴백이 한 번도 돌지 않았다. deviceId는 브라우저·OS 재시작·권한 재부여로 바뀌므로
 *    **모바일에서는 사실상 매번 무효**가 되고, 그러면 "저장한 카메라가 사라졌다"가 반복된다.
 *    함께 기록해 둔 `CameraDeviceLabel`·`CameraDeviceGroupId`도 죽은 값이었다.
 *
 * ⚠️ **`first`·`none`은 `null`로 접는다.** `matchDevice`는 장치가 있으면 항상 무언가를
 *    돌려주지만(첫 장치 폴백), 저장한 장치가 사라진 상황에서 "첫 장치"는 임의값이다.
 *    그때는 `facingMode`(전/후면 설정)가 더 정확한 의도 표현이고 — 모바일에서 첫 장치가
 *    후면인 기기가 많아, `first`를 강제하면 전면 설정을 조용히 뒤집는다.
 */
export function resolveStartDeviceId(
  devices: readonly CameraDevice[],
  stored: StoredDeviceRef | null,
): StartDeviceResolution {
  if (!hasStoredDevice(stored)) return { deviceId: null, reason: "none" };

  const match = matchDevice(devices, stored);
  if (match.reason === "first" || match.reason === "none" || match.device === null) {
    return { deviceId: null, reason: match.reason };
  }
  return { deviceId: match.device.deviceId, reason: match.reason };
}

/**
 * 라벨 표시 폴백. 권한 부여 전 `enumerateDevices`는 **라벨을 빈 문자열로** 준다
 * (핑거프린팅 방어) — 그때 "카메라 1"처럼 순번으로 보여준다.
 */
export function displayLabel(device: CameraDevice, index: number): string {
  const label = device.label.trim();
  return label.length > 0 ? label : `카메라 ${index + 1}`;
}

/** `MediaDevices`의 최소 표면(테스트 주입점). */
export interface MediaDevicesLike {
  enumerateDevices(): Promise<MediaDeviceInfo[]>;
  addEventListener?: (type: "devicechange", listener: () => void) => void;
  removeEventListener?: (type: "devicechange", listener: () => void) => void;
}

/** 비디오 입력 장치만 추린다. 실패는 빈 배열(예외 전파 금지). */
export async function listCameras(
  mediaDevices: MediaDevicesLike | undefined = typeof navigator !== "undefined"
    ? navigator.mediaDevices
    : undefined,
): Promise<CameraDevice[]> {
  if (mediaDevices === undefined) return [];
  try {
    const all = await mediaDevices.enumerateDevices();
    return all
      .filter((d) => d.kind === "videoinput")
      .map((d) => ({ deviceId: d.deviceId, label: d.label, groupId: d.groupId }));
  } catch (err) {
    logger.warn("카메라 장치 열거 실패", {
      reason: err instanceof Error ? err.message : String(err),
    });
    return [];
  }
}

/** `devicechange` 구독(USB 웹캠 착탈). 미지원이면 no-op을 돌려준다. */
export function onDeviceChange(
  listener: () => void,
  mediaDevices: MediaDevicesLike | undefined = typeof navigator !== "undefined"
    ? navigator.mediaDevices
    : undefined,
): () => void {
  if (mediaDevices?.addEventListener === undefined) return () => undefined;
  mediaDevices.addEventListener("devicechange", listener);
  return () => mediaDevices.removeEventListener?.("devicechange", listener);
}
