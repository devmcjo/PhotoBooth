import { canExitKiosk } from "@domain/accounts/accountAdminPolicy";
import type { UserRole } from "@domain/roles/userRole";
import { logger } from "@adapters/storage/logStore";
import { STRINGS } from "@ui/strings";

/**
 * [키오스크 종료] — 앱 종료의 웹 대체 (WD5 · 03 §13.2, React 무관)
 *
 * ⚠️ **[앱 종료]를 만들지 않는다.** `window.close()`는 스크립트가 연 창에서만 동작하고 키오스크의
 *    첫 탭은 사용자가 연 것이다 — 부르면 조용히 실패해 "버튼이 안 먹는다"가 된다.
 *    소스에 `window.close` 문자열이 0건임을 정적 검사가 고정한다.
 * ⚠️ **순서가 규격이다**: 가드 → 전체화면 해제 → 로그아웃 → 홈 복귀 → 안내 토스트.
 *    로그아웃이 홈 복귀보다 먼저여야 홈 화면이 게스트 상태로 그려진다.
 * ⚠️ 토큰을 직접 지우지 않는다 — `logout()`이 `currentUser`를 null로 만들면 M1 구독이 폐기한다.
 */

export interface KioskExitDeps {
  readonly role: UserRole | null;
  readonly exitFullscreen: () => Promise<void>;
  readonly logout: () => void;
  readonly returnHome: (reason: string) => Promise<void>;
  readonly toast: (kind: "info" | "error", message: string) => void;
}

/** 실행됐으면 `true`. 권한이 없으면 **부수효과 0**으로 `false`. */
export async function runKioskExit(deps: KioskExitDeps): Promise<boolean> {
  if (!canExitKiosk(deps.role)) {
    logger.warn("키오스크 종료 거부(권한 없음)");
    return false;
  }

  // 전체화면 해제 실패는 무시하고 계속한다 — 종료 자체가 막히면 키오스크가 갇힌다.
  try {
    await deps.exitFullscreen();
  } catch (err) {
    logger.warn("키오스크 종료: 전체화면 해제 실패(무시하고 계속)", {
      reason: err instanceof Error ? err.message : String(err),
    });
  }

  deps.logout();
  await deps.returnHome("키오스크 종료");
  deps.toast("info", STRINGS.kiosk.exitNotice);
  logger.info("키오스크 종료");
  return true;
}
