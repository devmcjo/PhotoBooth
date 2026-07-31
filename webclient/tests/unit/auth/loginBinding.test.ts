import { beforeEach, describe, expect, it } from "vitest";
import {
  runSignIn,
  type SignInActionDeps,
  type SignInPhase,
} from "@screens/login/useGoogleSignIn";
import type { LoginFailureReason } from "@domain/auth/loginFailure";
import { loginFailureMessageKey } from "@domain/auth/loginFailure";
import type { AppState } from "@domain/navigation/appState";
import type { StartSignInOutcome } from "@adapters/auth/googleSignIn";
import { loginStore } from "@shell/loginStore";
import { STRINGS } from "@ui/strings";

/**
 * `Login` 화면 배선 — 03 §3
 *
 * 리디렉트는 **[Google로 로그인] 탭에서만** 일어난다. 화면 진입·오류 표시·[닫기]로는
 * 어떤 네트워크·저장소 부수효과도 없다 → `start` 호출 횟수로 그것을 고정한다.
 */

interface Harness {
  readonly deps: SignInActionDeps;
  readonly phases: SignInPhase[];
  readonly failures: LoginFailureReason[];
  readonly starts: { readonly returnTo: AppState }[];
  clears: number;
}

function harness(
  outcome: StartSignInOutcome,
  overrides: Partial<SignInActionDeps> = {},
): Harness {
  const phases: SignInPhase[] = [];
  const failures: LoginFailureReason[] = [];
  const starts: { readonly returnTo: AppState }[] = [];
  const h: Harness = {
    phases,
    failures,
    starts,
    clears: 0,
    deps: {
      available: true,
      setPhase: (phase) => phases.push(phase),
      fail: (reason) => failures.push(reason),
      clear: () => {
        h.clears++;
      },
      start: (input) => {
        starts.push(input);
        return Promise.resolve(outcome);
      },
      returnTo: () => "FrameSelect",
      ...overrides,
    },
  };
  return h;
}

describe("runSignIn — 성공 경로", () => {
  it("직전 오류를 지우고 redirecting으로 바꾼 뒤 시작한다", async () => {
    const h = harness({ ok: true });
    await runSignIn(h.deps);

    expect(h.clears).toBe(1);
    expect(h.phases).toEqual(["redirecting"]);
    expect(h.starts).toEqual([{ returnTo: "FrameSelect" }]);
    expect(h.failures).toEqual([]);
  });

  it("★ 성공 시 phase를 idle로 되돌리지 않는다(곧 페이지가 사라진다 — 중복 클릭 방지)", async () => {
    const h = harness({ ok: true });
    await runSignIn(h.deps);
    expect(h.phases.at(-1)).toBe("redirecting");
  });

  it("복귀 지점을 그대로 실어 보낸다", async () => {
    const h = harness({ ok: true }, { returnTo: () => "Settings" });
    await runSignIn(h.deps);
    expect(h.starts[0]).toEqual({ returnTo: "Settings" });
  });
});

describe("runSignIn — 미구성(available=false)", () => {
  it("★ startGoogleSignIn을 부르지 않고 clientNotConfigured를 세운다", async () => {
    const h = harness({ ok: true }, { available: false });
    await runSignIn(h.deps);

    expect(h.starts).toHaveLength(0);
    expect(h.failures).toEqual(["clientNotConfigured"]);
    // 리디렉트 국면으로 들어가지 않는다(버튼이 없는데 "이동 중"이 되면 안 된다).
    expect(h.phases).toEqual([]);
  });
});

describe("runSignIn — 실패 경로", () => {
  it("실패면 phase가 idle로 복귀하고 사유가 그대로 세워진다", async () => {
    for (const reason of [
      "network",
      "clientNotConfigured",
      "rejected",
    ] satisfies LoginFailureReason[]) {
      const h = harness({ ok: false, reason });
      await runSignIn(h.deps);
      expect(h.phases, reason).toEqual(["redirecting", "idle"]);
      expect(h.failures, reason).toEqual([reason]);
    }
  });

  it("재시도 전에 직전 오류를 지운다(낡은 문구가 남지 않는다)", async () => {
    const h = harness({ ok: false, reason: "network" });
    await runSignIn(h.deps);
    await runSignIn(h.deps);
    expect(h.clears).toBe(2);
  });
});

describe("loginStore — 콜백이 실어 보낸 오류 전달", () => {
  beforeEach(() => {
    loginStore.setState({ notice: null });
  });

  it("fail → notice · clear → null", () => {
    expect(loginStore.getState().notice).toBeNull();
    loginStore.getState().fail("rejected");
    expect(loginStore.getState().notice).toBe("rejected");
    loginStore.getState().clear();
    expect(loginStore.getState().notice).toBeNull();
  });

  it("진단 사유를 그대로 보관한다(문구 접기는 화면이 한다)", () => {
    loginStore.getState().fail("redirectRejected");
    // 스토어는 400을 잊지 않고, 화면만 네트워크 문구로 보여준다.
    expect(loginStore.getState().notice).toBe("redirectRejected");
    expect(STRINGS.login.errors[loginFailureMessageKey("redirectRejected")]).toBe(
      "Google 로그인 중 오류가 발생했습니다. 네트워크를 확인해 주세요.",
    );
  });

  it("나중 오류가 앞 오류를 덮는다", () => {
    loginStore.getState().fail("cancelled");
    loginStore.getState().fail("notConfigured");
    expect(loginStore.getState().notice).toBe("notConfigured");
  });
});

describe("문구 카탈로그 — 규격(analysis/13 §14)과 문자 단위 일치", () => {
  it("5종 문구가 규격과 같다", () => {
    expect(STRINGS.login.errors).toEqual({
      cancelled: "Google 로그인이 취소되었습니다.",
      rejected:
        "이 Google 계정으로는 로그인할 수 없습니다. 허용된 계정·도메인인지 확인해 주세요.",
      notConfigured: "Google 로그인이 구성되지 않았습니다. 관리자에게 문의하세요.",
      network: "Google 로그인 중 오류가 발생했습니다. 네트워크를 확인해 주세요.",
      clientNotConfigured: "로그인이 구성되지 않았습니다. 관리자에게 문의하세요.",
    });
  });

  it("세션 만료 문구가 '세션'으로 시작한다(07 §4.3 · 12 C10)", () => {
    expect(STRINGS.error.sessionExpired).toBe("세션이 만료되었습니다. 다시 로그인해 주세요.");
  });
});
