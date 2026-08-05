import { describe, expect, it } from "vitest";
import {
  evenDimensions,
  evenlySample,
  planTimelapse,
  timelapseBitrate,
  TIMELAPSE_CODEC_CANDIDATES,
  TIMELAPSE_ENCODE_QUEUE_LIMIT,
  TIMELAPSE_MIN_FRAMES,
  TIMELAPSE_OUTPUT_FPS,
} from "@domain/capture/timelapsePlan";
import { computeSpeedFactor } from "@domain/capture/timelapseSpeed";

/**
 * 타임랩스 선별 규격 — 04 §7.2·§7.4, analysis/14 §7.2
 *
 * 전부 순수 함수라 node에서 전량 검증된다. 브라우저가 필요한 것은 인코더 어댑터뿐이다.
 */

/** 계획의 총 길이(μs) = 마지막 타임스탬프 + 마지막 프레임 duration. */
function totalDurationUs(plan: NonNullable<ReturnType<typeof planTimelapse>>): number {
  const last = plan.timestampsUs[plan.timestampsUs.length - 1] ?? 0;
  return last + plan.frameDurationUs;
}

describe("planTimelapse — 실경과 기반 선별(04 §7.2)", () => {
  it("6컷 ~38초 세션 → 3.04배속 · 12.5초 · 375프레임 · 33333μs", () => {
    const plan = planTimelapse({ spoolFrameCount: 570, actualSeconds: 38 });
    expect(plan).not.toBeNull();
    expect(plan!.speedFactor).toBeCloseTo(3.04, 10);
    expect(plan!.outputSeconds).toBeCloseTo(12.5, 10);
    expect(plan!.targetFrames).toBe(375);
    expect(plan!.selectedIndices).toHaveLength(375);
    expect(plan!.frameDurationUs).toBe(33333);
  });

  it("[바로 촬영] 다용으로 ~5초가 된 세션도 원속으로 만든다(null이 아니다)", () => {
    // 고정 stride였다면 30장 하한에 미달해 타임랩스가 통째로 사라진다 — 그것을 막는 케이스다.
    const plan = planTimelapse({ spoolFrameCount: 75, actualSeconds: 5 });
    expect(plan).not.toBeNull();
    expect(plan!.speedFactor).toBe(1);
    expect(plan!.outputSeconds).toBe(5);
    expect(plan!.selectedIndices).toHaveLength(75);
    expect(totalDurationUs(plan!)).toBe(5_000_000);
  });

  it.each([
    [12, 1.0, 12],
    [15, 1.0, 15],
    [30, 2.4, 12.5],
    [60, 4.8, 12.5],
    [120, 9.6, 12.5],
  ])(
    "analysis/14 §7.2 표: %s초 → N=%s · 결과 %s초",
    (sessionSeconds, expectedN, expectedOutput) => {
      const plan = planTimelapse({ spoolFrameCount: 900, actualSeconds: sessionSeconds });
      expect(plan).not.toBeNull();
      expect(plan!.speedFactor).toBeCloseTo(expectedN, 10);
      expect(plan!.outputSeconds).toBeCloseTo(expectedOutput, 10);
      // 배속 함수 자체는 기존 도메인 것을 그대로 쓴다(드리프트 방지).
      expect(plan!.speedFactor).toBe(computeSpeedFactor(sessionSeconds));
    },
  );

  it("스풀이 부족하면 duration이 늘어날 뿐 **출력 길이는 유지**된다", () => {
    const plan = planTimelapse({ spoolFrameCount: 40, actualSeconds: 38 });
    expect(plan).not.toBeNull();
    expect(plan!.targetFrames).toBe(375);
    expect(plan!.selectedIndices).toHaveLength(40);
    expect(plan!.frameDurationUs).toBeGreaterThan(33333);
    // `i * 33333`을 그대로 썼다면 40 * 33333 = 1.33초짜리가 나온다.
    expect(totalDurationUs(plan!)).toBe(12_500_000);
  });

  it("스풀이 충분하면 프레임 duration이 30fps 격자(33333μs)다", () => {
    const plan = planTimelapse({ spoolFrameCount: 900, actualSeconds: 60 });
    expect(plan!.frameDurationUs).toBeGreaterThanOrEqual(33332);
    expect(plan!.frameDurationUs).toBeLessThanOrEqual(33334);
  });

  it("타임스탬프가 단조 증가하고 인덱스에서 직접 산출된다(드리프트 없음)", () => {
    const plan = planTimelapse({ spoolFrameCount: 200, actualSeconds: 38 })!;
    for (let i = 1; i < plan.timestampsUs.length; i++) {
      expect(plan.timestampsUs[i]!).toBeGreaterThan(plan.timestampsUs[i - 1]!);
    }
    expect(plan.timestampsUs[0]).toBe(0);
  });

  it("29장은 null, 30장은 생성한다(1초 미만 영상 금지)", () => {
    expect(planTimelapse({ spoolFrameCount: 29, actualSeconds: 38 })).toBeNull();
    expect(planTimelapse({ spoolFrameCount: 30, actualSeconds: 38 })).not.toBeNull();
    expect(TIMELAPSE_MIN_FRAMES).toBe(30);
  });

  it("경과가 0 이하이거나 스풀이 0이면 null이다", () => {
    expect(planTimelapse({ spoolFrameCount: 900, actualSeconds: 0 })).toBeNull();
    expect(planTimelapse({ spoolFrameCount: 900, actualSeconds: -1 })).toBeNull();
    expect(planTimelapse({ spoolFrameCount: 900, actualSeconds: Number.NaN })).toBeNull();
    expect(planTimelapse({ spoolFrameCount: 0, actualSeconds: 38 })).toBeNull();
    expect(planTimelapse({ spoolFrameCount: Number.NaN, actualSeconds: 38 })).toBeNull();
  });

  it("targetFrames가 **은행가 반올림**이다(Math.round와 갈리는 입력으로 고정)", () => {
    // 12.5 → Math.round는 13, C#/규격의 round는 12(짝수 쪽).
    const a = planTimelapse({
      spoolFrameCount: 100,
      actualSeconds: 12.5,
      outputFps: 1,
      minFrames: 1,
    });
    expect(a!.targetFrames).toBe(12);
    expect(Math.round(12.5)).toBe(13);

    // 2.5 → Math.round는 3, 규격은 2.
    const b = planTimelapse({
      spoolFrameCount: 100,
      actualSeconds: 2.5,
      outputFps: 1,
      minFrames: 1,
    });
    expect(b!.targetFrames).toBe(2);
  });

  it("상수가 규격값이다", () => {
    expect(TIMELAPSE_OUTPUT_FPS).toBe(30);
    expect(TIMELAPSE_ENCODE_QUEUE_LIMIT).toBe(8);
    // Baseline L3.0이 1순위다(04 §7.3c).
    expect(TIMELAPSE_CODEC_CANDIDATES).toEqual(["avc1.42001E", "avc1.42E01E", "avc1.4D001E"]);
  });
});

