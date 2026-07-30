# 13 · 소스·문서 참조 지도

| 항목 | 값 |
|------|-----|
| 문서 | **무엇을 만들 때 저장소의 어느 문서·어느 파일을 봐야 하는가** |
| 왜 필요한가 | 이 폴더의 문서는 "웹에서 어떻게"를 다루고, **"무엇을"의 진실원은 `docs/analysis`와 실제 소스**다. 그 위치를 한 곳에 모았다 |
| 사용법 | 작업 시작 시 §7의 "작업별 읽기 레시피"에서 해당 행을 찾아 그 문서·파일만 읽는다 |
| 갱신 규칙 | 파일 이동·문서 추가 시 갱신 |

---

## 1. 진실원 우선순위 (충돌 시)

```
실제 소스  >  docs/analysis  >  docs/design  >  docs/web-client
   │              │                 │               │
   │              │                 │               └ 웹 구현 방법(이 폴더). 규격과 다르면 이 폴더가 버그
   │              │                 └ "왜 그렇게 결정했나"(이력 포함 — 폐기된 서술 주의 §3.1)
   │              └ "현재 무엇이 어떻게 동작하나"(규격의 진실원)
   └ 문서와 다르면 소스가 사실이고 analysis를 고쳐야 한다
```

**예외**: [12 차이 보고서](./12-web-vs-windows-differences.md)에 등재된 **웹 변형 항목**은 이 폴더가 우선한다.

---

## 2. 규격 문서 (`docs/analysis/`) — 웹 개발에 필요한 것

`docs/analysis/README.md`가 전체 인덱스다. 웹 개발에 **반드시** 필요한 것만 아래에 정리했다.

### 2.1 플랫폼 중립 규격 (새 클라이언트의 진실원 — 전량 필독)

| 문서 | 무엇이 있나 | 웹에서 어디에 쓰나 |
|------|-------------|---------------------|
| **`05-cross-platform-client-guide.md`** | 용어 사전(WPF 어휘 → 중립 어휘) · 프로파일 P1~P4 · 기능×플랫폼 매트릭스 · **Windows 전용 항목 목록(§5.1)** · **불변식 M1~M16(§6)** · 서버 블로커 B1~B8(§9) · 적합성 체크리스트(§11) | [00](./00-scope-and-decisions.md) 전반. **§7.4는 웹 제약 판정인데 이 폴더가 재판정했다**([00 §3](./00-scope-and-decisions.md)) |
| **`13-client-behavior-spec.md`** | **화면 13종(§1)** · 전이표(§2) · 세션·인증 규칙(§3) · 촬영 흐름 상세(§4) · 프레임 목록 우선순위(§5) · 저작 규격(§6) · 유휴(§7) · 설정 화면(§8) · 모달(§9) · P4 운영(§10) · 오류 정책(§11) · P1 소비자(§12) · **타이밍 상수 전수(§13)** · **문구 카탈로그(§14)** | [02](./02-app-shell-and-navigation.md)·[03](./03-screens-spec.md) 전체의 근거 |
| **`14-media-pipeline-spec.md`** | 좌표계(§1) · 카메라 계약(§2) · **중앙 크롭 식(§3)** · **슬롯 기하 알고리즘(§4)** · **합성 절차(§5)** · **필터 파라미터(§6)** · 녹화·타임랩스(§7) · 산출물(§8) · 스레딩(§9) · 체크리스트(§10) | [04](./04-media-pipeline-web.md) 전체의 근거. **의사코드를 그대로 구현할 것** |
| **`31-backend-api-reference.md`** | 헤더·게이트(§2) · **에러 매핑(§3)** · **엔드포인트 전수(§4)** · **업로드 3단계(§5)** · P1이 읽는 것(§6) · **세션 ID·경로·URL 조립(§7)** · 입력 검증 전수(§8) · 서버 구성값(§9) · 서버에 없는 것(§10) | [06](./06-backend-integration-web.md) 전체의 근거 |
| **`41-local-data-and-file-formats.md`** | 계약/자유 경계(§1) · **설정 키 전수(§2.1)** · QR 정규화(§2.4) · 게이트 키 취급(§2.5) · **프레임 저장소·`.slots` 포맷(§3)** · 세션 작업 공간(§4) · 결과물 보관(§5) · 브랜딩(§6) · 버전(§7) · 로그(§8) · 플랫폼별 위치(§9) | [05](./05-storage-and-persistence.md) 전체의 근거 |
| **`61-auth-platform-integration.md`** | 인증 모델(§1) · **현재 서버 제약 C1~C5(§2)** · **플랫폼별 OAuth 흐름(§3 — 웹은 §3.4)** · **서버 확장 설계 제안(§4)** · 서버 검증(§5) · **JWT 규약(§6)** · **PIN 게이트(§7)** · 미구성 처리(§8) · 오프라인(§9) | [07](./07-auth-and-permissions-web.md)·[08](./08-server-and-infra-prerequisites.md)의 근거 |

