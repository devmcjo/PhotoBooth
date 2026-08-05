import {
  COUNTDOWN_TICK_MS,
  CUT_INTERVAL_MS,
  FLASH_DURATION_MS,
} from "@domain/capture/captureTiming";
import { logger } from "@adapters/storage/logStore";

/**
 * N컷 연속 촬영 시퀀스 — 03 §6.1 (**순서가 규격이다**)
 *
 * ```
 * 컷 루프 (i = 1..N)
 *   a. 카운트다운(설정 초)
 *   b. 플래시 on → 120ms 대기
 *   c. 셔터음(비동기·실패 무시)
 *   d. 스틸 캡처 → 컷 버퍼 + OPFS cut{i}.jpg
 *   e. 플래시 off        ← 캡처 "후"다
 *   f. 300ms 대기
 * ```
 *
 * ⚠️ **카운트다운은 실경과(`performance.now()`) 기반이다**(WM3). tick을 세면 탭 스로틀링에서
 *    6초가 몇 초 더 늘어난다.
 * ⚠️ **컷 수 N을 하드코딩하지 않는다**(it17). 자동 컷 수는 7·9 같은 값을 만든다.
 * ⚠️ 취소는 **부분 결과를 남기지 않는다**(WM4). 호출측이 세션 폴더를 삭제한다.
 */

export class CaptureCancelledError extends Error {
  constructor() {
    super("촬영이 취소되었습니다.");
    this.name = "CaptureCancelledError";
  }
}

/** 시퀀스가 만든 컷 1개. 픽셀은 OPFS에 있고 여기서는 참조만 든다(WR8). */
export interface SequenceCut {
  readonly index: number;
  readonly fileName: string;
  readonly thumbnail?: ImageBitmap;
}

export interface CaptureSequenceDeps {
  /** 스틸 캡처. 실패는 `null`. */
  readonly captureStill: () => Promise<Blob | null>;
  /** OPFS에 컷 기록. 실패는 `false`. */
  readonly writeCut: (index: number, bytes: Blob) => Promise<boolean>;
  /** 컷 파일명(`sessionWorkspace.cutFileName`과 같은 규약). */
  readonly cutFileName: (index: number) => string;
  /** 썸네일 생성(선택). 실패해도 촬영을 막지 않는다. */
  readonly makeThumbnail?: (bytes: Blob) => Promise<ImageBitmap | undefined>;
  readonly now: () => number;
  readonly delay: (ms: number) => Promise<void>;
  /** 남은 초 표시(0이면 셔터 직전). */
  readonly onCountdown: (remainingSec: number) => void;
  readonly onFlash: (on: boolean) => void;
  /** 컷 1장 완료 통지(진행 표시 갱신). */
  readonly onCutCaptured: (cut: SequenceCut, total: number) => void;
  /** 셔터음. 비동기·실패 무시(04 WC5). */
  readonly playShutter: () => void;
}

export interface CaptureSequenceSettings {
  readonly countdownSec: number;
  readonly flash: boolean;
  readonly shutterSound: boolean;
}

export interface CaptureSequence {
  /** N컷을 촬영한다. 취소되면 `CaptureCancelledError`를 던진다. */
  run(cutCount: number, settings: CaptureSequenceSettings): Promise<SequenceCut[]>;
  /** 남은 카운트다운을 건너뛰고 즉시 셔터([바로 촬영]). **매 컷 사용 가능**하다. */
  skipCountdown(): void;
  /** 시퀀스 취소(이탈·탭 hidden·유휴). */
  cancel(): void;
  readonly isRunning: boolean;
}

