import { defineConfig } from "vitest/config";
import { aliases } from "./vite.aliases";

export default defineConfig({
  resolve: { alias: aliases },
  test: {
    // 도메인 계층은 브라우저 API를 쓰지 않으므로 node 환경으로 충분하다(01 §2.1).
    // DOM이 필요한 테스트는 파일 상단에 `// @vitest-environment jsdom`을 붙인다.
    environment: "node",
    include: ["tests/**/*.test.ts", "tests/**/*.test.tsx"],
    exclude: ["tests/e2e/**"],
    coverage: {
      provider: "v8",
      reportsDirectory: "coverage",
      include: ["src/domain/**/*.ts"],
      exclude: ["src/domain/**/index.ts"],
      reporter: ["text", "json-summary"],
      // Step 2 완료 기준: src/domain 커버리지 95% 이상(11-wbs Step 2).
      thresholds: {
        lines: 95,
        functions: 95,
        statements: 95,
        branches: 90,
      },
    },
  },
});
