/**
 * 계정 라우트 — 생성/목록/비번변경/삭제/역할 (설계 §6.2 A2~A6).
 *
 * 모든 경로 Bearer 필수. 역할 게이트는 서버가 JWT의 role로 재검증(클라 전달 actingRole 무시, §5.2).
 */
import { Router } from "express";
import {
  validateAccountId,
  validateEmail,
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
  setEmail,
  setRole,
} from "../services/accounts";

export function accountsRouter(): Router {
  const router = Router();

  // 모든 계정 엔드포인트는 로그인 필수.
  router.use(requireBearer());

  // POST /accounts  (파워) — {id, password, role, email?} → 201 user
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

      // email은 선택(서버는 null 허용, 신규 계정 필수화는 클라 UI가 강제, §5.1).
      // 값이 주어졌을 때만 형식 검증한다(null/undefined/빈 문자열은 미수집으로 처리).
      let email: string | null = null;
      const rawEmail = req.body?.email;
      if (rawEmail !== undefined && rawEmail !== null && rawEmail !== "") {
        const emailRes = validateEmail(rawEmail);
        if (!emailRes.ok) throw HttpError.invalid(emailRes.error);
        email = emailRes.value;
      }

      // actingRole은 토큰에서(클라 전달 무시). requireBearer가 principal 주입.
      const actingRole = req.principal!.role;
      const user = await createAccount(
        idRes.value,
        pwRes.value,
        roleRes.value,
        email,
        actingRole
      );
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

  // PATCH /accounts/{id}/email  (본인/파워, 위계) — {email} → 204
  // email 변경 시 emailVerified=false로 리셋되고 verify 메일이 발송된다(§8.3).
  router.patch(
    "/:id/email",
    asyncHandler(async (req, res) => {
      const idRes = validateAccountId(req.params.id);
      if (!idRes.ok) throw HttpError.invalid(idRes.error);
      const emailRes = validateEmail(req.body?.email);
      if (!emailRes.ok) throw HttpError.invalid(emailRes.error);

      await setEmail(idRes.value, emailRes.value, req.principal!);
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
