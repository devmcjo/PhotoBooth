/**
 * Google SSO 자동 계정 생성용 계정 id 파생(순수 로직) — 설계 §2.2 B-BE-1 / BE-2.
 *
 * email local-part에서 계정 id 후보를 만든다. `validateAccountId`(3~40자 `[A-Za-z0-9._-]`)를
 * **항상** 만족하도록 정규화하고, 충돌 시 `-2`/`-3`… suffix를 붙인다.
 * local-part가 전부 제거돼 빈 문자열이면 폴백 `g-{uuid8}`.
 *
 * 이 모듈은 **순수**(Firestore 부수효과 없음). 충돌 검사는 exists 콜백을 주입받아
 * Firestore 조회를 서비스 계층에 격리한다(테스트 가능성·§0 순수 로직 경계).
 * 외부 의존 0 — Node 표준 `crypto`만 사용.
 */
import { randomUUID } from "node:crypto";

/** 계정 id 규칙(validation.ts ID_RE와 동일): 3~40자, `[A-Za-z0-9._-]`. */
const ID_MIN = 3;
const ID_MAX = 40;
/** id에 허용되지 않는 문자(정규화 시 제거 대상). */
const DISALLOWED_RE = /[^A-Za-z0-9._-]/g;
/** 3자 미만일 때 뒤를 채우는 패딩 문자(허용 문자 중 하나). */
const PAD_CHAR = "0";

/**
 * email → 충돌 미고려 base 후보(순수). 항상 3~40자 `[A-Za-z0-9._-]`.
 * - local-part(`@` 앞)만 취해 소문자화하고 허용 외 문자 제거.
 * - 비면 폴백 `g-{uuid8}`(항상 규칙 만족: 소문자 hex 8자 + `g-` = 10자).
 * - 3자 미만이면 PAD_CHAR로 우측 패딩, 40자 초과면 앞 40자로 절단.
 */
export function deriveBaseAccountId(email: string): string {
  const at = email.indexOf("@");
  const localPart = at >= 0 ? email.slice(0, at) : email;
  const cleaned = localPart.toLowerCase().replace(DISALLOWED_RE, "");

  if (cleaned.length === 0) {
    // 폴백: g-{uuid 앞 8자}. uuid hex는 [0-9a-f]라 규칙 만족.
    return `g-${randomUUID().replace(/-/g, "").slice(0, 8)}`;
  }
  if (cleaned.length < ID_MIN) {
    return cleaned.padEnd(ID_MIN, PAD_CHAR);
  }
  if (cleaned.length > ID_MAX) {
    return cleaned.slice(0, ID_MAX);
  }
  return cleaned;
}

/**
 * base 후보에 suffix(`-{n}`, n>=2)를 붙이되 40자 상한을 지킨다(순수).
 * base+suffix가 40자를 넘으면 base를 잘라 suffix를 담을 공간을 확보한다.
 * 잘린 base가 비지 않도록 최소 1자는 남긴다(suffix 자체는 `-2`… 규칙 문자만 사용).
 */
export function applyAccountIdSuffix(base: string, n: number): string {
  const suffix = `-${n}`;
  if (base.length + suffix.length <= ID_MAX) {
    return `${base}${suffix}`;
  }
  const room = Math.max(1, ID_MAX - suffix.length);
  return `${base.slice(0, room)}${suffix}`;
}

/**
 * 충돌 회피를 포함한 계정 id 파생. `exists(candidate)`가 true면 다음 후보로.
 * base(충돌 없으면 그대로) → base-2 → base-3 … 순으로 시도.
 * 극단적 충돌(이론상 도달 불가)에 대비해 상한 이후엔 폴백 id로 대체한다.
 *
 * @param email 검증된 소문자 email(호출측이 정규화 후 전달).
 * @param exists 후보 id가 이미 존재하는지 확인하는 비동기 콜백(Firestore 조회).
 */
export async function deriveAccountId(
  email: string,
  exists: (candidate: string) => Promise<boolean>
): Promise<string> {
  const base = deriveBaseAccountId(email);
  if (!(await exists(base))) {
    return base;
  }
  // 충돌: -2, -3 … 순차 시도. 상한(9999)까지 못 찾으면 폴백 uuid id(사실상 도달 불가).
  for (let n = 2; n <= 9999; n++) {
    const candidate = applyAccountIdSuffix(base, n);
    if (!(await exists(candidate))) {
      return candidate;
    }
  }
  // 극단적 폴백: uuid 기반(충돌 확률 무시 가능). 여기서도 exists면 그대로 반환(호출측 create가 재확인).
  return `g-${randomUUID().replace(/-/g, "").slice(0, 8)}`;
}
