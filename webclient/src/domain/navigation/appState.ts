/**
 * 키오스크 화면 상태 — Windows `MCPhoto.Core/Navigation/AppState.cs` 이식 (analysis/13 §2)
 *
 * 문자열 유니온을 쓰는 이유: 공유 테스트 벡터(`docs/spec-vectors/`)와 로그가 C# enum **이름**을
 * 그대로 쓴다. 숫자 서수를 쓰면 양쪽 벡터가 어긋난다.
 */
export const APP_STATES = [
  "Home",
  "Login",
  "FrameSelect",
  "Guide",
  "Capture",
  "CutSelect",
  "Result",
  "Qr",
  "Done",
  "Settings",
  "UserMgmt",
  "FrameEditor",
  "Account",
] as const;

export type AppState = (typeof APP_STATES)[number];

export function isAppState(value: string): value is AppState {
  return (APP_STATES as readonly string[]).includes(value);
}
