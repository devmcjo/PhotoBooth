import { useEffect, useMemo, useRef, useState } from "react";
import { slotAspectRatio } from "@domain/frames/types";
import {
  getSelectedCuts,
  isSelectionComplete,
  slotCount as slotCountOf,
  toggleSelection,
  canFullRetake,
  beginFullRetake,
} from "@domain/capture/captureSession";
import { getCameraService } from "@adapters/camera/cameraService";
import { requestWakeLock } from "@adapters/platform/wakeLock";
import { unlockAudio } from "@adapters/platform/shutterSound";
import { logger } from "@adapters/storage/logStore";
import { useSettingsStore } from "@shell/settingsStore";
import { sessionStore, useSessionStore, type CapturedCut } from "@shell/sessionStore";
import { shellStore } from "@shell/shellStore";
import { Button, Spinner } from "@ui/components";
import { STRINGS } from "@ui/strings";
import { CameraPreview } from "./CameraPreview";
import { useResultCompose } from "@screens/result/useResultCompose";
import { defaultResultNextDeps, runResultNext } from "@screens/result/resultNext";
import type { FilterKind } from "@domain/filters/filterParams";
import { useCaptureRunner } from "@screens/capture/useCaptureRunner";
import styles from "./screens.module.css";

/**
 * 촬영 흐름 화면 — Home · Guide · Capture · CutSelect · Result (03 §2·§5·§6·§7·§8)
 *
 * ⚠️ `FrameSelect`는 **여기 없다.** Step 14에서 본편(`ui/views/FrameSelectView.tsx`)으로 분리했다 —
 *    대기 4국면·삭제 오버레이·카탈로그 배선이 붙어 이 파일에 두기에는 너무 커졌다.
 */

// ─────────────────────────────────── Home ───────────────────────────────────

export function HomeView({ appName, subtitle }: { readonly appName: string; readonly subtitle: string }) {
  return (
    <main className={styles.screen}>
      <h1 className={styles.title}>{appName}</h1>
      <p className={styles.subtitle}>{subtitle}</p>
      <Button
        variant="primary"
        onClick={() => {
          // 게스트 직행 — 로그인 화면을 강제로 거치지 않는다(03 §2).
          sessionStore.getState().discardCaptureData();
          shellStore.getState().go("FrameSelect");
        }}
      >
        {STRINGS.home.start}
      </Button>
      <p className={styles.note}>촬영을 시작하면 카메라 사용 권한을 묻습니다.</p>
    </main>
  );
}

// ─────────────────────────────────── Guide ───────────────────────────────────

export function GuideView() {
  const session = useSessionStore((s) => s.session);
  const values = useSettingsStore((s) => s.values);
  const slots = slotCountOf(session);

  return (
    <main className={styles.screen}>
      <h1 className={styles.title}>촬영 안내</h1>
      <dl className={styles.guideList}>
        <dt>컷 수</dt>
        <dd>
          {session.cutCount}
          {/* 설정에 없는 숫자(7 등)가 뜨는 이유를 알린다(it17). */}
          {session.isAutoCutCount && <span className={styles.autoBadge}>자동</span>}
        </dd>
        <dt>컷당 카운트다운</dt>
        <dd>{values.CountdownSec}초</dd>
        <dt>슬롯 수</dt>
        <dd>{slots}개</dd>
        <dt>거울모드</dt>
        <dd>{values.MirrorMode ? "on" : "off"}</dd>
      </dl>

      <div className={styles.actions}>
        <Button onClick={() => void shellStore.getState().returnHome("가이드 취소")}>
          {STRINGS.common.cancel}
        </Button>
        <Button
          variant="primary"
          disabled={session.frame === null}
          onClick={() => {
            // [촬영 시작] 제스처에서 오디오 unlock·WakeLock을 다시 확보한다(03 §5).
            void unlockAudio();
            void requestWakeLock();
            shellStore.getState().go("Capture");
          }}
        >
          촬영 시작
        </Button>
      </div>
    </main>
  );
}

