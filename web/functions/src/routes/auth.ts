/**
 * 인증 라우트 — 로그인 + 이메일 인증 + 비밀번호 재설정 (설계 §6.2·§8.2·§8.4).
 *
 * 로그인 성공 시 JWT 발급. 인증/재설정은 로그인 전 상태이므로 API 키 게이트만 요구(Bearer 불요).
 * request 계열은 열거 방지를 위해 존재/상태 무관 동일 202로 응답한다(§12).
 */
import { Router } from "express";
import { loadConfig } from "../config";
import { issueToken } from "../domain/jwt";
import {
  validateAccountId,
  validateAuthCode,
  validateCodeVerifier,
  validateEmail,
  validateLoopbackRedirectUri,
  validateNonce,
  validatePassword,
  validateVerificationCode,
} from "../domain/validation";
import { asyncHandler } from "../http/async";
import { requireApiKey } from "../http/auth";
import { HttpError } from "../http/errors";
import {
  confirmEmailVerificationByCode,
  confirmEmailVerificationByToken,
  confirmPasswordResetByCode,
  confirmPasswordResetByToken,
  login,
  loginWithGoogleEmail,
  registerSelf,
  requestEmailVerification,
  requestPasswordReset,
} from "../services/accounts";
import {
  GoogleAuthError,
  verifyGoogleCodeAndGetEmail,
} from "../services/googleAuth";