### 2.2 공통 규격 (그대로 유효)

| 문서 | 웹에서 필요한 절 |
|------|------------------|
| `00-overview-and-architecture.md` | §2 컴포넌트 맵 · §3 end-to-end 흐름 · §6 핵심 불변식 요약 — **전체 그림 파악용** |
| `40-database-firestore-and-storage-schema.md` | §2.3 `resultSessions` 스키마 · §4 Storage 경로 규약 · §5 보안 규칙 · §7 계약 불변식 |
| `60-auth-accounts-and-roles.md` | **§1 역할 위계·판정 함수** · **§1.4 역할 변경 매트릭스** · **§2 권한 매트릭스(화면·기능별)** · §3.5 로그아웃/유지 매트릭스 · §4.5 백엔드 미도달 시 동작 |
| `50-infra-gcp-lifecycle-and-ttl.md` | 만료 2축(접근 만료 vs 물리 삭제) — 웹 앱은 삭제에 관여하지 않는다 |
| `90-roadmap-and-future-work.md` | **§1 알려진 이슈**(같은 함정을 반복하지 않기) · §7.2 서버 블로커 상태 · §7.2.1 웹 범위 판정(이 폴더가 대체) |
| `30-backend-firebase-integration.md` | §3 인증 모델·§5 업로드 3단계의 **설계 의도**와 실패 정책(왜 그렇게 되어 있는지) |

### 2.3 Windows 구현 참조 (예시로만 — 이식 대상 아님)

| 문서 | 웹에서 얻을 것 | 주의 |
|------|----------------|------|
| `10-exe-app-architecture.md` | 계층 분리·DI·스레딩·리소스 해제의 **검증된 구조**. §4 캡처 파이프라인, §5.1 전역 예외 | WPF·OpenCV·ffmpeg 어휘는 이식 대상이 아니다 |
| `11-exe-app-features.md` | **기능별 실제 동작·엣지 케이스·과거 결함 이력**. 화면별 절이 [03](./03-screens-spec.md)과 1:1 대응 | §11(INI 경로)·§16(표시 모드)·§17 로그 폴더·§18(외부 파일 버전)·§15(외부 파일 브랜딩)은 **Windows 전용** |
| `12-exe-app-settings-and-config.md` | 설정 항목 전수·기본값·Clamp 세부(§1.3) | INI·3단 폴백·표시 모드·창 기하는 이식하지 않는다 |
| `20-frontend-web-download-page.md` | **P1 소비자 클라이언트의 완성 구현** — 웹 앱이 만드는 문서를 소비하는 쪽 | 웹 앱은 이 페이지를 **변경하지 않는다** |
| `70-logging-and-troubleshooting.md` | 증상→원인 매핑의 사고 방식 | 로그 문자열·경로는 Windows 전용 |
| `80-build-and-deployment.md` | 게이트 키 주입 방식의 의도 | 단일 EXE·Inno Setup은 무관 |

---

## 3. 설계 문서 (`docs/design/`)

`docs/design/README.md`가 인덱스다. **왜 그렇게 결정했는지**를 담는다.

