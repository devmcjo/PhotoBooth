# 웹 클라이언트 Step 17 — E2E · 수락 · 실기기 검증 (설계)

| 항목 | 값 |
|------|-----|
| 대상 | 키오스크 웹 클라이언트([`docs/web-client/`](../web-client/README.md)) **WBS Step 17** — ★마일스톤 B, **이 프로젝트의 마지막 Step** |
| 규격 | [`10 §5·§6·§7·§8`](../web-client/10-testing-and-acceptance.md) · [`11 Step 17`](../web-client/11-wbs.md) · [`14 §10`](../web-client/14-handoff-and-user-actions.md) · [`09`](../web-client/09-kiosk-operations.md) |
| 진실원 | 실제 소스(`webclient/src/**`) > [`docs/analysis`](../analysis/README.md) > 설계 문서([`design/README §4`](./README.md#4-문서-유효성-주의)) |
| 작성일 | 2026-08-01 |
| 선행 | Step 0~16 전부 완료(웹 1926 / 서버 316 / Windows 938 통과 — [`15 §7`](../web-client/15-implementation-conventions.md)) |
| 비범위 | 새 제품 기능 · `src/**` 수정(§17) · 실기기 대행(사람이 한다) |

> 이 Step은 **코드를 늘리는 Step이 아니라 판정을 내리는 Step**이다. 산출물은 세 가지다:
> ① Playwright E2E 자동화 ② 수락 체크리스트의 자동/수동 **분류** ③ V1~V25 실기기 통합 절차서.
> 그리고 **"자동화할 수 없다"는 판정 자체가 산출물**이다 — §7이 26개 시나리오를 하나씩 판정하고,
> 자동화 불가 항목은 어느 실측 번호로 넘어가는지 명시한다. 추정으로 통과 처리하지 않는다.

---

## 0. 검증된 사실 / 미검증 가정

### 0.1 검증된 사실 (2026-08-01, 코드를 직접 열어 확인)

| # | 사실 | 근거 |
|---|------|------|
| F1 | `vitest.config.ts`가 **이미 `tests/e2e/**`를 exclude** 한다. E2E 파일을 그 경로에 두면 vitest가 집지 않는다 | `webclient/vitest.config.ts:11` |
| F2 | `webclient/.gitignore`에 **`test-results/`·`playwright-report/`가 이미 있다**(“# Playwright” 절) | `webclient/.gitignore` 말미 |
| F3 | `tsconfig.json`의 `include`에 **`tests`가 들어 있다.** 루트 설정 파일은 개별 등재 방식이다(`vite.config.ts`·`vitest.config.ts`·`vite.sw.config.ts` 등) → `playwright.config.ts`는 **직접 추가**해야 타입 검사에 들어온다 | `webclient/tsconfig.json` |
| F4 | dev 서버는 **5173 + `strictPort: true`** 로 고정돼 있고 주석이 "바꾸지 마라"를 명시한다 | `vite.config.ts:48-58` |
| F5 | `webclient/.env`는 **없다**(`.env.example`·`.env.production.local`만 존재). 따라서 `npm run dev`를 그냥 띄우면 `VITE_GOOGLE_CLIENT_ID`가 빈 값이고 **로그인 버튼이 렌더되지 않는다** | `ls webclient/.env*`, `env.ts:75-78`, `useGoogleSignIn.ts:69` |
| F6 | 백엔드 base URL은 **설정이 아니라 `env.backendBaseUrl`** 이다(설정의 `BackendBaseUrl`은 표시·내보내기용) | `backendClient.ts:79` |
| F7 | 백엔드가 호출하는 경로는 **12종**뿐이다: `auth/google` · `health` · `frames/default` · `frames`(GET/POST) · `frames/{id}`(DELETE) · `accounts` · `accounts/me/pin/verify` · `accounts/me/pin` · `accounts/{id}`·`{id}/role`·`{id}/pin` · `accounts/me/qr-usage` · `config/temp-user-limits` · `uploads/prepare` · `uploads/commit` | `grep -rn "path: " src/adapters/http/*.ts` |
| F8 | 게이트 키 헤더는 `X-MCPhoto-Client`, 요청은 `credentials: "omit"`·`cache: "no-store"` | `backendClient.ts:21`, `:97-122` |
| F9 | **로그인 개시는 `sessionStorage`에 pending을 저장한 뒤 `location.assign(authorizeUrl)`** 하고, 그 URL에 `state`·`nonce`·`redirect_uri`가 **쿼리로 들어 있다** | `googleSignIn.ts:96-122`, `authorizeUrl.ts:40-53` |
| F10 | 콜백 판정은 **`state` 문자열 일치만** 본다. `nonce`는 클라이언트가 검증하지 않고 교환 요청에 실어 서버가 본다 | `oauthCallbackPolicy.ts:114-136`, `googleSignIn.ts:164-186` |
| F11 | `takePendingOauth()`는 **읽고 즉시 삭제**한다(원자적 1회 소비). 소비 순서: `getItem` → `removeItem` → parse | `oauthStateStore.ts:67-91` |
| F12 | `sessionStore.login()` 호출부는 `oauthCallbackRunner.defaultRunDeps().applySession` **1곳**이며 그 안에서 `setToken`이 **먼저** 돈다(정적 검사 AUTH-1) | `oauthCallbackRunner.ts:99-111`, `15 §3.4` |
| F13 | `main.tsx`가 `classifyRoute(pathname) === "oauthCallback"` 일 때만 콜백을 **React 밖 동기 1회** 소비한다. 경로는 `/oauth2callback` 하나다 | `main.tsx:101-114`, `router.ts:17-19` |
| F14 | **게스트는 `Qr`에 도달할 수 없다.** `isQrEffectivelyEnabled(raw, isLoggedIn=false, …)`가 **무조건 false**다 | `qrEffectivePolicy.ts:11-18` |
| F15 | `Result` [다음]의 순서는 `runResultNext`가 소유한다: 타임랩스 마무리 → 가드 → **로컬 보관** → 토스트 → 가드 → effective QR 판정 → `Qr`/`Done`. 업로드는 여기 없다(`Qr` 화면 소유) | `resultNext.ts:64-108`, `uploadRunner.ts:151` |
| F16 | 유휴 상한은 **120초 + 10초**이고 감시 대상 화면은 `FrameSelect`·`Guide`·`Capture`·`CutSelect`·`Result`·`Qr` **6종**이다. 판정 시계는 `performance.now()`, tick은 `setInterval(250ms)` | `idleWatchdog.ts:14-16`, `:94`, `stateMachine.ts:44-53` |
| F17 | `getOpfsClient()`는 **메인 스레드에서** `navigator.storage?.getDirectory` 유무를 보고, 없으면 **모든 쓰기가 `false`인 `UNSUPPORTED_OPFS_CLIENT`** 를 돌려준다 | `opfsClient.ts:181-193`, `:158-167` |
| F18 | `resultSaver`는 `finalBlob === null`이면 **`skipped`**(토스트 없음), `opfs.write`가 false면 **`failed`**(토스트 있음)다. 두 경로는 다르다 | `resultSaver.ts:120-176`, `resultSavePlan.ts:50-52`, `resultNext.ts:92` |
| F19 | 설정 영속 키는 `mcphoto.settings.v1`, 형태는 `{schemaVersion, values, webExtras}`이며 **타입이 맞는 키만 채택**하고 나머지는 기본값이다 | `settingsRepo.ts:23`, `:60-70` |
| F20 | 카운트다운 허용값 최소는 **3초**, 컷 수는 6/8/10(또는 자동 0). 기본은 6컷·6초·`SendTimelapse: true`·`SaveLocalCopy: true`·`EnableQrDelivery: true` | `appSettings.ts:15-16`, `:74-99` |
| F21 | 서버·번들 프레임이 **하나도 없어도** 코드 생성 fallback 프레임(1200×1600, **슬롯 4개**)으로 촬영이 가능하다. `public/frames/index.json`은 `[]`다 | `fallbackFrameSpec.ts:10-42`, `15 §6` Step 14 |
| F22 | `src/` 전체에 **`data-testid`가 0건**이다. 접근 가능 이름(`aria-label`)은 34곳뿐이다 | `grep -rn data-testid src` → 0 |
| F23 | 흐름 버튼 라벨: Home `"촬영 시작"`(`STRINGS.home.start`) · Guide `"촬영 시작"`(**리터럴**) · Capture `"바로 촬영"`(리터럴) · 컷 카드 `aria-label="컷 {n}"` · 공통 `[다음]`/`[취소]`는 `STRINGS.common` | `FlowViews.tsx:41-50`, `:85-96`, `:133-139`, `:179`, `:207-218` |
| F24 | 프레임 편집기에 `<input type="file" accept="image/png,image/jpeg">`가 있다 → `setInputFiles`로 이미지 주입이 가능하다 | `FrameEditorView.tsx:299-300` |
| F25 | 프레임 이름 `_` 판정은 **세 축**이다: 서버 등록 = `validateFrameNameForServer`(**하드 거부**) / 로컬 저장 = `validateFrameName` + `underscoreWarning`(**비차단 경고**) / 저장 전 선검증 = `isFileNameSafe`(길이·`_` 무관) | `15 §6` Step 14~16 절, `frameSavePolicy.ts` |
| F26 | PIN 3종의 401 의미가 다르다. `verifyMyPin`·`setMyPin`은 `unauthorized:"reject"`라 **1회 오입력이 로그아웃을 유발하지 않는다**(PIN-2) | `accountService.ts:36-62` |
| F27 | `THIRD-PARTY.md`는 **런타임 배포 의존만** 기재하고 말미에 "개발 전용 의존(`devDependencies`)은 배포물에 포함되지 않으므로 목록에서 제외한다"고 못박고 있다 | `webclient/THIRD-PARTY.md` |
| F28 | 서명 PUT은 **XHR**이고 `requiredHeaders`를 `Object.entries` 순회로 전량 부착하며 **자격 증명을 붙이지 않는다**(M14) | `uploadGateway.ts` 주석 + `uploadRunner.ts:267-285` |
| F29 | dev 서버에는 Service Worker가 없다(`installServiceWorker`는 `import.meta.env.PROD`에서만 등록) → **E2E는 SW 간섭을 받지 않는다** | `main.tsx:49-51`, `15 §6` Step 16 절 |
| F30 | `App.tsx`에서 `Settings`·`Account`·`UserMgmt` 세 화면만 `<PinGate>`로 감싸져 있고, **게스트는 게이트를 통과 없이 지난다**(V22-2) | `App.tsx:188-207`, `14 §10.8` |

### 0.2 미검증 가정 (전부 검증 단계가 매핑돼 있다)

| # | 가정 | 위험 | 검증 단계 / 실패 시 대체 |
|---|------|------|--------------------------|
| A1 | Playwright는 **CORS preflight(OPTIONS)를 라우팅하지 못한다**(알려진 제약). 그래서 목 백엔드를 **교차 오리진**에 두면 `X-MCPhoto-Client`·`Authorization`이 붙는 요청이 preflight 단계에서 실네트워크로 새 나간다 | 전 시나리오 | **X1** — 설계상 회피한다: 목 base URL을 **같은 오리진**(`http://localhost:5173/__mock-api/`)에 둔다(§4.2). 실패해도 preflight 자체가 발생하지 않으므로 가정이 무력화된다 |
| A2 | `page.clock`이 `performance.now()`까지 가짜로 만든다(유휴 판정 시계 — F16) | E5 | **X6** — `clock.install()` 후 `runFor("02:05")`로 경고가 뜨는지 확인. 뜨지 않으면 **실시간 135초** 폴백(테스트 타임아웃 200초) |
| A3 | headless Chromium에서 `OffscreenCanvas`+WebGL2 가공 경로가 뜨거나, 뜨지 않아도 **CPU 폴백으로 완주**한다 | E1 전체 | **X2** — E1이 `Done`까지 가면 참이다. 안 되면 `--enable-unsafe-swiftshader` 추가 → 그래도 안 되면 `headless: false`(Windows 로컬은 가능) |
| A4 | headless Chromium의 WebCodecs H.264 인코딩 가용성 | 없음(설계상) | **X4** — `SendTimelapse: false`를 기본 시드로 두어 업로드 관측을 1파일로 고정한다. 타임랩스 `null`은 계약상 합법(VF-6)이라 E1 완주에 영향이 없다. 실제 mp4 검증은 **V18**(실측) |
| A5 | CDP `Storage.overrideQuotaForOrigin(origin, 0)`으로 OPFS 쓰기 실패를 유발할 수 있다 | E6 | **X5** — 실패하면 **E6을 자동화에서 내리고 V19-6(할당량 소진 실측)으로 확정**한다(§7.3) |
| A6 | Playwright WebKit(Windows 빌드)이 앱 부팅·IndexedDB·OPFS를 지원한다 | webkit 프로젝트 | **X8** — 부팅 스모크가 실패하면 webkit 프로젝트를 **주석이 아니라 문서 기록과 함께 비활성**하고 Safari 검증은 전량 V(V7·V23-5·V24-2·V25-4)로 남긴다 |
| A7 | Vite dev의 SPA 폴백이 `/oauth2callback`에 `index.html`을 준다(`appType` 미지정 = `spa` 기본값) | E1b 등 로그인 전량 | **X3** — `page.goto("/oauth2callback?…")`이 앱을 띄우는지 확인. 아니면 `vite.config.ts`가 아니라 **테스트에서 `page.goto("/?…")` 대신 `history.pushState` 후 리로드**하는 우회는 불가하므로, 그때는 dev 서버에 미들웨어를 추가하지 않고 **OAuth 시나리오 전량을 V21로 강등**한다 |
| A8 | Google authorize 요청을 `route.abort()` 한 뒤에도 같은 탭의 `sessionStorage`(pending)가 살아 있다 | 로그인 전량 | **X3** — `fakeLogin` 성공 여부로 즉시 판명된다. 실패하면 abort 대신 **302 fulfill**(§5.3 대안)로 바꾼다 |
| A9 | Playwright의 TS 로더가 `tsconfig.json`의 `paths`(`@ui/*` 등)를 해석한다 | E22 등 | **X7** — 안 되면 spec에서 상대 경로(`../../src/ui/strings`)로 import한다 |
| A10 | 현재 웹 vitest 총계가 **1926(84파일)** 이다(`15 §7` 기재값) | 회귀 판정 | **X1** — 착수 시 `npx vitest run`으로 기준선을 실측하고, 이후 단계는 **감소가 없음**만 확인한다(E2E는 vitest 수를 늘리지 않는다) |

---

## 1. 이 Step이 푸는 문제 5개

| # | 문제 | 답 |
|---|------|-----|
| P1 | 저장소에 Playwright가 **없다**. Step 11이 E2E 2건을 이월했다 | §3·§4 — 도입·설정·픽스처 계층을 이 Step이 소유한다 |
| P2 | 로그인이 필요한 시나리오가 11개인데 **실 Google 인증을 쓸 수 없고, 코드에 백도어를 넣으면 AUTH-1·AUTH-4가 깨진다** | §5 — **하네스 쪽에서만** authorize 이동을 가로채 실제 `oauthCallbackRunner`를 태운다. 성립한다(§5.4 논증) |
| P3 | E2E가 vitest 정적 검사(40+ 불변식)를 다시 검증하면 **느리기만 하고 값이 없다** | §9 — E2E의 범위를 "여러 계층이 실제 브라우저에서 맞물리는가"로 못박고, 중복 금지 목록을 명시한다 |
| P4 | 수락 체크리스트(10 §8)가 **누가 무엇으로 확인하는지 없이** 체크박스만 있다 | §10 — 항목마다 `자동(spec)`/`사람(V번호)`를 붙인 3열 표로 재구성한다 |
| P5 | 실측 V1~V25가 **11개 절에 흩어져** 있어 사람이 순서대로 수행할 수 없다(선행·계정·기기가 뒤섞임) | §11 — 기기 3대 × 계정 5종의 **세션 S0~S9** 로 재편한 통합 절차서를 신설한다 |

---

## 2. 산출물 배치 (한눈에)

```
webclient/
  playwright.config.ts              ← 신규(§4.1). tsconfig include에 추가
  package.json                      ← devDependency 1 + 스크립트 3
  tests/e2e/
    fixtures/
      app.ts        ← test.extend: seedSettings + gotoApp + 콘솔 오류 수집
      backend.ts    ← mockBackend(): 라우트 표 + 호출 레코더 + 미등록 경로 501 가드
      auth.ts       ← fakeLogin(): authorize 가로채기 → /oauth2callback 재현
      capture.ts    ← runCaptureToResult(): 홈→…→Result 완주 헬퍼
      opfs.ts       ← listOpfs()/existsOpfs(): page.evaluate 기반 관측
      users.ts      ← 계정 픽스처 5종(guest 제외 4종 + PIN 미설정)
      visibility.ts ← 탭 hidden 에뮬레이션(E19)
    guest-flow.spec.ts        ← 이월분 ①  E1 · E23 · E10 · E11 · E19 · E21
    upload-qr.spec.ts         ← 이월분 ②  E1b · E2 · E7 · E9 · E12 · E24
    auth-session.spec.ts      ← E3-1 · E3-2 · E3b · E4
    offline-storage.spec.ts   ← E8 · E20 · E6
    idle-and-recovery.spec.ts ← E5 · E14
    roles-and-pin.spec.ts     ← E15 · E16 · E17 · E18
    frame-authoring.spec.ts   ← E13
    strings-catalog.spec.ts   ← E22
docs/web-client/
  10-testing-and-acceptance.md      ← §5 표에 "자동화 상태" 열 · §7 성능 결과 · §8 3열 재구성
  16-field-verification-runbook.md  ← 신규(§11) V1~V25 통합 절차서
  11-wbs.md · 12 · 14 · 15 · README ← 갱신(§13)
docs/design/
  web-step17-e2e-and-acceptance.md  ← 이 문서
  README.md                         ← §3.1에 등재
```

**`src/**`는 한 글자도 바뀌지 않는다.** 이것이 이 Step의 가장 강한 non-goal이다(§17).

---

## 3. Playwright 도입

### 3.1 버전과 설치

| 항목 | 값 | 근거 |
|------|-----|------|
| 패키지 | `@playwright/test` | 러너·assertion·fixture가 한 패키지다. `playwright` 단독 패키지는 쓰지 않는다 |
| 버전 핀 | **`"1.49.1"`**(캐럿 없음) | 저장소 관례(`01 §7` "버전 핀 고정 필수" · `mp4-muxer@5.2.2`·`qrcode-generator@2.0.4` 선례). **최소 요구는 1.45**(`page.clock` — §4.6에서 쓴다). 더 최신 안정판으로 올릴 때도 **정확 핀**을 유지하고 `package-lock.json`을 함께 커밋한다 |
| 위치 | `devDependencies` | 배포물(`web/kiosk/`)에 들어가지 않는다 |
| `THIRD-PARTY.md` | **기재하지 않는다** | F27 — 그 문서는 배포물에 포함되는 런타임 의존만 다룬다 |
| 브라우저 바이너리 | `npx playwright install chromium webkit` | **네트워크가 필요한 1회 작업**이다. CI·새 클론에서 선행 조건이며 `package.json` 스크립트로 노출한다 |

```jsonc
// package.json — 추가분만
"scripts": {
  "e2e": "playwright test",
  "e2e:chromium": "playwright test --project=chromium",
  "e2e:install": "playwright install chromium webkit"
},
"devDependencies": { "@playwright/test": "1.49.1" }
```

`.gitignore`는 **손대지 않는다**(F2 — 이미 있다). `tsconfig.json`의 `include`에 `"playwright.config.ts"`를 **추가**한다(F3).

### 3.2 왜 dev 서버(`npm run dev`)를 대상으로 하는가

| 후보 | 판정 |
|------|------|
| **`npm run dev`(5173)** | **채택.** ① env 주입이 서버 기동 시점에 되고(§4.3) ② SW가 없어 캐시 간섭이 0이고(F29) ③ 소스맵으로 실패 추적이 쉽다 |
| `npm run build && npm run preview` | 미채택. **SW가 등록되어**(F29) 캐시·업데이트 대기가 시나리오에 끼어든다. SW 자체 검증은 **V25-1·V25-2**(배포본 실측)가 소유한다 |
| 배포본(kiosk 사이트) | 미채택. CSP·실서버가 붙어 목을 쓸 수 없다. 배포본 검증은 V13·V21-2·V25-*가 소유한다 |

포트는 **5173 고정**이다(F4). `webServer.reuseExistingServer`를 켜 두면 개발자가 이미 띄운 서버를 재사용한다.

---

## 4. 하네스 아키텍처

### 4.1 `playwright.config.ts`

```ts
import { defineConfig, devices } from "@playwright/test";

const PORT = 5173;
const BASE = `http://localhost:${PORT}`;

export default defineConfig({
  testDir: "./tests/e2e",
  // 6컷 완주(3초 카운트다운 × 6 + 합성 + 보관)가 가장 긴 시나리오다.
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
        permissions: ["camera"],
        launchOptions: {
          args: [
            // 합성 카메라(기본 패턴). y4m 파일은 쓰지 않는다 — §4.5 근거.
            "--use-fake-device-for-media-stream",
            // headless에서 WebGL2(뷰티 필터 경로)를 SwiftShader로 띄운다.
            "--enable-unsafe-swiftshader",
            // 셔터음 자동재생(앱에 폴백이 있어 필수는 아니다 — 09 §2.1과 같은 플래그).
            "--autoplay-policy=no-user-gesture-required",
          ],
        },
      },
    },
    {
      name: "webkit",
      // 카메라가 필요한 시나리오는 제외한다(§6).
      grepInvert: /@camera/,
      use: { ...devices["Desktop Safari"] },
    },
  ],
  webServer: {
    command: "npm run dev",
    url: BASE,
    reuseExistingServer: !process.env.CI,
    timeout: 60_000,
    env: {
      // ⚠️ 목 백엔드를 **같은 오리진**에 둔다(§4.2 — preflight 회피).
      VITE_BACKEND_BASE_URL: `${BASE}/__mock-api`,
      // 값이 있어야 [Google로 로그인]이 렌더된다(F5). 실 client_id가 아니다.
      VITE_GOOGLE_CLIENT_ID: "e2e-client-id.apps.googleusercontent.com",
      VITE_BACKEND_API_KEY: "e2e-gate-key",
      VITE_HOSTING_BASE_URL: `${BASE}/__mock-download`,
      VITE_APP_VERSION: "0.0.0-e2e",
    },
  },
});
```

> ⚠️ `--use-fake-ui-for-media-stream`은 **쓰지 않는다.** 권한 프롬프트를 통째로 우회해 버리면
> 권한 경로가 검증에서 빠진다. 권한은 `permissions: ["camera"]`로 준다(권한 **거부** 시나리오는
> 별도 컨텍스트에서 권한을 주지 않으면 되고, 그 항목은 V4가 소유한다).

### 4.2 백엔드 목을 **같은 오리진**에 두는 이유 (설계상 가장 중요한 결정)

앱의 모든 백엔드 호출에는 `X-MCPhoto-Client`(+ 로그인 시 `Authorization`)가 붙고 본문은
`application/json`이다 — 전부 **CORS 단순 요청이 아니다**. 목 base URL이 교차 오리진이면
브라우저가 **preflight(OPTIONS)를 먼저** 보내는데, **Playwright의 `page.route`는 preflight를
가로채지 못한다**(A1). 그러면 OPTIONS가 실네트워크로 나가 실패하고, 본 요청은 아예 발생하지 않는다.

→ `VITE_BACKEND_BASE_URL`을 **`http://localhost:5173/__mock-api`** 로 준다. 같은 오리진이므로
CORS 자체가 성립하지 않고, `page.route("**/__mock-api/**")`가 **모든 요청을 100% 가로챈다**.

