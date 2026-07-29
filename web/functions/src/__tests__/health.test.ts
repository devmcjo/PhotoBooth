/**
 * GET /health 응답 테스트 — 무인증 도달성 유지 + deployedAt 노출 조건(설계 §6.2).
 *
 * 두 가지가 동시에 성립해야 한다:
 *  1) 키 없이도 200 {status,time} — deploy-web.bat의 배포 확인과 클라 도달성 체크가 이 형태에 의존한다.
 *  2) deployedAt은 유효 클라이언트 키를 제시했을 때만 — 무인증 호출자에게 배포 시점을 알려주지 않는다.
 *
 * loadConfig는 고정 키 목록으로, deployStamp는 고정 시각으로 mock한다(파일 시스템·시크릿 무의존).
 */
const VALID_KEY = "valid-client-key";
const STAMP = "2026-07-29T04:12:03.000Z";

jest.mock("../config", () => ({
  loadConfig: () => ({
    jwtSecret: "s",
    jwtExpiresInSeconds: 3600,
    clientApiKeys: [VALID_KEY],
    storageBucket: "b",
    hostingBaseUrl: "",
    googleOAuthClientId: "",
    googleOAuthClientSecret: "",
    googleOAuthEnabled: false,
    googleAllowedHd: "",
  }),
}));

const readDeployedAt = jest.fn<string | null, []>();
jest.mock("../deployStamp", () => ({
  readDeployedAt: () => readDeployedAt(),
}));

import type { NextFunction, Request, Response } from "express";
import { API_KEY_HEADER } from "../http/auth";
import { healthRouter } from "../routes/health";

interface HealthBody {
  status: string;
  time: string;
  deployedAt?: string;
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