| 문서 | 웹에서 볼 이유 |
|------|----------------|
| **`multiplatform-client-architecture.md`** | **계층 구조·계층 규칙(§2)** · 도메인에 들어가야 하는 것(§2.2) · 어댑터 인터페이스 목록(§2.3) · 코드 공유·드리프트 방지 장치(§3.1) · **§4.3 웹 범위 판정(이 폴더가 대체)** · §7.1 개인 프레임 서버 저장 비용 분석 |
| `firebase-contract.md` | 생산자↔소비자 계약. Storage 경로·토큰 URL·다운로드 페이지 URL·요금제 전제 |
| `wpf-backend-proxy-migration-design.md` | 업로드 3단계·게이트 구조의 **근거**(왜 서명 URL 직접 PUT인가) |
| `wpf-it13-temp-user-role-design.md` | TempUser 한도를 **서버가 담보하는 이유**(prepare 선검사 + commit 트랜잭션) |
| `wpf-it15-google-only-auth-design.md` | 비밀번호 폐지·SSO 단일화·**와이어 형식 동결**·PIN 완화 근거 |
| `wpf-it14-settings-pin-gate-design.md` | PIN 게이트 fail-closed 규약, **서버 잠금을 채택하지 않은 이유(DoS)** |
| `wpf-it16-advanced-user-role-design.md` | `CanWriteFrames`를 `IsPower`와 **분리한 근거** |
| `wpf-it15-frame-ux-design.md` | 프레임 편집 **로컬 전용 정책**·사본 분기·기존 프레임 불러오기 근거 |
| `wpf-frame-edit-completion-design.md` | 슬롯 배치·좌표 변환 설계 |
| `web-architecture.md` · `web-wbs.md` | **기존 P1 다운로드 페이지**의 설계·WBS(웹 앱과 다른 산출물) |
| `wpf-architecture.md` | Windows 전체 아키텍처 |

### 3.1 ⚠️ 설계 문서 유효성 주의 (`docs/design/README.md §4`)

| 주의 | 내용 |
|------|------|
| **폐기된 서술** | it15 이전 문서에는 **id/pw 로그인·비밀번호·시드 계정·서비스 계정 키·`MCPhoto.Firebase` 직결**이 남아 있다. **전부 이력이며 현행이 아니다.** 웹에 이식하면 보안 회귀다 |
| **미구현 설계** | `wpf-it11-deferred-features-design.md`의 **컷별 재촬영은 설계만 있고 미구현**이다(전체 재촬영만 구현). 웹도 만들지 않는다 |
| **결정 대기** | `docs/analysis/90 §1`이 미해결 항목의 **단일 진실**이다. 설계 문서의 아이디어가 확정을 뜻하지 않는다 |

---

## 4. Windows 소스 지도 — 화면·기능별

`src/MCPhoto.App/`(UI·화면 로직) · `src/MCPhoto.Core/`(도메인·계약) · `src/MCPhoto.Capture/`(미디어) · `src/MCPhoto.Http/`(백엔드).

### 4.1 화면 대응표 (웹 화면 → Windows 파일)

