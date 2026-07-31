/**
 * 기본 프레임 로딩 대기 정책 — Windows `Core/Frames/FrameLoadPolicy.cs` 이식 (analysis/13 §4.2 · 03 §4.1, it20)
 *
 * 첫 방문은 로컬 캐시가 비어 서버 다운로드를 기다린다. **웹은 이 상황이 Windows보다 훨씬 잦다** —
 * 신규 기기·시크릿 창·저장소 비우기마다 매번 첫 방문이다. 그 대기에 **상한**·**결과 판정**·**안내 문구**를
 * 부여하는 것이 이 모듈의 책임이고, 화면(Step 14)은 국면만 소비한다.
 *
 * ⚠️ 판정 값은 `docs/spec-vectors/frame-load-policy.json`으로 Windows와 교차 고정돼 있다.
 *    로직을 바꾸려면 **벡터를 먼저 고쳐** 양쪽을 동시에 실패시킨다(10 §3.3).
 */

/** 로딩 4국면. 벡터·로그가 C# enum **이름**을 그대로 쓰므로 문자열 유니온이다(`appState.ts` 관례). */
export const FRAME_LOAD_PHASES = ["Loading", "Ready", "Degraded", "Failed"] as const;
export type FrameLoadPhase = (typeof FRAME_LOAD_PHASES)[number];

/**
 * 초기 국면. C#은 enum 0번 값이 `Loading`이라 필드 초기화를 빠뜨려도 안전하게 대기로 시작하는데,
 * TS엔 그 안전망이 없으므로 **명시 상수로 대체**한다. 화면 상태의 초기값은 반드시 이 값을 쓴다.
 */
export const DEFAULT_FRAME_LOAD_PHASE: FrameLoadPhase = "Loading";

/**
 * 무진행(inactivity) 상한(초). 진행 보고가 이 시간 동안 한 번도 없으면 대기를 포기한다.
 * wall-clock 예산을 쓰지 않는 이유: 최초 실행의 지배 경로는 시작 prefetch가 이미 다운로드 중일 때
 * 진입하는 것이라, wall-clock 예산은 **정상 진행 중인 다운로드를 잘라** "실패했다"는 거짓 안내를 띄운다.
 * 단계 전환이 곧 진행의 증거이므로 무진행으로 정의한다.
 */
export const NO_PROGRESS_TIMEOUT_SECONDS = 30;

/**
 * 총 대기 상한(초). 아무리 진행 중이어도 손님을 이보다 길게 세워두지 않는다.
 * **유휴 무동작 판정(120초)보다 짧아야 한다** — 대기 중에 "자리를 비우셨나요?" 팝업이 겹치지 않게(02 §6.2).
 */
export const MAX_TOTAL_WAIT_SECONDS = 60;

/**
 * 유휴 경고 기본값(초)의 **사본**. 상한 불변식을 도메인 테스트에서 확인하기 위한 참조 상수이며
 * 진실원은 `@shell/idleWatchdog`의 `IDLE_TIMEOUT_MS`다. 사본이 어긋나면 셸 테스트가 잡는다
 * (Windows는 Core→App 참조가 불가능해 못 하는 검사다 — 웹에서만 가능한 추가 안전망).
 */
export const IDLE_WARNING_REFERENCE_SECONDS = 120;

/** ms 파생 상수 — TS엔 `TimeSpan`이 없으므로 **단위를 이름에 박는다**(초/ms 혼동 방지). */
export const NO_PROGRESS_TIMEOUT_MS = NO_PROGRESS_TIMEOUT_SECONDS * 1000;
export const MAX_TOTAL_WAIT_MS = MAX_TOTAL_WAIT_SECONDS * 1000;

/**
 * 지금부터 취소까지 남겨 둘 시간(ms). 무진행 상한과 총 상한 중 **먼저 오는 쪽**을 돌려준다.
 * 진행 보고마다 호출해 취소 타이머를 재무장한다. **0 이하면 즉시 취소해야 한다**(총 상한 도달).
 *
 * ⚠️ `Math.min(NO_PROGRESS_TIMEOUT_MS, MAX_TOTAL_WAIT_MS - elapsedMs)`로 축약하지 마라. 값은 같아 보이지만
 *    잔량이 음수일 때 **음수를 반환한다**(C#은 `<= 0`을 먼저 걸러 정확히 0을 준다). 원본과 같은 2단 분기로 쓴다.
 *
 * @param elapsedMs 이 로딩이 시작된 뒤 흐른 **실경과**(WM3 — tick 누적이 아니라 시각 델타).
 *                  도메인은 시계를 갖지 않으므로 호출자가 잰 값을 받는다.
 */
