import { useEffect, useRef, type CSSProperties } from "react";
import {
  frameToCanvas,
  isValidTransform,
  type EditorTransform,
} from "@domain/frames/editorTransform";
import {
  MAX_SCALE_PERCENT,
  MIN_SCALE_PERCENT,
} from "@domain/frames/frameSavePolicy";
import { SLOT_ASPECTS, slotAspectToLabel } from "@domain/frames/slotAspect";
import { MAX_SLOTS, MIN_SLOTS } from "@domain/frames/slotLayout";
import type { FrameTemplate, Slot } from "@domain/frames/types";
import { createFrameThumbnail } from "@adapters/frames/frameThumbnails";
import { logger } from "@adapters/storage/logStore";
import {
  useFrameEditor,
  type FrameEditorViewModel,
} from "@screens/frameEditor/useFrameEditor";
import { shellStore } from "@shell/shellStore";
import { Button, Spinner } from "@ui/components";
import { OverlayDialog } from "@ui/components/OverlayDialog";
import { ChoiceGroup, TextField } from "@ui/components/fields";
import { formatCount, STRINGS } from "@ui/strings";
import screens from "./screens.module.css";
import styles from "./frameEditor.module.css";

/**
 * `FrameEditor` — 프레임 생성·편집 (03 §11 · §15.4 · §15.7)
 *
 * ⚠️ **판정을 여기에 넣지 않는다.** 이 저장소에는 jsdom이 없어 컴포넌트가 테스트되지 않는다 —
 *    순서·권한·기하는 전부 `screens/frameEditor/*`와 `domain/frames/*`가 소유한다(15 §3.1).
 * ⚠️ 두 오버레이는 **화면 로컬**이다. `pushModal`을 부르지 않는다(03 §790).
 * ⚠️ 표시·드래그·클램프가 **하나의 `EditorTransform`**(`view.transform`)을 쓴다 — 여기서 좌표를
 *    따로 계산하면 저장한 슬롯과 합성 결과가 어긋난다(Windows B3 버그의 재발).
 */

const SLOT_COUNT_OPTIONS = Array.from({ length: MAX_SLOTS - MIN_SLOTS + 1 }, (_v, i) => {
  const value = MIN_SLOTS + i;
  return { value, label: `${value}` };
});

const ASPECT_OPTIONS = SLOT_ASPECTS.map((aspect) => ({
  value: aspect,
  label: slotAspectToLabel(aspect),
}));

/** 진입 포커스 대상. `Button`은 ref를 전달하지 않으므로 id로 찾는다. */
const PICKER_CANCEL_ID = "frame-picker-cancel";
const REGISTER_CANCEL_ID = "frame-register-cancel";

function slotBoxStyle(t: EditorTransform, slot: Slot): CSSProperties {
  const point = frameToCanvas(t, slot.x, slot.y);
  return {
    left: `${point.x}px`,
    top: `${point.y}px`,
    width: `${slot.width * t.scale}px`,
    height: `${slot.height * t.scale}px`,
  };
}

function FrameEditorStage({ view }: { readonly view: FrameEditorViewModel }) {
  const t = view.transform;
  const ready = isValidTransform(t) && view.previewUrl.length > 0;

  return (
    <div className={styles.stage} ref={view.stageRef}>
      {ready && (
        <img
          className={styles.frameImage}
          src={view.previewUrl}
          alt=""
          style={{
            left: `${t.originX}px`,
            top: `${t.originY}px`,
            width: `${t.displayWidth}px`,
            height: `${t.displayHeight}px`,
          }}
        />
      )}
      {ready &&
        view.slots.map((slot, index) => (
          <button
            key={slot.index}
            type="button"
            className={styles.slot}
            style={slotBoxStyle(t, slot)}
            aria-label={`${formatCount(STRINGS.frameEditor.slotAriaLabel, index + 1)} (${slot.x}, ${slot.y})`}
            disabled={view.busy}
            onPointerDown={(event) => view.onSlotPointerDown(index, event)}
            onPointerMove={view.onSlotPointerMove}
            // ⚠️ 셋 다 구독한다 — 하나라도 빠지면 드래그가 고착된다.
            onPointerUp={view.onSlotPointerEnd}
            onPointerCancel={view.onSlotPointerEnd}
            onLostPointerCapture={view.onSlotPointerEnd}
            onKeyDown={(event) => view.onSlotKeyDown(index, event)}
          />
        ))}
      {!view.hasImage && (
        <p className={styles.stageEmpty}>{STRINGS.frameEditor.noImage}</p>
      )}
    </div>
  );
}