describe("evenlySample — 균등 선별", () => {
  it("중복 없이 단조 증가한다", () => {
    const sample = evenlySample(570, 375);
    expect(sample).toHaveLength(375);
    expect(new Set(sample).size).toBe(375);
    for (let i = 1; i < sample.length; i++) {
      expect(sample[i]!).toBeGreaterThan(sample[i - 1]!);
    }
    expect(sample[0]).toBe(0);
    expect(sample[sample.length - 1]!).toBeLessThan(570);
  });

  it("count === total이면 전부 그대로다", () => {
    expect(evenlySample(5, 5)).toEqual([0, 1, 2, 3, 4]);
  });

  it("count가 1이면 첫 프레임 하나다", () => {
    expect(evenlySample(100, 1)).toEqual([0]);
  });

  it("count가 total보다 크면 total로 잘린다(같은 인덱스를 두 번 인코딩하지 않는다)", () => {
    expect(evenlySample(3, 10)).toEqual([0, 1, 2]);
  });

  it("0·음수·비유한 입력은 빈 배열이다", () => {
    expect(evenlySample(0, 5)).toEqual([]);
    expect(evenlySample(5, 0)).toEqual([]);
    expect(evenlySample(-1, 5)).toEqual([]);
    expect(evenlySample(Number.NaN, 5)).toEqual([]);
    expect(evenlySample(5, Number.POSITIVE_INFINITY)).toEqual([]);
  });
});

describe("timelapseBitrate — 04 §7.4 표", () => {
  it("구간 경계값", () => {
    expect(timelapseBitrate(640, 854)).toBe(2_500_000);
    expect(timelapseBitrate(641, 854)).toBe(5_000_000);
    expect(timelapseBitrate(810, 1080)).toBe(5_000_000);
    expect(timelapseBitrate(811, 1080)).toBe(8_000_000);
    expect(timelapseBitrate(1080, 1440)).toBe(8_000_000);
  });

  it("표를 넘으면 화소수 기반 산출 + 12Mbps 상한", () => {
    // 1082×1442 → 1082*1442*30*0.12 ≈ 5.6Mbps
    expect(timelapseBitrate(1082, 1442)).toBe(Math.round(1082 * 1442 * 30 * 0.12));
    // 4K급은 상한에 걸린다.
    expect(timelapseBitrate(3840, 2160)).toBe(12_000_000);
  });

  it("비정상 입력도 예외 없이 최저 구간을 준다", () => {
    expect(timelapseBitrate(0, 0)).toBe(2_500_000);
    expect(timelapseBitrate(Number.NaN, 100)).toBe(2_500_000);
  });
});

describe("evenDimensions — yuv420p 짝수 강제(04 §7.3c)", () => {
  it("홀수 변은 1px 잘라낸다 — Windows fb3e99d 결함과 같은 식", () => {
    expect(evenDimensions(1443, 1080)).toEqual({ width: 1442, height: 1080 });
    expect(evenDimensions(811, 1081)).toEqual({ width: 810, height: 1080 });
  });

  it("짝수 입력은 그대로다", () => {
    expect(evenDimensions(810, 1080)).toEqual({ width: 810, height: 1080 });
    expect(evenDimensions(1920, 1080)).toEqual({ width: 1920, height: 1080 });
  });

  it("최소 2를 보장한다(인코더가 0×0으로 열리지 않게)", () => {
    expect(evenDimensions(1, 1)).toEqual({ width: 2, height: 2 });
    expect(evenDimensions(0, 3)).toEqual({ width: 2, height: 2 });
    expect(evenDimensions(Number.NaN, Number.NaN)).toEqual({ width: 2, height: 2 });
  });
});
