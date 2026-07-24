/**
 * 인증 라우트 — POST /auth/login (설계 §6.2 A1).
 * 자격 검증(해시) 성공 시 JWT 발급. 실패 시 401(현행 계약: 로그인 실패 = null → 401).
 */
import { Router } from "express";
import { loadConfig } from "../config";
import { issueToken } from "../domain/jwt";
import { validateAccountId, validatePassword } from "../domain/validation";
import { asyncHandler } from "../http/async";
import { requireApiKey } from "../http/auth";
import { HttpError } from "../http/errors";
import { login } from "../services/accounts";

export function authRouter(): Router {
  const router = Router();

  // POST /auth/login  (API키) — {id, password} → {token, expiresIn, user}
  router.post(
    "/login",
    requireApiKey(),
    asyncHandler(async (req, res) => {
      const idRes = validateAccountId(req.body?.id);
      const pwRes = validatePassword(req.body?.password);
      // 로그인 입력 형식 오류도 인증 실패로 처리(계정 존재 여부 노출 최소화).
      if (!idRes.ok || !pwRes.ok) {
        throw HttpError.unauthorized("아이디 또는 비밀번호가 올바르지 않습니다.");
      }

      const result = await login(idRes.value, pwRes.value);
      if (!result) {
        throw HttpError.unauthorized("아이디 또는 비밀번호가 올바르지 않습니다.");
      }

      const cfg = loadConfig();
      const token = issueToken(
        { id: result.id, role: result.role },
        cfg.jwtSecret,
        cfg.jwtExpiresInSeconds
      );
      res.status(200).json({
        token,
        expiresIn: cfg.jwtExpiresInSeconds,
        user: result.user,
      });
    })
  );

  return router;
}
