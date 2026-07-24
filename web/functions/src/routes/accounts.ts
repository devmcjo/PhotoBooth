/**
 * 계정 라우트 — 생성/목록/비번변경/삭제/역할 (설계 §6.2 A2~A6).
 *
 * 모든 경로 Bearer 필수. 역할 게이트는 서버가 JWT의 role로 재검증(클라 전달 actingRole 무시, §5.2).
 */
import { Router } from "express";
import {
  validateAccountId,
  validatePassword,
  validateRole,
} from "../domain/validation";
import { asyncHandler } from "../http/async";
import { requireBearer, requirePower } from "../http/auth";
import { HttpError } from "../http/errors";
import {
  changePassword,
  createAccount,
  deleteAccount,
  listAccounts,
  setRole,
} from "../services/accounts";

export function accountsRouter(): Router {
  const router = Router();

  // 모든 계정 엔드포인트는 로그인 필수.
  router.use(requireBearer());

  // POST /accounts  (파워) — {id, password, role} → 201 user
  router.post(
    "/",
    requirePower(),
    asyncHandler(async (req, res) => {
      const idRes = validateAccountId(req.body?.id);
      if (!idRes.ok) throw HttpError.invalid(idRes.error);
      const pwRes = validatePassword(req.body?.password);
      if (!pwRes.ok) throw HttpError.invalid(pwRes.error);
      const roleRes = validateRole(req.body?.role);
      if (!roleRes.ok) throw HttpError.invalid(roleRes.error);

      // actingRole은 토큰에서(클라 전달 무시). requireBearer가 principal 주입.
      const actingRole = req.principal!.role;
      const user = await createAccount(idRes.value, pwRes.value, roleRes.value, actingRole);
      res.status(201).json(user);
    })
  );

  // GET /accounts  (파워) — user[]
  router.get(
    "/",
    requirePower(),
    asyncHandler(async (_req, res) => {
      res.status(200).json(await listAccounts());
    })
  );

  // PATCH /accounts/{id}/password  (본인/파워) — {newPassword} → 204
  router.patch(
    "/:id/password",
    asyncHandler(async (req, res) => {
      const idRes = validateAccountId(req.params.id);
      if (!idRes.ok) throw HttpError.invalid(idRes.error);
      const pwRes = validatePassword(req.body?.newPassword);
      if (!pwRes.ok) throw HttpError.invalid(pwRes.error);

      await changePassword(idRes.value, pwRes.value, req.principal!);
      res.status(204).end();
    })
  );

  // DELETE /accounts/{id}  (파워, 위계) — 204 (+cascade)
  router.delete(
    "/:id",
    requirePower(),
    asyncHandler(async (req, res) => {
      const idRes = validateAccountId(req.params.id);
      if (!idRes.ok) throw HttpError.invalid(idRes.error);
      if (idRes.value === req.principal!.id) {
        throw HttpError.forbidden("자기 자신은 삭제할 수 없습니다.");
      }
      await deleteAccount(idRes.value, req.principal!);
      res.status(204).end();
    })
  );

  // PATCH /accounts/{id}/role  (admin) — {role} → 204
  router.patch(
    "/:id/role",
    asyncHandler(async (req, res) => {
      const idRes = validateAccountId(req.params.id);
      if (!idRes.ok) throw HttpError.invalid(idRes.error);
      const roleRes = validateRole(req.body?.role);
      if (!roleRes.ok) throw HttpError.invalid(roleRes.error);

      await setRole(idRes.value, roleRes.value, req.principal!);
      res.status(204).end();
    })
  );

  return router;
}
