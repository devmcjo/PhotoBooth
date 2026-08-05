import { logger } from "@adapters/storage/logStore";

/**
 * 셔터음 — WC5
 *
 * ⚠️ 브라우저 자동재생 정책상 **사용자 제스처 안에서 `AudioContext`를 unlock**해야 소리가 난다.
 *    [촬영 시작] 제스처에서 `unlockAudio()`를 부른다.
 * ⚠️ **실패해도 촬영 흐름을 막지 않는다.** 소리가 안 나는 것은 촬영 실패가 아니다.
 *
 * 음원 파일(`/sounds/shutter.wav`)이 없으면 **합성음으로 폴백**한다 — 자산 준비 전에도 동작해야 한다.
 */

let audioContext: AudioContext | null = null;
let buffer: AudioBuffer | null = null;
let loadAttempted = false;

const SHUTTER_URL = "/sounds/shutter.wav";

function contextClass(): typeof AudioContext | undefined {
  if (typeof AudioContext !== "undefined") return AudioContext;
  const legacy = (globalThis as { webkitAudioContext?: typeof AudioContext }).webkitAudioContext;
  return legacy;
}

/** 첫 제스처에서 호출. 이미 unlock돼 있으면 아무 것도 하지 않는다. */
export async function unlockAudio(): Promise<boolean> {
  const Ctor = contextClass();
  if (Ctor === undefined) return false;

  audioContext ??= new Ctor();
  try {
    if (audioContext.state === "suspended") await audioContext.resume();
  } catch (err) {
    logger.info("AudioContext unlock 실패(셔터음 없이 진행)", {
      reason: err instanceof Error ? err.message : String(err),
    });
    return false;
  }

  // 음원은 한 번만 시도한다. 없으면 합성음으로 간다.
  if (!loadAttempted) {
    loadAttempted = true;
    try {
      const response = await fetch(SHUTTER_URL, { cache: "force-cache" });
      if (response.ok) {
        buffer = await audioContext.decodeAudioData(await response.arrayBuffer());
      } else {
        logger.info("셔터음 자산 없음 — 합성음으로 대체", { status: response.status });
      }
    } catch {
      logger.info("셔터음 자산 로드 실패 — 합성음으로 대체");
    }
  }

  return audioContext.state === "running";
}

/** 합성 셔터음: 짧은 클릭(감쇠하는 사인). 자산이 없어도 "찰칵"에 가까운 신호를 준다. */
function playSynthetic(context: AudioContext): void {
  const oscillator = context.createOscillator();
  const gain = context.createGain();
  const now = context.currentTime;

  oscillator.type = "square";
  oscillator.frequency.setValueAtTime(1800, now);
  oscillator.frequency.exponentialRampToValueAtTime(600, now + 0.05);

  gain.gain.setValueAtTime(0.18, now);
  gain.gain.exponentialRampToValueAtTime(0.0001, now + 0.08);

  oscillator.connect(gain).connect(context.destination);
  oscillator.start(now);
  oscillator.stop(now + 0.09);
}

/**
 * 셔터음 재생. **동기 호출이고 결과를 기다리지 않는다** — 촬영 타이밍을 소리가 좌우하면 안 된다.
 */
export function playShutterSound(): void {
  const context = audioContext;
  if (context === null || context.state !== "running") return;

  try {
    if (buffer !== null) {
      const source = context.createBufferSource();
      source.buffer = buffer;
      source.connect(context.destination);
      source.start();
      return;
    }
    playSynthetic(context);
  } catch (err) {
    // 재생 실패는 무시한다(촬영은 계속된다).
    logger.info("셔터음 재생 실패", {
      reason: err instanceof Error ? err.message : String(err),
    });
  }
}

/** 진단·테스트용. */
export function isAudioUnlocked(): boolean {
  return audioContext !== null && audioContext.state === "running";
}

export function resetAudioForTests(): void {
  audioContext = null;
  buffer = null;
  loadAttempted = false;
}
