/**
 * 배포 전 플레이스홀더 가드 순수 판정 테스트 (Step F3 · 2026-08-01 사고 재발 방지).
 *
 * 이 검사가 없어서 `GOOGLE_OAUTH_CLIENT_ID_WEB=<A1의 웹 client_id>` 가 배포에 실렸고,
 * 배포는 성공한 채 웹 로그인만 100% `invalid_client`로 실패했다.
 * 스크립트(`scripts/check-env-placeholders.mjs`)는 파일 I/O만 하므로 판정만 여기서 못박는다.
 */
import { findPlaceholderKeys, REQUIRED_NON_EMPTY_KEYS } from "../domain/envPlaceholder";

describe("findPlaceholderKeys", () => {
  test("치환되지 않은 <플레이스홀더>를 키 이름으로 검출한다(값은 반환하지 않는다)", () => {
    const text = [
      "GOOGLE_OAUTH_CLIENT_ID=712395684881-real.apps.googleusercontent.com",
      "GOOGLE_OAUTH_CLIENT_ID_WEB=<A1의 웹 client_id>",
    ].join("\n");

    const keys = findPlaceholderKeys(text);
    expect(keys).toEqual(["GOOGLE_OAUTH_CLIENT_ID_WEB"]);
    // 시크릿 유출 방지: 반환값 어디에도 값 자체가 없어야 한다.
    expect(keys.join(",")).not.toContain("<");
  });

  test("정상 env는 빈 배열이다(현행 배포 형태)", () => {
    const text = [
      "# 주석",
      "",
      "GOOGLE_OAUTH_CLIENT_ID=desktop.apps.googleusercontent.com",
      "GOOGLE_OAUTH_CLIENT_ID_WEB=web.apps.googleusercontent.com",
      "OAUTH_REDIRECT_ALLOWLIST=https://a.web.app/oauth2callback,http://localhost:5173/oauth2callback",
      'STORAGE_BUCKET="mcphoto-955fb.firebasestorage.app"',
    ].join("\n");

    expect(findPlaceholderKeys(text)).toEqual([]);
  });

  test("주석 줄의 꺾쇠는 무시한다 — 안내문에 <...> 예시가 들어 있어도 배포를 막지 않는다", () => {
    const text = [
      "# 사용법: GOOGLE_OAUTH_CLIENT_ID_WEB=<A1의 웹 client_id> 를 실제 값으로 치환할 것",
      "   # 들여쓴 주석에도 <꺾쇠>가 있을 수 있다",
      "GOOGLE_OAUTH_CLIENT_ID_WEB=web.apps.googleusercontent.com",
    ].join("\n");

    expect(findPlaceholderKeys(text)).toEqual([]);
  });

  test("필수 키의 빈 값도 검출한다(치환 실수의 다른 얼굴). 목록 밖 키의 빈 값은 통과", () => {
    const text = [
      "GOOGLE_OAUTH_CLIENT_ID_WEB=",
      "OAUTH_REDIRECT_ALLOWLIST=   ",
      // 목록 밖 + 빈 값 = 정상(선택 설정을 명시적으로 비운 것일 수 있다).
      "GOOGLE_ALLOWED_HD=",
    ].join("\n");

    expect(findPlaceholderKeys(text)).toEqual([
      "GOOGLE_OAUTH_CLIENT_ID_WEB",
      "OAUTH_REDIRECT_ALLOWLIST",
    ]);
    expect(REQUIRED_NON_EMPTY_KEYS).not.toContain("GOOGLE_ALLOWED_HD");
  });
});
