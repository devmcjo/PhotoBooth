/**
 * 웹 클라이언트 OAuth 지원 회귀 테스트 — B1(redirectUri 허용목록) · B2(audience 목록화)
 * 근거: docs/web-client/08 §4.1·§4.2
 *
 * 핵심 불변식 2개:
 *   ① **배포된 Windows(desktop) 경로가 무변경으로 동작한다** — loopback 통과, clientKind 미지정 = desktop.
 *   ② **허용 목록은 완전 일치만** — prefix 매칭은 open redirect / SSRF 통로다.
 */
import type { TokenPayload } from "google-auth-library";
import { loadConfig, resetConfigCache } from "../config";
import {
  OAUTH_CLIENT_KINDS,
  validateClientKind,
  validateRedirectUri,
} from "../domain/validation";
import { mapGoogleAuthError } from "../routes/auth";
import {
  acceptableAudiences,
  assertPayloadAndExtractEmail,
  GoogleAuthError,
  type GoogleAuthConfig,
  type GoogleVerifyInput,
} from "../services/googleAuth";

const DESKTOP_ID = "desktop-id.apps.googleusercontent.com";
const WEB_ID = "web-id.apps.googleusercontent.com";
const WEB_REDIRECT = "https://mcphoto-955fb-kiosk.web.app/oauth2callback";
const DEV_REDIRECT = "http://localhost:5173/oauth2callback";

// ────────────────────────────── B1: redirectUri ──────────────────────────────

describe("validateRedirectUri — loopback 무변경 (배포된 Windows 클라이언트)", () => {
  const allowlist = [WEB_REDIRECT];

  it.each([
    "http://127.0.0.1:53412/",
    "http://127.0.0.1/",
    "http://localhost:5000/",
    "http://localhost/",
  ])("%s 는 허용 목록과 무관하게 통과한다", (uri) => {
    const res = validateRedirectUri(uri, allowlist);
    expect(res.ok).toBe(true);
  });

  it("허용 목록이 비어 있어도 loopback은 통과한다(포트가 매번 달라 등록 불가)", () => {
    expect(validateRedirectUri("http://127.0.0.1:61234/", []).ok).toBe(true);
  });

  it("loopback이지만 경로·쿼리가 붙으면 종전 규칙대로 거부된다", () => {
    expect(validateRedirectUri("http://127.0.0.1:5000/callback", allowlist).ok).toBe(false);
    expect(validateRedirectUri("http://localhost:5000/?a=1", allowlist).ok).toBe(false);
  });
});

describe("validateRedirectUri — 허용 목록 (웹)", () => {
  const allowlist = [WEB_REDIRECT, DEV_REDIRECT];

  it("허용 목록의 URI가 통과한다", () => {
    for (const uri of allowlist) {
      const res = validateRedirectUri(uri, allowlist);
      expect(res.ok).toBe(true);
      if (res.ok) expect(res.value).toBe(uri);
    }
  });

  it("허용 목록 밖의 https URI는 거부된다", () => {
    expect(validateRedirectUri("https://evil.com/oauth2callback", allowlist).ok).toBe(false);
    expect(validateRedirectUri("https://mcphoto-955fb.web.app/oauth2callback", allowlist).ok).toBe(
      false
    );
  });

  it("prefix만 같은 호스트는 거부된다 — open redirect 방어의 핵심", () => {
    expect(
      validateRedirectUri(
        "https://mcphoto-955fb-kiosk.web.app.evil.com/oauth2callback",
        allowlist
      ).ok
    ).toBe(false);
    expect(
      validateRedirectUri("https://mcphoto-955fb-kiosk.web.app/oauth2callback/../x", allowlist).ok
    ).toBe(false);
  });

  it("쿼리·프래그먼트가 붙으면 완전 일치가 깨져 거부된다", () => {
    expect(validateRedirectUri(`${WEB_REDIRECT}?a=1`, allowlist).ok).toBe(false);
    expect(validateRedirectUri(`${WEB_REDIRECT}#frag`, allowlist).ok).toBe(false);
  });

  it("대소문자·트레일링 슬래시 차이도 거부된다(완전 일치)", () => {
    expect(validateRedirectUri(`${WEB_REDIRECT}/`, allowlist).ok).toBe(false);
    expect(validateRedirectUri(WEB_REDIRECT.toUpperCase(), allowlist).ok).toBe(false);
  });

  it("앞뒤 공백은 트림 후 비교한다", () => {
    expect(validateRedirectUri(`  ${WEB_REDIRECT}  `, allowlist).ok).toBe(true);
  });

  it("형식 방어는 종전과 같다(빈 값·길이·타입)", () => {
    expect(validateRedirectUri("", allowlist).ok).toBe(false);
    expect(validateRedirectUri("   ", allowlist).ok).toBe(false);
    expect(validateRedirectUri("x".repeat(257), allowlist).ok).toBe(false);
    expect(validateRedirectUri(undefined, allowlist).ok).toBe(false);
    expect(validateRedirectUri(123, allowlist).ok).toBe(false);
  });

  it("허용 목록이 비면 https는 전부 거부된다(구성 전 기본 상태)", () => {
    expect(validateRedirectUri(WEB_REDIRECT, []).ok).toBe(false);
  });

  it("URL로 파싱되지 않는 값도 목록에 없으면 거부된다", () => {
    expect(validateRedirectUri("not a url", allowlist).ok).toBe(false);
  });
});

