/**
 * C# 정수·반올림 연산 호환 유틸 — 04 §9 정수 연산 대응표
 *
 * ⚠️ **JS `Math.round`를 규격의 `round(...)`에 쓰면 안 된다.**
 * 규격 의사코드의 `round`는 C# `Math.Round(double)`이고 기본이 **은행가 반올림(half-to-even)** 이다.
 *   C#: Math.Round(66.5) = 66, Math.Round(67.5) = 68, Math.Round(-1.5) = -2
 *   JS : Math.round(66.5) = 67, Math.round(67.5) = 68, Math.round(-1.5) = -1
 * 중간값(.5)은 실제로 발생한다(`scaleSlots`의 `cx - newW/2`, 3:4 크롭의 `srcH×0.75` 등).
 * 슬롯 위치 "0px 오차" 계약(10 §4.2)이 이 함수에 걸려 있다.
 */

/** C# `Math.Round(double)` 대응 — 중간값은 짝수 쪽으로(banker's rounding). */
export function roundHalfToEven(value: number): number {
  if (!Number.isFinite(value)) return value;

  const floor = Math.floor(value);
  const fraction = value - floor;

  if (fraction > 0.5) return floor + 1;
  if (fraction < 0.5) return floor;

  // 정확히 .5 — 짝수 쪽을 택한다. (-2 % 2 === -0, -0 === 0 이므로 음수도 정상 판정)
  return floor % 2 === 0 ? floor : floor + 1;
}

/**
 * C# `Math.Clamp` 대응.
 * C#은 `min > max`에서 예외를 던지지만 도메인은 크래시를 만들지 않는다 — 그 경우 `min`을 반환한다.
 */
export function clamp(value: number, min: number, max: number): number {
  if (max < min) return min;
  if (value < min) return min;
  if (value > max) return max;
  return value;
}

/**
 * C# 정수 나눗셈(`int / int`) 대응 — **0 방향 절단**이다(`Math.floor`가 아니다).
 * 규격에서 피제수가 음수가 될 수 있는 곳에만 쓴다. 규격 대응표의 셀들은 모두 음수가 되지
 * 않으므로 `Math.floor`를 쓰며(표와 1:1), 이 함수는 부호가 섞일 수 있는 새 코드용이다.
 */
export function intDiv(numerator: number, denominator: number): number {
  return Math.trunc(numerator / denominator);
}
