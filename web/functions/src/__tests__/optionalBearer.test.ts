/**
 * optionalBearer 미들웨어 단위 테스트(설계 §5.1, Step 3).
 *
 * 무토큰 → principal 미주입·통과(게스트), 유효 Bearer → principal 주입, 무효 Bearer → 401.
 * loadConfig는 고정 시크릿으로 mock하고 실제 issueToken/verifyToken을 사용한다(순수 검증).
 */
const SECRET = "test-optional-bearer-secret";

jest.mock("../config", () => ({
  loadConfig: () => ({
    jwtSecret: SECRET,
    jwtExpiresInSeconds: 3600,
    clientApiKeys: ["k"],
    storageBucket: "b",
    hostingBaseUrl: "",
    googleOAuthClientId: "",
    googleOAuthClientSecret: "",
    googleOAuthEnabled: false,
    googleAllowedHd: "",
  }),
}));

import type { NextFunction, Request, Response } from "express";
import { optionalBearer } from "../http/auth";
import { HttpError } from "../http/errors";
import { issueToken } from "../domain/jwt";

/** 최소 Request/Response/next 모킹. next에 넘어온 인자(에러 또는 없음)를 캡처. */
function invoke(authHeader?: string): { req: Request; error: unknown; called: boolean } {
  const req = {
    headers: authHeader ? { authorization: authHeader } : {},
  } as unknown as Request;
  const res = {} as Response;
  let error: unknown;
  let called = false;
  const next: NextFunction = (err?: unknown) => {
    called = true;
    error = err;
  };
  optionalBearer()(req, res, next);
  return { req, error, called };
}

describe("optionalBearer — 선택적 Bearer 신원화", () => {
  test("무토큰 → principal 미주입, next() 통과(게스트)", () => {
    const { req, error, called } = invoke(undefined);
    expect(called).toBe(true);
    expect(error).toBeUndefined();
    expect(req.principal).toBeUndefined();
  });

  test("유효 Bearer → req.principal 주입(id/role)", () => {
    const token = issueToken({ id: "temp1", role: "temp_user" }, SECRET, 3600);
    const { req, error } = invoke(`Bearer ${token}`);
    expect(error).toBeUndefined();
    expect(req.principal).toEqual({ id: "temp1", role: "temp_user" });
  });

  test("무효 Bearer(서명 불일치) → 401 HttpError", () => {
    const bad = issueToken({ id: "x", role: "user" }, "other-secret", 3600);
    const { error } = invoke(`Bearer ${bad}`);
    expect(error).toBeInstanceOf(HttpError);
    expect((error as HttpError).status).toBe(401);
  });

  test("Bearer 형식 아닌 헤더(토큰 추출 실패) → 게스트 통과(무토큰과 동일)", () => {
    // extractBearer가 null → 무토큰 취급.
    const { req, error } = invoke("Basic abc");
    expect(error).toBeUndefined();
    expect(req.principal).toBeUndefined();
  });
});
