import { describe, expect, it } from "vitest";
import {
  buildAuthorizeUrl,
  GOOGLE_AUTHORIZE_ENDPOINT,
  OAUTH_CALLBACK_PATH,
  OAUTH_SCOPE,
  oauthRedirectUri,
} from "@domain/auth/authorizeUrl";

/**
 * 인가 URL 조립 — 07 §2.2
 *
 * Google은 파라미터가 어긋나면 **우리 페이지에 도달하기 전에** 자체 오류 화면을 띄운다
 * (로그가 남지 않는다) → 조립 문자열 전체를 고정해 회귀를 여기서 잡는다.
 */

const INPUT = {
  clientId: "1234-abc.apps.googleusercontent.com",
  redirectUri: "https://mcphoto-955fb.web.app/oauth2callback",
  codeChallenge: "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM",
  state: "state-token-43chars-aaaaaaaaaaaaaaaaaaaaaaa",
  nonce: "nonce-token-43chars-bbbbbbbbbbbbbbbbbbbbbbb",
} as const;

describe("buildAuthorizeUrl — 문자열 전체 고정", () => {
  it("파라미터 순서·인코딩이 규격과 같다", () => {
    expect(buildAuthorizeUrl(INPUT)).toBe(
      "https://accounts.google.com/o/oauth2/v2/auth" +
        "?client_id=1234-abc.apps.googleusercontent.com" +
        "&redirect_uri=https%3A%2F%2Fmcphoto-955fb.web.app%2Foauth2callback" +
        "&response_type=code" +
        "&scope=openid%20email%20profile" +
        "&code_challenge=E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM" +
        "&code_challenge_method=S256" +
        "&state=state-token-43chars-aaaaaaaaaaaaaaaaaaaaaaa" +
        "&nonce=nonce-token-43chars-bbbbbbbbbbbbbbbbbbbbbbb" +
        "&prompt=select_account",
    );
  });

  it("scope의 공백은 `%20`이다(`+`가 아니다 — URLSearchParams 금지 이유)", () => {
    const url = buildAuthorizeUrl(INPUT);
    expect(url).toContain("scope=openid%20email%20profile");
    expect(url).not.toContain("scope=openid+email+profile");
    expect(OAUTH_SCOPE).toBe("openid email profile");
  });

  it("`prompt=select_account`가 있다(키오스크 필수 — 이전 손님 계정 원탭 로그인 차단)", () => {
    expect(buildAuthorizeUrl(INPUT)).toContain("prompt=select_account");
  });

  it("`code_challenge_method=S256`이다(plain 아님)", () => {
    expect(buildAuthorizeUrl(INPUT)).toContain("code_challenge_method=S256");
  });

  it("refresh token을 요구하는 파라미터가 없다", () => {
    const url = buildAuthorizeUrl(INPUT);
    expect(url).not.toContain("access_type");
    expect(url).not.toContain("prompt=consent");
    expect(url).not.toContain("approval_prompt");
  });

  it("엔드포인트가 v2 authorize다", () => {
    expect(GOOGLE_AUTHORIZE_ENDPOINT).toBe("https://accounts.google.com/o/oauth2/v2/auth");
    expect(buildAuthorizeUrl(INPUT).startsWith(`${GOOGLE_AUTHORIZE_ENDPOINT}?`)).toBe(true);
  });

  it("특수문자가 들어간 값도 인코딩된다(파라미터 주입 방지)", () => {
    const url = buildAuthorizeUrl({ ...INPUT, state: "a&b=c d" });
    expect(url).toContain("state=a%26b%3Dc%20d");
  });
});

describe("oauthRedirectUri — 서버 허용 목록과 문자 단위로 같아야 한다", () => {
  it("트레일링 슬래시를 제거한 뒤 콜백 경로를 붙인다", () => {
    expect(oauthRedirectUri("https://h")).toBe("https://h/oauth2callback");
    expect(oauthRedirectUri("https://h/")).toBe("https://h/oauth2callback");
    expect(oauthRedirectUri("https://h///")).toBe("https://h/oauth2callback");
  });

  it("공백을 흘려보내지 않는다", () => {
    expect(oauthRedirectUri("  https://h/  ")).toBe("https://h/oauth2callback");
  });

  it("개발 서버 오리진(포트 5173)도 그대로 조립한다", () => {
    // ↔ 서버 OAUTH_REDIRECT_ALLOWLIST · Google Console 등록값(14 §2.2·§3.3).
    expect(oauthRedirectUri("http://localhost:5173")).toBe(
      "http://localhost:5173/oauth2callback",
    );
  });

  it("콜백 경로 상수가 Google Console 등록 경로다", () => {
    expect(OAUTH_CALLBACK_PATH).toBe("/oauth2callback");
  });
});
