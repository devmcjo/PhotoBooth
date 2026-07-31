import type { OutputFormat } from "../settings/appSettings";
import { isValidSessionId } from "../upload/uploadContract";

/**
 * 결과물 로컬 보관 이름 규약 — Windows `LocalSave/LocalSaveService.cs` 이식 (analysis/41 §5)
 *
 * ⚠️ 이 값들은 **Windows 앱과 같아야 한다.** 운영자가 두 플랫폼의 결과 폴더를 같은 규칙으로
 *    정렬·검색하고, 서버 세션과 짝지을 수 있어야 하기 때문이다.
 * ⚠️ 도메인은 시각·난수를 만들지 않는다 — 어댑터가 `Date`·32자 hex 토큰을 주입한다(01 §8).
 */

/** 결과물 세션 폴더 접두 — Windows `LocalSaveService.SessionFolderName`과 동일. */
export const RESULT_FOLDER_PREFIX = "mcphoto_";

/** 타임랩스 파일명. 계약 고정값이라 포맷 분기가 없다. */
export const TIMELAPSE_FILE_NAME = "timelapse.mp4";

/** 충돌 접미 최대치. Windows `for (int i = 2; i < 1000; i++)`와 같다. */
export const MAX_RESULT_FOLDER_SUFFIX = 999;

/**
 * 우리 규약으로 만든 폴더명 판정 패턴.
 * `mcphoto_` + 6자리 + `_` + 4자리 + 선택적 `-{2..999}` 또는 `-{32자리 hex}`.
 * 접두를 상수에서 조립해 이름이 바뀌어도 어긋나지 않게 한다.
 */
const RESULT_FOLDER_PATTERN = new RegExp(
  `^${RESULT_FOLDER_PREFIX}\\d{6}_\\d{4}(?:-(?:[2-9]|[1-9]\\d{1,2})|-[0-9a-f]{32})?$`,
);

function pad2(value: number): string {
  return String(value).padStart(2, "0");
}

/**
 * 세션 폴더명 `mcphoto_YYMMDD_HHMM` (예 `mcphoto_260720_1445`).
 * **로컬 시각 성분**으로 조립한다 — 운영자가 폴더를 시각으로 정렬·검색하기 때문이다.
 */
export function resultFolderName(localTime: Date): string {
  return (
    RESULT_FOLDER_PREFIX +
    pad2(localTime.getFullYear() % 100) +
    pad2(localTime.getMonth() + 1) +
    pad2(localTime.getDate()) +
    "_" +
    pad2(localTime.getHours()) +
    pad2(localTime.getMinutes())
  );
}

/**
 * `sessionId`(`yyyyMMdd_HHmmss_uuid`)에서 같은 폴더명을 유도한다.
 * 형식이 어긋나면 `null`(호출자가 `resultFolderName(localTime)`으로 폴백).
 *
 * ⚠️ 이 경로가 **기본**이다: 폴더 시각이 업로드 `sessionId`와 같은 순간을 가리켜야
 *    운영자가 로컬 폴더와 서버 세션을 짝지을 수 있다. Windows도 `session.SessionTime`
 *    (촬영 시작 시각)을 쓴다.
 *
 * 인덱스 규약: `0-3 yyyy · 4-5 MM · 6-7 dd · 8 '_' · 9-10 HH · 11-12 mm · 13-14 ss`.
 */
export function resultFolderNameFromSessionId(sessionId: string): string | null {
  if (!isValidSessionId(sessionId)) return null;
  return `${RESULT_FOLDER_PREFIX}${sessionId.slice(2, 8)}_${sessionId.slice(9, 13)}`;
}

/**
 * 충돌 해석. 같은 이름이 있으면 `-2`, `-3` … `-999`, 그래도 없으면 `-{fallbackToken}`.
 *
 * @param fallbackToken 어댑터가 주입하는 32자 hex. 도메인은 난수를 만들지 않는다(01 §8).
 */
export function resolveResultFolderName(
  base: string,
  existing: readonly string[],
  fallbackToken: string,
): string {
  const taken = new Set(existing);
  if (!taken.has(base)) return base;
  for (let suffix = 2; suffix <= MAX_RESULT_FOLDER_SUFFIX; suffix++) {
    const candidate = `${base}-${suffix}`;
    if (!taken.has(candidate)) return candidate;
  }
  // 극단 상황: Windows의 `Guid:N` 폴백과 같은 모양.
  return `${base}-${fallbackToken}`;
}

/** `final.jpg` 또는 `final.png`. */
export function finalFileName(format: OutputFormat): string {
  return format === "Png" ? "final.png" : "final.jpg";
}

/**
 * 우리 규약으로 만든 폴더명인가. **보존 정책의 삭제 후보를 이 판정으로 좁힌다** —
 * 사용자가 저장소를 직접 만졌거나 다른 기능이 `results/` 아래에 둔 것을 지우지 않기 위해서다.
 */
export function isResultFolderName(name: string): boolean {
  return RESULT_FOLDER_PATTERN.test(name);
}
