# 01 · 기술 스택과 프로젝트 구조

| 항목 | 값 |
|------|-----|
| 문서 | 무엇으로 만들고, 어떤 폴더 구조로, 어떻게 빌드·배포하는가 |
| 선행 문서 | [00 · 범위와 결정](./00-scope-and-decisions.md) |
| 대응 Windows 문서 | `docs/analysis/10-exe-app-architecture.md`(계층 구조 참조), `docs/design/multiplatform-client-architecture.md §2`(계층 규칙) |
| 갱신 규칙 | 의존성 추가·폴더 구조 변경·배포 방식 변경 시 갱신. CSP에 영향이 있으면 §5.3도 함께 |

---

## 1. 결정 요약

| 영역 | 선택 | 근거 |
|------|------|------|
| 언어 | **TypeScript 5.x**(`strict: true`) | 도메인 로직을 컴파일 타임에 고정. `MCPhoto.Core`의 순수 로직을 1:1 이식하기에 적합 |
| UI | **React 18** | 13화면 × 상태·권한 분기가 많다. 컴포넌트 단위 테스트·조건부 렌더 가드(M10)에 유리 |
| 빌드 | **Vite 5** | 정적 산출물만 필요(서버 런타임 없음). Hosting에 그대로 올린다 |
| 상태 | **Zustand** (스토어 4개: shell / session / settings / auth) | 전역 싱글턴 상태 + 구독이 필요(세션 변경 통지 → 토큰 폐기 M1). Context보다 구독 제어가 명확 |
| 라우팅 | **화면 상태는 URL이 아니다.** 라우터는 3경로만(`/` 앱, `/oauth2callback`, `/health-check`) | 키오스크에서 뒤로가기·URL 조작으로 상태를 깨면 안 된다([02 §3](./02-app-shell-and-navigation.md)) |
| 스타일 | **CSS Modules + CSS 변수 팔레트** | 터치 타깃·다크모드·접근성을 토큰으로 관리. 런타임 CSS-in-JS는 CSP `unsafe-inline` 유발 가능성 때문에 피한다 |
| 테스트 | **Vitest**(단위·도메인) + **Playwright**(E2E) + **자체 골든 이미지 비교** | [10](./10-testing-and-acceptance.md) |
| 패키지 매니저 | **npm**(저장소 기존 관행: `web/`·`web/functions`가 npm) | 일관성 |

### 1.1 프레임워크를 쓰지 않는 부분

| 영역 | 방식 | 이유 |
|------|------|------|
| 미디어 파이프라인 | **순수 Web API + Worker**(React 밖) | 프레임당 처리에 React 렌더 루프를 끼우면 성능이 무너진다. 프리뷰 canvas는 ref로 직접 그린다 |
| 도메인 로직 | **의존성 0의 순수 TS 함수** | 브라우저 API·React·시각(`Date.now`)·난수를 직접 부르지 않는다(인자로 주입) → 100% 단위 테스트 |
| Firebase | **P1 페이지만 Firebase JS SDK 사용(기존 유지).** 웹 앱은 **Firebase SDK를 쓰지 않는다** | 앱은 백엔드 HTTPS API + 서명 URL PUT만 쓴다. Firestore·Storage SDK를 넣으면 `docs/analysis/05 §1` 계약 위반 |

> ⚠️ **웹 앱에 `firebase` 패키지를 추가하지 말 것.** 클라이언트는 DB·Storage에 직접 접근하지 않는다. 위반하면 코드 리뷰에서 거부한다.

---

## 2. 계층 구조

Windows 구현이 검증한 4계층 경계를 그대로 쓴다(`docs/design/multiplatform-client-architecture.md §2`).

```
src/ui/           React 컴포넌트 · 화면 렌더 · 입력 · 접근성
   ↓ (스토어의 상태·액션만 소비)
src/screens/      화면 로직(Presenter) — 화면별 상태·명령·진입/이탈 훅
src/shell/        앱 셸 — 상태머신 · 유휴 감시 · 세션 · 전역 예외 · 전체화면
   ↓ (인터페이스에만 의존)
src/domain/       ★ 순수 TS · 브라우저 API 0 · 100% 테스트 가능
                  상태 전이 · 크롭 · 슬롯 기하 · 좌표 변환 · Ready 판정 ·
                  역할 정책 · 프레임 출처·편집 판정 · 사본 이름 · QR 정규화 ·
                  세션 ID·URL 조립 · 설정 clamp · 타임랩스 배속 · 유휴 카운트다운
   ↑ (도메인이 정의한 포트를 구현)
src/adapters/     플랫폼 어댑터 — 카메라 · 인코더 · 합성 · 저장 · HTTP · OAuth ·
                  QR · 로그 · 전체화면 · Wake Lock
```

