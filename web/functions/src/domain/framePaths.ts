/**
 * 프레임 이미지 Storage 경로 규칙 — **단일 출처**.
 *
 * 서버(`services/frames.ts`)와 이관 스크립트(`scripts/migrate-frame-storage-paths.mjs`)가
 * **반드시 이 함수를 함께 써야 한다.** 규칙이 양쪽에 복제되면 스크립트가 멀쩡한 파일을 옮기거나
 * 고아로 오판한다 — 되돌리기 어려운 데이터 사고다.
 *
 * 설계: `docs/design/wpf-frame-ownership-binding-design.md` D-14.
 */

/** 공용 기본 프레임의 Storage 폴더명. */
export const DEFAULT_OWNER_FOLDER = "default";

/**
 * 프레임 이미지 경로.
 * 개인 `frames/users/{userId}/{frameId}.png` · 공용 `frames/default/{frameId}.png`.
 *
 * ⚠️ 개인을 `users/` 아래로 분리하는 이유: 계정 id는 형식 검증만 거치므로 `default@…` 로 가입하면
 * id가 `default`가 된다. `frames/{userId}/` 규칙이었다면 그 계정의 개인 프레임이 **공용 경로에 섞이고**,
 * 계정 삭제 cascade(`userFramesPrefix`)가 **공용 프레임을 지운다**.
 */
export function framePath(userId: string | null, frameId: string): string {
  return userId
    ? `frames/users/${userId}/${frameId}.png`
    : `frames/${DEFAULT_OWNER_FOLDER}/${frameId}.png`;
}

/**
 * 계정 소유 프레임 전체의 Storage 접두(계정 삭제 cascade용).
 * `framePath`의 개인 분기와 **같은 접두**여야 한다 — 어긋나면 탈퇴자 이미지가 영구 잔존한다.
 */
export function userFramesPrefix(userId: string): string {
  return `frames/users/${userId}/`;
}

/**
 * 구 규칙 경로(2026-08-07 이전). 이관 스크립트가 "옮길 원본"을 찾는 데만 쓴다.
 * 공용은 구·신 규칙이 같으므로 이관 대상이 아니다.
 */
export function legacyFramePath(userId: string | null, frameId: string): string {
  return userId
    ? `frames/${userId}/${frameId}.png`
    : `frames/${DEFAULT_OWNER_FOLDER}/${frameId}.png`;
}
