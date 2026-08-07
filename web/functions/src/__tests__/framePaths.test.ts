import {
  DEFAULT_OWNER_FOLDER,
  framePath,
  legacyFramePath,
  userFramesPrefix,
} from "../domain/framePaths";

/**
 * 프레임 Storage 경로 규칙(설계 D-14 · T19·T20).
 *
 * 이 규칙은 **서버와 이관 스크립트가 함께 쓴다**(`scripts/migrate-frame-storage-paths.mjs`).
 * 어긋나면 스크립트가 멀쩡한 파일을 옮기거나 고아로 오판하므로 여기서 고정한다.
 */
describe("프레임 Storage 경로 규칙", () => {
  test("개인 프레임은 frames/users/{userId}/ 아래", () => {
    expect(framePath("alice", "f1")).toBe("frames/users/alice/f1.png");
  });

  test("공용 프레임은 frames/default/ 아래", () => {
    expect(framePath(null, "f1")).toBe("frames/default/f1.png");
    expect(DEFAULT_OWNER_FOLDER).toBe("default");
  });

  /**
   * ⚠️ 이 테스트가 이 파일의 존재 이유다.
   * 계정 id는 형식 검증만 거치므로 `default@…` 로 가입하면 id가 "default"가 된다.
   * 구 규칙(`frames/{userId}/`)이었다면 그 사람의 개인 프레임이 **공용 경로와 같은 곳**에 저장되어
   * 공용 목록이 오염되고, 계정 삭제 cascade가 공용 프레임까지 지웠다.
   */
  test('계정 id가 "default"여도 공용 경로와 섞이지 않는다', () => {
    const personal = framePath("default", "f1");
    const shared = framePath(null, "f1");

    expect(personal).toBe("frames/users/default/f1.png");
    expect(personal).not.toBe(shared);

    // 구 규칙에서는 실제로 충돌했다(회귀 방지를 위해 그 사실도 고정한다).
    expect(legacyFramePath("default", "f1")).toBe(legacyFramePath(null, "f1"));
  });

  /**
   * cascade 삭제 접두가 저장 경로와 어긋나면 탈퇴자 이미지가 Storage에 영구히 남는다.
   * 문자열을 따로 쓰지 말고 반드시 두 함수의 정합을 유지할 것.
   */
  test("cascade 접두는 개인 저장 경로의 접두와 정확히 일치한다", () => {
    const userId = "alice";
    const prefix = userFramesPrefix(userId);

    expect(prefix).toBe("frames/users/alice/");
    expect(framePath(userId, "f1").startsWith(prefix)).toBe(true);
    expect(framePath(userId, "f2").startsWith(prefix)).toBe(true);
  });

  test("cascade 접두는 공용 프레임을 포함하지 않는다", () => {
    // "default"라는 계정이 존재해도 공용 프레임은 다른 트리에 있다.
    expect(framePath(null, "f1").startsWith(userFramesPrefix("default"))).toBe(false);
  });

  test("구 경로는 개인만 다르고 공용은 동일하다(이관 대상 판정 근거)", () => {
    expect(legacyFramePath("alice", "f1")).toBe("frames/alice/f1.png");
    expect(legacyFramePath(null, "f1")).toBe(framePath(null, "f1")); // 공용은 이관 불필요
  });

  test("서로 다른 계정의 같은 프레임 id는 다른 경로가 된다", () => {
    expect(framePath("alice", "same")).not.toBe(framePath("bob", "same"));
  });
});