### 2.1 계층 규칙 (리뷰 필수 항목)

| 규칙 | 내용 |
|------|------|
| 의존 방향 단방향 | `ui → screens → shell → domain ← adapters`. **`domain`은 아무것도 import하지 않는다**(`type`도 브라우저 lib 금지) |
| 도메인에 부작용 금지 | `Date.now()`·`Math.random()`·`crypto`·`fetch`·`localStorage` 직접 호출 금지 → 전부 인자/포트로 주입 |
| 어댑터는 예외를 전파하지 않는다 | 장치 열기 실패·인코더 부재 = `false`/`null` 반환. 상위가 상태로 표현(`docs/analysis/14 §2.1`) |
| 하드웨어 단일 소유 | 카메라 어댑터는 **모듈 싱글턴 1개**. 화면은 소유하지 않고 빌린다(실촬영·라이브 프리뷰·테스트 모달이 같은 인스턴스 공유) |
| 화면 로직은 DOM을 모른다 | 모달 열기·전체화면 요청은 셸에 이벤트로 요청한다 |

### 2.2 도메인 모듈과 Windows 소스 대응

**이 표대로 이식하면 도메인 계층이 완성된다.** 각 Windows 파일은 순수 로직이므로 그대로 옮길 수 있고, 대응 테스트도 존재한다.

| 웹 모듈(`src/domain/`) | Windows 원본 | 규격 | 대응 테스트(벡터 추출원) |
|------------------------|--------------|------|--------------------------|
| `navigation/stateMachine.ts` | `src/MCPhoto.Core/Navigation/SessionStateMachine.cs` | `analysis/13 §2` | `tests/MCPhoto.Tests/AppStateTests.cs` |
| `navigation/idleCountdown.ts` | `src/MCPhoto.Core/Navigation/IdleCountdown.cs` | `analysis/13 §7` | `IdleCountdownTests.cs` |
| `capture/centerCrop.ts` | `src/MCPhoto.Core/Capture/CropCalculator.cs` | `analysis/14 §3` | `CropCalculatorTests.cs` |
| `capture/previewReadiness.ts` | `src/MCPhoto.Core/Capture/PreviewReadiness.cs` | `analysis/14 §2.3` | `PreviewReadinessTests.cs` |
| `capture/captureSession.ts` | `src/MCPhoto.Core/Capture/CaptureSession.cs` | `analysis/13 §4.5` | `CaptureSessionTests.cs` |
| `capture/timelapseSpeed.ts` | `src/MCPhoto.Core/Capture/FfmpegArgs.cs`(`ComputeSpeedFactor`) | `analysis/14 §7.2` | `FfmpegArgsTests.cs` |
| `frames/slotLayout.ts` | `src/MCPhoto.Core/Frames/SlotLayout.cs` | `analysis/14 §4.1~4.4` | `SlotLayoutTests.cs` |
| `frames/editorTransform.ts` | `src/MCPhoto.Core/Frames/EditorTransform.cs` | `analysis/14 §4.5` | `EditorTransformTests.cs` |
| `frames/frameOrigin.ts` | `src/MCPhoto.Core/Frames/FrameOrigin.cs` | `analysis/13 §6.1` | `FrameOriginTests.cs` |
| `frames/frameEditPolicy.ts` | `src/MCPhoto.Core/Frames/FrameEditPolicy.cs` | `analysis/13 §6.1` | `FrameEditPolicyTests.cs` |
| `frames/frameNaming.ts` | `src/MCPhoto.Core/Frames/FrameNaming.cs` | `analysis/13 §6.4` | `FrameNamingTests.cs` |
| `frames/slotAspect.ts` | `src/MCPhoto.Core/Frames/SlotAspect.cs` | `analysis/14 §1.1` | — |
| `frames/slotsFile.ts` (`.slots` 파서·직렬화) | `src/MCPhoto.Core/Frames/LocalFrameStore.cs`(포맷 부분) | `analysis/41 §3.3` | `LocalFrameStoreTests.cs` |
| `settings/appSettings.ts`(기본값·clamp) | `src/MCPhoto.Core/Settings/AppSettings.cs` | `analysis/41 §2.1` | `SettingsTests.cs` |
| `settings/cutCountPolicy.ts`(자동 컷 수 해석, it17) | `src/MCPhoto.Core/Settings/CutCountPolicy.cs` | `analysis/41 §2.7` | `CutCountPolicyTests.cs` |
| `settings/qrDeliveryPolicy.ts` | `src/MCPhoto.Core/Settings/QrDeliveryPolicy.cs` | `analysis/41 §2.4` | `QrDeliveryPolicyTests.cs` |
| `settings/qrEffectivePolicy.ts`(**런타임 QR on/off** — 게스트·TempUser 초과 오버라이드) | `src/MCPhoto.Core/Settings/QrEffectivePolicy.cs` | `design/wpf-it13-temp-user-role-design.md §7.1b` | `QrEffectivePolicyTests.cs` |
| `roles/rolePolicy.ts`(`isPower`·`canWriteFrames`·`canManage`·`canResetPin`) | `src/MCPhoto.Core/Models/UserRole.cs` | `analysis/60 §1` | `RoleManagementTests.cs` |
| `roles/roleChangePolicy.ts`(`assignableRoles`) | `src/MCPhoto.Core/Models/RoleChangePolicy.cs` | `analysis/60 §1.4` | `RoleManagementTests.cs`, `UserMgmtViewModelTests.cs` |
| `upload/uploadContract.ts`(세션 ID·경로·토큰 URL·다운로드 페이지 URL·만료) | `src/MCPhoto.Core/Upload/UploadContract.cs` | `analysis/31 §7` | `UploadContractTests.cs` |
| `upload/uploadOrchestration.ts`(3단계 순서·최소 1개·진행률 합산) | `src/MCPhoto.Core/Upload/UploadService.cs` | `analysis/31 §5` | `UploadServiceTests.cs` |
| `filters/filterParams.ts` | `src/MCPhoto.Capture/Filters.cs` | `analysis/14 §6` | `FiltersTests.cs` |
| `frames/frameCatalogPolicy.ts`(목록 우선순위·이름 dedup) | `src/MCPhoto.App/Services/FrameCatalogService.cs` | `analysis/13 §5` | `FrameCatalogServiceTests.cs` |

