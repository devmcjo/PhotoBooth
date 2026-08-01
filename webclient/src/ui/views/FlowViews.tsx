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
import { requestCameraPermission } from "@adapters/camera/cameraPermission";
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
import { useCameraPermission } from "@screens/capture/useCameraPermission";
import styles from "./screens.module.css";

/**
 * 촬영 흐름 화면 — Home · Guide · Capture · CutSelect · Result (03 §2·§5·§6·§7·§8)
 *
 * ⚠️ `FrameSelect`는 **여기 없다.** Step 14에서 본편(`ui/views/FrameSelectView.tsx`)으로 분리했다 —
 *    대기 4국면·삭제 오버레이·카탈로그 배선이 붙어 이 파일에 두기에는 너무 커졌다.
 */

// ─────────────────────────────────── Home ───────────────────────────────────

export function HomeView({ appName, subtitle }: { readonly appName: string; readonly subtitle: string }) {
  // 안내 문구는 **권한 상태에 따라 달라진다**(03 §2 86행). 조회만 하고 프롬프트는 띄우지 않는다.
  const { permission } = useCameraPermission();

  return (
    <main className={styles.screen}>
      {/*
        Windows `Views/HomeView.xaml:9-12`의 파스텔 장식 원 2개.
        ⚠️ 장식이므로 `aria-hidden`이고 `pointer-events:none`이다(스크린리더·클릭 모두 무시).
        ⚠️ 음수 오프셋은 `main.css`의 `overflow-x: hidden`이 흡수한다 — 그 규칙을 지우면
           가로 스크롤이 생긴다.
      */}
      <div className={styles.homeDecorTopLeft} aria-hidden="true" />
      <div className={styles.homeDecorBottomRight} aria-hidden="true" />

      <h1 className={styles.homeTitle}>{appName}</h1>
      <p className={styles.homeSubtitle}>{subtitle}</p>
      {/* ⚠️ Home의 CTA는 **1개**다(03 §2). 권한 요청 버튼은 Guide에 있다. */}
      <Button
        variant="primary"
        className={styles.homeCta}
        onClick={() => {
          // 게스트 직행 — 로그인 화면을 강제로 거치지 않는다(03 §2).
          sessionStore.getState().discardCaptureData();
          shellStore.getState().go("FrameSelect");
        }}
      >
        {STRINGS.home.start}
      </Button>
      {/* `granted`면 아무것도 렌더하지 않는다 — 이미 준비된 상태를 굳이 알릴 이유가 없다. */}
      {permission !== "granted" && (
        <p className={styles.note}>
          {permission === "denied"
            ? STRINGS.camera.homeDeniedNote
            : STRINGS.camera.homePromptNote}
        </p>
      )}
    </main>
  );
}

// ─────────────────────────────────── Guide ───────────────────────────────────

/**
 * 카메라 권한 사전 요청 블록 — 03 §5 · 07 §3
 *
 * Home이 아니라 Guide에 두는 이유: Home은 CTA 1개 화면이고(03 §2), Guide는 이미 [촬영 시작]
 * 직전의 "준비 확인" 화면이다 — **권한도 준비 항목**이다.
 *
 * ⚠️ 브라우저는 페이지가 뜨자마자 권한 팝업을 띄울 수 없다. `getUserMedia()` 호출이 필요하고
 *    그 호출은 사용자 제스처를 요구한다 → **버튼 클릭 안에서만** 요청한다.
 */