같은 이유로 **서명 PUT의 `putUrl`도 목이 같은 오리진으로 발급**한다
(`http://localhost:5173/__mock-storage/{sessionId}/{kind}`). 그 대가로 **`OPTIONS 204`는 E2E에서
관측할 수 없다** — 그 확인은 실버킷이 필요한 **V20-1**이 계속 소유한다(§7.2에 명시).

> 부수 효과: dev 서버는 `/__mock-api/...`를 모르지만 라우트가 항상 먼저 가로채므로 도달하지 않는다.
> 혹시 가로채지 못한 경로가 있으면 Vite의 SPA 폴백이 **`index.html`(200, HTML)** 을 돌려주어
> 조용한 오해가 생긴다 → §4.4의 **미등록 경로 가드**가 이것을 큰 소리로 실패시킨다.

### 4.3 픽스처 계층 (`순수 코어 + 얇은 래퍼`의 E2E 판 — `15 §3.1`)

```
tests/e2e/fixtures/
  backend.ts   ── mockBackend(page, overrides) → { calls, setUser, fail(path, status) }
  auth.ts      ── fakeLogin(page, user)         (backend.ts의 auth/google 응답을 세팅한다)
  app.ts       ── test.extend({ app })          (seedSettings → gotoApp → 콘솔 오류 수집)
  capture.ts   ── runCaptureToResult(page, {cuts, slots})
  opfs.ts      ── listOpfs(page, path) / existsOpfs(page, path)
  users.ts     ── USERS.user / USERS.tempUser / USERS.manager / USERS.admin / USERS.noPin
  visibility.ts── emulateHidden(page) / emulateVisible(page)
```