/** `ImageBitmap`은 `<img>`로 못 그리므로 canvas에 옮긴다(`FrameSelectView.FrameThumb` 선례). */
function PickerThumb({ src, name }: { readonly src: string; readonly name: string }) {
  const canvasRef = useRef<HTMLCanvasElement>(null);

  useEffect(() => {
    let cancelled = false;
    let bitmap: ImageBitmap | null = null;

    async function draw(): Promise<void> {
      try {
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
        logger.warn("피커 썸네일 그리기 실패", {
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

  return <canvas ref={canvasRef} className={styles.pickerThumb} aria-hidden="true" />;
}

function PickerCard({
  frame,
  selected,
  onSelect,
}: {
  readonly frame: FrameTemplate;
  readonly selected: boolean;
  readonly onSelect: () => void;
}) {
  return (
    <button
      type="button"
      className={[styles.pickerCard, selected ? styles.pickerCardSelected : ""]
        .filter(Boolean)
        .join(" ")}
      aria-pressed={selected}
      onClick={onSelect}
    >
      <PickerThumb src={frame.imageUrl} name={frame.name} />
      <span className={styles.pickerName}>{frame.name}</span>
    </button>
  );
}

function FramePickerOverlay({ view }: { readonly view: FrameEditorViewModel }) {
  const picker = view.picker;
  return (
    <OverlayDialog
      title={STRINGS.frameEditor.pickerTitle}
      onCancel={view.closePicker}
      initialFocusId={PICKER_CANCEL_ID}
      actions={
        <>
          <Button id={PICKER_CANCEL_ID} onClick={view.closePicker}>
            {STRINGS.common.cancel}
          </Button>
          <Button
            variant="primary"
            // 자동 선택이 없으므로 선택 전에는 비활성이다(오조작 시 작업 소실 방지).
            disabled={picker.selectedId === null || view.busy}
            onClick={view.applyPicked}
          >
            {STRINGS.frameEditor.pickerApply}
          </Button>
        </>
      }
    >
      {picker.phase === "loading" && (
        <div className={styles.pickerStatus}>
          <Spinner />
        </div>
      )}
      {picker.phase === "failed" && (
        <p className={styles.pickerStatus} role="alert">
          {picker.notice}
        </p>
      )}
      {picker.phase === "ready" && (
        <div className={styles.pickerGrid}>
          {picker.frames.map((frame) => (
            <PickerCard
              key={frame.id}
              frame={frame}
              selected={frame.id === picker.selectedId}
              onSelect={() => view.selectPickerFrame(frame.id)}
            />
          ))}
        </div>
      )}
    </OverlayDialog>
  );
}

function ServerRegisterOverlay({ view }: { readonly view: FrameEditorViewModel }) {
  return (
    <OverlayDialog
      title={STRINGS.frameEditor.registerTitle}
      onCancel={view.cancelRegister}
      initialFocusId={REGISTER_CANCEL_ID}
      actions={
        <>
          <Button id={REGISTER_CANCEL_ID} onClick={view.cancelRegister} disabled={view.busy}>
            {STRINGS.common.cancel}
          </Button>
          <Button variant="primary" onClick={view.confirmRegisterSave} disabled={view.busy}>
            {STRINGS.common.save}
          </Button>
        </>
      }
    >
      <label className={styles.registerOption}>
        <input
          type="checkbox"
          checked={view.registerToServer}
          onChange={(event) => view.toggleRegisterToServer(event.target.checked)}
        />
        {STRINGS.frameEditor.registerCheckbox}
      </label>
      {/* ⚠️ 체크 상태와 무관한 **고정 문구**다(컨버터·분기 없음 — 03 §11.4). */}
      <p className={styles.muted}>{STRINGS.frameEditor.registerCaption}</p>
      {view.registerToServer && view.showsUnderscoreWarning && (
        <p className={[styles.muted, styles.warning].join(" ")} role="alert">
          {STRINGS.frames.nameUnderscoreRejected}
        </p>
      )}
    </OverlayDialog>
  );
}

/** 권한 밖 역할에게는 **본문을 렌더하지 않는다**(렌더 가드 — M10 ①). */
function NotAllowedCard() {
  return (
    <main className={screens.screen}>
      <h1 className={screens.title}>{STRINGS.frameEditor.titleNew}</h1>
      <p className={screens.note} role="alert">
        {STRINGS.frameEditor.noPermission}
      </p>
      <Button onClick={() => shellStore.getState().go("FrameSelect")}>
        {STRINGS.frameEditor.backToFrameSelect}
      </Button>
    </main>
  );
}

export function FrameEditorView() {
  const view = useFrameEditor();
  if (!view.allowed) return <NotAllowedCard />;

  const fileInputId = "frame-editor-file";

  return (
    <main className={screens.screen}>
      <h1 className={screens.title}>{view.title}</h1>

      {/* 정책 배너는 **편집 세션 전용**이다(신규 생성 세션에는 문장이 거짓이 된다). */}
      {view.showsBanner && (
        <p className={styles.banner} role="note">
          {STRINGS.frames.localOnlyBanner}
        </p>
      )}

      <div className={styles.layout}>
        <FrameEditorStage view={view} />

        <aside className={styles.panel}>
          <div className={styles.panelRow}>
            <input
              id={fileInputId}
              className={styles.hiddenInput}
              type="file"
              accept="image/png,image/jpeg"
              onChange={(event) => {
                const file = event.target.files?.[0];
                // 같은 파일을 다시 골라도 change가 나도록 값을 비운다.
                event.target.value = "";
                if (file !== undefined) view.chooseFile(file);
              }}
            />
            <div className={screens.actions}>
              <Button
                disabled={view.busy}
                onClick={() => document.getElementById(fileInputId)?.click()}
              >
                {STRINGS.frameEditor.loadImage}
              </Button>
              {/* 생성 모드 전용(03 §11.5). */}
              {view.canPick && (
                <Button disabled={view.busy} onClick={view.openPicker}>
                  {STRINGS.frameEditor.pickExisting}
                </Button>
              )}
            </div>
          </div>

          <div className={styles.panelRow}>
            <span className={styles.panelLabel}>{STRINGS.frameEditor.slotCount}</span>
            {/* 값 기반 선택(인덱스 금지 — it7 B9와 같은 취지). */}
            <ChoiceGroup
              label={STRINGS.frameEditor.slotCount}
              value={view.slotCount}
              options={SLOT_COUNT_OPTIONS}
              disabled={view.busy || !view.hasImage}
              onChange={view.setSlotCount}
            />
          </div>

          <div className={styles.panelRow}>
            <span className={styles.panelLabel}>{STRINGS.frameEditor.slotAspect}</span>
            <ChoiceGroup
              label={STRINGS.frameEditor.slotAspect}
              value={view.aspect}
              options={ASPECT_OPTIONS}
              disabled={view.busy || !view.hasImage}
              onChange={view.setAspect}
            />
          </div>

          <div className={styles.panelRow}>
            <span className={styles.panelLabel}>
              {STRINGS.frameEditor.slotScale} {view.scalePercent}%
            </span>
            <input
              className={styles.range}
              type="range"
              min={MIN_SCALE_PERCENT}
              max={MAX_SCALE_PERCENT}
              step={1}
              value={view.scalePercent}
              disabled={view.busy || !view.hasImage}
              aria-label={STRINGS.frameEditor.slotScale}
              aria-valuetext={`${view.scalePercent}%`}
              onChange={(event) => view.setScale(Number(event.target.value))}
            />
          </div>

          {/* 이름 입력 **위**에 원본 안내를 둔다(03 §11.5). */}
          {view.pickedSourceNotice.length > 0 && (
            <p className={styles.muted}>{view.pickedSourceNotice}</p>
          )}

          <div className={styles.panelRow}>
            <span className={styles.panelLabel}>{STRINGS.frameEditor.nameLabel}</span>
            <TextField
              label={STRINGS.frameEditor.nameLabel}
              value={view.name}
              disabled={view.busy}
              placeholder={STRINGS.frameEditor.namePlaceholder}
              onChange={view.setName}
            />
          </div>

          <p className={styles.scopeNotice}>
            {view.scopeNotice}
            {view.showsUnderscoreWarning && (
              <span className={styles.warning}> {STRINGS.frames.underscoreWarning}</span>
            )}
          </p>

          <div className={screens.actions}>
            <Button onClick={view.cancel} disabled={view.busy}>
              {STRINGS.common.cancel}
            </Button>
            <Button variant="primary" onClick={view.requestSave} disabled={view.busy}>
              {STRINGS.common.save}
            </Button>
          </div>
        </aside>
      </div>

      {view.status.length > 0 && (
        <p className={screens.note} role="alert">
          {view.status}
        </p>
      )}
      {view.busy && (
        <p className={screens.note} aria-live="polite">
          {STRINGS.common.loading}
        </p>
      )}

      {/* ① ②는 상호배타다(03 §790) — 단일 필드라 동시에 뜨는 상태가 표현 불가능하다. */}
      {view.overlay === "picker" && <FramePickerOverlay view={view} />}
      {view.overlay === "serverRegister" && <ServerRegisterOverlay view={view} />}
    </main>
  );
}
