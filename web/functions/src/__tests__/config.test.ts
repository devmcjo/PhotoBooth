/**
 * loadConfig — Google OAuth 활성화 판정 회귀 테스트.
 *
 * 배포 임계: GOOGLE_OAUTH_CLIENT_SECRET은 defineSecret이라 배포 시 항상 존재해야 하므로
 * (SSO 미사용이어도 placeholder 등록 필요), "시크릿만 있고 CLIENT_ID 없음"은 **정상 비활성**이어야 한다.
 * 과거 대칭 검사(hasId !== hasSecret)는 이 경우 오구성 에러를 던져 배포된 백엔드가 전 요청 500이 됐다.
 */
import { loadConfig, resetConfigCache } from "../config";

const KEYS = [
  "JWT_SECRET",
  "CLIENT_API_KEYS",
  "STORAGE_BUCKET",
  "GOOGLE_OAUTH_CLIENT_ID",
  "GOOGLE_OAUTH_CLIENT_SECRET",
];

describe("loadConfig — Google OAuth 활성화 판정 (defineSecret placeholder 대응)", () => {
  const saved: Record<string, string | undefined> = {};

  beforeAll(() => {
    for (const k of KEYS) saved[k] = process.env[k];
  });
  afterAll(() => {
    for (const k of KEYS) {
      if (saved[k] === undefined) delete process.env[k];
      else process.env[k] = saved[k];
    }
    resetConfigCache();
  });

  beforeEach(() => {
    resetConfigCache();
    process.env.JWT_SECRET = "test-jwt";
    process.env.CLIENT_API_KEYS = "test-key";
    process.env.STORAGE_BUCKET = "test-bucket";
    delete process.env.GOOGLE_OAUTH_CLIENT_ID;
    delete process.env.GOOGLE_OAUTH_CLIENT_SECRET;
  });

  it("시크릿만 있고 CLIENT_ID 없음 → 비활성(placeholder/프로덕션 시나리오), 에러 없음", () => {
    process.env.GOOGLE_OAUTH_CLIENT_SECRET = "placeholder-set-real-secret-when-enabling-sso";
    const cfg = loadConfig();
    expect(cfg.googleOAuthEnabled).toBe(false);
  });

  it("CLIENT_ID·시크릿 모두 있음 → 활성", () => {
    process.env.GOOGLE_OAUTH_CLIENT_ID = "id.apps.googleusercontent.com";
    process.env.GOOGLE_OAUTH_CLIENT_SECRET = "real-secret";
    expect(loadConfig().googleOAuthEnabled).toBe(true);
  });

  it("CLIENT_ID 있는데 시크릿 없음 → 오구성 조기 실패", () => {
    process.env.GOOGLE_OAUTH_CLIENT_ID = "id.apps.googleusercontent.com";
    expect(() => loadConfig()).toThrow(/GOOGLE_OAUTH_CLIENT_SECRET/);
  });

  it("둘 다 없음 → 비활성", () => {
    expect(loadConfig().googleOAuthEnabled).toBe(false);
  });
});
