import type { FrameTemplate } from "./types";

/**
 * 프레임 출처 판정 — Windows `Frames/FrameOrigin.cs` 이식 (analysis/13 §6.1)
 *
 * `id` 접두 규약을 그대로 유지한다(Windows와 프레임을 상호 이동하기 위함 — WD4):
 *   `local:` = 본인/로컬 저장분 · `bundle:` = 번들 자산 · `fallback` = 코드 생성 ·
 *   접두 없는 실 id = DB 공용 기본 프레임
 */

export const FRAME_ORIGIN_KINDS = ["UserLocal", "DbDefault", "Bundle", "Fallback"] as const;
export type FrameOriginKind = (typeof FRAME_ORIGIN_KINDS)[number];

const LOCAL_PREFIX = "local:";
const BUNDLE_PREFIX = "bundle:";
const FALLBACK_PREFIX = "fallback";

/** 우선순위: bundle → fallback/빈 id → local → DbDefault. */
export function classifyFrameOrigin(frame: FrameTemplate): FrameOriginKind {
  const id = frame.id ?? "";
  if (id.startsWith(BUNDLE_PREFIX)) return "Bundle";
  if (id.length === 0 || id.startsWith(FALLBACK_PREFIX)) return "Fallback";
  if (id.startsWith(LOCAL_PREFIX)) return "UserLocal";
  return "DbDefault";
}

/** 지정 계정이 소유한 로컬 프레임인가(`local:` 접두 && userId 일치). */
export function isOwnedLocal(frame: FrameTemplate, userId: string | null | undefined): boolean {
  return (
    classifyFrameOrigin(frame) === "UserLocal" &&
    typeof userId === "string" &&
    userId.length > 0 &&
    frame.userId === userId
  );
}

/** DB 공용 기본 프레임인가(접두 없는 실 id && isDefault). */
export function isDbDefault(frame: FrameTemplate): boolean {
  return classifyFrameOrigin(frame) === "DbDefault" && frame.isDefault;
}
