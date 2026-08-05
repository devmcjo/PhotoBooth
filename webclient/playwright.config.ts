import { defineConfig, devices } from "@playwright/test";

/**
 * Playwright E2E 설정 — Step 17 (`docs/design/web-step17-e2e-and-acceptance.md` §4.1)
 *
 * 이 스위트가 대상으로 삼는 것은 **dev 서버(5173)** 다:
 *   ① `webServer.env`로 빌드 주입값을 기동 시점에 넣을 수 있고(`webclient/.env`가 없다)
 *   ② dev에는 Service Worker가 없어 캐시 간섭이 0이며(`main.tsx`의 `import.meta.env.PROD` 가드)
 *   ③ 소스맵으로 실패 추적이 쉽다.
 * 배포본(SW·CSP·실서버)의 검증은 실측 V13·V21-2·V25-* 가 소유한다(`docs/web-client/16`).
 *
 * ⚠️ 포트를 바꾸지 마라 — `vite.config.ts`가 `strictPort: true`로 5173에 고정돼 있고,
 *    Google 리디렉트 URI와 서버 허용목록이 `http://localhost:5173/oauth2callback`이다.
 */

const PORT = 5173;
const BASE = `http://localhost:${PORT}`;

export default defineConfig({
  testDir: "./tests/e2e",
  // 6컷 완주(카운트다운 × 6 + 합성 + 보관)가 가장 긴 시나리오다.
  timeout: 120_000,
  expect: { timeout: 10_000 },
  // OPFS·카메라·IndexedDB를 쓰는 무거운 시나리오다. 결정성을 우선한다.
  fullyParallel: false,
  workers: 1,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  reporter: [["list"], ["html", { open: "never" }]],
  use: {
    baseURL: BASE,
    locale: "ko-KR",
    timezoneId: "Asia/Seoul",
    trace: "retain-on-failure",
    video: "off",
  },
  projects: [
    {
      name: "chromium",
      use: {
        ...devices["Desktop Chrome"],
        // ⚠️ **설계 §4.1에서 이탈한 한 줄이다.** Playwright 1.49의 기본 headless는 구
        //    `chromium_headless_shell`인데 그 빌드에는 `getUserMedia`가 없어
        //    `NotSupportedError: Not supported`로 전 촬영 시나리오가 실패한다(실측 확인).
        //    `channel: "chromium"`은 정식 Chromium 빌드를 새 headless 모드로 띄운다 —
        //    headless를 유지한 채 카메라·WebGL 경로를 살리는 최소 변경이다.
        channel: "chromium",
        permissions: ["camera"],
        launchOptions: {
          args: [
            // 합성 카메라(기본 패턴). 결정적 픽셀은 골든 이미지(vitest)가 이미 고정하므로
            // y4m 파일은 커밋하지 않는다. 필요해지면 아래 한 줄만 추가하면 된다:
            //   `--use-file-for-fake-video-capture=tests/e2e/fixtures/camera.y4m`
            "--use-fake-device-for-media-stream",
            // headless에서 WebGL2(뷰티 필터의 Worker 경로)를 SwiftShader로 띄운다.
            // 뜨지 않아도 CPU 폴백이 있어 실패하지는 않는다.
            "--enable-unsafe-swiftshader",
            // 셔터음 자동재생. 앱에 폴백이 있어 필수는 아니다(09 §2.1과 같은 플래그).
            "--autoplay-policy=no-user-gesture-required",
          ],
        },
      },
    },
    {
      name: "webkit",
      // 제외 2종(설계 §6 — 조용히 지우지 않고 사유를 남긴다):
      //  · `@camera`     — `--use-fake-device-for-media-stream`은 Chromium 전용 스위치다.
      //                    WebKit에는 동등한 가짜 카메라 주입 수단이 없다.
      //  · `@opfs-write` — Playwright WebKit 18.2(Windows)에는 `navigator.storage.getDirectory`와
      //                    `OffscreenCanvas`가 **아예 없다**(실측). `getOpfsClient()`가
      //                    `UNSUPPORTED_OPFS_CLIENT`로 떨어져 모든 쓰기가 false다.
      //                    ⚠️ 이것은 **Safari의 동작이 아니라 이 빌드의 한계**다 — 실제
      //                    Safari 17+에는 OPFS가 있다. 저장 경로의 Safari 검증은 실측
      //                    V7·V23-5·V24-2·V25-4가 계속 소유한다.
      grepInvert: /@camera|@opfs-write/,
      use: { ...devices["Desktop Safari"] },
    },
  ],
  webServer: {
    command: "npm run dev",
    url: BASE,
    reuseExistingServer: !process.env.CI,
    timeout: 60_000,
    env: {
      // ⚠️ 목 백엔드를 **같은 오리진**에 둔다(설계 §4.2).
      //    교차 오리진이면 `X-MCPhoto-Client`·`Authorization` 때문에 CORS preflight(OPTIONS)가
      //    먼저 나가는데, Playwright의 `page.route`는 preflight를 가로채지 못한다.
      VITE_BACKEND_BASE_URL: `${BASE}/__mock-api`,
      // 값이 있어야 [Google로 로그인]이 렌더된다(`env.ts` — 빈 값이면 버튼을 숨긴다).
      // 실 client_id가 아니다. authorize 요청은 하네스가 가로채 실네트워크로 내보내지 않는다.
      VITE_GOOGLE_CLIENT_ID: "e2e-client-id.apps.googleusercontent.com",
      VITE_BACKEND_API_KEY: "e2e-gate-key",
      VITE_HOSTING_BASE_URL: `${BASE}/__mock-download`,
      VITE_APP_VERSION: "0.0.0-e2e",
    },
  },
});
