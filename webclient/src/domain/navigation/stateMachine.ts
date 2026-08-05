import type { AppState } from "./appState";

/**
 * 화면 상태 전이 규칙 — Windows `Navigation/SessionStateMachine.cs` 이식 (analysis/13 §2)
 *
 * 정상 흐름: Home→Login→FrameSelect→Guide→Capture→CutSelect→Result→Qr→Done→Home.
 * Home·Settings·Login·Account로의 전이는 어디서든 허용(오버레이성 진입·취소·유휴·예외·완료).
 */

/** 각 상태에서 사용자 액션으로 진행 가능한 다음 상태들. */
const FORWARD: Readonly<Record<AppState, readonly AppState[]>> = {
  Home: ["FrameSelect", "Login", "Settings"],
  Login: ["FrameSelect", "FrameEditor", "Settings"],
  FrameSelect: ["Guide", "FrameEditor"],
  Guide: ["Capture"],
  Capture: ["CutSelect"],
  CutSelect: ["Result", "Guide"], // Guide = 전체 재촬영
  Result: ["Qr", "Done"],
  Qr: ["Done"],
  Done: ["Home"],
  Settings: ["Login", "FrameEditor"],
  UserMgmt: ["Account"], // 관리자 도구(Account) 복귀
  FrameEditor: ["FrameSelect", "Settings", "Login"],
  Account: ["UserMgmt"],
};

/** 어디서든 진입 가능한 오버레이성 목적지. */
const ALWAYS_ALLOWED_TARGETS: readonly AppState[] = ["Home", "Settings", "Login", "Account"];

/**
 * `from → to` 전이가 합법인가.
 * 오버레이성 목적지는 자기 자신으로의 전이도 허용한다(Home→Home 복귀 — Windows 동작 보존).
 */
export function canTransition(from: AppState, to: AppState): boolean {
  if (ALWAYS_ALLOWED_TARGETS.includes(to)) return true;
  if (from === to) return false; // 그 외 자기 자신 전이는 무의미
  return FORWARD[from].includes(to);
}

/**
 * 촬영 흐름 중 상태인가 — **유휴 감시 대상**.
 * Settings·Login은 비대상이고, `FrameEditor`는 로그인 필수 능동 작업이라 제외한다(it4 B5).
 */
export function isSessionActive(state: AppState): boolean {
  return (
    state === "FrameSelect" ||
    state === "Guide" ||
    state === "Capture" ||
    state === "CutSelect" ||
    state === "Result" ||
    state === "Qr"
  );
}

/**
 * 오버레이성 화면인가 — **복귀 지점 저장 제외** 판정 (it19).
 * 오버레이끼리 전환할 때 복귀 지점을 덮어쓰면 [닫기]가 자기 자신으로 복귀해 아무 일도 하지 않는다.
 * `UserMgmt`는 `Account`의 하위 페이지라 같은 묶음이다(복귀 지점이 되면 Account↔UserMgmt를 벗어날 수 없다).
 */
export function isOverlayScreen(state: AppState): boolean {
  return state === "Settings" || state === "Login" || state === "Account" || state === "UserMgmt";
}

/** 상단 바를 표시할 상태인가. 몰입 화면(촬영·QR)에서는 숨긴다(it2 §3.1). */
export function isTopBarVisible(state: AppState): boolean {
  return state !== "Capture" && state !== "Qr";
}
