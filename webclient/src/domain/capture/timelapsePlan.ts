/**
 * 타임랩스 선별·인코딩 규격 — 04 §7.2·§7.3c·§7.4
 *
 * 촬영 중에는 인코딩하지 않고 OPFS에 스풀만 한다. 종료 시 **실제 경과**로 배속을 정하고
 * 스풀에서 균등 선별한다. Windows는 실제 녹화 길이로 배속을 계산하므로([바로 촬영] 다용
 * 세션도 원속 산출), 예상 길이로 stride를 고정하면 그 동등성이 깨진다.
 *
 * ⚠️ 여기에는 spec-vector가 없다. Windows 대응 함수가 존재하지 않기 때문이다
 *    (Windows는 ffmpeg `setpts,fps=30` 필터가 이 계산을 대신한다). 교차 고정할 대상이
 *    없으므로 `docs/spec-vectors/`를 건드리지 않는다.
 */
import { roundHalfToEven } from "../mathCompat";
import { computeSpeedFactor, expectedOutputSeconds } from "./timelapseSpeed";

/** 출력 컨테이너 타임라인 fps(04 §7.2). */
export const TIMELAPSE_OUTPUT_FPS = 30;
/** 이 수보다 적게 선별되면 1초 미만 영상이라 만들지 않는다(04 §7.2). */
export const TIMELAPSE_MIN_FRAMES = 30;
/** 백프레셔 임계 — `encodeQueueSize`가 이 값을 넘으면 드롭한다(04 §7.5). */
export const TIMELAPSE_ENCODE_QUEUE_LIMIT = 8;

/** 코덱 후보 — Baseline L3.0 우선, 실패 시 순서대로(04 §7.3c). */
export const TIMELAPSE_CODEC_CANDIDATES = ["avc1.42001E", "avc1.42E01E", "avc1.4D001E"] as const;

export interface TimelapsePlan {
  /** `computeSpeedFactor(actualSeconds)`. */
  readonly speedFactor: number;
  /** 목표 출력 길이(초) = `actualSeconds / speedFactor`. */
  readonly outputSeconds: number;
  /** 30fps 기준 이상적 프레임 수(스풀이 부족하면 실제 선별 수가 이보다 적다). */
  readonly targetFrames: number;
  /** 선별한 스풀 배열 인덱스(오름차순·중복 없음). */
  readonly selectedIndices: readonly number[];
  /** 프레임 1장당 duration(μs). 스풀 부족 시 33333보다 길어진다. */
  readonly frameDurationUs: number;
  /** 프레임별 프레젠테이션 타임스탬프(μs). `selectedIndices`와 같은 길이·순서. */
  readonly timestampsUs: readonly number[];
}

export interface TimelapsePlanInput {
  /** 스풀에 남아 있는 프레임 수. */
  readonly spoolFrameCount: number;
  /** 촬영 시퀀스 시작~종료 **실경과**(초). */
  readonly actualSeconds: number;
  readonly outputFps?: number;
  readonly minFrames?: number;
}

/**
 * 선별 계획. 만들 가치가 없으면 **`null`**(예외 아님 — VF-6).
 *
 * ⚠️ 타임스탬프를 `i * 33333μs`로 고정하지 않는다. 스풀이 부족한 세션에서 그렇게 하면
 *    출력이 의도한 길이보다 **짧아진다**. [04 §7.2]는 "duration이 길어질 뿐 길이는 유지"가
 *    규격이므로 `i * outputSeconds / count`로 배치한다. 스풀이 충분한 정상 경로에서는
 *    `outputSeconds*1e6 / (outputSeconds*30) = 33333.3…μs`로 결국 같은 값이다.
 */
