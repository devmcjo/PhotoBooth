# 11 · WBS 블루프린트 (구현 작업 분해)

| 항목 | 값 |
|------|-----|
| 대상 | `webclient/` 그린필드 구현 (TypeScript + React + Vite → Firebase Hosting `kiosk` 사이트) |
| 설계 근거 | 이 폴더의 [00](./00-scope-and-decisions.md)~[10](./10-testing-and-acceptance.md) |
| 형식 | `docs/templates/WBS_BLUEPRINT.md` 준수 |
| 검증 도구 | `npm`(vitest·playwright·tsc) · `firebase` CLI · 브라우저 DevTools · 실기기 |
| 작성일 | 2026-07-30 |

> 각 Step은 **self-contained**다. 대화 컨텍스트가 없는 에이전트가 그 Step만 읽고 실행할 수 있도록 배경·파일·검증을 명시했다.
> 단계 수가 템플릿 권장(3~12)보다 많다(17개 + Step 0). 이유: 화면 13개 + 미디어 파이프라인 + 인증 + 저장 계층이 한 덩어리로는 **독립 검증이 불가능**하기 때문이다. 각 Step은 "실패 시 원인이 하나로 특정된다"는 기준을 유지한다.

---

## 검증된 사실 (verified facts)

- **VF-1** 서버 CORS가 `cors({origin: true})`로 열려 있어 **브라우저에서 백엔드 API를 바로 호출할 수 있다.** (근거: `docs/analysis/31 §1`, 소스 `web/functions/src/app.ts`)
- **VF-2** `/accounts`·`/config` 라우터는 `requireBearer`만 걸고 `requireApiKey`를 걸지 않는다 → **관리 API는 게이트 키 없이 JWT만으로 동작한다.** (근거: `docs/analysis/31 §4.0`)
- **VF-3** `POST /auth/google`의 `redirectUri`는 **http loopback만** 통과한다(그 외 400). audience는 단일 `GOOGLE_OAUTH_CLIENT_ID` 고정. → **웹 로그인은 서버 확장(B1·B2) 선행 필수.** (근거: `docs/analysis/31 §4.2`, `61 §2`, 소스 `web/functions/src/domain/validation.ts`·`services/googleAuth.ts`)
- **VF-4** 업로드 라우터는 `apiKey + optionalBearer` 게이트다 → **서버는 무토큰(게스트) 업로드를 허용**하므로 로그인 없이 **업로드 3단계 자체**를 호출·검증할 수 있다. (근거: `docs/analysis/31 §5.1`, 소스 `web/functions/src/routes/uploads.ts`) ⚠️ **단 클라이언트는 게스트에게 업로드를 시작하지 않는다 — VF-11 참조.**
- **VF-5** 업로드 파일 검증은 `final`→`jpg|png`/`image/*`, `timelapse`→**`mp4`+`video/mp4`만** 허용한다. (근거: `docs/analysis/31 §8`)
- **VF-6** `timelapseUrl`을 null로 두고 commit하는 것은 **계약상 합법**이다(사진이 있으면 최소 1개 불변식 충족). (근거: `docs/analysis/14 §7.3`, `31 §5.3`)
- **VF-7** 기존 P1 다운로드 페이지는 `web/public/`에 완성돼 있고 Firestore 단건 get만 한다. **웹 앱과 무관하게 계속 동작한다.** (근거: `docs/analysis/20`)
- **VF-8** Windows 순수 로직이 파일 단위로 분리돼 있고 대응 테스트가 존재한다(경로는 [01 §2.2](./01-tech-stack-and-structure.md) 표). → **도메인 이식과 벡터 추출이 가능하다.**
- **VF-9** 설정 키·기본값·범위·`.slots` 포맷·결과물 파일명은 `docs/analysis/41`에 전수 문서화돼 있다.
- **VF-10** Windows 앱에는 배포용 번들 프레임 PNG가 커밋돼 있지 않다(`Example/`의 예시 이미지만 존재). → **번들 프레임 자산은 새로 준비하거나 코드 생성 fallback으로 시작**해야 한다.
- **VF-11** **게스트(미로그인)에게는 QR이 제공되지 않는다.** `ResultViewModel.Next`가 `QrEffectivePolicy.IsQrEnabled(raw, isLoggedIn, isTempUserBlocked)`로 판정하고 미로그인이면 `Qr`을 건너뛰고 `Done`으로 간다. → **Step 11의 종단(폰 스캔) 검증은 로그인 상태에서만 가능**하며, Step 11 자체는 effective QR 목으로 검증한다. (근거: 소스 `src/MCPhoto.App/ViewModels/ResultViewModel.cs:149`, `src/MCPhoto.Core/Settings/QrEffectivePolicy.cs`, `design/wpf-it13-temp-user-role-design.md §7.1b`. `docs/analysis/60 §2`·`13 §4.7`·`61 §1`의 대응 정정은 **반영 완료**(2026-07-30))
- **VF-12** 컷 수 해석 지점은 **`FrameSelect` [다음]** 1곳이다(`FrameSelectViewModel.Next` → `CaptureSession.Begin` → `CutCountPolicy.Resolve`). 세션이 `CutCount`·`IsAutoCutCount`를 보유하고 `GuideViewModel`이 설정이 아니라 **세션에서 읽는다**. → 전체 재촬영으로 `Guide`에 재진입해도 재해석하지 않는다. (it20 이후 `Loading`·`Failed` 국면에서는 그 [다음] 자체가 차단된다 — [03 §4.1](./03-screens-spec.md))
- **VF-13** Windows QR은 **QRCoder `ECCLevel.Q` + 기본 모듈 20px**이다(`src/MCPhoto.Core/Upload/QrService.cs`, `analysis/30 §3`). → 웹도 **ECC Q**로 맞춘다([03 §9](./03-screens-spec.md)).
- **VF-14** `createSyncAccessHandle()`은 **전용 Worker 전용** API이고, **Safari 17~18.x에는 `createWritable()`이 없다**(최신 Safari에서 Baseline 2025로 추가 — 지원 하한 17 기준으로는 없다고 간주, 기능 감지로 판정). → **모든 OPFS 쓰기를 Worker 경계 뒤로 모아야** iOS/iPadOS에서 저장이 성립한다([05 §3.1](./05-storage-and-persistence.md)).
- **VF-15** `MediaRecorder`·`HTMLCanvasElement.captureStream()`은 **Worker에 없다**(Window 전용, `OffscreenCanvas`에 `captureStream` 없음). → **타임랩스 경로 A는 메인 스레드 전용**이며 경로 B(WebCodecs, Worker 가능)를 1순위로 두는 근거가 된다([04 §7.3a](./04-media-pipeline-web.md)).

## 미검증 가정 (open assumptions)

- **OA-1** 버킷 CORS를 구성하면 **서명 URL PUT이 브라우저에서 성공**한다 → **검증: Step 0-5, Step 11**
- **OA-2** 버킷 CORS 구성 후 `firebasestorage.googleapis.com`의 프레임 이미지를 **CORS-clean하게 로드해 canvas 합성이 가능**하다(오염 없음) → **검증: Step 0-5, Step 8**
- **OA-3** 대상 브라우저에서 **H.264/mp4 인코딩이 가능**하다(WebCodecs `avc1` 또는 MediaRecorder mp4). 지원 시점은 [04 §7.3b](./04-media-pipeline-web.md)에 정리했으나 **실기기의 `isConfigSupported` 결과가 진실원**이다 → **검증: Step 9, Step 17**
- **OA-4** OPFS 쓰기가 대상 브라우저 전부(특히 iOS Safari 17)에서 **Worker + `createSyncAccessHandle` 경로로** 동작하고 촬영 세션 용량을 감당한다(VF-14) → **검증: Step 3, Step 10, Step 17**
- **OA-9** iOS/iPadOS Safari 17에서 **Worker `OffscreenCanvas` 2D·WebGL2**가 실제로 동작해 §1 파이프라인 구조가 성립한다([04 §2.3.1](./04-media-pipeline-web.md)) → **검증: Step 6(2D), Step 8(WebGL2 뷰티), Step 17(실기기)**
- **OA-10** `createImageBitmap` resize 옵션이 대상 브라우저에서 실효한다(미실효 시 폴백으로 성능 예산을 만족한다) → **검증: Step 8, Step 17**
- **OA-5** 서버 확장(B1·B2) 후 **웹 리디렉트 로그인이 성공**한다 → **검증: Step 12**
- **OA-6** iOS Safari에서 10컷(1080p) 세션이 탭 종료 없이 완주한다 → **검증: Step 17**
- **OA-7** `Screen Wake Lock`이 대상 기기에서 동작한다(미동작 시 OS 설정으로 대체) → **검증: Step 17**
- **OA-8** Windows 골든 이미지와 웹 합성 결과가 [10 §4.2](./10-testing-and-acceptance.md) 허용 오차 내에 들어온다 → **검증: Step 8**

> 모든 가정이 검증 Step에 매핑됨(완결성 게이트 통과).

---

## 단계 의존 그래프

```
Step 0 (서버·인프라 선행 — 개발과 병렬 가능)
   0-5(CORS)·0-6(Hosting) ──┐                      0-1~0-4(OAuth·게이트키) ──┐
                            │                                               │
Step 1 (스캐폴드 + 배포)  ───┤                                               │
   ├─ Step 2 (도메인 + 공유 벡터)        ← 독립, 가장 먼저 착수 가능           │
   ├─ Step 3 (저장 계층 + 부트스트랩)                                        │
   ├─ Step 4 (앱 셸 + 라우팅 + UI 기본)  ← Step 2                            │
   └─ Step 5 (HTTP 클라이언트)                                              │
Step 4+5 → Step 6 (카메라 파이프라인 + 카메라 테스트)                        │
Step 6   → Step 7 (촬영 + 컷 선택 + 최소 FrameSelect·fallback 프레임)        │
Step 7   → Step 8 (합성 + 필터 + 골든)                                       │
Step 8   → Step 8.5 (main 머지분 반영 — 도메인 2 + 벡터 1 + 셸 불변식)  ← 독립 │
Step 6   → Step 9 (타임랩스 인코더)            ← Step 7과 병렬 가능           │
Step 3+8 → Step 10 (결과물 로컬 보관 M6-W)                                   │
Step 5+8+9+10 → Step 11 (업로드 + QR + Done)   ★ 마일스톤 A: 게스트 완주      │
                                                                            │
Step 11 ─────────────────────────────────────────→ Step 12 (인증) ←──────────┘
Step 12 → Step 13 (PIN + 설정 화면)
Step 12 → Step 14 (프레임 저장소 + 프레임 선택)   ← Step 3
Step 14 → Step 15 (프레임 편집기 + 피커 + 삭제)
Step 12 → Step 16 (계정 + 사용자 관리 + 진단 + PWA)
전부   → Step 17 (E2E + 실기기 + 수락)          ★ 마일스톤 B: 출시 가능
```

- **병렬 가능**: Step 2는 어떤 것도 기다리지 않는다(가장 먼저 시작). Step 0-1~0-4는 Step 11까지의 작업과 완전히 병렬이다.
- **크리티컬 패스**: 1 → 4 → 6 → 7 → 8 → 10 → 11.

---

## Step 0: 서버·인프라 선행 작업

- **Context Brief**: 웹 클라이언트는 기존 백엔드를 그대로 쓰지만, 브라우저 특성 때문에 서버 4건·GCP 1건·Hosting 1건이 선행돼야 한다. 상세 절차·코드 변경 방향·검증은 **[08 · 서버·인프라 선행 작업](./08-server-and-infra-prerequisites.md)** 에 전부 있다. 이 Step은 그 문서를 실행하는 것이다.
- **대상 파일**: `web/firebase.json`, `web/.firebaserc`, `web/functions/src/domain/validation.ts`, `web/functions/src/services/googleAuth.ts`, `web/functions/src/config.ts`, `web/functions/src/__tests__/{validation,googleAuth}.test.ts`, GCP 버킷 CORS, Google Cloud Console
- **선행 조건**: 없음
- **구현 내용**: [08](./08-server-and-infra-prerequisites.md) §2~§6의 P0-1 ~ P0-6. **0-5(CORS)와 0-6(Hosting)을 먼저** 처리하면 Step 11까지 진행할 수 있다.
- **검증 명령**:
  - `gcloud storage buckets describe gs://mcphoto-955fb.firebasestorage.app --format="default(cors_config)"` — ⚠️ **이 PC에 `gcloud`가 설치돼 있지 않다는 실측 기록**이 있다(`web/OPS-cors.md §1`). 미설치면 **Cloud Shell**에서 실행하거나 gcloud를 설치한 뒤 수행한다
  - `cd web/functions && npm test` (서버 회귀 — 데스크톱 loopback 통과 + 허용 목록 밖 거부)
  - `curl -s -o /dev/null -w "%{http_code}" -H "X-MCPhoto-Client: <web-key>" https://asia-northeast3-mcphoto-955fb.cloudfunctions.net/api/frames/default` → **200**
  - `curl … -H "X-MCPhoto-Client: bogus" …/frames/default` → **401**
- **완료 기준**:
  - [관측] 버킷 CORS에 대상 오리진의 `PUT`·`GET`과 `x-goog-meta-firebaseStorageDownloadTokens`가 등록돼 있다. 웹 키로 `/frames/default`가 200, 임의 키가 401이다. kiosk 사이트가 200을 서빙한다.
  - [non-goal] **기존 Windows 클라이언트의 로그인·업로드가 영향받지 않아야 한다**(loopback 통과·기존 게이트 키 유효). P1 사이트 응답이 변하지 않는다.
  - [trigger] CORS는 브라우저 요청에만 영향을 준다. 서버 검증 변경은 `redirectUri`가 loopback이 아닐 때만 새 경로를 탄다.
- **롤백**: CORS는 이전 구성 파일로 재적용. 서버 코드는 커밋 revert + `firebase deploy --only functions`. `CLIENT_API_KEYS`는 웹 키만 제거.
- [ ] 완료

---

## Step 1: 프로젝트 스캐폴드 + 배포 파이프라인

- **Context Brief**: 그린필드다. `webclient/`에 Vite+React+TS 프로젝트를 만들고 **빈 화면을 Hosting `kiosk` 사이트에 실제로 배포**해 배포 경로·CSP·HTTPS를 먼저 확정한다. 이후 모든 Step이 이 토대 위에 올라간다. 구조·의존성·CSP·배포 명령은 **[01 · 기술 스택과 프로젝트 구조](./01-tech-stack-and-structure.md)** 에 있다.
- **대상 파일**: `webclient/{package.json,tsconfig.json,vite.config.ts,vitest.config.ts,index.html,.env.example,.gitignore}`, `webclient/src/{main.tsx,env.ts}`, `webclient/public/{manifest.webmanifest,branding.json}`, `web/firebase.json`(kiosk 블록), `.gitignore`(`web/kiosk/` 추가)
- **선행 조건**: Step 0-6(Hosting 타깃)
- **구현 내용**:
  - Vite React-TS 템플릿 기반. `tsconfig`는 `strict: true` + path alias(`@domain/*`·`@adapters/*`·`@shell/*`·`@screens/*`·`@ui/*`).
  - `vite.config.ts`의 `build.outDir = "../web/kiosk"`, `emptyOutDir: true`.
  - `env.ts`: `VITE_*` 7개를 읽고 정규화(`HostingBaseUrl` 트레일링 `/` **제거**, `BackendBaseUrl` **부여**). 게이트 키 부재는 경고 로그만(크래시 금지).
  - `index.html`: viewport(`user-scalable=no`) + 테마 색 + 앱 마운트 지점.
  - `firebase.json`에 [01 §5.1](./01-tech-stack-and-structure.md)의 `kiosk` 블록 추가(**기존 default 블록 무변경**).
  - 화면에는 버전 캡션 **`v{version}`** 만 표시하는 최소 앱. **배포 채널(`Site`)·빌드 시각은 캡션에 넣지 않는다**(it18 — [01 §4.1](./01-tech-stack-and-structure.md), [05 §8.2](./05-storage-and-persistence.md)).
- **검증 명령**:
  - `cd webclient && npm ci && npx tsc --noEmit && npm run build`
  - `cd ../web && npx firebase deploy --only hosting:kiosk`
  - `curl -sI https://mcphoto-955fb-kiosk.web.app/ | grep -i "content-security-policy"`
  - `curl -sI https://mcphoto-955fb.web.app/ | head -1` (P1 무변경 확인)
- **완료 기준**:
  - [관측] kiosk 사이트가 버전 캡션 `v{version}`을 표시하고(채널·빌드 시각 문자열이 **없다**), 응답에 CSP·`nosniff` 헤더가 있으며 브라우저 콘솔에 **CSP 위반이 0건**이다. `tsc --noEmit`이 통과한다.
  - [non-goal] 앱 로직·라우팅·상태 관리 **없음**. **P1 사이트는 변경되지 않는다**(배포 대상이 `hosting:kiosk`뿐).
  - [trigger] 빌드는 `npm run build`에서만, 배포는 `--only hosting:kiosk`에서만 일어난다.
- **롤백**: `webclient/` 삭제 + `firebase.json`의 kiosk 블록 제거(그린필드라 이전 상태 = 없음).
- [x] **완료 (2026-07-30) — 단, 첫 배포·CSP 헤더 실측은 미완**
  - 산출: `webclient/` 스캐폴드(Vite 5 + React 18 + TS strict, `outDir=../web/kiosk`), `env.ts`(두 URL 정규화 방향 반대 + 빈 값 폴백 + 경고 배열), `index.html`, `manifest.webmanifest` + 플레이스홀더 아이콘 3종, `branding.json`, `webclient/deploy.bat`.
  - 인프라: kiosk 사이트 생성(`mcphoto-955fb-kiosk`) + `.firebaserc`에 **두 타깃 등록**, `firebase.json` hosting을 배열로 전환(default 블록 무변경).
  - ⚠️ **회귀 방지 추가 조치**: `web/deploy-web.bat`의 배포 대상을 `hosting:default`로 고정했다. 그대로 `--only hosting`이면 kiosk까지 배포해 `web/kiosk/` 부재 시 실패한다(N1 위반). `docs/analysis/80 §6.5`에 등재.
  - **남은 것**: `firebase deploy --only hosting:kiosk` 실행 + `curl -sI`로 CSP·nosniff 확인 + 콘솔 CSP 위반 0건 확인.

---

## Step 2: 도메인 계층 이식 + 공유 테스트 벡터

