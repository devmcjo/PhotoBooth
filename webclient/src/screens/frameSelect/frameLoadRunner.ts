import { hasUsableImage } from "@domain/frames/frameCatalogPolicy";
import {
  catalogProgressLabel,
  CATALOG_START_LABEL,
  type FrameCatalogProgress,
} from "@domain/frames/frameCatalogProgress";
import {
  finalizeFrameLoad,
  frameLoadNotice,
  MAX_TOTAL_WAIT_SECONDS,
  NO_PROGRESS_TIMEOUT_SECONDS,
  type FrameLoadPhase,
} from "@domain/frames/frameLoadPolicy";
import type { FrameTemplate } from "@domain/frames/types";
import {
  FrameLoadCancelledError,
  type FrameCatalogLoadOptions,
  type FrameCatalogResult,
  type UnavailableFrame,
} from "@adapters/frames/frameCatalog";
import { logger } from "@adapters/storage/logStore";
import type { LoadDeadline } from "./frameLoadDeadline";

/**
 * `FrameSelect` 로딩 루틴 — Windows `FrameSelectViewModel.ReloadFramesAsync`의 웹 대응 (03 §4.1)
 *
 * **`finally`가 국면을 무조건 확정한다.** `finalizeFrameLoad`는 어떤 입력에서도 `Loading`을
 * 반환하지 않으므로(Step 8.5가 32조합 전수로 고정) 이 구조에서 **오버레이 고착은 원리적으로
 * 불가능**하다. `Loading`으로 남는 경로는 "이 로딩이 이미 stale"뿐이고, 그때는 화면이 이미
 * 바뀌었거나 새 로딩이 국면을 다시 소유한다.
 *
 * ⚠️ React를 import하지 않는다 — 순서·판정이 node에서 통째로 검증된다(`runResultNext` 선례).
 */

export type FrameLoadReason = "enter" | "retry" | "refresh";

/** 부분 갱신. 적용(상태 반영)은 호출자 몫이다. */
export interface FrameSelectPatch {
  readonly phase?: FrameLoadPhase;
  readonly loadingMessage?: string;
  readonly notice?: string;
  readonly frames?: readonly FrameTemplate[];
  readonly unavailable?: readonly UnavailableFrame[];
  readonly selectedId?: string | null;
}

export interface FrameLoadDeps {
  loadPublic(options: FrameCatalogLoadOptions): Promise<FrameCatalogResult>;
  loadLocalOnly(): Promise<FrameCatalogResult>;
  loadPersonal(userId: string): Promise<readonly FrameTemplate[]>;
  currentUserId(): string | null;
  /** 이 로딩 시작 시점의 국면 — `finalizeFrameLoad(current, …)`의 첫 인자. */
  initialPhase(): FrameLoadPhase;
  /**
   * 로딩 시작 시점의 목록 길이. quiet 재스캔이 중단됐을 때 `finalize`의 근거가 된다.
   * ⚠️ 0으로 시작하면 화면에 이미 떠 있는 목록과 어긋나 `Failed`가 잘못 뜬다.
   */
  initialFrameCount(): number;
  /** 이 로딩이 아직 최신인가(화면 이탈·재시도 연타 판정). */
  isStale(): boolean;
  apply(patch: FrameSelectPatch): void;
  createDeadline(abort: () => void): LoadDeadline;
  /**
   * 이 로딩의 취소 핸들을 호출자에게 넘긴다(컨트롤러 생성 **직후** 1회).
   * [기다리지 않고 시작]과 화면 이탈 cleanup이 이것으로 **현재 로딩만** 끊는다 —
   * 공유 작업은 계속 진행해 캐시를 완성한다(호출자별 취소).
   */
  registerAbort?(abort: () => void): void;
}

function describe(err: unknown): string {
  return err instanceof Error ? err.message : String(err);
}

