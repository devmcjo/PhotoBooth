import {
  parseOauthPendingState,
  type OauthPendingState,
} from "@domain/auth/oauthCallbackPolicy";
import { logger } from "@adapters/storage/logStore";

/**
 * 로그인 임시 상태(`sessionStorage`) — 07 §2.4
 *
 * ⚠️ **이 파일이 `sessionStorage`를 만지는 유일한 곳이다**(정적 테스트 M2-a가 고정한다).
 * ⚠️ **JWT를 쓰지 않는다.** 여기 들어가는 값은 `code_verifier`·`state`·`nonce`·`returnTo`·
 *    `startedAt`뿐이고 콜백 처리 시작 시 **즉시 소비·삭제**된다. 리디렉트로 페이지가 통째로
 *    사라지므로 메모리로는 전달할 방법이 없다 — M2 위반이 아닌 근거는 07 §2.4.
 * ⚠️ **예외를 전파하지 않는다**(15 §2). 프라이빗 모드·용량 초과는 `false`/`null`이다.
 */

/** 키는 **1개 고정**이다 — 매번 덮어써서 이탈한 손님의 pending이 쌓이지 않는다. */
export const OAUTH_PENDING_KEY = "mcphoto.oauth.pending.v1";

/** `sessionStorage` 최소 표면. 테스트가 가짜 저장소를 주입한다(`settingsRepo`와 같은 형태). */
export interface StorageLike {
  getItem(key: string): string | null;
  setItem(key: string, value: string): void;
  removeItem(key: string): void;
}

/**
 * 기본 저장소. **속성 접근 자체가 던지는** 브라우저(쿠키 차단 프라이빗 모드)가 있어 감싼다.
 * node 테스트 환경에는 없으므로 `null`이 되고, 그때 모든 함수가 실패 경로를 탄다.
 */
export function sessionStorageOrNull(): StorageLike | null {
  try {
    const store: Storage | undefined = globalThis.sessionStorage;
    return store ?? null;
  } catch {
    return null;
  }
}

/** 실패(프라이빗 모드·용량)는 예외가 아니라 `false`다. */
export function savePendingOauth(
  state: OauthPendingState,
  store: StorageLike | null = sessionStorageOrNull(),
): boolean {
  if (store === null) {
    logger.error("로그인 임시 상태 저장 실패", { reason: "sessionStorage 사용 불가" });
    return false;
  }
  try {
    store.setItem(OAUTH_PENDING_KEY, JSON.stringify(state));
    return true;
  } catch (err) {
    logger.error("로그인 임시 상태 저장 실패", {
      reason: err instanceof Error ? err.message : String(err),
    });
    return false;
  }
}

/**
 * **읽고 즉시 지운다**(원자적 소비). 재진입·새로고침 시 반드시 `null`이 되어
 * 같은 인가 코드로 두 번 교환하는 경로가 구조적으로 사라진다.
 *
 * ⚠️ 순서가 계약이다: `getItem` → `removeItem` → `JSON.parse` → 도메인 파싱.
 *    **삭제를 파싱보다 먼저** 해서 손상된 값도 반드시 사라지게 한다.
 */
export function takePendingOauth(
  store: StorageLike | null = sessionStorageOrNull(),
): OauthPendingState | null {
  if (store === null) return null;

  let raw: string | null = null;
  try {
    raw = store.getItem(OAUTH_PENDING_KEY);
    store.removeItem(OAUTH_PENDING_KEY);
  } catch (err) {
    logger.warn("로그인 임시 상태 읽기 실패", {
      reason: err instanceof Error ? err.message : String(err),
    });
    return null;
  }
  if (raw === null) return null;

  try {
    return parseOauthPendingState(JSON.parse(raw));
  } catch {
    // 손상된 JSON. 값은 이미 지워졌으므로 다음 진입은 깨끗하다.
    logger.warn("로그인 임시 상태 파싱 실패(손상된 값)");
    return null;
  }
}

/** 시작 실패 시 잔재 제거. 지우지 못해도 3분 타임아웃 판정이 다음 콜백을 막는다. */
export function clearPendingOauth(store: StorageLike | null = sessionStorageOrNull()): void {
  if (store === null) return;
  try {
    store.removeItem(OAUTH_PENDING_KEY);
  } catch {
    // 무시한다 — 지우기 실패로 로그인 흐름을 멈추지 않는다.
  }
}
