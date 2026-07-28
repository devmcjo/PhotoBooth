/**
 * 전역 설정 라우트 — TempUser QR 한도 조회·수정 (설계 §5.4).
 *
 * GET /config/temp-user-limits  (로그인) — 현재 한도(문서 부재 시 기본 48h/30회). 표시용, 모든 로그인 사용자.
 * PATCH /config/temp-user-limits (admin) — {qrHours?, qrCount?} 부분 갱신. 범위 검증 후 저장.
 *
 * 사용량(계정별)은 /accounts/me/qr-usage, 한도(전역·1쌍)는 여기. Admin만 수정(requireAdmin + 서버 재검증).
 */
import { Router } from "express";
import { validateQrCount, validateQrHours } from "../domain/validation";
import { asyncHandler } from "../http/async";
import { requireAdmin, requireBearer } from "../http/auth";
import { HttpError } from "../http/errors";
import {
  loadTempUserLimits,
  setTempUserLimits,
  TempUserLimitsPatch,
} from "../services/config";

export function configRouter(): Router {
  const router = Router();

  // 모든 config 엔드포인트는 로그인 필수(조회는 표시용, 수정은 admin).
  router.use(requireBearer());

  // GET /config/temp-user-limits — 현재 전역 한도(기본값 폴백).
  router.get(
    "/temp-user-limits",
    asyncHandler(async (_req, res) => {
      res.status(200).json(await loadTempUserLimits());
    })
  );

  // PATCH /config/temp-user-limits  (admin) — {qrHours?, qrCount?} → 200 갱신된 한도.
  router.patch(
    "/temp-user-limits",
    requireAdmin(),
    asyncHandler(async (req, res) => {
      const patch: TempUserLimitsPatch = {};

      // 둘 다 선택. 최소 1개는 있어야 의미가 있다(빈 patch 거부).
      const rawHours = req.body?.qrHours;
      if (rawHours !== undefined && rawHours !== null) {
        const r = validateQrHours(rawHours);
        if (!r.ok) throw HttpError.invalid(r.error);
        patch.qrHours = r.value;
      }
      const rawCount = req.body?.qrCount;
      if (rawCount !== undefined && rawCount !== null) {
        const r = validateQrCount(rawCount);
        if (!r.ok) throw HttpError.invalid(r.error);
        patch.qrCount = r.value;
      }
      if (patch.qrHours === undefined && patch.qrCount === undefined) {
        throw HttpError.invalid("qrHours 또는 qrCount 중 최소 하나가 필요합니다.");
      }

      res.status(200).json(await setTempUserLimits(patch));
    })
  );

  return router;
}