export function createCaptureSequence(deps: CaptureSequenceDeps): CaptureSequence {
  let cancelled = false;
  let skipRequested = false;
  let running = false;
  let flashOn = false;

  function throwIfCancelled(): void {
    if (cancelled) throw new CaptureCancelledError();
  }

  /**
   * 플래시 토글을 **멱등**으로 만든다. 컷 단위 `finally`와 시퀀스 단위 안전망이
   * 둘 다 끄려 하는데, 그대로 두면 같은 통지가 두 번 나가 화면이 불필요하게 리렌더된다.
   */
  function setFlash(on: boolean): void {
    if (flashOn === on) return;
    flashOn = on;
    deps.onFlash(on);
  }

  /** 실경과 기반 카운트다운. `skipCountdown()`이 들어오면 즉시 끝난다. */
  async function countdown(seconds: number): Promise<void> {
    const endsAt = deps.now() + seconds * 1000;
    for (;;) {
      throwIfCancelled();
      if (skipRequested) {
        skipRequested = false;
        logger.info("바로 촬영으로 카운트다운 건너뜀");
        break;
      }
      const remainingMs = endsAt - deps.now();
      if (remainingMs <= 0) break;
      deps.onCountdown(Math.ceil(remainingMs / 1000));
      // 남은 시간이 tick보다 짧으면 그만큼만 기다린다(과다 대기 방지).
      await deps.delay(Math.min(COUNTDOWN_TICK_MS, remainingMs));
    }
    deps.onCountdown(0);
  }

  async function captureOne(
    index: number,
    settings: CaptureSequenceSettings,
    total: number,
  ): Promise<SequenceCut | null> {
    // b. 플래시 on → 120ms. 설정이 off면 대기도 하지 않는다(불필요한 지연 금지).
    if (settings.flash) {
      setFlash(true);
      await deps.delay(FLASH_DURATION_MS);
    }

    try {
      throwIfCancelled();

      // c. 셔터음 — 실패해도 촬영 흐름을 막지 않는다.
      if (settings.shutterSound) deps.playShutter();

      // d. 스틸 캡처 → OPFS
      const blob = await deps.captureStill();
      if (blob === null) {
        logger.warn("컷 캡처 실패(스틸 없음)", { index });
        return null;
      }

      const written = await deps.writeCut(index, blob);
      if (!written) {
        // 성공 오인 금지(M4) — 상위가 토스트를 띄운다.
        logger.error("컷 저장 실패", { index });
        return null;
      }

      let thumbnail: ImageBitmap | undefined;
      if (deps.makeThumbnail !== undefined) {
        try {
          thumbnail = await deps.makeThumbnail(blob);
        } catch (err) {
          // 썸네일은 표시용이다 — 실패해도 컷은 유효하다.
          logger.warn("썸네일 생성 실패", {
            index,
            reason: err instanceof Error ? err.message : String(err),
          });
        }
      }

      const cut: SequenceCut = {
        index,
        fileName: deps.cutFileName(index),
        ...(thumbnail === undefined ? {} : { thumbnail }),
      };
      deps.onCutCaptured(cut, total);
      return cut;
    } finally {
      // e. 플래시 off — 캡처 "후"다. finally에 두어 실패·취소에서도 꺼진다.
      if (settings.flash) setFlash(false);
    }
  }

  return {
    get isRunning() {
      return running;
    },

    skipCountdown() {
      if (running) skipRequested = true;
    },

    cancel() {
      cancelled = true;
      skipRequested = false;
    },

    async run(cutCount, settings) {
      cancelled = false;
      skipRequested = false;
      running = true;
      const cuts: SequenceCut[] = [];

      try {
        // it17: N은 임의 정수다(자동 컷 수가 7을 만들 수 있다). 루프가 N을 그대로 받는다.
        for (let index = 1; index <= cutCount; index++) {
          throwIfCancelled();
          await countdown(settings.countdownSec);
          throwIfCancelled();

          const cut = await captureOne(index, settings, cutCount);
          if (cut !== null) cuts.push(cut);

          // f. 마지막 컷 뒤에는 기다리지 않는다(불필요한 지연).
          if (index < cutCount) {
            throwIfCancelled();
            await deps.delay(CUT_INTERVAL_MS);
          }
        }
        logger.info("촬영 시퀀스 완료", { requested: cutCount, captured: cuts.length });
        return cuts;
      } finally {
        running = false;
        // 취소 경로에서도 플래시가 남지 않게 한다.
        setFlash(false);
      }
    },
  };
}
