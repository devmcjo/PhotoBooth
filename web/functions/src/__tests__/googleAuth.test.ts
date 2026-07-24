import type { TokenPayload } from "google-auth-library";
import {
  assertPayloadAndExtractEmail,
  GoogleAuthConfig,
  GoogleAuthError,
  GoogleVerifyInput,
  OAuth2ClientFactory,
  OAuth2ClientLike,
  verifyGoogleCodeAndGetEmail,
} from "../services/googleAuth";

const CLIENT_ID = "test-client-id.apps.googleusercontent.com";
const CFG: GoogleAuthConfig = {
  clientId: CLIENT_ID,
  clientSecret: "test-client-secret",
};

const INPUT: GoogleVerifyInput = {
  code: "auth-code-abc",
  codeVerifier: "A".repeat(43),
  redirectUri: "http://127.0.0.1:52001/",
};

/** 유효한 기본 payload 생성(미래 exp, 올바른 iss/aud, verified email). */
function makePayload(overrides: Partial<TokenPayload> = {}): TokenPayload {
  return {
    iss: "https://accounts.google.com",
    aud: CLIENT_ID,
    sub: "1234567890",
    iat: Math.floor(Date.now() / 1000) - 10,
    exp: Math.floor(Date.now() / 1000) + 3600,
    email: "owner@example.com",
    email_verified: true,
    ...overrides,
  };
}

/** getToken/verifyIdToken 동작을 지정할 수 있는 mock 클라이언트 팩토리. */
function mockFactory(opts: {
  idToken?: string | null;
  getTokenThrows?: Error;
  verifyThrows?: Error;
  payload?: TokenPayload | undefined;
}): OAuth2ClientFactory {
  // idToken 키가 opts에 명시되면 그 값을(null 포함) 그대로 쓰고, 없으면 기본 더미 토큰.
  const idToken = "idToken" in opts ? opts.idToken : "dummy-id-token";
  return (): OAuth2ClientLike => ({
    async getToken() {
      if (opts.getTokenThrows) throw opts.getTokenThrows;
      return { tokens: { id_token: idToken } };
    },
    async verifyIdToken() {
      if (opts.verifyThrows) throw opts.verifyThrows;
      return { getPayload: () => opts.payload };
    },
  });
}

