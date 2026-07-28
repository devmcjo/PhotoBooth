/**
 * 역할 게이트 미들웨어 단위 테스트 (it16 Step S2·S3, 설계 §5.2·§8.4-36/37).
 *
 * `accounts.test.ts`는 **서비스 레벨**(setRole/resetOtherPin 직접 호출)이라 라우트 미들웨어를 통과시키지
 * 않는다 → 게이트 누락을 잡지 못한다(it16이 고친 결함이 정확히 그 사각지대였다).
 * 여기서는 `optionalBearer.test.ts`와 같은 Request/next 모킹으로 게이트 자체를 역할별로 검증한다.
 *
 * ⚠️ `loadConfig`를 mock하지 않는다: requirePower/requireAdmin은 principal만 보고 config에 닿지 않는다.
 *    (AppConfig에 필드가 추가돼도 이 파일이 깨지지 않는다.)
 */
import fs from "fs";
import path from "path";
import type { NextFunction, Request, Response } from "express";
import { requireAdmin, requirePower } from "../http/auth";
import { HttpError } from "../http/errors";
import type { UserRole } from "../domain/roles";

/** 게이트 1회 실행 결과: next에 넘어온 에러(없으면 통과). */
interface GateResult {
  passed: boolean;
  status: number | null;
}

function runGate(
  gate: (req: Request, res: Response, next: NextFunction) => void,
  role: UserRole | null
): GateResult {
  const req = {
    headers: {},
    ...(role === null ? {} : { principal: { id: "u1", role } }),
  } as unknown as Request;
  const res = {} as Response;
  let error: unknown;
  const next: NextFunction = (err?: unknown) => {
    error = err;
  };
  gate(req, res, next);
  if (error === undefined) return { passed: true, status: null };
  expect(error).toBeInstanceOf(HttpError);
  return { passed: false, status: (error as HttpError).status };
}

describe("requirePower — power(manager/admin)만 통과(it16 §5.1 동결표)", () => {
  test.each<[UserRole, number]>([
    ["temp_user", 403],
    ["user", 403],
    // it16 핵심: 고급 유저는 power가 아니다. isPower에 advanced_user를 넣으면 이 단정이 즉시 깨진다.
    ["advanced_user", 403],
  ])("비power(%s) → %i", (role, status) => {
    const r = runGate(requirePower(), role);
    expect(r.passed).toBe(false);
    expect(r.status).toBe(status);
  });

  test.each<[UserRole]>([["manager"], ["admin"]])("power(%s) → next() 통과", (role) => {
    expect(runGate(requirePower(), role)).toEqual({ passed: true, status: null });
  });

  test("principal 없음(requireBearer 미통과) → 401", () => {
    const r = runGate(requirePower(), null);
    expect(r.passed).toBe(false);
    expect(r.status).toBe(401);
  });
});

describe("requireAdmin — admin만 통과(전역 한도 설정 등)", () => {
  test.each<[UserRole]>([
    ["temp_user"],
    ["user"],
    ["advanced_user"],
    ["manager"],
  ])("비admin(%s) → 403", (role) => {
    const r = runGate(requireAdmin(), role);
    expect(r.passed).toBe(false);
    expect(r.status).toBe(403);
  });

  test("admin → next() 통과", () => {
    expect(runGate(requireAdmin(), "admin")).toEqual({ passed: true, status: null });
  });
});

// ── 프레임 쓰기 라우트 권한 (it16 §5.2 — 서버 코드 변경 0, 성질을 테스트로 고정) ──
//
// it16은 user·temp_user의 프레임 생성·편집·삭제 권한을 제거한다. 서버는 **이미** 이를 강제한다:
// POST /frames · PUT /frames/:id · DELETE /frames/:id가 모두 requirePower() 뒤에 있고 고급 유저는
// power가 아니므로 403이다. AdvancedUser의 프레임은 개인 로컬 저장뿐이라 서버 쓰기 요청 자체가 없다.
// 따라서 새 미들웨어를 만들지 않고, 이 성질이 미래에 깨지지 않도록 여기서 못 박는다.
describe("프레임 쓰기 라우트 권한(it16 §5.2) — 생성·수정·삭제는 power 전용", () => {
  test.each<[UserRole]>([["temp_user"], ["user"], ["advanced_user"]])(
    "%s는 프레임 생성·수정·삭제 게이트에서 403(로컬 저장만 허용되는 역할)",
    (role) => {
      // 세 쓰기 라우트가 공유하는 게이트가 requirePower() 하나이므로 게이트 1회 검증이 3라우트를 덮는다
      // (라우트별 게이트 유지 여부는 아래 구조 회귀가 별도로 확인한다).
      const r = runGate(requirePower(), role);
      expect(r.passed).toBe(false);
      expect(r.status).toBe(403);
    }
  );

  test("isPower에 새 역할이 추가되면 이 스위트가 실패한다(회귀 감지 의도 명시)", () => {
    // 고급 유저를 power로 만들면 공용 DB 프레임 생성·삭제까지 열린다 — 설계가 명시적으로 거부한 상태.
    expect(runGate(requirePower(), "advanced_user").status).toBe(403);
  });
});

