/**
 * 프레임 이름 규칙·사본 이름 생성 — Windows `Frames/FrameNaming.cs` 이식 (analysis/13 §6.2·§6.4)
 */

/** 사본 접미 토큰. 파일명 규약상 `_`를 쓰지 않는다(공용/개인 구분자이므로). */
export const COPY_SUFFIX = "사본";

/** 이름이 비어 있을 때 쓰는 기본 base(결과는 "새 프레임 사본"). */
export const DEFAULT_BASE_NAME = "새 프레임";

/** 충돌 회피 번호 상한. 초과 시 8자 난수 접미로 폴백한다. */
export const MAX_COPY_INDEX = 99;

export const MAX_FRAME_NAME_LENGTH = 100;

/** `"{X} 사본"` / `"{X} 사본 N"`(N = 1~2자리) 접미 패턴. */
const COPY_SUFFIX_PATTERN = /^(.*?)\s*사본(?:\s+(\d{1,2}))?$/;

/**
 * Windows `Path.GetInvalidFileNameChars()` 대응 — `< > : " / \ | ? *` + 제어문자(0x00~0x1F).
 * 프레임을 Windows `Frame\`으로 내보낼 수 있어야 하므로(WD4) 웹에서도 같은 문자를 거부한다.
 * 공백·하이픈은 **금지문자가 아니다**(사본 이름 "새 프레임 사본"이 공백을 포함한다).
 */
// eslint-disable-next-line no-control-regex
const INVALID_FILENAME_CHARS = /[<>:"/\\|?*\u0000-\u001f]/;

/**
 * `"{X} 사본"` / `"{X} 사본 N"` 접미를 제거해 원형 이름을 얻는다(접미가 없으면 원문 그대로).
 * 접미를 떼면 이름이 비게 되는 경우(예: `"사본"`)도 원문을 그대로 반환한다 — 빈 이름을 만들지 않는다.
 */
export function stripCopySuffix(name: string): string {
  if (name.trim().length === 0) return name;

  const match = COPY_SUFFIX_PATTERN.exec(name.trim());
  if (match === null) return name;

  const stripped = (match[1] ?? "").replace(/\s+$/, "");
  return stripped.trim().length === 0 ? name : stripped;
}

/**
 * `baseName` 기준으로 `existingNames`와 충돌하지 않는 사본 이름을 만든다.
 * `"{base} 사본"` → 충돌 시 `"{base} 사본 2"` … 99까지 → 그 뒤 `"{base} 사본 {8자 난수}"`.
 * 이미 사본 형태면 원형으로 되돌려 무한 누적을 막는다. 비교는 정확 일치(Ordinal).
 *
 * @param uniqueSuffix 99까지 모두 충돌할 때 쓸 8자 접미 생성기. 도메인은 난수를 직접 만들지 않는다
 *                     (어댑터가 `crypto`로 주입 — 01 §8). **항상 이름을 돌려준다**(저장을 막지 않는다).
 */
export function nextCopyName(
  baseName: string | null | undefined,
  existingNames: Iterable<string>,
  uniqueSuffix: () => string,
): string {
  let root = stripCopySuffix(baseName ?? "");
  if (root.trim().length === 0) root = DEFAULT_BASE_NAME;

  const taken = new Set<string>();
  for (const name of existingNames) {
    if (name.length > 0) taken.add(name);
  }

  const first = `${root} ${COPY_SUFFIX}`;
  if (!taken.has(first)) return first;

  for (let n = 2; n <= MAX_COPY_INDEX; n++) {
    const candidate = `${root} ${COPY_SUFFIX} ${n}`;
    if (!taken.has(candidate)) return candidate;
  }

  return `${root} ${COPY_SUFFIX} ${uniqueSuffix()}`;
}

export type FrameNameRejection = "empty" | "too-long" | "invalid-chars" | "underscore";

export interface FrameNameValidation {
  readonly ok: boolean;
  readonly reason?: FrameNameRejection;
}

/**
 * 로컬 저장용 이름 검증: 1~100자 + 파일시스템 금지문자 없음(치환하지 않고 거부).
 *
 * ⚠️ **`_`를 여기서 거부하지 않는다.** 로컬 개인 저장은 서버를 거치지 않으므로 `_`가 계약 위반이
 *    아니다. 다만 **공용** 스코프에서는 파일명 규약과 충돌하므로 `underscoreWarning`으로 안내한다
 *    (Windows `FrameEditorViewModel`과 동일한 비차단 경고).
 *    **서버에 등록하는 경로는 `validateFrameNameForServer`를 쓴다** — 서버가 400으로 거부한다.
 */
export function validateFrameName(name: string): FrameNameValidation {
  const trimmed = name.trim();
  if (trimmed.length === 0) return { ok: false, reason: "empty" };
  if (trimmed.length > MAX_FRAME_NAME_LENGTH) return { ok: false, reason: "too-long" };
  if (INVALID_FILENAME_CHARS.test(trimmed)) return { ok: false, reason: "invalid-chars" };
  return { ok: true };
}

/**
 * 서버 등록용 이름 검증 — `POST /frames`에 보낼 이름(power 신규 공용 프레임)에 쓴다.
 *
 * 로컬 규칙 + **`_` 금지**(M15). 서버 `domain/validation.ts validateFrameName`이 `_`를 400으로
 * 거부하므로, 보내기 전에 같은 판정을 해 사용자에게 즉시 사유를 알린다(성공 오인·왕복 낭비 방지).
 * 두 판정이 어긋나면 서버가 진실원이다.
 */
export function validateFrameNameForServer(name: string): FrameNameValidation {
  const local = validateFrameName(name);
  if (!local.ok) return local;
  if (name.trim().includes("_")) return { ok: false, reason: "underscore" };
  return { ok: true };
}

/** 프레임 저장 스코프. 공용 = 접두 없는 파일명, 개인 = `{계정}_{이름}`. */
export type FrameSaveScope = "public" | "personal";

/**
 * 공용 스코프 저장 시 이름에 `_`가 있으면 **비차단 경고** 대상이다.
 * 공용 목록은 파일명에 `_`가 없는 것만 집계하므로(개인 접두 구분자) 목록에서 보이지 않을 수 있다.
 * 개인 스코프는 `{계정}_` 접두가 붙는 것이 정상이라 경고하지 않는다.
 */
export function underscoreWarning(name: string, scope: FrameSaveScope): boolean {
  return scope === "public" && name.includes("_");
}