function CameraPermissionBlock() {
  const { permission, refresh } = useCameraPermission();

  // 이미 허용됐으면 아무것도 렌더하지 않는다.
  if (permission === "granted") return null;

  if (permission === "denied") {
    return (
      <section className={styles.permissionBlock}>
        <p className={styles.permissionHint} role="status">
          {STRINGS.camera.deniedHint}
        </p>
        {/* ⚠️ `innerHTML` 금지 — 복구 절차는 전부 JSX 텍스트 노드다. */}
        <details className={styles.permissionRecovery}>
          <summary>{STRINGS.camera.recovery.title}</summary>
          <ul>
            <li>{STRINGS.camera.recovery.chrome}</li>
            <li>{STRINGS.camera.recovery.android}</li>
            <li>{STRINGS.camera.recovery.ios}</li>
            <li>{STRINGS.camera.recovery.macos}</li>
            <li>{STRINGS.camera.recovery.os}</li>
          </ul>
        </details>
      </section>
    );
  }

  // `prompt`와 `null`(조회 불가 — Safari·Firefox) 모두 버튼을 **보여준다**.
  // 눌러서 손해가 없고, 숨기면 그 브라우저에서 이 기능이 통째로 사라진다.
  return (
    <section className={styles.permissionBlock}>
      <p className={styles.permissionHint}>{STRINGS.camera.allowHint}</p>
      <Button
        variant="primary"
        onClick={() => {
          // 어댑터는 throw하지 않는다 — 결과는 판별 유니온이고 상태는 구독이 갱신한다.
          // 미지원 브라우저는 `change` 이벤트가 오지 않으므로 직접 다시 조회한다.
          void requestCameraPermission().then(() => refresh());
        }}
      >
        {STRINGS.camera.allowButton}
      </Button>
    </section>
  );
}

export function GuideView() {
  const session = useSessionStore((s) => s.session);
  const values = useSettingsStore((s) => s.values);
  const slots = slotCountOf(session);

  return (
    <main className={styles.screen}>
      {/* Windows `GuideView.xaml`은 정보를 Card(MinWidth 440) 안에 담는다. */}
      <section className={styles.guideCard}>
        <h1 className={styles.guideTitle}>촬영 안내</h1>
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

        <CameraPermissionBlock />

        {/* ⚠️ [촬영 시작]은 **어떤 권한 상태에서도 비활성화하지 않는다** — 손님이 갇히면 안 되고,
            거부 상태라면 `Capture`가 사유를 다시 보여준다(03 §6.3). */}
        <Button
          variant="primary"
          className={styles.guidePrimary}
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
        <Button
          variant="ghost"
          className={styles.guideCancel}
          onClick={() => void shellStore.getState().returnHome("가이드 취소")}
        >
          {STRINGS.common.cancel}
        </Button>
      </section>
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
          // 장치 부재·점유 실패는 조건이 바뀌면 성공할 수 있다 → 진입 절차를 처음부터 다시 탄다.
          // 항상 넘겨도 안전하다: 버튼 노출은 `CameraPreview`가 `isCameraRetryable`로 거른다
          // (권한 거부·비보안 컨텍스트에는 나타나지 않는다 — 03 §6.3).
          onRetry={() => runner.retryCamera()}
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
      {/* WPF `ResultView.xaml`: 2열(좌 미리보기 가변 · 우 340 고정). 좁은 화면은 1열로 접힌다. */}
      <div className={styles.resultLayout}>
        <div className={styles.resultMain}>
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
          {result.elapsedMs !== null && (
            <p className={styles.note}>합성 {result.elapsedMs}ms</p>
          )}
        </div>

        <div className={styles.resultSide}>
          {/*
            WPF `Button.Filter`: 세로 목록(항목 간 8) · 선택 시 Accent.Soft 배경 + Accent 테두리.
            ⚠️ `variant`를 주지 않는다 — 선택 표현은 `.filterPill`이 `aria-pressed`로만 가른다.
          */}
          <div className={styles.filterList} role="group" aria-label="필터">
            {result.filters.map((filter) => (
              <Button
                key={filter}
                className={styles.filterPill}
                aria-pressed={filter === result.filter}
                disabled={result.composing}
                onClick={() => result.setFilter(filter)}
              >
                {filterLabels[filter]}
              </Button>
            ))}
          </div>

          <Button
            variant="primary"
            className={styles.resultNext}
            disabled={result.composing || result.imageUrl === null || finishing}
            onClick={() => void goNext()}
          >
            {STRINGS.common.next}
          </Button>
          <Button
            variant="ghost"
            className={styles.resultCancel}
            onClick={() => void shellStore.getState().returnHome("결과 취소")}
          >
            {STRINGS.common.cancel}
          </Button>
        </div>
      </div>
    </main>
  );
}
