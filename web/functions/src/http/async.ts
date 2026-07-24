/**
 * async 라우트 핸들러 래퍼 — Promise rejection을 Express 에러 미들웨어로 전파한다.
 *
 * Express 4는 async 핸들러의 미처리 rejection을 자동 포착하지 못하므로, catch로
 * next(err)에 넘겨 조용한 실패(hang)를 막는다.
 */
import { NextFunction, Request, RequestHandler, Response } from "express";

export function asyncHandler(
  fn: (req: Request, res: Response, next: NextFunction) => Promise<unknown>
): RequestHandler {
  return (req, res, next) => {
    fn(req, res, next).catch(next);
  };
}