> **`DisplayApplyPolicy.cs`는 이식하지 않는다**(창 개념 없음, WD7).

---

## 3. 폴더 구조

새 소스는 저장소 루트의 **`webclient/`** 에 둔다(기존 `web/`은 Hosting 구성 + P1 페이지 + Functions이므로 섞지 않는다).

```
webclient/
├─ package.json
├─ tsconfig.json                    # strict, paths: @domain/* @adapters/* ...
├─ vite.config.ts                   # build.outDir = "../web/kiosk"
├─ vitest.config.ts
├─ playwright.config.ts
├─ index.html                       # 앱 진입(단일)
├─ public/                          # 그대로 복사되는 정적 자산
│  ├─ manifest.webmanifest          # PWA(설치 유도 — WD4·WB3 완화)
│  ├─ branding.json                 # 브랜딩 기본값(운영자가 교체 — WD13)
│  ├─ icons/                        # PWA 아이콘
│  ├─ frames/                       # 번들 기본 프레임(오프라인 폴백)
│  │   ├─ 베이직 4컷.png
│  │   └─ 베이직 4컷.slots
│  └─ sounds/shutter.wav            # 셔터음(없으면 합성음 폴백)
├─ src/
│  ├─ main.tsx                      # 부트스트랩(순서 규격 — §4.2)
│  ├─ env.ts                        # 빌드 주입값 검증·정규화
│  ├─ domain/                       # ★ 순수 TS (§2.2 표)
│  │  ├─ navigation/  capture/  frames/  settings/  roles/  upload/  filters/
│  │  └─ index.ts
│  ├─ shell/
│  │  ├─ shellStore.ts              # 화면 상태·오버레이 복귀·모달·토스트
│  │  ├─ sessionStore.ts            # currentUser + 촬영 세션(단일 소스)
│  │  ├─ settingsStore.ts
│  │  ├─ authStore.ts               # JWT 홀더(persist 금지 — M2)
│  │  ├─ idleWatchdog.ts
│  │  ├─ globalErrorHandler.ts      # M16
│  │  └─ fullscreenController.ts    # WD7
│  ├─ screens/                      # 화면 로직(13개) + 모달 로직(6개)
│  │  ├─ home/  login/  frameSelect/  guide/  capture/  cutSelect/
│  │  ├─ result/  qr/  done/  frameEditor/  settings/  account/  userMgmt/
│  │  └─ modals/ cameraTest/ diagnostics/ pinPrompt/ framePicker/ confirmDelete/ idleWarning/
│  ├─ adapters/
│  │  ├─ camera/                    # getUserMedia + Worker 파이프라인
│  │  │  ├─ cameraService.ts        # 싱글턴 · 포트 구현
│  │  │  ├─ frameProcessor.worker.ts# 거울·크롭 1회 가공 → 3분기
│  │  │  └─ deviceEnumerator.ts
│  │  ├─ encode/
│  │  │  ├─ timelapseEncoder.ts     # 경로 판정 + 인코딩(WD2)
│  │  │  ├─ mediaRecorderMp4.ts
│  │  │  └─ webCodecsMp4.ts
│  │  ├─ compose/
│  │  │  ├─ compositor.ts           # 합성(analysis/14 §5)
│  │  │  └─ filters/                # grayscale/brightness/beauty(WebGL2)
│  │  ├─ storage/
│  │  │  ├─ settingsRepo.ts         # localStorage
│  │  │  ├─ frameStore.ts           # IndexedDB + OPFS
│  │  │  ├─ sessionWorkspace.ts     # OPFS sessions/{id}/
│  │  │  ├─ resultSaver.ts          # OPFS + FSA 폴더 + 내보내기(WD3)
│  │  │  └─ logStore.ts             # IndexedDB 링버퍼(WD6)
│  │  ├─ http/
│  │  │  ├─ backendClient.ts        # 헤더·에러 매핑·타임아웃
│  │  │  ├─ accountService.ts  frameRepository.ts  uploadGateway.ts
│  │  │  ├─ qrUsageService.ts  tempUserLimitsService.ts  healthService.ts
│  │  ├─ auth/googleSignIn.ts       # PKCE 리디렉트
│  │  ├─ qr/qrService.ts            # QR PNG/Canvas 생성
│  │  └─ platform/{wakeLock,visibility,keyboardLock,persistStorage}.ts
│  ├─ ui/
│  │  ├─ components/                # Button·Toggle·Select·Modal·Toast·Spinner ...
│  │  ├─ theme/                     # CSS 변수 팔레트(라이트/다크)
│  │  └─ views/                     # 화면별 프레젠테이션 컴포넌트
│  └─ sw.ts                         # Service Worker(precache — WD15)
├─ tests/
│  ├─ unit/                         # domain 단위 테스트
│  ├─ vectors/                      # 공유 테스트 벡터(JSON) 로더
│  ├─ golden/                       # 골든 이미지 + 비교 유틸
│  └─ e2e/                          # Playwright
└─ .env.example                     # 빌드 주입값 예시(§4.1)
```

