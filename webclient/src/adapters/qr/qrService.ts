import qrcode from "qrcode-generator";
import { planQrRender } from "@domain/upload/qrRenderPlan";
import { logger } from "@adapters/storage/logStore";

/**
 * QR 생성·렌더 — 03 §9 · VF-13
 *
 * ⚠️ 오류정정 레벨은 **Q**로 고정한다. Windows `QrService.cs`의 `ECCLevel.Q`와 같아야 한다
 *    (같은 URL이 두 클라이언트에서 같은 밀도로 나와야 현장 스캔 경험이 일정하다).
 * ⚠️ 배경은 **흰색 고정**이다. 다크모드에서도 반전하지 않는다 — 반전 QR을 못 읽는 스캐너가 있다.
 * ⚠️ 라이브러리의 `createImgTag`/`createSvgTag`(HTML 문자열)를 **쓰지 않는다.** canvas에 직접
 *    그려서 `innerHTML` 경로를 아예 만들지 않는다.
 */

/** ⚠️ 바꾸면 정적 테스트가 실패한다(VF-13). */
export const QR_ECC_LEVEL = "Q" as const;

/** 키오스크 표시 기본 크기(px). **표시 파라미터**이고 계약이 아니다(03 §9). */
export const QR_TARGET_PX = 640;

export interface QrMatrix {
  readonly moduleCount: number;
  isDark(row: number, col: number): boolean;
}

/**
 * QR 모듈 행렬 생성. **실패는 예외가 아니라 `null`**이다(용량 초과 등).
 * 화면은 `null`이면 "QR을 만들 수 없습니다"로 축소하고 [기기에 저장]을 안내한다.
 *
 * `typeNumber: 0`은 자동 선택이다 — 다운로드 페이지 URL(약 90자)은 type 6~7에 들어간다.
 */
export function createQrMatrix(text: string): QrMatrix | null {
  try {
    const qr = qrcode(0, QR_ECC_LEVEL);
    qr.addData(text);
    qr.make();

    const moduleCount = qr.getModuleCount();
    if (moduleCount <= 0) return null;

    return { moduleCount, isDark: (row, col) => qr.isDark(row, col) };
  } catch (err) {
    logger.warn("QR 렌더 실패", {
      // ⚠️ QR 내용(다운로드 페이지 URL)은 남기지 않는다 — URL 자체가 capability다.
      reason: err instanceof Error ? err.message : String(err),
    });
    return null;
  }
}

/**
 * 흰 배경 + 검정 모듈 + 여백 4모듈로 canvas에 그린다.
 * 성공 여부를 boolean으로 돌려준다(2D 컨텍스트 부재 등은 `false`, 예외를 던지지 않는다).
 */
export function drawQrToCanvas(
  canvas: HTMLCanvasElement,
  matrix: QrMatrix,
  targetPx: number = QR_TARGET_PX,
): boolean {
  try {
    const context = canvas.getContext("2d");
    if (context === null) {
      logger.warn("QR 렌더 실패", { reason: "2D 컨텍스트를 얻을 수 없습니다." });
      return false;
    }

    const plan = planQrRender(matrix.moduleCount, targetPx);
    canvas.width = plan.canvasPx;
    canvas.height = plan.canvasPx;

    context.fillStyle = "#ffffff";
    context.fillRect(0, 0, plan.canvasPx, plan.canvasPx);

    context.fillStyle = "#000000";
    for (let row = 0; row < matrix.moduleCount; row++) {
      for (let col = 0; col < matrix.moduleCount; col++) {
        if (!matrix.isDark(row, col)) continue;
        context.fillRect(
          plan.quietPx + col * plan.modulePx,
          plan.quietPx + row * plan.modulePx,
          plan.modulePx,
          plan.modulePx,
        );
      }
    }

    logger.info("QR 렌더", {
      moduleCount: matrix.moduleCount,
      modulePx: plan.modulePx,
      canvasPx: plan.canvasPx,
    });
    return true;
  } catch (err) {
    logger.warn("QR 렌더 실패", {
      reason: err instanceof Error ? err.message : String(err),
    });
    return false;
  }
}
