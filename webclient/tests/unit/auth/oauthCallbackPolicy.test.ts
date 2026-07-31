import { describe, expect, it } from "vitest";
import { APP_STATES } from "@domain/navigation/appState";
import { canTransition } from "@domain/navigation/stateMachine";
import {
  decideOauthCallback,
  OAUTH_FLOW_TIMEOUT_MS,
  parseOauthCallbackParams,
  parseOauthPendingState,
  resolveOauthReturnTo,
  type OauthPendingState,
} from "@domain/auth/oauthCallbackPolicy";

/**
 * 콜백 판정 — 07 §2.2 5단계
 *
 * **검사 순서가 계약이다.** state 대조가 error보다 앞에 있어야 검증되지 않은 콜백의
 * 파라미터를 해석하지 않는다.
 */

const PENDING: OauthPendingState = {
  codeVerifier: "v".repeat(43),
  state: "state-abc",
  nonce: "nonce-abc",
  returnTo: "FrameSelect",
  startedAt: 1_000_000,
};

const NOW = PENDING.startedAt + 5_000;

describe("parseOauthCallbackParams", () => {
  it("code·state·error를 뽑는다(선행 `?` 유무 무관)", () => {
    expect(parseOauthCallbackParams("?code=c1&state=s1")).toEqual({
      code: "c1",
      state: "s1",
      error: null,
    });
    expect(parseOauthCallbackParams("code=c1&state=s1").code).toBe("c1");
  });

  it("빈 값·공백만 있는 값은 부재로 본다", () => {
    expect(parseOauthCallbackParams("?code=&state=%20&error=")).toEqual({
      code: null,
      state: null,
      error: null,
    });
  });

  it("Google의 error 파라미터를 읽는다", () => {
    expect(parseOauthCallbackParams("?error=access_denied&state=s1").error).toBe("access_denied");
  });

  it("쿼리가 없으면 전부 null이다", () => {
    expect(parseOauthCallbackParams("")).toEqual({ code: null, state: null, error: null });
  });

  it("URL 인코딩을 디코드한다", () => {
    expect(parseOauthCallbackParams("?code=a%2Fb%3Dc").code).toBe("a/b=c");
  });
});

describe("parseOauthPendingState — 저장소 값 방어", () => {
  it("정상 값을 왕복한다", () => {
    expect(parseOauthPendingState({ ...PENDING })).toEqual(PENDING);
  });

  it("객체가 아니면 null이다", () => {
    for (const raw of [null, undefined, 1, "x", true, []]) {
      // 배열은 객체이지만 필수 필드가 없어 null이 된다.
      expect(parseOauthPendingState(raw)).toBeNull();
    }
  });

  it.each(["codeVerifier", "state", "nonce"] as const)("%s 누락은 null이다", (key) => {
    const raw: Record<string, unknown> = { ...PENDING };
    delete raw[key];
    expect(parseOauthPendingState(raw)).toBeNull();
  });

  it.each(["codeVerifier", "state", "nonce"] as const)("%s 타입 오류는 null이다", (key) => {
    expect(parseOauthPendingState({ ...PENDING, [key]: 42 })).toBeNull();
  });

  it("startedAt이 숫자가 아니거나 유한하지 않으면 null이다", () => {
    expect(parseOauthPendingState({ ...PENDING, startedAt: "1000" })).toBeNull();
    expect(parseOauthPendingState({ ...PENDING, startedAt: Number.NaN })).toBeNull();
    expect(parseOauthPendingState({ ...PENDING, startedAt: Number.POSITIVE_INFINITY })).toBeNull();
  });

  it("returnTo가 없거나 문자열이 아니면 빈 문자열로 두고 거부하지 않는다", () => {
    // 복귀 지점 하나 때문에 성공한 로그인을 버리지 않는다 — clamp가 Home으로 떨어뜨린다.
    expect(parseOauthPendingState({ ...PENDING, returnTo: undefined })?.returnTo).toBe("");
    expect(parseOauthPendingState({ ...PENDING, returnTo: 7 })?.returnTo).toBe("");
  });
});

