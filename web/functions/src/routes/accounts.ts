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
  validatePin,
  validateRole,
} from "../domain/validation";
import { asyncHandler } from "../http/async";
import { requireBearer, requirePower } from "../http/auth";
import { HttpError } from "../http/errors";
import {
  changePassword,
  createAccount,
  deleteAccount,
  getQrUsage,
  listAccounts,
  resetOtherPin,
  setEmail,
  setOwnPin,
  setRole,
  verifyPin,
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

  // GET /accounts/me/qr-usage  (로그인) — 본인 QR 사용 게이트 상태(it13 §5.3).
  // 파라미터 라우트(/:id/...)보다 먼저 등록해 "me"가 :id로 잡히지 않게 한다.
  router.get(
    "/me/qr-usage",
    asyncHandler(async (req, res) => {
      res.status(200).json(await getQrUsage(req.principal!));
    })
  );

  // it14: PIN 라우트. "me/pin*"은 파라미터 라우트(/:id/pin)보다 먼저 등록(me가 :id로 잡히지 않게).

  // POST /accounts/me/pin/verify  (로그인) — {pin} → 설정 진입 게이트 검증(E1).
  //   일치 200 {ok:true} | 불일치 401 | PIN 미설정 409(클라가 최초 설정 플로우 유도).
  router.post(
    "/me/pin/verify",
    asyncHandler(async (req, res) => {
      const pinRes = validatePin(req.body?.pin);
      if (!pinRes.ok) throw HttpError.invalid(pinRes.error);

      const result = await verifyPin(req.principal!.id, pinRes.value);
      if (!result.ok) {
        if (result.reason === "unset") {
          throw HttpError.conflict("설정 진입 PIN이 설정되지 않았습니다.");
        }
        throw HttpError.unauthorized("PIN이 일치하지 않습니다.");
      }
      res.status(200).json({ ok: true });
    })
  );

  // PUT /accounts/me/pin  (로그인) — {newPin, currentPin?} → 본인 PIN 설정/변경(E2).
  //   이미 PIN 있으면 currentPin 확인 필수(불일치 401), 미설정이면 최초 설정(currentPin 불요). → 204.
  router.put(
    "/me/pin",
    asyncHandler(async (req, res) => {
      const newPinRes = validatePin(req.body?.newPin);
      if (!newPinRes.ok) throw HttpError.invalid(newPinRes.error);

      // currentPin은 선택(최초 설정 시 생략). 값이 주어지면 형식 검증(불일치는 setOwnPin이 401).
      let currentPin: string | null = null;
      const rawCurrent = req.body?.currentPin;
      if (rawCurrent !== undefined && rawCurrent !== null && rawCurrent !== "") {
        const curRes = validatePin(rawCurrent);
        if (!curRes.ok) throw HttpError.invalid(curRes.error);
        currentPin = curRes.value;
      }

      await setOwnPin(req.principal!.id, currentPin, newPinRes.value);
      res.status(204).end();
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

  // PATCH /accounts/{id}/role  (파워: manager+admin) — {role} → 204
  // it13: 라우트는 requirePower로 열고(user/temp_user 조기 차단), 세부 매트릭스는 setRole이 강제.
  router.patch(
    "/:id/role",
    requirePower(),
    asyncHandler(async (req, res) => {
      const idRes = validateAccountId(req.params.id);
      if (!idRes.ok) throw HttpError.invalid(idRes.error);
      const roleRes = validateRole(req.body?.role);
      if (!roleRes.ok) throw HttpError.invalid(roleRes.error);

      await setRole(idRes.value, roleRes.value, req.principal!);
      res.status(204).end();
    })
  );

  // PUT /accounts/{id}/pin  (로그인, 권한 기반) — {newPin} → 타 계정 PIN 재설정(E3).
  //   canManage(actor.role, targetRole) 강제(위반 403). 자기 자신 대상은 400(본인은 E2 사용). → 204.
  router.put(
    "/:id/pin",
    asyncHandler(async (req, res) => {
      const idRes = validateAccountId(req.params.id);
      if (!idRes.ok) throw HttpError.invalid(idRes.error);
      const newPinRes = validatePin(req.body?.newPin);
      if (!newPinRes.ok) throw HttpError.invalid(newPinRes.error);

      // 자기 자신은 E2(현재 PIN 확인 경로)를 사용해야 한다.
      if (idRes.value === req.principal!.id) {
        throw HttpError.invalid("본인 PIN은 이 경로로 변경할 수 없습니다(본인 PIN 변경 경로 사용).");
      }

      await resetOtherPin(idRes.value, newPinRes.value, req.principal!);
      res.status(204).end();
    })
  );

  return router;
}
