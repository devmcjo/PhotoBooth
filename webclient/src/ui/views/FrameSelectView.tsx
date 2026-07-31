import { useEffect, useRef } from "react";
import { slotAspectRatio, type FrameTemplate } from "@domain/frames/types";
import type { UnavailableFrame } from "@adapters/frames/frameCatalog";
import { createFrameThumbnail } from "@adapters/frames/frameThumbnails";
import { logger } from "@adapters/storage/logStore";
import { useFrameSelect, type FrameSelectViewModel } from "@screens/frameSelect/useFrameSelect";
import { shellStore } from "@shell/shellStore";
import { Button, Spinner } from "@ui/components";
import { formatCount, STRINGS } from "@ui/strings";
import screens from "./screens.module.css";
import styles from "./frameSelect.module.css";

/**
 * `FrameSelect` — 프레임 확정 (03 §4 · §4.1 · §15.5)
 *
 * ⚠️ **판정을 여기에 넣지 않는다.** 이 저장소에는 jsdom이 없어 컴포넌트가 테스트되지 않는다 —
 *    국면·권한·순서는 전부 `screens/frameSelect/*`가 소유하고 여기서는 렌더만 한다(15 §3.1).
 * ⚠️ 차단은 **2중**이다: scrim(렌더 가드) + 각 액션 함수 첫 줄의 상태 가드(M10).
 * ⚠️ 삭제 확인은 **화면 로컬 오버레이**다 — `pushModal`을 부르지 않는다(`03 §790`).
 *    Step 15에서 셸 모달 식별자 자체를 지웠으므로(FR-8) 이 원칙은 영구다.
 */

/** `ImageBitmap`은 `<img>`로 못 그리므로 canvas에 옮긴다(`CutThumbnail` 선례). */
function FrameThumb({ src, name }: { readonly src: string; readonly name: string }) {
  const canvasRef = useRef<HTMLCanvasElement>(null);

  useEffect(() => {
    let cancelled = false;
    let bitmap: ImageBitmap | null = null;

    async function draw(): Promise<void> {
      try {
        // 캐시된 프레임은 `blob:`(OPFS 유래), 번들은 상대 경로다 — 둘 다 same-origin이다.
        const response = await fetch(src);
        if (!response.ok) return;
        const thumbnail = await createFrameThumbnail(await response.blob());
        if (thumbnail === null) return;
        if (cancelled) {
          thumbnail.close();
          return;
        }
        bitmap = thumbnail;
        const canvas = canvasRef.current;
        if (canvas === null) return;
        canvas.width = thumbnail.width;
        canvas.height = thumbnail.height;
        canvas.getContext("2d")?.drawImage(thumbnail, 0, 0);
      } catch (err) {
        logger.warn("프레임 썸네일 그리기 실패", {
          name,
          reason: err instanceof Error ? err.message : String(err),
        });
      }
    }

    void draw();
    return () => {
      cancelled = true;
      // ⚠️ ImageBitmap은 GC 대상이 아니다(WR8) — 언마운트에서 반드시 닫는다.
      bitmap?.close();
      bitmap = null;
    };
  }, [src, name]);

  return <canvas ref={canvasRef} className={styles.thumb} aria-hidden="true" />;
}

function FrameCard({
  frame,
  selected,
  disabled,
  showDelete,
  onSelect,
  onDelete,
}: {
  readonly frame: FrameTemplate;
  readonly selected: boolean;
  readonly disabled: boolean;
  readonly showDelete: boolean;
  readonly onSelect: () => void;
  readonly onDelete: () => void;
}) {
  return (
    <div className={styles.card}>
      <button
        type="button"
        className={[styles.cardButton, selected ? styles.cardSelected : ""]
          .filter(Boolean)
          .join(" ")}
        aria-pressed={selected}
        disabled={disabled}
        onClick={onSelect}
      >
        <FrameThumb src={frame.imageUrl} name={frame.name} />
        <span className={styles.cardName}>{frame.name}</span>
        <span className={styles.cardCaption}>슬롯 {frame.slots.length}개</span>
      </button>
      {/* 카드 안의 버튼 중첩을 피해 **형제**로 둔다. */}
      {showDelete && (
        <button
          type="button"
          className={styles.deleteButton}
          aria-label={`${frame.name} 삭제`}
          onClick={onDelete}
        >
          ✕
        </button>
      )}
    </div>
  );
}

/** 이미지를 가져오지 못한 서버 프레임. 카드는 보이되 **선택 불가**다(설계 이탈 ③). */
function UnavailableCard({ entry }: { readonly entry: UnavailableFrame }) {
  return (
    <div className={styles.unavailableCard} aria-disabled="true">
      <img className={styles.unavailableThumb} src={entry.imageUrl} alt="" />
      <span className={styles.cardName}>{entry.name}</span>
      <span className={styles.cardCaption}>{STRINGS.frames.unavailableImage}</span>
    </div>
  );
}

function FrameLoadingOverlay({
  message,
  onSkip,
}: {
  readonly message: string;
  readonly onSkip: () => void;
}) {
  return (
    <div className={styles.scrim} role="status">
      <Spinner label={message} />
      <p className={styles.overlayMessage} aria-live="polite">
        {message}
      </p>
      <Button onClick={onSkip}>{STRINGS.frames.skipWait}</Button>
    </div>
  );
}

