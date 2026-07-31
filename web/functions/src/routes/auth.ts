/**
 * 인증 라우트 — Google SSO 단일 경로 (it15 설계 §5.4).
 *
 * it15에서 ID/PW 로그인·회원가입·이메일 인증·비밀번호 재설정 라우트를 전량 제거했다.
 * 남는 것은 `POST /google` 하나뿐이며, 로그인 전 상태이므로 API 키 게이트만 요구한다(Bearer 불요).
 * 제거된 경로는 410 스텁을 남기지 않는다 — app.ts의 404 핸들러가 처리한다(설계 §5.4 A4 판정).
 */
import { Router } from "express";
import { loadConfig } from "../config";
import { issueToken } from "../domain/jwt";
import {
  validateAuthCode,
  validateClientKind,
  validateCodeVerifier,
  validateNonce,
  validateRedirectUri,
} from "../domain/validation";
import { asyncHandler } from "../http/async";
import { requireApiKey } from "../http/auth";
import { HttpError } from "../http/errors";
import { loginWithGoogleEmail } from "../services/accounts";
import {
  GoogleAuthError,
  verifyGoogleCodeAndGetEmail,
} from "../services/googleAuth";

export function authRouter(): Router {
  const router = Router();

  // POST /auth/google  (API키) — item1b Google SSO (설계 §5, 매핑은 BE-2 재설계로 자동 생성/승격).
  //   body {code, codeVerifier, redirectUri, nonce?}
  //   → code 교환 + id_token 검증 → 검증된 email로 계정 자동 생성(temp_user)/매핑 → {token, expiresIn, user}.
  //   비활성(미구성) → 501. 형식 오류 → 400. Google 검증 실패(도메인·미검증 등) → 401 일반화(열거 방지, §6.4).
  router.post(
    "/google",
    requireApiKey(),
    asyncHandler(async (req, res) => {
      const cfg = loadConfig();
      // Google SSO 미구성이면 명확한 구성 오류로 응답("사용 시에만 강제" 원칙, §8.2).
      if (!cfg.googleOAuthEnabled) {
        throw HttpError.notImplemented("Google 로그인이 구성되지 않았습니다.");
      }

      // 입력 형식 검증(SSRF·오용 차단). 형식 오류는 400.
      const codeRes = validateAuthCode(req.body?.code);
      if (!codeRes.ok) throw HttpError.invalid(codeRes.error);
      const verifierRes = validateCodeVerifier(req.body?.codeVerifier);
      if (!verifierRes.ok) throw HttpError.invalid(verifierRes.error);
      // B2: 어느 OAuth 클라이언트로 교환할지 요청이 명시한다. 미지정 = desktop(하위 호환).
      const kindRes = validateClientKind(req.body?.clientKind);
      if (!kindRes.ok) throw HttpError.invalid(kindRes.error);
      // B1: loopback(데스크톱) 또는 허용 목록(웹)만 통과. 완전 일치.
      const redirectRes = validateRedirectUri(
        req.body?.redirectUri,
        cfg.oauthRedirectAllowlist
      );
      if (!redirectRes.ok) throw HttpError.invalid(redirectRes.error);

      // 요청한 종류가 구성되지 않았으면 구성 오류다(401로 감추지 않는다 — 운영자가 원인을 알아야 한다).
      const client = cfg.googleOAuthClients[kindRes.value];
      if (!client) {
        throw HttpError.notImplemented(
          `Google 로그인이 이 클라이언트 종류로 구성되지 않았습니다: ${kindRes.value}`
        );
      }

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
            clientId: client.clientId,
            clientSecret: client.clientSecret,
            allowedHd: cfg.googleAllowedHd,
            audiences: cfg.googleOAuthAudiences,
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
            "이 Google 계정으로는 로그인할 수 없습니다. 허용된 계정·도메인인지 확인해 주세요."
          );
        }
        throw err;
      }

      // 계정 매핑: 검증된 email로 자동 생성/로그인(BE-2, services/accounts.ts).
      // null은 미검증 email 등 방어값 또는 드문 동시 생성 경합 실패만 — 일반화 401(사유는 로그만).
      const result = await loginWithGoogleEmail(email);
      if (!result) {
        console.warn("Google 로그인: 계정 자동 생성/매핑 실패(경합 또는 방어값).");
        throw HttpError.unauthorized(
          "이 Google 계정으로는 로그인할 수 없습니다. 허용된 계정·도메인인지 확인해 주세요."
        );
      }

      // JWT 발급 + 응답(§9.1 G1 동결 계약).
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
