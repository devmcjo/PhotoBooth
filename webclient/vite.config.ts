import { defineConfig, loadEnv, type Plugin } from "vite";
import react from "@vitejs/plugin-react";
import { aliases } from "./vite.aliases";
import { buildPrecacheManifest } from "./vite.precache";

/**
 * precache 매니페스트 방출 — 01 §6.
 *
 * 산출된 자산 목록을 `precache-manifest.json`으로 남기고, 두 번째 빌드
 * (`vite.sw.config.ts`)가 그것을 읽어 **`sw.js` 안에 인라인**한다.
 * ⚠️ Hosting에서 이 파일은 **no-cache**여야 한다(`web/firebase.json`) — 옛 매니페스트가
 *    캐시되면 새 SW가 없는 자산을 precache하려다 실패한다.
 */
function precacheManifestPlugin(): Plugin {
  return {
    name: "mcphoto-precache-manifest",
    generateBundle(_options, bundle) {
      const manifest = buildPrecacheManifest(Object.keys(bundle));
      this.emitFile({
        type: "asset",
        fileName: "precache-manifest.json",
        source: JSON.stringify(manifest),
      });
    },
  };
}

export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, process.cwd(), "VITE_");

  // 빌드 시각은 진단 화면 전용이다(하단 캡션에는 쓰지 않는다 — it18, 05 §8.2).
  // .env에 값이 있으면 그것을 쓰고, 없으면 빌드 순간을 주입한다.
  const buildDate = env.VITE_BUILD_DATE?.trim() || new Date().toISOString();

  return {
    plugins: [react(), precacheManifestPlugin()],
    resolve: { alias: aliases },
    define: {
      "import.meta.env.VITE_BUILD_DATE": JSON.stringify(buildDate),
    },
    build: {
      // 산출물은 Hosting kiosk 사이트의 public 디렉터리다(01 §5.1·§5.2).
      outDir: "../web/kiosk",
      emptyOutDir: true,
      target: "es2022",
      sourcemap: true,
    },
    server: {
      // 실기기 테스트는 Hosting preview channel을 권장한다(01 §5.4).
      //
      // ⚠️ 포트를 바꾸지 마라. Google Console에 등록된 리디렉트 URI와 서버
      //    `OAUTH_REDIRECT_ALLOWLIST`가 **`http://localhost:5173/oauth2callback`** 이고
      //    서버는 완전 일치로 검사한다(14 §2.2·§3.3).
      // ⚠️ `strictPort: true`가 필요한 이유: 꺼져 있으면 포트 충돌 시 vite가 조용히 5174로
      //    옮겨가고, 그러면 Google이 `redirect_uri_mismatch`로 거부해 "로그인이 안 된다"만 보인다.
      port: 5173,
      strictPort: true,
    },
  };
});
