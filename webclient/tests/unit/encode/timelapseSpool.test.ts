import { describe, expect, it } from "vitest";
import {
  decimatedInterval,
  planDecimation,
  shouldSpoolFrame,
  TIMELAPSE_SPOOL_DECIMATION_FACTOR,
  TIMELAPSE_SPOOL_INTERVAL_MS,
  TIMELAPSE_SPOOL_MAX_FRAMES,
} from "@domain/capture/timelapseSpool";

/** 스풀 파일명 규약과 같은 형태(0 패딩 5자리)로 가짜 목록을 만든다. */
function names(count: number, from = 0): string[] {
  return Array.from({ length: count }, (_, i) => `${String(from + i).padStart(5, "0")}.jpg`);
}

describe("shouldSpoolFrame — 시간 기반 stride(04 §7.2)", () => {
  it("초기값 -Infinity면 첫 프레임을 수집한다", () => {
    // 함정 #4 회귀 고정: 초기값을 0으로 두면 시계 원점 근처에서 첫 프레임을 먹는다.
    expect(shouldSpoolFrame(Number.NEGATIVE_INFINITY, 0, TIMELAPSE_SPOOL_INTERVAL_MS)).toBe(true);
    expect(shouldSpoolFrame(0, 0, TIMELAPSE_SPOOL_INTERVAL_MS)).toBe(false);
  });

  it("간격 미만이면 수집하지 않는다", () => {
    expect(shouldSpoolFrame(1000, 1050, 66.67)).toBe(false);
  });

  it("정확히 간격이면 수집한다(경계 포함)", () => {
    expect(shouldSpoolFrame(1000, 1066.67, 66.67)).toBe(true);
    expect(shouldSpoolFrame(1000, 1200, 66.67)).toBe(true);
  });

  it("수집 상한은 15fps다", () => {
    expect(TIMELAPSE_SPOOL_INTERVAL_MS).toBeCloseTo(66.667, 3);
    expect(1000 / TIMELAPSE_SPOOL_INTERVAL_MS).toBeCloseTo(15, 10);
  });
});

describe("planDecimation — 상한 도달 시 절반 솎아내기", () => {
  it("상한 미만이면 null이다", () => {
    expect(planDecimation(names(899))).toBeNull();
    expect(planDecimation(names(0))).toBeNull();
  });

  it("900장에서 홀수 인덱스 450장을 버리고 450장을 남긴다", () => {
    const plan = planDecimation(names(TIMELAPSE_SPOOL_MAX_FRAMES));
    expect(plan).not.toBeNull();
    expect(plan!.remove).toHaveLength(450);
    expect(plan!.keptCount).toBe(450);
    expect(plan!.remove[0]).toBe("00001.jpg");
    expect(plan!.remove[1]).toBe("00003.jpg");
  });

  it("남는 파일명이 정렬 순서를 유지한다(재번호를 매기지 않는다)", () => {
    const all = names(10);
    const plan = planDecimation(all, 10)!;
    const kept = all.filter((n) => !plan.remove.includes(n));
    expect(kept).toEqual(["00000.jpg", "00002.jpg", "00004.jpg", "00006.jpg", "00008.jpg"]);
    // 문자열 정렬 = 시간 정렬이 삭제 후에도 성립해야 이후 프레임을 이어 붙일 수 있다.
    expect([...kept].sort()).toEqual(kept);
  });

  it("홀수 개수에서도 마지막(최신) 프레임이 남는다", () => {
    const all = names(9);
    const plan = planDecimation(all, 9)!;
    expect(plan.keptCount).toBe(5);
    expect(plan.remove).not.toContain("00008.jpg");
  });
});

describe("decimatedInterval — 솎아낸 뒤 간격", () => {
  it("간격이 2배가 된다", () => {
    expect(decimatedInterval(TIMELAPSE_SPOOL_INTERVAL_MS)).toBeCloseTo(133.333, 3);
    expect(decimatedInterval(100)).toBe(200);
    expect(TIMELAPSE_SPOOL_DECIMATION_FACTOR).toBe(2);
  });

  it("반복 적용할 수 있다(2회 솎아내면 4배)", () => {
    expect(decimatedInterval(decimatedInterval(50))).toBe(200);
  });
});