**규칙**: spec 파일에는 **시나리오 서술만** 남기고 브라우저 조작 세부는 픽스처에 둔다.
`getByRole`/`getByText`의 문구는 **`@ui/strings`에서 import**한다(F22 — testid가 없다.
리터럴을 spec에 복사하면 문구 변경 시 어디가 깨졌는지 알 수 없다).
문구가 리터럴인 3곳(Guide `"촬영 시작"`·Capture `"바로 촬영"`·CutSelect `aria-label`)만
픽스처 상단에 **상수로 모아** 둔다(F23).

### 4.4 `mockBackend()` — 라우트 표와 가드

| 경로(base 상대) | 메서드 | 기본 응답 | 쓰는 시나리오 |
|------|--------|-----------|----------------|
| `auth/google` | POST | `{ token, expiresIn: 3600, user }` — `setUser()`가 바꾼다 | 로그인 전량 |
| `health` | GET | `{ ok: true }` | 진단(선택) |
| `frames/default` | GET | `[]` → fallback 프레임 1개로 촬영(F21) | 촬영 전량 |
| `frames` | POST | `{ frame: {...}, upload: {...} }` | E13(서버 등록) |
| `frames/{id}` | DELETE | `{ deleted: true }` | (미사용) |
| `accounts` | GET | 목록 4명(자기 자신 포함) | E18 |
| `accounts/me/pin/verify` | POST | 204(성공) / 401(불일치) | E16·E17·E18 |
| `accounts/me/pin` | PUT | 204 | (선택) |
| `accounts/{id}`·`{id}/role`·`{id}/pin` | DELETE/PUT/PUT | 204 | E18 |
| `accounts/me/qr-usage` | GET | `{role:"temp_user", blocked, reason, …}` | E24 |
| `config/temp-user-limits` | GET/PUT | `{qrHours:48, qrCount:30}` | (선택) |
| `uploads/prepare` | POST | `{ bucket, uploads:[{kind, putUrl: __mock-storage/…, downloadUrl, requiredHeaders}] }` | E2·E7·E9·E12 |
| `uploads/commit` | POST | `{ id, downloadPageUrl, …}` | E1b·E2 |
| `__mock-storage/**` | PUT | 200 | E2(헤더 관측) |
| **그 밖의 `__mock-api/**`** | — | **`501` + `calls`에 `unhandled` 기록** | 전량(가드) |

- `requiredHeaders`는 실서버와 같은 형태로 **2개**를 준다:
  `{"Content-Type": "image/jpeg", "x-goog-meta-firebaseStorageDownloadTokens": "e2e-token"}`.
  E2가 PUT 요청 헤더에 **둘 다** 있고 `authorization`이 **없음**을 확인한다(M14 + 자격 증명 미부착).
- `calls`는 `{method, path, headers, bodyJson}` 배열이다. 순서 단언(prepare → PUT → commit)과
  "요청 0건" 단언(E1·E23)이 전부 이 배열 위에서 이루어진다.
- **미등록 경로 501 가드**가 이 픽스처의 핵심이다. 앱이 새 엔드포인트를 부르기 시작하면
  조용히 통과하지 않고 그 spec이 실패한다.

### 4.5 카메라·GPU·인코더

| 항목 | 결정 | 근거 |
|------|------|------|
| 카메라 | `--use-fake-device-for-media-stream`의 **기본 합성 패턴** | 픽셀 정확도는 **골든 이미지(vitest)** 가 이미 고정한다(`10 §4`). E2E에 결정적 픽셀이 필요 없다 |
| `--use-file-for-fake-video-capture=<y4m>` | **쓰지 않는다**(파일도 커밋하지 않는다) | 바이너리 픽스처를 늘릴 값이 없다. 나중에 결정적 프레임이 필요해지면 `tests/e2e/fixtures/camera.y4m`을 추가하고 이 플래그만 붙이면 된다 — 그 자리를 주석으로 남긴다 |
| WebGL2 | `--enable-unsafe-swiftshader` | headless에서 뷰티 필터의 Worker WebGL2 경로가 뜨게 한다. 안 떠도 CPU 폴백이 있어 실패하지 않는다(A3) |
| 타임랩스 | 기본 시드에서 **`SendTimelapse: false`** | 업로드 파일 수를 1로 고정해 순서 단언을 결정적으로 만든다. mp4 실검증은 V18(A4) |
| 셔터음 | 기본 시드에서 **`ShutterSound: false`**, 플래시도 false | 오디오·타이밍 변수를 없앤다 |

### 4.6 시간 제어

기본은 **실시간**이다(WM3 — 앱이 실경과로 판정하므로 가짜 시간을 남용하면 검증 의미가 사라진다).
**유휴(E5)에만** `page.clock`을 쓴다: 120초 + 10초를 실시간으로 기다리면 스위트가 느려지고,
유휴 판정은 이미 `idleCountdown.test.ts`가 값으로 고정하고 있어 E2E의 값은 **"화면·모달·홈 복귀가
실제로 연결되는가"** 뿐이다.

```ts
await page.clock.install();          // goto 전에
// … FrameSelect 진입 …
await page.clock.runFor("02:05");    // 경고 모달
await page.clock.runFor("00:11");    // 홈 복귀
```

카운트다운 촬영(`captureSequence`)에는 **절대 쓰지 않는다** — 미디어 파이프라인과 섞이면
검증 대상이 아니라 하네스를 시험하게 된다.

### 4.7 OPFS 관측

앱은 메인 스레드에서 OPFS를 **쓰지** 않지만(VF-14), **테스트는 `page.evaluate`로 읽어도 된다**.

```ts
export async function listOpfs(page: Page, path: string): Promise<string[]> {
  return page.evaluate(async (p) => {
    let dir = await navigator.storage.getDirectory();
    for (const seg of p.split("/").filter(Boolean)) {
      dir = await dir.getDirectoryHandle(seg);            // 없으면 throw → 호출측이 잡는다
    }
    const out: string[] = [];
    for await (const [name, handle] of (dir as any).entries()) {
      out.push(handle.kind === "directory" ? `${name}/` : name);
    }
    return out.sort();
  }, path);
}
```

이것이 E8(보관이 업로드보다 먼저) · E21(세션 잔재 정리) · E19(부분 컷 미잔존)의 **관측 수단**이다.

---

## 5. OAuth 모킹 전략 — **성립한다**

### 5.1 요구와 제약

- 로그인이 필요한 시나리오: **E1b · E2 · E3 · E3b · E4 · E7 · E9 · E12 · E13 · E15 · E16 · E17 · E18 · E24**(14개).
- **금지**: `src/`에 테스트용 로그인 경로를 만드는 것. `AUTH-1`(`sessionStore.login(` 호출부 1곳)과
  `AUTH-4`(`App.tsx`에 `devLogin` 0건)를 정적 검사가 고정하고 있고, Step 12에서 그런 헬퍼를
  **의도적으로 삭제**한 이력이 있다(`15 §6` Step 12 절).

### 5.2 성립 근거 (F9~F13의 조합)

1. 개시 시점에 앱이 **`state`·`nonce`·`code_verifier`를 자기가 만들어 `sessionStorage`에 저장**하고,
   그중 `state`는 **authorize URL의 쿼리로 노출**된다(F9). → 하네스는 그 URL만 보면 `state`를 안다.
   **PKCE `code_verifier`는 알 필요가 없다** — 클라이언트는 그것을 검증하지 않고 서버로 넘길 뿐이고,
   그 서버가 우리 목이다(F10).
2. 콜백 판정은 **`state` 문자열 일치만** 본다(F10). `nonce` 검증은 서버(=목) 몫이다. → **우회 불가 요소가 없다.**
3. 콜백 소비는 `/oauth2callback` 경로 진입 시 **React 밖 동기 1회**로 일어난다(F13). → `page.goto`면 충분하다.
4. 세션 수립은 `applySession`이 `setToken` → `sessionStore.login()` 순서로 한다(F12).
   → **정상 경로 그대로** 실행되므로 AUTH-1을 건드리지 않는다.

### 5.3 시퀀스 (`fixtures/auth.ts`)

```ts
export async function fakeLogin(page: Page, user: SessionUserLike, token = E2E_TOKEN) {
  backend.setUser(user, token);                    // POST __mock-api/auth/google 응답 준비

  let authorizeUrl: URL | null = null;
  await page.route("https://accounts.google.com/**", async (route) => {
    authorizeUrl = new URL(route.request().url());
    // 실제 이동을 막는다. 앱 페이지는 그대로 살아 있고 sessionStorage(pending)도 유지된다.
    await route.abort();
  });

  await page.getByRole("button", { name: STRINGS.login.google }).click();
  await expect.poll(() => authorizeUrl).not.toBeNull();

  // 앱이 만든 값을 그대로 되돌려준다 — 하네스가 값을 지어내지 않는다.
  const state = authorizeUrl!.searchParams.get("state")!;
  const redirectUri = authorizeUrl!.searchParams.get("redirect_uri")!;   // http://localhost:5173/oauth2callback
  expect(authorizeUrl!.searchParams.get("prompt")).toBe("select_account"); // AUTH-5를 실행 경로에서도 확인
  await page.goto(`${redirectUri}?code=E2E_CODE&state=${encodeURIComponent(state)}`);

  await expect(page.getByText(user.id)).toBeVisible();   // 상단바 계정 라벨
  await page.unroute("https://accounts.google.com/**");
}
```

**대안(A8이 깨졌을 때)**: `route.abort()` 대신
`route.fulfill({ status: 302, headers: { location: `${redirectUri}?code=…&state=…` } })`.
브라우저가 리디렉트를 따라가 같은 결과가 된다. 두 방법 모두 `accounts.google.com`으로 나가는
실제 트래픽은 0이다.

이 시퀀스는 덤으로 다음을 **실행 경로에서** 검증한다:
- `redirect_uri`가 `http://localhost:5173/oauth2callback`으로 정확히 조립된다(`oauthRedirectUri`).
- `prompt=select_account`가 붙는다(AUTH-5 — 정적 검사는 소스만 보지만 여기서는 실제 URL이다).
- 교환 요청 본문에 **`clientKind: "web"`**(AUTH-2)과 비어 있지 않은 `codeVerifier`가 있다
  → `mockBackend`의 `auth/google` 핸들러가 단언한다.

### 5.4 왜 이것이 "백도어"가 아닌가