/** idOrEmail/token 등 자유 문자열 필드의 최소 검증(비어있지 않은 문자열·과길이 방어). */
function nonEmptyString(value: unknown, max = 254): string | null {
  if (typeof value !== "string") return null;
  const v = value.trim();
  if (v.length === 0 || v.length > max) return null;
  return v;
}

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

  // POST /auth/register  (API키, Bearer 불요) — self-signup (설계 §2.2 B-BE-2, BE-3).
  //   body {id, password, email?} → role="user" **서버 강제**(클라 role 지정 불가).
  //   성공 201 {token, expiresIn, user} — 가입 즉시 로그인(JWT 발급, USER-DECISION D-B3).
  //   id 중복 → 409(UX상 사유 노출 허용). email verified 충돌 → 409(초과 메시지). 형식 오류 → 400.
  router.post(
    "/register",
    requireApiKey(),
    asyncHandler(async (req, res) => {
      const idRes = validateAccountId(req.body?.id);
      if (!idRes.ok) throw HttpError.invalid(idRes.error);
      const pwRes = validatePassword(req.body?.password);
      if (!pwRes.ok) throw HttpError.invalid(pwRes.error);

      // email은 선택. 있으면 형식 검증(소문자 정규화값 사용). null/undefined면 생략.
      let email: string | null = null;
      if (req.body?.email !== undefined && req.body?.email !== null && req.body?.email !== "") {
        const emailRes = validateEmail(req.body.email);
        if (!emailRes.ok) throw HttpError.invalid(emailRes.error);
        email = emailRes.value;
      }

      // role은 body로 받지 않는다(항상 user 강제). registerSelf가 id 중복 409·email 충돌 409 처리.
      const user = await registerSelf(idRes.value, pwRes.value, email);

      const cfg = loadConfig();
      const token = issueToken(
        { id: user.id, role: "user" },
        cfg.jwtSecret,
        cfg.jwtExpiresInSeconds
      );
      res.status(201).json({
        token,
        expiresIn: cfg.jwtExpiresInSeconds,
        user,
      });
    })
  );

  // POST /auth/google  (API키) — item1b Google SSO (설계 §5).
  //   body {code, codeVerifier, redirectUri, nonce?}
  //   → code 교환 + id_token 검증 → email 매핑(등록·검증된 계정만) → login과 동일 {token, expiresIn, user}.
  //   비활성(미구성) → 501. 형식 오류 → 400. 매핑 실패/미검증/Google 오류 → 401 일반화(열거 방지, §6.4).
  router.post(
    "/google",
    requireApiKey(),
    asyncHandler(async (req, res) => {
      const cfg = loadConfig();
      // Google SSO 미구성이면 명확한 구성 오류로 응답(sendgrid와 동일한 "사용 시에만 강제" 원칙, §8.2).
      if (!cfg.googleOAuthEnabled) {
        throw HttpError.notImplemented("Google 로그인이 구성되지 않았습니다.");
      }

      // 입력 형식 검증(SSRF·오용 차단). 형식 오류는 400.
      const codeRes = validateAuthCode(req.body?.code);
      if (!codeRes.ok) throw HttpError.invalid(codeRes.error);
      const verifierRes = validateCodeVerifier(req.body?.codeVerifier);
      if (!verifierRes.ok) throw HttpError.invalid(verifierRes.error);
      const redirectRes = validateLoopbackRedirectUri(req.body?.redirectUri);
      if (!redirectRes.ok) throw HttpError.invalid(redirectRes.error);

      // nonce는 선택: 있으면 형식 검증 후 id_token nonce 대조에 사용(§8.4).
      let nonce: string | undefined;
      if (req.body?.nonce !== undefined && req.body?.nonce !== null) {
        const nonceRes = validateNonce(req.body.nonce);
        if (!nonceRes.ok) throw HttpError.invalid(nonceRes.error);
        nonce = nonceRes.value;
      }

      // code 교환 + id_token 검증 → 검증된 email(소문자). 실패는 GoogleAuthError.
      let email: string;
      try {
        email = await verifyGoogleCodeAndGetEmail(
          {
            clientId: cfg.googleOAuthClientId,
            clientSecret: cfg.googleOAuthClientSecret,
            allowedHd: cfg.googleAllowedHd,
          },
          {
            code: codeRes.value,
            codeVerifier: verifierRes.value,
            redirectUri: redirectRes.value,
            nonce,
          }
        );
      } catch (err) {
        // Google 검증 실패는 사유를 로그에만 남기고(토큰·email 미포함), 일반화 401(열거 방지, §6.4·§8.6).
        if (err instanceof GoogleAuthError) {
          console.warn("Google 로그인 검증 실패:", err.message);
          throw HttpError.unauthorized(
            "이 Google 계정으로 로그인할 수 없습니다. 관리자에게 등록을 요청하세요."
          );
        }
        throw err;
      }

      // 계정 매핑(등록·검증된 계정만). 실패는 일반화 401(사유는 로그만).
      const result = await loginWithGoogleEmail(email);
      if (!result) {
        console.warn("Google 로그인: 매핑되는 등록·검증 계정 없음.");
        throw HttpError.unauthorized(
          "이 Google 계정으로 로그인할 수 없습니다. 관리자에게 등록을 요청하세요."
        );
      }

      // login과 완전히 동일한 JWT·응답 형식(신규 인증 상태 0, §5.4).
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

  // ── 이메일 인증 (§8.2) ──

  // POST /auth/verify-email/request  (API키) — {idOrEmail} → 202(열거 방지, 재발송 겸용)
  router.post(
    "/verify-email/request",
    requireApiKey(),
    asyncHandler(async (req, res) => {
      const idOrEmail = nonEmptyString(req.body?.idOrEmail);
      if (idOrEmail) {
        await requestEmailVerification(idOrEmail);
      }
      // 입력이 비어도 동일 202(형식 노출 최소화).
      res.status(202).json({ accepted: true });
    })
  );

  // POST /auth/verify-email/confirm  (API키) — {token} 또는 {id, code} → 200 {verified} | 400/401
  router.post(
    "/verify-email/confirm",
    requireApiKey(),
    asyncHandler(async (req, res) => {
      const token = nonEmptyString(req.body?.token, 512);
      if (token) {
        // 링크 경로: token은 `{tokenId}.{secret}` 결합값. userId는 body.id로 전달받는다.
        const idRes = validateAccountId(req.body?.id);
        if (!idRes.ok) throw HttpError.invalid("계정 식별자가 필요합니다.");
        const result = await confirmEmailVerificationByToken(idRes.value, token);
        if (!result.verified) {
          // taken(이미 다른 계정이 verified) → 409 + 초과 메시지, 그 외 → 기존 401(§3.4).
          if (result.reason === "taken") {
            throw HttpError.conflict("해당 이메일로 생성 가능한 계정 수를 초과하였습니다.");
          }
          throw HttpError.unauthorized("인증 토큰이 유효하지 않거나 만료되었습니다.");
        }
        res.status(200).json({ verified: true });
        return;
      }

      // 코드 경로: {id, code}.
      const idRes = validateAccountId(req.body?.id);
      if (!idRes.ok) throw HttpError.invalid(idRes.error);
      const codeRes = validateVerificationCode(req.body?.code);
      if (!codeRes.ok) throw HttpError.invalid(codeRes.error);

      const result = await confirmEmailVerificationByCode(idRes.value, codeRes.value);
      if (!result.verified) {
        // taken → 409 + 초과 메시지, 그 외 → 기존 401(§3.4).
        if (result.reason === "taken") {
          throw HttpError.conflict("해당 이메일로 생성 가능한 계정 수를 초과하였습니다.");
        }
        throw HttpError.unauthorized("인증 코드가 올바르지 않거나 만료되었습니다.");
      }
      res.status(200).json({ verified: true });
    })
  );

  // ── 비밀번호 재설정 (§8.4) ──

  // POST /auth/password-reset/request  (API키) — {idOrEmail} → 항상 202(열거 방지)
  router.post(
    "/password-reset/request",
    requireApiKey(),
    asyncHandler(async (req, res) => {
      const idOrEmail = nonEmptyString(req.body?.idOrEmail);
      if (idOrEmail) {
        await requestPasswordReset(idOrEmail);
      }
      res.status(202).json({ accepted: true });
    })
  );

  // POST /auth/password-reset/confirm  (API키)
  //   {token, newPassword} 또는 {idOrEmail, code, newPassword} → 200 {reset} | 400/401
  router.post(
    "/password-reset/confirm",
    requireApiKey(),
    asyncHandler(async (req, res) => {
      const pwRes = validatePassword(req.body?.newPassword);
      if (!pwRes.ok) throw HttpError.invalid(pwRes.error);

      const token = nonEmptyString(req.body?.token, 512);
      if (token) {
        // 링크 경로: token + userId(body.id).
        const idRes = validateAccountId(req.body?.id);
        if (!idRes.ok) throw HttpError.invalid("계정 식별자가 필요합니다.");
        const ok = await confirmPasswordResetByToken(idRes.value, token, pwRes.value);
        if (!ok) {
          throw HttpError.unauthorized("재설정 토큰이 유효하지 않거나 만료되었습니다.");
        }
        res.status(200).json({ reset: true });
        return;
      }

      // 코드 경로: {idOrEmail, code, newPassword}.
      const idOrEmail = nonEmptyString(req.body?.idOrEmail);
      if (!idOrEmail) throw HttpError.invalid("idOrEmail이 필요합니다.");
      const codeRes = validateVerificationCode(req.body?.code);
      if (!codeRes.ok) throw HttpError.invalid(codeRes.error);

      const ok = await confirmPasswordResetByCode(idOrEmail, codeRes.value, pwRes.value);
      if (!ok) {
        throw HttpError.unauthorized("재설정 코드가 올바르지 않거나 만료되었습니다.");
      }
      res.status(200).json({ reset: true });
    })
  );

  return router;
}
