import { logger } from "@adapters/storage/logStore";
import { sessionStore } from "./sessionStore";

/**
 * JWT 홀더 — **메모리 전용** (M2 · WD9 · 02 §5)
 *
 * ⚠️ `localStorage`·`sessionStorage`·IndexedDB·쿠키에 **절대 쓰지 않는다.**
 *    Zustand `persist` 미들웨어도 쓰지 않는다. 새로고침 = 재로그인이 정상 흐름이다.
 *    E4 테스트가 "저장소에 토큰 문자열 0건"을 자동 검사한다.
 *
 * ⚠️ 토큰 폐기를 **버튼에 걸지 않는다**(M1). `sessionStore.currentUser`가 null이 되는
 *    **모든** 경로를 아래 구독 한 곳이 덮는다 — 게스트 전환 경로가 늘어도 사각이 없다.
 */

/** 모듈 스코프 변수 1개. 이것이 토큰의 유일한 거처다. */
let token: string | null = null;
let expiresAtMs: number | null = null;

export function setToken(next: string, expiresInSeconds: number, nowMs: number): void {
  token = next;
  expiresAtMs = nowMs + expiresInSeconds * 1000;
}

/** Bearer에 쓸 토큰. 없으면 null(호출측이 헤더를 붙이지 않는다). */
export function getToken(): string | null {
  return token;
}

export function hasToken(): boolean {
  return token !== null;
}

/** 만료 예정 시각(ms). 진단 표시용. */
export function getTokenExpiry(): number | null {
  return expiresAtMs;
}

/**
 * 토큰 폐기. 직접 부르는 곳은 ① 아래 구독 ② JWT 만료(401) 처리뿐이다.
 * 로그아웃 버튼에서 부르지 않는다 — `sessionStore.logout()`이 구독을 통해 여기 도달한다.
 */
export function clearToken(reason: string): void {
  if (token === null) return;
  token = null;
  expiresAtMs = null;
  // 토큰 값은 절대 로그에 남기지 않는다(analysis/41 §8). 사유만 남긴다.
  logger.info("JWT 폐기", { reason });
}

let unsubscribe: (() => void) | null = null;

/**
 * M1 배선 설치(앱 시작 시 1회). `currentUser`가 null이 되면 토큰을 폐기한다.
 * @returns 해제 함수(테스트용)
 */
export function installTokenLifecycle(): () => void {
  if (unsubscribe !== null) return unsubscribe;

  unsubscribe = sessionStore.subscribe(
    (state) => state.currentUser,
    (user) => {
      if (user === null) clearToken("세션 사용자 해제");
    },
  );
  return unsubscribe;
}

/** 테스트·재초기화용. */
export function uninstallTokenLifecycle(): void {
  unsubscribe?.();
  unsubscribe = null;
}

/** 테스트용 강제 초기화(제품 코드에서 부르지 않는다). */
export function resetAuthForTests(): void {
  token = null;
  expiresAtMs = null;
}
