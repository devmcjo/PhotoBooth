/**
 * `Account` 화면 진입 모드 인계 채널 — 설계 §5.1
 *
 * `Account`는 **한 화면 안의 두 모드**다([내 정보] / [관리자 도구]). 모드를 `AppState`로 나누면
 * "오버레이 간 전환이 복귀 지점을 덮어써 [닫기]가 무반응이 되는" it19의 실패가 되살아난다.
 * 그래서 모드는 화면 로컬 상태이고, **진입 시 초기값만** 이 채널이 인계한다.
 *
 * 셸 스토어에 넣지 않는 이유는 `frameEditorIntent`와 같다 — 화면 상태가 아니라 다음 진입의 인자다.
 */

export type AccountMode = "account" | "admin";

/**
 * ⚠️ 모드 값은 **이 상수로만** 참조한다. `"admin"` 리터럴이 화면·러너에 흩어지면 정적 검사
 *    ACC-1(역할 문자열 리터럴 0건)이 역할 비교와 구분할 수 없게 된다 — 검사를 느슨하게 만드는
 *    대신 참조 지점을 하나로 모은다.
 */
export const ACCOUNT_MODE_INFO: AccountMode = "account";
export const ACCOUNT_MODE_ADMIN: AccountMode = "admin";

const DEFAULT_MODE: AccountMode = ACCOUNT_MODE_INFO;

let pending: AccountMode = DEFAULT_MODE;

/** `go("Account")` **직전에** 부른다. */
export function writeAccountModeIntent(mode: AccountMode): void {
  pending = mode;
}

/**
 * ⚠️ **비파괴 읽기**다. 소비형으로 만들면 `<StrictMode>`의 2회차 마운트가 기본값으로 떨어져
 *    [관리자 도구]로 들어와도 [내 정보]가 열린다(Step 15 `frameEditorIntent`와 동일 함정).
 */
export function readAccountModeIntent(): AccountMode {
  return pending;
}

/** 테스트·재초기화용. 프로덕션에서는 다음 `write`가 값을 덮는다. */
export function resetAccountModeIntent(): void {
  pending = DEFAULT_MODE;
}
