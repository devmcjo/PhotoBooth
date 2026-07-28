import { hashPassword, verifyHash } from "../domain/password";

/**
 * it15: 비밀번호 개념 폐지 후 이 모듈의 유일한 소비자는 **설정 진입 PIN**(pinHash)이다.
 * 레거시 평문 지연 마이그레이션(verifyPassword/looksHashed) 케이스는 대상 함수와 함께 제거했다.
 */
describe("password — bcrypt 해시·검증(PIN 저장 인프라)", () => {
  test("hash → verifyHash 라운드트립", async () => {
    const hash = await hashPassword("s3cret!");
    expect(hash).toMatch(/^\$2[aby]\$\d{2}\$/); // bcrypt 형태
    expect(await verifyHash("s3cret!", hash)).toBe(true);
    expect(await verifyHash("wrong", hash)).toBe(false);
  });

  test("같은 평문도 salt가 달라 해시가 매번 다르다(둘 다 검증 통과)", async () => {
    const a = await hashPassword("0134");
    const b = await hashPassword("0134");
    expect(a).not.toBe(b);
    expect(await verifyHash("0134", a)).toBe(true);
    expect(await verifyHash("0134", b)).toBe(true);
  });

  test("4자리 PIN 해시·검증(it15 유일 게이트 자격증명)", async () => {
    const hash = await hashPassword("0000");
    expect(await verifyHash("0000", hash)).toBe(true);
    expect(await verifyHash("0001", hash)).toBe(false);
  });
});
