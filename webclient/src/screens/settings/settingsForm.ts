import type { AppSettingsValues, WebExtras } from "@domain/settings/appSettings";
import { onQrReEnabled } from "@domain/settings/qrDeliveryPolicy";
import {
  isSettingEditable,
  omittedSaveKeys,
  type SettingsEditContext,
} from "@domain/settings/settingsEditPolicy";
import { logger } from "@adapters/storage/logStore";
import type { ToastKind } from "@shell/shellStore";
import { STRINGS } from "@ui/strings";

/**
 * 설정 화면의 draft·패치·저장 절차 — 03 §12.4·§12.5 (React 무관)
 *
 * ⚠️ **여기서 clamp하지 않는다.** `clampSettings`·`closestFrom`·`normalizeQrToggles`를 부르면
 *    진실원(analysis/41 §2)이 두 곳이 되어 Windows와 값이 갈라진다(정적 검사 SET-1).
 *    보정은 `settingsStore.save` → `settingsRepo` 안에서 일어나고, 화면은 **재반영으로 결과를 본다**.
 * ⚠️ 게스트 제한의 본체는 `buildSavePatch`가 **제한 키를 패치에서 빼는 것**이다(M10 ③).
 */

export interface SettingsDraft {
  /**
   * 전 키를 담는다(미노출 키 포함). 렌더하지 않는 키는 화면이 건드리지 않고,
   * `buildSavePatch`가 저장 패치에서 빼므로 저장소의 기존 값이 그대로 보존된다(WD7·WD8).
   */
  readonly values: AppSettingsValues;
  readonly webExtras: WebExtras;
}

export function createDraft(values: AppSettingsValues, webExtras: WebExtras): SettingsDraft {
  return { values, webExtras };
}

/**
 * 값 1개 변경. **편집 불가면 draft를 그대로 돌려준다**(액션 가드 — M10 ②).
 * 렌더 가드(비활성)만 믿지 않는 이유: 키보드·스크린리더·스크립트로도 이벤트가 들어온다.
 */
export function changeSetting<K extends keyof AppSettingsValues>(
  draft: SettingsDraft,
  key: K,
  value: AppSettingsValues[K],
  ctx: SettingsEditContext,
): SettingsDraft {
  if (!isSettingEditable(key, ctx)) {
    logger.warn("제한된 설정 항목 편집 시도", { settingKey: key, guest: ctx.isGuest });
    return draft;
  }
  return { ...draft, values: { ...draft.values, [key]: value } };
}

export type QrToggleKey = "EnableQrDelivery" | "SendPhoto" | "SendTimelapse";

/**
 * QR 토글 변경 + **재활성 규칙**(analysis/41 §2.4): QR 전송이 off → on으로 바뀌는 순간
 * 하위 둘을 강제로 켠다.
 *
 * ⚠️ 이 규칙은 **사용자 이벤트에서만** 적용된다. 로드·재반영 경로는 이 함수를 지나지 않으므로
 *    "설정 로드 중 억제"가 구조적으로 보장된다.
 */
export function applyQrToggle(
  draft: SettingsDraft,
  key: QrToggleKey,
  next: boolean,
  ctx: SettingsEditContext,
): SettingsDraft {
  if (!isSettingEditable(key, ctx)) {
    logger.warn("제한된 설정 항목 편집 시도", { settingKey: key, guest: ctx.isGuest });
    return draft;
  }

  if (key !== "EnableQrDelivery" || !next || draft.values.EnableQrDelivery) {
    return { ...draft, values: { ...draft.values, [key]: next } };
  }

  const revived = onQrReEnabled();
  return {
    ...draft,
    values: {
      ...draft.values,
      EnableQrDelivery: true,
      SendPhoto: revived.sendPhoto,
      SendTimelapse: revived.sendTimelapse,
    },
  };
}

/** 웹 전용 보조값 변경(카메라 라벨·groupId·전후면). 게스트 제한 대상이 아니다. */
export function changeWebExtra<K extends keyof WebExtras>(
  draft: SettingsDraft,
  key: K,
  value: WebExtras[K],
): SettingsDraft {
  return { ...draft, webExtras: { ...draft.webExtras, [key]: value } };
}

/**
 * 저장 패치. **잠긴 키와 미노출 키를 뺀다** — 빠진 키는 저장소가 기존 값을 그대로 남긴다.
 * draft가 어떤 경로로 오염됐더라도(예: 게스트가 개발자 도구로 값을 바꿔도) 여기서 걸러진다.
 */
export function buildSavePatch(
  draft: SettingsDraft,
  ctx: SettingsEditContext,
): Partial<AppSettingsValues> {
  const omitted = new Set<string>(omittedSaveKeys(ctx));
  const patch: Record<string, unknown> = {};
  for (const [key, value] of Object.entries(draft.values)) {
    if (omitted.has(key)) continue;
    patch[key] = value;
  }
  return patch as Partial<AppSettingsValues>;
}

export interface SaveSettingsDeps {
  readonly draft: SettingsDraft;
  readonly ctx: SettingsEditContext;
  /** `settingsStore.save`. 반환값이 저장 성공 여부다(M4). */
  readonly save: (
    patch: Partial<AppSettingsValues>,
    options: { readonly isGuest: boolean; readonly webExtras?: Partial<WebExtras> },
  ) => boolean;
  /** 저장 직후 스토어에서 다시 읽는다(보정·정규화·게스트 되돌림이 반영된 값). */
  readonly readBack: () => SettingsDraft;
  readonly resetDraft: (draft: SettingsDraft) => void;
  readonly toast: (kind: ToastKind, message: string) => void;
}

/**
 * 저장 절차 — **순서가 규격이다**(03 §12.4).
 *
 * ```
 * 1. (웹에는 "창 기하 캡처" 단계가 없다 — WD7)
 * 2. 패치 조립 → 저장(내부에서 clamp + QR 정규화)
 * 3. ★ 저장된 값을 화면에 **재반영**   ← 빼면 컷 수 7→6 보정·QR 정규화가 화면에 안 보인다
 * 4. 즉시 적용(웹은 카메라 장치 힌트뿐 — 카메라가 이 화면에서 돌지 않는다)
 * 5. 성공·실패를 **정직하게** 알린다(M4 — 성공 오인 금지)
 * ```
 */
export function saveSettings(deps: SaveSettingsDeps): { readonly ok: boolean } {
  const patch = buildSavePatch(deps.draft, deps.ctx);
  const ok = deps.save(patch, {
    isGuest: deps.ctx.isGuest,
    webExtras: deps.draft.webExtras,
  });

  // 3. 실패해도 재반영한다 — 스토어가 사용자 입력값을 그대로 들고 있어 draft가 되돌아가지 않고,
  //    게스트 제한 키만 운영자 값으로 복원돼 화면에 정확히 드러난다.
  deps.resetDraft(deps.readBack());

  deps.toast(ok ? "success" : "error", ok ? STRINGS.save.succeeded : STRINGS.save.failed);
  return { ok };
}
