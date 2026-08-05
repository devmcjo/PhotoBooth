import { describe, expect, it } from "vitest";
import {
  ENV_DEFAULTS,
  ensureTrailingSlash,
  resolveEnv,
  stripTrailingSlash,
  versionCaption,
} from "../../src/env";

/**
 * 빌드 주입값 정규화 — 01 §4.1
 * ⚠️ 두 URL의 정규화 방향이 **반대**라는 것이 이 테스트의 핵심이다.
 */

describe("URL 정규화 — 방향이 반대다", () => {
  it("BackendBaseUrl은 트레일링 슬래시를 부여한다", () => {
    expect(ensureTrailingSlash("https://a/api")).toBe("https://a/api/");
    expect(ensureTrailingSlash("https://a/api/")).toBe("https://a/api/");
    expect(ensureTrailingSlash("  https://a/api  ")).toBe("https://a/api/");
    expect(ensureTrailingSlash("")).toBe("");
    expect(ensureTrailingSlash("   ")).toBe("");
  });

  it("HostingBaseUrl은 트레일링 슬래시를 제거한다", () => {
    expect(stripTrailingSlash("https://a.web.app/")).toBe("https://a.web.app");
    expect(stripTrailingSlash("https://a.web.app///")).toBe("https://a.web.app");
    expect(stripTrailingSlash("https://a.web.app")).toBe("https://a.web.app");
    expect(stripTrailingSlash("  https://a.web.app/  ")).toBe("https://a.web.app");
  });

  it("두 함수가 서로를 대체하지 않는다", () => {
    const url = "https://x/y";
    expect(ensureTrailingSlash(url)).not.toBe(stripTrailingSlash(url));
  });
});

describe("resolveEnv", () => {
  it("빈 값을 기본값으로 대체한다", () => {
    const { config } = resolveEnv({});
    expect(config.backendBaseUrl).toBe(`${ENV_DEFAULTS.backendBaseUrl}/`);
    expect(config.hostingBaseUrl).toBe(ENV_DEFAULTS.hostingBaseUrl);
    expect(config.storageBucket).toBe(ENV_DEFAULTS.storageBucket);
    expect(config.appVersion).toBe(ENV_DEFAULTS.appVersion);
  });

  it("주입값이 있으면 그것을 정규화해 쓴다", () => {
    const { config } = resolveEnv({
      VITE_BACKEND_BASE_URL: "https://custom/api",
      VITE_HOSTING_BASE_URL: "https://custom.web.app/",
      VITE_STORAGE_BUCKET: "custom.bucket",
      VITE_APP_VERSION: "1.2.3",
      VITE_BACKEND_API_KEY: "key-1",
      VITE_GOOGLE_CLIENT_ID: "client-1",
      VITE_BUILD_DATE: "2026-07-30T00:00:00.000Z",
    });
    expect(config.backendBaseUrl).toBe("https://custom/api/");
    expect(config.hostingBaseUrl).toBe("https://custom.web.app");
    expect(config.backendApiKey).toBe("key-1");
    expect(config.googleClientId).toBe("client-1");
    expect(config.buildDate).toBe("2026-07-30T00:00:00.000Z");
  });

  it("게이트 키가 없으면 경고만 남기고 크래시하지 않는다", () => {
    const { config, warnings } = resolveEnv({ VITE_GOOGLE_CLIENT_ID: "c" });
    expect(config.backendApiKey).toBe("");
    expect(warnings).toHaveLength(1);
    expect(warnings[0]).toContain("VITE_BACKEND_API_KEY");
  });

  it("client id가 없으면 로그인 버튼을 숨긴다는 경고를 남긴다", () => {
    const { warnings } = resolveEnv({ VITE_BACKEND_API_KEY: "k" });
    expect(warnings).toHaveLength(1);
    expect(warnings[0]).toContain("VITE_GOOGLE_CLIENT_ID");
  });

  it("둘 다 없으면 경고가 2건이다", () => {
    expect(resolveEnv({}).warnings).toHaveLength(2);
  });

  it("공백만 있는 값은 빈 값으로 취급한다", () => {
    const { config, warnings } = resolveEnv({
      VITE_BACKEND_API_KEY: "   ",
      VITE_GOOGLE_CLIENT_ID: "\t",
    });
    expect(config.backendApiKey).toBe("");
    expect(config.googleClientId).toBe("");
    expect(warnings).toHaveLength(2);
  });
});

describe("versionCaption — it18", () => {
  it("`v{version}` 형식이다", () => {
    expect(versionCaption("1.2.0")).toBe("v1.2.0");
    expect(versionCaption("  0.1.0  ")).toBe("v0.1.0");
  });

  it("값이 없으면 v0.0.0으로 폴백한다", () => {
    expect(versionCaption("")).toBe("v0.0.0");
    expect(versionCaption("   ")).toBe("v0.0.0");
  });

  it("배포 채널·빌드 시각을 넣지 않는다", () => {
    const caption = versionCaption("1.2.0");
    expect(caption).toBe("v1.2.0");
    expect(caption).not.toMatch(/\d{4}-\d{2}-\d{2}/);
  });
});
