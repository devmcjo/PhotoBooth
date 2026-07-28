/**
 * Express 앱 조립 — 12개 엔드포인트를 라우터로 마운트 (설계 §6.2).
 *
 * 단일 함수(api)에 얹으므로 실제 URL은 `.../api/{path}` (예: .../api/auth/login).
 * JSON 파서 → 라우터 → 404 → 에러 미들웨어 순. 미처리 rejection은 asyncHandler가 next로 전파.
 */
import cors from "cors";
import express, { NextFunction, Request, Response } from "express";
import { HttpError, sendError } from "./http/errors";
import { authRouter } from "./routes/auth";
import { accountsRouter } from "./routes/accounts";
import { configRouter } from "./routes/config";
import { framesRouter } from "./routes/frames";
import { uploadsRouter } from "./routes/uploads";
import { healthRouter } from "./routes/health";

export function createApp(): express.Express {
  const app = express();

  // WPF(HttpClient)는 브라우저가 아니라 CORS 불필요하지만, Emulator 스모크(브라우저/도구)와
  // 향후 관리 콘솔 대비로 최소 CORS만 허용(설계 §11 CORS는 선택).
  app.use(cors({ origin: true }));
  app.use(express.json({ limit: "256kb" }));

  app.use("/auth", authRouter());
  app.use("/accounts", accountsRouter());
  app.use("/config", configRouter());
  app.use("/frames", framesRouter());
  app.use("/uploads", uploadsRouter());
  app.use("/health", healthRouter());

  // 404 — 매칭되는 라우트 없음.
  app.use((_req: Request, _res: Response, next: NextFunction) => {
    next(HttpError.notFound("엔드포인트를 찾을 수 없습니다."));
  });

  // 에러 미들웨어 — HttpError는 표준형으로, 그 외는 500으로 매핑(조용한 실패 금지).
  // eslint-disable-next-line @typescript-eslint/no-unused-vars
  app.use((err: unknown, _req: Request, res: Response, _next: NextFunction) => {
    if (err instanceof HttpError) {
      sendError(res, err);
      return;
    }
    // JSON 파싱 오류(express.json) → 400.
    if (err instanceof SyntaxError && "body" in err) {
      sendError(res, HttpError.invalid("요청 본문이 올바른 JSON이 아닙니다."));
      return;
    }
    console.error("처리되지 않은 오류:", err);
    sendError(res, HttpError.internal());
  });

  return app;
}
