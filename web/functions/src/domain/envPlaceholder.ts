/**
 * 배포 전 `.env` 플레이스홀더 검사 — 순수 판정(2026-08-01 사고 재발 방지).
 *
 * 사고 메커니즘: 인수인계 문서(`docs/web-client/14 §3`)의 예시 명령을 값 치환 없이 실행해
 * `.env.mcphoto-955fb`에 `GOOGLE_OAUTH_CLIENT_ID_WEB=<A1의 웹 client_id>` 가 그대로 저장됐다.
 * **배포는 성공했고** 웹 로그인만 조용히 `invalid_client`로 100% 실패했다. 아무도 눈치채지 못했다.
 *
 * 파일 I/O·프로세스 종료는 `scripts/check-env-placeholders.mjs`(얇은 껍데기)가 맡고,
 * "무엇이 잘못됐는가" 판정만 여기에 둔다(운영 스크립트 테스트 패턴 — `domain/migration.ts` 선례).
 *
 * ⚠️ **값을 절대 반환하지 않는다.** 반환값은 키 이름뿐이다 — 시크릿이 로그·CI 출력으로 새면 안 된다.
 */

/**
 * 있으면 반드시 비어 있지 않아야 하는 키.
 *
 * "존재하지 않음"은 정상이다 — `.env`(로컬 비밀-아닌 값)와 `.env.<project>`(배포 전용)가
 * 서로 다른 키 부분집합을 갖고, web OAuth를 구성하지 않은 배포도 합법이기 때문이다(config.ts).
 * 여기서 잡는 것은 **"키는 썼는데 값을 비워 둔"** 상태다 — 치환 실수의 다른 얼굴이다.
 */
export const REQUIRED_NON_EMPTY_KEYS: readonly string[] = [
  "JWT_SECRET",
  "CLIENT_API_KEYS",
  "STORAGE_BUCKET",
  "HOSTING_BASE_URL",
  "GOOGLE_OAUTH_CLIENT_ID",
  "GOOGLE_OAUTH_CLIENT_ID_WEB",
  "OAUTH_REDIRECT_ALLOWLIST",
];

/** 값 앞뒤를 감싼 따옴표 한 겹을 벗긴다(dotenv가 하는 것과 같은 최소 처리). */
function unquote(raw: string): string {
  const v = raw.trim();
  if (v.length >= 2) {
    const first = v[0];
    const last = v[v.length - 1];
    if ((first === '"' && last === '"') || (first === "'" && last === "'")) {
      return v.slice(1, -1);
    }
  }
  return v;
}

/**
 * dotenv 텍스트에서 **치환되지 않은 플레이스홀더**와 **빈 필수값**의 키 이름을 모은다.
 *
 * 판정 규칙:
 *  1. `#`로 시작하는 줄(주석)과 빈 줄은 무시한다.
 *  2. `KEY=VALUE`의 VALUE에 `<` 또는 `>`가 있으면 치환되지 않은 것으로 본다.
 *     — 정상적인 client_id·버킷명·URL 목록에는 꺾쇠가 들어가지 않는다.
 *  3. `REQUIRED_NON_EMPTY_KEYS`에 있는 키의 값이 비어 있으면 함께 모은다.
 *
 * @returns 문제가 있는 **키 이름** 배열(등장 순서, 중복 제거). 값은 담지 않는다.
 */
export function findPlaceholderKeys(text: string): string[] {
  const bad: string[] = [];
  for (const rawLine of text.split(/\r?\n/)) {
    const line = rawLine.trim();
    if (line.length === 0 || line.startsWith("#")) continue;

    const eq = line.indexOf("=");
    if (eq <= 0) continue; // `KEY=VALUE` 형태가 아니면 dotenv도 무시한다.

    const key = line.slice(0, eq).trim();
    if (key.length === 0) continue;
    const value = unquote(line.slice(eq + 1));

    const isPlaceholder = value.includes("<") || value.includes(">");
    const isEmptyRequired = value.length === 0 && REQUIRED_NON_EMPTY_KEYS.includes(key);
    if ((isPlaceholder || isEmptyRequired) && !bad.includes(key)) {
      bad.push(key);
    }
  }
  return bad;
}
