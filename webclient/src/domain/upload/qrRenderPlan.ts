/**
 * QR 렌더 기하 — 순수 계산 (03 §9)
 *
 * 픽셀을 그리는 것은 어댑터(`adapters/qr/qrService.ts`)이고, 여기서는 **몇 픽셀로 그릴지**만 정한다.
 * 표시 파라미터라 계약은 아니지만, 여백·정수 배율은 **스캐너 인식률에 직결**되므로 규칙으로 고정한다.
 */

/** QR 여백(quiet zone) — **4모듈이 규격**이다(03 §9). 줄이면 스캐너가 인식하지 못한다. */
export const QR_QUIET_ZONE_MODULES = 4;

export interface QrRenderPlan {
  /** 모듈 1개의 픽셀 크기(정수 ≥ 1). */
  readonly modulePx: number;
  /** 캔버스 한 변 픽셀 = modulePx * (moduleCount + quiet*2). */
  readonly canvasPx: number;
  /** 좌·상 여백 픽셀 = modulePx * quiet. */
  readonly quietPx: number;
}

function floorOr(value: number, fallback: number, requirePositive: boolean): number {
  if (!Number.isFinite(value)) return fallback;
  if (requirePositive ? value <= 0 : value < 0) return fallback;
  return Math.floor(value);
}

/**
 * 표시 크기에서 **정수 배율** 모듈 픽셀을 정한다.
 *
 * 정수인 이유: 소수 배율은 모듈 경계가 반픽셀에 걸려 스캐너 인식률이 떨어진다.
 * `targetPx`가 너무 작아도 **최소 1px**을 보장한다(0이면 빈 캔버스가 된다).
 *
 * ⚠️ 어떤 입력에도 **던지지 않는다**(도메인은 방어적). `moduleCount <= 0`은
 *    `createQrMatrix`가 `null`을 준 경우라 화면이 이미 걸러낸다.
 */
export function planQrRender(
  moduleCount: number,
  targetPx: number,
  quietModules: number = QR_QUIET_ZONE_MODULES,
): QrRenderPlan {
  const quiet = floorOr(quietModules, QR_QUIET_ZONE_MODULES, false);
  const modules = floorOr(moduleCount, 0, true);

  // 그릴 것이 없다 — 1x1 빈 캔버스로 축소한다(0을 돌려주면 canvas가 무효 크기가 된다).
  if (modules <= 0) return { modulePx: 1, canvasPx: 1, quietPx: 0 };

  const total = modules + quiet * 2;
  const target = Number.isFinite(targetPx) && targetPx > 0 ? targetPx : 0;
  const modulePx = Math.max(1, Math.floor(target / total));

  return { modulePx, canvasPx: modulePx * total, quietPx: modulePx * quiet };
}
