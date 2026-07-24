/**
 * 헬스 체크 — GET /health (설계 §6.2). 인증 없음.
 * 클라의 IsInitialized(백엔드 도달 가능) 판정에 사용된다(설계 §5.1).
 */
import { Router } from "express";

export function healthRouter(): Router {
  const router = Router();
  router.get("/", (_req, res) => {
    res.status(200).json({ status: "ok", time: new Date().toISOString() });
  });
  return router;
}
