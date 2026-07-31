/**
 * 용량 표기(순수) — 설정 [보관된 결과물] · 저장소 사용량 표시용 (05 §5.4)
 *
 * 1024 기준, 소수 **1자리**. 정수로 떨어지면 소수점을 붙이지 않는다("340 MB").
 * ⚠️ 판정(경고 배지)에는 쓰지 않는다 — 그것은 `isStorageLow`가 **정수 바이트끼리** 비교한다
 *    (15 §4 함정 #3). 이 함수는 **표시 전용**이다.
 */

const UNITS = ["B", "KB", "MB", "GB", "TB", "PB"] as const;
const STEP = 1024;

export function formatBytes(bytes: number): string {
  if (!Number.isFinite(bytes) || bytes <= 0) return "0 B";

  let value = bytes;
  let unitIndex = 0;
  while (value >= STEP && unitIndex < UNITS.length - 1) {
    value /= STEP;
    unitIndex++;
  }

  // 바이트 단위는 소수를 만들지 않는다(1023.5 B 같은 표기는 의미가 없다).
  if (unitIndex === 0) return `${Math.round(value)} B`;

  const rounded = Math.round(value * 10) / 10;
  const text = Number.isInteger(rounded) ? String(rounded) : rounded.toFixed(1);
  return `${text} ${UNITS[unitIndex]}`;
}