function FrameFailedCard({
  notice,
  onRetry,
  onHome,
}: {
  readonly notice: string;
  readonly onRetry: () => void;
  readonly onHome: () => void;
}) {
  return (
    <div className={styles.scrim} role="alert">
      <p className={styles.overlayNotice}>{notice}</p>
      <div className={screens.actions}>
        <Button onClick={onHome}>{STRINGS.frames.goHome}</Button>
        <Button variant="primary" onClick={onRetry}>
          {STRINGS.common.retry}
        </Button>
      </div>
    </div>
  );
}

/** 진입 포커스 대상. `Button`은 ref를 전달하지 않으므로 id로 찾는다. */
const DELETE_CANCEL_ID = "frame-delete-cancel";

function FrameDeleteOverlay({ view }: { readonly view: FrameSelectViewModel }) {
  const frame = view.deleteTarget;
  const cancelDelete = view.cancelDelete;

  useEffect(() => {
    // 진입 시 [취소]에 포커스 — 파괴적 액션이 기본 포커스를 갖지 않게 한다.
    document.getElementById(DELETE_CANCEL_ID)?.focus();
    const onKeyDown = (event: KeyboardEvent): void => {
      if (event.key === "Escape") {
        event.preventDefault();
        cancelDelete();
      }
    };
    document.addEventListener("keydown", onKeyDown);
    return () => document.removeEventListener("keydown", onKeyDown);
  }, [cancelDelete]);

  if (frame === null) return null;

  return (
    <div className={styles.deleteScrim}>
      <div
        className={styles.deleteDialog}
        role="dialog"
        aria-modal="true"
        aria-label={STRINGS.frames.deleteConfirmTitle}
      >
        <h2 className={styles.deleteTitle}>{STRINGS.frames.deleteConfirmTitle}</h2>
        <p className={styles.deleteBody}>
          {formatCount(STRINGS.frames.deleteConfirmBody, frame.name)}
        </p>
        {view.isPower && (
          <label className={styles.deleteOption}>
            <input
              type="checkbox"
              checked={view.deleteAlsoServer}
              onChange={(event) => view.toggleDeleteServer(event.target.checked)}
            />
            {STRINGS.frames.deleteAlsoServer}
          </label>
        )}
        <div className={styles.deleteActions}>
          <Button id={DELETE_CANCEL_ID} onClick={view.cancelDelete} disabled={view.deleteBusy}>
            {STRINGS.common.cancel}
          </Button>
          <Button variant="danger" onClick={view.confirmDelete} disabled={view.deleteBusy}>
            {STRINGS.common.delete}
          </Button>
        </div>
      </div>
    </div>
  );
}

export function FrameSelectView() {
  const view = useFrameSelect();
  const selected = view.selected;
  const aspect = selected?.slots[0];
  const aspectLabel = aspect === undefined ? null : slotAspectRatio(aspect).toFixed(2);

  return (
    <main className={screens.screen}>
      <h1 className={screens.title}>프레임 선택</h1>

      <div className={styles.stage}>
        <div className={screens.frameGrid}>
          {view.frames.map((frame) => (
            <FrameCard
              key={frame.id}
              frame={frame}
              selected={frame.id === view.selectedId}
              disabled={!view.interactive}
              // 렌더 가드: 국면·권한·출처가 모두 통과할 때만 ✕가 존재한다.
              showDelete={view.interactive && view.canDelete(frame)}
              onSelect={() => view.select(frame.id)}
              onDelete={() => view.requestDelete(frame)}
            />
          ))}
          {view.unavailable.map((entry) => (
            <UnavailableCard key={entry.id} entry={entry} />
          ))}
        </div>

        {view.phase === "Loading" && (
          <FrameLoadingOverlay message={view.loadingMessage} onSkip={view.skipWait} />
        )}
        {view.phase === "Failed" && (
          <FrameFailedCard
            notice={view.notice}
            onRetry={view.retry}
            onHome={() => void shellStore.getState().returnHome("프레임 준비 실패에서 홈 선택")}
          />
        )}
      </div>

      {view.phase === "Degraded" && view.notice.length > 0 && (
        <>
          <p className={screens.note} role="alert">
            {view.notice}
          </p>
          <Button onClick={view.retry}>{STRINGS.common.retry}</Button>
        </>
      )}

      {view.deleteNotice.length > 0 && (
        <p className={screens.note} role="alert">
          {view.deleteNotice}
        </p>
      )}

      {selected !== null && (
        <p className={screens.note}>
          선택: {selected.name} (슬롯 {selected.slots.length}개
          {aspectLabel === null ? "" : ` · 비율 ${aspectLabel}`})
        </p>
      )}

      <div className={screens.actions}>
        <Button onClick={view.cancel}>{STRINGS.common.cancel}</Button>
        {view.canCreateFrame && (
          <Button disabled={!view.interactive} onClick={view.createFrame}>
            프레임 만들기
          </Button>
        )}
        {view.canEditSelected && (
          <Button disabled={!view.interactive} onClick={view.editSelected}>
            선택 편집
          </Button>
        )}
        <Button
          variant="primary"
          disabled={!view.interactive || selected === null}
          onClick={view.goNext}
        >
          {STRINGS.common.next}
        </Button>
      </div>

      {view.deleteTarget !== null && <FrameDeleteOverlay view={view} />}
    </main>
  );
}