배포 산출물은 `web/kiosk/`(빌드 커밋 여부는 §5.2).

---

## 4. 구성값 주입과 부트스트랩

### 4.1 빌드 주입값 (Vite `import.meta.env`)

`docs/analysis/41 §2.1`의 설정 키 중 **접속 구성**은 빌드 시 주입하고, 나머지는 설정 화면에서 편집한다.

| 환경변수 | 대응 설정 키 | 기본값 | 비밀? |
|----------|--------------|--------|:-----:|
| `VITE_BACKEND_BASE_URL` | `BackendBaseUrl` | `https://asia-northeast3-mcphoto-955fb.cloudfunctions.net/api` | 아니오 |
| `VITE_BACKEND_API_KEY` | `BackendApiKey` | (없음 — **필수 주입**) | **공개됨**(WD10) |
| `VITE_GOOGLE_CLIENT_ID` | `GoogleClientId` | (웹 전용 client_id) | 아니오 |
| `VITE_HOSTING_BASE_URL` | `HostingBaseUrl` | `https://mcphoto-955fb.web.app` | 아니오 |
| `VITE_STORAGE_BUCKET` | `StorageBucket` | `mcphoto-955fb.firebasestorage.app` | 아니오 |
| `VITE_APP_VERSION` | 버전 표기(`v{version}`) | `0.0.0` | 아니오 |
| `VITE_BUILD_DATE` | 빌드 시각(진단 화면 전용 — 하단 캡션에는 쓰지 않는다) | 빌드 시 주입 | 아니오 |

