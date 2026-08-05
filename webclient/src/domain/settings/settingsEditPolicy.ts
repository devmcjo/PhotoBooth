import { GUEST_LOCKED_KEYS, type AppSettingsValues } from "./appSettings";

/**
 * 설정 편집 가능 판정(순수) — analysis/41 §2.3 · 03 §12.3 · 07 §7
 *
 * 게스트 제한의 **본체는 "저장 패치에서 키를 빼는 것"** 이다. 렌더 가드(비활성)는 안내일 뿐이고,
 * 어느 한 곳이라도 게스트 키를 흘리면 운영자 설정이 조용히 사라진다(M10 4중 방어의 ②③).
 *
 * ⚠️ `GUEST_LOCKED_KEYS`를 **다시 정의하지 않는다** — `appSettings`의 것을 재사용한다.
 *    두 벌이 되면 새 제한 키가 한쪽에만 들어가는 사고가 난다.
 */

/**
 * UI가 렌더하지 않는 키. **값은 보존된다**(WD7·WD8) — draft에 담지 않고 저장 패치에서 빼면
 * `settingsStore`의 병합이 현재 값을 그대로 유지한다.
 */
export const SETTINGS_HIDDEN_KEYS: readonly (keyof AppSettingsValues)[] = [
  "DisplayMode",
  "WindowBounds",
  "ExternalCameraEnabled",
  "PhotoPrinterEnabled",
];

/**
 * TempUser 무료 한도 초과 시 **추가로** 차단되는 키(03 §12.1의 QR 전송 하위 묶음).
 * 보관 시간은 QR 전송의 하위 항목이므로 함께 잠근다.
 */
export const QR_RELATED_KEYS: readonly (keyof AppSettingsValues)[] = [
  "EnableQrDelivery",
  "SendPhoto",
  "SendTimelapse",
  "RetentionHours",
];

export interface SettingsEditContext {
  readonly isGuest: boolean;
  /** TempUser 무료 한도 초과(`shell/qrUsageStore`의 동기 판정). */
  readonly qrBlocked: boolean;
}

function includes(keys: readonly (keyof AppSettingsValues)[], key: keyof AppSettingsValues): boolean {
  return keys.includes(key);
}

export type SettingLockReason = "guest" | "qrLimit";

/**
 * 이 키가 왜 잠겼는가. 잠기지 않았으면 `null`.
 * 게스트 제한이 먼저다 — QR 키는 두 사유에 모두 걸릴 수 있고, 안내 문구는 "로그인 필요"가 맞다.
 */
export function settingLockReason(
  key: keyof AppSettingsValues,
  ctx: SettingsEditContext,
): SettingLockReason | null {
  if (ctx.isGuest && includes(GUEST_LOCKED_KEYS, key)) return "guest";
  if (ctx.qrBlocked && includes(QR_RELATED_KEYS, key)) return "qrLimit";
  return null;
}

/** 지금 이 키를 편집할 수 있는가. 미노출 키는 **어떤 경우에도** 편집 대상이 아니다. */
export function isSettingEditable(
  key: keyof AppSettingsValues,
  ctx: SettingsEditContext,
): boolean {
  if (includes(SETTINGS_HIDDEN_KEYS, key)) return false;
  return settingLockReason(key, ctx) === null;
}

/**
 * 게스트에게 **보여줄** 값. 제한된 boolean 키는 OFF로 표시한다(03 §12.3).
 *
 * ⚠️ **저장 경로는 이 값을 절대 쓰지 않는다.** 런타임 동작은 저장된 운영자 값 그대로이고,
 *    제한되는 것은 편집 권한뿐이다. 이 값이 저장에 새면 로그인 사용자의 설정이 전부 꺼진다.
 * ⚠️ TempUser 한도 초과는 값을 가리지 **않는다**(운영자 값 그대로 — 게스트와 다르다).
 */
export function displaySettingValue<K extends keyof AppSettingsValues>(
  key: K,
  stored: AppSettingsValues[K],
  ctx: SettingsEditContext,
): AppSettingsValues[K] {
  if (!ctx.isGuest || !includes(GUEST_LOCKED_KEYS, key)) return stored;
  return (typeof stored === "boolean" ? false : stored) as AppSettingsValues[K];
}

/** 저장 패치에서 빼야 하는 키 전부(미노출 + 현재 컨텍스트에서 잠긴 것). 중복은 제거된다. */
export function omittedSaveKeys(
  ctx: SettingsEditContext,
): readonly (keyof AppSettingsValues)[] {
  const omitted = new Set<keyof AppSettingsValues>(SETTINGS_HIDDEN_KEYS);
  if (ctx.isGuest) for (const key of GUEST_LOCKED_KEYS) omitted.add(key);
  if (ctx.qrBlocked) for (const key of QR_RELATED_KEYS) omitted.add(key);
  return [...omitted];
}