| 웹 화면 | Windows 화면 로직 | Windows 뷰 | 관련 Core |
|---------|-------------------|-----------|-----------|
| `Home` | `ViewModels/HomeViewModel.cs` | `Views/HomeView.xaml` | — |
| `Login` | `ViewModels/LoginGuestViewModel.cs` | `Views/LoginGuestView.xaml` | `Core/Accounts/{IAccountService,IGoogleSignInService,GoogleOAuthPkce}.cs` |
| `FrameSelect` | `ViewModels/FrameSelectViewModel.cs` | `Views/FrameSelectView.xaml` | `Core/Frames/{FrameEditPolicy,FrameOrigin,LocalFrameStore}.cs`, `App/Services/FrameCatalogService.cs` |
| `Guide` | `ViewModels/GuideViewModel.cs` | `Views/GuideView.xaml` | — |
| `Capture` | `ViewModels/CaptureViewModel.cs` | `Views/CaptureView.xaml` | `Core/Capture/{CaptureSession,PreviewReadiness,CropCalculator}.cs`, `Capture/OpenCvCameraService.cs` |
| `CutSelect` | `ViewModels/CutSelectViewModel.cs` | `Views/CutSelectView.xaml` | `Core/Capture/CaptureSession.cs` |
| `Result` | `ViewModels/ResultViewModel.cs` | `Views/ResultView.xaml` | `Capture/{CompositionService,Filters,TimelapseService}.cs`, `Core/LocalSave/` |
| `Qr` | `ViewModels/QrPopupViewModel.cs` | `Views/QrPopupView.xaml` | `Core/Upload/{UploadService,UploadContract,QrService}.cs`, `Http/HttpFirebaseClient.cs` |
| `Done` | `ViewModels/DoneViewModel.cs` | `Views/DoneView.xaml` | — |
| `FrameEditor` | `ViewModels/FrameEditorViewModel.cs` | `Views/FrameEditorView.xaml(.cs)` | `Core/Frames/{SlotLayout,EditorTransform,FrameNaming,SlotAspect}.cs` |
| `Settings` | `ViewModels/SettingsViewModel.cs` | `Views/SettingsView.xaml` | `Core/Settings/{AppSettings,QrDeliveryPolicy}.cs` |
| `Account` | `ViewModels/AccountViewModel.cs` | `Views/AccountView.xaml` | `Core/Models/{User,UserRole}.cs`, `Http/HttpAccountService.cs` |
| `UserMgmt` | `ViewModels/UserMgmtViewModel.cs` | `Views/UserMgmtView.xaml` | `Core/Models/{UserRole,RoleChangePolicy}.cs` |
| 카메라 테스트 모달 | `ViewModels/CameraTestViewModel.cs` | `Views/CameraTestWindow.xaml` | `App/Services/CameraTestDialogService.cs` |
| 진단 모달 | `ViewModels/DiagnosticsViewModel.cs` | `Views/DiagnosticsWindow.xaml` | `App/Services/{DiagnosticsDialogService,LogFolderService}.cs` |
| PIN 모달 | — | `Views/PinPromptWindow.xaml(.cs)` | `App/Services/PinPromptDialogService.cs` |
| 프레임 피커 모달 | `ViewModels/FramePickerViewModel.cs` | `Views/FrameEditorView.xaml`(내부 오버레이) | — |
| **앱 셸**(상단바·유휴·전이) | **`AppShellViewModel.cs`** | `MainWindow.xaml(.cs)` | `Core/Navigation/{SessionStateMachine,IdleWatchdog,IdleCountdown}.cs` |
| 세션·토큰 홀더 | `SessionContext.cs`, `Services/BackendSessionSynchronizer.cs` | — | `Http/Session/{IBackendSession,BackendSession}.cs` |
| 부트스트랩·DI | `App.xaml.cs`, `ServiceRegistration.cs` | — | — |

### 4.2 순수 로직 파일 (웹 도메인 이식 대상 — 최우선)

전체 표는 [01 §2.2](./01-tech-stack-and-structure.md)에 있다. 읽는 팁만 여기 남긴다.