| 축 | 판정 |
|----|------|
| `src/` 변경 | **0**. `page.route`와 `page.goto`는 브라우저 바깥의 조작이다 |
| AUTH-1 | `sessionStore.login(`을 부르는 곳은 여전히 `oauthCallbackRunner` **1곳**이고, 그 함수가 **실제로** 실행된다 |
| AUTH-4 | `App.tsx`에 아무것도 추가하지 않는다 |
| 검증 가치 | PKCE 생성 · state 저장 · URL 조립 · 콜백 파싱 · 판정 순서 · 교환 요청 조립 · 토큰 주입 순서까지 **전부 실코드**가 돈다. 목으로 대체된 것은 **Google 서버와 우리 백엔드** 두 원격뿐이다 |

### 5.5 그래도 남는 것 (자동화 불가 — V21이 소유)

| 항목 | 이유 |
|------|------|
| V21-1 실 Google 계정 완주 | 실 인증·실계정 |
| V21-2 배포본 CSP에서 리디렉트 통과 | dev에는 CSP가 없다 |
| V21-3 `prompt=select_account`가 **계정 선택 화면을 실제로 띄우는가** | Google UI |
| V21-6 두 도메인(web.app / firebaseapp.com) 모두 성공 | 배포 도메인 |
| V21-9 서버 `OAUTH_REDIRECT_ALLOWLIST` 불일치 시 400 | 실서버 |

E2E는 V21-7(취소)·V21-8(콜백 직접 진입)·V21-10(client_id 미구성)의 **화면 반응**은 재현할 수 있다
(각각 `error=access_denied` 부착 / pending 없이 `/oauth2callback` 진입 / 그 spec만 별도 서버 env).
다만 V21-10은 dev 서버 env를 바꿔야 하므로 **자동화하지 않고 V로 남긴다**(서버 1개 원칙 유지).

---

## 6. WebKit 프로젝트 — 무엇을 돌리고 무엇을 빼는가

| 사실 | 결과 |
|------|------|
| `--use-fake-device-for-media-stream`은 **Chromium 전용 스위치**다. Playwright WebKit에는 동등한 가짜 카메라 주입 수단이 없다 | 카메라가 필요한 시나리오는 WebKit에서 **실행하지 않는다** |
| Playwright의 `permissions: ["camera"]`도 Chromium에서만 의미가 있다 | 동상 |
| Playwright WebKit ≠ Safari(엔진은 같은 계열이나 빌드·플랫폼이 다르다) | **Safari 고유 항목**(OPFS `createWritable` 부재 · `playsinline` · 저장소 회수)은 WebKit 프로젝트가 통과해도 **검증된 것이 아니다** → V7·V23-5·V24-2·V25-4가 계속 소유한다 |

**구현 방법**: 카메라가 필요한 테스트에 `{ tag: "@camera" }`를 달고, webkit 프로젝트에
`grepInvert: /@camera/`를 준다(§4.1). 결과적으로 WebKit이 도는 것은:

| spec | 시나리오 |
|------|----------|
| `auth-session.spec.ts`(E4만) | JWT 미저장 |
| `roles-and-pin.spec.ts` | E15 · E16 · E17 · E18 |
| `frame-authoring.spec.ts` | E13 |
| `strings-catalog.spec.ts` | E22 |
| `idle-and-recovery.spec.ts` | E5(FrameSelect에서 대기 — 카메라 불필요) · E14 |

WebKit이 스모크조차 실패하면(A6) **프로젝트를 남긴 채 비활성**하고 `10 §6`에 그 사실과 사유를 적는다.
"돌려 봤더니 안 돌아서 뺐다"를 기록으로 남기는 것이 조용히 지우는 것보다 낫다.

---

## 7. E1~E24 자동화 판정표 (26개 — E1b·E3b 포함)

판정: **자동**(spec이 전부 검증) · **부분**(핵심은 자동, 일부는 V) · **불가**(V로 넘긴다).

| # | 시나리오 | 판정 | spec / 레버 | 남는 것 |
|---|----------|:----:|-------------|---------|
| E1 | 게스트 촬영 완주 | 자동 | `guest-flow` · fake camera + fallback 프레임 · `calls`에 `uploads/*` 0건 | 실카메라 품질 = V1·V14 |
| E1b | 로그인 촬영 완주 | 자동 | `upload-qr` · `fakeLogin(USERS.user)` → `Qr` → QR canvas 렌더 확인 | 폰 스캔 = V21-5 |
| E2 | 업로드 3단계 | **부분** | `upload-qr` · prepare→PUT→commit 순서·본문·**`requiredHeaders` 전량**·`authorization` 부재 | **`OPTIONS 204`는 관측 불가**(§4.2) → V20-1·V20-2 |
| E3 | 로그아웃 후 토큰 미부착 | **재정의(§7.1)** | `auth-session` · E3-1/E3-2로 분해 | "게스트 익명 업로드" 자체가 제품에 없다 |
| E3b | 재로그인 후 토큰 교체 | 자동 | `auth-session` · A로 로그인→업로드(Bearer A)→로그아웃→B로 로그인→업로드(**Bearer B**) | — |
| E4 | JWT 미저장(M2) | 자동 | `auth-session` · localStorage·sessionStorage·cookie·**모든 IndexedDB 레코드**에서 토큰 문자열 0건 | 배포본 관측 = V21-4 |
| E5 | 유휴 타임아웃 | 자동 | `idle-and-recovery` · `page.clock`(§4.6) · FrameSelect에서 대기 → 경고 → 홈 · **계정 라벨 유지** | 실기기 체감 = V11 |
| E6 | 저장 실패 표시(M4) | **부분/조건부** | `offline-storage` · **CDP `Storage.overrideQuotaForOrigin(origin, 0)`** 을 `Result` 진입 후 적용(§7.3) | A5가 깨지면 **불가** → V19-6 |
| E7 | 업로드 실패 시 QR 미노출(M5) | 자동 | `upload-qr` · prepare 500 → QR 없음 + 사유 문구 + [완료] 활성 | 오프라인 실동작 = V20-6 |
| E8 | 로컬 보관이 업로드보다 먼저(M6-W) | 자동 | `offline-storage` · **prepare 라우트 핸들러 안에서** `listOpfs("results")`가 이미 비어 있지 않음을 확인 | 실기기 지연 = V19-2 |
| E9 | 최소 1개 불변식(M7) | 자동 | `upload-qr` · `SendPhoto:false`+`SendTimelapse:false` → `uploads/*` 0건 + "전송할 결과물이 없습니다." | — |
| E10 | 프레임 고정(M11) | 자동 | `guest-flow` · `Capture`에 프레임 변경 컨트롤 부재(버튼 2개뿐) | — |
| E11 | 컷 선택 개수(M12) | 자동 | `guest-flow` · 3/4 선택 시 [다음] disabled, 4/4에서 enabled, 5번째 클릭 무효 | — |
| E12 | 세션 ID 형식(M13) | 자동 | `upload-qr` · prepare 본문 `sessionId`를 **도메인 `isValidSessionId`로** 검사(정규식 재작성 금지) | — |
| E13 | 프레임 이름 `_`(M15) | **부분(재정의 §7.4)** | `frame-authoring` · manager 로그인 → 편집기 → PNG 주입 → 이름 `a_b` → **서버 등록 체크 on 저장 → 거부**, 체크 off는 **경고만** | 실서버 등록 = V24-4 |
| E14 | 전역 예외 복구(M16) | 자동 | `idle-and-recovery` · `page.evaluate(() => setTimeout(() => { throw … }))` → 홈 + 토스트 + **로그인 유지** | — |
| E15 | 권한 게이트(M10) | **부분** | `roles-and-pin` · `USERS.user`로 로그인 → `FrameSelect`에 [프레임 만들기] 부재 | "액션 직접 호출 거부"는 **내부 함수 호출**이라 E2E 범위 밖 → 단위 테스트가 이미 고정(§9) |
| E16 | PIN 5회 실패 + 5분 잠금 | 자동 | `roles-and-pin` · verify를 401 고정 → 5회 → 모달 닫힘 + `localStorage["mcphoto.pinLock.v1"]` 존재 → **리로드 후 재로그인** → 잠금 문구 | 실계정 = V22-5 |
| E17 | PIN 401 오해 방지 | 자동 | `roles-and-pin` · 1회 오입력 → **계정 라벨 유지** + "(1/5)" | 실계정 = V22-3 |
| E18 | 역할 매트릭스 | 자동 | `roles-and-pin` · manager 로그인 → `accounts` 목록 목 → 다른 manager 행에 [PIN] 없음·[삭제] 있음 · 콤보에 `admin` 없음 · 자기 행 액션 없음 · **좁은 뷰포트 카드에서도 동일** | 실계정 = V25-5 |
| E19 | 탭 hidden 취소(WM4) | **부분** | `guest-flow` · `visibility.ts`가 `document.hidden`/`visibilityState`를 덮고 `visibilitychange` 발화 → 홈 + **OPFS `sessions/` 비어 있음** | 진짜 탭 전환 = V16 |
| E20 | 오프라인 촬영 | 자동 | `offline-storage` · `context.setOffline(true)` → 프레임 목록 폴백(**안내 문구 없음** — `Ready`) + 촬영·보관 성공 | 실기기 = V23-2 |
| E21 | 새로고침 | 자동 | `guest-flow` · 촬영 중 `page.reload()` → Home + OPFS `sessions/` 비어 있음 | — |
| E22 | 문구 카탈로그 | **부분** | `strings-catalog` · 6화면의 주요 문구가 `@ui/strings` 값과 **문자열 일치**(하드코딩 우회 검출) | 카탈로그 ↔ `analysis/13 §14` **문서 대조**는 사람 검토(§10 공통 항목) |
| E23 | 게스트 QR 게이트(VF-11) | 자동 | `guest-flow`(이월분) · `Done` 도달 · `uploads/*` 0건 · **`EnableQrDelivery`가 localStorage에서 불변** | — |
| E24 | TempUser 한도 초과 게이트 | 자동 | `upload-qr` · `qr-usage`를 blocked로 목 → `Done` · 설정 불변 → **해제 후 재로그인** → `Qr` 진입 | 실계정 한도 = V22-7 계열 |

**요약: 자동 19 · 부분 6 · 재정의 1(E3).** 완전 불가 판정은 없지만, **E2·E6·E13·E15·E19·E22의 일부는
구조적으로 실측·단위 테스트가 소유한다.**

### 7.1 E3 재정의 — "게스트 익명 업로드"는 **제품에 존재하지 않는 경로**다

`10 §5`의 E3은 "**`qrEffectivePolicy`를 목으로 `true` 고정**해 업로드를 실행시키고 헤더를 관측"하라고
적혀 있다. 이것은 **웹 클라이언트에서 성립하지 않는다**:

1. `qrEffectivePolicy`는 순수 도메인 함수이고, 하네스에서 그 판정을 바꾸려면 **모듈 치환**(alias 조작)
   또는 **소스 백도어**가 필요하다. 전자는 "테스트한 앱이 배포할 앱이 아니게" 만들고, 후자는 금지다.
2. 게스트가 `Qr`에 도달하는 경로가 **없다**(F14). 그리고 **그것이 곧 E23/VF-11**이다 — 검증하려는
   불변식(익명 업로드에 토큰이 없다)의 **전제 상황 자체를 다른 불변식이 금지**한다.
3. `auth: "optional"`인 호출은 `uploads/prepare`·`uploads/commit` **둘뿐**이다(F7·`uploadGateway`).
   나머지 인증 호출은 전부 `required`라 **토큰이 없으면 요청이 아예 나가지 않는다**(`backendClient.ts:91-95`).
   → 로그아웃 후 "Bearer 없는 요청"을 만들 방법이 제품 안에 없다.

**따라서 E3을 다음 3개로 분해한다.**

| 새 번호 | 내용 | 자동화 |
|---------|------|:------:|
| **E3-1** | 로그인 상태의 `uploads/prepare`에 `Authorization: Bearer <A>`가 **있다** | 자동 |
| **E3-2** | 로그아웃 후 같은 흐름을 반복하면 **`uploads/*` 요청이 0건**이다(익명 업로드 사건이 발생하지 않는다) | 자동 |
| **E3b** | 재로그인 후 prepare의 Bearer가 **B**다(A의 잔존이 아니다) | 자동 |
| (보조) | `authStore`: 세션 null → `getToken()` null · `installTokenLifecycle` 구독 | **기존 vitest가 이미 고정**(`15 §3.4` M1 배선) |