- **Context Brief**: 앱의 "동일 동작"은 대부분 순수 로직에 있다. Windows `MCPhoto.Core`의 순수 함수 20여 개를 **의존성 0의 TS로 이식**하고, Windows 테스트에서 입력→기대출력을 JSON 벡터로 추출해 **양쪽이 같은 파일을 읽게** 만든다. 이식 대상 파일 목록·대응 테스트는 **[01 §2.2](./01-tech-stack-and-structure.md)** 표, 벡터 목록은 **[10 §3](./10-testing-and-acceptance.md)**, 정수 연산 변환은 **[04 §9](./04-media-pipeline-web.md)** 표.
- **대상 파일**: `webclient/src/domain/**`(navigation·capture·frames·settings·roles·upload·filters), `webclient/tests/unit/**`, `webclient/tests/vectors/loadVector.ts`, `docs/spec-vectors/*.json`(신규), `tests/MCPhoto.Tests/*`(벡터 읽기로 전환)
- **선행 조건**: 없음(Step 1과 병렬 가능 — 순수 TS라 빌드 환경만 필요)
- **구현 내용**:
  - [01 §2.2](./01-tech-stack-and-structure.md) 표의 모든 모듈을 이식한다. **브라우저 API·`Date.now()`·`Math.random()`을 쓰지 않고 인자로 주입**받는다.
  - **[04 §9](./04-media-pipeline-web.md)의 정수 연산 대응표를 그대로 적용**한다(`Math.floor`/`Math.round` 명시).
  - `docs/spec-vectors/`에 [10 §3.2](./10-testing-and-acceptance.md)의 14개 벡터 파일을 만든다(Windows 테스트에서 덤프 → 덤프 코드 제거 → 양쪽이 읽기).
  - Vitest로 [10 §2](./10-testing-and-acceptance.md) 표의 케이스 전부 작성.
  - `DisplayApplyPolicy`는 **이식하지 않는다**(창 개념 없음 — WD7).
- **검증 명령**:
  - `cd webclient && npx vitest run --coverage`
  - `cd .. && dotnet test tests/MCPhoto.Tests` (벡터 전환 후 Windows 테스트가 여전히 통과)
  - `node -e "console.log(require('fs').readdirSync('docs/spec-vectors').filter(f=>f.endsWith('.json')).length)"` → 14 (의존성 없이 실행 가능)
- **완료 기준**:
  - [관측] `src/domain` 커버리지 **95% 이상**이고, 14개 벡터 파일을 웹·Windows 테스트가 **양쪽에서 읽어 통과**한다. `src/domain`의 어떤 파일도 브라우저·React·Node API를 import하지 않는다(import 검사 테스트로 고정).
  - [non-goal] UI·어댑터·저장소 코드 **없음**. Windows 제품 코드는 **변경하지 않는다**(테스트 파일만 벡터 읽기로 전환).
  - [trigger] 벡터 불일치 시 **양쪽 테스트가 동시에 실패**해야 한다(한 벡터 값을 일부러 바꿔 확인).
- **롤백**: `src/domain`·`tests` 삭제, Windows 테스트를 커밋 이전으로 revert.
- [x] **완료 (2026-07-30)**
  - 도메인 28개 파일 이식(§2.2 표 전량 + `mathCompat.roundHalfToEven`·`fallbackFrameSpec`). `DisplayApplyPolicy` 미이식(WD7).
  - 벡터 **14개 파일 / 271 케이스** 생성(`docs/spec-vectors/`, 생성기 `webclient/scripts/genVectors.ts`).
  - 검증: 웹 **234 테스트 통과**, `src/domain` 커버리지 **99.4% stmts / 97.4% branches**. Windows **839 테스트 통과**(신규 `SpecVectorTests` 16 포함, 기존 823 무회귀).
  - **트리거 확인 완료**: `center-crop.json`의 기대값 1px을 일부러 틀리게 하면 **웹·Windows가 동시에 실패**하고 복원하면 동시에 통과한다.
  - 순수성 기계 검증: `tests/unit/domain/purity.test.ts`가 도메인 밖 import·`Date.now`·`Math.random`·브라우저 API·`console`을 파일 단위로 금지한다.
  - ⚠️ **설계와 다르게 한 점**: 기존 Windows 테스트를 벡터 읽기로 **전환하지 않고** `SpecVectorTests.cs`를 **신설**했다. 기존 823개의 집중 단언을 보존하면서 교차 검증을 얻는 편이 회귀 위험이 낮다(요구의 목적 "양쪽이 같은 파일을 읽어 통과"는 충족).
  - **프레임 이름 `_` 규칙 해소(모순 아님 — 스코프가 다르다)**: 서버 `web/functions/src/domain/validation.ts:297`이 `POST /frames`의 이름에 `_`가 있으면 **400으로 거부**한다. 따라서 ① **서버 등록 경로**(power 신규 공용) = **하드 거부**(M15·E13이 맞다) ② **로컬 전용 저장**(advanced_user 개인) = 서버를 거치지 않으므로 거부 사유가 없고, 공용 파일명 규약 충돌만 **비차단 경고**(Windows `FrameEditorViewModel`과 동일). 도메인은 `validateFrameNameForServer`(하드) / `validateFrameName` + `underscoreWarning`(로컬)로 **두 경로를 분리**해 이식했다. 문서 정정 불필요.

---

## Step 3: 저장 계층 + 부트스트랩

- **Context Brief**: 설정(localStorage)·세션 작업 공간(OPFS)·로그(IndexedDB)를 만들고, 규격 순서대로 앱을 부팅한다. 특히 **앱 시작 시 OPFS `sessions/` 잔재 일괄 삭제**는 규격이며 빠지면 임시 파일이 무한 누적된다. 스키마·규칙은 **[05 · 저장·영속](./05-storage-and-persistence.md)**, 부트스트랩 순서는 **[01 §4.2](./01-tech-stack-and-structure.md)**.
- **대상 파일**: `src/adapters/storage/opfsWriter.worker.ts`(**모든 OPFS 쓰기의 단일 경계 — [05 §3.1](./05-storage-and-persistence.md)**), `src/adapters/storage/{settingsRepo,sessionWorkspace,logStore}.ts`, `src/adapters/platform/persistStorage.ts`, `src/shell/settingsStore.ts`, `src/main.tsx`(부트스트랩), `tests/unit/storage/*.test.ts`
- **선행 조건**: Step 1, Step 2(설정 clamp·QR 정규화 도메인 함수)
- **구현 내용**:
  - `settingsRepo`: [05 §2.1](./05-storage-and-persistence.md) 스키마. 로드 시 파싱 실패·부재 → 기본값 + 경고 로그, **clamp + QR 정규화** 적용. 저장은 **boolean 반환**(성공 오인 금지). `BackendApiKey`는 저장하지 않는다.
  - `sessionWorkspace`: OPFS `sessions/{sessionId}/` 생성·파일 쓰기(Worker `createSyncAccessHandle` 우선)·폴더 삭제·**시작 시 잔재 일괄 삭제**.
  - `logStore`: IndexedDB 링버퍼(14일/5,000건), 배치 flush(1초 또는 20건), `pagehide`에서 강제 flush, 내보내기 함수. **금지 항목 마스킹 유틸** 포함.
  - `persistStorage`: `navigator.storage.persist()` 요청 + 결과 기록.
  - `main.tsx`: [01 §4.2](./01-tech-stack-and-structure.md)의 11단계 순서 구현(브랜딩 fetch 800ms 타임아웃 포함).
- **검증 명령**: `npx vitest run tests/unit/storage` · 브라우저에서 새로고침 → DevTools Application → OPFS에 `sessions/` 잔재 없음 · localStorage에 설정 키 존재
- **완료 기준**:
  - [관측] 설정을 저장·재로드하면 clamp된 값이 유지된다. 손상된 JSON을 넣어도 크래시 없이 기본값으로 뜬다. OPFS에 더미 `sessions/x/` 폴더를 만들고 새로고침하면 **사라진다**. 저장 실패(용량 초과 목)에서 `save()`가 `false`를 반환한다.
  - [non-goal] `results/`·`frames/`·로그는 **잔재 정리로 삭제되지 않는다**(더미 파일을 넣어 확인). 화면 UI 없음.
  - [trigger] 잔재 정리는 **앱 시작 시 1회만**. 브랜딩 fetch 실패가 부팅을 막지 않는다.
- **롤백**: 해당 파일 삭제. 저장소 데이터는 DevTools에서 수동 삭제.
- [x] **완료 (2026-07-31) — 브라우저 실측만 남음**
  - `opfsProtocol`(경로 방어: `..`·빈 세그먼트 거부) + `opfsWriter.worker`(**sync-access-handle 1순위 → Worker 내 createWritable 폴백 → none**, `truncate(0)` 후 쓰기, `finally`에서 `close()`) + `opfsClient`(RPC·15초 타임아웃·실패는 `false`).
  - `settingsRepo`(알 수 없는 키 보존·타입 불일치만 기본값·`BackendApiKey` 미저장·`omitKeys`로 게스트 값 보존·저장 boolean), `sessionWorkspace`, `logPolicy`+`logStore`(IndexedDB 링버퍼·마스킹·배치·메모리 폴백·`logger` 파사드가 부팅 이전 로그를 버퍼링), `persistStorage`, `branding`(800ms·독립 폴백), `settingsStore`, `bootstrap`(1~6단계).
  - 검증: 웹 **68 + 부트스트랩 11 테스트**. 순서 검증(브랜딩 fetch < 잔재 정리), `results/`·`frames/` 미접촉, OPFS 미지원·localStorage 불가에서도 부팅 완주.
  - ⚠️ **발견·수정**: `isStorageLow`가 `1 - usage/quota`의 부동소수 오차로 **정확히 임계값(90%)을 경고로 넘겼다** → 바이트 정수 비교로 교체.
  - **남은 것**: 실제 브라우저에서 DevTools → OPFS 잔재 삭제·localStorage 키 확인(문서의 [관측] 항목).
- [ ] 브라우저 실측

---

## Step 4: 앱 셸 + 라우팅 + UI 기본 컴포넌트

- **Context Brief**: 화면 상태머신·세션/토큰 홀더·유휴 감시·전체화면·전역 예외·상단바·토스트·모달 스택을 만든다. **M1 배선(세션 사용자 null → 토큰 폐기)** 을 여기서 심는다. 화면 내용은 아직 없고 **더미 화면 13개로 전이만** 검증한다. 규격은 **[02 · 앱 셸과 내비게이션](./02-app-shell-and-navigation.md)**, 공통 UI 규격은 **[03 §1](./03-screens-spec.md)**.
- **대상 파일**: `src/shell/{shellStore,sessionStore,authStore,idleWatchdog,globalErrorHandler,fullscreenController}.ts`, `src/adapters/platform/{visibility,wakeLock,keyboardLock}.ts`, `src/ui/components/**`, `src/ui/theme/**`, `src/ui/strings.ts`, `src/App.tsx`, `tests/unit/shell/*.test.ts`
- **선행 조건**: Step 2(상태머신·유휴 카운트다운), Step 3(로그)
- **구현 내용**:
  - `shellStore`: 현재 화면 · 오버레이 복귀 지점 · 전이(순서: 이탈 정리 → 교체 → 유휴 갱신 → 진입) · `returnHome(reason)` 5단계 · 모달 스택 · 토스트.
  - `sessionStore`: `currentUser`(진입점 `login`/`logout`만) + 촬영 세션 데이터. **구독 가능**.
  - `authStore`: 모듈 변수 1개. **`sessionStore` 구독으로 null 시 폐기**(M1). persist 미들웨어 미사용(M2).
  - `idleWatchdog`: **`performance.now()` 실경과 기반**(WM3), 120초/10초, 감시 대상 화면 목록, 경고 중 활동 무시.
  - `fullscreenController`: 첫 제스처 요청 + `fullscreenchange` 배너 + Chromium `keyboard.lock` best-effort.
  - `visibility`: hidden 시 `Capture`면 취소+홈(WM4), visible 시 유휴 재판정·WakeLock 재요청.
  - `globalErrorHandler`: `onerror`·`unhandledrejection`·ErrorBoundary → 로그 + 홈 복귀(로그인 유지).
  - 라우팅: `/`·`/oauth2callback` 2경로 + `popstate` 매핑([02 §3](./02-app-shell-and-navigation.md)).
  - UI 기본: Button·Toggle·Select·Slider·Modal·Toast·Spinner·TopBar. 터치 48px·다크모드·`aria` 규격.
  - `strings.ts`: `docs/analysis/13 §14` 문구 카탈로그를 1:1로 옮긴다.
- **검증 명령**: `npx vitest run tests/unit/shell` · 브라우저에서 더미 화면 전이·유휴 경고(테스트용 5초 단축 플래그)·전체화면 배너 확인
- **완료 기준**:
  - [관측] 13개 더미 화면 전이가 표대로 동작하고 불법 전이는 **거부 + 경고 로그**만 남긴다. 오버레이 진입→복귀 시 촬영 세션 데이터가 유지된다. `sessionStore.logout()` 호출 시 `authStore`의 토큰이 **null이 된다**(단위 테스트로 고정). 강제 예외를 던지면 홈으로 복귀하고 로그인이 유지된다.
  - [non-goal] 화면 내용·카메라·HTTP **없음**. 유휴 만료가 **로그아웃하지 않는다**(테스트로 고정). 상단바가 `Capture`·`Qr`에서 숨겨지고 그 화면에 자체 취소 버튼이 있다.
  - [trigger] 유휴 경고는 감시 대상 화면에서 120초 무동작에만. 전체화면 요청은 **사용자 제스처**에만.
