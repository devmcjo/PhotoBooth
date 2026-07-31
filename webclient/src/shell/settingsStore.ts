import { create } from "zustand";
import {
  DEFAULT_SETTINGS,
  DEFAULT_WEB_EXTRAS,
  GUEST_LOCKED_KEYS,
  type AppSettingsValues,
  type WebExtras,
} from "@domain/settings/appSettings";
import { normalizeQrToggles, onQrReEnabled } from "@domain/settings/qrDeliveryPolicy";
import type { SettingsRepo } from "@adapters/storage/settingsRepo";
import { logger } from "@adapters/storage/logStore";

/**
 * 설정 스토어 — 로드된 설정의 단일 소스 (01 §3)
 *
 * ⚠️ **`persist` 미들웨어를 쓰지 않는다.** 영속은 `settingsRepo`가 담당한다 —
 *    미들웨어를 끼우면 clamp·QR 정규화·게스트 제한을 우회한 값이 저장된다.
 */

export interface SettingsState {
  readonly values: AppSettingsValues;
  readonly webExtras: WebExtras;
  /** 마지막 저장 성공 여부. null이면 아직 저장한 적 없음. */
  readonly lastSaveOk: boolean | null;

  /** 부트스트랩에서 로드 결과를 주입한다. */
  hydrate(values: AppSettingsValues, webExtras: WebExtras): void;
  /**
   * 저장. 게스트면 제한 키를 기록하지 않아 **운영자 값이 보존**된다(analysis/41 §2.3).
   * 반환값이 저장 성공 여부다 — 화면이 이것으로 토스트를 띄운다(M4).
   */
  save(patch: Partial<AppSettingsValues>, options: { isGuest: boolean }): boolean;
  saveWebExtras(patch: Partial<WebExtras>): boolean;
  /** QR 전송을 off → on으로 되돌릴 때 하위 토글을 함께 켠다(화면 로직 전용 규칙). */
  reEnableQr(): boolean;
}

let repo: SettingsRepo | null = null;

/** 부트스트랩이 저장소를 연결한다. 연결 전 `save`는 실패(false)로 처리된다. */
export function attachSettingsRepo(next: SettingsRepo | null): void {
  repo = next;
}

function persist(
  values: AppSettingsValues,
  webExtras: WebExtras,
  isGuest: boolean,
): boolean {
  if (repo === null) {
    logger.error("설정 저장 실패: 저장소가 연결되지 않았습니다.");
    return false;
  }
  const ok = repo.save(
    { values, webExtras },
    isGuest ? { omitKeys: GUEST_LOCKED_KEYS } : undefined,
  );
  if (!ok) logger.error("설정 저장 실패: 저장 위치에 쓸 수 없습니다.");
  return ok;
}

export const useSettingsStore = create<SettingsState>((set, get) => ({
  values: DEFAULT_SETTINGS,
  webExtras: DEFAULT_WEB_EXTRAS,
  lastSaveOk: null,

  hydrate(values, webExtras) {
    set({ values, webExtras });
  },

  save(patch, options) {
    const current = get();
    // QR 세 키는 서로 연동되므로 저장 전에 정규화한다(M7).
    const merged = { ...current.values, ...patch };
    const normalized = normalizeQrToggles({
      enableQrDelivery: merged.EnableQrDelivery,
      sendPhoto: merged.SendPhoto,
      sendTimelapse: merged.SendTimelapse,
    });
    const next: AppSettingsValues = {
      ...merged,
      EnableQrDelivery: normalized.enableQrDelivery,
      SendPhoto: normalized.sendPhoto,
      SendTimelapse: normalized.sendTimelapse,
    };

    const ok = persist(next, current.webExtras, options.isGuest);
    // 저장이 실패해도 화면 상태는 사용자가 입력한 값을 유지한다(다시 시도할 수 있게).
    // 단 게스트 제한 키는 **메모리에서도 되돌린다** — 화면에 반영되면 운영자 값이 바뀐 것처럼 보인다.
    const applied: AppSettingsValues = options.isGuest
      ? GUEST_LOCKED_KEYS.reduce<AppSettingsValues>(
          (acc, key) => ({ ...acc, [key]: current.values[key] }),
          next,
        )
      : next;

    set({ values: applied, lastSaveOk: ok });
    return ok;
  },

  saveWebExtras(patch) {
    const current = get();
    const webExtras = { ...current.webExtras, ...patch };
    const ok = persist(current.values, webExtras, false);
    set({ webExtras, lastSaveOk: ok });
    return ok;
  },

  reEnableQr() {
    const revived = onQrReEnabled();
    return get().save(
      { EnableQrDelivery: true, SendPhoto: revived.sendPhoto, SendTimelapse: revived.sendTimelapse },
      { isGuest: false },
    );
  },
}));

/** React 밖(어댑터·셸)에서 현재 설정을 읽는 경로. */
export function currentSettings(): AppSettingsValues {
  return useSettingsStore.getState().values;
}
