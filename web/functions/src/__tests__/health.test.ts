/**
 * GET /health 응답 테스트 — 무인증 도달성 유지 + deployedAt·oauth 노출 조건(설계 §6.2).
 *
 * 세 가지가 동시에 성립해야 한다:
 *  1) 키 없이도 200 {status,time} — deploy-web.bat의 배포 확인과 클라 도달성 체크가 이 형태에 의존한다.
 *  2) deployedAt은 유효 클라이언트 키를 제시했을 때만 — 무인증 호출자에게 배포 시점을 알려주지 않는다.
 *  3) oauth(구성 상태)도 같은 조건이고, **client_id 값이 응답 어디에도 없다**.
 *
 * loadConfig는 고정 키 목록으로, deployStamp는 고정 시각으로 mock한다(파일 시스템·시크릿 무의존).
 */
const VALID_KEY = "valid-client-key";
const STAMP = "2026-07-29T04:12:03.000Z";
const WEB_CLIENT_ID = "111-web.apps.googleusercontent.com";
const DESKTOP_CLIENT_ID = "222-desktop.apps.googleusercontent.com";

/** 테스트마다 갈아 끼우는 구성. `loadConfig`가 던지는 경우도 재현한다. */
let configOverride: (() => unknown) | null = null;

jest.mock("../config", () => ({
  loadConfig: () => {
    if (configOverride) return configOverride();
    return {
      jwtSecret: "s",
      jwtExpiresInSeconds: 3600,
      clientApiKeys: [VALID_KEY],
      storageBucket: "b",
      hostingBaseUrl: "",
      googleOAuthClientId: DESKTOP_CLIENT_ID,
      googleOAuthClientSecret: "x",
      googleOAuthEnabled: true,
      googleAllowedHd: "",
      googleOAuthClients: {
        desktop: { clientId: DESKTOP_CLIENT_ID, clientSecret: "x" },
        web: { clientId: WEB_CLIENT_ID, clientSecret: "y" },
      },
      googleOAuthAudiences: [DESKTOP_CLIENT_ID, WEB_CLIENT_ID],
      oauthRedirectAllowlist: ["https://a/oauth2callback", "https://b/oauth2callback"],
    };
  },
}));

const readDeployedAt = jest.fn<string | null, []>();
jest.mock("../deployStamp", () => ({
  readDeployedAt: () => readDeployedAt(),
}));

import type { NextFunction, Request, Response } from "express";
import type { OAuthConfigStatus } from "../domain/oauthStatus";
import { API_KEY_HEADER } from "../http/auth";
import { healthRouter } from "../routes/health";

interface HealthBody {
  status: string;
  time: string;
  deployedAt?: string;
  oauth?: OAuthConfigStatus;
}

/** GET / 를 라우터에 직접 흘려보내고 (status, body)를 캡처한다(supertest 미사용 — 기존 테스트 규약). */
function getHealth(headers: Record<string, string> = {}): { status: number; body: HealthBody } {
  const req = { method: "GET", url: "/", headers } as unknown as Request;
  let status = 0;
  let body: unknown;
  const res = {
    status(code: number) {
      status = code;
      return this;
    },
    json(payload: unknown) {
      body = payload;
      return this;
    },
  } as unknown as Response;
  const next: NextFunction = () => {
    throw new Error("헬스 라우트가 매칭되지 않았습니다(next 호출).");
  };
  healthRouter()(req, res, next);
  return { status, body: body as HealthBody };
}

describe("GET /health — 도달성 응답", () => {
  beforeEach(() => {
    readDeployedAt.mockReset();
    readDeployedAt.mockReturnValue(STAMP);
    configOverride = null;
  });

  test("키 없음 → 200 {status,time}, deployedAt 미포함", () => {
    const { status, body } = getHealth();

    expect(status).toBe(200);
    expect(body.status).toBe("ok");
    expect(typeof body.time).toBe("string");
    expect(body.deployedAt).toBeUndefined();
  });

  test("유효 키 → deployedAt 포함", () => {
    const { status, body } = getHealth({ [API_KEY_HEADER]: VALID_KEY });

    expect(status).toBe(200);
    expect(body.deployedAt).toBe(STAMP);
  });

  test("무효 키 → deployedAt 미포함(200은 유지)", () => {
    const { status, body } = getHealth({ [API_KEY_HEADER]: "wrong-key" });

    expect(status).toBe(200);
    expect(body.deployedAt).toBeUndefined();
  });

  test("유효 키 + 스탬프 부재 → deployedAt 미포함(200은 유지)", () => {
    readDeployedAt.mockReturnValue(null);

    const { status, body } = getHealth({ [API_KEY_HEADER]: VALID_KEY });

    expect(status).toBe(200);
    expect(body.status).toBe("ok");
    expect(body.deployedAt).toBeUndefined();
  });
});

/**
 * OAuth 구성 신호(2026-08-01 후속) — 운영자가 진단 모달에서 플레이스홀더 배포를 알아채기 위한 것.
 * **값 노출 금지**가 이 블록의 핵심 계약이다.
 */
describe("GET /health — oauth 구성 신호", () => {
  beforeEach(() => {
    readDeployedAt.mockReset();
    readDeployedAt.mockReturnValue(STAMP);
    configOverride = null;
  });

  test("키 없음 → oauth 미포함", () => {
    expect(getHealth().body.oauth).toBeUndefined();
  });

  test("무효 키 → oauth 미포함", () => {
    expect(getHealth({ [API_KEY_HEADER]: "wrong-key" }).body.oauth).toBeUndefined();
  });

  test("유효 키 → 두 종류 모두 ok · 공유 아님 · 허용목록 개수", () => {
    const { body } = getHealth({ [API_KEY_HEADER]: VALID_KEY });

    expect(body.oauth).toEqual({
      web: "ok",
      desktop: "ok",
      sharedClientId: false,
      redirectAllowlistCount: 2,
    });
  });

  test("웹 client_id가 플레이스홀더면 malformed로 보인다(2026-08-01 사고 재현)", () => {
    configOverride = () => ({
      clientApiKeys: [VALID_KEY],
      googleOAuthClients: {
        desktop: { clientId: DESKTOP_CLIENT_ID, clientSecret: "x" },
        web: { clientId: "<A1의 웹 client_id>", clientSecret: "y" },
      },
      oauthRedirectAllowlist: [],
    });

    const { body } = getHealth({ [API_KEY_HEADER]: VALID_KEY });

    expect(body.oauth?.web).toBe("malformed");
    expect(body.oauth?.desktop).toBe("ok");
    expect(body.oauth?.redirectAllowlistCount).toBe(0);
  });

  test("응답 어디에도 client_id 값이 담기지 않는다", () => {
    const serialized = JSON.stringify(getHealth({ [API_KEY_HEADER]: VALID_KEY }).body);

    expect(serialized).not.toContain(WEB_CLIENT_ID);
    expect(serialized).not.toContain(DESKTOP_CLIENT_ID);
    expect(serialized).not.toContain("apps.googleusercontent.com");
  });

  test("loadConfig가 던져도 200 도달성 응답은 유지된다(oauth만 생략)", () => {
    configOverride = () => {
      throw new Error("오구성");
    };

    const { status, body } = getHealth({ [API_KEY_HEADER]: VALID_KEY });

    expect(status).toBe(200);
    expect(body.status).toBe("ok");
    expect(body.oauth).toBeUndefined();
    // 키 판정(hasValidApiKey)도 오구성을 "무효"로 접으므로 deployedAt까지 함께 생략된다.
    expect(body.deployedAt).toBeUndefined();
  });
});
