import {
  extractBearer,
  issueToken,
  TokenError,
  verifyToken,
} from "../domain/jwt";

const SECRET = "test-secret-value-please-change";

describe("jwt — 발급/검증(HS256) 및 헤더 추출", () => {
  test("issue → verify 라운드트립(sub/role 보존)", () => {
    const token = issueToken({ id: "devmcjo", role: "admin" }, SECRET, 3600);
    const principal = verifyToken(token, SECRET);
    expect(principal.id).toBe("devmcjo");
    expect(principal.role).toBe("admin");
  });

  test("잘못된 시크릿으로 검증 실패", () => {
    const token = issueToken({ id: "u1", role: "user" }, SECRET, 3600);
    expect(() => verifyToken(token, "other-secret")).toThrow(TokenError);
  });

  test("만료 토큰 거부", () => {
    const token = issueToken({ id: "u1", role: "user" }, SECRET, -1);
    expect(() => verifyToken(token, SECRET)).toThrow(TokenError);
  });

  test("변조 토큰 거부", () => {
    const token = issueToken({ id: "u1", role: "user" }, SECRET, 3600);
    const tampered = token.slice(0, -2) + (token.endsWith("a") ? "bb" : "aa");
    expect(() => verifyToken(tampered, SECRET)).toThrow(TokenError);
  });

  test("빈 시크릿은 구성 오류(발급/검증 모두)", () => {
    expect(() => issueToken({ id: "u1", role: "user" }, "", 3600)).toThrow();
    expect(() => verifyToken("x.y.z", "")).toThrow();
  });

  test("extractBearer: 대소문자 무관·형식 불일치 null", () => {
    expect(extractBearer("Bearer abc.def.ghi")).toBe("abc.def.ghi");
    expect(extractBearer("bearer  abc.def.ghi ")).toBe("abc.def.ghi");
    expect(extractBearer("Basic abc")).toBeNull();
    expect(extractBearer("")).toBeNull();
    expect(extractBearer(undefined)).toBeNull();
  });
});