이 판정을 `10 §5`의 E3 행에 **그대로 반영**한다(§13). "웹에서는 그 상황을 만들 수 없다"를
문서에 남기지 않으면 다음 세션이 또 목을 만들려 시도한다.

### 7.2 E2 — `OPTIONS 204`는 왜 자동화에서 빠지는가

§4.2대로 서명 PUT을 **같은 오리진**으로 발급하므로 preflight가 애초에 발생하지 않는다.
교차 오리진으로 두면 Playwright가 preflight를 가로채지 못해(A1) 테스트가 실네트워크에 의존하게 된다.
→ **E2는 "PUT 200 + `requiredHeaders` 전량 + 자격 증명 0"까지** 자동화하고,
`OPTIONS 204 → PUT 200`의 실왕복은 **V20-1**(실버킷 CORS)이 계속 소유한다.

### 7.3 E6 — 유일하게 조건부인 항목

저장 실패 토스트가 뜨려면 **`finalBlob`이 있는 상태에서 `opfs.write`가 false**여야 한다(F18).
후보 레버를 전부 검토했다:

| 레버 | 판정 |
|------|------|
| `navigator.storage.getDirectory` 제거(init script) | **틀렸다.** 컷도 못 읽어 합성이 실패 → `finalBlob === null` → `skipped`(토스트 없음). E6이 아니라 다른 경로를 보게 된다 |
| OPFS Worker 스크립트를 `page.route`로 스텁 | 프로토콜(`opfsProtocol.ts`) 문자열에 하네스를 결합시킨다. 앱이 프로토콜을 바꾸면 **테스트가 조용히 무의미**해진다 → 채택하지 않는다 |
| **CDP `Storage.overrideQuotaForOrigin(origin, 0)`** | **채택.** `Result` 도달 **후**(컷·합성 완료 후) 적용하면 `results/` 쓰기만 실패한다. Chromium 전용이고 `@camera`+chromium 프로젝트에서만 돈다 |

A5가 거짓으로 판명되면 **E6을 자동화 목록에서 내리고** `10 §5` E6 행에 "자동화 불가 —
할당량 소진은 브라우저가 만들어 준다. **V19-6**" 이라고 적는다. 억지로 통과시키지 않는다.

### 7.4 E13 — M15의 실제 축은 **서버 등록**이다

`15 §6`(Step 14~16 절)과 `frameSavePolicy`가 정한 세 축(F25) 때문에,
"`_` 포함 이름 **저장 거부**"는 **로컬 저장에서는 일어나지 않는다**(비차단 경고다).
E13은 따라서 다음 두 단언으로 쓴다:

- 서버 등록 체크 **on** + 이름 `a_b` → **거부 문구**가 뜨고 `POST frames` 요청이 **0건**이다.
- 체크 **off** → 저장이 **성공**하고 경고 문구(`frames.underscoreWarning`)만 보인다.

`10 §5` E13 행도 이 문구로 정정한다(§13).

---

## 8. spec 파일별 시나리오

| 파일 | 태그 | 시나리오 | 선행 픽스처 |
|------|------|----------|-------------|
| `guest-flow.spec.ts` **(이월 ①)** | `@camera` | E1 · E23 · E10 · E11 · E19 · E21 | `mockBackend`(빈 프레임 목록) · `seedSettings` |
| `upload-qr.spec.ts` **(이월 ②)** | `@camera` | E1b · E2 · E7 · E9 · E12 · E24 | + `fakeLogin(USERS.user)` / `USERS.tempUser` |
| `auth-session.spec.ts` | E4만 태그 없음, 나머지 `@camera` | E3-1 · E3-2 · E3b · E4 | `fakeLogin` × 2계정 |
| `offline-storage.spec.ts` | `@camera` | E8 · E20 · E6 | `context.setOffline` · CDP 쿼터 |
| `idle-and-recovery.spec.ts` | — | E5 · E14 | `page.clock` |
| `roles-and-pin.spec.ts` | — | E15 · E16 · E17 · E18 | `fakeLogin(USERS.user/manager)` · PIN 라우트 |
| `frame-authoring.spec.ts` | — | E13 | `fakeLogin(USERS.manager)` · `setInputFiles` |
| `strings-catalog.spec.ts` | — | E22 | — |

**촬영 헬퍼**(`capture.ts`)의 기본 시드: `CutCount: 6` · `CountdownSec: 3` · `FlashMode:false` ·
`ShutterSound:false` · `SendTimelapse:false` · `SaveLocalCopy:true` · `EnableQrDelivery:true`.
각 컷은 [바로 촬영]을 눌러 카운트다운을 건너뛴다(F23) → **6컷이 수 초에 끝난다**.
슬롯 4개(fallback 프레임 — F21)이므로 컷 선택은 4장이다.

---

## 9. E2E가 하지 않는 것 (중복 금지 — `15 §3.4`와의 경계)

`15 §3.4`의 40+ 불변식은 **테스트가 소스를 읽어** 고정하고 있다. E2E로 다시 확인하지 않는다.

| 이미 고정된 것 | 고정 수단 | E2E에서 하지 않는다 |
|----------------|-----------|---------------------|
| WM1 CSS 반전 금지 · `<video>` 미렌더 | 정적 grep | 픽셀 좌우 비교 |
| M2 / M2-a / M2-b 저장소 경계 | 소스 grep | **E4는 예외** — "실제로 브라우저 저장소에 없는가"는 grep이 증명할 수 없다 |
| AUTH-1~5 · PIN-1~5 · SET-1~5 · FR-1~15 · ACC-1~4 · SW-1~3 · DIAG-1 | 소스 grep | 소스 문자열 검사 재현 |
| 도메인 값(크롭·슬롯·역할·세션 ID 형식·컷 수·QR 정규화) | `docs/spec-vectors` 271케이스 | 값 재검증 — **E12는 도메인 함수를 호출해** 판정만 재사용한다 |
| 합성·필터 픽셀 | 골든 이미지 | 픽셀 비교 |
| 순서 불변식(M6-W·M7·M8·업로드 3단계) | `resultNext.test.ts`·`uploadRunner.test.ts` | **재현은 한다** — 단위 테스트는 목 위의 순서고, E2E는 **실브라우저에서 실제로 그 순서로 일어나는가**다. 이것이 E2E의 존재 이유다 |
| 액션 가드(내부 함수 직접 호출 거부) | 화면 로직 단위 테스트 | E15의 후반부 |

한 문장 규칙: **"소스를 읽으면 알 수 있는 것"은 vitest, "브라우저에서 돌려 봐야 아는 것"은 Playwright,
"사람 눈·하드웨어가 필요한 것"은 V.**

---

## 10. 수락 체크리스트(`10 §8`) 재구성

### 10.1 형식

각 항목을 **3열**로 바꾼다. 체크박스는 남기되 **무엇으로 확인했는지**가 항상 옆에 있게 한다.

```markdown
| ✔ | 항목 | 확인 수단 |
|:-:|------|-----------|
| [ ] | JWT를 어떤 저장소에도 쓰지 않는다(M2) | **자동** `auth-session.spec.ts` E4 + 정적 M2/M2-a/M2-b |
| [ ] | 카메라 Ready 게이트 통과 후에만 시퀀스가 시작된다 | **자동** `guest-flow` E1 + `cameraService` 단위 / **사람** V1 |
| [ ] | 타임랩스가 mp4/H.264/무음/10~15초이거나 null로 축소된다 | **사람** V18-1·V18-2·V18-6 |
```

확인 수단 표기는 **세 가지뿐**이다: `자동 {spec} {E번호}` · `정적 {불변식}` · `사람 {V번호}`.
**빈칸을 남기지 않는다** — 수단을 못 적는 항목은 검증 계획이 없다는 뜻이므로 그 자리에서
V 항목을 신설하거나 항목을 삭제한다.

### 10.2 분류 결과 (developer가 표에 옮길 값 — 근거를 미리 확정해 둔다)

| 절 | 자동으로 닫히는 항목 | 사람이 닫아야 하는 항목 |
|----|---------------------|--------------------------|
| **공통**(7) | JWT 미저장(E4) · 로그아웃 토큰(E3-1/E3-2/E3b) · 미처리 예외 복구(E14) | 게이트 키 미커밋(**저장소 검토**) · API 실패 가시성(V 전반) · 오류 5종 구분(V21-7~10·V22-6) · 로그에 비밀 없음(V25-8 + 정적 AUTH-3·PIN-1) |
| **P2 촬영**(12) | 세션 ID(E12) · `requiredHeaders`(E2) · 보관 우선(E8) · QR 노출 조건(E7) · 게스트 게이트(E23) · TempUser 게이트(E24) · 유휴 비로그아웃(E5) · 탭 hidden(E19) | 동일 가공(WM1 — V2) · Ready 게이트 체감(V1) · **자동 컷 수 7컷 실촬영**(V22-8) · 타임랩스 실물(V18) |
| **P3 저작**(6) | 이름 `_`(E13) · 편집 권한 게이트(E15 렌더축) | 슬롯 저장 검증·사본 분기·`PUT /frames` 미호출(**정적 FR-9** + 단위) · **WYSIWYG 0px**(V24-3 — 골든은 합성만 본다) |
| **P4 운영**(5) | 역할 매트릭스(E18) · PIN 게이트 fail-closed(E16·E17) · 목록 실패 표시(E18 변형) | 자기 계정 삭제 서버 거부(V25-5) · PIN 재설정 위계(V25-5) |
| **웹 전용**(10) | 오프라인 촬영(E20) · 잔재 정리(E21) · 진행률 XHR(E2 — 진행 이벤트 관측) | CORS-clean 프레임(V23-3) · 실경과 타이머(V18-5) · `results/` 용량 정책(V22-10) · 진단 정직성(V25-8) · `downloadPageUrl` P1 도메인(V20-4/V21-5) · **CSP 위반 0**(V13·V21-2) · 차이 보고서 0건(문서 검토) |

> ⚠️ **"CSP 위반이 콘솔에 없다"는 dev 서버에서 확인할 수 없다**(로컬에는 CSP가 없다 — V13).
> 다만 E2E가 **콘솔 오류 자체를 수집**해(`app.ts` 픽스처) 어떤 spec에서도 `console.error`·
> 미처리 예외가 발생하면 실패시킨다. 이것은 CSP 검증이 아니라 **회귀 그물**이다.

---

## 11. V1~V25 통합 절차서 — `docs/web-client/16-field-verification-runbook.md`

### 11.1 배치 결정

| 후보 | 판정 |
|------|------|
| `14 §11`로 확장 | 미채택. 14는 이미 752줄이고 **"사람이 할 일"의 원장(ledger)** 이다. 절차서를 붙이면 두 성격이 섞여 현장에서 못 쓴다 |
| **`docs/web-client/16-field-verification-runbook.md` 신설** | **채택.** 번호 체계(00~15)의 다음 자리이고, 현장에서 **인쇄해 들고 다닐 수 있는 단일 문서**가 된다 |
| `webclient/docs/` 아래 | 미채택. 설계 문서 세트와 분리되면 인덱스에서 사라진다 |

**중복 방지 규칙(문서 상단에 명시)**:
> 이 문서는 **순서·그룹핑·진행 기록**만 소유한다. 각 항목의 *정의*와 *"왜 자동화가 안 되나"* 의
> 진실원은 [`14 §10`](./14-handoff-and-user-actions.md)이다. **항목을 추가·수정할 때는 14 §10을 먼저
> 고치고 여기에는 행만 추가한다.** 두 문서가 어긋나면 14가 사실이다.

`docs/web-client/README.md §1` 표에 **17번째 행**으로 등재한다(§13).

### 11.2 문서 구조

```
0. 이 문서 쓰는 법(3분) — 진실원 규칙 · 결과 기록 방법 · 실패 시 처리
1. 사전 준비 R0 (기기·계정·환경) — 체크리스트
2. 세션 S1~S9 — 각 세션이 "한 자리에 앉아 끝낼 수 있는 단위"
3. 성능 기록 표 (10 §7)
4. 미완·차단 항목 기록란 (왜 못 했는지 · 무엇이 필요한지)
```