describe("googleAuth — assertPayloadAndExtractEmail(순수 payload 검증)", () => {
  test("유효 payload → 소문자 정규화된 email 반환", () => {
    const email = assertPayloadAndExtractEmail(
      makePayload({ email: "Owner@Example.COM" }),
      CFG,
      INPUT
    );
    expect(email).toBe("owner@example.com");
  });

  test("payload 없음 → GoogleAuthError", () => {
    expect(() => assertPayloadAndExtractEmail(undefined, CFG, INPUT)).toThrow(GoogleAuthError);
  });

  test("aud 불일치 → GoogleAuthError", () => {
    expect(() =>
      assertPayloadAndExtractEmail(makePayload({ aud: "other-client" }), CFG, INPUT)
    ).toThrow(GoogleAuthError);
  });

  test("iss 불일치 → GoogleAuthError", () => {
    expect(() =>
      assertPayloadAndExtractEmail(makePayload({ iss: "https://evil.example" }), CFG, INPUT)
    ).toThrow(GoogleAuthError);
  });

  test("iss=accounts.google.com(스킴 없음)도 허용", () => {
    const email = assertPayloadAndExtractEmail(
      makePayload({ iss: "accounts.google.com" }),
      CFG,
      INPUT
    );
    expect(email).toBe("owner@example.com");
  });

  test("만료(exp 과거) → GoogleAuthError", () => {
    expect(() =>
      assertPayloadAndExtractEmail(
        makePayload({ exp: Math.floor(Date.now() / 1000) - 5 }),
        CFG,
        INPUT
      )
    ).toThrow(GoogleAuthError);
  });

  test("email_verified=false → GoogleAuthError", () => {
    expect(() =>
      assertPayloadAndExtractEmail(makePayload({ email_verified: false }), CFG, INPUT)
    ).toThrow(GoogleAuthError);
  });

  test("email 없음 → GoogleAuthError", () => {
    expect(() =>
      assertPayloadAndExtractEmail(makePayload({ email: undefined }), CFG, INPUT)
    ).toThrow(GoogleAuthError);
  });

  test("nonce 요청 있음 + payload nonce 일치 → 통과", () => {
    const email = assertPayloadAndExtractEmail(
      makePayload({ nonce: "abc123" }),
      CFG,
      { ...INPUT, nonce: "abc123" }
    );
    expect(email).toBe("owner@example.com");
  });

  test("nonce 요청 있음 + payload nonce 불일치 → GoogleAuthError", () => {
    expect(() =>
      assertPayloadAndExtractEmail(makePayload({ nonce: "server-side" }), CFG, {
        ...INPUT,
        nonce: "client-side",
      })
    ).toThrow(GoogleAuthError);
  });

  test("nonce 요청 있음 + payload nonce 없음 → GoogleAuthError", () => {
    expect(() =>
      assertPayloadAndExtractEmail(makePayload({ nonce: undefined }), CFG, {
        ...INPUT,
        nonce: "client-side",
      })
    ).toThrow(GoogleAuthError);
  });

  test("nonce 요청 없음 → payload nonce 무관하게 통과", () => {
    const email = assertPayloadAndExtractEmail(makePayload({ nonce: "whatever" }), CFG, INPUT);
    expect(email).toBe("owner@example.com");
  });

  test("allowedHd 설정 + hd 일치 → 통과", () => {
    const email = assertPayloadAndExtractEmail(
      makePayload({ hd: "rsupport.com" }),
      { ...CFG, allowedHd: "rsupport.com" },
      INPUT
    );
    expect(email).toBe("owner@example.com");
  });

  test("allowedHd 설정 + hd 불일치 → GoogleAuthError", () => {
    expect(() =>
      assertPayloadAndExtractEmail(
        makePayload({ hd: "other.com" }),
        { ...CFG, allowedHd: "rsupport.com" },
        INPUT
      )
    ).toThrow(GoogleAuthError);
  });

  test("allowedHd 설정 + hd 없음 → GoogleAuthError", () => {
    expect(() =>
      assertPayloadAndExtractEmail(
        makePayload({ hd: undefined }),
        { ...CFG, allowedHd: "rsupport.com" },
        INPUT
      )
    ).toThrow(GoogleAuthError);
  });
});

describe("googleAuth — verifyGoogleCodeAndGetEmail(mock 팩토리 주입)", () => {
  test("code 교환 성공 + 검증 통과 → email 반환", async () => {
    const factory = mockFactory({ idToken: "id-tok", payload: makePayload() });
    await expect(verifyGoogleCodeAndGetEmail(CFG, INPUT, factory)).resolves.toBe(
      "owner@example.com"
    );
  });

  test("code 교환 실패(getToken throws) → GoogleAuthError", async () => {
    const factory = mockFactory({ getTokenThrows: new Error("invalid_grant") });
    await expect(verifyGoogleCodeAndGetEmail(CFG, INPUT, factory)).rejects.toThrow(
      GoogleAuthError
    );
  });

  test("code 교환 응답에 id_token 없음 → GoogleAuthError", async () => {
    const factory = mockFactory({ idToken: null, payload: makePayload() });
    await expect(verifyGoogleCodeAndGetEmail(CFG, INPUT, factory)).rejects.toThrow(
      GoogleAuthError
    );
  });

  test("id_token 검증 실패(verifyIdToken throws, 서명 위조 등) → GoogleAuthError", async () => {
    const factory = mockFactory({ verifyThrows: new Error("Invalid token signature") });
    await expect(verifyGoogleCodeAndGetEmail(CFG, INPUT, factory)).rejects.toThrow(
      GoogleAuthError
    );
  });

  test("검증은 통과했으나 payload가 미검증 email → GoogleAuthError", async () => {
    const factory = mockFactory({ payload: makePayload({ email_verified: false }) });
    await expect(verifyGoogleCodeAndGetEmail(CFG, INPUT, factory)).rejects.toThrow(
      GoogleAuthError
    );
  });

  test("nonce 불일치 → GoogleAuthError(교환·검증은 성공해도 payload 재확인에서 거부)", async () => {
    const factory = mockFactory({ payload: makePayload({ nonce: "server" }) });
    await expect(
      verifyGoogleCodeAndGetEmail(CFG, { ...INPUT, nonce: "client" }, factory)
    ).rejects.toThrow(GoogleAuthError);
  });
});