> **it18 반영**: Windows가 배포 채널(`Site`) 표기를 폐기하고 버전을 빌드 산출물 자신(어셈블리 리소스)에서 읽는 방식으로 바꿨다(`analysis/41 §7`). 웹의 빌드 상수 방식은 이 방향과 일치하며, **`Site` 상수는 만들지 않는다**.

규칙(`docs/analysis/41 §2.1` 계약):

- `HostingBaseUrl`은 **트레일링 슬래시 제거**, `BackendBaseUrl`은 **트레일링 슬래시 부여**. 방향이 반대다 — **같은 정규화 함수를 쓰지 말 것**.
- 설정 화면에 값이 있으면 **설정값이 우선**한다(빌드 주입은 기본값).
- `BackendApiKey`는 **설정에 저장하지 않는다**(`analysis/41 §2.5`). 진단 화면에는 "설정됨/미설정"만 표시.
- `GoogleClientId`가 비면 **로그인 버튼을 통째로 숨긴다**(`analysis/61 §8`).
- `env.ts`가 시작 시 값을 검증하고, 필수값(게이트 키)이 없으면 **로그 경고 + 진단 화면에 미설정 표시**(크래시 금지).

### 4.2 부트스트랩 순서 (규격)

`docs/analysis/41 §6`(브랜딩은 첫 화면 생성 전 주입)과 `§4`(세션 잔재 정리)를 지킨다.

```
1. env 검증·정규화
2. 로그 스토어 초기화(이후 모든 단계가 로깅 가능해야 한다)
3. /branding.json fetch (실패·타임아웃 800ms → 기본값)     ← 첫 렌더 전
4. 설정 로드 + clamp (손상 시 기본값 + 경고 로그)
5. navigator.storage.persist() 요청 (결과 기록, 실패 무시)
6. OPFS sessions/ 잔재 일괄 삭제                          ← analysis/41 §4 규격
7. Service Worker 등록 (실패 무시)
8. 전역 예외 핸들러 설치(M16)
9. OAuth 콜백 경로면 콜백 처리 → 그 후 앱 진입
10. React 마운트 → Home
11. 첫 사용자 제스처에서: 전체화면 요청 + AudioContext unlock + Wake Lock
```

> **11번이 첫 제스처에 묶이는 이유**: 전체화면·오디오·Wake Lock은 모두 **사용자 제스처를 요구**한다. Home의 [촬영하기]와 별개로, 화면 아무 곳이나 첫 터치에서 시도한다.

---

## 5. 배포

### 5.1 Hosting 멀티사이트 (WD12)

기존 P1 다운로드 페이지(`web/public/`)를 건드리지 않기 위해 **별 사이트**로 배포한다.

| 사이트 | 내용 | 도메인(예) | CSP·캐시 |
|--------|------|-----------|----------|
| 기본(default) | P1 다운로드 페이지(현행) | `mcphoto-955fb.web.app` | 현행 유지(변경 금지) |
| `kiosk` | **웹 앱(신규)** | `mcphoto-955fb-kiosk.web.app` | §5.3 |

`web/firebase.json`의 `hosting`을 **배열로 바꾸고** 기존 블록에 `"target": "default"`를 추가한다(기존 값은 그대로 유지).

```json
"hosting": [
  {
    "target": "default",
    "public": "public",
    "...": "기존 설정 그대로 — 손대지 않는다"
  },
  {
    "target": "kiosk",
    "public": "kiosk",
    "ignore": ["firebase.json", "**/.*", "**/node_modules/**"],
    "rewrites": [{ "source": "**", "destination": "/index.html" }],
    "headers": [
      { "source": "**", "headers": [
        { "key": "Content-Security-Policy", "value": "<§5.3 값>" },
        { "key": "X-Content-Type-Options", "value": "nosniff" },
        { "key": "Referrer-Policy", "value": "strict-origin-when-cross-origin" },
        { "key": "Permissions-Policy", "value": "camera=(self), microphone=(), geolocation=()" }
      ]},
      { "source": "/index.html", "headers": [{ "key": "Cache-Control", "value": "no-cache, max-age=0" }] },
      { "source": "/sw.js", "headers": [{ "key": "Cache-Control", "value": "no-cache, max-age=0" }] },
      { "source": "/branding.json", "headers": [{ "key": "Cache-Control", "value": "no-cache, max-age=0" }] },
      { "source": "/assets/**", "headers": [{ "key": "Cache-Control", "value": "public, max-age=31536000, immutable" }] }
    ]
  }
]
```

