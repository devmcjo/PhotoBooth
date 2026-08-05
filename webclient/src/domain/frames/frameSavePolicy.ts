import { canWriteFrames, isPower, type UserRole } from "../roles/userRole";
import { requiresFork } from "./frameEditPolicy";
import { isFileNameSafe, type FrameSaveScope } from "./frameNaming";
import { exceedsLocalFrameLimit } from "./frameStorePolicy";
import { isValidLayout } from "./slotLayout";
import type { FrameTemplate, Slot } from "./types";

/**
 * 프레임 저장 판정 — 세션 정체성 축 + 저장 전 검증 7단 + ⑧ (03 §11.3·§11.4 · analysis/13 §6.3·§6.4)
 *
 * ⚠️ **이 파일이 순서를 소유한다.** 진입점이 [저장]과 서버 등록 확인 오버레이 **2개**이므로 한쪽만
 *    검사하면 오버레이 경로로 우회된다. 비동기 조회(⑦ 스코프 이름, ⑧ 개인 개수)는 호출자가 먼저
 *    끝내고 **판정은 여기 순수 함수 하나**가 한다.
 * ⚠️ 문자열을 갖지 않는다 — reason 유니온만 돌려주고 문구 조립은 `ui/strings.ts`가 한다.
 */

// ───────────────────────────── 세션 정체성 축 ─────────────────────────────

export const FRAME_SESSION_SOURCES = ["New", "EditOwnLocal", "ForkFromCatalog"] as const;
export type FrameSessionSource = (typeof FRAME_SESSION_SOURCES)[number];

/**
 * [선택 편집] 진입 시의 세션 축. `requiresFork(frame)`가 유일한 판정 근거다.
 *
 * ⚠️ **[기존 프레임 불러오기] 피커는 이 함수를 부르지 않는다.** 피커로 불러온 세션의 정체성은
 *    `New`(신규 생성)이며 사본이 아니다(2026-07-30 재정의 — analysis/13 §6.5).
 */
export function editSessionSource(frame: FrameTemplate): FrameSessionSource {
  return requiresFork(frame) ? "ForkFromCatalog" : "EditOwnLocal";
}

/** 저장 스코프. power = 공용, 그 외(advanced_user) = 개인. */
export function frameSaveScope(role: UserRole | null): FrameSaveScope {
  return role !== null && isPower(role) ? "public" : "personal";
}

/**
 * 정책 배너(analysis/13 §6.4)는 **편집 세션 전용**이다 —
 * 신규 생성 세션은 서버 등록이 가능하므로 "이 기기에만 적용된다"는 문장이 거짓이 된다.
 */
export function showsLocalOnlyBanner(source: FrameSessionSource): boolean {
  return source !== "New";
}

/** 서버 등록 확인 오버레이의 체크박스 기본값. 삭제 확인(기본 off)과 **축이 다르다**(03 §11.4). */
export const DEFAULT_REGISTER_TO_SERVER = true;

/**
 * 서버 등록 확인 오버레이 노출 조건 = **서버 등록 분기와 완전히 같은 축**(03 §11.4).
 *
 * ⚠️ `isCreateMode` 같은 파생값을 쓰지 마라 — 두 축이 갈라지면 "오버레이는 떴는데 등록은 안 되는"
 *    조용한 불일치가 생긴다. 권한 축은 `isPower`만 쓴다(`canWriteFrames`로 넓히면 DB 권한이 없는
 *    advanced_user에게 서버 등록 체크박스를 노출한다).
 * ⚠️ 이 함수의 호출부는 **정확히 2곳**이다(오버레이 판정 · 등록 분기). 정적 검사 FR-11이 고정한다.
 */
export function requiresServerRegisterPrompt(
  role: UserRole | null,
  source: FrameSessionSource,
): boolean {
  return role !== null && isPower(role) && source === "New";
}

export type SaveScopeNoticeKind = "public-new" | "public-fork" | "overwrite" | "personal";

/** 저장 버튼 위 캡션의 **종류**만 고른다. 문구 조립은 UI(`ui/strings.ts`)가 한다. */
export function saveScopeNoticeKind(
  role: UserRole | null,
  source: FrameSessionSource,
): SaveScopeNoticeKind {
  if (role !== null && isPower(role)) {
    if (source === "New") return "public-new";
    if (source === "ForkFromCatalog") return "public-fork";
    return "overwrite";
  }
  return source === "EditOwnLocal" ? "overwrite" : "personal";
}

// ───────────────────────────── 배율 범위 ─────────────────────────────

/**
 * 슬롯 배율 범위(03 §11.2 · analysis/13 §6.2 · analysis/14 §4.2).
 *
 * ⚠️ **되돌리지 마라.** 규격 문서에 한동안 `70~130`이 남아 있었지만 그것은 **폐기된 초기 설계값**이다.
 *    커밋 `0a93b59`("프레임 편집기 강화 — 슬롯 스케일 10~300%·직접입력")이 의도적으로 넓혔고
 *    현행 Windows 구현이 `FrameEditorViewModel.MinScale = 10` / `MaxScale = 300`이다.
 *    진실원 우선순위는 **실제 소스 > docs/analysis > docs/design**(`docs/design/README.md §4`)이므로
 *    소스가 사실이고 문서를 갱신하는 것이 맞다 — 2026-08-01에 규격 문서 6곳을 여기에 맞췄다.
 */