// ────────────────────────────── B2: clientKind ───────────────────────────────

describe("validateClientKind", () => {
  it("미지정은 desktop이다(하위 호환)", () => {
    for (const value of [undefined, null]) {
      const res = validateClientKind(value);
      expect(res.ok).toBe(true);
      if (res.ok) expect(res.value).toBe("desktop");
    }
  });

  it.each(OAUTH_CLIENT_KINDS)("%s 는 허용된다", (kind) => {
    const res = validateClientKind(kind);
    expect(res.ok).toBe(true);
    if (res.ok) expect(res.value).toBe(kind);
  });

  it("화이트리스트 밖 문자열은 거부된다 — 임의 값으로 서버 구성을 고르지 못하게", () => {
    for (const bad of ["admin", "Desktop", "WEB", "", " ", "desktop,web"]) {
      expect({ bad, ok: validateClientKind(bad).ok }).toEqual({ bad, ok: false });
    }
  });

  it("문자열이 아니면 거부된다", () => {
    expect(validateClientKind(1).ok).toBe(false);
    expect(validateClientKind({}).ok).toBe(false);
  });
});

// ─────────────────────────── B2: config 종류별 구성 ───────────────────────────

describe("loadConfig — 종류별 OAuth 클라이언트", () => {
  const KEYS = [
    "JWT_SECRET",
    "CLIENT_API_KEYS",
    "STORAGE_BUCKET",
    "GOOGLE_OAUTH_CLIENT_ID",
    "GOOGLE_OAUTH_CLIENT_SECRET",
    "GOOGLE_OAUTH_CLIENT_ID_WEB",
    "GOOGLE_OAUTH_CLIENT_SECRET_WEB",
    "OAUTH_REDIRECT_ALLOWLIST",
  ];
  const saved: Record<string, string | undefined> = {};

  beforeAll(() => {
    for (const k of KEYS) saved[k] = process.env[k];
  });
  afterAll(() => {
    for (const k of KEYS) {
      if (saved[k] === undefined) delete process.env[k];
      else process.env[k] = saved[k];
    }
    resetConfigCache();
  });

  beforeEach(() => {
    resetConfigCache();
    process.env.JWT_SECRET = "test-jwt";
    process.env.CLIENT_API_KEYS = "test-key";
    process.env.STORAGE_BUCKET = "test-bucket";
    delete process.env.GOOGLE_OAUTH_CLIENT_ID;
    delete process.env.GOOGLE_OAUTH_CLIENT_SECRET;
    delete process.env.GOOGLE_OAUTH_CLIENT_ID_WEB;
    delete process.env.GOOGLE_OAUTH_CLIENT_SECRET_WEB;
    delete process.env.OAUTH_REDIRECT_ALLOWLIST;
  });

  it("desktop만 구성되면 web은 없고 audience는 1개다(현행 배포 상태)", () => {
    process.env.GOOGLE_OAUTH_CLIENT_ID = DESKTOP_ID;
    process.env.GOOGLE_OAUTH_CLIENT_SECRET = "desktop-secret";

    const cfg = loadConfig();
    expect(cfg.googleOAuthEnabled).toBe(true);
    expect(cfg.googleOAuthClients.desktop).toEqual({
      clientId: DESKTOP_ID,
      clientSecret: "desktop-secret",
    });
    expect(cfg.googleOAuthClients.web).toBeUndefined();
    expect(cfg.googleOAuthAudiences).toEqual([DESKTOP_ID]);
  });

  it("둘 다 구성되면 audience가 2개다", () => {
    process.env.GOOGLE_OAUTH_CLIENT_ID = DESKTOP_ID;
    process.env.GOOGLE_OAUTH_CLIENT_SECRET = "desktop-secret";
    process.env.GOOGLE_OAUTH_CLIENT_ID_WEB = WEB_ID;
    process.env.GOOGLE_OAUTH_CLIENT_SECRET_WEB = "web-secret";

    const cfg = loadConfig();
    expect(cfg.googleOAuthAudiences).toEqual([DESKTOP_ID, WEB_ID]);
    expect(cfg.googleOAuthClients.web?.clientSecret).toBe("web-secret");
  });

  it("web만 구성돼도 활성이다(desktop 없이 웹만 운영하는 배포)", () => {
    process.env.GOOGLE_OAUTH_CLIENT_ID_WEB = WEB_ID;
    process.env.GOOGLE_OAUTH_CLIENT_SECRET_WEB = "web-secret";

    const cfg = loadConfig();
    expect(cfg.googleOAuthEnabled).toBe(true);
    expect(cfg.googleOAuthClients.desktop).toBeUndefined();
    expect(cfg.googleOAuthAudiences).toEqual([WEB_ID]);
  });

  it("web id만 있고 secret이 없으면 조기 실패한다(desktop과 같은 규칙)", () => {
    process.env.GOOGLE_OAUTH_CLIENT_ID_WEB = WEB_ID;
    expect(() => loadConfig()).toThrow(/GOOGLE_OAUTH_CLIENT_SECRET_WEB/);
  });

  it("web secret만 있으면(placeholder 등록 상태) 정상 비활성이다 — 배포가 깨지지 않는다", () => {
    process.env.GOOGLE_OAUTH_CLIENT_SECRET_WEB = "placeholder";
    const cfg = loadConfig();
    expect(cfg.googleOAuthEnabled).toBe(false);
    expect(cfg.googleOAuthClients.web).toBeUndefined();
  });

  it("허용 목록을 CSV로 읽고 공백을 트림한다", () => {
    process.env.OAUTH_REDIRECT_ALLOWLIST = ` ${WEB_REDIRECT} , ${DEV_REDIRECT} , `;
    expect(loadConfig().oauthRedirectAllowlist).toEqual([WEB_REDIRECT, DEV_REDIRECT]);
  });

  it("허용 목록 미설정은 빈 배열이다(웹 로그인 비허용 기본값)", () => {
    expect(loadConfig().oauthRedirectAllowlist).toEqual([]);
  });
});