/** 로컬 전용 폴백. 여기까지 실패하면 **빈 결과**로 축퇴해 `Failed`가 실제로 도달 가능해진다. */
async function safeLocalOnly(deps: FrameLoadDeps): Promise<FrameCatalogResult> {
  try {
    return await deps.loadLocalOnly();
  } catch (err) {
    logger.warn("로컬 전용 폴백도 실패 — 빈 목록으로 축퇴", { reason: describe(err) });
    return { frames: [], unavailable: [], source: "Fallback" };
  }
}

export async function runFrameLoad(
  deps: FrameLoadDeps,
  reason: FrameLoadReason,
): Promise<void> {
  // 삭제 직후 재스캔은 **조용한 갱신**이다(오버레이·진행 문구 없음 — 03 §4.1).
  const quiet = reason === "refresh";
  // 지역 사본 — `apply`가 비동기로 반영돼도 `finally`가 옳은 값을 읽는다.
  let phase = deps.initialPhase();
  let frameCount = deps.initialFrameCount();
  let interrupted = false;
  let completed = false;

  if (!quiet) {
    phase = "Loading";
    deps.apply({ phase, loadingMessage: CATALOG_START_LABEL, notice: "" });
  }

  const controller = new AbortController();
  deps.registerAbort?.(() => controller.abort());
  // quiet에서도 상한은 동일하게 건다 — 무한 대기 금지는 계기와 무관하다.
  const deadline = deps.createDeadline(() => controller.abort());
  try {
    deadline.arm();

    const onProgress = quiet
      ? undefined
      : (progress: FrameCatalogProgress): void => {
          if (deps.isStale()) return; // 늦은 보고가 새 로딩 문구를 덮지 않게
          deps.apply({ loadingMessage: catalogProgressLabel(progress) });
          deadline.arm(); // 진행 관측 → 무진행 타이머 재무장
        };

    let result: FrameCatalogResult;
    try {
      result = await deps.loadPublic({
        signal: controller.signal,
        ...(onProgress === undefined ? {} : { onProgress }),
      });
    } catch (err) {
      // 화면 이탈 취소 → `finally`도 아무것도 하지 않는다(폐기된 화면의 상태를 쓰지 않는다).
      if (deps.isStale()) return;
      // 취소와 그 밖의 실패를 **같은 갈래**로 다룬다. 구분은 로그 문구에만 쓴다.
      interrupted = true;
      logger.warn("기본 프레임 대기 중단 — 로컬 전용 폴백", {
        reason: err instanceof FrameLoadCancelledError ? "cancelled" : describe(err),
        noProgressSec: NO_PROGRESS_TIMEOUT_SECONDS,
        totalSec: MAX_TOTAL_WAIT_SECONDS,
      });
      result = await safeLocalOnly(deps);
    }
    if (deps.isStale()) return;

    const merged = [...result.frames];
    const userId = deps.currentUserId();
    if (userId !== null) {
      // 개인 프레임 실패가 공용 목록을 무너뜨리지 않게 개별 방어(Windows와 동형).
      try {
        merged.push(...(await deps.loadPersonal(userId)));
      } catch (err) {
        logger.warn("개인 프레임 로드 실패(공용 목록은 유지)", { reason: describe(err) });
      }
    }
    if (deps.isStale()) return;

    // 목록을 **미리 비우지 않는다.** 마지막에 한 번 교체한다 — 선행 비우기는 quiet 재스캔에서
    // "빈 목록 + 조작 열림"을 노출하고 enter 경로에서도 목록을 깜빡이게 한다.
    const frames = merged.filter(hasUsableImage);
    frameCount = frames.length;
    deps.apply({
      frames,
      unavailable: result.unavailable,
      selectedId: frames[0]?.id ?? null,
    });
    completed = true;
  } finally {
    deadline.dispose();
    if (!deps.isStale()) {
      // ★ 무조건 확정 — `Loading` 고착 방지의 구조적 장치다.
      const next = finalizeFrameLoad(phase, frameCount, interrupted || !completed, quiet);
      deps.apply({ phase: next, notice: frameLoadNotice(next) });
    }
  }
}