export const MIN_SCALE_PERCENT = 10;
export const MAX_SCALE_PERCENT = 300;
/** 편집 세션 진입·이미지 교체 직후의 배율. */
export const DEFAULT_SCALE_PERCENT = 100;

// ───────────────────────────── 저장 전 검증 ─────────────────────────────

export interface FrameSaveValidationInput {
  /** null = 게스트(비로그인). */
  readonly role: UserRole | null;
  readonly sessionSource: FrameSessionSource;
  /** PNG 바이트를 확보했는가(이미지 미로드 상태 차단). */
  readonly hasImage: boolean;
  readonly slots: readonly Slot[];
  readonly frameWidth: number;
  readonly frameHeight: number;
  /** **원문 그대로**(trim하지 않는다 — 실제 저장되는 문자열이다). */
  readonly name: string;
  /** fork 원본 이름. 없으면 "". */
  readonly sourceName: string;
  /**
   * 저장 스코프의 기존 이름들. ⚠️ **열거 실패는 빈 배열**이고 그때 ⑦은 조용히 꺼진다(비차단).
   * 그것이 ④를 ⑦보다 먼저 두는 이유다(2중 방어).
   */
  readonly existingNames: readonly string[];
  /** 저장 **전** 개인 프레임 개수. 공용 스코프에서는 무시된다. */
  readonly personalCount: number;
}

/**
 * 차단 사유. **선언 순서가 곧 검사 순서**이며 정적 검사 FR-13이 이 순서를 고정한다
 * (특히 ④ `same-as-source`가 ⑦ `name-conflict`보다 앞이라는 성질).
 */
export type FrameSaveRejection =
  | "not-logged-in" // ①
  | "no-write-permission" // ②
  | "invalid-slots" // ③
  | "same-as-source" // ④
  | "name-empty" // ⑤
  | "name-invalid-chars" // ⑥
  | "name-conflict" // ⑦
  | "limit-reached"; // ⑧ (웹 추가 게이트 — 7단 **뒤** 고정)

export interface FrameSaveValidation {
  readonly ok: boolean;
  readonly reason?: FrameSaveRejection;
}

/**
 * 저장 전 검증 — **순서가 규격이다**(03 §11.3).
 *
 * ⑧(개인 10개 상한)을 7단 뒤에 두는 이유: ①~⑦의 순서와 문구가 규격으로 고정돼 있어, 앞에 끼우면
 * "이름이 비었는데 `프레임은 최대 10개까지…`가 뜨는" 오안내가 생긴다.
 */
export function validateFrameSave(input: FrameSaveValidationInput): FrameSaveValidation {
  // ① 로그인
  if (input.role === null) return { ok: false, reason: "not-logged-in" };
  // ② 쓰기 권한 — 화면 게이트로 도달할 수 없는 역할이지만 저장 경로에도 둔다(3차 게이트, fail-closed).
  if (!canWriteFrames(input.role)) return { ok: false, reason: "no-write-permission" };
  // ③ 슬롯 유효성(개수 1~6 · 경계 내 · 겹침 없음) + 이미지 확보
  if (!input.hasImage) return { ok: false, reason: "invalid-slots" };
  if (!isValidLayout(input.slots, input.frameWidth, input.frameHeight)) {
    return { ok: false, reason: "invalid-slots" };
  }

  const isPublic = frameSaveScope(input.role) === "public";
  const isFork = input.sessionSource === "ForkFromCatalog";

  // ④ 원본 덮어쓰기 가드 — ⑦보다 **먼저**다.
  //    ⑦은 열거 실패 시 조용히 꺼지지만 ④는 "원본 이름"이라는 확정 사실만 본다.
  //    개인 스코프는 저장 키에 소유자가 들어가 공용 원본과 물리적으로 겹치지 않으므로 공용에서만 본다.
  if (isFork && isPublic && input.name === input.sourceName) {
    return { ok: false, reason: "same-as-source" };
  }

  // ⑤⑥ 이름 안전성 — **`isFileNameSafe` 하나**로 판정한다(길이를 보지 않는다).
  //     ⚠️ 100자 제한이 묶인 `validateFrameName` 계열을 쓰면 축이 어긋난다(03 §11.3 웹 주의).
  if (input.name.trim().length === 0) return { ok: false, reason: "name-empty" };
  if (!isFileNameSafe(input.name)) return { ok: false, reason: "name-invalid-chars" };

  // ⑦ 스코프 이름 충돌 — 예외는 **[선택 편집]으로 연 본인 로컬 생성분 세션 하나뿐**이다.
  //    로컬 저장은 같은 키를 경고 없이 덮어쓴다 → 가드가 없으면 다른 프레임이 조용히 파괴된다.
  //    비교는 정확 일치(Ordinal) — JS의 `===`가 그 축이다.
  const collides = input.existingNames.includes(input.name);
  if (input.sessionSource !== "EditOwnLocal" && collides) {
    return { ok: false, reason: "name-conflict" };
  }

  // ⑧ 개인 프레임 10개 상한(05 §4.8 — `scope:"user"`·`ownerId` 기준).
  //    ⚠️ **새 키를 만드는 저장에만** 적용한다. 덮어쓰기(이미 존재하는 이름)는 개수를 늘리지 않으므로
  //       상한에 걸리면 안 된다 — 안 그러면 10개를 채운 계정이 자기 프레임을 수정조차 못 한다.
  if (!isPublic && !collides && exceedsLocalFrameLimit(input.personalCount)) {
    return { ok: false, reason: "limit-reached" };
  }

  return { ok: true };
}
