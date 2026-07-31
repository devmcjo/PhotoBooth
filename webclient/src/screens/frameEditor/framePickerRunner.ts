import { hasUsableImage } from "@domain/frames/frameCatalogPolicy";
import type { FrameTemplate } from "@domain/frames/types";
import {
  FrameLoadCancelledError,
  type FrameCatalogLoadOptions,
  type FrameCatalogResult,
} from "@adapters/frames/frameCatalog";
import { logger } from "@adapters/storage/logStore";
import { STRINGS } from "@ui/strings";
import type { LoadDeadline } from "@screens/frameSelect/frameLoadDeadline";
import type { FramePickerPatch } from "./frameEditorState";

/**
 * [기존 프레임 불러오기] 후보 목록 로더 — 03 §11.5 · §15.4 (설계 §7.2)
 *
 * Step 14의 `runFrameLoad`를 **재사용하지 않는다**(그 함수의 patch 형태·국면 문구는 `FrameSelect`
 * 전용이다). 대신 **같은 구조**를 축소해 쓴다: 단일 비행 합류 + 호출자별 취소 + 상한 +
 * `finally` 무조건 확정.
 *
 * ⚠️ `finally`가 국면을 **무조건** 확정하므로 `loading` 고착이 구조적으로 불가능하다.
 * ⚠️ 취소는 **호출자별**이다 — 오버레이를 닫아도 공유 작업은 계속 진행해 캐시를 완성한다.
 * ⚠️ React를 import하지 않는다(순서·판정이 node에서 통째로 검증된다).
 */

export interface FramePickerDeps {
  loadPublic(options: FrameCatalogLoadOptions): Promise<FrameCatalogResult>;
  loadLocalOnly(): Promise<FrameCatalogResult>;
  loadPersonal(userId: string): Promise<readonly FrameTemplate[]>;
  currentUserId(): string | null;
  /** 이 로딩이 아직 최신인가(오버레이 닫힘·재열기 판정). */
  isStale(): boolean;
  apply(patch: FramePickerPatch): void;
  /** `defaultLoadDeadline`(Step 14 모듈)을 훅이 주입한다 — 러너는 브라우저 타이머를 모른다. */
  createDeadline(abort: () => void): LoadDeadline;
  registerAbort(abort: () => void): void;
}

function describe(err: unknown): string {
  return err instanceof Error ? err.message : String(err);
}

/** 로컬 전용 폴백. 여기까지 실패하면 **빈 결과**로 축퇴해 `failed`가 실제로 도달 가능해진다. */
async function safeLocalOnly(deps: FramePickerDeps): Promise<FrameCatalogResult> {
  try {
    return await deps.loadLocalOnly();
  } catch (err) {
    logger.warn("피커 로컬 전용 폴백도 실패 — 빈 목록으로 축퇴", { reason: describe(err) });
    return { frames: [], unavailable: [], source: "Fallback" };
  }
}

export async function runFramePickerLoad(deps: FramePickerDeps): Promise<void> {
  deps.apply({ phase: "loading", frames: [], notice: "", selectedId: null });

  // 지역 사본 — `apply`가 비동기로 반영돼도 `finally`가 옳은 값을 읽는다.
  let frames: readonly FrameTemplate[] = [];
  /** 공용 목록을 받지 못했는가(취소·상한·네트워크). 빈 결과의 **사유 문구**를 가른다. */
  let interrupted = false;

  const controller = new AbortController();
  deps.registerAbort(() => controller.abort());
  // 상한을 붙이는 이유: [취소]가 상시 있어도 서버 무응답에서 100초 스피너를 보이는 것보다
  // 30/60초에 로컬 목록으로 마감하는 편이 `FrameSelect`와 일관된다(설계 이탈 ④).
  const deadline = deps.createDeadline(() => controller.abort());

  try {
    deadline.arm();

    let result: FrameCatalogResult;
    try {
      result = await deps.loadPublic({ signal: controller.signal });
    } catch (err) {
      if (deps.isStale()) return;
      interrupted = true;
      logger.warn("피커 공용 목록 대기 중단 — 로컬 전용 폴백", {
        reason: err instanceof FrameLoadCancelledError ? "cancelled" : describe(err),
      });
      result = await safeLocalOnly(deps);
    }
    if (deps.isStale()) return;

    const merged = [...result.frames];
    const userId = deps.currentUserId();
    if (userId !== null) {
      // 개인 프레임 실패가 공용 목록을 무너뜨리지 않게 개별 방어.
      try {
        merged.push(...(await deps.loadPersonal(userId)));
      } catch (err) {
        logger.warn("피커 개인 프레임 로드 실패(공용 목록은 유지)", { reason: describe(err) });
      }
    }
    if (deps.isStale()) return;

    frames = merged.filter(hasUsableImage);
    // ⚠️ 자동 선택하지 않는다 — 적용이 파괴적이라 오조작 시 편집 중인 작업이 날아간다(§7.4).
    deps.apply({ frames, selectedId: null });
  } finally {
    deadline.dispose();
    if (!deps.isStale()) {
      // ★ 무조건 확정 — `loading` 고착 방지의 구조적 장치다.
      // 빈 결과의 사유를 가른다: 정상 조회인데 후보가 없으면 "없음", 대기가 잘렸으면 "실패".
      const empty = frames.length === 0;
      deps.apply({
        phase: empty ? "failed" : "ready",
        notice: empty
          ? interrupted
            ? STRINGS.frameEditor.pickerFailed
            : STRINGS.frameEditor.pickerEmpty
          : "",
      });
    }
  }
}