| 파일 | 읽을 때 주의 |
|------|--------------|
| `Core/Capture/CropCalculator.cs` | **정수 나눗셈**이 결과를 좌우한다. JS 변환은 [04 §9](./04-media-pipeline-web.md) 표 참조 |
| `Core/Frames/SlotLayout.cs` | `AutoArrange`·`ScaleSlots`·`ClampToFrame`·`IsValid` 4개. `ScaleSlots`는 **`_baseSlots` 기준**이 핵심 |
| `Core/Frames/EditorTransform.cs` | 순수함수화된 좌표 변환. **표시·드래그·클램프가 이것 하나를 공유**해야 WYSIWYG가 성립 |
| `Core/Frames/FrameEditPolicy.cs` | `CanDelete`가 **소유자를 보지 않는 이유**가 주석에 있다(그대로 유지) |
| `Core/Frames/FrameNaming.cs` | 사본 이름 1~99 → 난수, base 되돌림 규칙 |
| `Core/Models/UserRole.cs` | `IsPower`/`CanWriteFrames`/`CanManage`/**`CanResetPin`**/`ManageRank`. 두 축 분리 주석 필독 |
| `Core/Models/RoleChangePolicy.cs` | 서버 `canSetRole`과 **1:1 대칭**. 순서(위계 오름차순)도 규격 |
| `Core/Upload/UploadContract.cs` | 세션 ID·경로·토큰 URL·다운로드 페이지 URL·만료 계산 |
| `Core/Upload/UploadService.cs` | 3단계 순서·최소 1개 불변식·진행률 합산 |
| `Core/Settings/AppSettings.cs` | `Clamp()`·`NormalizeBackend()`·`NormalizeQr()`. **두 URL 정규화 방향이 반대** + **자동 sentinel(0) 보정 제외 가드**(it17) |
| `Core/Settings/CutCountPolicy.cs` | 자동 컷 수 해석(it17): `IsAuto`(0만)·`Resolve`(자동 `max(6,슬롯+2)` / 고정 `max(설정,슬롯)`). 해석 지점은 `CaptureSession.Begin` 1곳 |
| `Core/Settings/QrDeliveryPolicy.cs` | `Normalize`/`OnReEnabled` |
| `Core/Settings/QrEffectivePolicy.cs` | **런타임 QR on/off 단일 지점.** 미로그인 → false, TempUser 한도 초과 → false, 그 외 raw. **raw 설정을 절대 write하지 않는다.** 호출부는 `ResultViewModel.Next`(`:149`) 1곳 |
| `Core/Capture/PreviewReadiness.cs` | Ready 3조건 + 1회 신호 |
| `Core/Capture/FfmpegArgs.cs` | `ComputeSpeedFactor`만 이식(ffmpeg 인자는 무관) |
| `Core/Navigation/SessionStateMachine.cs` | 전이표·판정 순서 |
| `Capture/Filters.cs` | 필터 3종의 정확한 연산(alpha·beta·블렌드 비율) |
| `Capture/CompositionService.cs` | 합성 절차·슬롯 클램프(편집기용과 **식이 다름**) |
| `Core/Frames/LocalFrameStore.cs` | `.slots` 포맷·`#dbid` 규약·공용/개인 접두·삭제 성공 판정 |
| `Core/Settings/DisplayApplyPolicy.cs` | **이식하지 않는다**(창 개념 없음) |

---

## 5. 백엔드 소스 지도 (`web/functions/src/`)

| 파일 | 내용 | 웹에서 볼 이유 |
|------|------|----------------|
| `app.ts` | Express 앱 조립 · 라우터 6개 · **CORS `origin:true`** · 404 핸들러 | 브라우저 호출 가능 근거 |
| `config.ts` | 환경변수·시크릿(`JWT_SECRET`·`CLIENT_API_KEYS`·`STORAGE_BUCKET`·`GOOGLE_OAUTH_CLIENT_ID`…) | [08 §4](./08-server-and-infra-prerequisites.md) 변경 지점 |
| `http/auth.ts` | `requireApiKey`·`requireBearer`·**`optionalBearer`**·`requirePower`·`requireAdmin` | 게이트 동작 확인 |
| `http/errors.ts` | 에러 봉투·상태코드 매핑 | [06 §3](./06-backend-integration-web.md) |
| `routes/auth.ts` | `POST /auth/google` | 로그인 계약 |
| `routes/accounts.ts` | 계정·PIN·역할 라우트(**`me/pin*`이 `:id/pin`보다 먼저 등록**) | [07](./07-auth-and-permissions-web.md) |
| `routes/config.ts` | 전역 TempUser 한도 | `Account`(Admin) |
| `routes/frames.ts` | 프레임 CRUD(**`POST /frames`가 `userId=null,isDefault=true` 하드코딩**) | [03 §11.3](./03-screens-spec.md) |
| `routes/uploads.ts` | prepare/commit | [06 §4](./06-backend-integration-web.md) |
| `routes/health.ts` | `/health` + `deployedAt` | 진단 |
| `domain/validation.ts` | 입력 검증 전수 · **`validateLoopbackRedirectUri`**(B1 변경 지점) | [08 §4.1](./08-server-and-infra-prerequisites.md) |
| `domain/roles.ts` | `MANAGE_RANK`·`isPower`·`canManage`·**`canResetPin`**·`canSetRole` — **클라와 1:1 대칭** | 역할 로직 대조 |
| `domain/session.ts` | 세션 ID·Storage 경로·토큰 URL 조립(**클라와 대칭**) | [06 §5](./06-backend-integration-web.md) |
| `domain/jwt.ts` | HS256·클레임·만료 | [07 §4](./07-auth-and-permissions-web.md) |
| `domain/tempUserLimit.ts` | 한도 판정식(`>=` 경계) | 한도 안내 |
| `services/googleAuth.ts` | code 교환·id_token 검증(**`assertPayloadAndExtractEmail`** — B2 변경 지점) | [08 §4.2](./08-server-and-infra-prerequisites.md) |
| `services/accounts.ts` | 계정 자동 생성·PIN·cascade 삭제 | 계정 동작 |
| `services/frames.ts` | 프레임 저장·삭제·`deleteAllFramesByUser` | 프레임 동작 |
| `services/uploads.ts` | prepare·commit·**URL 소속 검증**·TempUser 트랜잭션 | [06 §4.3](./06-backend-integration-web.md) |
| `services/signing.ts` | V4 서명 URL·다운로드 토큰 메타 | `requiredHeaders` 근거 |
| `services/dto.ts` | **Firestore 문서 형태(저장 키의 진실원)** | 스키마 |
| `scripts/migrate-google-only-accounts.mjs` | admin 부트스트랩·**`--clear-pin`(PIN 분실 복구)** | [09 §7](./09-kiosk-operations.md) |

