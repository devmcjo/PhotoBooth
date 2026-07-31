import { canEditFrame } from "@domain/frames/frameEditPolicy";
import { nextCopyName } from "@domain/frames/frameNaming";
import { editSessionSource, type FrameSessionSource } from "@domain/frames/frameSavePolicy";
import type { ImageSize } from "@domain/frames/types";
import type { UserRole } from "@domain/roles/userRole";
import { logger } from "@adapters/storage/logStore";
import type { FrameEditorIntent } from "@shell/frameEditorIntent";
import { STRINGS } from "@ui/strings";
import type { FrameEditorAction, FrameEditorInit } from "./frameEditorState";

/**
 * 편집기 진입 준비 — [선택 편집] 세션의 이미지·슬롯·fork 이름 제안 (설계 §9.3 · §13 · §16.2)
 *
 * ⚠️ **재인코딩하지 않는다.** `loadFrameImageFromUrl`을 쓰면 장변 4000 축소가 적용돼
 *    `frame.slots`의 좌표계와 어긋나 **기존 슬롯이 전부 밀린다.** Windows `LoadForEdit`도
 *    `LoadImage`를 경유하지 않고 파일을 그대로 읽는다(같은 이유).
 * ⚠️ React·브라우저 전역을 참조하지 않는다 — fetch·디코드는 전부 주입이다.
 */

/**
 * 권한 밖 프레임의 편집 요청을 **신규 생성으로 강등**한다(3차 게이트의 2차).
 * 타인·번들 프레임의 이미지를 애초에 읽지 않는 것이 목적이다.
 */
export function resolveEntryIntent(
  intent: FrameEditorIntent,
  role: UserRole | null,
  userId: string | null,
): { readonly intent: FrameEditorIntent; readonly blocked: boolean } {
  if (intent.kind !== "edit") return { intent, blocked: false };
  if (canEditFrame(intent.frame, role, userId)) return { intent, blocked: false };
  logger.warn("편집 권한이 없는 프레임 진입 — 신규 생성으로 강등", {
    frameId: intent.frame.id,
    role: role ?? "guest",
  });
  return { intent: { kind: "new" }, blocked: true };
}

/** 첫 렌더에 쓰는 초기값. 편집 진입은 비동기 준비가 있으므로 `busy: true`로 시작한다. */
export function frameEditorInitFor(intent: FrameEditorIntent): FrameEditorInit {
  if (intent.kind !== "edit") {
    return { sessionSource: "New", sourceName: "", busy: false };
  }
  return {
    sessionSource: editSessionSource(intent.frame),
    sourceName: intent.frame.name,
    busy: true,
  };
}

export interface EditorEntryDeps {
  /** 저장 스코프의 기존 이름(fork 이름 제안용). 실패는 빈 배열. */
  scopeNames(): Promise<readonly string[]>;
  /** 사본 이름 8자 접미 생성기(도메인은 난수를 만들지 않는다 — 01 §8). */
  uniqueSuffix(): string;
  /** 원본 바이트를 **그대로** 읽는다(재인코딩 금지). 실패는 `null`. */
  fetchBytes(url: string): Promise<Blob | null>;
  /** 메타에 크기가 없을 때만 부른다. 실패는 `null`. */
  probeSize(blob: Blob): Promise<ImageSize | null>;
  dispatch(action: FrameEditorAction): void;
  /** 이 진입이 아직 최신인가(화면 이탈·재진입 판정). */
  isStale(): boolean;
}

function describe(err: unknown): string {
  return err instanceof Error ? err.message : String(err);
}

/** fork 세션의 이름 제안(`{원본} 사본`). `EditOwnLocal`은 원본 이름 그대로 덮어쓴다. */
async function proposeName(
  deps: EditorEntryDeps,
  source: FrameSessionSource,
  originalName: string,
): Promise<string> {
  if (source !== "ForkFromCatalog") return originalName;
  let names: readonly string[] = [];
  try {
    names = await deps.scopeNames();
  } catch (err) {
    logger.warn("사본 이름 제안용 이름 열거 실패(빈 집합으로 진행)", { reason: describe(err) });
  }
  return nextCopyName(originalName, names, deps.uniqueSuffix);
}

/**
 * [선택 편집] 진입 절차. `kind:"new"`에서는 아무것도 하지 않는다(빈 편집기가 이미 정답이다).
 *
 * 실패해도 폼은 열린다 — 이미지가 없으면 저장 검증 ③이 막으므로 반쪽 저장이 생기지 않는다.
 */
export async function runEditorEntry(
  deps: EditorEntryDeps,
  intent: FrameEditorIntent,
): Promise<void> {
  if (intent.kind !== "edit") return;

  const frame = intent.frame;
  const source = editSessionSource(frame);
  deps.dispatch({ type: "entryStarted" });

  try {
    const name = await proposeName(deps, source, frame.name);
    if (deps.isStale()) return;

    const bytes = await deps.fetchBytes(frame.imageUrl);
    if (deps.isStale()) return;
    if (bytes === null) {
      deps.dispatch({ type: "entryFailed", status: STRINGS.frameEditor.editImageMissing });
      return;
    }

    let size = frame.imageSize;
    if (size.width <= 0 || size.height <= 0) {
      const probed = await deps.probeSize(bytes);
      if (deps.isStale()) return;
      if (probed === null) {
        deps.dispatch({ type: "entryFailed", status: STRINGS.frameEditor.editImageMissing });
        return;
      }
      size = probed;
    }

    deps.dispatch({
      type: "editSessionReady",
      name,
      png: bytes,
      imageSize: size,
      slots: frame.slots,
    });
  } catch (err) {
    if (deps.isStale()) return;
    logger.error("편집 진입 준비 실패", { frameId: frame.id, reason: describe(err) });
    deps.dispatch({ type: "entryFailed", status: STRINGS.frameEditor.editImageMissing });
  }
}