export function planTimelapse(input: TimelapsePlanInput): TimelapsePlan | null {
  const fps = input.outputFps ?? TIMELAPSE_OUTPUT_FPS;
  const minFrames = input.minFrames ?? TIMELAPSE_MIN_FRAMES;

  if (!Number.isFinite(input.actualSeconds) || input.actualSeconds <= 0) return null;
  if (!Number.isFinite(input.spoolFrameCount) || input.spoolFrameCount <= 0) return null;

  const speedFactor = computeSpeedFactor(input.actualSeconds);
  const outputSeconds = expectedOutputSeconds(input.actualSeconds, speedFactor);
  // 규격의 `round(...)`는 은행가 반올림이다(04 §9) — `Math.round`를 쓰면 Windows와 갈린다.
  const targetFrames = roundHalfToEven(outputSeconds * fps);
  const count = Math.min(targetFrames, Math.floor(input.spoolFrameCount));
  if (count < minFrames) return null;

  const selectedIndices = evenlySample(Math.floor(input.spoolFrameCount), count);
  // μs 환산은 웹 전용 계산이라 크로스 플랫폼 계약이 없다. 여기서는 정수 μs로만 떨어지면
  // 되므로 통상 반올림을 쓴다(0.5μs 동률이 실제로 발생하지 않는 규모다).
  const totalUs = Math.round(outputSeconds * 1_000_000);
  const frameDurationUs = Math.max(1, Math.round(totalUs / count));
  const timestampsUs: number[] = [];
  for (let i = 0; i < count; i++) {
    // 누적 합이 아니라 인덱스에서 직접 산출한다 — duration을 더해 가면 드리프트가 쌓인다.
    timestampsUs.push(Math.round((i * totalUs) / count));
  }

  return {
    speedFactor,
    outputSeconds,
    targetFrames,
    selectedIndices,
    frameDurationUs,
    timestampsUs,
  };
}

/**
 * `total`개에서 `count`개를 균등 선별한 인덱스.
 * `index_i = floor(i * total / count)` — `count <= total`이면 **strictly increasing**이라
 * 중복이 나오지 않는다. `count <= 0 || total <= 0`이면 빈 배열.
 *
 * 여기만 `roundHalfToEven`이 아니라 `floor`인 이유: 이 함수에는 Windows 대응 계약이 없고,
 * `floor`라야 인덱스 중복이 **원천 차단**된다(반올림은 두 i가 같은 인덱스를 낼 수 있다).
 */
export function evenlySample(total: number, count: number): number[] {
  if (!Number.isFinite(total) || !Number.isFinite(count)) return [];
  if (total <= 0 || count <= 0) return [];

  const size = Math.min(Math.floor(count), Math.floor(total));
  const indices: number[] = [];
  for (let i = 0; i < size; i++) {
    indices.push(Math.floor((i * Math.floor(total)) / size));
  }
  return indices;
}

/**
 * 04 §7.4 비트레이트 표. CRF 20 상당 근사.
 *   ≤640×854 → 2.5Mbps · ≤810×1080 → 5Mbps · ≤1080×1440 → 8Mbps
 *   그 이상 → `w*h*30*0.12`(12Mbps 상한)
 */
export function timelapseBitrate(width: number, height: number): number {
  if (!Number.isFinite(width) || !Number.isFinite(height) || width <= 0 || height <= 0) {
    return 2_500_000;
  }
  if (width <= 640 && height <= 854) return 2_500_000;
  if (width <= 810 && height <= 1080) return 5_000_000;
  if (width <= 1080 && height <= 1440) return 8_000_000;
  return Math.min(12_000_000, Math.round(width * height * 30 * 0.12));
}

/**
 * yuv420p(4:2:0)는 **양변이 짝수**여야 인코더가 열린다(04 §7.3c).
 * Windows `FfmpegArgs.EvenDimensionCrop`(`crop=trunc(iw/2)*2:trunc(ih/2)*2`)과 동일 식이라
 * 1443×1080 → 1442×1080처럼 **우/하단 1px을 잘라낸다**. 최소 2를 보장한다.
 */
export function evenDimensions(
  width: number,
  height: number,
): { width: number; height: number } {
  return { width: clampEven(width), height: clampEven(height) };
}

function clampEven(value: number): number {
  if (!Number.isFinite(value)) return 2;
  const even = Math.trunc(value / 2) * 2;
  return even < 2 ? 2 : even;
}