// ────────────────────────────────── Capture ──────────────────────────────────

export function CaptureView() {
  const session = useSessionStore((s) => s.session);
  const runner = useCaptureRunner();

  return (
    <main className={styles.screen}>
      <div className={styles.captureStage}>
        <p className={styles.progress} aria-live="polite">
          {runner.capturedCount} / {session.cutCount}
        </p>

        <CameraPreview
          overlay={
            <>
              {runner.flashing && <div className={styles.flash} aria-hidden="true" />}
              {runner.countdown > 0 && (
                <div className={styles.countdown} aria-live="assertive">
                  {runner.countdown}
                </div>
              )}
            </>
          }
        />
      </div>

      <div className={styles.actions}>
        <Button onClick={() => void shellStore.getState().returnHome("촬영 취소")}>
          {STRINGS.common.cancel}
        </Button>
        <Button
          variant="primary"
          disabled={!runner.canShootNow}
          onClick={() => runner.shootNow()}
        >
          바로 촬영
        </Button>
      </div>
    </main>
  );
}

// ───────────────────────────────── CutSelect ─────────────────────────────────

export function CutSelectView() {
  const session = useSessionStore((s) => s.session);
  const retakeEnabled = useSettingsStore((s) => s.values.RetakeEnabled);
  const retakeLimit = useSettingsStore((s) => s.values.RetakeLimit);
  const slots = slotCountOf(session);

  // 대표 슬롯(첫 슬롯)의 종횡비로 썸네일 비율을 맞춘다(03 §7).
  const aspect = useMemo(() => {
    const first = session.frame?.slots[0];
    return first === undefined ? 0.75 : slotAspectRatio(first);
  }, [session.frame]);

  const canRetake = retakeEnabled && canFullRetake(session, retakeLimit);

  return (
    <main className={styles.screen}>
      <h1 className={styles.title}>
        컷 선택 ({session.selection.length}/{slots})
      </h1>

      <div className={styles.cutGrid}>
        {session.cuts.map((cut, index) => {
          const order = session.selection.indexOf(index);
          return (
            <button
              key={cut.fileName}
              type="button"
              className={[styles.cutCard, order >= 0 ? styles.cutCardSelected : ""]
                .filter(Boolean)
                .join(" ")}
              style={{ aspectRatio: `${aspect}` }}
              aria-pressed={order >= 0}
              aria-label={`컷 ${index + 1}${order >= 0 ? ` (${order + 1}번째 선택)` : ""}`}
              onClick={() => sessionStore.getState().setSession(toggleSelection(session, index))}
            >
              <CutThumbnail cut={cut} />
              {/* 해제하면 이후 번호가 자동으로 재계산된다(선택 배열 인덱스가 곧 순서다). */}
              {order >= 0 && <span className={styles.cutOrder}>{order + 1}</span>}
            </button>
          );
        })}
      </div>

      <div className={styles.actions}>
        {retakeEnabled && (
          <Button
            disabled={!canRetake}
            onClick={() => {
              if (!canRetake) return; // 커맨드 가드(버튼 비활성과 별개 — M10)
              sessionStore.getState().setSession(beginFullRetake(session));
              // 컷 수를 **재해석하지 않는다**(it17) — 세션의 값을 그대로 쓴다.
              shellStore.getState().go("Guide");
            }}
          >
            재촬영
          </Button>
        )}
        <Button onClick={() => void shellStore.getState().returnHome("컷 선택 취소")}>
          {STRINGS.common.cancel}
        </Button>
        <Button
          variant="primary"
          disabled={!isSelectionComplete(session)}
          onClick={() => {
            logger.info("컷 선택 완료", {
              selected: getSelectedCuts(session).map((c) => c.fileName),
            });
            shellStore.getState().go("Result");
          }}
        >
          {STRINGS.common.next}
        </Button>
      </div>

      {retakeEnabled && !canRetake && (
        <p className={styles.note}>재촬영 횟수를 모두 사용했습니다.</p>
      )}
    </main>
  );
}