---

## 6. 테스트·인프라 파일

| 위치 | 내용 | 웹에서 쓸 곳 |
|------|------|--------------|
| `tests/MCPhoto.Tests/*.cs` (721개) | 순수 로직 테스트 — **공유 벡터 추출원** | [10 §2·§3](./10-testing-and-acceptance.md) 표에 파일별 대응 |
| `web/functions/src/__tests__/*.test.ts` | 서버 Jest(게이트 회귀 `authGates.test.ts` 포함) | [08 §4](./08-server-and-infra-prerequisites.md) 회귀 테스트 추가 지점 |
| `web/tests/rules.test.js` | Firestore/Storage 규칙 Emulator 테스트 | P1 관련(웹 앱은 규칙을 쓰지 않는다) |
| `web/firebase.json` | Hosting·규칙·Functions·Emulator 구성 | [01 §5.1](./01-tech-stack-and-structure.md) kiosk 블록 추가 |
| `web/firestore.rules` · `web/storage.rules` | 보안 규칙(웹 SDK 경로 차단) | **변경 불필요**(앱은 SDK를 쓰지 않는다) |
| `web/OPS-ttl.md` | GCS Lifecycle·Firestore TTL 운영 절차 | 만료 정책 이해 |
| **`web/OPS-cors.md`** | 버킷 CORS **실측 판정**(다운로드 GET = 서비스 레벨 `ACAO:*`로 불필요 / **업로드 PUT = 필요**) + 컨틴전시 절차 | [08 §5](./08-server-and-infra-prerequisites.md)의 전제 |
| `web/deploy-web.bat` | P1 배포 배치 | 참고(앱은 별 스크립트) |
| `publish.ps1` · `installer/MCPhoto.iss` | Windows 배포 | 웹과 무관 |
| `docs/templates/WBS_BLUEPRINT.md` | WBS 작성 형식 | [11](./11-wbs.md)이 준수 |
| `docs/prd/photobooth-prd.md` | 원 PRD | 배경 이해 |
| `Example/` | 프레임·슬롯 설정 예시 이미지 | 디자인 참고(번들 프레임 자산은 별도 준비 필요) |

---

## 7. 작업별 읽기 레시피

**필요한 것만 읽는다.** 각 행의 문서·파일만 보면 그 작업이 가능하도록 골랐다.

