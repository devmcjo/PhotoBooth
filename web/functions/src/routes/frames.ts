/**
 * 프레임 라우트 — default 조회/user 조회/저장/삭제 (설계 §6.2 F1~F4).
 *
 * - GET /frames/default : 공개(API키). 게스트도 기본 프레임을 본다.
 * - GET /frames?userId= : Bearer. 본인 프레임만(파워는 임의 조회 허용).
 * - POST /frames        : Bearer(파워). 공용 기본 프레임 생성 + 서명 URL 발급.
 * - DELETE /frames/{id} : Bearer(파워).
 */
import { Router } from "express";
import {
  validateAccountId,
  validateFrameName,
  validateImageSize,
  validateSlots,
} from "../domain/validation";
import { isPower } from "../domain/roles";
import { asyncHandler } from "../http/async";
import { requireApiKey, requireBearer, requirePower } from "../http/auth";
import { HttpError } from "../http/errors";
import {
  deleteFrame,
  getDefaultFrames,
  getUserFrames,
  saveFrame,
} from "../services/frames";

export function framesRouter(): Router {
  const router = Router();

  // GET /frames/default  (API키) — frame[] (imageUrl 포함)
  router.get(
    "/default",
    requireApiKey(),
    asyncHandler(async (_req, res) => {
      res.status(200).json(await getDefaultFrames());
    })
  );

  // GET /frames?userId=  (Bearer) — 본인만(파워는 임의 계정 조회 허용)
  router.get(
    "/",
    requireBearer(),
    asyncHandler(async (req, res) => {
      const raw = req.query.userId;
      const userId = typeof raw === "string" ? raw : "";
      const idRes = validateAccountId(userId);
      if (!idRes.ok) throw HttpError.invalid("userId가 올바르지 않습니다.");

      const actor = req.principal!;
      if (idRes.value !== actor.id && !isPower(actor.role)) {
        throw HttpError.forbidden("다른 계정의 프레임을 조회할 수 없습니다.");
      }
      res.status(200).json(await getUserFrames(idRes.value));
    })
  );

  // POST /frames  (Bearer 파워) — {name, isDefault, imageSize, slots} → 201 {frame, upload}
  router.post(
    "/",
    requireBearer(),
    requirePower(),
    asyncHandler(async (req, res) => {
      const nameRes = validateFrameName(req.body?.name);
      if (!nameRes.ok) throw HttpError.invalid(nameRes.error);
      const sizeRes = validateImageSize(req.body?.imageSize);
      if (!sizeRes.ok) throw HttpError.invalid(sizeRes.error);
      const slotsRes = validateSlots(req.body?.slots);
      if (!slotsRes.ok) throw HttpError.invalid(slotsRes.error);

      // 파워가 만드는 공용 기본 프레임: userId=null, isDefault=true 강제(설계 §5.3·계약 §2.2).
      // user 커스텀 프레임은 it8 A2로 로컬 전용 → 서버는 공용 기본만 생성한다.
      const result = await saveFrame({
        name: nameRes.value,
        isDefault: true,
        imageSize: sizeRes.value,
        slots: slotsRes.value,
        userId: null,
        contentType: "image/png",
      });
      res.status(201).json(result);
    })
  );

  // DELETE /frames/{id}  (Bearer 파워) — 200 {deleted:bool}
  router.delete(
    "/:id",
    requireBearer(),
    requirePower(),
    asyncHandler(async (req, res) => {
      const frameId = req.params.id;
      if (typeof frameId !== "string" || frameId.length === 0) {
        throw HttpError.invalid("프레임 id가 필요합니다.");
      }
      const deleted = await deleteFrame(frameId);
      res.status(200).json({ deleted });
    })
  );

  return router;
}