각 항목 행 형식:

```markdown
| ✔ | ID | 확인할 것 | 기대 | 선행 | 상세 |
|:-:|----|-----------|------|------|------|
| [ ] | V19-1 | 오프라인 촬영 완주 후 OPFS `results/…/final.jpg` | 파일 존재·크기>0 | R0-4 | [14 §10.4](./14-handoff-and-user-actions.md#104-v19) |
```

### 11.3 사전 준비 R0

| # | 준비물 | 왜 |
|---|--------|-----|
| R0-1 | **Windows PC + Chrome/Edge 111+**, 카메라 2대(1대는 USB) | V5·V22-9(장치 전환) |
| R0-2 | **Android 태블릿(12+) Chrome** · **iPad(iPadOS 17+) Safari** | 주력 기기 2종(`10 §6.1`) |
| R0-3 | **스마트폰 1대**(QR 스캔용, 위 기기와 별개) | V20-4/V21-5 |
| R0-4 | `npm run dev`(localhost:5173) **와** A5 배포본 URL 둘 다 | CSP·SW는 배포본에서만 관측된다(V13·V21-2·V25-1) |
| R0-5 | 실계정 4종: **① temp_user(신규 가입 직후) ② user ③ manager ④ PIN 미설정 계정** | ④는 V22-4 전용이며 **없으면 그 항목을 수행할 수 없다** |
| R0-6 | 개발 PC에 `ffprobe` | V18-1·V18-2 |
| R0-7 | 기기별 `09 §2~§5` 세팅(키오스크 모드·카메라 사전 허용·자동 잠금 해제·PWA 설치) | 세팅 없이 측정한 값은 무의미하다 |
| R0-8 | MC포토 **Windows 앱** 설치본 | V25-3(프레임 zip `Frame\` 인식) |

### 11.4 세션 그룹핑 (기기 × 계정)

| 세션 | 기기 | 계정 | 항목 | 비고 |
|------|------|------|------|------|
| **S1** 기본 흐름 | Windows Chrome | **게스트** | V1 V2 V3 V4 V5 V6 · V8 V9 V10 V11 V12 · V14 V15 V16 V17 · V22-2 · V23-1 V23-2 V23-4 V23-7 · V24-1 V24-7 · V20-5 | 카메라·촬영·저장소·프레임 목록. **로그인 없이 되는 것 전부** |
| **S2** 로그인·업로드 | Windows Chrome | **user**(또는 temp_user) | V21-1 V21-3 V21-4 V21-7 V21-8 · V20-1 V20-2 V20-3 V20-6 V20-7 V20-8 · V18-1 V18-2 V18-3 V18-5 · V19-1~V19-4 V19-6 · V22-1 V22-3 V22-5 V22-6 V22-7 V22-8 V22-10 V22-12 | **S1 완료가 선행**이다. V19-3(폴더 지정)을 먼저 해 두면 V18의 ffprobe가 쉬워진다 |
| **S3** 권한·저작 | Windows Chrome | **manager** | V23-3 V23-8 · V24-3 V24-4 V24-8 · V25-3 V25-4 V25-5 | 서버 프레임·등록·사용자 관리. **Windows 앱 필요**(V25-3) |
| **S4** PIN 최초 설정 | Windows Chrome | **PIN 미설정 계정** | V22-4 | 계정을 소모한다(설정하면 미설정 상태가 사라진다) → **마지막에** 수행하거나 전용 계정을 준비 |
| **S5** 배포본·PWA | Windows Chrome(배포본) | user + manager | V13 · V21-2 V21-6 · V25-1 V25-2 V25-6 V25-7 V25-8 | **재배포 2회**가 필요하다(V25-2) |
| **S6** Android 태블릿 | Android Chrome | 게스트 + user | V1 V2 V14 V15 V16 V17 · V18-4 · V19-5 · V22-9 V22-11 V22-13 · V24-5 · `10 §6.3` 9항목 | 폼팩터·터치·회전 |
| **S7** iPadOS Safari | iPad Safari | 게스트 + user | V7 · V18-4 · V19-5 · V23-5 · V24-2 V24-5 · V25-4(미지원 안내) · `10 §6.3` 9항목 | **OPFS·인코더 하한**의 진짜 검증처. Playwright WebKit이 통과해도 여기서 다시 본다(§6) |
| **S8** QR 스캔 | 스마트폰 | — | V20-4 → **V21-5** | S2에서 만든 QR을 그 자리에서 스캔한다(같은 세션에 붙여도 된다) |
| **S9** 성능·메모리 | 3대 전부 | user | `10 §7` 4측정 + `10 §6.3`의 fps·메모리 | 결과를 `10 §7` 표에 기록 |

**선행 그래프**(문서에 그대로 넣는다): `R0 → S1 → S2 → {S3, S4, S5, S8}` · `R0 → S6` · `R0 → S7` · `S2·S6·S7 → S9`.

### 11.5 결과 기록 규칙

- 각 행은 `[x]` 또는 `[!]`(실패) 또는 `[-]`(수행 불가)로 닫는다. **빈칸으로 남기지 않는다.**
- `[!]`·`[-]`는 §4 "미완·차단 항목"에 **한 줄 사유**를 남긴다(예: "manager 실계정 미발급").
- 실패가 **설계 결함**이면 `docs/web-client/12`에 행을 추가할지 판단하고, **구현 결함**이면
  원인 Step으로 되돌린다(11 Step 17의 롤백 규정).
- ~~V24-6~~ 처럼 **해소된 번호는 재사용하지 않는다**(다른 문서가 번호를 참조한다).

---

## 12. 성능(`10 §7`)과 차이 보고서(`12`)

| 대상 | 처리 |
|------|------|
| `10 §7` 성능 | **E2E로 측정하지 않는다.** headless·SwiftShader 수치는 실기기 예산과 무관하다. S9에서 실측해 `10 §7`에 표로 누적한다(기기·브라우저·버전·값·측정일) |
| `12` 차이 보고서 | E2E·실측에서 **문서에 없는 동작 차이**가 나오면 행을 추가한다. Step 17의 완료 기준이 "미등재 차이 0건"이므로, **추가 없이 끝나면 "전 항목 재확인함"을 문서 상단 갱신일로 남긴다**(조용히 넘어가지 않는다) |

---

## 13. 문서 갱신 지침 (developer 수행)

| 문서 | 갱신 내용 |
|------|-----------|
| `10 §5` | 표에 **"자동화" 열** 추가(자동/부분/불가 + spec 파일). **E3 행을 §7.1대로 재작성**, E6 행에 §7.3 판정, E13 행에 §7.4 문구, E2 행에 "OPTIONS는 V20-1" 각주 |
| `10 §6` | WebKit 프로젝트의 실행 범위와 **제외 사유**(카메라 플래그 부재) 1문단 추가 |
| `10 §7` | S9 실측값 표 |
| `10 §8` | §10.1 형식으로 **전 항목 3열 재구성** |
| `11 Step 17` | 산출물·검증 수치·설계 이탈·미검증(V) 기록 + `[x] 완료` |
| `12` | 신규 차이 행 또는 "전 항목 재확인(2026-08-xx)" |
| `14 §10` | 각 절 상단에 **"→ 통합 절차서 16의 S번호"** 링크 1줄. 항목 정의는 그대로 둔다(진실원 유지) |
| **`16`(신규)** | §11 구조대로 작성 |
| `15 §1·§7` | 재개 표에서 Step 17 행 제거 → **"구현·E2E 완료. 남은 것은 실측(16)"** 으로 교체. §3에 **"E2E 스위트 실행법"** 3줄 추가(`npm run e2e:install` → `npm run e2e`) |
| `README §1` | 17번째 행으로 `16` 등재 + 상태 줄 갱신 |
| `docs/design/README §3.1` | 이 설계 문서 등재(이 Step에서 이미 수행) |

---

## 14. 파일별 역할 요약

| 파일 | 역할 | 하지 않는 것 |
|------|------|--------------|
| `playwright.config.ts` | 프로젝트 2개·webServer·env 주입·타임아웃 | 시나리오 지식 0 |
| `fixtures/backend.ts` | 라우트 표 · 호출 레코더 · **미등록 501 가드** | 화면 조작 |
| `fixtures/auth.ts` | authorize 가로채기 → 콜백 재현 | 세션 위조(=`login()` 직접 호출) |
| `fixtures/app.ts` | 설정 시드 · `goto` · **콘솔 오류 수집** | 백엔드 지식 |
| `fixtures/capture.ts` | 홈→Result 완주 | 단언 |
| `fixtures/opfs.ts` | OPFS 열거 | 쓰기 |
| `fixtures/visibility.ts` | hidden 에뮬레이션 | 실제 탭 전환 흉내 주장 |
| `*.spec.ts` | **시나리오 서술 + 단언만** | 브라우저 조작 세부 |

---

## 15. 구현 단계 (WBS 블루프린트)

> 형식은 [`docs/templates/WBS_BLUEPRINT.md`](../templates/WBS_BLUEPRINT.md). 각 단계는 **self-contained**다.
> **공통 사전**: `cd E:\Study\photobooth\webclient`
> **공통 검증**: `npx tsc --noEmit` → `npx vitest run`(**감소 없음**) → `npx playwright test --project=chromium`

### X1: Playwright 도입 + 설정 + 스모크 + 기준선
- **Context Brief**: 저장소에 Playwright가 없다(이 Step이 도입을 소유한다 — `11 Step 17`). dev 서버는 **5173 고정**이고(`vite.config.ts:48-58`), `webclient/.env`가 없어 그냥 띄우면 로그인 버튼이 숨는다 → **webServer env 주입이 필수**다. 목 백엔드는 반드시 **같은 오리진**(`/__mock-api`)에 둔다 — 교차 오리진이면 CORS preflight를 Playwright가 가로채지 못해 전 시나리오가 깨진다(설계 §4.2).
- **대상 파일**: `webclient/package.json` · `webclient/playwright.config.ts`(신규) · `webclient/tsconfig.json` · `webclient/tests/e2e/smoke.spec.ts`(신규, 임시)
- **선행 조건**: 없음
- **구현 내용**: `@playwright/test@1.49.1`(캐럿 없음) devDependency + 스크립트 3종(§3.1). `playwright.config.ts`는 §4.1 그대로. `tsconfig.json` `include`에 `"playwright.config.ts"` 추가. 스모크 spec: `/`로 이동 → `STRINGS.home.start` 버튼이 보인다 + 콘솔 오류 0. **`.gitignore`·`THIRD-PARTY.md`는 건드리지 않는다**(F2·F27).
- **검증 명령**: `npx vitest run`(**기준선 기록** — A10) · `npm run e2e:install` · `npx playwright test --project=chromium` · `npx tsc --noEmit`
- **완료 기준**:
  - [관측] 스모크가 chromium에서 통과하고 `webServer`가 5173에 자동 기동한다. `npx tsc --noEmit` 오류 0.
  - [non-goal] `src/**`·`.gitignore`·`THIRD-PARTY.md`·`vitest.config.ts`가 **무변경**. `npx vitest run` 통과 수가 기준선과 **같다**(E2E가 vitest에 섞이지 않는다 — F1).
  - [trigger] E2E는 `npx playwright test`에서만 돈다 — `npm test`(vitest)는 영향받지 않는다.
- **롤백**: 신규 2파일 삭제 + `package.json`·`tsconfig.json` diff 되돌림.
- [ ] 완료

### X2: 공용 픽스처 + `guest-flow.spec.ts`의 E1·E23 (이월분 ①)
- **Context Brief**: Step 11이 미룬 이월분 중 하나다. **게스트는 `Qr`에 도달하지 않고 `Done`으로 끝나며 업로드 요청이 0건**(VF-11)임을 실브라우저에서 재현한다. 서버 프레임 목록을 `[]`로 목하면 코드 생성 **fallback 프레임(슬롯 4개)** 으로 촬영이 가능하다(`fallbackFrameSpec.ts`) — 이미지 자산이 필요 없다. 화면에 `data-testid`가 **0건**이므로 셀렉터는 역할·문구 기반이고 문구는 `@ui/strings`에서 import한다.
- **대상 파일**: `tests/e2e/fixtures/{app,backend,capture,opfs,users}.ts`(신규) · `tests/e2e/guest-flow.spec.ts`(신규) · `tests/e2e/smoke.spec.ts`(삭제 — 스모크는 픽스처 테스트로 대체)
- **선행 조건**: X1
- **구현 내용**: §4.3·§4.4·§8. `mockBackend`는 라우트 표 + `calls` 레코더 + **미등록 `__mock-api` 경로 501 가드**. `seedSettings`는 `page.addInitScript`로 `localStorage["mcphoto.settings.v1"]`에 `{schemaVersion:1, values, webExtras}`를 심는다(`CountdownSec:3`·`SendTimelapse:false`). `runCaptureToResult`는 매 컷 [바로 촬영]을 누른다. E1: `Done` 도달 + `uploads/*` 0건. E23: `EnableQrDelivery`가 localStorage에서 **불변**.
- **검증 명령**: `npx playwright test --project=chromium guest-flow` · `npx tsc --noEmit`
- **완료 기준**:
  - [관측] 게스트 6컷 세션이 `Done`까지 완주하고 `calls`에 `uploads/prepare`·`uploads/commit`이 **0건**이다. OPFS `results/`에 폴더가 1개 생긴다.
  - [non-goal] `Qr` 화면이 **한 번도 렌더되지 않는다**. 저장된 `EnableQrDelivery`가 `true` 그대로다. 미등록 백엔드 경로 호출이 0건(501 가드 미발동).
  - [trigger] 촬영 진행은 [바로 촬영] 클릭에서만 — 카운트다운 자연 만료를 기다리지 않는다.
- **롤백**: 신규 spec·픽스처 삭제.
- [ ] 완료

### X3: OAuth 모킹 픽스처 + `auth-session.spec.ts`
- **Context Brief**: 로그인 시나리오 14개의 토대다. **`src/`에 백도어를 넣으면 안 된다**(AUTH-1·AUTH-4). 대신 앱이 `location.assign(authorizeUrl)`로 나가는 요청을 `page.route`로 가로채 **URL 쿼리에서 `state`·`redirect_uri`를 읽고**, `route.abort()` 후 `page.goto(redirectUri + "?code=…&state=…")`로 콜백을 재현한다. `sessionStorage`의 pending은 같은 탭·같은 오리진이라 살아 있고, `oauthCallbackRunner`가 **실제로** 실행되어 `sessionStore.login()`이 정상 경로로 호출된다(설계 §5).
- **대상 파일**: `tests/e2e/fixtures/auth.ts`(신규) · `tests/e2e/auth-session.spec.ts`(신규) · `tests/e2e/fixtures/backend.ts`(auth/google 핸들러 단언 추가)
- **선행 조건**: X2
- **구현 내용**: §5.3 시퀀스. `auth/google` 핸들러는 요청 본문의 **`clientKind === "web"`**(AUTH-2)과 `codeVerifier.length > 0`을 단언한 뒤 `{token, expiresIn:3600, user}`를 준다. 시나리오: **E3-1**(로그인 업로드에 `Bearer A`) · **E3-2**(로그아웃 후 같은 흐름 → `uploads/*` 0건) · **E3b**(재로그인 → `Bearer B`) · **E4**(localStorage·sessionStorage·cookie·**전 IndexedDB 레코드**에 토큰 문자열 0건). E4는 `@camera` 태그를 **달지 않는다**(WebKit에서도 돈다).
- **검증 명령**: `npx playwright test --project=chromium auth-session` · `npx tsc --noEmit`
- **완료 기준**:
  - [관측] `fakeLogin` 후 상단바에 계정 id가 뜨고 주소창에 `code`·`state`가 남지 않는다(`scrubUrl`). E3b에서 두 번째 prepare의 Bearer가 **A가 아니라 B**다.
  - [non-goal] `accounts.google.com`으로 나가는 **실제 네트워크 요청이 0건**이다. `sessionStorage`에 `mcphoto.oauth.pending.v1`이 콜백 뒤 **남지 않는다**. `src/**` 무변경.
  - [trigger] 로그인은 [Google로 로그인] 클릭 → 가로채기 → 콜백 goto **3단**에서만 성립한다. 세션을 직접 주입하지 않는다.
- **롤백**: 신규 2파일 삭제.
- [ ] 완료

### X4: `upload-qr.spec.ts` (이월분 ②)
- **Context Brief**: Step 11이 미룬 두 번째 이월분. 업로드 3단계(prepare → 서명 PUT → commit)를 **실브라우저에서** 재현한다. 서명 PUT은 XHR이고 `requiredHeaders`를 전량 부착하며 **자격 증명을 붙이지 않는다**(M14). `putUrl`은 목이 **같은 오리진**으로 발급한다 — 교차 오리진이면 preflight를 가로챌 수 없다(§4.2). 따라서 **`OPTIONS 204`는 이 spec의 검증 대상이 아니다**(V20-1 소유).
- **대상 파일**: `tests/e2e/upload-qr.spec.ts`(신규) · `tests/e2e/fixtures/backend.ts`(uploads·qr-usage 핸들러)
- **선행 조건**: X3
- **구현 내용**: **E1b**(로그인 완주 → `Qr` → QR canvas 렌더 → [완료] → 홈) · **E2**(호출 순서 `prepare→PUT→commit` + PUT 헤더에 `Content-Type`·`x-goog-meta-firebaseStorageDownloadTokens` **둘 다** 있고 `authorization` **없음**) · **E7**(prepare 500 → QR 없음 + 사유 문구 + [완료] 활성) · **E9**(`SendPhoto:false`+`SendTimelapse:false` → `uploads/*` 0건 + "전송할 결과물이 없습니다.") · **E12**(prepare 본문 `sessionId`를 **도메인 `isValidSessionId`로** 검사) · **E24**(`qr-usage` blocked → `Done` + 설정 불변 → 해제 후 재로그인 → `Qr` 진입).
- **검증 명령**: `npx playwright test --project=chromium upload-qr` · `npx tsc --noEmit`
- **완료 기준**:
  - [관측] `calls` 순서가 `uploads/prepare` → `__mock-storage` PUT → `uploads/commit`이다. commit 본문의 `downloadPageUrl`이 **`VITE_HOSTING_BASE_URL`(P1 자리)** 로 시작한다.
  - [non-goal] PUT 요청에 `authorization`·`x-mcphoto-client`가 **없다**. prepare가 실패한 경우 commit이 **호출되지 않는다**(M8). E9에서 요청이 0건이다(M7).
  - [trigger] 업로드는 `Qr` 진입에서만 시작된다 — `Result` [다음]이 직접 올리지 않는다(M6-W 구조).
- **롤백**: spec 삭제.
- [ ] 완료

### X5: `offline-storage.spec.ts` (E8 · E20 · E6)
- **Context Brief**: **M6-W(보관이 업로드보다 먼저)** 를 실브라우저에서 증명한다. 관측 수단은 `page.evaluate`의 OPFS 열거다(앱은 메인 스레드에서 OPFS를 쓰지 않지만 **테스트가 읽는 것은 무방**하다). E6(저장 실패 토스트)은 유일하게 **조건부**다 — `navigator.storage.getDirectory`를 지우면 합성 자체가 실패해 `skipped`(토스트 없음)가 되므로 **그 방법을 쓰면 안 된다**(설계 §7.3). CDP `Storage.overrideQuotaForOrigin(origin, 0)`을 **`Result` 도달 후** 적용한다.
- **대상 파일**: `tests/e2e/offline-storage.spec.ts`(신규) · `tests/e2e/fixtures/opfs.ts`
- **선행 조건**: X3
- **구현 내용**: **E8** — 로그인 상태로 촬영 완주, `uploads/prepare` **라우트 핸들러 안에서** `listOpfs("results")`를 호출해 **이미 폴더가 있음**을 단언한 뒤 응답한다. **E20** — `context.setOffline(true)` 후 게스트 촬영: 프레임 목록이 폴백으로 뜨고 **"…가져오지 못해…" 문구가 없으며**(오프라인은 `Ready`다) 보관이 성공한다. **E6** — `Result`에서 CDP로 쿼터 0 → [다음] → `STRINGS.save.failed` 토스트 + **전이는 계속된다**. A5가 거짓이면 이 테스트를 `test.fixme`로 남기지 말고 **삭제하고** `10 §5` E6 행에 "V19-6" 판정을 적는다.
- **검증 명령**: `npx playwright test --project=chromium offline-storage` · `npx tsc --noEmit`
- **완료 기준**:
  - [관측] prepare 시점에 OPFS `results/`가 이미 비어 있지 않다(M6-W). 오프라인 게스트 촬영이 완주하고 `results/`에 파일이 남는다.
  - [non-goal] 오프라인에서 **안내 오버레이·경고 문구가 뜨지 않는다**(E20 회귀 — `loadCore`의 catch가 오프라인을 `Ready`로 유지한다). E6에서 토스트가 떠도 화면은 `Qr`/`Done`으로 **계속 전이**한다.
  - [trigger] 쿼터 override는 `Result` 도달 **후**에만 적용한다 — 촬영 전에 걸면 컷 저장이 실패해 다른 경로를 보게 된다.
- **롤백**: spec 삭제(+ E6 판정을 문서에 기록).
- [ ] 완료

### X6: `guest-flow` 확장(E10·E11·E19·E21) + `idle-and-recovery.spec.ts`(E5·E14)
- **Context Brief**: 유휴 상한은 **120초 + 10초**이고 감시 대상은 6화면이다(`idleWatchdog.ts:14-16`, `stateMachine.ts:44-53`) — **`FrameSelect`도 포함**되므로 카메라 없이 검증할 수 있다. 판정 시계는 `performance.now()`라 `page.clock`으로 앞당긴다. 탭 hidden은 Playwright가 진짜로 만들 수 없으므로 `document.hidden`/`visibilityState`를 덮고 `visibilitychange`를 발화한다(진짜 탭 전환은 V16이 소유).
- **대상 파일**: `tests/e2e/guest-flow.spec.ts` · `tests/e2e/idle-and-recovery.spec.ts`(신규) · `tests/e2e/fixtures/visibility.ts`(신규)
- **선행 조건**: X2
- **구현 내용**: **E10**(Capture에 프레임 변경 컨트롤 부재) · **E11**(3/4에서 [다음] disabled → 4/4에서 enabled) · **E19**(hidden 에뮬레이션 → 홈 + OPFS `sessions/` 비어 있음) · **E21**(촬영 중 `page.reload()` → Home + `sessions/` 비어 있음. beforeunload 대화상자는 Playwright가 자동 처리한다) · **E5**(`clock.install()` → FrameSelect → `runFor("02:05")` 경고 → `runFor("00:11")` 홈, **계정 라벨 유지**) · **E14**(`setTimeout`에서 throw → 홈 + `STRINGS.error.temporary` 토스트 + 로그인 유지).
- **검증 명령**: `npx playwright test --project=chromium guest-flow idle-and-recovery` · `npx tsc --noEmit`
- **완료 기준**:
  - [관측] 유휴 경고 모달이 뜨고 10초 뒤 홈으로 간다. 예외 주입 후에도 앱이 살아 있고 상단바 계정 라벨이 그대로다.
  - [non-goal] 유휴 만료가 **로그아웃하지 않는다**(M3). 탭 hidden·새로고침 뒤 OPFS `sessions/`에 **부분 컷이 남지 않는다**(WM4). `results/`·`frames/`는 지워지지 않는다.
  - [trigger] 시간 조작은 `idle-and-recovery.spec.ts`에서만 — 촬영 시퀀스에는 `page.clock`을 쓰지 않는다.
- **롤백**: 신규 spec·픽스처 삭제.
- [ ] 완료

### X7: `roles-and-pin.spec.ts`(E15~E18) + `strings-catalog.spec.ts`(E22)
- **Context Brief**: 카메라가 필요 없는 화면 검증군이며 **WebKit에서도 돌 대상**이다. PIN 계열의 401은 **불일치이지 세션 만료가 아니다**(`unauthorized:"reject"` — PIN-2). 그래서 1회 오입력에 로그아웃되면 안 된다(E17). 5회 실패 잠금은 `localStorage["mcphoto.pinLock.v1"]`에 남는다. ⚠️ **리로드하면 게스트가 되고 게스트는 PIN 게이트를 지나지 않는다** — 잠금 지속을 보려면 리로드 **후 재로그인**해야 한다.
- **대상 파일**: `tests/e2e/roles-and-pin.spec.ts`(신규) · `tests/e2e/strings-catalog.spec.ts`(신규) · `tests/e2e/fixtures/users.ts`
- **선행 조건**: X3
- **구현 내용**: **E15**(`USERS.user` → FrameSelect에 [프레임 만들기] 부재) · **E16**(verify 401 × 5 → 모달 닫힘 + 잠금 키 존재 → 리로드 + 재로그인 → 잠금 문구, 키패드 미노출) · **E17**(1회 오입력 → 계정 라벨 유지 + `(1/5)`) · **E18**(manager → `accounts` 목록 목 → 다른 manager 행 [PIN] 없음/[삭제] 있음, 콤보에 `admin` 없음, 자기 행 액션 없음, **좁은 뷰포트 카드에서도 동일**) · **E22**(6화면 주요 문구가 `@ui/strings` 값과 일치).
- **검증 명령**: `npx playwright test --project=chromium roles-and-pin strings-catalog` · `npx tsc --noEmit`
- **완료 기준**:
  - [관측] PIN 5회 실패 뒤 재진입에서 남은 시간 문구가 뜬다. manager 화면의 행 액션이 매트릭스와 일치한다.
  - [non-goal] PIN 오입력이 **로그아웃을 유발하지 않는다**(상단바 라벨 불변 — E17). 목록 조회 실패가 **빈 목록으로** 표시되지 않는다. spec 어디에도 PIN 값이 로그로 남지 않는다.
  - [trigger] PIN 모달은 보호 화면 진입에서만 뜬다 — 게스트는 모달 없이 지난다(요청 0건).
- **롤백**: 신규 2파일 삭제.
- [ ] 완료

### X8: `frame-authoring.spec.ts`(E13) + WebKit 프로젝트 확정
- **Context Brief**: 프레임 이름 `_` 판정은 **세 축**이다(`15 §6`): 서버 등록은 **하드 거부**, 로컬 저장은 **비차단 경고**, 저장 전 선검증은 길이·`_`를 보지 않는다. 따라서 M15(E13)의 관측 지점은 **서버 등록 체크 on**이다. 편집기에는 `<input type="file" accept="image/png,image/jpeg">`가 있어 `setInputFiles`로 PNG를 주입할 수 있다(`FrameEditorView.tsx:299`). WebKit은 Chromium의 가짜 카메라 스위치를 지원하지 않으므로 `@camera` 태그를 제외하고 돌린다.
- **대상 파일**: `tests/e2e/frame-authoring.spec.ts`(신규) · `tests/e2e/fixtures/*`(태그 정리) · `playwright.config.ts`(webkit 확정)
- **선행 조건**: X7
- **구현 내용**: manager 로그인 → FrameSelect [프레임 만들기] → PNG(작은 단색, 테스트가 `Buffer`로 생성) 주입 → 이름 `a_b` → **등록 체크 on 저장 → 거부 문구 + `POST frames` 0건** / **체크 off 저장 → 성공 + 경고 문구**. 그 뒤 모든 spec에 `@camera` 태그를 최종 점검하고 `npx playwright test --project=webkit`을 돌려 통과 목록을 확정한다.
- **검증 명령**: `npx playwright test --project=chromium frame-authoring` · `npx playwright test --project=webkit` · `npx tsc --noEmit`
- **완료 기준**:
  - [관측] `_` 이름 + 등록 체크 on에서 저장이 거부되고 서버 요청이 0건이다. 체크 off에서는 저장되고 경고만 뜬다. webkit 프로젝트의 통과/제외 목록이 확정된다.
  - [non-goal] webkit에서 카메라 시나리오가 **시도되지 않는다**(grepInvert). webkit이 실패하면 **조용히 지우지 않고** `10 §6`에 사유를 남긴 채 비활성한다(A6).
  - [trigger] 서버 등록은 오버레이 체크가 켜진 상태의 [저장]에서만 일어난다.
- **롤백**: spec 삭제 + webkit 프로젝트 제거.
- [ ] 완료

### X9: `10` 문서 재구성 (§5 자동화 열 · §8 수락 체크리스트 3열)
- **Context Brief**: 수락 체크리스트가 지금은 체크박스만 있어 **누가 무엇으로 확인했는지** 남지 않는다. 설계 §10의 3열 형식으로 전면 재구성하고, `10 §5` 시나리오 표에는 자동화 판정(설계 §7)을 반영한다. **E3·E6·E13·E2 네 행은 문구 자체를 고친다** — 그러지 않으면 다음 세션이 "목으로 `qrEffectivePolicy`를 true 고정"을 또 시도한다.
- **대상 파일**: `docs/web-client/10-testing-and-acceptance.md`
- **선행 조건**: X4·X5·X7·X8(실제 통과 결과가 있어야 표를 사실로 채운다)
- **구현 내용**: §7 판정표를 `10 §5`에 "자동화" 열로 반영 + E3(§7.1)·E2(§7.2)·E6(§7.3)·E13(§7.4) 행 재작성. `10 §6`에 WebKit 범위 문단. `10 §8`을 §10.1 형식으로 재구성하고 §10.2 분류를 채운다. **수단이 없는 항목을 남기지 않는다.**
- **검증 명령**: `npx playwright test`(전체 통과 목록과 문서 표가 일치하는지 대조) · `grep -c "\[ \]" docs/web-client/10-testing-and-acceptance.md`(항목 수 확인)
- **완료 기준**:
  - [관측] `10 §8`의 모든 행에 `자동`/`정적`/`사람` 중 하나의 수단이 적혀 있다. `10 §5`의 26행에 자동화 판정이 있다.
  - [non-goal] 실행하지 않은 항목을 **체크하지 않는다**. `10 §1~§4`(테스트 계층·벡터·골든)는 변경하지 않는다.
  - [trigger] 표 갱신은 실제 spec 실행 결과가 나온 뒤에만 — 예상으로 적지 않는다.
- **롤백**: 문서 diff 되돌림.
- [ ] 완료

### X10: `16` 실기기 절차서 신설 + 인덱스·재개 가이드 갱신 + 최종 검증
- **Context Brief**: V1~V25가 `14 §10`의 11개 절에 흩어져 있어 사람이 순서대로 수행할 수 없다. 기기 3대 × 계정 5종의 **세션 S1~S9**로 재편한 절차서를 신설한다(설계 §11). **항목 정의의 진실원은 `14 §10`이고 `16`은 순서·기록만 소유한다** — 두 곳에 정의를 복사하면 반드시 갈라진다. Step 17이 마지막 Step이므로 `15 §7`을 "구현·E2E 완료, 남은 것은 실측"으로 바꾼다.
- **대상 파일**: `docs/web-client/16-field-verification-runbook.md`(신규) · `docs/web-client/README.md` · `docs/web-client/15-implementation-conventions.md` · `docs/web-client/14-handoff-and-user-actions.md` · `docs/web-client/11-wbs.md` · `docs/web-client/12-web-vs-windows-differences.md`
- **선행 조건**: X9
- **구현 내용**: §11.2 구조 · §11.3 R0 · §11.4 세션 표 · §11.5 기록 규칙. `14 §10`의 각 절 상단에 "→ 16의 S번호" 링크 1줄만 추가한다(정의는 그대로). `README §1`에 17번째 행 + 상태 줄. `15 §1` 재개 표 교체 + `15 §3`에 E2E 실행법 3줄 + `15 §7` 갱신. `11 Step 17`에 산출물·검증 수치·설계 이탈·남은 실측을 기록하고 `[x]`. `12`는 신규 차이 행 또는 **"전 항목 재확인(날짜)"**.
- **검증 명령**: `npx playwright test`(전체) · `npx vitest run`(X1 기준선과 동일) · `npx tsc --noEmit` · `npm run build` · `cd ../web/functions && npm test` · `cd ../.. && dotnet test tests/MCPhoto.Tests` · `git status`(의도한 파일만)
- **완료 기준**:
  - [관측] `16`이 R0 + S1~S9 + 성능 표 + 미완 기록란을 갖추고, V1~V25(하위번호 포함)가 **하나도 빠짐없이** 어느 세션엔가 배치돼 있다(교차 확인: `14 §10`의 V 번호 집합 = `16`의 V 번호 집합).
  - [non-goal] `14 §10`의 항목 정의·"왜 자동화가 안 되나" 열이 **변경되지 않는다**(링크 1줄만 추가). `docs/spec-vectors/*`·`web/functions`·`src/MCPhoto.*`·`webclient/src/**` 무변경. 해소된 번호(V24-6)를 **재사용하지 않는다**.
  - [trigger] `11 Step 17`의 `[x]`는 **E2E 전량 통과 + 문서 3종 완료** 시에만 — 실측(V) 완료는 사람 몫이므로 체크 조건이 아니다(그 사실을 Step 17 기록에 명시한다).
- **롤백**: 신규 문서 삭제 + 문서 diff 되돌림.
- [ ] 완료

---

## 16. 완결성 게이트 (developer 전달 전 자체 검사)

- [x] 검증된 사실(F1~F30) / 미검증 가정(A1~A10)이 분리돼 있다
- [x] 모든 가정에 검증 단계가 매핑돼 있다 (A1→X1 · A2→X6 · A3→X2 · A4→X4 · A5→X5 · A6→X8 · A7→X3 · A8→X3 · A9→X7 · A10→X1)
- [x] 10개 단계 전부에 7개 필수 필드가 있다
- [x] 모든 완료 기준이 관측 기반 3문 형식이고 UI 단계에 non-goal·trigger가 있다
- [x] 검증 명령이 자동 실행 가능한 CLI다
- [x] 단계 수 10개 (3~12 범위)
- [x] E1~E24(+E1b·E3b) **26개 전부**에 자동화 판정과 잔여 항목이 붙어 있다(§7)
- [x] 자동화 불가·부분 항목이 **어느 V 번호로 넘어가는지** 명시돼 있다
- [x] WBS가 지정한 이월분 2개 파일명(`upload-qr.spec.ts`·`guest-flow.spec.ts`)을 그대로 쓴다

---

## 17. 이 Step의 비목표 (명시)

| 하지 않는 것 | 왜 |
|--------------|-----|
| **`src/**` 수정** | 검증 Step이다. 제품 코드를 고쳐야 하는 결함이 나오면 **원인 Step으로 되돌려** 고치고 그 Step의 규약(테스트·문서)을 따른다 |
| 테스트 전용 로그인·세션 위조 헬퍼 | AUTH-1·AUTH-4. Step 12가 삭제한 것을 되살리지 않는다. 하네스에서 푼다(§5) |
| `qrEffectivePolicy` 등 도메인 모듈의 alias 치환(별도 vite config) | "테스트한 앱 ≠ 배포할 앱"이 된다. E3은 §7.1대로 재정의한다 |
| 교차 오리진 목 백엔드 | preflight를 가로챌 수 없다(§4.2). 같은 오리진 목이 유일한 결정적 해법이다 |
| E2E에서의 성능 측정 | headless·SwiftShader 수치는 실기기 예산과 무관하다. S9(실측)가 소유한다 |
| E2E에서의 픽셀 비교 | 골든 이미지(vitest)가 이미 0px로 고정한다 |
| 정적 불변식(40+)의 E2E 재검증 | §9 — 느리기만 하고 값이 없다 |
| CI 파이프라인(GitHub Actions 등) 구성 | 저장소에 CI 정의가 없다. `npx playwright test`가 헤드리스로 도는 것까지가 이 Step의 범위다 |
| 실기기 검증 **대행** | 사람이 해야 한다. `16`은 그 사람을 위한 절차서다 |
| `THIRD-PARTY.md` 갱신 | devDependency는 배포물에 없다(F27) |