| 만들려는 것 | 이 폴더 | `docs/analysis` | Windows 소스 |
|-------------|---------|-----------------|--------------|
| 프로젝트 셋업·배포 | [01](./01-tech-stack-and-structure.md), [08](./08-server-and-infra-prerequisites.md) | — | `web/firebase.json` |
| 도메인 이식 | [01 §2.2](./01-tech-stack-and-structure.md), [04 §9](./04-media-pipeline-web.md), [10 §2·§3](./10-testing-and-acceptance.md) | 13 §13, 14 §3·§4, 41 §2.1, 60 §1 | §4.2 표의 파일 전부 + `tests/MCPhoto.Tests` |
| 앱 셸·상태머신 | [02](./02-app-shell-and-navigation.md) | 13 §1·§2·§3·§7·§11 | `AppShellViewModel.cs`, `SessionContext.cs`, `Core/Navigation/*` |
| 카메라·프리뷰 | [04 §2~§4](./04-media-pipeline-web.md) | 14 §2·§3 | `Capture/OpenCvCameraService.cs`, `Core/Capture/PreviewReadiness.cs` |
| 촬영 시퀀스 | [03 §6](./03-screens-spec.md), [04 §5.1](./04-media-pipeline-web.md) | 13 §4.4, 14 §2.2 | `CaptureViewModel.cs`, `Core/Capture/CaptureSession.cs` |
| 컷 선택 | [03 §7](./03-screens-spec.md) | 13 §4.5 | `CutSelectViewModel.cs` |
| 합성·필터 | [04 §5·§6](./04-media-pipeline-web.md), [10 §4](./10-testing-and-acceptance.md) | 14 §5·§6 | `Capture/{CompositionService,Filters}.cs`, `tests/…/{CompositionTests,FiltersTests}.cs` |
| 타임랩스 | [04 §7](./04-media-pipeline-web.md) | 14 §7 | `Capture/TimelapseService.cs`, `Core/Capture/FfmpegArgs.cs` |
| 결과물 저장 | [05 §5](./05-storage-and-persistence.md) | 41 §5, 13 §4.7 | `Core/LocalSave/LocalSaveService.cs` |
| 업로드·QR | [06 §4](./06-backend-integration-web.md), [03 §9](./03-screens-spec.md) | 31 §5·§7, 13 §4.8 | `Core/Upload/*`, `Http/HttpFirebaseClient.cs` |
| 로그인 | [07 §2](./07-auth-and-permissions-web.md), [08 §3·§4](./08-server-and-infra-prerequisites.md) | 61 §2·§3.4·§4·§5, 31 §4.2 | `App/Services/GoogleSignInService.cs`, `Core/Accounts/GoogleOAuthPkce.cs`, `web/functions/src/{domain/validation.ts,services/googleAuth.ts}` |
| PIN 게이트 | [07 §6](./07-auth-and-permissions-web.md), [03 §15.3](./03-screens-spec.md) | 61 §7, 31 §4.5 | `AppShellViewModel.EnsurePinGateAsync`, `Views/PinPromptWindow.xaml.cs` |
| 설정 화면 | [03 §12](./03-screens-spec.md), [05 §2](./05-storage-and-persistence.md) | 13 §8, 41 §2, 12 §1 | `SettingsViewModel.cs`, `Core/Settings/AppSettings.cs` |
| 프레임 목록·카탈로그 | [03 §4](./03-screens-spec.md), [05 §4](./05-storage-and-persistence.md) | 13 §5, 41 §3, 31 §4.10 | `App/Services/FrameCatalogService.cs`, `Core/Frames/LocalFrameStore.cs` |
| 프레임 편집기 | [03 §11](./03-screens-spec.md) | 13 §6, 14 §4 | `FrameEditorViewModel.cs`, `Core/Frames/{SlotLayout,EditorTransform,FrameNaming,FrameEditPolicy}.cs` |
| 계정·사용자 관리 | [03 §13·§14](./03-screens-spec.md), [07 §5](./07-auth-and-permissions-web.md) | 13 §10, 60 §1·§2, 31 §4.3~§4.9 | `{AccountViewModel,UserMgmtViewModel}.cs`, `Core/Models/{UserRole,RoleChangePolicy}.cs` |
| 진단 모달 | [03 §15.2](./03-screens-spec.md) | 13 §9.2 | `DiagnosticsViewModel.cs` |
| 유휴 감시 | [02 §6](./02-app-shell-and-navigation.md) | 13 §7 | `Core/Navigation/{IdleWatchdog,IdleCountdown}.cs` |
| 오류·실패 정책 | [06 §3](./06-backend-integration-web.md), [02 §9](./02-app-shell-and-navigation.md) | 13 §11, 31 §3, 60 §4.5 | `Http/HttpBackendClient.cs`(`MapToDomainException`) |
| 운영·배포 | [09](./09-kiosk-operations.md) | 50, 80(참고) | `web/OPS-ttl.md` |
| 테스트 | [10](./10-testing-and-acceptance.md) | 05 §11 | `tests/MCPhoto.Tests/*` |

