import { create } from "zustand";
import {
  clampSettings,
  DEFAULT_SETTINGS,
  DEFAULT_WEB_EXTRAS,
  GUEST_LOCKED_KEYS,
  type AppSettingsValues,
  type WebExtras,
} from "@domain/settings/appSettings";
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
   *
   * ⚠️ `isGuest`는 **호출자가 판정해 넘긴다.** 이 파일에 `isGuest: false`를 하드코딩한
   *    편의 함수를 다시 만들지 마라 — 게스트 조작이 운영자 값으로 기록된다(정적 검사 SET-4).
   * ⚠️ 웹 전용 보조값(`webExtras`)을 **같은 트랜잭션**에 합친다: 저장 왕복 1회 ·
   *    성공/실패 boolean 1개(M4 정직성). 카메라 장치 선택이 3개 키를 함께 쓰기 때문이다.
   */
  save(
    patch: Partial<AppSettingsValues>,
    options: { readonly isGuest: boolean; readonly webExtras?: Partial<WebExtras> },
  ): boolean;
}

let repo: SettingsRepo | null = null;

/** 부트스트랩이 저장소를 연결한다. 연결 전 `save`는 실패(false)로 처리된다. */
export function attachSettingsRepo(next: SettingsRepo | null): void {
  repo = next;
}

/**
 * 연결된 저장소(내보내기 원문이 필요한 설정 화면 전용). 미연결이면 `null`.
 * ⚠️ 저장은 이 핸들로 하지 마라 — `save()`를 지나야 clamp·QR 정규화·게스트 제한이 적용된다.
 */
export function currentSettingsRepo(): SettingsRepo | null {
  return repo;
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
    const webExtras: WebExtras =
      options.webExtras === undefined
        ? current.webExtras
        : { ...current.webExtras, ...options.webExtras };

    // ⚠️ **메모리에도 clamp를 적용한다.** `settingsRepo.save`가 저장 직전에 clamp하므로,
    //    여기서 하지 않으면 "저장된 값(6)"과 "화면이 읽는 값(7)"이 갈라지고 03 §12.4의
    //    재반영 단계가 보정 사실을 보여주지 못한다. `clampSettings`가 QR 정규화(M7)도 포함한다.
    const next = clampSettings({ ...current.values, ...patch });

    const ok = persist(next, webExtras, options.isGuest);
    // 저장이 실패해도 화면 상태는 사용자가 입력한 값을 유지한다(다시 시도할 수 있게).
    // 단 게스트 제한 키는 **메모리에서도 되돌린다** — 화면에 반영되면 운영자 값이 바뀐 것처럼 보인다.
    const applied: AppSettingsValues = options.isGuest
      ? GUEST_LOCKED_KEYS.reduce<AppSettingsValues>(
          (acc, key) => ({ ...acc, [key]: current.values[key] }),
          next,
        )
      : next;

    set({ values: applied, webExtras, lastSaveOk: ok });
    return ok;
  },
}));

/** React 밖(어댑터·셸)에서 현재 설정을 읽는 경로. */
export function currentSettings(): AppSettingsValues {
  return useSettingsStore.getState().values;
}
