import {
  hashPassword,
  looksHashed,
  verifyHash,
  verifyPassword,
} from "../domain/password";

describe("password — bcrypt 해시·검증·지연 마이그레이션", () => {
  test("looksHashed: bcrypt prefix 판정", () => {
    expect(looksHashed("$2a$10$abcdefghijklmnopqrstuv")).toBe(true);
    expect(looksHashed("$2b$10$abcdefghijklmnopqrstuv")).toBe(true);
    expect(looksHashed("1111")).toBe(false);
    expect(looksHashed("")).toBe(false);
    expect(looksHashed(null)).toBe(false);
  });

  test("hash → verifyHash 라운드트립", async () => {
    const hash = await hashPassword("s3cret!");
    expect(looksHashed(hash)).toBe(true);
    expect(await verifyHash("s3cret!", hash)).toBe(true);
    expect(await verifyHash("wrong", hash)).toBe(false);
  });

  test("verifyPassword: 해시 저장값이면 마이그레이션 불필요", async () => {
    const hash = await hashPassword("pw1");
    const okRes = await verifyPassword("pw1", hash);
    expect(okRes.matched).toBe(true);
    expect(okRes.needsMigration).toBe(false);

    const noRes = await verifyPassword("pw2", hash);
    expect(noRes.matched).toBe(false);
    expect(noRes.needsMigration).toBe(false);
  });

  test("verifyPassword: 레거시 평문 매칭 시 needsMigration=true", async () => {
    const matched = await verifyPassword("1111", "1111");
    expect(matched.matched).toBe(true);
    expect(matched.needsMigration).toBe(true);

    const mismatched = await verifyPassword("2222", "1111");
    expect(mismatched.matched).toBe(false);
    expect(mismatched.needsMigration).toBe(false);
  });
});