사이트 생성·타깃 연결(1회):

```bash
cd web
npx firebase hosting:sites:create mcphoto-955fb-kiosk
npx firebase target:apply hosting kiosk mcphoto-955fb-kiosk
npx firebase target:apply hosting default mcphoto-955fb
```

> ⚠️ `target:apply`를 하면 `.firebaserc`에 `targets`가 추가된다. **기존 `default` 타깃도 반드시 함께 지정**해야 `firebase deploy --only hosting`이 P1 사이트를 잃지 않는다.

### 5.2 빌드·배포 명령

```bash
# 개발
cd webclient && npm ci && npm run dev          # https 필요 시 vite --https (§5.4)

# 빌드 (산출물: web/kiosk/)
npm run build

# 배포 (앱만)
cd ../web && npx firebase deploy --only hosting:kiosk

# 배포 (P1도 함께 — 기본은 하지 않는다)
npx firebase deploy --only hosting
```

- `web/kiosk/`는 **`.gitignore`에 추가**하고 커밋하지 않는다(빌드 산출물). CI가 없다면 배포 스크립트(`webclient/deploy.bat`)가 `npm run build && firebase deploy --only hosting:kiosk`를 수행한다.
- 게이트 키는 `.env.production.local`(gitignore)에 두거나 CI 시크릿으로 주입한다. **저장소에 커밋하지 않는다**(`analysis/05 §11` 체크리스트).

### 5.3 CSP (kiosk 사이트)

앱은 Firebase SDK를 쓰지 않으므로 gstatic이 필요 없다. 대신 **백엔드 함수·Storage 두 도메인**이 필요하다.

```
default-src 'self';
script-src 'self' 'wasm-unsafe-eval';
style-src 'self' 'unsafe-inline';
img-src 'self' data: blob: https://firebasestorage.googleapis.com;
media-src 'self' blob: https://firebasestorage.googleapis.com;
connect-src 'self'
  https://asia-northeast3-mcphoto-955fb.cloudfunctions.net
  https://storage.googleapis.com
  https://firebasestorage.googleapis.com;
worker-src 'self' blob:;
font-src 'self';
object-src 'none';
base-uri 'self';
frame-ancestors 'none';
form-action 'none'
```

| 항목 | 왜 필요한가 |
|------|-------------|
| `'wasm-unsafe-eval'` | MP4 muxer·이미지 처리에 wasm을 쓰는 라이브러리를 허용하기 위함. wasm을 안 쓰기로 확정되면 제거한다 |
| `blob:` (img/media/worker) | 합성 결과·타임랩스 프리뷰·Worker 로딩 |
| `connect-src` 함수 도메인 | 백엔드 API |
| `connect-src storage.googleapis.com` | **서명 URL PUT**(prepare가 주는 `putUrl` 호스트) |
| `connect-src firebasestorage.googleapis.com` | 서버 프레임 이미지 CORS fetch(canvas 오염 방지 — WM2) |
| `form-action 'none'` | 폼 전송 경로가 없다(OAuth는 `location.assign`이라 `form-action`과 무관) |

> Google OAuth authorize URL로는 **전체 페이지 이동**(`location.assign`)을 하므로 CSP `navigate-to`를 걸지 않는다(걸면 로그인이 막힌다).

### 5.4 로컬 개발에서 HTTPS가 필요한 이유

`getUserMedia`·`crypto.randomUUID`·OPFS·Service Worker는 **보안 컨텍스트**를 요구한다. `http://localhost`는 보안 컨텍스트로 인정되므로 기본 개발은 `localhost`로 충분하다. 단 **다른 기기(태블릿·폰)에서 열어 테스트할 때는 반드시 HTTPS**여야 한다.

| 방법 | 용도 |
|------|------|
| `vite --host` + `localhost` | PC 브라우저 개발 |
| `vite --host --https`(자체 서명) + 기기에 인증서 신뢰 | 사내망 실기기 테스트 |
| **Firebase Hosting preview channel**(`firebase hosting:channel:deploy dev`) | **실기기 검증에 권장** — 실제 HTTPS·CSP·CORS 조건이 운영과 같다 |

OAuth 리디렉트 URI는 **개발용도 Google Console에 등록**해야 한다([08 §3](./08-server-and-infra-prerequisites.md)).

