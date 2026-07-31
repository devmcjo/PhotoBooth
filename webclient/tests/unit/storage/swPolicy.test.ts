import { describe, expect, it } from "vitest";
import {
  cacheNameForBuild,
  CACHE_PREFIX,
  classifySwRequest,
  isCacheableResponse,
  PRECACHE_STABLE_URLS,
  type SwRoute,
} from "@adapters/platform/swPolicy";

/**
 * Service Worker 라우팅 분류기 — 01 §6 (설계 §8.3)
 *
 * ⚠️ **판정 순서가 계약**이다. 3(cross-origin)·4(API)가 5(navigate)보다 앞이어야
 *    백엔드로 가는 문서 요청까지 bypass된다.
 * ⚠️ **기본은 bypass**다 — 알 수 없는 경로를 캐시하면 API 응답·서명 URL이 섞인다.
 */

const ORIGIN = "https://kiosk.example.app";

function route(
  url: string,
  overrides: { method?: string; mode?: string } = {},
): SwRoute["kind"] {
  return classifySwRequest({
    method: overrides.method ?? "GET",
    mode: overrides.mode ?? "no-cors",
    url,
    origin: ORIGIN,
  }).kind;
}

describe("classifySwRequest — 9단 판정", () => {
  it("1. GET이 아니면 bypass다(업로드 PUT·POST)", () => {
    expect(route(`${ORIGIN}/index.html`, { method: "PUT", mode: "navigate" })).toBe("bypass");
    expect(route(`${ORIGIN}/assets/x.js`, { method: "POST" })).toBe("bypass");
    expect(route(`${ORIGIN}/assets/x.js`, { method: "HEAD" })).toBe("bypass");
  });

  it("2. URL 파싱 실패는 bypass다", () => {
    expect(route("not a url")).toBe("bypass");
    expect(route("")).toBe("bypass");
  });

  it("3. **cross-origin은 전부 bypass**다(백엔드·서명 PUT·Storage)", () => {
    const crossOrigin = [
      "https://asia-northeast3-mcphoto-955fb.cloudfunctions.net/api/health",
      "https://storage.googleapis.com/bucket/object?X-Goog-Signature=abc",
      "https://firebasestorage.googleapis.com/v0/b/x/o/y",
    ];
    for (const url of crossOrigin) {
      expect(route(url)).toBe("bypass");
      // navigate 모드여도 마찬가지다(3이 5보다 앞이다).
      expect(route(url, { mode: "navigate" })).toBe("bypass");
    }
  });

  it("4. 동일 출처라도 `/api/`·`/uploads`는 bypass다", () => {
    expect(route(`${ORIGIN}/api/health`)).toBe("bypass");
    expect(route(`${ORIGIN}/api/accounts`, { mode: "navigate" })).toBe("bypass");
    expect(route(`${ORIGIN}/session/uploads/abc.jpg`)).toBe("bypass");
  });

  it("5. 문서 요청은 navigate다", () => {
    expect(route(`${ORIGIN}/`, { mode: "navigate" })).toBe("navigate");
    expect(route(`${ORIGIN}/oauth2callback?code=x`, { mode: "navigate" })).toBe("navigate");
  });

  it("6. `/assets/`는 immutable이다(내용 해시)", () => {
    expect(route(`${ORIGIN}/assets/index-abc123.js`)).toBe("immutable");
    expect(route(`${ORIGIN}/assets/index-abc123.css`)).toBe("immutable");
  });

  it("7. 운영자 교체 파일은 fresh다", () => {
    for (const path of [
      "/branding.json",
      "/manifest.webmanifest",
      "/frames/index.json",
      "/precache-manifest.json",
    ]) {
      expect(route(`${ORIGIN}${path}`)).toBe("fresh");
    }
  });

  it("8. 아이콘·프레임·사운드·파비콘은 static이다", () => {
    expect(route(`${ORIGIN}/icons/icon-192.png`)).toBe("static");
    expect(route(`${ORIGIN}/frames/summer.png`)).toBe("static");
    expect(route(`${ORIGIN}/sounds/shutter.wav`)).toBe("static");
    expect(route(`${ORIGIN}/favicon.ico`)).toBe("static");
  });

  it("9. **미지의 경로는 bypass**다(기본 거부)", () => {
    expect(route(`${ORIGIN}/unknown/thing.json`)).toBe("bypass");
    expect(route(`${ORIGIN}/data.bin`)).toBe("bypass");
    expect(route(`${ORIGIN}/index.html`)).toBe("bypass"); // navigate 모드가 아니면 bypass다
  });

  it("`/frames/index.json`은 static보다 fresh가 먼저 걸린다(7이 8보다 앞)", () => {
    expect(route(`${ORIGIN}/frames/index.json`)).toBe("fresh");
  });
});

describe("isCacheableResponse", () => {
  it("200 + basic/default만 캐시한다", () => {
    expect(isCacheableResponse(200, "basic")).toBe(true);
    expect(isCacheableResponse(200, "default")).toBe(true);
  });

  it("**opaque는 절대 캐시하지 않는다**", () => {
    expect(isCacheableResponse(200, "opaque")).toBe(false);
    expect(isCacheableResponse(0, "opaque")).toBe(false);
  });

  it("200이 아니면 캐시하지 않는다", () => {
    for (const status of [204, 206, 301, 304, 404, 500]) {
      expect(isCacheableResponse(status, "basic")).toBe(false);
    }
  });
});

describe("precache 목록·캐시 이름", () => {
  it("셸 고정 URL이 전부 절대 경로다", () => {
    for (const url of PRECACHE_STABLE_URLS) {
      expect(url.startsWith("/")).toBe(true);
    }
  });

  it("캐시 이름은 빌드 id에 따라 달라진다(옛 캐시를 activate가 지운다)", () => {
    expect(cacheNameForBuild("abc")).toBe(`${CACHE_PREFIX}abc`);
    expect(cacheNameForBuild("abc")).not.toBe(cacheNameForBuild("def"));
  });
});
