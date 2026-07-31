import { useEffect, useMemo, useRef, useState } from "react";
import { slotAspectRatio, type FrameTemplate } from "@domain/frames/types";
import {
  getSelectedCuts,
  isSelectionComplete,
  slotCount as slotCountOf,
  toggleSelection,
  canFullRetake,
  beginFullRetake,
} from "@domain/capture/captureSession";
import { createFallbackFrame, ensureFallbackImageUrl } from "@adapters/frames/fallbackFrame";
import { hasUsableImage } from "@domain/frames/frameCatalogPolicy";
import {
  classifyFrameLoad,
  frameLoadNotice,
  type FrameLoadPhase,
} from "@domain/frames/frameLoadPolicy";
import { getCameraService } from "@adapters/camera/cameraService";
import { requestWakeLock } from "@adapters/platform/wakeLock";
import { unlockAudio } from "@adapters/platform/shutterSound";
import { logger } from "@adapters/storage/logStore";
import { fixFrameAndResolveCutCount } from "@shell/captureSessionController";
import { useSettingsStore } from "@shell/settingsStore";
import { sessionStore, useSessionStore, type CapturedCut } from "@shell/sessionStore";
import { shellStore } from "@shell/shellStore";
import { Button, Spinner } from "@ui/components";
import { STRINGS } from "@ui/strings";
import { CameraPreview } from "./CameraPreview";
import { useResultCompose } from "@screens/result/useResultCompose";
import { isQrEffectivelyEnabled } from "@domain/settings/qrEffectivePolicy";
import type { FilterKind } from "@domain/filters/filterParams";
import { useCaptureRunner } from "@screens/capture/useCaptureRunner";
import styles from "./screens.module.css";

/**
 * 촬영 흐름 화면 — Home · FrameSelect(최소판) · Guide · Capture · CutSelect
 * (03 §2·§4·§5·§6·§7)
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

// ──────────────────────────────── FrameSelect ────────────────────────────────

/**
 * 최소 프레임 선택(WBS Step 7의 선순환 해소용).
 * 목록은 **코드 생성 fallback 1개**뿐이고, 서버 카탈로그·캐시·권한 UI·삭제는 Step 14가 확장한다.
 */
export function FrameSelectView() {
  const configuredCutCount = useSettingsStore((s) => s.values.CutCount);
  const [frames, setFrames] = useState<FrameTemplate[]>([]);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  /** 로딩 1회분이 끝났는지. Step 14가 진짜 국면 상태(FrameLoadPhase)로 대체한다. */
  const [loaded, setLoaded] = useState(false);
  /** [다시 시도] 카운터. 증가하면 effect가 다시 돈다(어댑터는 실패를 캐시하지 않는다). */
  const [reloadKey, setReloadKey] = useState(0);

  useEffect(() => {
    let cancelled = false;
    setLoaded(false);
    void ensureFallbackImageUrl().then((url) => {
      if (cancelled) return;
      // 이미지 생성 실패 시 어댑터는 예외가 아니라 **빈 URL**을 준다. 그 프레임을 목록에 올리면
      // 6컷을 다 찍은 뒤 Result에서야 합성이 실패한다 → 여기서 걸러 Failed로 끝낸다(03 §4.1).
      const candidate = createFallbackFrame(url, new Date().toISOString());
      const usable = hasUsableImage(candidate) ? [candidate] : [];
      setFrames(usable);
      // 첫 항목 자동 선택(03 §4). 목록이 비면 선택도 없다 → [다음] 비활성.
      setSelectedId(usable[0]?.id ?? null);
      setLoaded(true);
    });
    return () => {
      cancelled = true;
    };
  }, [reloadKey]);

  const selected = frames.find((f) => f.id === selectedId) ?? null;
  // 로딩이 끝나기 전에는 판정하지 않는다. 대기 오버레이·진행 문구는 Step 14 범위다.
  const phase: FrameLoadPhase = loaded ? classifyFrameLoad(frames.length, false) : "Loading";
  const notice = frameLoadNotice(phase);

  return (
    <main className={styles.screen}>
      <h1 className={styles.title}>프레임 선택</h1>
      <div className={styles.frameGrid}>
        {frames.map((frame) => (
          <button
            key={frame.id}
            type="button"
            className={[styles.frameCard, frame.id === selectedId ? styles.frameCardSelected : ""]
              .filter(Boolean)
              .join(" ")}
            aria-pressed={frame.id === selectedId}
            onClick={() => setSelectedId(frame.id)}
          >
            {frame.imageUrl.length > 0 && (
              <img className={styles.frameThumb} src={frame.imageUrl} alt="" />
            )}
            <span>{frame.name}</span>
            <span className={styles.note}>슬롯 {frame.slots.length}개</span>
          </button>
        ))}
      </div>

      {notice.length > 0 && (
        <p className={styles.note} role="alert">
          {notice}
        </p>
      )}

      <div className={styles.actions}>
        <Button onClick={() => void shellStore.getState().returnHome("프레임 선택 취소")}>
          {STRINGS.common.cancel}
        </Button>
        {phase === "Failed" && (
          <Button onClick={() => setReloadKey((n) => n + 1)}>{STRINGS.common.retry}</Button>
        )}
        <Button
          variant="primary"
          disabled={selected === null}
          onClick={() => {
            if (selected === null) return;
            // ★ 컷 수 해석의 **유일한 지점**(VF-12 · WD19).
            fixFrameAndResolveCutCount(selected, configuredCutCount);
            shellStore.getState().go("Guide");
          }}
        >
          {STRINGS.common.next}
        </Button>
      </div>
      <p className={styles.note}>
        Step 14에서 서버 공용 프레임·개인 프레임 목록으로 확장됩니다.
      </p>
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
  const rawEnableQr = useSettingsStore((s) => s.values.EnableQrDelivery);
  const user = useSessionStore((s) => s.currentUser);

  const filterLabels: Record<FilterKind, string> = {
    None: "원본",
    Grayscale: "흑백",
    Brightness: "밝게",
    Beauty: "뷰티",
  };

  function goNext(): void {
    // TempUser 한도는 Step 11에서 qr-usage 조회로 채운다(지금은 미차단).
    const qrOn = isQrEffectivelyEnabled(rawEnableQr, user !== null, false);
    shellStore.getState().go(qrOn ? "Qr" : "Done");
  }

  return (
    <main className={styles.screen}>
      <div className={styles.captureStage}>
        {result.composing && <Spinner label="합성 중입니다…" />}
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
          disabled={result.composing || result.imageUrl === null}
          onClick={goNext}
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
