import type { OauthAbortReason } from "./oauthCallbackPolicy";

/**
 * 로그인 실패 사유 ↔ 문구 키 매핑(순수) — 07 §2.6 · 03 §3.1
 *
 * 사유는 **진단용으로 더 세분**되고(400과 네트워크를 로그에서 갈라야 한다) 문구는 **더 적다**.
 * 이 축 변환을 화면에 흩뿌리면 400이 조용히 "네트워크"로 뭉개져 원인 파악이 어려워진다.
 */
export type LoginFailureReason =
  /** 취소·state 불일치·code 없음·3분 초과 (abort 5종 전부). */
  | "cancelled"
  /** 서버 401 — 계정·도메인 거부. */
  | "rejected"
  /** 서버 501 — SSO 미구성. */
  | "notConfigured"
  /** 서버 400 — redirectUri 거부(B1 미적용 의심). 문구는 network과 같다. */
  | "redirectRejected"
  /** 네트워크·타임아웃·응답 형식 오류·PKCE 생성 불가. */
  | "network"
  /** `env.googleClientId` 빈 값(버튼 미노출 — 방어용). */
  | "clientNotConfigured";

/** `STRINGS.login.errors`의 키와 1:1이다. */
export type LoginMessageKey =
  | "cancelled"
  | "rejected"
  | "notConfigured"
  | "network"
  | "clientNotConfigured";

/**
 * `Record`로 두는 이유: 사유를 하나 늘리면 **컴파일이 깨져** 문구 결정을 강제한다.
 * `redirectRejected`만 이름이 다른 문구로 접힌다(손님에게는 네트워크 문구, 원인은 로그가 지목).
 */
const MESSAGE_KEY_BY_REASON: Readonly<Record<LoginFailureReason, LoginMessageKey>> = {
  cancelled: "cancelled",
  rejected: "rejected",
  notConfigured: "notConfigured",
  redirectRejected: "network",
  network: "network",
  clientNotConfigured: "clientNotConfigured",
};

export function loginFailureMessageKey(reason: LoginFailureReason): LoginMessageKey {
  return MESSAGE_KEY_BY_REASON[reason];
}

/**
 * abort 5종 → **전부 `cancelled`**(07 §2.6). 손님에게 "state가 어긋났습니다"를 보여줄 수는 없고,
 * 진단은 `abortReason` 로그가 담당한다. 함수로 두어 매핑을 테스트로 고정한다.
 */
const FAILURE_BY_ABORT_REASON: Readonly<Record<OauthAbortReason, LoginFailureReason>> = {
  "no-pending": "cancelled",
  "state-mismatch": "cancelled",
  "provider-error": "cancelled",
  timeout: "cancelled",
  "no-code": "cancelled",
};

export function abortReasonToLoginFailure(reason: OauthAbortReason): LoginFailureReason {
  return FAILURE_BY_ABORT_REASON[reason];
}
