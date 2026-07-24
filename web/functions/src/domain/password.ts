/**
 * 비밀번호 해싱·검증(bcrypt) + 기존 평문 계정 지연 마이그레이션 판정.
 *
 * 설계 §7.1: 평문 저장 제거. 로그인 시 해시 검증하되, 기존 평문 계정은
 * 로그인 성공(평문 매칭) 시 즉시 해시로 교체 저장(lazy migration).
 * bcryptjs(순수 JS 구현)를 사용해 네이티브 빌드 의존을 피한다.
 */
import bcrypt from "bcryptjs";

/** bcrypt cost factor. 10 = 기본 권장(로그인 지연·보안 균형). */
const SALT_ROUNDS = 10;

/** bcrypt 해시 문자열의 형태(`$2a$`/`$2b$`/`$2y$` prefix)인지 판정. */
export function looksHashed(value: string | null | undefined): boolean {
  return typeof value === "string" && /^\$2[aby]\$\d{2}\$/.test(value);
}

/** 평문 비밀번호를 bcrypt 해시로 변환. */
export async function hashPassword(plain: string): Promise<string> {
  return bcrypt.hash(plain, SALT_ROUNDS);
}

/** 평문과 bcrypt 해시 비교. */
export async function verifyHash(plain: string, hash: string): Promise<boolean> {
  return bcrypt.compare(plain, hash);
}

/**
 * 저장된 자격증명(해시 또는 레거시 평문)에 대해 입력 비밀번호를 검증한다.
 *
 * @returns matched: 비밀번호 일치 여부. needsMigration: 레거시 평문에 매칭돼 해시 교체가 필요한지.
 *          - 저장값이 해시면 bcrypt 비교(마이그레이션 불필요).
 *          - 저장값이 평문(레거시)이면 단순 비교, 매칭 시 needsMigration=true(호출측이 재해싱 저장).
 */
export async function verifyPassword(
  plain: string,
  stored: string
): Promise<{ matched: boolean; needsMigration: boolean }> {
  if (looksHashed(stored)) {
    return { matched: await verifyHash(plain, stored), needsMigration: false };
  }
  // 레거시 평문 경로: 상수시간 비교는 아니나 마이그레이션 이후 소멸하는 경로.
  const matched = stored === plain;
  return { matched, needsMigration: matched };
}