// ───────────────────────── B2: audience 검증 목록화 ──────────────────────────

describe("acceptableAudiences", () => {
  it("목록이 없으면 code 교환에 쓴 client_id 하나로 폴백한다(하위 호환)", () => {
    expect(acceptableAudiences({ clientId: DESKTOP_ID, clientSecret: "s" })).toEqual([DESKTOP_ID]);
    expect(acceptableAudiences({ clientId: DESKTOP_ID, clientSecret: "s", audiences: [] })).toEqual(
      [DESKTOP_ID]
    );
  });

  it("빈 문자열은 목록에서 걸러낸다", () => {
    expect(
      acceptableAudiences({ clientId: DESKTOP_ID, clientSecret: "s", audiences: ["", WEB_ID] })
    ).toEqual([WEB_ID]);
  });
});

describe("assertPayloadAndExtractEmail — audience 목록", () => {
  const cfg: GoogleAuthConfig = {
    clientId: WEB_ID,
    clientSecret: "web-secret",
    audiences: [DESKTOP_ID, WEB_ID],
  };
  const input: GoogleVerifyInput = {
    code: "code",
    codeVerifier: "A".repeat(43),
    redirectUri: WEB_REDIRECT,
  };

  function payload(overrides: Partial<TokenPayload> = {}): TokenPayload {
    return {
      iss: "https://accounts.google.com",
      aud: WEB_ID,
      sub: "1",
      iat: Math.floor(Date.now() / 1000) - 10,
      exp: Math.floor(Date.now() / 1000) + 3600,
      email: "Owner@Example.com",
      email_verified: true,
      ...overrides,
    };
  }

  it.each([DESKTOP_ID, WEB_ID])("목록에 있는 aud(%s)가 통과한다", (aud) => {
    expect(assertPayloadAndExtractEmail(payload({ aud }), cfg, input)).toBe("owner@example.com");
  });

  it("목록 밖 aud는 거부된다", () => {
    expect(() =>
      assertPayloadAndExtractEmail(payload({ aud: "other.apps.googleusercontent.com" }), cfg, input)
    ).toThrow(GoogleAuthError);
  });

  it("aud가 문자열이 아니면 거부된다(배열 aud 방어)", () => {
    expect(() =>
      assertPayloadAndExtractEmail(
        payload({ aud: [WEB_ID] as unknown as string }),
        cfg,
        input
      )
    ).toThrow(GoogleAuthError);
  });

  it("email_verified: false는 여전히 거부된다(목록화가 다른 방어를 약화시키지 않았다)", () => {
    expect(() =>
      assertPayloadAndExtractEmail(payload({ email_verified: false }), cfg, input)
    ).toThrow(GoogleAuthError);
  });

  it("issuer·만료 방어도 그대로다", () => {
    expect(() => assertPayloadAndExtractEmail(payload({ iss: "https://evil" }), cfg, input)).toThrow(
      GoogleAuthError
    );
    expect(() =>
      assertPayloadAndExtractEmail(
        payload({ exp: Math.floor(Date.now() / 1000) - 1 }),
        cfg,
        input
      )
    ).toThrow(GoogleAuthError);
  });
});

