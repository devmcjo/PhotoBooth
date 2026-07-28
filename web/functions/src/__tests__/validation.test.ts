import {
  extToFormat,
  validateAccountId,
  validateAuthCode,
  validateCodeVerifier,
  validateEmail,
  validateFrameName,
  validateImageSize,
  validateLoopbackRedirectUri,
  validateNonce,
  validatePassword,
  validatePin,
  validateRetentionHours,
  validateRole,
  validateSlots,
  validateUploadFile,
  validateVerificationCode,
} from "../domain/validation";

describe("validation — 서버 입력 검증(경계 방어)", () => {
  test("validateAccountId: 형식/길이", () => {
    expect(validateAccountId("devmcjo").ok).toBe(true);
    expect(validateAccountId("a.b_c-1").ok).toBe(true);
    expect(validateAccountId("ab").ok).toBe(false); // 3자 미만
    expect(validateAccountId("has space").ok).toBe(false);
    expect(validateAccountId("한글계정").ok).toBe(false);
    expect(validateAccountId(123).ok).toBe(false);
    expect(validateAccountId("x".repeat(41)).ok).toBe(false);
  });

  test("validatePassword: 비어있음/과길이", () => {
    expect(validatePassword("1111").ok).toBe(true);
    expect(validatePassword("").ok).toBe(false);
    expect(validatePassword(null).ok).toBe(false);
    expect(validatePassword("x".repeat(201)).ok).toBe(false);
  });

  test("validateRole: 화이트리스트", () => {
    expect(validateRole("admin").ok).toBe(true);
    expect(validateRole("root").ok).toBe(false);
    expect(validateRole(undefined).ok).toBe(false);
  });

  test("validateRetentionHours: 정수 1~72", () => {
    expect(validateRetentionHours(24).ok).toBe(true);
    expect(validateRetentionHours(1).ok).toBe(true);
    expect(validateRetentionHours(72).ok).toBe(true);
    expect(validateRetentionHours(0).ok).toBe(false);
    expect(validateRetentionHours(73).ok).toBe(false);
    expect(validateRetentionHours(1.5).ok).toBe(false);
    expect(validateRetentionHours("24").ok).toBe(false);
  });

  test("validateSlots: 1~6개·양수 width/height", () => {
    const okSlots = [{ index: 0, x: 1, y: 2, width: 10, height: 20 }];
    const res = validateSlots(okSlots);
    expect(res.ok).toBe(true);
    if (res.ok) expect(res.value).toHaveLength(1);

    expect(validateSlots([]).ok).toBe(false); // 0개
    expect(validateSlots(new Array(7).fill({ index: 0, x: 0, y: 0, width: 1, height: 1 })).ok).toBe(false); // 7개
    expect(validateSlots([{ index: 0, x: 0, y: 0, width: 0, height: 1 }]).ok).toBe(false); // width 0
    expect(validateSlots([{ index: 0, x: -1, y: 0, width: 1, height: 1 }]).ok).toBe(false); // 음수
    expect(validateSlots("nope").ok).toBe(false);
  });

  test("validateImageSize: width/height>0", () => {
    expect(validateImageSize({ width: 100, height: 200 }).ok).toBe(true);
    expect(validateImageSize({ width: 0, height: 200 }).ok).toBe(false);
    expect(validateImageSize({ width: 100 }).ok).toBe(false);
    expect(validateImageSize(null).ok).toBe(false);
  });

  test("validateFrameName: 1~100자·'_' 금지", () => {
    expect(validateFrameName("여름프레임").ok).toBe(true);
    expect(validateFrameName("has_underscore").ok).toBe(false);
    expect(validateFrameName("").ok).toBe(false);
    expect(validateFrameName("x".repeat(101)).ok).toBe(false);
  });

  test("validateUploadFile: kind별 ext/contentType 정합", () => {
    expect(validateUploadFile({ kind: "final", ext: "jpg", contentType: "image/jpeg" }).ok).toBe(true);
    expect(validateUploadFile({ kind: "final", ext: "png", contentType: "image/png" }).ok).toBe(true);
    expect(validateUploadFile({ kind: "timelapse", ext: "mp4", contentType: "video/mp4" }).ok).toBe(true);

    // ext/kind 불일치
    expect(validateUploadFile({ kind: "final", ext: "mp4", contentType: "video/mp4" }).ok).toBe(false);
    // contentType 불일치
    expect(validateUploadFile({ kind: "final", ext: "jpg", contentType: "video/mp4" }).ok).toBe(false);
    // 알 수 없는 kind
    expect(validateUploadFile({ kind: "raw", ext: "jpg", contentType: "image/jpeg" }).ok).toBe(false);
  });

  test("extToFormat: png만 png, 그 외 jpg", () => {
    expect(extToFormat("png")).toBe("png");
    expect(extToFormat("jpg")).toBe("jpg");
  });

  test("validateEmail: 형식·길이·소문자 정규화", () => {
    const r = validateEmail("User@Example.COM");
    expect(r.ok).toBe(true);
    if (r.ok) expect(r.value).toBe("user@example.com"); // 소문자 정규화

    expect(validateEmail("  a@b.co  ").ok).toBe(true); // 트림
    expect(validateEmail("plainaddress").ok).toBe(false); // @ 없음
    expect(validateEmail("no@domain").ok).toBe(false); // 도메인에 점 없음
    expect(validateEmail("a b@c.com").ok).toBe(false); // 공백
    expect(validateEmail("two@@c.com").ok).toBe(false); // @ 2개
    expect(validateEmail("").ok).toBe(false);
    expect(validateEmail(null).ok).toBe(false);
    expect(validateEmail(123).ok).toBe(false);
    expect(validateEmail(`${"x".repeat(250)}@example.com`).ok).toBe(false); // 254자 초과
  });

  test("validateVerificationCode: 정확히 6자리 숫자", () => {
    const r = validateVerificationCode("012345");
    expect(r.ok).toBe(true);
    if (r.ok) expect(r.value).toBe("012345");

    expect(validateVerificationCode("  654321 ").ok).toBe(true); // 트림
    expect(validateVerificationCode("12345").ok).toBe(false); // 5자리
    expect(validateVerificationCode("1234567").ok).toBe(false); // 7자리
    expect(validateVerificationCode("12a456").ok).toBe(false); // 비숫자
    expect(validateVerificationCode(123456).ok).toBe(false); // 숫자 타입
    expect(validateVerificationCode(null).ok).toBe(false);
  });

  test("validatePin: 정확히 4자리 숫자(it14 설정 진입 PIN)", () => {
    const r = validatePin("0134");
    expect(r.ok).toBe(true);
    if (r.ok) expect(r.value).toBe("0134");

    expect(validatePin("  4321 ").ok).toBe(true); // 트림
    expect(validatePin("123").ok).toBe(false); // 3자리
    expect(validatePin("12345").ok).toBe(false); // 5자리
    expect(validatePin("12a4").ok).toBe(false); // 비숫자
    expect(validatePin("").ok).toBe(false);
    expect(validatePin(1234).ok).toBe(false); // 숫자 타입(문자열 아님)
    expect(validatePin(null).ok).toBe(false);
  });

  // ── item1b: Google SSO 입력 검증 ──

  test("validateAuthCode: 비어있지 않은 문자열·과길이(≤2048)", () => {
    expect(validateAuthCode("4/0Adeu5...").ok).toBe(true);
    const r = validateAuthCode("  abc  ");
    expect(r.ok).toBe(true);
    if (r.ok) expect(r.value).toBe("abc"); // 트림
    expect(validateAuthCode("").ok).toBe(false);
    expect(validateAuthCode("   ").ok).toBe(false);
    expect(validateAuthCode(null).ok).toBe(false);
    expect(validateAuthCode(123).ok).toBe(false);
    expect(validateAuthCode("x".repeat(2049)).ok).toBe(false);
  });

  test("validateCodeVerifier: RFC 7636 43~128자 [A-Za-z0-9-._~]", () => {
    const good = "A".repeat(43);
    expect(validateCodeVerifier(good).ok).toBe(true);
    expect(validateCodeVerifier("A".repeat(128)).ok).toBe(true);
    expect(validateCodeVerifier("aZ0-._~" + "x".repeat(40)).ok).toBe(true); // 허용 문자
    expect(validateCodeVerifier("A".repeat(42)).ok).toBe(false); // 42자 미만
    expect(validateCodeVerifier("A".repeat(129)).ok).toBe(false); // 128자 초과
    expect(validateCodeVerifier("A".repeat(42) + "!").ok).toBe(false); // 금지 문자(!)
    expect(validateCodeVerifier("A".repeat(42) + "+").ok).toBe(false); // base64 표준(+/)은 unreserved 아님
    expect(validateCodeVerifier(null).ok).toBe(false);
  });

  test("validateLoopbackRedirectUri: 127.0.0.1/localhost loopback만 허용", () => {
    expect(validateLoopbackRedirectUri("http://127.0.0.1:52001/").ok).toBe(true);
    expect(validateLoopbackRedirectUri("http://localhost:8080/").ok).toBe(true);
    expect(validateLoopbackRedirectUri("http://127.0.0.1/").ok).toBe(true); // 포트 없음(기본 80)
    expect(validateLoopbackRedirectUri("http://127.0.0.1:52001").ok).toBe(true); // 경로 없음

    // 거부: https(loopback은 http), 외부 host, 경로/쿼리/프래그먼트, 인증정보, 잘못된 형식.
    expect(validateLoopbackRedirectUri("https://127.0.0.1:52001/").ok).toBe(false);
    expect(validateLoopbackRedirectUri("http://evil.com/").ok).toBe(false);
    expect(validateLoopbackRedirectUri("http://127.0.0.1:52001/callback").ok).toBe(false);
    expect(validateLoopbackRedirectUri("http://127.0.0.1:52001/?x=1").ok).toBe(false);
    expect(validateLoopbackRedirectUri("http://127.0.0.1:52001/#frag").ok).toBe(false);
    expect(validateLoopbackRedirectUri("http://user:pw@127.0.0.1:52001/").ok).toBe(false);
    expect(validateLoopbackRedirectUri("ftp://127.0.0.1/").ok).toBe(false);
    expect(validateLoopbackRedirectUri("not a url").ok).toBe(false);
    expect(validateLoopbackRedirectUri("").ok).toBe(false);
    expect(validateLoopbackRedirectUri(null).ok).toBe(false);
    expect(validateLoopbackRedirectUri("http://127.0.0.1:" + "9".repeat(260) + "/").ok).toBe(false); // 과길이
  });

  test("validateNonce: 1~256자 [A-Za-z0-9-._~]", () => {
    expect(validateNonce("abc-123_XY.z~").ok).toBe(true);
    const r = validateNonce("  nonce1  ");
    expect(r.ok).toBe(true);
    if (r.ok) expect(r.value).toBe("nonce1"); // 트림
    expect(validateNonce("").ok).toBe(false);
    expect(validateNonce("has space").ok).toBe(false);
    expect(validateNonce("bad!char").ok).toBe(false);
    expect(validateNonce("x".repeat(257)).ok).toBe(false);
    expect(validateNonce(null).ok).toBe(false);
    expect(validateNonce(123).ok).toBe(false);
  });
});
