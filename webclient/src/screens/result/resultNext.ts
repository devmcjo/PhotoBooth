import type { AppState } from "@domain/navigation/appState";
import type { AppSettingsValues } from "@domain/settings/appSettings";
import { isQrEffectivelyEnabled } from "@domain/settings/qrEffectivePolicy";
import type { TimelapseResult } from "@adapters/encode/timelapseEncoder";
import { getTimelapseService } from "@adapters/encode/timelapseService";
import {
  saveResultLocally,
  type ResultSaveInput,
  type ResultSaveOutcome,
} from "@adapters/storage/resultSaver";
import { currentSettings } from "@shell/settingsStore";
import { isTempUserQrBlocked } from "@shell/qrUsageStore";
import { sessionStore } from "@shell/sessionStore";
import { currentScreen, shellStore, type ToastKind } from "@shell/shellStore";
import { STRINGS } from "@ui/strings";

/**
 * `Result` [다음] 처리 — **순서가 규격이다** (03 §8.1 · M6-W)
 *
 * ```
 * 1. 타임랩스 마무리(실패해도 계속 — VF-6)
 * 2. 홈 복귀 가드      → 중단이면 보관·전이 모두 하지 않는다
 * 3. 타임랩스 결과 소비 ← stop()이 폐기하므로 [다음] 밖에서는 읽을 수 없다
 * 4. 로컬 보관         ★ 업로드보다 먼저다(M6-W)
 * 5. 실패 토스트(전이는 계속한다 — 손님이 키오스크에 갇히면 안 된다)
 * 6. 홈 복귀 가드 재검사
 * 7. effective QR 판정 → Qr 또는 Done
 * ```
 *
 * **업로드 3단계는 여기 없다** — `Qr` 화면이 소유한다(03 §8.1의 [다음] 순서에 업로드가 없고,
 * 업로드 3단계는 03 §9.1의 `Qr` 진입 절차다. Windows도 `QrPopupViewModel.OnEnterAsync`가 한다).
 *
 * React 밖에 두는 이유: 순서가 불변식인데 컴포넌트 안에 있으면 node 테스트가 닿지 못한다(15 §3.1).
 */

export interface ResultNextDeps {
  readonly finishTimelapse: () => Promise<TimelapseResult | null>;
  readonly currentTimelapse: () => TimelapseResult | null;
  readonly finalBlob: () => Blob | null;
  readonly save: (input: ResultSaveInput) => Promise<ResultSaveOutcome>;
  readonly settings: () => AppSettingsValues;
  readonly sessionId: () => string | null;
  readonly isLoggedIn: () => boolean;
  /**
   * TempUser 무료 한도 초과인가. **동기**여야 한다 — 비동기로 바꾸면 [다음]이 네트워크를
   * 기다려 손님이 최대 100초 멈춘다. `qrUsageStore`가 계정 변경 시 캐시해 둔 값을 읽는다.
   */
  readonly isTempUserBlocked: () => boolean;
  /** `currentScreen() === "Result"`. */
  readonly stillOnResult: () => boolean;
  readonly go: (to: AppState) => void;
  readonly toast: (kind: ToastKind, message: string) => void;
  readonly now: () => Date;
  readonly uuid: () => string;
}

export interface ResultNextOutcome {
  /** 홈 복귀·유휴 만료로 중단됐는가. true면 그 지점 이후를 하지 않았다. */
  readonly aborted: boolean;
  readonly save: ResultSaveOutcome | null;
  readonly destination: "Qr" | "Done" | null;
}

export async function runResultNext(deps: ResultNextDeps): Promise<ResultNextOutcome> {
  try {
    await deps.finishTimelapse();
  } catch {
    // `finish()`는 던지지 않지만(VF-6) 이중 방어 — 인코딩 실패가 보관·전이를 막으면 안 된다.
  }

  // 대기 중 홈 복귀·유휴 만료가 일어났으면 세션·작업 공간·타임랩스가 이미 폐기됐다.
  // 취소된 촬영물을 영구 보관하는 것은 잔재 삭제 규격(analysis/41 §4)의 취지에 반한다.
  if (!deps.stillOnResult()) return { aborted: true, save: null, destination: null };

  // ⚠️ 여기서 소비해야 한다. `stop()`(홈 복귀)이 결과를 폐기한다.
  const timelapse = deps.currentTimelapse();
  const settings = deps.settings();

  const outcome = await deps.save({
    finalBlob: deps.finalBlob(),
    format: settings.OutputFormat,
    // ★ 타임랩스 null 분기는 이 한 곳뿐이다. 없는 것은 정상이다(VF-6).
    timelapseBlob: timelapse?.blob ?? null,
    saveLocalCopy: settings.SaveLocalCopy,
    sessionId: deps.sessionId(),
    localTime: deps.now(),
    fallbackToken: deps.uuid().replace(/-/g, ""),
  });

  // `partial`(타임랩스만 실패)에는 토스트를 띄우지 않는다 — 손님이 할 수 있는 조치가 없고
  // 타임랩스 부재는 계약상 합법이다. 운영자용 신호는 로그·진단에 남는다.
  if (outcome.status === "failed") deps.toast("error", STRINGS.save.failed);

  // ⚠️ 업로드 3단계를 **여기에 넣지 마라.** 소유자는 `Qr` 화면이다(`screens/qr/uploadRunner.ts`) —
  //    03 §8.1의 [다음] 순서에 업로드가 없고, 03 §9.1이 업로드를 `Qr` 진입 절차로 규정한다.
  //    [재시도]가 `Qr` 화면 액션이라 여기에도 두면 같은 부수효과의 진입점이 2개가 된다.
  //    M6-W(보관 → 업로드)는 save가 go보다 앞이므로 **구조적으로** 성립한다.

  if (!deps.stillOnResult()) return { aborted: true, save: outcome, destination: null };

  const qrOn = isQrEffectivelyEnabled(
    settings.EnableQrDelivery,
    deps.isLoggedIn(),
    deps.isTempUserBlocked(),
  );
  const destination = qrOn ? "Qr" : "Done";
  deps.go(destination);
  return { aborted: false, save: outcome, destination };
}

/**
 * 실제 배선. 전부 **호출 시점에 싱글턴을 해석하는 클로저**다 —
 * 모듈 로드 시 `getTimelapseService()`를 부르면 node 테스트가 인코더 Worker를 붙잡는다.
 */
export function defaultResultNextDeps(
  overrides: Partial<ResultNextDeps> & { readonly finalBlob: () => Blob | null },
): ResultNextDeps {
  const base: ResultNextDeps = {
    finishTimelapse: () => getTimelapseService().finish(),
    currentTimelapse: () => getTimelapseService().current(),
    finalBlob: () => null,
    save: (input) => saveResultLocally(input),
    settings: currentSettings,
    sessionId: () => sessionStore.getState().sessionId,
    isLoggedIn: () => sessionStore.getState().currentUser !== null,
    isTempUserBlocked: () => isTempUserQrBlocked(),
    stillOnResult: () => currentScreen() === "Result",
    go: (to) => {
      shellStore.getState().go(to);
    },
    toast: (kind, message) => shellStore.getState().toast(kind, message),
    now: () => new Date(),
    uuid: () => crypto.randomUUID(),
  };
  return { ...base, ...overrides };
}
