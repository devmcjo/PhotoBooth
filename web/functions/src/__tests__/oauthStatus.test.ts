/**
 * OAuth 구성 상태 판정(순수) — `domain/oauthStatus.ts`.
 *
 * 이 판정이 존재하는 이유는 2026-08-01 사고다: `GOOGLE_OAUTH_CLIENT_ID_WEB`에
 * `<A1의 웹 client_id>` 문자열이 그대로 배포돼 웹 로그인이 100% 실패했는데
 * **화면에 그 사실을 알리는 신호가 하나도 없었다.**
 */
import { classifyClientId, describeOAuthConfig } from "../domain/oauthStatus";

const WEB = "1234-web.apps.googleusercontent.com";
const DESKTOP = "5678-desktop.apps.googleusercontent.com";

describe("classifyClientId", () => {
  test("빈 값·공백·undefined·null → unset", () => {
    expect(classifyClientId("")).toBe("unset");
    expect(classifyClientId("   ")).toBe("unset");
    expect(classifyClientId(undefined)).toBe("unset");
    expect(classifyClientId(null)).toBe("unset");
  });

  test("정상 client_id → ok", () => {
    expect(classifyClientId(WEB)).toBe("ok");
    expect(classifyClientId(` ${DESKTOP} `)).toBe("ok");
  });

  test("치환되지 않은 플레이스홀더 → malformed", () => {
    expect(classifyClientId("<A1의 웹 client_id>")).toBe("malformed");
  });

  test("접미사가 없거나 잘린 값 → malformed", () => {
    expect(classifyClientId("1234-web")).toBe("malformed");
    expect(classifyClientId("1234-web.apps.googleusercontent.co")).toBe("malformed");
  });

  test("접미사뿐이고 앞의 식별자가 없으면 malformed", () => {
    expect(classifyClientId(".apps.googleusercontent.com")).toBe("malformed");
  });
});

describe("describeOAuthConfig", () => {
  test("두 종류 모두 정상 → ok/ok · 공유 아님 · 허용목록 개수", () => {
    expect(
      describeOAuthConfig({
        googleOAuthClients: {
          desktop: { clientId: DESKTOP },
          web: { clientId: WEB },
        },
        oauthRedirectAllowlist: ["a", "b", "c"],
      })
    ).toEqual({
      web: "ok",
      desktop: "ok",
      sharedClientId: false,
      redirectAllowlistCount: 3,
    });
  });

  test("web 미구성 → unset(desktop만 살아 있는 정상 배포)", () => {
    const status = describeOAuthConfig({
      googleOAuthClients: { desktop: { clientId: DESKTOP } },
      oauthRedirectAllowlist: [],
    });

    expect(status.web).toBe("unset");
    expect(status.desktop).toBe("ok");
    expect(status.sharedClientId).toBe(false);
  });

  test("desktop 값을 web에 복사한 오구성 → sharedClientId true", () => {
    const status = describeOAuthConfig({
      googleOAuthClients: {
        desktop: { clientId: DESKTOP },
        web: { clientId: DESKTOP },
      },
    });

    expect(status.sharedClientId).toBe(true);
    // 형식 자체는 정상이라 ok다 — "형식"과 "공유"는 다른 축이다.
    expect(status.web).toBe("ok");
  });

  test("둘 다 미설정이면 sharedClientId는 false다(빈 값끼리 같다고 보지 않는다)", () => {
    const status = describeOAuthConfig({});

    expect(status).toEqual({
      web: "unset",
      desktop: "unset",
      sharedClientId: false,
      redirectAllowlistCount: 0,
    });
  });

  test("반환값에 client_id 값이 들어 있지 않다", () => {
    const serialized = JSON.stringify(
      describeOAuthConfig({
        googleOAuthClients: {
          desktop: { clientId: DESKTOP },
          web: { clientId: WEB },
        },
        oauthRedirectAllowlist: ["https://kiosk.example/oauth2callback"],
      })
    );

    expect(serialized).not.toContain(WEB);
    expect(serialized).not.toContain(DESKTOP);
    expect(serialized).not.toContain("oauth2callback");
  });
});
