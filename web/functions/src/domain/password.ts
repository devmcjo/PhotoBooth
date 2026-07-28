/**
 * bcrypt 해시·검증.
 *
 * it15: 비밀번호 개념이 폐지되어 이 모듈의 유일한 소비자는 **설정 진입 PIN**(pinHash)이다.
 * 레거시 평문 계정 지연 마이그레이션(verifyPassword/looksHashed)은 제거했다 — 저장값은 항상 bcrypt 해시다.
 * 파일명은 유지한다(리네임 시 diff 폭증, 설계 §3.4 S8).
 * bcryptjs(순수 JS 구현)를 사용해 네이티브 빌드 의존을 피한다.
 */
import bcrypt from "bcryptjs";

/** bcrypt cost factor. 10 = 기본 권장(검증 지연·보안 균형). */
const SALT_ROUNDS = 10;

/** 평문(PIN)을 bcrypt 해시로 변환. */
export async function hashPassword(plain: string): Promise<string> {
  return bcrypt.hash(plain, SALT_ROUNDS);
}

/** 평문과 bcrypt 해시 비교. */
export async function verifyHash(plain: string, hash: string): Promise<boolean> {
  return bcrypt.compare(plain, hash);
}
