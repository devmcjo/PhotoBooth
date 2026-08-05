import { useCallback, useEffect, useRef, useState } from "react";
import { getSelectedCuts } from "@domain/capture/captureSession";
import { availableFilters, type FilterKind } from "@domain/filters/filterParams";
import { compose } from "@adapters/compose/compositor";
import { logger } from "@adapters/storage/logStore";
import { currentWorkspace } from "@shell/captureSessionController";
import { sessionStore, useSessionStore } from "@shell/sessionStore";
import { useSettingsStore } from "@shell/settingsStore";
import { shellStore } from "@shell/shellStore";
import { STRINGS } from "@ui/strings";

/**
 * `Result` 합성 배선 — 03 §8 · 04 §5
 *
 * 진입 즉시 합성하고, 필터를 바꾸면 **전체 재합성**한다(부분 갱신은 슬롯 경계에 이음매를 만든다).
 *
 * ⚠️ `blob:` URL은 **교체할 때 이전 것을 revoke**한다. 필터를 여러 번 바꾸면 URL이 누적돼
 *    모바일에서 메모리가 샌다.
 * ⚠️ 프레임은 촬영 시작 전에 고정됐다(M11). 여기서 바꿀 수 없다.
 */

export interface ResultCompose {
  readonly imageUrl: string | null;
  readonly composing: boolean;
  readonly error: string | null;
  readonly filter: FilterKind;
  readonly filters: readonly FilterKind[];
  readonly elapsedMs: number | null;
  setFilter(filter: FilterKind): void;
  /** 합성 결과 Blob(로컬 보관·업로드가 쓴다 — Step 10·11). */
  currentBlob(): Blob | null;
}

export function useResultCompose(): ResultCompose {
  const session = useSessionStore((s) => s.session);
  const selectedFilter = useSessionStore((s) => s.selectedFilter);
  const values = useSettingsStore((s) => s.values);

  const [imageUrl, setImageUrl] = useState<string | null>(null);
  const [composing, setComposing] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [elapsedMs, setElapsedMs] = useState<number | null>(null);
  const blobRef = useRef<Blob | null>(null);
  const urlRef = useRef<string | null>(null);

  const filters = availableFilters(values);

  const replaceUrl = useCallback((next: string | null) => {
    if (urlRef.current !== null) URL.revokeObjectURL(urlRef.current);
    urlRef.current = next;
    setImageUrl(next);
  }, []);

  useEffect(() => {
    let cancelled = false;
    const frame = session.frame;
    const workspace = currentWorkspace();

    async function run(): Promise<void> {
      if (frame === null || workspace === null) {
        setError("합성할 세션이 없습니다.");
        setComposing(false);
        return;
      }

      setComposing(true);
      setError(null);
      try {
        // OPFS의 컷 JPEG를 슬롯 순서대로 읽는다(선택 순서 = 슬롯 순서 — M12).
        const cutFiles: Blob[] = [];
        for (const cut of getSelectedCuts(session)) {
          const file = await workspace.readFile(cut.fileName);
          if (file === null) throw new Error(`컷 파일을 읽을 수 없습니다: ${cut.fileName}`);
          cutFiles.push(file);
        }

        const result = await compose({
          frameImageUrl: frame.imageUrl,
          slots: frame.slots,
          cuts: cutFiles,
          filter: selectedFilter,
          format: values.OutputFormat,
        });
        if (cancelled) return;

        blobRef.current = result.blob;
        // `Qr` 화면이 업로드할 수 있도록 세션 컨텍스트로 인계한다(ref는 언마운트와 함께 사라진다).
        // 합성이 **성공했을 때만** 올린다 — 실패·취소 세션이 이전 결과를 올리면 안 된다.
        sessionStore.getState().setFinalImage({ blob: result.blob, format: values.OutputFormat });
        replaceUrl(URL.createObjectURL(result.blob));
        setElapsedMs(result.elapsedMs);
      } catch (err) {
        if (cancelled) return;
        const reason = err instanceof Error ? err.message : String(err);
        logger.error("합성 실패", { reason, filter: selectedFilter });
        setError(reason);
        shellStore.getState().toast("error", STRINGS.error.temporary);
      } finally {
        if (!cancelled) setComposing(false);
      }
    }

    void run();
    return () => {
      cancelled = true;
    };
  }, [session, selectedFilter, values.OutputFormat, replaceUrl]);

  // 화면을 떠날 때 마지막 URL을 해제한다.
  useEffect(
    () => () => {
      if (urlRef.current !== null) URL.revokeObjectURL(urlRef.current);
      urlRef.current = null;
    },
    [],
  );

  return {
    imageUrl,
    composing,
    error,
    filter: selectedFilter,
    filters,
    elapsedMs,
    setFilter: (filter) => sessionStore.getState().setFilter(filter),
    currentBlob: () => blobRef.current,
  };
}
