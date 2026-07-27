import {
  applyAccountIdSuffix,
  deriveAccountId,
  deriveBaseAccountId,
} from "../domain/accountId";

/** validation.ts ID_RE와 동일 규칙(3~40자 [A-Za-z0-9._-]). 파생값이 항상 만족해야 한다. */
const ID_RE = /^[A-Za-z0-9._-]{3,40}$/;

describe("accountId — deriveBaseAccountId(순수 base 도출)", () => {
  test("일반 local-part → 소문자, 규칙 만족", () => {
    const id = deriveBaseAccountId("John.Doe@example.com");
    expect(id).toBe("john.doe");
    expect(id).toMatch(ID_RE);
  });

  test("허용 외 문자 제거(+, 공백, 한글 등)", () => {
    // local-part "a+b c" → '+'·공백 제거 → "abc"
    expect(deriveBaseAccountId("a+b c@x.com")).toBe("abc");
    expect(deriveBaseAccountId("a+b c@x.com")).toMatch(ID_RE);
    // '+' 제거 후 2자 이하이면 패딩되어 3자 이상
    expect(deriveBaseAccountId("a+@x.com").length).toBeGreaterThanOrEqual(3);
  });

  test("3자 미만이면 패딩되어 3자 이상", () => {
    const id = deriveBaseAccountId("a@x.com");
    expect(id.length).toBeGreaterThanOrEqual(3);
    expect(id).toMatch(ID_RE);
    expect(id.startsWith("a")).toBe(true);
  });

  test("40자 초과면 40자로 절단", () => {
    const long = "a".repeat(60) + "@x.com";
    const id = deriveBaseAccountId(long);
    expect(id.length).toBe(40);
    expect(id).toMatch(ID_RE);
  });

  test("`.`, `_`, `-` 는 보존", () => {
    const id = deriveBaseAccountId("a.b_c-d@x.com");
    expect(id).toBe("a.b_c-d");
    expect(id).toMatch(ID_RE);
  });

  test("빈 local-part(제거 후 빈 문자열) → g- 폴백, 규칙 만족", () => {
    const id = deriveBaseAccountId("한글이름@x.com");
    expect(id.startsWith("g-")).toBe(true);
    expect(id).toMatch(ID_RE);
  });

  test("@ 없는 입력도 전체를 local-part로 취급", () => {
    const id = deriveBaseAccountId("plainuser");
    expect(id).toBe("plainuser");
    expect(id).toMatch(ID_RE);
  });

  test("폴백 id는 매 호출 유니크(uuid 기반)", () => {
    const a = deriveBaseAccountId("@x.com");
    const b = deriveBaseAccountId("@x.com");
    expect(a).not.toBe(b);
    expect(a).toMatch(ID_RE);
    expect(b).toMatch(ID_RE);
  });
});

describe("accountId — applyAccountIdSuffix(순수 suffix)", () => {
  test("짧은 base는 그대로 -n 부여", () => {
    expect(applyAccountIdSuffix("john", 2)).toBe("john-2");
    expect(applyAccountIdSuffix("john", 3)).toBe("john-3");
  });

  test("40자 초과 시 base 절단 후 suffix 유지, 규칙 만족", () => {
    const base = "a".repeat(40);
    const out = applyAccountIdSuffix(base, 2);
    expect(out.length).toBeLessThanOrEqual(40);
    expect(out.endsWith("-2")).toBe(true);
    expect(out).toMatch(ID_RE);
  });

  test("큰 n(-1234)도 40자 상한 지킴", () => {
    const base = "a".repeat(40);
    const out = applyAccountIdSuffix(base, 1234);
    expect(out.length).toBeLessThanOrEqual(40);
    expect(out.endsWith("-1234")).toBe(true);
    expect(out).toMatch(ID_RE);
  });
});

describe("accountId — deriveAccountId(충돌 회피, exists 콜백 주입)", () => {
  test("충돌 없으면 base 그대로", async () => {
    const id = await deriveAccountId("john@x.com", async () => false);
    expect(id).toBe("john");
  });

  test("base 충돌 → -2 부여", async () => {
    const taken = new Set(["john"]);
    const id = await deriveAccountId("john@x.com", async (c) => taken.has(c));
    expect(id).toBe("john-2");
  });

  test("연속 충돌 → -2,-3 건너뛰고 첫 빈 후보", async () => {
    const taken = new Set(["john", "john-2", "john-3"]);
    const id = await deriveAccountId("john@x.com", async (c) => taken.has(c));
    expect(id).toBe("john-4");
  });

  test("파생 id는 항상 규칙 만족", async () => {
    const id = await deriveAccountId("한글@x.com", async () => false);
    expect(id).toMatch(ID_RE);
  });
});
