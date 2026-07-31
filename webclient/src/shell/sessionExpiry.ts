import { logger } from "@adapters/storage/logStore";
import { STRINGS } from "@ui/strings";
import { sessionStore } from "./sessionStore";
import { shellStore } from "./shellStore";

/**
 * JWT 만료(401) 처리 — 07 §4.3 · 12 C10 · 02 §5.2
 *
 * 배선 지점은 **`backendClient`의 401 분기 한 곳뿐**이다(설계 §4.5). 화면·서비스에
 * `isUnauthorized(err)` 기반 세션 해제를 추가하지 않는다 — 두 곳이 되면 토스트가 2번 뜬다.
 *
 * ⚠️ `logout()`이 아니라 `expireSession()`이다. 만료 시 **촬영 데이터는 유지**가 규격이고
 *    `logout()`은 `discardCaptureData()`를 동반한다.
 * ⚠️ 토큰 폐기를 직접 하지 않는다 — `currentUser`가 null이 되면 M1 구독이 처리한다.
 * ⚠️ `shell/globalErrorHandler.ts`가 `@ui/strings`를 import하는 선례가 이미 있다.
 */

/**
 * 멱등이다 — 이미 게스트면 아무 것도 하지 않는다. 한 화면에서 여러 요청이 동시에 401을
 * 맞아도 토스트는 1번, 진단 로그도 1줄이다.
 *
 * @param path 진단용 요청 경로(`backendClient`가 넘긴다). 없으면 컨텍스트를 남기지 않는다.
 */
export function handleSessionExpired(path?: string): void {
  if (sessionStore.getState().currentUser === null) return;

  sessionStore.getState().expireSession();
  shellStore.getState().toast("error", STRINGS.error.sessionExpired);
  logger.warn(
    "세션 만료 감지(401) — 세션 해제",
    path === undefined ? undefined : { path },
  );
}