---

## 8. 자주 헷갈리는 것 5가지

| # | 헷갈리는 지점 | 사실 |
|---|---------------|------|
| 1 | `HostingBaseUrl`을 kiosk 도메인으로 바꿔야 하나? | **아니다.** QR이 가리키는 **P1 다운로드 페이지 도메인**이다. kiosk로 바꾸면 QR이 앱을 연다 |
| 2 | 프레임 편집을 서버에 저장해야 하나? | **아니다.** `PUT /frames/{id}`는 **호출하지 않는다**(로컬 전용 정책). 공용 **신규 생성**만 `POST /frames` |
| 3 | 웹에 Firebase SDK를 넣어야 하나? | **아니다.** 앱은 백엔드 API + 서명 URL만 쓴다. Firestore·Storage SDK를 넣으면 계약 위반. **P1 페이지만** SDK를 쓴다 |
| 4 | 게이트 키가 없으면 아무것도 안 되나? | 관리 API(`/accounts`·`/config`)는 **Bearer만** 필요하다. 게이트 키가 필요한 것은 `/auth/google`·`/frames/default`·`/uploads/*` |
| 5 | 403이면 권한 문제인가? | **`error.code`를 봐야 한다.** `TEMP_USER_*`는 권한이 아니라 **무료 한도** 문제이고 문구가 다르다 |
| 6 | 서버가 게스트 업로드를 허용하니 게스트도 QR을 받나? | **아니다.** 서버 게이트(`optionalBearer`)와 **클라이언트 정책**은 별개다. `QrEffectivePolicy`가 미로그인이면 QR을 끄므로 **게스트는 `Result → Done`** 으로 끝난다([03 §8.1](./03-screens-spec.md)) |

---

## 9. 이 폴더 문서 목록 (빠른 링크)

| # | 문서 | 한 줄 |
|---|------|-------|
| — | [README](./README.md) | 진입·읽는 순서 |
| 00 | [범위와 결정](./00-scope-and-decisions.md) | 무엇을 만들고 무엇을 빼는가 · WD1~WD20 · 불변식 |
| 01 | [기술 스택과 구조](./01-tech-stack-and-structure.md) | 스택·폴더·CSP·배포 |
| 02 | [앱 셸과 내비게이션](./02-app-shell-and-navigation.md) | 상태머신·세션·유휴·전체화면 |
| 03 | [화면별 상세 명세](./03-screens-spec.md) | 13화면 + 6모달 |
| 04 | [미디어 파이프라인](./04-media-pipeline-web.md) | 카메라·합성·필터·타임랩스 |
| 05 | [저장·영속](./05-storage-and-persistence.md) | 설정·프레임·세션·결과물·로그 |
| 06 | [백엔드 연동](./06-backend-integration-web.md) | API·업로드 3단계·에러 |
| 07 | [인증·권한](./07-auth-and-permissions-web.md) | OAuth·JWT·PIN·역할 |
| 08 | [서버·인프라 선행 작업](./08-server-and-infra-prerequisites.md) | P0 6건·CORS·OAuth 확장 |
| 09 | [키오스크 운영](./09-kiosk-operations.md) | 브라우저 키오스크·권한·백업 |
| 10 | [테스트와 수락 기준](./10-testing-and-acceptance.md) | 벡터·골든·E2E·체크리스트 |
| 11 | [WBS](./11-wbs.md) | Step 0~17 실행 계획 |
| 12 | [Web ↔ Windows 차이 보고서](./12-web-vs-windows-differences.md) | **차이 전량 + 기능 추가 규칙** |
| 13 | (이 문서) | 소스·문서 참조 지도 |