// ───────── 라우트 매핑: 구성 오류(501) vs 계정 거부(401) — 2026-08-01 ─────────

describe("mapGoogleAuthError — 라우트 응답 매핑", () => {
  it("kind:'clientConfig' → 501 not_implemented(운영자 구성 오류)", () => {
    const http = mapGoogleAuthError(
      new GoogleAuthError("code 교환 실패: invalid_client", "clientConfig")
    );
    expect(http.status).toBe(501);
    expect(http.code).toBe("not_implemented");
    expect(http.message).toBe("Google 로그인이 구성되지 않았습니다.");
  });

  it("kind:'rejected' → 401 문구가 한 글자도 바뀌지 않는다(열거 방지 유지)", () => {
    // 기본값(kind 미지정)도 rejected여야 한다 — 기존 throw 지점의 동작 보존.
    for (const err of [
      new GoogleAuthError("code 교환 실패: invalid_grant", "rejected"),
      new GoogleAuthError("허용되지 않은 hosted domain."),
    ]) {
      const http = mapGoogleAuthError(err);
      expect(http.status).toBe(401);
      expect(http.code).toBe("unauthorized");
      expect(http.message).toBe(
        "이 Google 계정으로는 로그인할 수 없습니다. 허용된 계정·도메인인지 확인해 주세요."
      );
    }
  });
});
