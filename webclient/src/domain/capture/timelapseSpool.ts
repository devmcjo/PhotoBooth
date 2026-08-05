/**
 * 촬영 중 스풀 수집 정책 — 04 §7.2
 *
 * 상태를 들고 있지 않는다(수집 간격 판정은 가공 Worker가, 파일 수 관리는 메인이 한다).
 * 두 곳이 같은 규칙을 쓰도록 **규칙만** 여기에 둔다.
 */

/** 수집 상한 15fps → 기본 간격 66.67ms(04 §7.2). */
export const TIMELAPSE_SPOOL_INTERVAL_MS = 1000 / 15;
/** 스풀 상한. 도달하면 절반 솎아내고 간격을 2배로 한다(900장 × ~50KB ≈ 45MB). */
export const TIMELAPSE_SPOOL_MAX_FRAMES = 900;
/** 솎아내기 배수. */
export const TIMELAPSE_SPOOL_DECIMATION_FACTOR = 2;

/**
 * 이번 가공 프레임을 스풀할 것인가.
 *
 * @param lastCapturedAtMs 직전 스풀 시각. **아직 한 번도 없으면 `-Infinity`**를 넘긴다.
 *        0을 초기값으로 쓰면 시계 원점 근처에서 첫 프레임을 먹는다(15 §4 함정 #4와 동종).
 */
export function shouldSpoolFrame(
  lastCapturedAtMs: number,
  nowMs: number,
  intervalMs: number,
): boolean {
  // 정확히 간격에 도달한 프레임은 수집한다(`>=`). 부동소수 오차로 한 프레임씩 밀리는 것보다
  // 경계를 포함하는 쪽이 목표 15fps에 가깝다.
  return nowMs - lastCapturedAtMs >= intervalMs;
}

export interface SpoolDecimationPlan {
  /** 삭제할 파일명(입력 배열의 홀수 인덱스). */
  readonly remove: readonly string[];
  /** 삭제 후 남는 수. */
  readonly keptCount: number;
}

/**
 * 상한 도달 시 **홀수 인덱스 항목을 버려** 시간 간격을 2배로 벌린다.
 *
 * 남는 파일명은 **그대로 유지**한다(재번호 없음). 0 패딩 파일명이라 문자열 정렬 = 시간 정렬이
 * 계속 성립하고, 이후 프레임은 증가하는 index로 계속 붙기 때문이다. 재번호를 매기면
 * 삭제·이름 변경이 900회 발생해 OPFS가 촬영 중에 막힌다.
 *
 * 상한 미만이면 `null`.
 */
export function planDecimation(
  sortedNames: readonly string[],
  maxFrames: number = TIMELAPSE_SPOOL_MAX_FRAMES,
): SpoolDecimationPlan | null {
  if (sortedNames.length < maxFrames) return null;

  const remove: string[] = [];
  for (let i = 1; i < sortedNames.length; i += 2) {
    remove.push(sortedNames[i]!);
  }
  return { remove, keptCount: sortedNames.length - remove.length };
}

/** 솎아낸 뒤의 수집 간격. */
export function decimatedInterval(intervalMs: number): number {
  return intervalMs * TIMELAPSE_SPOOL_DECIMATION_FACTOR;
}
