import { defineConfig, loadEnv } from "vite";
import react from "@vitejs/plugin-react";
import { aliases } from "./vite.aliases";

export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, process.cwd(), "VITE_");

  // 빌드 시각은 진단 화면 전용이다(하단 캡션에는 쓰지 않는다 — it18, 05 §8.2).
  // .env에 값이 있으면 그것을 쓰고, 없으면 빌드 순간을 주입한다.
  const buildDate = env.VITE_BUILD_DATE?.trim() || new Date().toISOString();

  return {
    plugins: [react()],
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
      port: 5273,
      strictPort: false,
    },
  };
});
