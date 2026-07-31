import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { defineConfig } from "vite";
import { aliases } from "./vite.aliases";
import { EMPTY_PRECACHE_MANIFEST, type PrecacheManifest } from "./vite.precache";

/**
 * Service Worker 전용 두 번째 rollup 진입 — 01 §6 (설계 §8.1)
 *
 * `workbox`·`vite-plugin-pwa`를 쓰지 않는 이유: ① `THIRD-PARTY.md` 핀 고정·라이선스 검토 비용
 * ② 생성 코드가 CSP·캐시 정책을 우리가 통제하지 못하는 형태로 넣는다 ③ 필요한 것은 셸 precache
 * 하나뿐이다.
 *
 * ⚠️ **실행 순서가 규격이다**: `vite build`(앱) → `vite build --config vite.sw.config.ts`(SW).
 *    ②가 ①의 `precache-manifest.json`을 읽고, `emptyOutDir: false`라 ①의 산출물을 지우지 않는다.
 * ⚠️ **`format: "iife"`** 다. 모듈 SW는 Safari 16.4 미만에 없어서 등록 자체가 실패한다.
 */

const MANIFEST_PATH = fileURLToPath(new URL("../web/kiosk/precache-manifest.json", import.meta.url));

function readManifest(): PrecacheManifest {
  try {
    const raw: unknown = JSON.parse(readFileSync(MANIFEST_PATH, "utf8"));
    if (typeof raw !== "object" || raw === null) return EMPTY_PRECACHE_MANIFEST;
    const record = raw as { buildId?: unknown; assets?: unknown };
    if (typeof record.buildId !== "string" || !Array.isArray(record.assets)) {
      return EMPTY_PRECACHE_MANIFEST;
    }
    return {
      buildId: record.buildId,
      assets: record.assets.filter((item): item is string => typeof item === "string"),
    };
  } catch {
    // 앱 빌드를 건너뛰고 SW만 빌드하는 경우(dev 확인). 셸 자산 없이도 등록은 된다.
    return EMPTY_PRECACHE_MANIFEST;
  }
}

export default defineConfig(() => {
  const manifest = readManifest();

  return {
    resolve: { alias: aliases },
    define: {
      // 자산 목록을 **sw.js 바이트 안에** 인라인한다 — 목록이 바뀌면 파일도 바뀌어야
      // 브라우저가 업데이트를 감지한다(15 §4 함정 14).
      __MCPHOTO_PRECACHE__: JSON.stringify(manifest),
    },
    build: {
      outDir: "../web/kiosk",
      // ⚠️ 앱 빌드 산출물을 지우면 안 된다.
      emptyOutDir: false,
      target: "es2022",
      sourcemap: true,
      rollupOptions: {
        input: fileURLToPath(new URL("./src/sw.ts", import.meta.url)),
        output: {
          format: "iife" as const,
          entryFileNames: "sw.js",
          inlineDynamicImports: true,
        },
      },
    },
  };
});
