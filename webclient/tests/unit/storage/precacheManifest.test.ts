import { describe, expect, it } from "vitest";
import {
  buildPrecacheManifest,
  collectPrecacheAssets,
  EMPTY_PRECACHE_MANIFEST,
  precacheBuildId,
} from "../../../vite.precache";

/**
 * precache 매니페스트 helper — 01 §6 (설계 §8.2)
 *
 * ⚠️ 빌드 id가 **결정적**이어야 한다. 타임스탬프를 쓰면 내용이 같은 재빌드가 캐시를 churn시키고,
 *    반대로 자산 목록이 바뀌었는데 id가 같으면 브라우저가 SW 업데이트를 감지하지 못한다.
 */

describe("collectPrecacheAssets", () => {
  it("`/` 접두를 붙이고 사전순으로 정렬한다", () => {
    expect(collectPrecacheAssets(["assets/b.js", "assets/a.css"])).toEqual([
      "/assets/a.css",
      "/assets/b.js",
    ]);
  });

  it("`.map`·`sw.js`·매니페스트 자신을 제외한다", () => {
    expect(
      collectPrecacheAssets([
        "assets/index-abc.js",
        "assets/index-abc.js.map",
        "sw.js",
        "sw.js.map",
        "precache-manifest.json",
      ]),
    ).toEqual(["/assets/index-abc.js"]);
  });

  it("이미 `/`·`./`로 시작해도 중복 접두를 만들지 않는다", () => {
    expect(collectPrecacheAssets(["/assets/a.js", "./assets/a.js"])).toEqual(["/assets/a.js"]);
  });

  it("빈 이름은 버린다", () => {
    expect(collectPrecacheAssets(["", "/", "assets/a.js"])).toEqual(["/assets/a.js"]);
  });
});

describe("precacheBuildId", () => {
  it("같은 입력은 같은 해시다", () => {
    expect(precacheBuildId(["/a.js", "/b.js"])).toBe(precacheBuildId(["/a.js", "/b.js"]));
  });

  it("**순서가 달라도 같은 해시**다", () => {
    expect(precacheBuildId(["/a.js", "/b.js"])).toBe(precacheBuildId(["/b.js", "/a.js"]));
  });

  it("내용이 다르면 해시가 다르다", () => {
    expect(precacheBuildId(["/a.js"])).not.toBe(precacheBuildId(["/b.js"]));
    expect(precacheBuildId([])).not.toBe(precacheBuildId(["/a.js"]));
  });

  it("8자리 hex다", () => {
    expect(precacheBuildId(["/a.js"])).toMatch(/^[0-9a-f]{8}$/);
    expect(precacheBuildId([])).toMatch(/^[0-9a-f]{8}$/);
  });
});

describe("buildPrecacheManifest", () => {
  it("자산 목록과 그 해시를 함께 낸다", () => {
    const manifest = buildPrecacheManifest(["assets/a.js", "assets/a.js.map", "sw.js"]);
    expect(manifest.assets).toEqual(["/assets/a.js"]);
    expect(manifest.buildId).toBe(precacheBuildId(["/assets/a.js"]));
  });

  it("dev 폴백은 자산이 비어 있다(SW만 단독 빌드해도 등록은 된다)", () => {
    expect(EMPTY_PRECACHE_MANIFEST).toEqual({ buildId: "dev", assets: [] });
  });
});