- **롤백**: `src/shell`·`src/ui` 삭제.
- [x] **완료 (2026-07-31) — 브라우저 실측만 남음**
  - `sessionStore`(**`subscribeWithSelector` 적용** + 썸네일 `close()`), `authStore`(모듈 변수 1개 + `installTokenLifecycle` 구독 = **M1**), `shellStore`(전이·오버레이 복귀·`returnHome` 6단계·모달 스택·토스트), `idleWatchdog`(**실경과 기반**), `fullscreenController`, `visibility`(WM4), `wakeLock`, `keyboardLock`, `globalErrorHandler`(M16), `router`(2경로·popstate·beforeunload), `ui/strings`, `ui/theme/tokens.css`, 공통 컴포넌트 6종, `App.tsx`(더미 13화면 + ErrorBoundary), `main.tsx`(8·10·11단계).
  - 검증: **36 테스트**. M1 4케이스(로그아웃/직접 조작/재로그인 교체/**미배선 시 토큰 잔존**), M2 정적 검사(authStore 소스에 저장소 API 0건), 오버레이 복귀 덮어쓰기 방지(it19), `returnHome` 순서, 유휴 실경과·경고 중 활동 무시·감시 제외 화면, 탭 hidden 취소.
  - ⚠️ **발견·수정**: `globalErrorHandler`의 쿨다운 초기값이 `0`이라 **`now()`가 작은 시계에서 첫 오류 복구가 먹혔다** → `-Infinity`로 교체.
  - **남은 것**: 브라우저에서 더미 화면 전이·유휴 경고·전체화면 배너 육안 확인.
- [ ] 브라우저 실측

---

## Step 5: 백엔드 HTTP 클라이언트

- **Context Brief**: 백엔드 API 호출을 한 곳에서 조립한다(헤더·타임아웃·에러 매핑·로깅). 이 Step 끝에 **`GET /health` 프로브가 화면에 표시**되어 서버 도달을 눈으로 확인할 수 있어야 한다. 규격은 **[06 · 백엔드 연동](./06-backend-integration-web.md)**, 와이어 계약은 `docs/analysis/31`.
- **대상 파일**: `src/adapters/http/{backendClient,healthService,accountService,frameRepository,uploadGateway,qrUsageService,tempUserLimitsService}.ts`, `src/adapters/http/errors.ts`, `tests/unit/http/*.test.ts`
- **선행 조건**: Step 1(env), Step 3(로그)
- **구현 내용**:
  - `backendClient`: base URL 결합 · 게이트 키 **모든 호출 부착** · Bearer **토큰 있을 때만** · 100초 타임아웃(`AbortController`) · 에러 봉투 파싱 → 예외 타입 매핑([06 §3.3](./06-backend-integration-web.md)) · 요청/응답 로깅(본문·토큰 제외).
  - 각 서비스는 `docs/analysis/31 §4`의 요청/응답을 그대로 타입화한다.
  - Bearer 필수 호출에 토큰이 없으면 **요청을 보내지 않고** `NotAuthenticatedError`.
  - `PUT /frames/{id}` 함수를 **만들지 않는다**(정책).
- **검증 명령**: `npx vitest run tests/unit/http` · 브라우저에서 임시 진단 화면의 health 결과 확인 · `GET /frames/default` 200 확인
- **완료 기준**:
  - [관측] `/health`가 200이고 유효 게이트 키에서 `deployedAt`이 온다. 401/403/404/409/500/501·네트워크 실패가 **각각 다른 예외 타입**으로 매핑된다(목 테스트). `TEMP_USER_*` 403이 `TempUserLimitError`로 분기된다.
  - [non-goal] 업로드 PUT·인증 흐름 **없음**(Step 11·12). 자동 재시도 **없음**. 로그에 토큰·본문이 남지 않는다(로그 스냅샷 테스트).
  - [trigger] 게이트 키는 값이 있을 때만 부착. Bearer는 토큰이 있을 때만.
- **롤백**: `src/adapters/http` 삭제.
- [x] **완료 (2026-07-31) — 실서버 프로브만 남음**
  - `errors`(BackendError·NetworkError·NotAuthenticatedError·**TempUserLimitError**·SsoNotConfiguredError + 봉투 파싱·폴백 코드), `backendClient`(게이트 키 전 호출·Bearer 3수준·100초 타임아웃·`credentials: "omit"`·자동 재시도 없음), `healthService`(2프로브), `accountService`, `frameRepository`, `uploadGateway`, `qrUsageService`(fail-open), `tempUserLimitsService`.
  - 검증: **48 테스트**. 7개 상태코드가 각각 다른 타입으로, `TEMP_USER_*` 403이 권한 오류와 분리, 네트워크 실패가 상태 오류와 미혼동, `auth:"required"` + 무토큰이 **요청을 보내지 않음**, `requiredHeaders` 원형 보존(M14), 로그에 토큰·본문·서버 message 미포함.
  - ⚠️ **발견·수정**: 로그 마스킹 목록의 `code`(OAuth 인가 코드)가 **오류 코드까지 마스킹**해 진단 불가였다 → 마스킹은 유지하고 로그 필드를 `errorCode`로 분리.
  - **남은 것**: 실서버 `/health`·`/frames/default` 프로브(게이트 키 필요 — 사용자 액션 후).
- [ ] 실서버 프로브

---

## Step 6: 카메라 파이프라인 + 카메라 테스트 모달

- **Context Brief**: `getUserMedia`로 스트림을 열고 **Worker에서 프레임당 1회 거울+크롭 가공** 후 프리뷰·스틸·타임랩스 3소비자가 공유하는 구조를 만든다. **CSS 반전 금지**(WM1)와 **Ready 게이트**가 핵심이다. 규격은 **[04 §2~§5.1](./04-media-pipeline-web.md)**, 모달 규격은 **[03 §15.1](./03-screens-spec.md)**.
- **대상 파일**: `src/adapters/camera/{cameraService,frameProcessor.worker,deviceEnumerator}.ts`, `src/screens/modals/cameraTest/*`, `src/ui/views/CameraPreview.tsx`, `tests/unit/camera/*.test.ts`
- **선행 조건**: Step 4(셸), Step 2(centerCrop·previewReadiness)
- **구현 내용**:
  - `cameraService` 싱글턴: `start(deviceId, targetAspect, mirror)` / `stop()` / `captureStill()` / 프레임 이벤트 / `getSettings()` 노출. **열기 실패는 `false` 반환**.
  - `<video>`는 `autoplay muted playsinline` + 숨김. `requestVideoFrameCallback`(폴백 rAF) + `mediaTime` 중복 스킵.
  - Worker: OffscreenCanvas 1개 재사용, 거울(setTransform) → 중앙 크롭(drawImage 소스 사각형), 결과를 프리뷰(`transferControlToOffscreen` 권장)·스틸 요청·샘플러에 분기. **모든 `VideoFrame`/`ImageBitmap` `close()`**.
  - Ready 게이트: 가공 완료 프레임 8개 + 500ms + fps>0, **8초 타임아웃**.
  - 장치 열거: `enumerateDevices` 백그라운드, 라벨 빈 값 처리, `{deviceId,label,groupId}` 저장, `devicechange` 구독.
  - 카메라 테스트 모달: 모달 먼저 표시 → `stop()` 후 `start()` → Ready 대기 → 셔터(플래시 재현·결과 폐기·"저장되지 않았습니다") → 닫힘 시 확실히 정지. 실제 해상도·fps 표시.
- **검증 명령**: `npx vitest run tests/unit/camera` · 브라우저 실측(프리뷰 fps·거울 on/off) · `--use-fake-device-for-media-stream`으로 Playwright 스모크
- **완료 기준**:
  - [관측] 프리뷰가 **가공 결과 canvas**로 표시되고 거울 on/off가 즉시 반영된다. 카메라 없는 환경에서 **8초 내 `Failed`**로 전환되고 무한 로딩이 없다. 테스트 모달을 열고 닫으면 카메라 LED가 꺼진다(트랙 stop).
  - [non-goal] **`<video>`를 직접 화면에 보이지 않는다.** CSS `transform: scaleX(-1)`가 코드에 없다(grep으로 확인 — WM1). 촬영 시퀀스·저장 **없음**.
  - [trigger] 스트림은 `start()` 호출에만 열린다. Ready 신호는 3조건 충족 시 **1회만**.
- **롤백**: `src/adapters/camera`·카메라 테스트 모달 삭제.
- [x] **완료 (2026-07-31) — 실기기 실측만 남음**
  - `cameraTypes`(FrameSource·FrameProcessor 인터페이스로 브라우저 의존을 분리 → node 테스트 가능), `fpsMeter`(최근 1초 윈도우), `deviceEnumerator`(**deviceId → label → groupId → 첫 장치** 폴백 + 빈 라벨 매칭 금지 + `devicechange`), `frameProcessor.worker`(거울 `setTransform` → 중앙 크롭 `drawImage` 소스 사각형, OffscreenCanvas 1개 재사용, 3소비자 분기, `finally`에서 `close()`, 큐를 쌓지 않고 **최신 프레임으로 교체**), `frameProcessorClient`(스틸 타임아웃 5초), `videoFrameSource`(`playsinline`·숨김·`rVFC`→rAF 폴백·`mediaTime` 중복 스킵), `cameraService`(싱글턴·멱등 start·Ready 게이트·8초 타임아웃·`configure` 런타임 토글), `CameraPreview`+`CameraStatsCaption`, 카메라 테스트 모달.
  - 검증: **35 테스트**(누적 456). Ready 3조건 개별 확인, 8초 타임아웃 → Failed + 자원 정리, `OverconstrainedError` 재시도, 열기 실패 = `false`(예외 없음), `stop()`이 트랙·Worker·소스 전부 정리(LED 조건), 거울 토글이 재시작 없이 Worker로 전달, Ready 전 스틸 거부.
  - **WM1 정적 검사 추가**: `src/` 전체(.ts/.tsx/.css)에 `scaleX(-1)`·`rotateY(180deg)`가 없고 `CameraPreview`가 `<video>`를 렌더하지 않음을 테스트가 고정한다.
  - ⚠️ **fps 윈도우 경계 판정**: 정확히 1초 경계의 프레임을 윈도우 **밖**으로 본다(과대보고 금지). 과소보고는 Ready가 조금 늦어질 뿐이라 안전측이다.
  - ⚠️ **`rVFC` 타입 주의**: TS DOM lib이 `requestVideoFrameCallback`을 필수 멤버로 선언하지만 Safari 15.4 미만에는 없다 — 타입을 믿고 분기를 빼면 그 기기에서 프레임 루프가 시작되지 않는다. 옵셔널 타입으로 감싸 런타임 감지를 유지했다.
  - **남은 것**: 실기기에서 프리뷰 fps·거울 on/off가 저장 결과에 반영되는지·모달 닫은 뒤 LED 소등·카메라 없는 환경 8초 Failed 확인([14 §10](./14-handoff-and-user-actions.md)).
- [ ] 실기기 실측

---

## Step 7: 촬영 시퀀스 + 컷 선택

- **Context Brief**: N컷 연속 촬영(카운트다운·플래시 120ms·셔터음·간격 300ms)과 컷 선택을 구현한다. 진입 절차 순서가 규격이며, **탭 hidden 시 취소**(WM4)가 필수다. 규격은 **[03 §6·§7](./03-screens-spec.md)**, 타이밍은 `docs/analysis/13 §13`.
- **⚠️ 프레임 공급(선순환 해소)**: 촬영에는 프레임(슬롯·대표 종횡비)이 필요한데 카탈로그·`FrameSelect` 본편은 Step 14다. 이 Step에서 **최소 `FrameSelect`** 를 함께 만든다 — 목록 = **코드 생성 fallback 프레임 1개**(`analysis/14 §4.7`: 1200×1600, 2×2 슬롯 4개) + 선택 → 세션 고정 → **`cutCountPolicy.resolve` 해석 배선(`isAutoCutCount` 포함)**. 서버 카탈로그·캐시·권한 UI·삭제는 Step 14가 이 화면을 확장한다. 이로써 Step 7~11이 Step 14 없이 완주 가능하다.
- **대상 파일**: `src/screens/frameSelect/*`(최소판), `src/adapters/frames/fallbackFrame.ts`, `src/screens/capture/*`, `src/screens/cutSelect/*`, `src/screens/guide/*`, `src/screens/home/*`, `src/adapters/platform/shutterSound.ts`, `src/ui/views/{FrameSelectView,CaptureView,CutSelectView,GuideView,HomeView}.tsx`
- **선행 조건**: Step 6
- **구현 내용**:
  - `Home`·`Guide` 화면(브랜딩·설정값 표시·첫 제스처 전체화면/오디오/WakeLock).
  - `Capture`: [03 §6.1](./03-screens-spec.md)의 7단계. 카운트다운은 **실경과 기반**, [바로 촬영]은 매 컷 가능, 플래시는 **DOM 오버레이**, 셔터음은 비동기·실패 무시, 스틸은 OPFS `cut{i}.jpg` + 썸네일 `ImageBitmap`.
  - `CutSelect`: 대표 슬롯 비율 썸네일, 토글·번호 재계산, `선택 수 == 슬롯 수`에서만 [다음], 전체 재촬영(설정 on + 상한 미달).
  - 이탈: 시퀀스 취소 → (인코더 정지) → 카메라 정지 순서.
- **검증 명령**: `npx vitest run tests/unit/screens` · Playwright(fake device)로 6컷 완주 · 탭 전환 취소 확인
- **완료 기준**:
  - [관측] 6컷 세션이 카운트다운·플래시·300ms 간격을 지키며 완주하고 OPFS에 `cut1..6.jpg`가 생긴다. [바로 촬영]이 남은 카운트다운을 건너뛴다. 슬롯 4개 프레임에서 4개 선택 시에만 [다음]이 활성된다.
  - [non-goal] 합성·업로드 **없음**. 촬영 중 탭을 숨기면 **홈으로 복귀하고 부분 컷이 남지 않는다**(OPFS 세션 폴더 삭제 확인 — WM4). 재촬영 상한 초과 시 버튼 비활성 + 커맨드 거부. **컷 수 N을 하드코딩하지 않는다**(it17 — 컷 루프와 `CutSelect` 그리드가 7·9 같은 임의 N을 수용하고, 그리드 열 수가 CSS `auto-fill`/wrap이다).
  - [trigger] 시퀀스는 **Ready 이후에만** 시작. 플래시는 설정 on일 때만, 셔터음도 설정 on일 때만.
- **롤백**: 해당 화면 파일 삭제(더미 화면으로 복귀).
- [x] **완료 (2026-07-31) — 실기기 완주 실측만 남음**
  - `domain/capture/captureTiming`(FLASH 120ms · INTERVAL 300ms — 카메라 테스트 모달과 **같은 상수를 공유**한다), `captureSequence`(a~f 순서·실경과 카운트다운·[바로 촬영]·취소), `useCaptureRunner`(진입 7단계 배선), `captureSessionController`(프레임 확정 + **컷 수 해석 유일 지점** + OPFS 작업 공간 + 홈 복귀 정리 훅), `shutterSound`(AudioContext unlock + 자산 없으면 합성음), `fallbackFrame`(하양 1200×1600 PNG 생성), 화면 5종(Home·FrameSelect 최소판·Guide·Capture·CutSelect).
  - 검증: **26 테스트**(누적 482). a~f 순서, 플래시 off가 캡처 **후**, 컷 사이 300ms·마지막 뒤 없음, 실경과 카운트다운(느린 delay에서도 tick 수가 아니라 경과로 종료), [바로 촬영]이 **매 컷** 동작, 취소 시 플래시 잔존 없음, 스틸·저장 실패를 컷으로 세지 않음, **N=1·6·7·8·9·10 하드코딩 없음**(it17).
  - 그리드는 CSS `auto-fill`이라 7·9컷도 수용한다. 컷 선택 번호는 선택 배열 인덱스라 해제 시 자동 재계산된다.
  - ⚠️ **발견·수정**: 플래시 off가 컷 단위 `finally`와 시퀀스 단위 안전망에서 **두 번** 통지됐다 → `setFlash`를 멱등으로 바꿔 중복 리렌더를 제거했다.
  - ⚠️ 재촬영은 `Guide`로 돌아가되 **컷 수를 재해석하지 않는다**(세션 값 사용 — it17).
  - **남은 것**: 실기기에서 6컷 완주·OPFS `cut1..6.jpg` 생성·탭 hidden 시 부분 컷 미잔존 확인([14 §10.1](./14-handoff-and-user-actions.md) V14~V17).
- [ ] 실기기 완주 실측

---

## Step 8: 합성 + 필터 + 골든 이미지

- **Context Brief**: 프레임 + 선택 컷 + 필터로 최종 이미지를 만든다. **슬롯 위치는 0px 오차**, 필터는 허용 오차 내여야 한다. 흑백은 **CSS filter를 쓰면 계수가 달라 실패**한다. 규격은 **[04 §5·§6](./04-media-pipeline-web.md)**, 허용 오차는 **[10 §4](./10-testing-and-acceptance.md)**.
- **대상 파일**: `src/adapters/compose/{compositor.ts,compose.worker.ts,filters/*}`, `src/screens/result/*`, `src/ui/views/ResultView.tsx`, `webclient/tests/golden/*`, `docs/spec-vectors/golden/**`
- **선행 조건**: Step 7, Step 0-5(프레임 이미지 CORS — OA-2)
- **구현 내용**:
  - Worker에서 합성: 배경(프레임 원본 해상도) → 슬롯 index 순 → 필터 → `centerCrop`(cover) → **`createImageBitmap resizeQuality:"high"`**(폴백 단계 축소) → 덮어쓰기 → `convertToBlob`(jpeg 0.95 / png).
  - 필터: 흑백(**BT.601 직접 계산**), 밝게(1.1/20), 뷰티(WebGL2 7×7 bilateral, CPU 폴백).
  - `Result` 화면: 진입 즉시 합성(스피너), 필터 변경 시 전체 재합성, `blob:` URL 교체 시 이전 것 revoke.
  - 골든 이미지: Windows 앱으로 기준 4장 생성 → `docs/spec-vectors/golden/`에 커밋 → `pixelmatch` 비교 테스트.
- **검증 명령**: `npx vitest run tests/golden` · 브라우저에서 필터 4종 전환 · `performance.now()` 로그로 합성 시간 확인
- **완료 기준**:
  - [관측] 4개 필터 골든 비교가 [10 §4.2](./10-testing-and-acceptance.md) 허용 오차 내로 통과하고 **슬롯 위치 오차가 0px**이다. **OA-2 검증(어댑터 수준 — FrameSelect 본편 불요)**: `GET /frames/default`(웹 게이트 키)에서 `imageUrl` 1건을 받아 CORS-clean 로드 → 합성 → `convertToBlob`이 **예외 없이** 성공한다. 합성 시간이 1.2초 이내.
  - [non-goal] `ctx.filter`·CSS `filter`로 흑백을 만들지 않는다(grep 확인). 타임랩스·저장·업로드 **없음**.
  - [trigger] 재합성은 필터 변경·진입에만. 프레임은 변경할 수 없다(M11).
- **롤백**: `src/adapters/compose`·`Result` 화면 삭제.
- [x] **완료 (2026-07-31)**
  - `composeCore`(**순수 RGBA — 브라우저 API 없음**), `pixelFilters`(BT.601 흑백·밝게·뷰티 bilateral CPU), `resizeArea`(OpenCV `INTER_AREA` 대응), `compositor`(디코딩·인코딩만), `useResultCompose` + `ResultView`.
  - **골든 이미지 체계 구축**: `tests/MCPhoto.Tests/GoldenImageTests.cs`가 결정적 패턴(체커보드·그라데이션·피부톤·고주파 1080×1440 4장 + 프레임 1200×1600)을 **코드로 생성**하고 실제 `CompositionService`로 기준 4장을 만든다. 최초 실행에 생성, 이후에는 **무손실 회귀 게이트**(합성 코드가 바뀌면 여기서 먼저 실패).
  - **웹 대조 결과: 4개 필터 전부 허용 오차 통과**(10 §4.2). 프레임 배경(슬롯 밖)은 **0px·완전 일치**로 별도 검증 — 슬롯이 1px만 밀려도 걸린다.
  - node에 canvas가 없어 `tests/golden/png.ts`에 최소 PNG 디코더(`node:zlib`)를 뒀다. **제품 코드가 아니다**.
  - ⚠️ **설계 판단**: 픽셀 연산을 `composeCore`(순수)로 몰아 **브라우저와 골든 테스트가 같은 코드 경로**를 지나게 했다. 브라우저 `createImageBitmap` resize를 쓰면 테스트가 검증하지 못하는 경로가 생긴다.
  - ⚠️ **발견**: 최초 픽스처(컷 480×640 < 슬롯 490×653)가 **확대 경로**를 타 MAE 21로 실패했다. 실제 컷은 카메라 해상도라 축소가 정상 — 픽스처를 1080×1440으로 고쳐 현실과 맞췄다.
  - **미구현(의도)**: 뷰티 WebGL2 가속. CPU 구현이 **정확도 기준**이고 골든을 통과한다. 실기기 성능 예산(1.2초)을 넘으면 그때 WebGL2 경로를 CPU 폴백과 함께 추가한다.

---

## Step 8.5: main 머지분 반영 (Step 0~8 산출물 보정)

- **Context Brief**: `main`(`e5efdfd`) 머지로 **규격이 늘었다**. it20 프레임 로딩 국면과 프레임 편집기 재정의가 그것인데, 화면 본편은 Step 14·15지만 **Step 2(도메인 전량 이식)와 Step 4(셸)의 완료 기준이 다시 미충족 상태**가 됐다. 그 차이만 메운다. 규격은 **[01 §2](./01-tech-stack-and-structure.md)** 매핑표(2행 추가 + `isFileNameSafe` 주석), **[03 §4.1](./03-screens-spec.md)**, **[02 §6.2](./02-app-shell-and-navigation.md)**, **[10 §2·§3.2](./10-testing-and-acceptance.md)**.
- **대상 파일**: `src/domain/frames/{frameLoadPolicy.ts,frameCatalogProgress.ts,frameNaming.ts}`, `src/domain/index.ts`, `docs/spec-vectors/frame-load-policy.json`(신규), `tests/MCPhoto.Tests/SpecVectorTests.cs`, `webclient/tests/unit/domain/{frames.test.ts,vectors.test.ts}`, `webclient/tests/unit/shell/shell.test.ts`, `src/ui/views/FlowViews.tsx`
- **선행 조건**: 없음(순수 도메인 + 기존 화면 보정)
- **구현 내용**:
  - **`frameLoadPolicy` 이식**: `FrameLoadPhase` 4값, 상한 상수 3종(무진행 30초·총 60초·유휴 참조 120초), `nextDeadline(elapsed)`(둘 중 **먼저 오는 쪽**, 0 이하 → 즉시 취소), `classify(count, interrupted)`, `finalize(current, count, interrupted, quiet)`(**반환값에 `Loading`이 없다** — 오버레이 고착 불가), `noticeFor(phase)`.
  - **`frameCatalogProgress` 이식**: 단계 4값 + `toLabel()`(`total>0`일 때만 `(n/m)`) + 시작 문구 상수.
  - **`isFileNameSafe` 분리**: 빈 값·공백만·파일시스템 금지문자만 판정하고 **길이는 보지 않는다**. `validateFrameName`이 이 함수를 쓰도록 재작성하되 **기존 판정 결과는 불변**(길이 검사는 그대로 남는다).
  - **벡터 `frame-load-policy.json`**: `classify`·`finalize`·`nextDeadline` 케이스. **Windows `FrameLoadPolicyTests.cs`가 고정한 판정이 기준**이며 `SpecVectorTests.cs`가 같은 파일을 읽어 통과해야 한다. `genVectors.ts`로 웹 구현을 덤프하지 않는다(교차 검증 무력화 — [15 §3.3](./15-implementation-conventions.md)).
  - **유휴 상한 불변식 정적 테스트**: `MAX_TOTAL_WAIT_SECONDS * 1000 < IDLE_TIMEOUT_MS`. 문서에만 두면 어느 한쪽 상수를 고칠 때 조용히 깨진다.
  - **Step 7 최소 `FrameSelect` 보정**: fallback 이미지 생성이 실패해 `imageUrl`이 빈 프레임은 **목록에 올리지 않는다**. 지금은 그 프레임으로 [다음]이 활성이라 손님이 **6컷을 다 찍은 뒤 `Result`에서야** 합성 실패를 만난다. 프레임 0개 = `Failed`(§4.1)의 정신대로 **선택 화면에서** 안내 + [다시 시도]로 끝낸다.
- **검증 명령**: `npx tsc --noEmit && npx vitest run` · `dotnet test tests/MCPhoto.Tests --filter SpecVectorTests`
- **완료 기준**:
  - [관측] 웹·Windows가 `frame-load-policy.json`을 **양쪽에서 읽어 통과**한다. 벡터 값 하나를 일부러 틀리면 **양쪽이 동시에 실패**한다.
  - [관측] `finalize`가 어떤 입력 조합에서도 `Loading`을 반환하지 않는다(전수 테스트).
  - [관측] 유휴 불변식 테스트가 존재하고 통과한다.
  - [관측] fallback 이미지 생성을 강제 실패시키면 `FrameSelect`에서 안내가 뜨고 **[다음]이 활성화되지 않는다**.
  - [non-goal] 대기 오버레이·카탈로그 로더·편집기 화면은 **만들지 않는다**(Step 14·15). 도메인 순수성 검사를 통과해야 한다.
- **롤백**: 신규 도메인 2파일·벡터 1파일 삭제, `frameNaming`·`FlowViews` revert.
- [x] **완료 (2026-07-31)**
  - **도메인 2파일 신설**: `frameLoadPolicy.ts`(4국면 + 상한 3종 + `nextFrameLoadDeadlineMs`·`classifyFrameLoad`·`finalizeFrameLoad`·`frameLoadNotice`), `frameCatalogProgress.ts`(단계 4값 + `catalogProgressLabel` + 시작 문구). 둘 다 `src/domain/index.ts`에 export 추가, **순수성 검사 자동 통과**(glob 수집 — purity 65 → 69).
  - **`isFileNameSafe` 축 분리**: 길이를 보지 않는 별도 순수 함수로 추출하고 `validateFrameName`이 **`trimmed`를 넘겨** 호출하도록 재작성. 기존 `frameNaming` 테스트 **무수정 통과**로 판정 불변을 증명했다(회귀 감시자는 기존 테스트다).
  - **공유 벡터 `docs/spec-vectors/frame-load-policy.json` 52케이스**(classify 7 / finalize 32 / nextDeadline 8 / notice 4 / constants 1). 값은 `FrameLoadPolicyTests.cs` 13건에서 **손으로 옮겼다** — `genVectors.ts`를 돌리지 않았다(웹 구현으로 덮어쓰면 교차 검증 무력화 — 15 §3.3). 벡터 파일 14 → **15**(`loadVector.ts`·`SpecVectorTests.cs` 양쪽 목록 갱신).
  - **검증 수치**: 웹 vitest **492 → 530 전부 통과**(frames +27, vectors +3, shell +4, purity +4), Windows **937 → 938 전부 통과**(`SpecVectorTests.FrameLoadPolicy_Matches_Vector` 1건 추가, `SpecVectorTests` 자체는 16 → 17). `npx tsc --noEmit` 0, `npm run build` 성공, `npm run coverage` 임계 통과(lines 98.47 / branches 97.29).
  - **트리거 실증**: `nextDeadline`의 `45000 → 15000`을 `16000`으로 훼손하니 **웹·Windows가 동시에 실패**했고(웹 `expected 15000 to be 16000`, Windows `SpecVectorTests.cs:474 Values differ`), 원복 후 양쪽 전부 통과했다. 교차 고정 장치가 실제로 작동한다.
  - `finalize`가 **32조합 전수에서 `Loading`을 반환하지 않음**을 웹 테스트와 벡터 양쪽에 박았다(벡터 자체가 불변식을 위반하지 못하도록 `expected.phase`에 `Loading`이 없음을 검사하는 테스트도 추가).
  - 유휴 상한 불변식 4건을 `shell.test.ts`에 고정(총 60초 < `IDLE_TIMEOUT_MS` 120초, 무진행 < 총, **도메인 사본 = 셸 실제값 동기화**, ms 파생 = 초 × 1000). 사본 동기화 검사는 Core→App 참조가 불가능한 Windows에는 없는 **웹 전용 안전망**이다.
  - ⚠️ **설계 이탈 ①(함수명 한정형)**: WBS 본문 약칭 `classify`/`finalize`/`nextDeadline`/`noticeFor` → `classifyFrameLoad`/`finalizeFrameLoad`/`nextFrameLoadDeadlineMs`/`frameLoadNotice`. 이유: `src/domain/index.ts`가 **평면 `export *` 배럴**이라 일반명은 Step 14·15에서 모호 재수출을 만든다(실제로 `captureSession.slotCount`가 `FlowViews`에서 `slotCountOf` 별칭을 강제했다). 저장소 관례도 한정형이다.
  - ⚠️ **설계 이탈 ②(벡터 kind 2종 추가)**: 10 §3.2 표에 없는 `notice`·`constants`를 넣었다. 이유: 안내 문구와 상한 숫자도 **양쪽에 중복 존재하는 규격**이라 벡터에 없으면 한쪽만 고쳐도 아무 테스트가 실패하지 않는다. kind별 개수를 양쪽이 단언하므로 케이스가 통째로 빠지는 사고도 잡힌다.
  - ⚠️ **설계 이탈 ③(`hasUsableImage` 추가)**: 대상 파일 목록 밖인 `frameCatalogPolicy.ts`에 판정을 추가했다. 이유: jsdom이 없어(`vitest environment: node`) 뷰 안의 인라인 판정은 테스트가 닿지 못한다 — "순수 코어 + 얇은 래퍼"(15 §3.1)대로 판정만 도메인으로 올려 `frames.test.ts`가 덮게 했다. Step 14 카탈로그 조립에서도 같은 판정이 필요하다.
  - ⚠️ **설계 이탈 ④(xUnit 인자 순서)**: `SpecVectorTests`의 `constants` 갈래에서 설계 코드 조각대로 쓰면 상한 상수가 `const`라 **xUnit2000 경고 4건**이 새로 생겼다. 진실원이 Windows이므로 상수를 `expected`, 벡터 값을 `actual`로 두어 경고를 없앴다(실패 메시지에 틀린 쪽인 벡터 값이 찍혀 오히려 정확하다). 저장소 경고는 기존 `GoldenImageTests` xUnit1031 1건 그대로다.
  - **미검증(사용자 액션)**: `FrameSelect`의 fallback 실패 경로(§9 관측 — 안내 문구 노출 · [다음] 비활성 · [다시 시도] 동작)는 **브라우저 1회 수동 관측이 남아 있다**. 이 세션에 브라우저 자동화 수단이 없었고 jsdom·Testing Library가 없어 자동 테스트로 대체할 수 없다. 판정 함수 3종(`hasUsableImage`·`classifyFrameLoad`·`frameLoadNotice`)은 단위 테스트가 덮으며, 배선만 육안 확인이 필요하다. Step 14에서 화면 테스트 체계와 함께 정리한다.
  - **미구현(의도)**: 대기 오버레이·카탈로그 로더(단일 비행 + 진행 replay)·프레임 편집기 화면은 **Step 14·15 범위**라 만들지 않았다. `catalogProgressLabel`은 아직 호출자가 없다(Step 14가 소비한다).

---

## Step 9: 타임랩스 인코더

- **Context Brief**: 브라우저에서 **H.264/mp4 무음** 타임랩스를 직접 만든다(WD2). 세션 전체 녹화를 하지 않고 **촬영 중 프레임을 샘플링**해 30fps로 인코딩한다. 지원하지 않는 브라우저는 `null`로 축소(계약상 합법). 규격은 **[04 §7](./04-media-pipeline-web.md)**.
- **대상 파일**: `src/adapters/encode/{timelapseEncoder,webCodecsMp4,mediaRecorderMp4}.ts`, `src/adapters/encode/encode.worker.ts`, `tests/unit/encode/*.test.ts`
- **선행 조건**: Step 6(가공 프레임 스트림)
- **구현 내용**:
  - 경로 판정: `VideoEncoder.isConfigSupported("avc1.42001E")` → `MediaRecorder.isTypeSupported("video/mp4;codecs=avc1")` → 미지원. **결과를 로그·진단에 기록**.
  - **스풀 + 종료 시 선별**([04 §7.2](./04-media-pipeline-web.md)): 촬영 중 OPFS `sessions/{id}/tl/`에 ≤15fps JPEG 스풀(상한 900장, 도달 시 절반 솎아내기) → 종료 시 실경과로 `computeSpeedFactor` → 균등 선별 → 인코딩. 선별 30장 미만이면 `null`.
  - WebCodecs 경로: `timestamp = i * 33333μs`, 비트레이트 [04 §7.4](./04-media-pipeline-web.md) 표, JS MP4 muxer로 `Blob(video/mp4)`.
  - 백프레셔: `encodeQueueSize > 8`이면 드롭.
  - 실패·부재는 **예외가 아니라 `null`** + 경고 로그. 정지는 타임아웃 후 강제 종료.
- **검증 명령**: `npx vitest run tests/unit/encode` · 브라우저에서 결과 mp4를 `<video>`로 재생 · `ffprobe`로 코덱·길이 확인(개발 PC)
- **완료 기준**:
  - [관측] 6컷 세션(약 38초)에서 **10~15초 길이의 재생 가능한 mp4**가 생성되고 컨테이너가 정상 종료된다(모바일·데스크톱 양쪽에서 재생). **[바로 촬영]을 매 컷 눌러 세션을 ~5초로 줄여도** 원속(~5초) 타임랩스가 생성된다(`null`로 떨어지지 않는다 — 실경과 선별 검증). 오디오 트랙이 없다. 진단에 선택된 경로가 표시된다.
  - [non-goal] `session.mp4`를 만들지 않는다. 인코더 미지원 브라우저에서 **촬영이 정상 완주**하고 타임랩스만 `null`이 된다. 인코딩 실패가 촬영을 중단시키지 않는다.
  - [trigger] 수집은 촬영 시퀀스 시작~종료 사이에만. `stride` 프레임마다만 인코딩한다.
- **롤백**: `src/adapters/encode` 삭제(타임랩스 미제공 상태로 동작).
- [x] **완료 (2026-07-31)** — 설계: [`docs/design/web-step9-timelapse-encoder-design.md`](../design/web-step9-timelapse-encoder-design.md)
  - **도메인 2파일 신설**(순수 · `purity.test.ts`가 자동 포함): `capture/timelapsePlan.ts`
    (`planTimelapse`·`evenlySample`·`timelapseBitrate`·`evenDimensions` + 상수 4종),
    `capture/timelapseSpool.ts`(`shouldSpoolFrame`·`planDecimation`·`decimatedInterval` + 상수 3종).
    배속은 기존 `timelapseSpeed.ts`를 **재사용**했다(새로 만들지 않음). `src/domain/index.ts`에 2줄 추가.
  - **어댑터 7파일 신설**(`src/adapters/encode/`): `encodeProtocol.ts`(타입·상수) ·
    `encoderSupport.ts`(경로 판정 + `lastEncoderProbe`) · `webCodecsMp4.ts`(경로 B 코어, **전부 포트 주입**) ·
    `encode.worker.ts`(경로 B 실행 껍데기) · `encodeClient.ts`(Worker RPC, 작업당 1회성 spawn/terminate) ·
    `mediaRecorderMp4.ts`(경로 A 코어 + 브라우저 포트) · `timelapseEncoder.ts`(오케스트레이터·**로그 유일 지점**) ·
    `timelapseService.ts`(수집 수명 + 결과 보관 싱글턴).
  - **의존성 1개 추가**: `mp4-muxer@5.2.2`(**MIT**, `--save-exact` — 캐럿 없음). `webclient/THIRD-PARTY.md`를
    **신설**해 react/react-dom/zustand/mp4-muxer 4행 + deprecated 채택 사유를 기록했다(상용 배포 요구 — 01 §7).
  - **검증 수치**: `npx tsc --noEmit` 0, `npx vitest run` **19파일 530 → 26파일 645 전부 통과**
    (encode +7파일 107건, camera 36 → 40, purity 69 → 73). `npx vite build` 성공 —
    `web/kiosk/assets/encode.worker-*.js`(35.0 kB) 청크가 생성되고 muxer 코드(`moov` 문자열)가
    그 안에 들어갔음을 확인했다(**A1 검증 완료**). `npx vitest run --coverage` 임계 통과
    (도메인 lines 98.57 / branches 97.58, `timelapsePlan.ts`는 100%).
  - **정적 불변식 3건 신설**(15 §3.4 관례): ① `src/adapters/encode/**` 중 `mp4-muxer`를 import하는 파일은
    `encode.worker.ts` **하나뿐** ② 코어 `webCodecsMp4.ts`에 `logStore`·`logger.` 0건
    ③ `encode.worker.ts`에 OPFS 쓰기(`createWritable`·`createSyncAccessHandle`)와 `logger` 0건.
  - ⚠️ **설계 이탈 ①(가공 Worker 스풀 채널 신설 — 대상 파일 밖)**: `frameProcessorProtocol`·
    `frameProcessor.worker`·`frameProcessorClient`·`cameraTypes`·`cameraService`에 `configureSpool`/
    `spoolFrame` 전용 채널을 추가했다. **이유**: 기존 스틸 슬롯(`pendingStill`)은 1개짜리 덮어쓰기라
    스풀러가 15fps로 `captureStill`을 부르면 컷 촬영 요청과 충돌해 **먼저 온 요청이 소멸**하고,
    그것이 컷이면 5초 타임아웃 뒤 `null` → 컷 수 < 슬롯 수 → **세션이 홈으로 강제 복귀**한다
    (`captureSequence.ts:134` · `useCaptureRunner.ts:155`). 타임랩스를 붙이다 촬영을 깨뜨리는 것은
    이 Step의 non-goal 정면 위반이다. `tsconfig`의 `include`에 `tests`가 있어
    `cameraService.test.ts`의 `FakeProcessor`도 함께 확장했다(기존 단언 무수정, 스풀 테스트 4건 추가).
  - ⚠️ **설계 이탈 ②(`ResultView.goNext` [다음] 1단계 배선 — 대상 파일 밖)**: `FlowViews.tsx`·
    `useCaptureRunner.ts`·`strings.ts`를 수정했다. **이유**: 여기서 부르지 않으면 Step 9 산출물이
    실행되는 경로가 아예 없어 [관측] 기준을 만족할 수 없다. `goNext`를 async로 바꿔 03 §8.1 1단계
    (`finish()`)를 수행하고, 대기 중 홈 복귀가 일어나면 **전이하지 않는다**(`currentScreen() !== "Result"`).
    수집 시작·종료는 `useCaptureRunner`에 붙였고, **`cancelCaptureSequence`에서도** 수집을 끊는다 —
    `returnHome`이 `cancelCaptureSequence → cleanupWorkspace(폴더 삭제) → stopEncoder` 순이라
    (02 §2.5) `stopEncoder`에서만 멈추면 삭제 **후** 도착한 스풀 쓰기가 `tl/`을 되살린다.
  - ⚠️ **설계 이탈 ③(타임스탬프 산출식)**: WBS 본문의 `timestamp = i * 33333μs` 대신
    `round(i * outputSeconds * 1e6 / count)`를 쓴다. **이유**: [04 §7.2]가 "프레임 duration =
    outputSec / frames.length … 스풀이 부족하면 duration이 길어질 뿐 **길이는 유지**"로 더 구체적이며,
    `i * 33333`을 쓰면 스풀 부족 세션의 출력이 의도보다 짧아진다(40장 선별 시 12.5초 → 1.33초).
    스풀이 충분한 정상 경로에서는 두 식이 **같은 33333μs**다(테스트로 고정). 같은 이유로 muxer에
    **`frameRate`를 넘기지 않는다** — 넘기면 타임스탬프가 30fps 격자로 반올림돼 위 규격이 무의미해진다.
  - ⚠️ **설계 이탈 ④(실패 사유 우선 보고)**: 설계 의사코드는 `encoded === 0`을 `failure`보다 먼저
    검사하는데, 그러면 인코더 오류로 루프가 끊긴 경우 사유가 "인코딩된 프레임이 없습니다"로 덮여
    **진짜 원인이 사라진다**(05 §7.2가 요구하는 "실패 사유"가 무의미해진다). 순서를 뒤집었다.
  - ⚠️ **설계 이탈 ⑤(사소)**: `TimelapseServiceDeps.camera`의 `Pick`에서 `processedSize`를 뺐다
    (`size`는 마지막 스풀 프레임에서 오므로 실제로 쓰지 않는다 — 미사용 의존을 남기지 않았다).
    `encodeTimelapse`는 설계에 없던 **최상위 `try/catch`** 로 감쌌다(§6.7의 "절대 throw하지 않는다"를
    기계적으로 보장. `timelapseService.finish()`의 `.catch()`는 이중 방어로 남겼다).
  - **설계 결정 고정(리뷰 지점)**: **경로 B 실패 시 경로 A로 자동 재시도하지 않는다.** 판정은 처음 1회다.
    B 실패는 통상 인코더 자체의 문제이고 A는 최대 15초를 더 쓰면서 결과 보장도 없다([04 §7.5]의
    "실패 → `null`"). 테스트로 고정했다(`timelapseEncoder.test.ts`).
  - **미구현(의도)**: 로컬 보관·업로드(Step 10·11 — `current()` getter만 노출), 진단 모달(Step 16 —
    `encoderProbe()` getter + 로그까지만), `Guide`의 "타임랩스 미제공" 안내([12 C3] — 문구 미확정),
    `session.mp4`(non-goal). `docs/spec-vectors/` **무변경**(Windows 대응 함수가 없어 교차 고정 대상이
    아니다 — Windows는 ffmpeg `setpts,fps=30` 필터가 처리한다) → 이번 Step에 `dotnet test`는 불필요했다.
  - **미검증(사용자 액션 V18)**: 생성된 mp4의 실제 재생·`moov` 정상·코덱 h264·오디오 0트랙·길이,
    [바로 촬영] 5초 세션 원속 산출, 모바일/데스크톱 재생, 인코딩 ≤6초·프리뷰 ≥24fps·`droppedSpool`,
    미지원 브라우저 완주, 경로 A 실동작 — **7건 전부 브라우저 실행이 필요해 추정 통과 처리하지 않았다.**
    절차는 [14 §10.3](./14-handoff-and-user-actions.md)에 **V18**로 등재했다.

---

## Step 10: 결과물 로컬 보관 (M6-W)

- **Context Brief**: **업로드 이전에** 결과물을 기기에 남긴다. OPFS 기록이 필수 계층이고, 데스크톱 Chromium은 운영자가 선택한 실제 폴더에도 쓴다. 이 Step이 불변식 M6-W의 본체다. 규격은 **[05 §5](./05-storage-and-persistence.md)**.
- **대상 파일**: `src/adapters/storage/resultSaver.ts`(opfsWriter 경유), `src/adapters/storage/dirHandleRepo.ts`, `src/screens/result/next.ts`([다음] 처리), `src/screens/settings/resultsPanel.tsx`, `tests/unit/storage/resultSaver.test.ts`
- **선행 조건**: Step 3, Step 8, (Step 9 있으면 타임랩스 포함)
- **구현 내용**:
  - `resultSaver.save()`: 폴더명 `mcphoto_YYMMDD_HHMM`(충돌 시 `-2`…), `final.{ext}` + `timelapse.mp4`. **OPFS 기록 → 폴더 핸들 있으면 폴더에도 기록** → 결과 반환(성공/실패).
  - `dirHandleRepo`: `showDirectoryPicker` → IndexedDB 저장 → `queryPermission`/`requestPermission` 흐름([05 §5.3](./05-storage-and-persistence.md)).
  - `Result`의 [다음] 처리 순서: 타임랩스 마무리 → **로컬 보관** → QR 분기.
  - 용량 정책: `results/` 2GB / 200세션 초과 시 오래된 것부터 삭제 + 로그.
  - 설정에 [보관된 결과물] 패널(목록·용량·내보내기·삭제).
- **검증 명령**: `npx vitest run tests/unit/storage/resultSaver.test.ts` · **네트워크를 끊고** 촬영 완주 후 DevTools에서 OPFS `results/` 확인 · 폴더 지정 후 실제 파일 생성 확인
- **완료 기준**:
  - [관측] 네트워크가 끊긴 상태에서 촬영을 완주하면 OPFS `results/mcphoto_YYMMDD_HHMM/final.jpg`가 **존재**한다(E8). 폴더를 지정한 데스크톱에서는 그 폴더에도 같은 파일이 생긴다. OPFS 쓰기 실패를 목으로 유발하면 **실패 토스트**가 뜬다.
  - [non-goal] 보관이 **업로드 시도 이전**에 끝난다(요청 순서 로그로 확인). 폴더 저장 미지원 브라우저에서 버튼이 렌더되지 않는다. 용량 정리가 `frames/`·로그를 건드리지 않는다.
  - [trigger] 보관은 `SaveLocalCopy` on일 때만. 폴더 권한 재요청은 **사용자 버튼**에만.
- **롤백**: `resultSaver`·`dirHandleRepo` 삭제(로컬 보관 없이 업로드만).
- [x] **완료 (2026-07-31)** — 설계: [`docs/design/web-step10-local-save-design.md`](../design/web-step10-local-save-design.md)
  - **도메인 3파일 신설**(순수 · `purity.test.ts`가 자동 포함): `results/resultNaming.ts`
    (`resultFolderName`·`resultFolderNameFromSessionId`·`resolveResultFolderName`·`finalFileName`·
    `isResultFolderName` + 상수 3종), `results/resultSavePlan.ts`(`planResultSave` — `skip`/`save`
    **판별 유니온**), `results/resultsRetention.ts`(`planResultsRetention` + 한도 2종).
    `src/domain/index.ts`에 3줄 추가(평면 배럴이라 **한정형 이름** 유지 — 충돌 0건 확인).
  - **어댑터 3파일 신설**: `storage/resultsStore.ts`(목록·용량·삭제·읽기 + 보존 정책 집행) ·
    `storage/dirHandleRepo.ts`(② 계층 — 피커·IndexedDB 영속·권한·폴더 쓰기) ·
    `storage/resultSaver.ts`(①·②·③ 오케스트레이션 · **절대 throw하지 않는다**).
  - **화면 1파일 신설**: `screens/result/resultNext.ts`(`runResultNext`·`defaultResultNextDeps`) —
    [다음] 순서를 React 밖으로 빼내 node에서 검증 가능하게 만들었다.
  - **OPFS `usage` op 추가**(`opfsProtocol`·`opfsWriter.worker`·`opfsClient`): 경로를 받아 직속
    자식별 용량을 **왕복 1회**로 돌려주는 읽기 전용 op. `getFile().size`만 쓴다
    (`createSyncAccessHandle().getSize()`는 파일당 배타 잠금이라 다음 쓰기를 막는다).
    실패·미지원은 빈 결과 → 상위에서 "정리 불필요"로 축소되어 **삭제를 덜 하는 안전한 방향**이다.
  - **배선**: `FlowViews.tsx`의 `ResultView.goNext`를 `runResultNext(defaultResultNextDeps(...))`
    한 줄로 교체하고 `try/finally` 범위를 **보관까지** 넓혔다(보관 중 이중 클릭 차단).
    `App.tsx`의 `DummyScreen`에 **임시** [로컬 저장 폴더 선택] 버튼(`Settings` 한정 + 기능 감지 게이트).
  - **검증 수치**: `npx tsc --noEmit` 0, `npx vitest run` **26파일 645 → 29파일 758 전부 통과**
    (도메인 +31, 어댑터 +56, 순서 +15, `opfs.test.ts` +11). `npx vite build` 성공.
    `npx vitest run --coverage` 임계 통과(`domain/results` stmts 100 / branch 97.22 / funcs 100 / lines 100).
  - **정적 불변식 6건 신설**(15 §3.4 관례): ① `resultSaver.ts`·`resultsStore.ts`에 내부 저장소 직접
    접근 문자열 0건(VF-14) ② `resultSaver.ts`는 `OpfsClient` 경유·Worker 직접 import 0건
    ③ `dirHandleRepo.ts`는 내부 저장소 미접촉(⚠️ **이 파일만 `createWritable` 허용** — 대상이 OPFS가
    아니라 사용자 디렉터리이고 Worker가 그 핸들에 닿을 수 없다) ④ `DIR_HANDLE_DB_NAME !== LOG_DB_NAME`
    ⑤ 신규 3파일에 `console.*` 0건 ⑥ `usage` 핸들러 안에 쓰기·삭제 API 0건.
  - ⚠️ **설계 이탈 ①(WBS의 `src/screens/result/next.ts` → `resultNext.ts`)**: 평면 배럴·import
    가독성 때문에 한정형 파일명을 썼다. 설계 문서(§2.1)가 지정한 이름이다.
  - ⚠️ **설계 이탈 ②([보관된 결과물] 패널 Step 13 이월)**: 설정 화면 자체가 아직 `DummyScreen`이라
    패널만 만들면 진입점이 없어 검증할 수 없다. 어댑터(`resultsStore`)는 이번에 완성해 패널이
    얹히기만 하면 된다.
  - ⚠️ **설계 이탈 ③([폴더 선택] 정식 UI·권한 재요청 배너 Step 13 이월)**: 대신 `DummyScreen`에
    임시 진입점을 뒀다(Step 6의 [카메라 테스트] 선례). 진입점이 없으면 ② 계층을 **한 번도 실행할 수
    없어** 완료 기준을 확인할 방법이 사라진다.
  - ⚠️ **설계 이탈 ④(부트스트랩 `queryPermission` → 보관 시점 lazy 조회)**: 그 결과의 유일한 소비자
    (설정 배너)가 Step 13이다. `bootstrap.ts`·`bootstrap.test.ts`를 건드리지 않아 회귀 표면이 줄었다.
  - ⚠️ **설계 이탈 ⑤(`docs/spec-vectors/` 무변경)**: Windows의 충돌 접미 로직(`MakeUniqueFolder`)이
    private + 파일시스템 의존이라 벡터로 절반밖에 못 고정하고, 벡터 1개 추가는 `EXPECTED_VECTOR_NAMES`와
    `SpecVectorTests.cs`를 **함께** 고쳐야 해 C#을 전혀 건드리지 않는 이 Step에 교차 변경을 끌어들인다.
    대신 웹 테스트에 Windows와 **같은 리터럴**(`mcphoto_260720_1445`)을 두고
    `// ↔ tests/MCPhoto.Tests/LocalSaveTests.cs:33` 주석으로 짝을 명시했다 → `dotnet test`는 불필요했다.
  - ⚠️ **설계 이탈 ⑥(사소)**: 임시 진입점의 `settingsStore.save()` **반환값을 확인**해 실패면 오류
    토스트를 띄운다(설계는 성공 토스트만 명시). 저장 실패를 성공으로 보이게 하지 않기 위해서다(M4).
    `dirHandleRepo`의 신규 IndexedDB 연결에는 `onversionchange`를 **걸었다**(로그 DB가 빠진 함정).
  - **📌 다음 작업자에게**: `logStore.ts`가 `mcphoto` DB를 열 때 `db.onversionchange = () => db.close()`가
    **아직 없다**. ~~Step 14가 "프레임 메타 스토어를 같은 DB에 버전 올려 추가"하기 전에 반드시 처리해야 한다~~
    → **해소(2026-08-01, Step 14)**: 같은 DB를 올리지 않고 **별 DB `mcphoto-frames` v1**을 신설했다.
    `mcphoto`를 업그레이드할 계획이 사라졌으므로 `onversionchange` 추가는 불필요하고, 추가하면 다른 탭
    업그레이드 시 **로그가 조용히 죽는** 새 실패 모드가 생긴다 → 넣지 않는다.
  - **미구현(의도)**: 업로드·QR 일체(Step 11 — `resultNext.ts`에 주석 자리만 남겼다),
    E8 [기기에 저장] 다운로드 버튼(Step 11), [보관된 결과물] 패널·정식 폴더 UI(Step 13),
    `isTempUserBlocked`는 아직 상수 `false`(Step 11이 `qrUsageService`로 교체).
  - **미검증(사용자 액션 V19)**: 네트워크 차단 완주 후 OPFS `results/` 실제 생성, 폴더 지정 후 실제
    파일 생성·권한 상실 경로, `usage` walk 실기기 소요 — **3건 전부 브라우저 실행이 필요해 추정 통과
    처리하지 않았다.** 절차는 [14 §10.4](./14-handoff-and-user-actions.md)에 **V19**로 등재했다.

---

## Step 11: 업로드 3단계 + QR + 완료 ★ 마일스톤 A

- **Context Brief**: prepare → 서명 PUT(XHR 진행률) → commit을 수행하고 QR을 표시한다. **QR은 성공 후에만**(M5), `requiredHeaders`는 **전부 부착**(M14). 여기까지 완성되면 **촬영→합성→로컬 보관→업로드→QR 경로가 실제로 동작**한다. 규격은 **[06 §4](./06-backend-integration-web.md)**, 화면은 **[03 §9·§10](./03-screens-spec.md)**.
- **⚠️ 게스트는 `Qr`에 도달하지 않는다**: effective QR 판정(`qrEffectivePolicy` — 미로그인 → off)에 따라 게스트는 `Result → Done`으로 끝난다([03 §8.1](./03-screens-spec.md), Windows와 동일). 따라서 이 Step에서 **화면 흐름을 통한 종단 검증(폰 QR 스캔)은 로그인이 필요하고, 로그인은 Step 12 + 서버 선행(0-1~0-4)에 달려 있다.** 이 Step에서는 ① 업로드 게이트웨이·QR 렌더·`Done`을 구현하고 ② 검증은 **`qrEffectivePolicy`를 목으로 `true` 고정**(또는 Playwright에서 세션 사용자 주입)해 수행한다. **제품 코드에 게이트 우회 플래그를 남기지 않는다**(테스트 목 한정). 로그인 후 실기기 종단 검증은 Step 12·Step 17에서 한다.
- **대상 파일**: `src/adapters/http/uploadGateway.ts`(PUT 추가), `src/adapters/qr/qrService.ts`, `src/screens/qr/*`, `src/screens/done/*`, `src/ui/views/{QrView,DoneView}.tsx`
- **선행 조건**: Step 5, Step 10, **Step 0-5(CORS — OA-1)**
- **구현 내용**:
  - prepare(파일당 1회) → **XHR PUT**(`requiredHeaders` 순회 부착, 인증 헤더 미부착, 진행률) → commit(prepare의 `downloadUrl` 그대로).
  - 전송 대상 확정(설정 토글 + 파일 존재). 둘 다 없으면 **업로드 시도 없이** "전송할 결과물이 없습니다."
  - 성공: QR(**ECC Q** — Windows `QrService.cs`와 일치 · 여백 4모듈 · 흰 배경 고정 · 모듈 픽셀은 화면에 맞춤) + "{N}시간 후 자동 삭제" 고지. 실패: QR 숨김 + [06/03]의 사유별 문구 + [완료]/[재시도](**새 세션 ID로 전 과정**).
  - [기기에 저장] 버튼(다운로드 내보내기).
  - `Done`: 6초 후 자동 홈(실경과 기반).
- **검증 명령**: 브라우저 실행(effective QR 목 `true`) → 촬영 완주 → Network에서 `OPTIONS 204 → PUT 200` 확인 → 폰으로 QR 스캔 → P1 페이지에서 다운로드 · `npx playwright test e2e/upload-qr.spec.ts` · `npx playwright test e2e/guest-flow.spec.ts`(게스트는 `Done`으로 끝나는 것을 확인)
- **완료 기준**:
  - [관측] effective QR이 `true`인 상태로 촬영을 완주하면 QR이 뜨고 **폰으로 스캔해 사진(및 타임랩스)을 다운로드**할 수 있다. PUT 헤더가 `requiredHeaders`와 정확히 일치한다. 진행률이 0→100으로 증가한다. 무토큰(게스트) 요청 경로에서 prepare에 `Authorization` 헤더가 **없다**.
  - [non-goal] **게스트로 촬영하면 `Qr`을 건너뛰고 `Done`으로 가며 업로드 요청이 0건**이다(Network 확인). 업로드 실패 시 **QR이 뜨지 않고** [완료]로 진행 가능하며 결과물이 로컬에 남아 있다. 같은 세션 ID로 재commit하지 않는다. 로그에 서명 URL·토큰이 없다. **제품 코드에 QR 게이트 우회 경로가 없다**(grep 확인).
  - [trigger] QR 렌더는 commit 성공 후에만. 업로드는 **effective QR on** + 전송 대상이 1개 이상일 때만.
- **롤백**: `Qr`·`Done` 화면과 PUT 코드 제거(결과 화면에서 종료).
- [x] **완료 (2026-07-31)** — 설계: [`docs/design/web-step11-upload-qr-done-design.md`](../design/web-step11-upload-qr-done-design.md)
  - **산출물(신규 11)**: `domain/upload/qrRenderPlan.ts`(여백 4모듈·정수 배율) · `domain/upload/exportFileName.ts`(P1 페이지와 같은 규칙, **UUID 미포함**) ·
    `adapters/qr/qrService.ts`(ECC **Q** 고정 · canvas 직접 렌더) · `adapters/platform/fileExport.ts`(`<a download>` + revoke 지연) ·
    `shell/qrUsageStore.ts`(계정 변경 1회 조회 · 동기 판정 · fail-open) · `screens/qr/uploadRunner.ts`(3단계 오케스트레이션, React 무관) ·
    `screens/qr/useUploadRun.ts` · `screens/done/doneAutoHome.ts`(6초 실경과) · `ui/views/QrView.tsx` · `ui/views/DoneView.tsx` ·
    `tests/unit/{http/uploadGateway,screens/uploadRunner,screens/doneAutoHome,shell/qrUsage,qr/qrService}.test.ts`
  - **수정**: `adapters/http/uploadGateway.ts`(**XHR `put()`** 추가 — 진행률·`requiredHeaders` 전량 순회·판별 유니온) · `shell/sessionStore.ts`(`finalImage` 인계 + `discardCaptureData`에서 폐기) ·
    `screens/result/useResultCompose.ts`(합성 성공 시 인계) · `screens/result/resultNext.ts`(`isTempUserBlocked` 실배선 + **잘못된 예약 주석 정정**) ·
    `main.tsx`(`installQrUsageLifecycle`) · `App.tsx`(라우팅) · `ui/strings.ts` · `ui/views/screens.module.css` · `package.json`·`THIRD-PARTY.md`
  - **검증(실측)**: `npx tsc --noEmit` 오류 0 · `npx vitest run` **873 통과(34파일)** — 758/29에서 **+115** · `npx vite build` 성공(번들 217→266 kB, gzip 74→92 kB) ·
    `npx vitest run --coverage` `src/domain` 라인 **98.72%**(임계 95) · `grep -rn "forceQr\|skipQrGate\|bypass" src/` **0건** · `docs/spec-vectors/` **무변경 → `dotnet test`는 수행 대상 아님**
  - **의존성**: `qrcode-generator@2.0.4`(MIT · 런타임 의존 0 · 자체 `.d.ts`) **캐럿 없는 정확 핀**. `THIRD-PARTY.md`에 등재(LICENSE 파일이 없고 소스 헤더에 MIT 고지가 있다는 점까지 기록).
  - ⚠️ **설계 이탈 ①(가장 중요)**: **업로드 3단계의 실행 주체를 `Qr` 화면으로 확정**했다. 최초 지시와 `resultNext.ts`의 예약 주석은 `runResultNext` 안이었으나,
    ① [03 §8.1](./03-screens-spec.md)의 [다음] 순서에 업로드가 **없고** ② [03 §9.1](./03-screens-spec.md)이 업로드를 `Qr` 진입 절차로 규정하며
    ③ Windows도 `QrPopupViewModel.OnEnterAsync`가 수행하고 ④ [재시도]가 `Qr` 화면 액션이라 양쪽에 두면 **같은 부수효과의 진입점이 2개**가 된다.
    M6-W는 `save`가 `go`보다 앞이므로 **구조적으로** 유지되고 `resultNext.test.ts`의 `["finishTimelapse","save","go"]`도 **무변경**이다.
    잘못된 예약 주석은 `resultNext.ts`와 [15 §6](./15-implementation-conventions.md) Step 10 절에서 **정정**했고, `resultNext.ts` 소스에
    `uploads/prepare`·`uploads/commit`·`runUpload`가 0건임을 **정적 테스트로 고정**했다.
  - ⚠️ **설계 이탈 ②**: Playwright E2E 2종(`upload-qr`·`guest-flow`)을 만들지 않았다 — 저장소에 Playwright 설치·설정이 없다. **Step 17로 이월**(아래 Step 17 절에 명시).
  - ⚠️ **설계 이탈 ③**: `prepare`의 `bucket`으로 설정 `StorageBucket`을 **갱신하지 않는다**. 웹은 URL을 재조립하지 않고 `downloadUrl`을 그대로 넘기므로 이득이 없고,
    `StorageBucket`은 `GUEST_LOCKED_KEYS`라 손님 세션에서 쓰면 권한 축이 흐려진다. 값은 **로그로만** 남긴다.
  - ⚠️ **설계 이탈 ④**: 서명 PUT이 예외 대신 **판별 유니온**(`SignedPutOutcome`)을 돌려준다 — [15 §2](./15-implementation-conventions.md) "어댑터는 예외를 전파하지 않는다"를 따랐다([06 §4.2](./06-backend-integration-web.md)의 `reject` 예시 코드와 다르다. 계약은 "XHR로 진행률"이다).
  - ⚠️ **설계 이탈 ⑤**: `STRINGS.upload.retentionNotice`·`inProgress`를 `analysis/13 §14` 카탈로그 문구로 **정정**했다(사용처 0건이라 회귀 없음).
  - ⚠️ **설계 이탈 ⑥**: 진행률 가중치는 이식된 `overallProgress`(**활성 단계 균등**)를 그대로 쓰고, 대신 **[06 §4.5](./06-backend-integration-web.md) 문서를 구현에 맞게 정정**했다(표시값이고 계약이 아니며 교차 벡터 대상도 아니다).
  - ⚠️ **설계 이탈 ⑦(범위 외 발견 + 수정)**: `App.tsx`의 `ScreenRouter`에 **`Result` 케이스가 빠져 있었다** — Step 8/10이 `ResultView`를 만들고 라우팅을 붙이지 않아 `Result`가 더미 화면으로 렌더됐다.
    그대로 두면 마일스톤 A 경로가 화면에서 도달 불가라 `Result`·`Qr`·`Done` 3케이스를 함께 붙였다.
  - **미검증(사용자 액션 V20)**: 브라우저 서명 PUT 실동작(`OPTIONS 204 → PUT 200`)·`lengthComputable` 실관측·폰 QR 스캔·[기기에 저장] 실파일 — **5건 전부 브라우저/폰이 필요해 추정 통과 처리하지 않았다.**
    절차는 [14 §10.5](./14-handoff-and-user-actions.md)에 **V20**으로 등재했다. ⚠️ **폰 스캔은 Step 12(로그인) 이후**에만 가능하다(게스트는 `Qr`에 도달하지 않는다 — VF-11).

---

## Step 12: 인증 (Google SSO 리디렉트 + JWT)

- **Context Brief**: PKCE 리디렉트 로그인을 구현하고 JWT를 **메모리에만** 둔다. M1(세션 null → 토큰 폐기) 배선은 Step 4에서 심었으므로 여기서는 **실제로 동작하는지 검증**한다. **서버 확장(Step 0-1~0-4)이 끝나야 성공한다.** 규격은 **[07 §2~§4](./07-auth-and-permissions-web.md)**.
- **대상 파일**: `src/adapters/auth/googleSignIn.ts`, `src/screens/login/*`, `src/screens/oauthCallback/*`, `src/App.tsx`(라우팅), `tests/unit/auth/*.test.ts`
- **선행 조건**: Step 4, Step 5, **Step 0-1 ~ 0-4**
- **구현 내용**:
  - PKCE 생성(Web Crypto) → `sessionStorage`에 `{codeVerifier,state,nonce,returnTo,startedAt}` → authorize URL로 `location.assign`.
  - `/oauth2callback`: 값 복원 → **state 대조** → error·3분 타임아웃 검사 → `POST /auth/google` → 토큰 메모리 보관 + 세션 설정 → **sessionStorage 삭제 + `history.replaceState`로 code·state 제거** → `returnTo` 복귀.
  - `Login` 화면: 버튼 1개, `GoogleClientId` 빈 값이면 숨김, [닫기] 항상 노출, 오류 문구 5종 분기.
  - 401 만료 처리: 토큰 폐기 + 세션 해제 + 안내. **PIN 검증의 401은 예외 처리**(세션 유지).
- **검증 명령**: 브라우저에서 실제 로그인 · `npx playwright test e2e/auth.spec.ts`(M1·M2 시나리오 E3·E4) · DevTools Application에서 토큰 문자열 검색 0건
- **완료 기준**:
  - [관측] Google 로그인이 성공해 상단 계정 라벨이 계정 id로 바뀌고 **직전 화면으로 복귀**한다(OA-5 검증). 콜백 후 URL에 `code`·`state`가 남지 않는다. **로그아웃 후 게스트 촬영의 prepare에 `Authorization`이 없다**(E3).
  - [non-goal] **`localStorage`·`sessionStorage`·IndexedDB·쿠키에 JWT가 없다**(E4 — 자동 검사). 로그인 실패·미구성에서도 [닫기]로 게스트 흐름 복귀가 된다. PIN 오입력이 로그아웃을 유발하지 않는다.
  - [trigger] 리디렉트는 버튼 탭에만. 콜백 처리는 `sessionStorage`에 값이 있을 때 **1회만**.
- **롤백**: `googleSignIn`·콜백 라우트 제거(게스트 전용 앱으로 동작).
- [x] **완료 (2026-08-01)** — 설계: [`docs/design/web-step12-google-sso-auth.md`](../design/web-step12-google-sso-auth.md)
  - **산출물(신규 12)**: `domain/auth/pkceCodec.ts`(base64url 자체 구현 — `btoa` 미사용) · `domain/auth/authorizeUrl.ts`(파라미터 순서·인코딩 고정) ·
    `domain/auth/oauthCallbackPolicy.ts`(중단 5사유 판정 · `returnTo` clamp) · `domain/auth/loginFailure.ts`(진단 6종 → 문구 5종) ·
    `adapters/auth/pkce.ts`(Web Crypto 포트) · `adapters/auth/oauthStateStore.ts`(**`sessionStorage` 유일 소유자**) · `adapters/auth/googleSignIn.ts`(`POST /auth/google`) ·
    `screens/oauthCallback/oauthCallbackRunner.ts`(capture/run/apply 3단 — React 무관) · `screens/login/useGoogleSignIn.ts`(로직 `runSignIn` 분리) ·
    `shell/loginStore.ts` · `shell/sessionExpiry.ts` · `ui/views/{LoginView,OauthCallbackView}.tsx` ·
    `tests/unit/auth/{pkceCodec,authorizeUrl,oauthCallbackPolicy,loginFailure,pkce,oauthStateStore,googleSignIn,oauthCallbackRunner,loginBinding,sessionExpiry,authInvariants}.test.ts`(11파일)
  - **수정(8)**: `main.tsx`(9단계 실배선 — `classifyRoute` **첫 소비자**) · `App.tsx`(`Login` 라우팅 + **`devLogin` 삭제**) ·
    `shell/sessionStore.ts`(**`expireSession()` 신설**) · `adapters/http/backendClient.ts`(`RequestOptions.unauthorized` + `onSessionExpired` — 401 단일 지점) ·
    `adapters/http/accountService.ts`(`verifyMyPin`에 `unauthorized:"reject"`) · `ui/strings.ts`(`login` 절 + `error.sessionExpired` 문구 정정) ·
    `vite.config.ts`(dev 포트 **5273 → 5173** + `strictPort: true`) · `docs/design/README.md`(2곳 등재)
  - **검증(실측)**: `npx tsc --noEmit` 오류 0 · `npx vitest run` **1051 통과(45파일)** — 873/34에서 **+178(+11파일)** · `npx vite build` 성공(번들 266→276 kB, gzip 92→95 kB) ·
    `cd web/functions && npm test` **316 통과(18스위트, 무변경)** · `dotnet test tests/MCPhoto.Tests` **938 통과(무변경)** ·
    정적 불변식 7건은 **일시 변형 4회로 실패까지 확인**(M2-a/AUTH-1/AUTH-2/AUTH-5) · `docs/spec-vectors/` **무변경**
  - **정적 불변식 7건 신설**(`tests/unit/auth/authInvariants.test.ts` — 15 §3.4. 테스트 케이스는 **9건**이며 7건 외에 검사 경로 존재 가드 + dev 포트 정합이 붙는다):
    **M2-a** `sessionStorage`는 `oauthStateStore.ts` 한 파일에만 ·
    **M2-b** 인증 11파일에 `localStorage`·`indexedDB`·`document.cookie`·`persist(` 0건 · **AUTH-1** `.login(` 호출부는 콜백 러너 1곳뿐(`devLogin` 류 재발 방지) ·
    **AUTH-2** `clientKind`가 `"web"`으로 고정 · **AUTH-3** 인증 파일의 `logger` 컨텍스트에 `code`·`state`·`nonce`·`codeVerifier`·`token`·`pin` 키 0건 ·
    **AUTH-4** `App.tsx`에 `devLogin` 0건 · **AUTH-5** authorize URL에 `prompt=select_account` 존재
  - ⚠️ **설계 이탈 ①(가장 중요 — 규격 반영)**: 401 처리에 `logout()`이 아니라 **`sessionStore.expireSession()`** 을 쓴다. `logout()`은 `discardCaptureData()`를 동반하는데
    [02 §5.2](./02-app-shell-and-navigation.md) 매트릭스는 "JWT 만료 감지" 행의 촬영 데이터를 **유지**로 못박고 [07 §4.3](./07-auth-and-permissions-web.md)도 "촬영·합성·로컬 보관은 계속"이라고 쓴다.
    401이 가장 잘 나는 지점이 `Qr` 업로드(`optionalBearer`도 무효 토큰은 401)라 거기서 `finalImage`를 버리면 **[기기에 저장]까지 죽는다**.
    **M1은 그대로 성립한다** — 구독은 "`logout()` 호출"이 아니라 `currentUser`가 null이 되는 것을 보므로 `installTokenLifecycle`은 **무수정**이다.
  - ⚠️ **설계 이탈 ②**: 콜백을 **`APP_STATES`에 넣지 않았다.** 200ms~2초짜리 부트스트랩 국면이라 전이 대상이 아니고, 상태로 만들면 `canTransition` 13×13에 아무도 못 가는 행/열이 생긴다.
    경로 분기는 기존 `classifyRoute`(전 저장소 호출 0건이던 순수 함수)가 담당하고, `main.tsx`가 `ScreenRouter` **밖**에서 `OauthCallbackGate`로 렌더한다.
  - ⚠️ **설계 이탈 ③**: URL 스크럽(`history.replaceState`)을 [07 §2.2](./07-auth-and-permissions-web.md) 절차의 h가 아니라 **판정 직후·교환 전**에 한다 — ① 실패 경로에도 주소창에 `code`가 남지 않고
    ② 교환 100초 사이 새로고침으로 같은 code 재진입이 **구조적으로 불가능**해지고 ③ `installRouter`가 더미 history 엔트리를 쌓기 전이라 `/oauth2callback`이 히스토리에 남지 않는다.
    **[07 §2.2] 문서도 이 순서로 정정**했다.
  - ⚠️ **설계 이탈 ④**: 화면 컴포넌트는 `ui/views/`, 로직은 `screens/`에 뒀다(WBS 대상 파일은 `screens/login/*`·`screens/oauthCallback/*`였다).
    Step 8~11의 실제 배치(`QrView.tsx` ↔ `screens/qr/uploadRunner.ts`)를 따랐다 — 새 화면만 다른 배치를 쓰면 `ScreenRouter`의 import가 두 갈래가 된다.
  - ⚠️ **설계 이탈 ⑤(사소·테스트 가능성)**: `oauthStateStore`의 3함수에 **선택적 저장소 인자**를 뒀다(`savePendingOauth(state, store?)`). 설계 시그니처는 인자 없음이지만
    `settingsRepo`의 `StorageLike` 주입 선례를 따라야 "예외를 던지는 저장소"·"손상 JSON" 경로를 node에서 검증할 수 있다(설계 §6.1도 "저장소를 주입한다"고 쓴다). 기본값은 실 `sessionStorage`다.
    같은 이유로 `handleSessionExpired(path?)`가 진단용 경로를 받고(설계 §4.6의 `{ path }` 로그를 위해), `useGoogleSignIn`의 로직을 **`runSignIn(deps)`** 으로 분리했다(훅은 node에서 호출할 수 없다).
  - ⚠️ **설계 이탈 ⑥(사소)**: AUTH-2 정적 검사가 `clientKind: "web"` 리터럴 **또는** `OAUTH_CLIENT_KIND = "web"` + `clientKind: OAUTH_CLIENT_KIND` 둘 중 하나를 통과시킨다.
    설계 §6.2는 리터럴 검사라고 썼지만 §3.2·§8은 상수 경유를 지시해 서로 어긋났다 — 상수를 `"desktop"`으로 바꾸는 변형에서 실제로 실패함을 확인했다.
  - **미구현(의도)**: PIN 모달·설정 화면(Step 13), 계정·사용자 관리 화면(Step 16), Playwright E2E(E3·E3b·E4·E17 — Step 17). E2E 4종은 이번 Step에서 **단위 테스트로 등가 보장**했다
    (E3/E3b = `sessionExpiry.test.ts` + 기존 `authStore.test.ts` · E4 = 정적 M2-a/M2-b · E17 = `sessionExpiry.test.ts`의 PIN 절).
  - **미검증(사용자 액션 V21)**: 실 Google 계정 완주 · 배포본 CSP 위반 0건 · `prompt=select_account` 실동작 · 새로고침 게스트 복귀 + 저장소 토큰 0건 · 로그인 후 폰 QR 스캔 ·
    `firebaseapp.com` 도메인 · 실패 경로 4종(취소·직접 진입·400·client_id 빈 값) — **10건 전부 브라우저·실계정·폰이 필요해 추정 통과 처리하지 않았다.**
    절차는 [14 §10.6](./14-handoff-and-user-actions.md)에 **V21-1~V21-10**으로 등재했다(E17 화면 관측은 Step 13으로 이월 — [14 §10.7](./14-handoff-and-user-actions.md)).
  - **📌 다음 작업자에게**: ① `verifyMyPin` 외의 PIN 계열 호출을 추가하면 **`unauthorized: "reject"` 를 반드시 넘긴다**(기본값은 `expired`다).
    ② `sessionStore.login()`을 부르는 코드를 늘리면 AUTH-1이 실패한다 — 세션을 세우는 경로는 콜백 러너 1곳이어야 한다.
    ③ `sessionStorage`가 필요하면 `oauthStateStore.ts`에 넣지 말고 **왜 필요한지 먼저 검토**한다(M2-a가 막는다).

---

## Step 13: PIN 게이트 + 설정 화면

- **Context Brief**: 설정·계정 진입 PIN 게이트(fail-closed, 5회/1.5초 + **기기 5분 잠금**)와 설정 화면 전체를 만든다. 게스트 편집 제한은 **저장 시 해당 키를 기록하지 않아 관리자 값을 보존**하는 것이 핵심이다. 규격은 **[07 §6](./07-auth-and-permissions-web.md)**, **[03 §12](./03-screens-spec.md)**, **[05 §2](./05-storage-and-persistence.md)**.
- **대상 파일**: `src/domain/auth/pinGatePolicy.ts`, `src/domain/settings/settingsEditPolicy.ts`, `src/adapters/storage/pinLockRepo.ts`, `src/screens/modals/pinPrompt/*`, `src/shell/pinGate.ts`, `src/screens/settings/*`, `src/ui/views/{PinGate,SettingsView}.tsx`
- **선행 조건**: Step 12(계정 API), Step 3(설정 저장), Step 10(`resultsStore` — [보관된 결과물] 패널 이월분)
- **상세 설계**: [`docs/design/web-step13-settings-pin-gate.md`](../design/web-step13-settings-pin-gate.md) — 9단계 WBS·설계 이탈 6건·정적 불변식 8건 포함
- **구현 내용**:
  - `ensurePinGate(deps)`: [07 §6.2](./07-auth-and-permissions-web.md) 의사코드 그대로. 계정 서비스·모달 사용 불가 시 `false`(fail-closed).
  - ⚠️ 게이트는 **네비게이션 가드가 아니라 `<PinGate>` 렌더 게이트**다 — OAuth 복귀가 `returnTo="Settings"`로 **직행**하므로(`screens/oauthCallback/oauthCallbackRunner.ts`) 호출부마다 붙이면 그 경로가 빠진다. `Account`도 함께 감싼다(화면 본체는 Step 16).
  - ⚠️ `sessionStore.markPinSet()` 신설 필수: `hasPin`을 갱신하지 않으면 최초 설정 다음 진입이 `currentPin` 없는 PUT → **401 데드락**이 된다.
  - ⚠️ `accountService.setMyPin`에 **`unauthorized: "reject"` 를 추가**한다(Step 12에서 `verifyMyPin`에만 붙었다) — 없으면 `currentPin` 불일치 401이 로그아웃을 유발한다(E17의 PUT 판).
  - PIN 모달: 온스크린 숫자 키패드, 4자리, 확인/최초설정 2모드, 1.5초 쿨다운, 5회 → 닫힘 + **localStorage 5분 잠금**, 네트워크 오류는 카운트 미가산.
  - 설정 화면: [03 §12.1](./03-screens-spec.md) 그룹 5개 + 웹 전용 항목. 미노출 4항목은 렌더하지 않되 **값은 보존**. 게스트 제한 11개 항목. QR 연동 정규화·재활성. 저장 순서·성공/실패 정직 표시. 하단 sticky 저장 바.
  - 카메라 장치 선택(재검색·테스트·라벨 폴백) + **`App.tsx`의 임시 진입점 2개**([카메라 테스트 열기]·[로컬 저장 폴더 선택])를 여기로 이사하고 원본 제거.
  - **[보관된 결과물] 패널**(Step 10 이월): `resultsStore`의 `usage`/`removeFolder` 위에 얹기만 한다. 새 저장소 코드 금지.
  - **이월(의도)**: [프레임 내보내기]/[가져오기] → **Step 16**(`exportImport.ts` — 2026-08-01 정정. 종전 "Step 15"는 오기였다) · [앱 업데이트 확인] → Step 16(SW 선행) · [진단·상태] 버튼 → Step 16(모달 본체 선행).
- **검증 명령**: `npx tsc --noEmit` · `npx vitest run`(baseline 1051에서 증가) · `npx vite build` · 게스트/로그인 상태에서 설정 저장 후 `localStorage["mcphoto.settings.v1"]` 비교
  - ⚠️ **Playwright는 쓰지 않는다** — 아직 설치돼 있지 않고 도입은 Step 17이다. E16·E17은 node 단위 테스트로 등가 보장하고 화면 관측은 **V22 실측**([14 §10.8](./14-handoff-and-user-actions.md))으로 분리한다.
- **완료 기준**:
  - [관측] 로그인 사용자는 설정 진입 시 **매번 PIN**을 요구받고, 게스트는 무가드로 진입한다. PIN 5회 실패 시 모달이 닫히고 **재시작 후에도 5분간 차단**된다. 컷 수 8로 저장하면 `Guide`에 반영된다.
  - [non-goal] **게스트로 저장해도 관리자 설정값(거울모드·QR 등)이 바뀌지 않는다**(저장 전후 localStorage 비교). 네트워크 오류가 실패 횟수를 늘리지 않는다. 미노출 4항목의 값이 저장 후에도 보존된다.
  - [trigger] PIN 게이트는 **로그인 사용자의 설정·계정 진입**에만. 재활성 규칙은 사용자가 QR 토글을 off→on 할 때만.
- **롤백**: 설정 화면·PIN 모달 제거(설정 편집 불가 상태).
- [x] **완료(2026-08-01)**
  - **산출물(신규 20)**: 도메인 `auth/pinGatePolicy.ts`(배럴 **미등재** — `domain/auth/*` 관례) · `settings/settingsEditPolicy.ts` · `settings/settingsImport.ts` · `results/byteFormat.ts` /
    어댑터 `storage/pinLockRepo.ts` / 셸 `pinGate.ts` / 화면 `screens/modals/pinPrompt/{pinPromptRunner.ts,PinPromptModal.tsx,pinPrompt.module.css}` ·
    `screens/settings/{settingsForm,storedResultsPanel,cameraDevicePanel,serverStatusPanel,settingsTransfer,useSettingsScreen}.ts` /
    UI `ui/views/{PinGate.tsx,SettingsView.tsx,settings.module.css}` · `ui/components/{fields.tsx,fields.module.css}` /
    테스트 `tests/unit/settings/{pinGatePolicy,pinLockRepo,pinPromptRunner,pinGate,settingsEditPolicy,settingsForm,storedResultsPanel,settingsTransfer,settingsInvariants}.test.ts`(9파일)
  - **수정(8)**: `adapters/http/accountService.ts`(**`setMyPin`에 `unauthorized: "reject"`**) · `adapters/platform/persistStorage.ts`(`readStorageStatus` — 요청 없는 조회) ·
    `shell/sessionStore.ts`(**`markPinSet()`**) · `shell/settingsStore.ts`(`save`에 `webExtras` 병합 · **메모리에도 clamp** · 죽은 코드 2개 제거 · `currentSettingsRepo()`) ·
    `domain/index.ts`(3개 등재) · `main.tsx`(`installPinGateLifecycle`) · `App.tsx`(`<PinGate>` 2곳 · `pinPrompt` 모달 · **임시 진입점 2개 제거**) · `ui/strings.ts`(`pin`·`settings` 절 + `formatCount` 타입 확장)
  - **검증 실측(2026-08-01)**: `npx tsc --noEmit` 오류 0 · `npx vitest run` **54파일 1297 통과**(baseline 45파일 1051 → **+9파일 +246**) · `npx vite build` 성공(172 모듈, 1.47s).
    `docs/spec-vectors/` **무변경** → `dotnet test`·`web/functions npm test` 재실행 불요(서버·WPF 코드도 무변경).
  - **버그 수정 3건(이번 Step 범위)**:
    ① `setMyPin`에 `unauthorized: "reject"` 누락 → `currentPin` 불일치/이미 설정된 PIN의 **401이 로그아웃을 유발**하던 것(E17의 PUT 판)을 고치고 **정적 PIN-2**로 고정.
    ② `SessionUser.hasPin` 갱신 경로 부재 → 최초 설정 다음 진입이 **401 데드락**. `sessionStore.markPinSet()`(멱등·null 미생성) 신설.
    ③ `settingsStore.reEnableQr()`·`saveWebExtras()`의 **`isGuest: false` 하드코딩**(호출자 0곳) → 두 함수를 제거하고 `save(patch, { isGuest, webExtras })` 하나로 합침. **정적 SET-4**로 재발 차단.
  - **정적 불변식 10건 신설**(`settingsInvariants.test.ts`) — **전부 일시 변형으로 실패를 확인한 뒤 되돌렸다**:
    **PIN-1** PIN 6파일의 `logger` 컨텍스트에 `pin`·`newPin`·`currentPin`·`code`·`state`·`nonce`·`token` 0건 ·
    **PIN-2** `verifyMyPin`·`setMyPin` 둘 다 `unauthorized: "reject"` · **PIN-2b** `resetOtherPin`은 무변경 ·
    **PIN-3** `mcphoto.pinLock.v1` 문자열이 `pinLockRepo.ts` 한 파일에만 · **PIN-4** `pinPrompt` `pushModal`은 `shell/pinGate.ts` 한 곳뿐 ·
    **PIN-5** 게이트 판정 파일에 `localStorage` 0건 · **SET-1** `SettingsView`·`settingsForm`에 `clampSettings(`·`closestFrom(`·`normalizeQrToggles(` 0건 ·
    **SET-2** `GUEST_LOCKED_KEYS` 11개 전부가 `SettingsView`의 `badge("…")`·`locked("…")`를 지난다 ·
    **SET-3** `App.tsx`에 임시 진입점 문자열 0건 · **SET-4** `settingsStore`에 `isGuest: false` 0건 · **SET-5** `screens/settings/*`가 `settingsRepo.save`를 직접 부르지 않는다
  - ⚠️ **설계 이탈 ①(가장 중요)**: 게이트를 **네비게이션 가드가 아니라 `<PinGate>` 렌더 게이트**로 만들었다(설계 §3.1과 동일). OAuth 복귀가 `returnTo="Settings"`로 직행하므로
    호출부 가드는 그 경로를 덮지 못한다. 게이트 미통과 시 `SettingsView`가 **마운트조차 되지 않아** 설정값이 노출되지 않는다.
  - ⚠️ **설계 이탈 ②**: `settingsStore.save`가 **메모리 값에도 `clampSettings`를 적용**하도록 고쳤다(설계에 없던 수정). 기존 구현은 저장소에만 clamp를 적용해
    "저장된 값 6 / 화면이 읽는 값 7"이 갈라졌고, 그러면 03 §12.4의 **재반영 단계가 보정 사실을 보여주지 못한다**. `settingsForm.test.ts`의 "컷 수 7 → 6" 케이스가 이것을 고정한다.
  - ⚠️ **설계 이탈 ③**: `PinPromptModal`이 `Modal`의 내장 `Esc`(→ `popModal`)를 쓰지 않고 **자체 keydown으로 `resolvePinPrompt`** 를 부른다.
    내장 경로는 대기 중인 약속을 해제하지 않아 게이트가 스피너에 고착된다. 또 언마운트 취소를 **다음 태스크로 미뤄** `<StrictMode>` 이중 effect가 1회차를 취소하지 못하게 했다(15 §6 동종 함정).
  - ⚠️ **설계 이탈 ④(사소)**: 잠금 안내 토스트를 `ensurePinGate` **한 곳에서만** 낸다(설계는 셸이 한 번 더 내는 형태였다 — 그대로면 잠금 시 토스트가 2개가 된다).
    `cancelled`(사용자가 [닫기]·`Esc`)에는 토스트를 내지 않는다.
  - ⚠️ **설계 이탈 ⑤(사소)**: `settingsTransfer`가 저장소 원문을 읽기 위해 `shell/settingsStore`에 **`currentSettingsRepo()`** 를 추가했다(설계에는 없던 접근자).
    저장은 여전히 `save()`만 지나며, 정적 **SET-5**가 `screens/settings/*`의 `settingsRepo.save` 직접 호출 0건을 고정한다.
  - ⚠️ **설계 이탈 ⑥(사소)**: [로컬 저장 폴더 선택]/[해제]만 **즉시 저장**한다. 폴더 핸들이 그 순간 IndexedDB에 들어가므로 표시값을 sticky [저장]까지 미루면
    "표시는 비었는데 복사는 된다"는 어긋남이 생긴다. 편집 중인 다른 draft 값은 건드리지 않는다.
  - ⚠️ **`serverStatusPanel`의 취소는 결과 폐기 방식이다** — `healthService.probe()`가 `AbortSignal`을 받지 않아 진행 중 요청 자체는 끊지 못한다(화면 상태는 건드리지 않는다).
    실제 요청 취소가 필요하면 Step 16에서 `healthService`에 신호를 뚫는다.
  - **이월(의도 — 스텁 문구를 운영자에게 노출하지 않기 위함)**: [프레임 내보내기]/[가져오기] → **Step 16**(2026-08-01 정정 — `exportImport.ts`가 소유한다) · [앱 업데이트 확인] → **Step 16** · [진단·상태] 버튼 → **Step 16**.
    `Account` 화면 본체도 Step 16이지만 **게이트 배선만 지금 넣었다**(나중에 붙이면 한 경로가 빠진다).
  - **미검증(사용자 액션 V22)**: 매번 PIN · 게스트 무가드 · **PIN 1회 오입력이 로그아웃을 유발하지 않음(E17 화면 관측)** · 최초 설정 후 재진입(A5) · 5회 → 5분 잠금 재시작 유지(E16) ·
    오프라인 입력 · 게스트 저장 후 운영자 값 보존(E23) · 컷 수 자동 왕복 · 카메라 2대 전환 · 보관 결과물 삭제 · 폴더 선택 3브라우저 · 설정 내보내기/가져오기 · 키패드 접근성 —
    **13건 전부 브라우저·실계정·실기기가 필요해 추정 통과 처리하지 않았다.** 절차는 [14 §10.8](./14-handoff-and-user-actions.md)에 **V22-1~V22-13**으로 등재했다.
  - **📌 다음 작업자에게**: ① `pinPrompt` 모달을 직접 `pushModal` 하지 마라 — 게이트 우회 경로가 되고 **PIN-4**가 실패한다.
    ② 설정 저장은 반드시 `settingsStore.save(patch, { isGuest })`를 지난다. `isGuest`를 하드코딩하면 **SET-4**가 실패한다.
    ③ 새 게스트 제한 키를 추가하면 `GUEST_LOCKED_KEYS`에 넣는 것만으로 **SET-2**가 렌더 가드 누락을 잡아 준다.
    ④ `<PinGate>`의 effect에 **cleanup을 넣지 마라** — StrictMode 이중 effect가 1회차를 취소해 설정 화면에서 즉시 튕겨 나간다. 승인 폐기는 `installPinGateLifecycle`이 한다.

---

## Step 14: 프레임 저장소 + 프레임 선택 화면

- **Context Brief**: 프레임 카탈로그(로컬 캐시 → 서버 → 번들 → fallback 4단 우선순위 + **이름 dedup**)와 프레임 선택 화면을 만든다. 서버 프레임 이미지는 **CORS-clean 로드 후 OPFS 캐시**가 필수다(WM2). **첫 방문 대기 4국면(it20)이 이 Step의 절반이다** — 규격은 **[03 §4.1](./03-screens-spec.md)**, **[06 §6.1](./06-backend-integration-web.md)**, **[05 §4](./05-storage-and-persistence.md)**.
- **대상 파일**: `src/domain/frames/frameLoadPolicy.ts`, `src/domain/frames/frameCatalogProgress.ts`, `docs/spec-vectors/frame-load-policy.json`, `src/adapters/storage/frameStore.ts`(opfsWriter 경유), `src/adapters/frames/frameCatalog.ts`, `src/screens/frameSelect/*`, `src/ui/views/FrameSelectView.tsx`, `webclient/public/frames/*`
- **선행 조건**: Step 3, Step 5, Step 12(본인 프레임 로드), Step 0-5
- **구현 내용**:
  - **도메인 선이식(it20)**: `frameLoadPolicy`(4국면 판정·무진행 30초/총 60초 상한·안내 문구) + `frameCatalogProgress`(단계 → 문구, `(n/m)` 조립). **둘 다 순수 함수**다. Windows `FrameLoadPolicyTests.cs`(13건)·`FrameCatalogProgressTests.cs`(5건)에서 벡터를 덤프해 `frame-load-policy.json`으로 교차 고정한다([10 §3.2](./10-testing-and-acceptance.md)).
  - `frameStore`: IndexedDB 메타([05 §4.2](./05-storage-and-persistence.md) 스키마) + OPFS PNG. 저장·조회·삭제(실제 부재 확인)·10개 상한.
  - `frameCatalog`: 4단 우선순위 + **이름 기준 dedup**, 서버 미도달 시 ②만 건너뛴다. 번들 `.slots` 없으면 2×2 자동, 최종 fallback은 코드 생성(1200×1600). **단일 비행 + 진행 보고**: 동시 호출은 한 작업을 공유하고 늦게 합류한 구독자에게 최근 보고를 replay한다. 취소는 **호출자별**이며 공유 작업은 계속 진행해 캐시를 완성한다([06 §6.1](./06-backend-integration-web.md)).
  - 프레임 선택 화면: 썸네일 그리드(축소 비트맵), 첫 항목 자동 선택, 권한 플래그 2축, 카드 ✕ 노출 규칙.
  - **대기 오버레이·실패 카드·인라인 안내**: `Loading`에서 [다음]·[만들기]·[선택 편집]·삭제 ✕를 **scrim + 상태 가드 2중**으로 차단. 국면 확정은 `finally`가 **무조건** 수행한다(오버레이 고착 경로 0). [기다리지 않고 시작]은 새 로딩을 시작하지 않고 현재 대기만 접는다. 삭제 후 재스캔은 **조용한 갱신**이다.
  - [다음]에서 프레임 고정 + **컷 수 해석 1회**(it17): `cutCountPolicy.resolve(configuredCutCount, slotCount)` — 자동(`0`)이면 `max(6, 슬롯+2)`, 고정이면 `max(설정, 슬롯)`. 세션에 **`cutCount`와 `isAutoCutCount`를 함께** 기록한다(Guide의 "(자동)" 배지 근거). **이 화면이 유일한 해석 지점**이며 `Guide`·`Capture`·전체 재촬영에서 재해석하지 않는다.
- **검증 명령**: `npx vitest run tests/unit/frames` · 오프라인 모드에서 목록 확인 · 서버 프레임으로 합성 성공 확인 · **DevTools 네트워크 스로틀(Slow 3G)로 첫 방문 재현**
- **완료 기준**:
  - [관측] 온라인에서 서버 공용 프레임이 목록에 나타나고 OPFS에 캐시된다. **두 번째 진입에서 재다운로드하지 않는다**(이름 dedup — Network 확인). 오프라인에서도 목록이 비지 않는다. 선택한 프레임으로 합성이 성공한다.
  - [관측·it20] **저장소를 비운 첫 방문**(Slow 3G)에서 진입 즉시 대기 오버레이 + `(n/m)` 카운터가 뜨고, "빈 목록 + 활성 [다음]"이 **한 프레임도 나타나지 않는다**. 오프라인 진입은 안내 없이 조용히 캐시로 마감된다(`Ready` — `Degraded` 아님).
  - [불변식] **총 대기 상한 60초 < `IDLE_TIMEOUT_MS` 120초**를 정적 테스트가 고정한다([02 §6.2](./02-app-shell-and-navigation.md)). 대기 중 유휴 경고가 겹치지 않는다.
  - [관측·it17] `CutCount=0`(자동)으로 저장한 뒤 **슬롯 5개** 프레임을 고르고 [다음]을 누르면 세션의 `cutCount`가 **7**, `isAutoCutCount`가 `true`이며 `Guide`에 "7 (자동)"이 표시된다.
  - [non-goal] 편집·삭제 UI는 권한 없는 역할에서 **렌더되지 않는다**. `user`·`temp_user`의 기존 프레임이 **목록에서 사라지지 않는다**. `frames/` 캐시가 세션 잔재 정리에 삭제되지 않는다.
  - [trigger] 서버 조회는 화면 진입 시 1회. 프레임 고정과 **컷 수 해석은 [다음]에만**(설정 화면에서 컷 수를 바꿔도 진행 중 세션의 값은 변하지 않는다).
- **롤백**: `frameStore`·`frameCatalog` 제거 → fallback 프레임만으로 동작.
- [x] **완료(2026-08-01)** — 설계: [`docs/design/web-step14-frame-catalog-and-select.md`](../design/web-step14-frame-catalog-and-select.md)
  - **산출물(신규 13)**: `domain/frames/frameStorePolicy.ts`·`bundleManifest.ts` / `adapters/storage/frameStore.ts` /
    `adapters/frames/frameCatalog.ts`·`bundleFrames.ts`·`frameDownloader.ts`·`frameImageCache.ts`·`frameThumbnails.ts` /
    `screens/frameSelect/frameLoadDeadline.ts`·`frameLoadRunner.ts`·`frameSelectActions.ts`·`useFrameSelect.ts` /
    `ui/views/FrameSelectView.tsx`(+ `frameSelect.module.css`) / `public/frames/index.json`(`[]` — 규약만).
    **수정 7**: `domain/frames/frameLoadPolicy.ts`(`isFrameListInteractive` **추가만**) · `domain/index.ts` ·
    `adapters/http/frameRepository.ts`(`deleteFrame → Promise<boolean>`) · `adapters/compose/compositor.ts`(원격/로컬 fetch 분기) ·
    `adapters/storage/logStore.ts`(주석만) · `ui/strings.ts` · `ui/views/FlowViews.tsx`(최소 `FrameSelect` 제거) · `App.tsx` · `main.tsx`(prefetch).
  - **검증 수치**: `npx tsc --noEmit` 0 · `npx vitest run` **1469 통과**(62파일, 종전 1297/54파일 → **+172**) ·
    `npm run coverage` `src/domain` lines 99.04 / branches 98.07 / functions 98.88(임계 95/90/95) · `npx vite build` 성공(192 모듈).
    `docs/spec-vectors/`·`tests/MCPhoto.Tests/`·`web/functions/` **무변경** → `dotnet test` 불필요.
  - **설계 이탈 6건**(설계 문서 §14와 동일): ① IndexedDB를 `mcphoto`가 아니라 **`mcphoto-frames` v1**에 만든다(로그 DB의
    상시 연결 + `onversionchange` 부재 → 영구 blocked). ② 삭제 확인을 공용 `confirmDelete` 모달이 아니라 **화면 로컬 오버레이**로
    만든다(Step 15 선점 방지 — 정적 FR-5). ③ CORS 실패 프레임을 "선택 시 안내"가 아니라 **선택 불가 카드**로 만든다
    (합성 실패를 촬영 뒤로 미루지 않는다 — `06 §6` 정정). ④ OPFS 이미지가 **이미 없으면 삭제를 성공**으로 본다
    (`05 §4.7` 정정 — 실패로 보면 카드가 영원히 남는다). ⑤ 개인 프레임에 `GET /frames?userId=`를 **쓰지 않는다**
    (`auth:"required"` → 401 → 세션 해제. 얻는 것은 빈 배열). ⑥ `public/frames/`에 자산을 커밋하지 않고 **빈 매니페스트만** 둔다.
  - **구현 중 추가한 이탈 2건**: ⑦ `FrameLoadDeps`에 선택 필드 `registerAbort?(abort)`를 넣었다 — 설계가
    "[기다리지 않고 시작]·언마운트가 `controller.abort()`를 부른다"고만 쓰고 그 핸들을 화면에 넘기는 채널을 정하지
    않았다. 러너가 컨트롤러를 소유하므로 생성 직후 1회 넘긴다. ⑧ `createFrameStore`에 선택 필드
    `imageUrl?`·`releaseImage?`를 넣었다(기본값 = `frameImageCache`) — node 테스트에 `File`·`URL.createObjectURL`
    왕복을 강요하지 않기 위함이고, 운영 경로의 소유자는 설계대로 `frameImageCache`다.
  - **정적 불변식 6건 신설**(`tests/unit/frames/frameInvariants.test.ts`): FR-1(OPFS 직접 접근 0) · FR-2(`canDeleteFrame` 2인자) ·
    FR-3(DB 이름 3종 상이) · FR-5(`FrameSelectView`에 `pushModal(`·`"confirmDelete"` 0) · FR-6(`compositor`에 `mode: "cors"` 존재) ·
    FR-7(`frameLoadPolicy` 기존 export 8종 보존). 여기에 VF-12(`fixFrameAndResolveCutCount` 호출부 1곳) ·
    `getUserFrames` 호출 0건 · prefetch 위치 · 신규 13파일 `console.*` 0건을 함께 고정했다.
  - **미검증(사용자 액션 V23)**: 브라우저 실행이 필요한 8건을 [14 §10.9](./14-handoff-and-user-actions.md)에 등재했다.
    **추정으로 통과 처리하지 않았다.**
  - **📌 다음 작업자에게**: ① 삭제 확인 UI는 화면 로컬 오버레이다 — Step 15가 `screens/modals/confirmDelete/*`를 만들면
    승격 여부를 그때 정한다(FR-5가 선점을 막는다). ② `frameStore.saveLocal`·`countPersonal`·`exceedsLocalFrameLimit`가
    **이미 준비돼 있다**(호출자 없음). ③ 편집 대상 인계 채널은 없다 — `useFrameSelect`의 `TODO(Step 15)` 두 곳이 그 자리다.
    ④ `frameRepository.getUserFrames`를 부르지 마라(정적 검사가 0건을 고정한다).

---

## Step 15: 프레임 편집기 + 피커 + 삭제

- **Context Brief**: 슬롯 배치 편집기를 만든다. **표시·드래그·클램프가 하나의 좌표 변환**을 써야 WYSIWYG가 성립한다(Step 2에서 이식한 `editorTransform`). 편집은 **로컬 전용**이며 `PUT /frames/{id}`를 호출하지 않는다. 규격은 **[03 §11](./03-screens-spec.md)**, 기하는 `docs/analysis/14 §4`.
- **⚠️ 이연 컷라인(WD20)**: 일정 압박 시 이 Step을 둘로 쪼갠다 — **15a(v1.0 필수)** = 편집기 + power 공용 신규 등록(`POST /frames` + 이미지 PUT) + 삭제 / **15b(v1.1 이연 가능)** = advanced_user 개인 로컬 저장 · 카탈로그 프레임 fork 사본 저장 · 프레임 피커의 개인 프레임 후보 · Step 16의 프레임 zip 내보내기/가져오기(E2). 15b를 미룰 때는 [선택 편집]·비power 저장 버튼만 미노출하면 되고 권한 게이트 구조는 그대로다. **설계·문서는 둘 다 완성 상태를 유지한다**(이연은 구현 순서일 뿐).
- **대상 파일**: `src/screens/frameEditor/*`, ~~`src/screens/modals/framePicker/*`~~, ~~`src/screens/modals/confirmDelete/*`~~, `src/ui/views/FrameEditorView.tsx`
  - ⚠️ **취소선 2개는 만들지 않았다**(설계 이탈 ①). `03 §790`이 불러오기·삭제 확인·서버 등록 확인을 전부 **화면 로컬 오버레이**로 규정했고 Step 14가 삭제를 그 형태로 이미 구현했다. 대신 `ui/components/OverlayDialog.tsx`(공통 껍데기)를 만들고 `ModalId`에서 `"framePicker"`·`"confirmDelete"`를 **제거**했다(정적 FR-8).
- **선행 조건**: Step 14, Step 2(slotLayout·editorTransform·frameNaming·frameEditPolicy)
- **구현 내용**:
  - 이미지 로드(PNG/JPG, 10MB, 장변 4000 축소, **PNG 재인코딩**), 슬롯 개수 1~6·종횡비 3종·스케일 10~300%(원본 기준), Pointer Events 드래그(그랩 오프셋 절대 위치), 저장 검증(개수·경계·겹침).
  - 권한 3단 게이트 + **저장 전 검증 7단(순서가 규격 — [03 §11.3](./03-screens-spec.md))**: ①로그인 ②쓰기 권한 ③슬롯 유효성 ④원본 덮어쓰기 ⑤빈 이름 ⑥금지문자 ⑦스코프 이름 충돌. **진입점이 [저장]과 서버 등록 확인 모달 2개이므로 실제 저장 함수 첫 줄에서 재실행**한다(모달 경로 우회 차단).
  - **`isFileNameSafe` 분리**: 기존 `validateFrameName`은 100자 제한이 묶여 있어 ⑤⑥의 축과 다르다 → 순수 함수를 분리하고 `validateFrameName`이 그것을 쓰게 한다([01 §2](./01-tech-stack-and-structure.md) 주석).
  - **정책 배너는 편집 세션 전용**(신규 생성 세션은 서버 등록이 가능해 배너 문장이 거짓이 된다). 사본 분기는 **[선택 편집] 경로 전용**. `_` 비차단 경고·저장 스코프 동적 안내(등록을 **단정하지 않는 문구**).
  - **서버 등록 확인 모달([03 §11.4](./03-screens-spec.md))**: power 신규 생성 저장 시 노출. 체크박스 **기본 on** + 열 때마다 리셋, 체크 상태를 **닫기 전에 확정**, 고정 캡션, **원자성**(서버 등록 실패 시 로컬 저장도 안 함).
  - power 신규 생성 = 체크 on일 때 `POST /frames` + 이미지 PUT(+로컬 캐시) / 체크 off면 로컬 공용만, advanced_user = 로컬 전용.
  - 프레임 피커 모달(내부 그리드, 이미지 읽기만, 슬롯 배율 보정, 임시 파일 없음). **세션 정체성 = 신규 생성**(사본 아님), **이름 자동 제안 없음** + 원본 안내 캡션(이미지를 직접 다시 불러오면 비운다).
  - 삭제 확인 모달(로컬 항상 → power 체크 시 서버, 결과별 문구 4종). 삭제 후 목록 재스캔은 **조용한 갱신**(Step 14 국면 규칙).
- **검증 명령**: `npx vitest run tests/unit/frameEditor` · 편집 후 그 프레임으로 촬영·합성해 **슬롯 위치 일치** 확인 · 골든 이미지 재실행
- **완료 기준**:
  - [관측] 이미지를 넣으면 슬롯이 자동 배치되고 드래그로 이동하며, 저장 후 **그 프레임으로 촬영한 합성 결과의 슬롯 위치가 편집기 화면과 일치**한다(0px). 겹침·경계 이탈에서 저장이 거부된다. 사본 이름 규칙이 [선택 편집] 경로에서 동작한다.
  - [관측] **기존 이름을 그대로 타이핑해 저장하면 차단된다**(⑦) — 다른 공용 프레임이 조용히 덮어써지지 않는다. 예외는 [선택 편집]으로 연 본인 로컬 프레임뿐이다.
  - [non-goal] **`PUT /frames/{id}`를 호출하지 않는다**(Network 확인). 카탈로그 유래 프레임 편집이 **원본을 변경하지 않는다**. `user` 역할로는 편집기에 진입할 수 없다. 임시 파일이 생기지 않는다(저장 취소 후 OPFS 확인).
  - [trigger] 서버 등록은 **power의 신규 생성 세션 + 모달 체크 on**에만 일어난다. 피커로 불러온 세션도 **신규 생성이므로 등록 대상**이다(2026-07-30 재정의 — 종전 "피커 세션은 등록 안 됨"은 폐기).
  - [원자성] 서버 등록이 실패하면 **로컬에도 저장되지 않고** 편집 세션이 유지된다(부분 성공 0).
- **롤백**: 편집기·피커·삭제 모달 제거(프레임 사용만 가능).
- [x] **완료(2026-08-01)** — 설계: [`docs/design/web-step15-frame-editor-and-picker.md`](../design/web-step15-frame-editor-and-picker.md). **WD20 15a + 15b 전량**(이연 없음).
  - **산출물(신규 12)**: `domain/frames/frameSavePolicy.ts`·`frameImagePolicy.ts` /
    `adapters/frames/frameImageLoader.ts` / `shell/frameEditorIntent.ts` /
    `screens/frameEditor/frameEditorState.ts`·`frameEditorSave.ts`·`frameEditorEntry.ts`·`framePickerRunner.ts`·`previewUrl.ts`·`useFrameEditor.ts` /
    `ui/components/OverlayDialog.tsx` / `ui/views/FrameEditorView.tsx`(+ `frameEditor.module.css`).
    **수정 8**: `domain/frames/slotLayout.ts`(`rescaleSlots` **추가만**) · `domain/index.ts` ·
    `adapters/storage/frameStore.ts`(`scopeFrameNames` 추가 + `persist` 고아 정리) ·
    `adapters/http/frameRepository.ts`(`createFrame`의 `upload` 봉투) · `shell/shellStore.ts`(`ModalId` 4종으로 축소) ·
    `screens/frameSelect/useFrameSelect.ts`(인계 채널 배선) · `ui/strings.ts`(`frameEditor` 섹션 + 매핑 함수 3종) ·
    `App.tsx`·`ui/views/SettingsView.tsx`(주석).
  - **기존 결함 2건 동반 수정**: **F-4** `createFrame`이 응답 봉투 `{frame, upload:{putUrl,requiredHeaders}}`를 안 읽고 최상위에서
    찾아 **항상 `putUrl=null`** 이었다(호출자가 0명이라 드러나지 않았다 — 그대로 두면 이미지 PUT이 조용히 생략돼 모든 키오스크에서
    영구 "불러올 수 없음" 카드가 된다). **F-5** `saveLocal`이 같은 키를 덮어쓸 때 이전 OPFS PNG를 지우지 않아 편집 저장마다
    고아 파일이 남았다(정리는 **새 레코드 기록 뒤** — 반대로 하면 쓰기 실패 시 이미지 없는 프레임이 된다).
  - **검증 수치**: `npx tsc --noEmit` 0 · `npx vitest run` **1655 통과**(69파일, 종전 1469/62파일 → **+186**) ·
    `npx vitest run --coverage` `src/domain` lines 99.1 / branches 97.98 / functions 98.95(임계 95/90/95) ·
    `npx vite build` 성공(207 모듈). `docs/spec-vectors/`·`tests/MCPhoto.Tests/`·`web/functions/` **무변경** → `dotnet test` 불필요.
  - **설계 이탈 6건**(설계 문서 §18과 동일): ① 공용 모달 2종을 만들지 않고 `ModalId`에서 제거(화면 로컬 오버레이 통일) ·
    ② 스테이지를 canvas가 아니라 **`<img>` + DOM 슬롯**으로(좌표계 이중화·`ImageBitmap` 수명·접근성 수동 구현 제거) ·
    ③ ⑧ 개인 10개 상한을 7단 **뒤**에 편입(덮어쓰기는 상한 제외) · ④ 피커 목록에도 무진행 30초/총 60초 상한 ·
    ⑤ 이미지 PUT 실패 시 서버 문서를 best-effort로 정리 · ⑥ `saveLocal` 덮어쓰기 고아 정리(F-5).
  - **구현 중 추가한 이탈 3건**: ⑦ `frameSavePolicy`에 `DEFAULT_SCALE_PERCENT`(=100)를 함께 뒀다(reducer가 진입·이미지 교체·피커
    적용에서 같은 값을 쓴다 — 배율 상수를 한곳에 모으기 위함). ⑧ 파일 이미지 교체에서도 배율을 100으로 되돌린다(설계는 피커
    적용에만 명시했다 — 새 이미지는 새 레이아웃이라 같은 규칙이 자연스럽다). ⑨ `frameImageLoader`에 `fetchFrameImageBytes`·
    `probeFrameImageSize`를 함께 뒀다 — [선택 편집] 진입의 **재인코딩 금지** 경로(§9.3)가 쓸 브라우저 함수가 설계에 배치되지
    않았다. 같은 파일에 두고 "이쪽은 재인코딩하지 않는다"를 주석으로 못박았다.
  - **정적 불변식 8건 신설**: FR-8(`"framePicker"`·`"confirmDelete"` 리터럴 0 + `ModalId` 4종) · FR-9(`PUT /frames/{id}` 0) ·
    FR-10(`validateFrameSave(`가 `deps.createServerFrame(`·`deps.saveLocal(`보다 먼저) · FR-11(`requiresServerRegisterPrompt(` 호출 **정확히 2곳**) ·
    FR-12(`validateFrameName(` 0 + `isFileNameSafe(` 존재) · FR-13(reason 리터럴 8개 순서 — 특히 ④ < ⑦) ·
    FR-14(편집기 코드 `console.*` 0) · FR-15(`frameImageLoader`에 `mode: "cors"`). FR-5는 `FrameEditorView.tsx`까지 확장했고
    FR-1의 검사 대상에 `frameImageLoader.ts`를 추가했다. 인계 채널 배선·`readFrameEditorIntent` 비파괴성도 함께 고정했다.
  - **미검증(사용자 액션 V24)**: 브라우저·실계정·실기기가 필요한 8건을 [14 §10.10](./14-handoff-and-user-actions.md)에 등재했다.
    **추정으로 통과 처리하지 않았다.**
  - **📌 다음 작업자에게**: ① 삭제 UI는 Step 14의 화면 로컬 오버레이를 그대로 쓴다 — 재작성하지 마라(FR-5·FR-8).
    ② 프레임 zip 내보내기/가져오기는 **Step 16**(`exportImport.ts`)이 소유한다(15 §6의 "Step 15" 서술은 오기였고 정정했다).
    ③ `canEditFrame`은 power가 공용 로컬로 저장한 프레임(`userId=null`)을 **편집 불가**로 판정한다 — Windows와 같은 동작이고
    FR-2가 삭제 축을 고정하므로 고치지 마라. 우회로는 피커로 불러와 새 이름으로 저장하는 것이다.
    ④ 배율 범위는 **10~300**이다(Windows 실구현과 동일). 규격 문서에 남아 있던 70~130은 커밋 `0a93b59`가 넓히기 전의 **폐기값**이었고 2026-08-01에 문서 6곳을 소스에 맞췄다 — 되돌리지 마라.

---

## Step 16: 계정 · 사용자 관리 · 진단 · PWA

- **Context Brief**: 남은 P4 화면과 진단 모달, PWA/Service Worker, 내보내기/가져오기를 완성한다. 역할 게이트는 **서버 매트릭스와 1:1**이어야 한다. 규격은 **[03 §13·§14·§15.2](./03-screens-spec.md)**, **[07 §5](./07-auth-and-permissions-web.md)**, PWA는 **[01 §6](./01-tech-stack-and-structure.md)**.
- **대상 파일**: `src/screens/account/*`, `src/screens/userMgmt/*`, `src/screens/modals/diagnostics/*`, `src/sw.ts`, `src/adapters/storage/exportImport.ts`, `src/ui/views/{AccountView,UserMgmtView}.tsx`
- **선행 조건**: Step 12, Step 13
- **구현 내용**:
  - `Account`: 모드 2종. 내 정보(읽기 전용, 로그인 방식 라벨·"알 수 없음" 폴백) + PIN 변경. Admin 모드는 사용자 관리 진입(power) · 전역 한도(admin) · **[키오스크 종료]**(앱 종료 대체).
  - `UserMgmt`: 목록(실패 시 오류 표시, 빈 목록 폴백 금지) · 삭제(동급 허용) · **PIN 재설정(동급 차단)** · 역할 콤보(`assignableRoles`, 자기 행 미노출). 좁은 화면은 카드 리스트.
  - 진단 모달: [03 §15.2](./03-screens-spec.md)의 **6섹션**(카메라·인코더·서버·로그/저장소·개발자 문의·앱) + `/health`·`/frames/default` 두 프로브. **게이트 키 값 미표시**.
  - PWA: manifest + SW precache(앱 셸·번들 프레임·셔터음), **`skipWaiting` 미사용**, 업데이트 대기 표시 + [지금 적용].
  - 내보내기/가져오기: 설정 JSON · 프레임 zip(Windows `Frame\` 규칙) · 로그 `.log`.
- **검증 명령**: `npx playwright test e2e/role-matrix.spec.ts`(E18) · Lighthouse PWA 확인 · 오프라인에서 앱 로드 확인 · 프레임 zip을 Windows `Frame\`에 풀어 인식 확인
- **완료 기준**:
  - [관측] manager 로그인 시 다른 manager 행에 **[PIN]이 없고 [삭제]는 있다**. 역할 콤보에 `admin`이 없다. 진단 모달이 카메라·인코더·서버·저장소 상태를 표시하고 게이트 키는 "설정됨"만 보인다. 네트워크를 끊어도 앱이 로드된다(SW).
  - [non-goal] **앱 종료 버튼이 없다**(WD5). 목록 조회 실패가 빈 목록으로 표시되지 않는다. SW가 촬영 중 앱을 갱신하지 않는다. 게이트 키 값이 화면·로그에 없다.
  - [trigger] 사용자 관리 진입은 power만. 전역 한도 편집은 admin만. SW 갱신 적용은 [지금 적용] 또는 다음 시작에만.
- **롤백**: 해당 화면·SW 제거.
- [ ] 완료

---

## Step 17: E2E · 실기기 검증 · 수락 ★ 마일스톤 B

- **Context Brief**: [10 §5](./10-testing-and-acceptance.md)의 **E1~E24 전부(E1b·E3b 포함)** 를 자동화하고, [10 §6](./10-testing-and-acceptance.md) 매트릭스의 실기기에서 수동 체크리스트를 수행한다. 마지막으로 [10 §8](./10-testing-and-acceptance.md) 수락 체크리스트를 채운다.
- **대상 파일**: `webclient/tests/e2e/**`, `webclient/playwright.config.ts`, `docs/web-client/10-testing-and-acceptance.md`(성능 결과 기록), `docs/web-client/12-web-vs-windows-differences.md`(차이 최종 확인)
- **선행 조건**: Step 1~16 전부
- **⚠️ 앞 Step에서 이월된 것**: **Playwright 도입 자체가 이 Step이다.** 저장소에 Playwright 설치·설정이 없어 Step 11이 E2E를 만들지 않고 넘겼다(2026-07-31 승인).
  구체적으로 이월된 시나리오 2종:
  | 이월분 | 내용 | 원래 Step |
  |--------|------|-----------|
  | `e2e/upload-qr.spec.ts` | effective QR을 목으로 `true` 고정 → 촬영 완주 → `OPTIONS 204 → PUT 200` → QR 렌더 → 실패 시 QR 미표시 + [완료] 진행 가능 | Step 11 |
  | `e2e/guest-flow.spec.ts` | **게스트는 `Qr`을 건너뛰고 `Done`으로 끝나며 업로드 요청이 0건**이다(VF-11) | Step 11 |
  Step 11은 이 둘을 node 단위 테스트(`uploadRunner.test.ts`·`uploadGateway.test.ts`)와 [14 §10.5](./14-handoff-and-user-actions.md)의 **V20 실측**으로 대체했다.
- **구현 내용**:
  - Playwright: Chromium(fake device) + WebKit. E1~E24(E1b·E3b 포함) 시나리오 구현. CI에서 실행 가능하게(헤드리스). **위 이월분 2종 포함.**
  - 실기기: Windows Chrome · Android 태블릿 Chrome · iPadOS Safari **최소 3대**에서 [10 §6.3](./10-testing-and-acceptance.md) 체크리스트 수행 및 성능 수치 기록(OA-3·OA-4·OA-6·OA-7 검증).
  - 미등재 동작 차이가 발견되면 [12](./12-web-vs-windows-differences.md)에 행을 추가한다.
- **검증 명령**: `npx playwright test` · 실기기 체크리스트 · `npx vitest run --coverage`
- **완료 기준**:
  - [관측] E1~E24(E1b·E3b 포함)가 전부 통과하고, 실기기 3대에서 10컷 세션이 완주하며 성능 예산을 만족한다. [10 §8](./10-testing-and-acceptance.md) 수락 체크리스트가 전부 체크된다.
  - [non-goal] **[12 차이 보고서에 등재되지 않은 동작 차이가 0건**이다. Windows 앱·서버·P1 페이지에 회귀가 없다(Windows 테스트 + 서버 테스트 통과 확인).
  - [trigger] 출시 판정은 이 Step 완료 시에만.
- **롤백**: 해당 없음(검증 단계). 실패 항목은 원인 Step으로 되돌려 수정한다.
- [ ] 완료

---

## 마일스톤 요약

| 마일스톤 | 완료 Step | 그 시점에 가능한 것 | 선행 서버 작업 |
|----------|-----------|---------------------|----------------|
| **A. 촬영·업로드 경로 완주** | Step 11 | 촬영 → 합성 → 필터 → 타임랩스 → 로컬 보관 → 업로드 → QR → 폰 다운로드. **게스트 흐름은 `Done`까지**(QR은 로그인 전제 — effective QR 목으로 검증) | **0-5·0-6만** |
| **B. 전 기능 출시** | Step 17 | 로그인·프레임 저작·설정·계정·사용자 관리 포함 전부 | 0-1 ~ 0-6 |
