import { canWriteFrames, isPower, type UserRole } from "../roles/userRole";
import { classifyFrameOrigin, isOwnedLocal } from "./frameOrigin";
import type { FrameTemplate } from "./types";

/**
 * 프레임 편집·삭제 권한(역할 × 출처) — Windows `Frames/FrameEditPolicy.cs` 이식 (analysis/13 §6.1)
 *
 * `advanced_user` = 본인 로컬 생성분만 · power = 본인 로컬 + DB 공용 기본 ·
 * `user`·`temp_user` = **사용만**(읽기 전용) · 번들·fallback·게스트 = 불가
 */

/** `role`이 null이면 게스트(비로그인) → 항상 불가. */
export function canEditFrame(
  frame: FrameTemplate,
  role: UserRole | null,
  userId: string | null,
): boolean {
  if (role === null) return false;
  if (!canWriteFrames(role)) return false;

  switch (classifyFrameOrigin(frame)) {
    case "UserLocal":
      return isOwnedLocal(frame, userId); // 본인 것만
    case "DbDefault":
      return isPower(role); // power만
    default:
      return false; // 번들·fallback
  }
}

/**
 * 삭제(로컬 사본 제거) 가능 여부.
 *
 * ⚠️ **소유자(userId)를 보지 않는다.** power가 fork·저장한 *공용* 로컬 프레임은 `userId=null`로
 *    로드되므로 `isOwnedLocal`로 판정하면 현행 삭제 능력이 회귀한다(Windows 주석의 회귀 경고 그대로).
 *    타인 개인 프레임은 목록 로드 단계의 소유자 필터에서 애초에 걸러진다.
 */
export function canDeleteFrame(frame: FrameTemplate, role: UserRole | null): boolean {
  if (role === null || !canWriteFrames(role)) return false;

  switch (classifyFrameOrigin(frame)) {
    case "UserLocal":
      return true; // 로컬 저장분(개인 `local:` / power 공용 fork)
    case "DbDefault":
      return isPower(role); // 공용 DB 프레임은 power만
    default:
      return false;
  }
}

/**
 * 편집·저장 시 원본을 보존하고 새 이름으로 분기(fork)해야 하는가.
 * 카탈로그 유래(DbDefault·Bundle·Fallback) = true, UserLocal = false. 역할과 무관하다.
 */
export function requiresFork(frame: FrameTemplate): boolean {
  return classifyFrameOrigin(frame) !== "UserLocal";
}
