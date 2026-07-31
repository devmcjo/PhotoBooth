/**
 * 타임랩스 배속 역산 — Windows `Capture/FfmpegArgs.cs`(`ComputeSpeedFactor`) 이식 (analysis/14 §7.2)
 *
 * 웹은 ffmpeg를 쓰지 않지만 **"목표 10~15초"라는 판정 규격은 동일**하다(WD2).
 * 인코더는 이 배속으로 OPFS 스풀 프레임을 균등 선별한다(04 §7.2).
 */

export const TARGET_MIN_SECONDS = 10.0;
export const TARGET_MAX_SECONDS = 15.0;

/** 목표 중앙값(12.5초). */
const TARGET_MID = (TARGET_MIN_SECONDS + TARGET_MAX_SECONDS) / 2;

/**
 * 세션 길이(초)에서 목표 10~15초가 되도록 배속 N을 역산.
 * - 세션이 목표 상한 이하면 N=1(원속 그대로 — [바로 촬영] 다용으로 세션이 짧아진 경우).
 * - 그보다 길면 N = sessionSeconds / 12.5, 최소 1.
 */
export function computeSpeedFactor(sessionSeconds: number): number {
  if (sessionSeconds <= TARGET_MAX_SECONDS) return 1.0;
  return Math.max(1.0, sessionSeconds / TARGET_MID);
}

/** 배속 적용 후 예상 결과 길이(초). 검증용. */
export function expectedOutputSeconds(sessionSeconds: number, speedFactor: number): number {
  return speedFactor <= 0 ? sessionSeconds : sessionSeconds / speedFactor;
}
