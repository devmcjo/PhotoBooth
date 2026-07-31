import type { AppSettingsValues, WebExtras } from "@domain/settings/appSettings";
import {
  displayLabel,
  matchDevice,
  type CameraDevice,
  type DeviceMatch,
  type StoredDeviceRef,
} from "@adapters/camera/deviceEnumerator";
import { changeSetting, changeWebExtra, type SettingsDraft } from "./settingsForm";
import type { SettingsEditContext } from "@domain/settings/settingsEditPolicy";

/**
 * 카메라 장치 선택 — 03 §12.6 (React 무관)
 *
 * ⚠️ **`deviceId`는 안정적이지 않다.** 그래서 선택 시 `CameraDevice`(deviceId) +
 *    `webExtras.{CameraDeviceLabel,CameraDeviceGroupId}` **3개를 함께** 기록한다.
 *    라벨·groupId가 없으면 브라우저 재시작 후 "저장한 카메라가 사라졌다"가 반복된다(WC3).
 */

export interface CameraOption {
  readonly deviceId: string;
  /** 권한 전 빈 라벨은 "카메라 N"으로 폴백한다. */
  readonly label: string;
  readonly groupId: string;
  /** 권한 전이라 진짜 이름을 모르는가(안내 문구 조건). */
  readonly labelUnknown: boolean;
}

export function buildCameraOptions(devices: readonly CameraDevice[]): readonly CameraOption[] {
  return devices.map((device, index) => ({
    deviceId: device.deviceId,
    label: displayLabel(device, index),
    groupId: device.groupId,
    labelUnknown: device.label.trim().length === 0,
  }));
}

/** 하나라도 라벨을 모르면 *"권한을 허용하면 장치 이름이 표시됩니다."* 를 띄운다. */
export function needsPermissionHint(options: readonly CameraOption[]): boolean {
  return options.some((option) => option.labelUnknown);
}

/** 설정에 저장된 장치 참조(3개 키를 한 객체로). */
export function storedDeviceRef(
  values: Pick<AppSettingsValues, "CameraDevice">,
  webExtras: Pick<WebExtras, "CameraDeviceLabel" | "CameraDeviceGroupId">,
): StoredDeviceRef {
  return {
    deviceId: values.CameraDevice,
    label: webExtras.CameraDeviceLabel,
    groupId: webExtras.CameraDeviceGroupId,
  };
}

/** 현재 draft가 가리키는 장치. 매칭 순서는 어댑터가 소유한다(deviceId → label → groupId → 첫 장치). */
export function resolveSelectedDevice(
  devices: readonly CameraDevice[],
  draft: SettingsDraft,
): DeviceMatch {
  return matchDevice(devices, storedDeviceRef(draft.values, draft.webExtras));
}

/**
 * 장치 선택 → draft에 3개 키를 함께 반영한다.
 * `CameraDevice`는 게스트 제한 키가 아니지만, 액션 가드를 지나도록 `changeSetting`을 통과시킨다.
 */
export function selectCamera(
  draft: SettingsDraft,
  device: CameraDevice,
  ctx: SettingsEditContext,
): SettingsDraft {
  const withDeviceId = changeSetting(draft, "CameraDevice", device.deviceId, ctx);
  const withLabel = changeWebExtra(withDeviceId, "CameraDeviceLabel", device.label);
  return changeWebExtra(withLabel, "CameraDeviceGroupId", device.groupId);
}

/** 전/후면 힌트(모바일 폴백용). */
export function selectFacing(
  draft: SettingsDraft,
  facing: WebExtras["CameraFacing"],
): SettingsDraft {
  return changeWebExtra(draft, "CameraFacing", facing);
}