---

## 6. PWA · Service Worker (WD15)

| 항목 | 규격 |
|------|------|
| 목적 | ① 오프라인 게스트 촬영 ② 설치 시 저장소 영속성 향상(§00 §3.2) ③ 전체화면 `standalone` 실행 |
| manifest | `display: "fullscreen"`, `orientation: "any"`, `start_url: "/"`, 아이콘 192/512, `background_color`·`theme_color` |
| precache 대상 | 앱 셸(HTML/JS/CSS) · 번들 프레임 PNG+`.slots` · 셔터음 · 아이콘 |
| **precache 금지 대상** | 백엔드 API 응답 · 서명 URL · 서버 프레임 이미지(용량·신선도) |
| 런타임 캐시 | 서버 프레임 이미지는 **SW 캐시가 아니라 OPFS 프레임 캐시**로 관리한다([05 §4](./05-storage-and-persistence.md)) — 목록 dedup 규격과 한 곳에서 관리해야 한다 |
| 업데이트 정책 | `skipWaiting` **하지 않는다**. 새 버전은 다음 앱 시작에 적용(촬영 중 갱신 금지). 진단 화면에 "업데이트 대기 중" 표시 + [지금 적용] 버튼 |
| 오프라인 판정 | `navigator.onLine`은 신뢰하지 않는다. **`GET /health` 프로브 결과**를 서버 연결 상태의 근거로 쓴다(`analysis/13 §9.2`) |

---

## 7. 의존성 (핀 고정 · 전량 자체 호스팅)

**CDN에서 런타임 로드하지 않는다**(CSP·오프라인·재현성). 전부 번들에 포함한다.

| 목적 | 후보 | 주의 |
|------|------|------|
| UI | `react`, `react-dom` | — |
| 상태 | `zustand` | `persist` 미들웨어를 **authStore에 쓰지 않는다**(M2) |
| IndexedDB | `idb` (얇은 래퍼) | 없어도 무방. 스키마 버전 관리만 하면 됨 |
| QR 생성 | `qrcode`(canvas/PNG 출력) | Windows는 QRCoder **`ECCLevel.Q`**(기본값이 아니라 명시 지정 — `src/MCPhoto.Core/Upload/QrService.cs`, `analysis/30 §3`). **오류정정 레벨은 Q로 맞춘다**([03 §9](./03-screens-spec.md)) |
| MP4 muxing | `mp4-muxer` 또는 동급 순수 JS muxer | **버전 핀 고정 필수.** WebCodecs 경로에서만 사용 |
| 테스트 | `vitest`, `@vitest/coverage-v8`, `@playwright/test`, `pixelmatch`(골든 비교) | — |

> **라이선스 확인 항목**: MP4 muxer·QR 라이브러리의 라이선스를 상용 배포 관점에서 확인하고 `webclient/THIRD-PARTY.md`에 기록한다(상용화 단계 요구).

---

## 8. 코드 규약

| 규약 | 내용 |
|------|------|
| 정수 나눗셈 | 규격이 정수 나눗셈인 곳은 **`Math.floor`**, 반올림인 곳은 **`Math.round`** 를 명시한다. JS `/`를 그대로 쓰면 Windows와 픽셀이 어긋난다([04 §9](./04-media-pipeline-web.md) 대응표 필수 참조) |
| 시각 | 도메인에는 `now: number`를 주입한다. `Date.now()` 직접 호출은 어댑터·셸에서만 |
| 난수 | `crypto.randomUUID()`는 어댑터에서만. 도메인은 생성기 함수를 주입받는다 |
| 로그 | `logger.info/warn/error(msg, ctx?)`만 사용. `console.*` 직접 호출 금지(로그 스토어를 우회한다) |
| **로그 금지 값** | JWT · 게이트 키 · 인가 코드 · `code_verifier` · `state` · `nonce` · PIN (`analysis/41 §8`) |
| 문구 | 사용자 문구는 `src/ui/strings.ts` 한 곳에 모으고 **`analysis/13 §14` 카탈로그와 1:1**로 맞춘다(테스트로 고정) |
| 접근성 | 터치 타깃 최소 48px · 가로 스크롤 금지 · `aria-live` 상태 안내 · `prefers-reduced-motion` 존중 · 다크 모드 지원 |
| 파일당 1 책임 | 화면 로직 파일에 어댑터 구현을 섞지 않는다 |
