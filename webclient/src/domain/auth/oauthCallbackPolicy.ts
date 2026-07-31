import type { AppState } from "../navigation/appState";

/**
 * OAuth 콜백 판정(순수) — 07 §2.2 5단계 · 07 §2.5
 *
 * 콜백 처리에서 **부수효과가 없는 부분 전부**가 여기에 있다. 어댑터는 URL·저장소를 읽어
 * 값으로 넘기기만 하고, "무엇을 할지"는 이 파일이 결정한다 → 5개 중단 사유가 node에서
 * 전부 검증된다.
 */

/** 3분. Windows `GoogleSignInService` 타임아웃과 같은 값이다. */
export const OAUTH_FLOW_TIMEOUT_MS = 180_000;

export interface OauthPendingState {
  readonly codeVerifier: string;
  readonly state: string;
  readonly nonce: string;
  /** 복귀 화면 이름. **문자열 그대로** 보관하고 소비 시 clamp한다(아래 `resolveOauthReturnTo`). */
  readonly returnTo: string;
  /** epoch ms. */
  readonly startedAt: number;
}

export interface OauthCallbackParams {
  readonly code: string | null;
  readonly state: string | null;
  /** Google의 `error` 파라미터(`access_denied` 등). */
  readonly error: string | null;
}

export type OauthAbortReason =
  /** sessionStorage에 값이 없다(직접 진입·새로고침·재진입·다른 오리진으로 복귀). */
  | "no-pending"
  /** CSRF 방어 — state 대조 실패. */
  | "state-mismatch"
  /** Google이 error를 돌려줬다(사용자 취소 포함). */
  | "provider-error"
  /** `startedAt` + 3분 초과. */
  | "timeout"
  /** code 파라미터 부재. */
  | "no-code";

export type OauthCallbackDecision =
  | {
      readonly kind: "exchange";
      readonly code: string;
      readonly codeVerifier: string;
      readonly nonce: string;
      readonly returnTo: AppState;
    }
  | { readonly kind: "abort"; readonly reason: OauthAbortReason };

function nonEmpty(value: string | null): string | null {
  if (value === null) return null;
  const trimmed = value.trim();
  return trimmed.length > 0 ? trimmed : null;
}

/**
 * `location.search`(`?code=…&state=…`) 파싱. 빈 문자열은 부재와 같게 취급한다.
 * `URLSearchParams`는 순수 계산이고 purity 금지 목록에 없다 — 여기서는 **읽기 전용**이다.
 */
export function parseOauthCallbackParams(search: string): OauthCallbackParams {
  const params = new URLSearchParams(search);
  return {
    code: nonEmpty(params.get("code")),
    state: nonEmpty(params.get("state")),
    error: nonEmpty(params.get("error")),
  };
}

function asNonEmptyString(value: unknown): string | null {
  return typeof value === "string" && value.trim().length > 0 ? value : null;
}

/**
 * 저장소에서 되살린 값을 방어적으로 파싱한다. 하나라도 어긋나면 `null` →
 * `decideOauthCallback`이 `no-pending`으로 끊는다(손상 값으로 교환을 시도하지 않는다).
 */
export function parseOauthPendingState(raw: unknown): OauthPendingState | null {
  if (typeof raw !== "object" || raw === null) return null;
  const record = raw as Record<string, unknown>;

  const codeVerifier = asNonEmptyString(record.codeVerifier);
  const state = asNonEmptyString(record.state);
  const nonce = asNonEmptyString(record.nonce);
  if (codeVerifier === null || state === null || nonce === null) return null;

  if (typeof record.startedAt !== "number" || !Number.isFinite(record.startedAt)) return null;

  return {
    codeVerifier,
    state,
    nonce,
    // 문자열이 아니면 빈 값으로 두고 clamp가 Home으로 떨어뜨린다(여기서 거부하지 않는다 —
    // 복귀 지점 하나 때문에 성공한 로그인을 버릴 이유가 없다).
    returnTo: typeof record.returnTo === "string" ? record.returnTo : "",
    startedAt: record.startedAt,
  };
}

/**
 * **검사 순서가 계약이다**(테스트가 각 분기를 고정한다):
 *   1) pending 없음        → no-pending
 *   2) state 불일치·부재    → state-mismatch   ★ 무엇보다 먼저 CSRF를 끊는다
 *   3) error 파라미터 존재  → provider-error
 *   4) 3분 초과            → timeout
 *   5) code 없음           → no-code
 *   6) 그 외               → exchange
 *
 * ⚠️ 2)를 3)보다 앞에 두는 이유: 검증되지 않은 콜백의 **어떤 파라미터도 해석하지 않는다**.
 *    다섯 사유의 사용자 문구가 모두 같으므로(07 §2.6) 순서가 UX를 바꾸지 않는다.
 */
export function decideOauthCallback(
  params: OauthCallbackParams,
  pending: OauthPendingState | null,
  nowMs: number,
): OauthCallbackDecision {
  if (pending === null) return { kind: "abort", reason: "no-pending" };
  if (params.state === null || params.state !== pending.state) {
    return { kind: "abort", reason: "state-mismatch" };
  }
  if (params.error !== null) return { kind: "abort", reason: "provider-error" };
  if (nowMs - pending.startedAt > OAUTH_FLOW_TIMEOUT_MS) {
    return { kind: "abort", reason: "timeout" };
  }
  if (params.code === null) return { kind: "abort", reason: "no-code" };

  return {
    kind: "exchange",
    code: params.code,
    codeVerifier: pending.codeVerifier,
    nonce: pending.nonce,
    returnTo: resolveOauthReturnTo(pending.returnTo),
  };
}

/**
 * 복귀 화면 clamp. **리디렉트로 앱이 통째로 재시작됐으므로** 촬영 세션·합성 결과가 전부 없다 →
 * 세션에 의존하는 화면으로는 돌아갈 수 없다. 콜드 스타트(`Home`)에서 합법인 화면만 허용한다:
 *   `Home` · `FrameSelect` · `Settings` · `Account`
 * (= `canTransition("Home", x)`가 참인 집합 − `Login`. 그래서 `go()`가 거부당하는 경우가 없다.)
 * 그 외·미지의 문자열·null은 전부 `"Home"`.
 */
const OAUTH_RETURN_TO_ALLOWED: readonly AppState[] = ["Home", "FrameSelect", "Settings", "Account"];

export function resolveOauthReturnTo(raw: string | null): AppState {
  if (raw === null) return "Home";
  const trimmed = raw.trim();
  return (OAUTH_RETURN_TO_ALLOWED as readonly string[]).includes(trimmed)
    ? (trimmed as AppState)
    : "Home";
}
