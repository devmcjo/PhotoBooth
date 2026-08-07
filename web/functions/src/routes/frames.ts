/**
 * 프레임 라우트 — default 조회/user 조회/저장/삭제 (설계 §6.2 F1~F4).
 *
 * - GET /frames/default : 공개(API키). 게스트도 기본 프레임을 본다.
 * - GET /frames?userId= : Bearer. 본인 프레임만(파워는 임의 조회 허용).
 * - POST /frames        : Bearer(파워). 공용 기본 프레임 생성 + 서명 URL 발급.
 * - POST /frames/mine   : Bearer(고급 유저 이상). **개인 프레임** 생성 + 서명 URL 발급.
 * - DELETE /frames/{id} : Bearer(고급 유저 이상). 본인 소유는 본인이, 공용은 파워만.
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
import {
  requireApiKey,
  requireBearer,
  requireFrameWrite,
  requirePower,
} from "../http/auth";
import { HttpError } from "../http/errors";
import {
  deleteFrame,
  getDefaultFrames,
  getFrameOwnerId,
  getUserFrames,
  saveFrame,
  updateFrame,
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

  // POST /frames/mine  (Bearer + 프레임 저작 권한) — 개인 프레임 생성 + 서명 URL 발급
  //
  // 왜 POST /frames와 분리하는가(설계 S3): 기존 라우트는 "공용 기본 프레임 생성"이라는 의미가
  // 문서·테스트·클라에 고정돼 있고 게이트가 다르다(requirePower vs requireFrameWrite).
  // 한 라우트에서 body 값으로 분기하면 권한 오판 위험이 커진다 — it16이 두 권한 축을 분리한 정신과 같다.
  router.post(
    "/mine",
    requireBearer(),
    requireFrameWrite(),
    asyncHandler(async (req, res) => {
      const nameRes = validateFrameName(req.body?.name);
      if (!nameRes.ok) throw HttpError.invalid(nameRes.error);
      const sizeRes = validateImageSize(req.body?.imageSize);
      if (!sizeRes.ok) throw HttpError.invalid(sizeRes.error);
      const slotsRes = validateSlots(req.body?.slots);
      if (!slotsRes.ok) throw HttpError.invalid(slotsRes.error);

      // 소유자·공개 여부는 **서버가 강제**한다(클라 body 값 무시) — POST /frames가 반대 방향으로
      // 강제하는 것과 대칭. 개수 상한은 없고(D-10), 계정 내 이름 중복은 saveFrame이 409로 거부한다(S8).
      const result = await saveFrame({
        name: nameRes.value,
        isDefault: false,
        imageSize: sizeRes.value,
        slots: slotsRes.value,
        userId: req.principal!.id,
        contentType: "image/png",
      });
      res.status(201).json(result);
    })
  );

  // PUT /frames/{id}  (Bearer 파워) — 기존 공용 기본 프레임 업데이트(같은 id 덮어쓰기).
  // ⚠️ 앱은 이 라우트를 호출하지 않는다(설계 D-16 — 프레임 수정 기능 폐지). 운영/관리 도구 전용.
  // {name, imageSize, slots, replaceImage?} → 200 {frame, upload?}
  // POST /frames와 요청/응답 DTO 일관(클라 재사용). isDefault·userId는 서버가 보존한다.
  router.put(
    "/:id",
    requireBearer(),
    requirePower(),
    asyncHandler(async (req, res) => {
      const frameId = req.params.id;
      if (typeof frameId !== "string" || frameId.length === 0) {
        throw HttpError.invalid("프레임 id가 필요합니다.");
      }
      const nameRes = validateFrameName(req.body?.name);
      if (!nameRes.ok) throw HttpError.invalid(nameRes.error);
      const sizeRes = validateImageSize(req.body?.imageSize);
      if (!sizeRes.ok) throw HttpError.invalid(sizeRes.error);
      const slotsRes = validateSlots(req.body?.slots);
      if (!slotsRes.ok) throw HttpError.invalid(slotsRes.error);

      // replaceImage: 클라 diff 결과. 미지정/false면 메타만 갱신(이미지 보존, 서명 URL 미발급).
      const replaceImage = req.body?.replaceImage === true;

      const result = await updateFrame({
        frameId,
        name: nameRes.value,
        imageSize: sizeRes.value,
        slots: slotsRes.value,
        replaceImage,
        contentType: "image/png",
      });
      res.status(200).json(result);
    })
  );

  // DELETE /frames/{id}  (Bearer + 프레임 저작 권한) — 200 {deleted:bool}
  //
  // 설계 D-12: **본인 소유 프레임은 본인이 삭제**할 수 있다. 종전에는 requirePower라서
  // advanced_user가 자기 프레임을 서버에서 지울 방법이 없었다(개인 프레임이 서버 정본이 되면 치명적).
  // 공용 기본 프레임은 종전대로 power만 지운다.
  router.delete(
    "/:id",
    requireBearer(),
    requireFrameWrite(),
    asyncHandler(async (req, res) => {
      const frameId = req.params.id;
      if (typeof frameId !== "string" || frameId.length === 0) {
        throw HttpError.invalid("프레임 id가 필요합니다.");
      }

      const owner = await getFrameOwnerId(frameId);
      if (owner === undefined) {
        // 문서 없음 → 기존 계약대로 200 {deleted:false}(존재 여부를 오류로 노출하지 않는다).
        res.status(200).json({ deleted: false });
        return;
      }

      const actor = req.principal!;
      if (owner === null) {
        if (!isPower(actor.role)) {
          throw HttpError.forbidden("공용 기본 프레임은 파워 계정만 삭제할 수 있습니다.");
        }
      } else if (owner !== actor.id && !isPower(actor.role)) {
        throw HttpError.forbidden("다른 계정의 프레임을 삭제할 수 없습니다.");
      }

      const deleted = await deleteFrame(frameId);
      res.status(200).json({ deleted });
    })
  );

  return router;
}
