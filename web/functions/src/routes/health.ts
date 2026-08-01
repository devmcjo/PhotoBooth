/**
 * 헬스 체크 — GET /health (설계 §6.2). 인증 없음.
 * 클라의 IsInitialized(백엔드 도달 가능) 판정에 사용된다(설계 §5.1).
 *
 * deployedAt(최종 웹 배포 시각)과 oauth(구성 상태)는 **유효 클라이언트 키를 제시한 호출자에게만**
 * 포함된다. 무인증 스캐너에 배포 시점·구성 상태를 알려줄 이유가 없고, 무인증 200 응답은
 * deploy-web.bat의 배포 확인·클라 도달성 체크가 그대로 쓰도록 형태를 유지해야 하기 때문이다.
 */
import { Router } from "express";
import { loadConfig } from "../config";
import { describeOAuthConfig, OAuthConfigStatus } from "../domain/oauthStatus";
import { readDeployedAt } from "../deployStamp";
import { hasValidApiKey } from "../http/auth";

interface HealthResponse {
  status: string;
  time: string;
  /** 최종 웹 배포 시각(UTC ISO8601). 키 미제시·스탬프 부재 시 생략된다. */
  deployedAt?: string;
  /**
   * OAuth 구성 상태(열거값·개수만). 키 미제시·구성 로드 실패 시 생략된다.
   * ⚠️ client_id 값은 어떤 형태로도 담기지 않는다(`domain/oauthStatus.ts`).
   */
  oauth?: OAuthConfigStatus;
}

/**
 * 구성 로드가 실패해도 헬스 체크를 500으로 바꾸지 않는다 — 도달성 응답이 최우선이다
 * (`hasValidApiKey`가 오구성을 "키 무효"로 접는 것과 같은 방침).
 */
function readOAuthStatus(): OAuthConfigStatus | null {
  try {
    return describeOAuthConfig(loadConfig());
  } catch {
    return null;
  }
}

export function healthRouter(): Router {
  const router = Router();
  router.get("/", (req, res) => {
    const body: HealthResponse = { status: "ok", time: new Date().toISOString() };
    if (hasValidApiKey(req)) {
      const deployedAt = readDeployedAt();
      if (deployedAt) body.deployedAt = deployedAt;
      const oauth = readOAuthStatus();
      if (oauth) body.oauth = oauth;
    }
    res.status(200).json(body);
  });
  return router;
}
