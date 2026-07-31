/**
 * precache 매니페스트 helper — 01 §6 (순수 · 단위 테스트)
 *
 * ⚠️ **함정**: 브라우저는 SW 업데이트를 `sw.js`의 **바이트 차이**로 감지한다. 자산 해시가
 *    바뀌어도 `sw.js`가 같으면 영원히 갱신되지 않는다. 그래서 자산 목록을 `sw.js` 안에
 *    인라인해(`vite.sw.config.ts`의 `define`) 내용이 바뀌면 파일도 바뀌게 만든다.
 * ⚠️ 빌드 타임스탬프를 쓰지 않는다 — 내용이 같은 재빌드가 캐시를 churn시킨다.
 */

/** 번들 산출물에서 precache 대상이 **아닌** 것. */
const EXCLUDED_EXACT: readonly string[] = ["sw.js", "precache-manifest.json"];

/**
 * 번들 산출 파일명 → precache 대상 URL(`/assets/x-abc.js` 형태, 사전순 정렬).
 * `.map`·`sw.js`·매니페스트 자신은 제외한다. 중복은 한 번만 남긴다.
 */
export function collectPrecacheAssets(fileNames: readonly string[]): string[] {
  const urls = new Set<string>();
  for (const raw of fileNames) {
    const name = raw.replace(/^\.?\//, "");
    if (name.length === 0) continue;
    if (name.endsWith(".map")) continue;
    if (EXCLUDED_EXACT.includes(name)) continue;
    urls.add(`/${name}`);
  }
  return [...urls].sort();
}

/**
 * 자산 목록의 결정적 해시(FNV-1a 32bit, 8자리 hex).
 * **순서 무관**이다 — 내부에서 정렬한 뒤 해싱하므로 같은 집합이면 같은 값이 나온다.
 */
export function precacheBuildId(assets: readonly string[]): string {
  const joined = [...assets].sort().join("\n");
  let hash = 0x811c9dc5;
  for (let index = 0; index < joined.length; index++) {
    hash ^= joined.charCodeAt(index) & 0xff;
    // FNV prime 16777619 — 32bit 곱셈을 오버플로 없이 계산한다.
    hash = Math.imul(hash, 0x01000193) >>> 0;
  }
  return hash.toString(16).padStart(8, "0");
}

export interface PrecacheManifest {
  readonly buildId: string;
  readonly assets: readonly string[];
}

/** 파일 목록 → 매니페스트. 빌드 플러그인과 테스트가 같은 경로를 쓴다. */
export function buildPrecacheManifest(fileNames: readonly string[]): PrecacheManifest {
  const assets = collectPrecacheAssets(fileNames);
  return { buildId: precacheBuildId(assets), assets };
}

/** dev 빌드 단독 실행 대비 폴백(매니페스트 파일이 아직 없을 때). */
export const EMPTY_PRECACHE_MANIFEST: PrecacheManifest = { buildId: "dev", assets: [] };