describe("decideOauthCallback — 검사 순서가 계약이다", () => {
  it("pending이 없으면 no-pending이다(다른 어떤 파라미터도 보지 않는다)", () => {
    expect(decideOauthCallback({ code: "c", state: "s", error: null }, null, NOW)).toEqual({
      kind: "abort",
      reason: "no-pending",
    });
  });

  it("state 불일치는 state-mismatch다", () => {
    expect(
      decideOauthCallback({ code: "c", state: "other", error: null }, PENDING, NOW),
    ).toEqual({ kind: "abort", reason: "state-mismatch" });
  });

  it("state 부재도 state-mismatch다", () => {
    expect(decideOauthCallback({ code: "c", state: null, error: null }, PENDING, NOW)).toEqual({
      kind: "abort",
      reason: "state-mismatch",
    });
  });

  it("★ error와 state 불일치가 동시면 state-mismatch다(순서 보장)", () => {
    expect(
      decideOauthCallback({ code: null, state: "other", error: "access_denied" }, PENDING, NOW),
    ).toEqual({ kind: "abort", reason: "state-mismatch" });
  });

  it("state가 맞고 error가 있으면 provider-error다(사용자 취소 포함)", () => {
    expect(
      decideOauthCallback(
        { code: null, state: PENDING.state, error: "access_denied" },
        PENDING,
        NOW,
      ),
    ).toEqual({ kind: "abort", reason: "provider-error" });
  });

  it("★ error가 timeout보다 앞이다", () => {
    expect(
      decideOauthCallback(
        { code: "c", state: PENDING.state, error: "server_error" },
        PENDING,
        PENDING.startedAt + OAUTH_FLOW_TIMEOUT_MS + 10_000,
      ),
    ).toEqual({ kind: "abort", reason: "provider-error" });
  });

  it("3분 경계: 정확히 180000ms는 통과하고 180001ms는 timeout이다", () => {
    expect(OAUTH_FLOW_TIMEOUT_MS).toBe(180_000);

    const ok = decideOauthCallback(
      { code: "c", state: PENDING.state, error: null },
      PENDING,
      PENDING.startedAt + OAUTH_FLOW_TIMEOUT_MS,
    );
    expect(ok.kind).toBe("exchange");

    const late = decideOauthCallback(
      { code: "c", state: PENDING.state, error: null },
      PENDING,
      PENDING.startedAt + OAUTH_FLOW_TIMEOUT_MS + 1,
    );
    expect(late).toEqual({ kind: "abort", reason: "timeout" });
  });

  it("시계가 뒤로 간 경우(음수 경과)는 timeout이 아니다", () => {
    expect(
      decideOauthCallback(
        { code: "c", state: PENDING.state, error: null },
        PENDING,
        PENDING.startedAt - 10_000,
      ).kind,
    ).toBe("exchange");
  });

  it("code가 없으면 no-code다", () => {
    expect(
      decideOauthCallback({ code: null, state: PENDING.state, error: null }, PENDING, NOW),
    ).toEqual({ kind: "abort", reason: "no-code" });
  });

  it("전부 통과하면 exchange이고 pending의 비밀값을 실어 준다", () => {
    expect(
      decideOauthCallback({ code: "code-1", state: PENDING.state, error: null }, PENDING, NOW),
    ).toEqual({
      kind: "exchange",
      code: "code-1",
      codeVerifier: PENDING.codeVerifier,
      nonce: PENDING.nonce,
      returnTo: "FrameSelect",
    });
  });

  it("returnTo가 허용 밖이면 exchange 결과가 Home으로 clamp된다", () => {
    const decision = decideOauthCallback(
      { code: "c", state: PENDING.state, error: null },
      { ...PENDING, returnTo: "Capture" },
      NOW,
    );
    expect(decision.kind === "exchange" && decision.returnTo).toBe("Home");
  });
});

describe("resolveOauthReturnTo — 콜드 스타트에서 합법인 화면만", () => {
  it("4종만 통과한다", () => {
    for (const allowed of ["Home", "FrameSelect", "Settings", "Account"] as const) {
      expect(resolveOauthReturnTo(allowed)).toBe(allowed);
    }
  });

  it("그 외 상태·미지의 문자열·null은 전부 Home이다", () => {
    for (const state of APP_STATES) {
      const expected =
        state === "Home" || state === "FrameSelect" || state === "Settings" || state === "Account"
          ? state
          : "Home";
      expect(resolveOauthReturnTo(state), state).toBe(expected);
    }
    expect(resolveOauthReturnTo(null)).toBe("Home");
    expect(resolveOauthReturnTo("")).toBe("Home");
    expect(resolveOauthReturnTo("nonsense")).toBe("Home");
    expect(resolveOauthReturnTo("  FrameSelect  ")).toBe("FrameSelect");
  });

  it("Login으로는 복귀하지 않는다(로그인 성공 후 로그인 화면은 무의미하다)", () => {
    expect(resolveOauthReturnTo("Login")).toBe("Home");
  });

  it("허용 집합은 `canTransition('Home', x)` 참인 집합에서 Login을 뺀 것과 같다", () => {
    // 이 관계가 깨지면 성공한 로그인 뒤 `go()`가 거부돼 손님이 Home에 남는다.
    const reachableFromHome = APP_STATES.filter(
      (to) => to !== "Home" && to !== "Login" && canTransition("Home", to),
    );
    const allowed = APP_STATES.filter((to) => to !== "Home" && resolveOauthReturnTo(to) === to);
    expect(allowed.slice().sort()).toEqual(reachableFromHome.slice().sort());
  });
});