// ── 구조 회귀: 라우트에 게이트가 남아 있는지 소스로 확인(설계 §8.4-37) ──────────
//
// 라우터를 Express로 띄우지 않고도 "게이트 제거" 커밋을 잡아낸다.
// 서비스 레벨 테스트가 통과해도 게이트가 빠지면 인가가 뚫리므로(it16이 고친 결함) 소스를 직접 본다.

/** 라우트 소스에서 주석을 제거한다 — 게이트를 설명하는 주석이 실제 호출로 오집계되지 않게. */
function routeCode(routeFile: string): string {
  const src = fs.readFileSync(path.join(__dirname, "..", "routes", routeFile), "utf8");
  return src.replace(/\/\*[\s\S]*?\*\//g, "").replace(/\/\/[^\n]*/g, "");
}

function countRequirePower(routeFile: string): number {
  return routeCode(routeFile).match(/requirePower\(\)/g)?.length ?? 0;
}

/** `router.<method>("<path>", ...)` 등록별로 power 게이트 유무를 판정(등록 지점 ~ 다음 등록 지점). */
function powerGatedRoutes(routeFile: string): Record<string, boolean> {
  const code = routeCode(routeFile);
  const re = /router\.(get|post|put|patch|delete)\(\s*"([^"]*)"/g;
  const found: Array<{ key: string; start: number }> = [];
  let m: RegExpExecArray | null;
  while ((m = re.exec(code)) !== null) {
    found.push({ key: `${m[1].toUpperCase()} ${m[2]}`, start: m.index });
  }
  const result: Record<string, boolean> = {};
  found.forEach((reg, i) => {
    const end = i + 1 < found.length ? found[i + 1].start : code.length;
    result[reg.key] = code.slice(reg.start, end).includes("requirePower()");
  });
  return result;
}

describe("라우트 게이트 구조 회귀(it16 §8.4-37)", () => {
  test("accounts.ts의 requirePower()는 4회 — list·delete·role·pin(it16 S2 추가)", () => {
    expect(countRequirePower("accounts.ts")).toBe(4);
  });

  test("frames.ts의 requirePower()는 3회 — post·put·delete(it16에서 변경되지 않아야 한다)", () => {
    expect(countRequirePower("frames.ts")).toBe(3);
  });

  test("frames.ts: 쓰기 3라우트만 power 게이트, 조회 2라우트는 게이트 없음", () => {
    expect(powerGatedRoutes("frames.ts")).toEqual({
      "GET /default": false, // API키(게스트 조회)
      "GET /": false, // Bearer, 본인 or power는 핸들러 내부에서 판정
      "POST /": true, // 공용 기본 프레임 생성
      "PUT /:id": true, // 공용 기본 프레임 수정
      "DELETE /:id": true, // 공용 기본 프레임 삭제
    });
  });

  test("accounts.ts: 타 계정 조작 4라우트만 power 게이트, 본인 경로는 게이트 없음", () => {
    expect(powerGatedRoutes("accounts.ts")).toEqual({
      "GET /": true, // 계정 목록
      "GET /me/qr-usage": false, // 본인 QR 사용량
      "POST /me/pin/verify": false, // 본인 PIN 검증(E1 — 비power도 가능)
      "PUT /me/pin": false, // 본인 PIN 설정·변경(E2 — 비power도 가능)
      "DELETE /:id": true, // 계정 삭제
      "PATCH /:id/role": true, // 역할 지정
      "PUT /:id/pin": true, // 타 계정 PIN 재설정(it16 S2에서 추가된 게이트)
    });
  });
});
