/**
 * 업로드 라우트 — prepare(서명URL) / commit(resultSession 생성) (설계 §6.2 U1·U2).
 *
 * 게스트(촬영자) 흐름이므로 API키 게이트. 파일 바이트는 함수를 경유하지 않고
 * 클라가 prepare 응답의 서명 URL로 직접 PUT한다(설계 §5.4-A).
 */
import { Router } from "express";
import { isValidSessionId } from "../domain/session";
import { validateRetentionHours, validateUploadFile } from "../domain/validation";
import { asyncHandler } from "../http/async";
import { requireApiKey } from "../http/auth";
import { HttpError } from "../http/errors";
import { commitUpload, prepareUpload } from "../services/uploads";

export function uploadsRouter(): Router {
  const router = Router();

  router.use(requireApiKey());

  // POST /uploads/prepare — {sessionId, files:[{kind, ext, contentType}]} → {uploads[], bucket}
  router.post(
    "/prepare",
    asyncHandler(async (req, res) => {
      const sessionId = req.body?.sessionId;
      if (!isValidSessionId(sessionId)) {
        throw HttpError.invalid("sessionId 형식이 올바르지 않습니다.");
      }
      const rawFiles = req.body?.files;
      if (!Array.isArray(rawFiles) || rawFiles.length === 0) {
        throw HttpError.invalid("files 배열이 필요합니다(최소 1개).");
      }
      const files = [];
      for (const f of rawFiles) {
        const r = validateUploadFile(f);
        if (!r.ok) throw HttpError.invalid(r.error);
        files.push(r.value);
      }

      const result = await prepareUpload(sessionId, files);
      res.status(200).json(result);
    })
  );

  // POST /uploads/commit — {sessionId, finalImageUrl?, timelapseUrl?, retentionHours, downloadPageUrl} → 201 resultSession
  router.post(
    "/commit",
    asyncHandler(async (req, res) => {
      const sessionId = req.body?.sessionId;
      if (!isValidSessionId(sessionId)) {
        throw HttpError.invalid("sessionId 형식이 올바르지 않습니다.");
      }
      const retentionRes = validateRetentionHours(req.body?.retentionHours);
      if (!retentionRes.ok) throw HttpError.invalid(retentionRes.error);

      const downloadPageUrl = req.body?.downloadPageUrl;
      if (typeof downloadPageUrl !== "string" || downloadPageUrl.length === 0) {
        throw HttpError.invalid("downloadPageUrl이 필요합니다.");
      }

      const finalImageUrl =
        typeof req.body?.finalImageUrl === "string" ? req.body.finalImageUrl : null;
      const timelapseUrl =
        typeof req.body?.timelapseUrl === "string" ? req.body.timelapseUrl : null;

      const session = await commitUpload({
        sessionId,
        finalImageUrl,
        timelapseUrl,
        retentionHours: retentionRes.value,
        downloadPageUrl,
      });
      res.status(201).json(session);
    })
  );

  return router;
}
