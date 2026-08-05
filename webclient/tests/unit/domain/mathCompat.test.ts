import { describe, expect, it } from "vitest";
import { clamp, intDiv, roundHalfToEven } from "@domain/mathCompat";

describe("roundHalfToEven — C# Math.Round(double) 대응", () => {
  it("중간값은 짝수 쪽으로 간다(JS Math.round와 다르다)", () => {
    expect(roundHalfToEven(0.5)).toBe(0);
    expect(roundHalfToEven(1.5)).toBe(2);
    expect(roundHalfToEven(2.5)).toBe(2);
    expect(roundHalfToEven(3.5)).toBe(4);
    expect(roundHalfToEven(66.5)).toBe(66);
    expect(roundHalfToEven(67.5)).toBe(68);
    expect(roundHalfToEven(150.5)).toBe(150);
    expect(roundHalfToEven(196.5)).toBe(196);
  });

  it("음수 중간값도 C#과 같다", () => {
    // C#은 -0.0(음의 0)을 돌려주고 여기서는 +0을 돌려준다. 모든 소비자가 픽셀 정수로 쓰므로
    // 값이 같으면 충분하다(`0 === -0`). 부호 있는 0에 의존하는 코드는 도메인에 없다.
    expect(roundHalfToEven(-0.5)).toBe(0);
    expect(roundHalfToEven(-1.5)).toBe(-2);
    expect(roundHalfToEven(-2.5)).toBe(-2);
    expect(roundHalfToEven(-3.5)).toBe(-4);
  });

  it("중간값이 아니면 통상 반올림이다", () => {
    expect(roundHalfToEven(0.4)).toBe(0);
    expect(roundHalfToEven(0.6)).toBe(1);
    expect(roundHalfToEven(-0.4)).toBe(0);
    expect(roundHalfToEven(-0.6)).toBe(-1);
    expect(roundHalfToEven(62.499999)).toBe(62);
    expect(roundHalfToEven(62.500001)).toBe(63);
  });

  it("정수는 그대로다", () => {
    expect(roundHalfToEven(0)).toBe(0);
    expect(roundHalfToEven(7)).toBe(7);
    expect(roundHalfToEven(-7)).toBe(-7);
  });

  it("유한하지 않은 값은 그대로 돌려준다(크래시 금지)", () => {
    expect(roundHalfToEven(Number.NaN)).toBeNaN();
    expect(roundHalfToEven(Number.POSITIVE_INFINITY)).toBe(Number.POSITIVE_INFINITY);
    expect(roundHalfToEven(Number.NEGATIVE_INFINITY)).toBe(Number.NEGATIVE_INFINITY);
  });

  it("JS Math.round와 실제로 갈리는 지점이 있다(대응표가 필요한 이유)", () => {
    const divergent = [0.5, 2.5, 66.5, 150.5, 196.5, -1.5, -3.5];
    const differences = divergent.filter((v) => roundHalfToEven(v) !== Math.round(v));
    expect(differences.length).toBeGreaterThan(0);
  });
});

describe("clamp — C# Math.Clamp 대응", () => {
  it("범위 안·밖을 보정한다", () => {
    expect(clamp(5, 1, 10)).toBe(5);
    expect(clamp(0, 1, 10)).toBe(1);
    expect(clamp(11, 1, 10)).toBe(10);
    expect(clamp(1, 1, 10)).toBe(1);
    expect(clamp(10, 1, 10)).toBe(10);
  });

  it("min > max이면 예외가 아니라 min을 돌려준다(도메인은 크래시하지 않는다)", () => {
    expect(clamp(5, 10, 1)).toBe(10);
  });
});

describe("intDiv — C# 정수 나눗셈(0 방향 절단) 대응", () => {
  it("양수는 floor와 같고 음수는 다르다", () => {
    expect(intDiv(7, 2)).toBe(3);
    expect(intDiv(-7, 2)).toBe(-3); // floor는 -4
    expect(Math.floor(-7 / 2)).toBe(-4);
    expect(intDiv(0, 5)).toBe(0);
  });
});
