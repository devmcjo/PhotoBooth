import {
  computeExpiresAtMs,
  generateCode,
  generateToken,
  hashToken,
  isExpired,
  parseToken,
  RESET_TTL_SECONDS,
  verifyTokenHash,
  VERIFY_TTL_SECONDS,
} from "../domain/tokens";

describe("tokens — 토큰 생성·해시·비교·만료(순수 로직)", () => {
  test("hashToken: 동일 입력은 동일 sha256 hex(64자), 다른 입력은 다른 해시", () => {
    const h1 = hashToken("hello");
    const h2 = hashToken("hello");
    const h3 = hashToken("world");
    expect(h1).toBe(h2);
    expect(h1).not.toBe(h3);
    expect(h1).toMatch(/^[0-9a-f]{64}$/);
  });

  test("generateCode: 항상 6자리 숫자 문자열(선행 0 포함)", () => {
    for (let i = 0; i < 200; i++) {
      const code = generateCode();
      expect(code).toMatch(/^\d{6}$/);
    }
  });

  test("generateToken: tokenId·secret·code·해시 구성, token=tokenId.secret", () => {
    const t = generateToken();
    expect(t.tokenId.length).toBeGreaterThan(0);
    expect(t.secret.length).toBeGreaterThan(0);
    expect(t.code).toMatch(/^\d{6}$/);
    expect(t.token).toBe(`${t.tokenId}.${t.secret}`);
    // 저장 해시는 평문과 달라야 하고, 실제 평문의 해시와 일치해야 한다.
    expect(t.secretHash).toBe(hashToken(t.secret));
    expect(t.codeHash).toBe(hashToken(t.code));
    expect(t.secretHash).not.toBe(t.secret);
  });

  test("generateToken: 두 번 생성 시 tokenId·secret이 서로 다름(추측 불가)", () => {
    const a = generateToken();
    const b = generateToken();
    expect(a.tokenId).not.toBe(b.tokenId);
    expect(a.secret).not.toBe(b.secret);
  });

  test("parseToken: 정상 결합 토큰 분해", () => {
    const t = generateToken();
    const parsed = parseToken(t.token);
    expect(parsed).not.toBeNull();
    expect(parsed?.tokenId).toBe(t.tokenId);
    expect(parsed?.secret).toBe(t.secret);
  });

  test("parseToken: secret에 '.'이 포함돼도 첫 '.' 기준 분리", () => {
    const parsed = parseToken("abc.de.fg");
    expect(parsed?.tokenId).toBe("abc");
    expect(parsed?.secret).toBe("de.fg");
  });

  test("parseToken: 형식 위반은 null", () => {
    expect(parseToken(undefined)).toBeNull();
    expect(parseToken(123)).toBeNull();
    expect(parseToken("")).toBeNull();
    expect(parseToken("nodot")).toBeNull();
    expect(parseToken(".secret")).toBeNull(); // tokenId 비어있음
    expect(parseToken("tokenid.")).toBeNull(); // secret 비어있음
  });

  test("verifyTokenHash: 올바른 평문만 통과(상수시간 비교)", () => {
    const t = generateToken();
    expect(verifyTokenHash(t.secret, t.secretHash)).toBe(true);
    expect(verifyTokenHash(t.code, t.codeHash)).toBe(true);
    expect(verifyTokenHash("wrong-secret", t.secretHash)).toBe(false);
    expect(verifyTokenHash("000000", t.codeHash)).toBe(t.code === "000000");
  });

  test("verifyTokenHash: 저장 해시 길이가 다르면 false(방어)", () => {
    expect(verifyTokenHash("x", "short")).toBe(false);
  });

  test("isExpired / computeExpiresAtMs: 만료 경계", () => {
    const now = 1_000_000;
    const exp = computeExpiresAtMs(now, 60); // +60초
    expect(exp).toBe(now + 60_000);
    expect(isExpired(exp, now)).toBe(false); // 아직 유효
    expect(isExpired(exp, exp)).toBe(true); // 경계(같은 시각)는 만료로 취급
    expect(isExpired(exp, exp + 1)).toBe(true);
  });

  test("TTL 상수: verify=5분(§3.3 규칙 C3), reset=1h(유지)", () => {
    expect(VERIFY_TTL_SECONDS).toBe(5 * 60);
    expect(RESET_TTL_SECONDS).toBe(60 * 60);
  });
});
