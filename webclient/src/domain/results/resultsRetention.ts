import { isResultFolderName } from "./resultNaming";

/**
 * 보관본 용량 정책(순수) — 05 §5.4
 *
 * 브라우저 할당량 때문에 **웹에만 있는 정책**이다(Windows는 무기한 보관).
 * ⚠️ 비율이 아니라 **정수 바이트끼리** 비교한다(15 §4 함정 #3 — 부동소수 오차).
 */

export const RESULTS_MAX_BYTES = 2 * 1024 * 1024 * 1024; // 2GB
export const RESULTS_MAX_SESSIONS = 200;

export interface ResultFolderUsage {
  readonly name: string;
  readonly bytes: number;
}

export interface ResultsRetentionLimits {
  readonly maxBytes: number;
  readonly maxSessions: number;
}

export interface ResultsRetentionDecision {
  /** 삭제할 폴더명(오래된 순). 비어 있으면 정리 불필요. */
  readonly remove: readonly string[];
  readonly keptCount: number;
  readonly keptBytes: number;
  /** 정리를 유발한 사유(정리 **전** 상태). 로그·진단 표시용. */
  readonly triggers: readonly ("count" | "bytes")[];
  /** 정리 후에도 한도를 넘는가(단일 세션이 2GB를 넘는 극단 상황) — 정직하게 보고한다. */
  readonly stillOverLimit: boolean;
}

export const DEFAULT_RESULTS_RETENTION_LIMITS: ResultsRetentionLimits = {
  maxBytes: RESULTS_MAX_BYTES,
  maxSessions: RESULTS_MAX_SESSIONS,
};

/**
 * 이름 오름차순 비교. `mcphoto_YYMMDD_HHMM`은 0 패딩이라 **문자열 사전순 = 시간순**이다.
 * `localeCompare`를 쓰지 않는다 — 로케일·ICU에 따라 결과가 달라지면 삭제 순서가 흔들린다.
 *
 * 알려진 오차: 같은 분 안에서 `-10`이 `-2`보다 앞선다. 같은 분 안의 순서라 보존 정책에 실질 영향이 없다.
 */
function compareFolderName(a: ResultFolderUsage, b: ResultFolderUsage): number {
  return a.name < b.name ? -1 : a.name > b.name ? 1 : 0;
}

/**
 * 한도 초과분을 오래된 것부터 축출한다.
 *
 * - 삭제 후보는 **우리 규약 이름**뿐이다. 규약 밖 이름은 회계(`keptBytes`)에는 포함하되
 *   삭제하지 않는다 — 정직한 총량과 남의 데이터 보호를 동시에 만족시킨다.
 * - ⚠️ **가장 최신 후보는 절대 삭제하지 않는다.** 방금 기록한 결과물을 지우면 M6-W가 무의미해진다.
 *   단일 세션이 한도를 넘어도 그 세션은 남고 `stillOverLimit: true`로 보고한다.
 */
export function planResultsRetention(
  folders: readonly ResultFolderUsage[],
  limits: ResultsRetentionLimits = DEFAULT_RESULTS_RETENTION_LIMITS,
): ResultsRetentionDecision {
  let keptCount = folders.length;
  let keptBytes = 0;
  for (const folder of folders) keptBytes += folder.bytes;

  const triggers: ("count" | "bytes")[] = [];
  if (keptCount > limits.maxSessions) triggers.push("count");
  if (keptBytes > limits.maxBytes) triggers.push("bytes");

  const candidates = folders.filter((folder) => isResultFolderName(folder.name));
  candidates.sort(compareFolderName);

  const remove: string[] = [];
  // 상한이 `length - 1`인 것이 "최신 1개는 남긴다" 규칙이다.
  for (let i = 0; i < candidates.length - 1; i++) {
    if (keptCount <= limits.maxSessions && keptBytes <= limits.maxBytes) break;
    const victim = candidates[i]!;
    remove.push(victim.name);
    keptCount--;
    keptBytes -= victim.bytes;
  }

  return {
    remove,
    keptCount,
    keptBytes,
    triggers,
    stillOverLimit: keptCount > limits.maxSessions || keptBytes > limits.maxBytes,
  };
}