/** 썸네일. `ImageBitmap`은 `<img>`로 못 그리므로 canvas에 옮긴다. */
function CutThumbnail({ cut }: { readonly cut: CapturedCut }) {
  const canvasRef = useRef<HTMLCanvasElement>(null);

  useEffect(() => {
    const canvas = canvasRef.current;
    const bitmap = cut.thumbnail;
    if (canvas === null || bitmap === undefined) return;
    canvas.width = bitmap.width;
    canvas.height = bitmap.height;
    canvas.getContext("2d")?.drawImage(bitmap, 0, 0);
  }, [cut.thumbnail]);

  return <canvas ref={canvasRef} className={styles.cutThumb} aria-hidden="true" />;
}

/** 카메라를 쓰지 않는 화면으로 나갈 때 확실히 정지시킨다. */
export function useStopCameraOnUnmount(): void {
  useEffect(() => () => getCameraService().stop(), []);
}

// ─────────────────────────────────── Result ──────────────────────────────────

/**
 * 결과 화면 — 03 §8 (합성 + 필터 4종)
 *
 * ⚠️ [다음]의 목적지는 **effective QR 판정**이 정한다(qrEffectivePolicy).
 *    게스트·TempUser 한도 초과는 `Qr`을 건너뛰고 `Done`으로 간다(VF-11 · E23).
 *    저장된 `EnableQrDelivery`를 **write하지 않는다** — 게스트 촬영 한 번에 운영자 설정이 꺼진다.
 */
export function ResultView() {
  const result = useResultCompose();
  /** [다음] 처리(타임랩스 생성 + 로컬 보관) 진행 중. 이중 클릭을 막는다. */
  const [finishing, setFinishing] = useState(false);

  const filterLabels: Record<FilterKind, string> = {
    None: "원본",
    Grayscale: "흑백",
    Brightness: "밝게",
    Beauty: "뷰티",
  };

  async function goNext(): Promise<void> {
    if (finishing) return;
    setFinishing(true);
    try {
      // 순서 전체(타임랩스 → 로컬 보관 → 전이)를 resultNext가 소유한다.
      // 여기서 순서를 다시 조립하지 마라 — M6-W는 resultNext.test.ts가 고정한다.
      await runResultNext(defaultResultNextDeps({ finalBlob: result.currentBlob }));
    } finally {
      // ⚠️ 보관까지 끝난 뒤에 푼다. 타임랩스 생성만 감싸면 보관 중 이중 클릭이 들어온다.
      setFinishing(false);
    }
  }

  return (
    <main className={styles.screen}>
      <div className={styles.captureStage}>
        {result.composing && <Spinner label="합성 중입니다…" />}
        {finishing && <Spinner label={STRINGS.result.timelapseBusy} />}
        {result.error !== null && (
          <p className={styles.note} role="alert">
            {result.error}
          </p>
        )}
        {result.imageUrl !== null && !result.composing && (
          <img className={styles.resultImage} src={result.imageUrl} alt="합성 결과" />
        )}
      </div>

      <div className={styles.actions}>
        {result.filters.map((filter) => (
          <Button
            key={filter}
            variant={filter === result.filter ? "primary" : "secondary"}
            disabled={result.composing}
            onClick={() => result.setFilter(filter)}
          >
            {filterLabels[filter]}
          </Button>
        ))}
      </div>

      <div className={styles.actions}>
        <Button onClick={() => void shellStore.getState().returnHome("결과 취소")}>
          {STRINGS.common.cancel}
        </Button>
        <Button
          variant="primary"
          disabled={result.composing || result.imageUrl === null || finishing}
          onClick={() => void goNext()}
        >
          {STRINGS.common.next}
        </Button>
      </div>

      {result.elapsedMs !== null && (
        <p className={styles.note}>합성 {result.elapsedMs}ms</p>
      )}
    </main>
  );
}