export function nextFrameLoadDeadlineMs(elapsedMs: number): number {
  const remainingTotal = MAX_TOTAL_WAIT_MS - elapsedMs;
  if (remainingTotal <= 0) return 0;
  return remainingTotal < NO_PROGRESS_TIMEOUT_MS ? remainingTotal : NO_PROGRESS_TIMEOUT_MS;
}

/**
 * 로딩 결과 판정.
 * - `frameCount <= 0` → `Failed`(쓸 프레임이 없다. 음수도 방어적으로 같은 갈래)
 * - `waitInterrupted` → `Degraded`(상한 초과 · [기다리지 않고 시작] · 예외)
 * - 그 외 → `Ready`
 *
 * **서버 조회 실패 자체는 `Degraded`가 아니다.** 오프라인 부스가 로컬 캐시로 조용히 운영되는 것이
 * 종전 동작이며(it10 폴백), 안내를 띄우면 매 진입 노이즈가 된다 — 조회 실패는 어댑터에서 삼켜져
 * `waitInterrupted=false`로 도달한다(analysis/13 §4.2 · E20).
 */
export function classifyFrameLoad(frameCount: number, waitInterrupted: boolean): FrameLoadPhase {
  if (frameCount <= 0) return "Failed";
  return waitInterrupted ? "Degraded" : "Ready";
}

/**
 * 로딩 종료 시 확정할 국면. 화면의 `finally`가 **무조건** 이 함수로 국면을 닫는다(Loading 고착 방지).
 *
 * `quiet=true`(삭제 후 조용한 재스캔)면 종전 국면을 유지한다. 단 세 경우는 예외 없이 갱신한다:
 * ① 프레임 0개 → `Failed`(빈 목록 + 활성 [다음]은 이 설계가 없애려는 상태),
 * ② 종전이 `Failed`인데 프레임이 생겼으면 `Ready`로 회복,
 * ③ 종전이 `Loading`이면 `Ready`로 닫는다 — **반환값에 `Loading`이 없다**는 불변식을 조건 없이 성립시킨다.
 *    (③을 빠뜨리면 조용한 재스캔이 대기 오버레이를 영구 고착시킨다. Windows 설계 §5.1 코드 조각이 실제로
 *     이 갈래를 빠뜨려 §10.1 T-8 진리표와 모순됐고, **불변식 쪽을 채택**한 것이 현재 규격이다.)
 *
 * @param current 종료 직전 국면
 * @param frameCount 최종 목록 개수
 * @param waitInterrupted 대기가 중단됐거나 정상 완료에 도달하지 못했는지
 * @param quiet 조용한 재스캔(오버레이·안내를 띄우지 않는 계기)인지
 */
export function finalizeFrameLoad(
  current: FrameLoadPhase,
  frameCount: number,
  waitInterrupted: boolean,
  quiet: boolean,
): FrameLoadPhase {
  if (frameCount <= 0) return "Failed";
  if (!quiet) return classifyFrameLoad(frameCount, waitInterrupted);
  return current === "Failed" || current === "Loading" ? "Ready" : current;
}

/** `Degraded` 안내. "모두"가 들어가는 이유는 `frameLoadNotice` 주석 참조. */
export const FRAME_LOAD_DEGRADED_NOTICE =
  "서버 프레임을 모두 가져오지 못해 지금 준비된 프레임으로 진행합니다.";
export const FRAME_LOAD_FAILED_NOTICE =
  "사용할 수 있는 프레임이 없습니다. 네트워크를 확인하고 다시 시도해 주세요.";

/**
 * 국면별 사용자 안내(`Loading`·`Ready`는 빈 문자열).
 *
 * "가져오지 못해"가 아니라 **"모두 가져오지 못해"** 인 이유: 총 상한(60초) 초과는 진행 중인 정상 다운로드도
 * 자르므로 **일부는 이미 받아 목록에 들어와 있을 수 있다**. "전부 실패"로 적으면 거짓이 된다.
 *
 * ⚠️ 이 문구는 `@ui/strings`에 복제하지 않는다. 03 §4.1의 "판정·문구는 순수 함수로 두고 화면은 국면만
 *    소비한다"가 이 축의 규격이고, Windows도 `Core`에 둔다 — 두 곳에 두면 갈라진다.
 */
export function frameLoadNotice(phase: FrameLoadPhase): string {
  switch (phase) {
    case "Degraded":
      return FRAME_LOAD_DEGRADED_NOTICE;
    case "Failed":
      return FRAME_LOAD_FAILED_NOTICE;
    default:
      return "";
  }
}
