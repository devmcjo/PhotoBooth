/**
 * 기본 프레임 준비 진행 표현 — Windows `Core/Frames/FrameCatalogProgress.cs` 이식 (03 §4.1 · 06 §6.1, it20)
 *
 * 표시 문구를 순수 함수로 함께 제공한다 — 화면이 문자열을 조립하지 않으므로 **문구가 UI 없이 단위 테스트된다**
 * (`slotAspectToLabel` 관례와 동형). 카탈로그 로더(단일 비행 + 진행 replay)는 Step 14가 만든다.
 *
 * ⚠️ 말줄임표는 U+2026 `…` **한 글자**다(`...` 세 점이 아니다). Windows 문구와 바이트가 달라지면 안 된다.
 */

export const FRAME_CATALOG_PHASES = [
  "ResolvingLocal",
  "QueryingServer",
  "DownloadingImage",
  "Completed",
] as const;
export type FrameCatalogPhase = (typeof FRAME_CATALOG_PHASES)[number];

/**
 * 진행 보고 1건. `index`·`total`은 **다운로드 단계에서만** 의미가 있어 선택 필드다
 * (C# `record struct`의 `Index = 0, Total = 0` 기본값에 대응).
 *
 * ⚠️ **프레임 이름을 담지 않는다.** 운영자가 자유 입력하는 이름은 길이 제한이 없어 카드 폭을 넘기거나
 *    오버레이 높이를 요동시킨다(Windows §5.2 판정 — 테스트가 회귀를 막는다).
 */
export interface FrameCatalogProgress {
  readonly phase: FrameCatalogPhase;
  /** 다운로드 중인 항목 순번(1-based). 다른 단계에서는 의미 없음. */
  readonly index?: number;
  /** 전체 다운로드 대상 수. `0`이면 카운터를 붙이지 않는다. */
  readonly total?: number;
}

/** 로딩 시작 직후(아직 어떤 보고도 없을 때) 보여줄 기본 문구. 오버레이의 빈 문구 구간을 없앤다. */
export const CATALOG_START_LABEL = "기본 프레임을 준비하고 있어요…";

/** 이 진행 상황의 한국어 표시 문구. `total > 0`일 때만 `(n/m)` 카운터를 덧붙인다. */
export function catalogProgressLabel({
  phase,
  index = 0,
  total = 0,
}: FrameCatalogProgress): string {
  switch (phase) {
    case "ResolvingLocal":
      return "설치된 프레임을 확인하는 중…";
    case "QueryingServer":
      return "서버에서 기본 프레임 목록을 확인하는 중…";
    case "DownloadingImage":
      // total=0에 "(0/0)"을 붙이면 진행이 멈춘 것처럼 보인다 → 카운터를 생략한다.
      return total > 0
        ? `기본 프레임 내려받는 중… (${index}/${total})`
        : "기본 프레임 내려받는 중…";
    case "Completed":
      return "프레임 목록을 정리하는 중…";
    default:
      // 알 수 없는 단계(서버·미래 버전에서 온 값)는 시작 문구로 떨어진다 — 빈 문구를 만들지 않는다.
      return CATALOG_START_LABEL;
  }
}
