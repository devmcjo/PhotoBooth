# 10 · Exe 앱(WPF 데스크톱) 아키텍처

| 항목 | 내용 |
| --- | --- |
| 문서 | 10-exe-app-architecture.md |
| 범위 | MCPhoto Exe 앱(WPF/.NET 8)의 솔루션 구성·계층·MVVM/DI·상태머신·캡처 파이프라인·전역 예외/데이터 폴더 |
| 최종 업데이트 | 2026-07-29 (it15·it16 반영 — `MCPhoto.Firebase` → `MCPhoto.Http`, DI 등록·부트스트랩·계정 모드 갱신) |
| 관련 소스 경로 | `src/MCPhoto.App/**`, `src/MCPhoto.Core/Navigation/**`, `src/MCPhoto.Core/Capture/**`, `src/MCPhoto.Capture/**`, `MCPhoto.sln` |
| 갱신 규칙 | 프로젝트 참조 관계, DI 등록(`ServiceRegistration.cs`), 상태 enum/전이표(`SessionStateMachine.cs`), View↔VM 매핑(`App.xaml`), 캡처 스레딩 모델(`OpenCvCameraService`)이 바뀌면 이 문서를 갱신한다. |

관련 문서: [11 기능 상세](./11-exe-app-features.md) · [12 설정/구성/브랜딩](./12-exe-app-settings-and-config.md) · 인덱스 [README](./README.md)

> ⚠️ **이 문서는 Windows 데스크톱 구현 참조다.** WPF·.NET·OpenCvSharp·ffmpeg·`%ProgramData%` 같은 어휘는 **현재 구현의 선택**이며 시스템 규격이 아니다.
>
> **다른 플랫폼 클라이언트를 만든다면**: 진입은 [05 · 멀티플랫폼 가이드](./05-cross-platform-client-guide.md), 화면·상태 규격은 [13](./13-client-behavior-spec.md), 캡처·합성 알고리즘은 [14](./14-media-pipeline-spec.md), 로컬 저장 규약은 [41](./41-local-data-and-file-formats.md)이 진실원이다. 이 문서에서 얻을 것은 **계층 분리·DI·스레딩·리소스 해제의 검증된 구조**이며, 그 구조 자체는 어느 플랫폼에서도 유효하다.

---

## 1. 솔루션·프로젝트 구성과 의존 방향

`MCPhoto.sln`에는 5개 프로젝트가 있고, `src` 솔루션 폴더 아래 4개 + `tests`가 있다(`MCPhoto.sln:6-17`).

| 프로젝트 | TFM | 종류 | 주요 의존(PackageReference) | 역할 |
| --- | --- | --- | --- | --- |
| `MCPhoto.Core` | `net8.0` | 도메인 라이브러리(순수, WPF 비의존) | `Microsoft.Extensions.Logging.Abstractions`, `QRCoder` | 모델·상태머신·설정·프레임·업로드 계약·QR 등 플랫폼 무관 로직 |
| `MCPhoto.Capture` | `net8.0-windows` (`UseWPF`) | 캡처/합성 구현 | `OpenCvSharp4.Windows`, `OpenCvSharp4.WpfExtensions` | 카메라(OpenCV)·ffmpeg 녹화·타임랩스·합성·필터·폴백 프레임 |
| `MCPhoto.Http` | `net8.0` | 백엔드 API 클라이언트 | `Microsoft.Extensions.Http`, `Microsoft.Extensions.Logging.Abstractions` | 업로드/계정/프레임/한도의 HTTP 구상 + JWT 세션 홀더. **it15에서 `MCPhoto.Firebase`(Admin SDK 직결)를 대체** |
| `MCPhoto.App` | `net8.0-windows` (`WinExe`, `UseWPF`) | WPF 실행 파일(`AssemblyName=MCPhoto`) | `CommunityToolkit.Mvvm`, `Microsoft.Extensions.Hosting/DI/Logging`, `Serilog(.File)` | UI·ViewModel·셸·DI 부트스트랩·이미징 |
| `MCPhoto.Tests` | (tests) | 테스트 | — | 순수 로직·headless XAML 회귀 테스트 |

의존 방향(단방향, 도메인이 최하위):

```
MCPhoto.App  ──▶  MCPhoto.Capture  ──▶  MCPhoto.Core
     │                                     ▲
     ├───────────▶  MCPhoto.Http  ─────────┘
     └───────────────────────────────────▶ (Core)
```

- `MCPhoto.App.csproj` — App은 Core/Capture/Http를 모두 참조.
- `MCPhoto.Capture.csproj:17`, `MCPhoto.Http.csproj` — 둘 다 Core만 참조.
- Core는 어떤 프로젝트도 참조하지 않음(도메인 순수성). App·Capture·Http는 Core의 인터페이스(`ICameraService`, `ICompositionService`, `IFrameRepository`, `IUploadService`, `IFirebaseClient`, `IAccountService`, `ISettingsService`, `IBrandingService` 등)에만 의존하고, 구상은 DI로 조립된다.

계층 요약: **도메인(Core)** = 모델·상태 규칙·순수 로직(`SessionStateMachine`, `SlotLayout`, `EditorTransform`, `CropCalculator`, `PreviewReadiness`, `IdleCountdown`, `QrDeliveryPolicy`, `UploadContract`) + 서비스 인터페이스 + **백엔드 비의존 오케스트레이션**(`UploadService`·`QrService`는 it15에서 Core로 이관). **앱(App)** = MVVM·셸·DI·이미징·다이얼로그 서비스. **인프라(Capture/Http)** = 인터페이스 구상. 백엔드 계약 상세는 [30](./30-backend-firebase-integration.md).

### 1.1 번들 자산(App 빌드 산출물)

- `tools/ffmpeg/ffmpeg.exe`가 존재하면 출력·publish에 복사(녹화·타임랩스 필수, `MCPhoto.App.csproj:28-48`). ffprobe는 미사용이라 제외.
- 루트 `Frame/**`를 출력 `Frame/`으로 복사(번들 기본 프레임, `MCPhoto.App.csproj:50-55`).
- `branding.ini.sample`을 실행 폴더에 동봉(고객이 `branding.ini`로 리네임해 앱 이름 변경, `MCPhoto.App.csproj`).
- 버전·빌드 시각 표기는 **동봉 파일이 없다** — 어셈블리 버전 리소스(`Directory.Build.props`의 `<Version>`)와 exe 타임스탬프에서 읽는다(it18, 상세는 [12 §6](./12-exe-app-settings-and-config.md)).

---

## 2. MVVM · DI(Generic Host) · View↔VM 매핑

### 2.1 프레임워크

- MVVM는 `CommunityToolkit.Mvvm` 사용: VM은 `ObservableObject`를 상속하고 `[ObservableProperty]`(백킹 필드 자동 생성)·`[RelayCommand]`(커맨드 자동 생성)를 사용한다.
- 화면 VM 공통 기반은 `ViewModelBase`(`ViewModelBase.cs`): `ObservableObject` + `OnEnterAsync()`/`OnLeaveAsync()` 훅. 셸이 화면 진입/이탈 시 이 훅을 호출한다.
- 셸 VM(`AppShellViewModel`)과 라이브 프리뷰 VM(`PreviewViewModel`), 카메라 테스트 VM(`CameraTestViewModel`), 컷 썸네일(`CutThumbnail`)은 `ViewModelBase`가 아닌 `ObservableObject` 직접 상속(화면 스왑 대상이 아니거나 특수 수명).

### 2.2 부트스트랩(Generic Host)

`App.OnStartup`(`App.xaml.cs:26-82`)에서:

1. `DataFolder`(`%ProgramData%\MCPhoto`) 생성 + Serilog 파일 싱크 구성(일 롤링, 14일 보존, `App.xaml.cs:28-35`).
2. 이전 실행 잔재 세션 폴더 정리(`SessionWorkspace.CleanupOnStartup`, `App.xaml.cs:38-43`).
3. `Host.CreateDefaultBuilder()`로 DI 컨테이너를 조립하고 `ServiceRegistration.Register(services)` 호출(`App.xaml.cs:45-55`). 로깅은 `AddSerilog(dispose:true)`로 교체.
4. 전역 예외 핸들러 3종 등록(§5, `:58-60`).
5. 브랜딩 로드 후 `Resources["Branding.AppName"]`·`["Branding.Subtitle"]` 주입(창 생성 **전**이어야 `DynamicResource`가 최신값 반영, `App.xaml.cs:64-71`).
6. 기본 프레임 백그라운드 prefetch(fire-and-forget, 실패 무시 — 로컬 캐시가 목적, `App.xaml.cs:78`).
7. `MainWindow`를 DI에서 해결해 `Show()`(`App.xaml.cs:80-81`).

> ⚠️ **시드 계정 보장(`EnsureSeedAsync`)은 it15에서 삭제**됐다 — ID/PW 계정이 폐지되어 시드 개념 자체가 소멸했고, 최초 admin은 마이그레이션 스크립트가 부트스트랩한다(`App.xaml.cs:73-74` 주석, [60](./60-auth-accounts-and-roles.md)).

`App.Services`는 뷰에서 VM 해결에 쓰이는 서비스 프로바이더(`App.xaml.cs:22`), `App.Current`는 강타입 재정의(`App.xaml.cs:24`).

### 2.3 DI 등록과 ViewModel 수명

`ServiceRegistration.Register`(`ServiceRegistration.cs:29-93`)의 수명 정책:

| 등록 | 수명 | 근거 |
| --- | --- | --- |
| `MainWindow` | Singleton | 셸 창(`ServiceRegistration.cs:32`) |
| `IBrandingService`→`IniBrandingService`, `IBuildInfoService`→`AssemblyBuildInfoService` | Singleton | 시작 1회 확정(브랜딩은 ini 로드, 빌드 정보는 exe 자신에서 — it18) |
| `ICameraTestDialogService`, `IPinPromptDialogService`, `IDiagnosticsDialogService`, `ILogFolderService` | Singleton | 모달·유틸 서비스(`:40`, `:42`, `:49-50`) |
| `IGoogleSignInService`→`GoogleSignInService` | Singleton | Google SSO(시스템 브라우저 + loopback + PKCE, `:46`) |
| `ISettingsService`→`IniSettingsService` | Singleton | 설정 단일 소스. **백엔드 게이트 키 기본값은 exe 내장값 주입**(`:53-55`) |
| `ICameraService`→`OpenCvCameraService` | **Singleton** | 카메라 하드웨어·스레드 단일 소유(§4, `:58`) |
| `IExternalCamera`→`NullExternalCamera`, `IPhotoPrinter`→`NullPhotoPrinter` | Singleton | 외부 장치 스캐폴드(현재 no-op, `:62-63`) |
| `IIdleWatchdog`→`IdleWatchdog`, `AppShellViewModel` | Singleton | 유휴 감시·셸 상태머신(`:66-67`) |
| `ILocalSaveService`, `FfmpegRunner`, `ITimelapseService`, `ICompositionService` | Singleton | 상태 없는(또는 공유) 서비스(`:70`, `:73-74`, `:77`) |
| **백엔드 서비스 묶음** — `BackendSessionSynchronizer`/`IBackendSession`, `IFirebaseClient`→`HttpFirebaseClient`, `IFrameRepository`→`HttpFrameRepository`, `IAccountService`→`HttpAccountService`, `IQrUsageService`, `ITempUserLimitsService` | Singleton | `RegisterBackendServices`(`:81`, `:100-169`) + 명명 HttpClient `"backend"`(`:103-109`) |
| `IUploadService`→`UploadService`, `IQrService`→`QrService` | Singleton | Core 구현(백엔드 비의존, `:82-83`) |
| `ILocalFrameStore`→`LocalFrameStore` | Singleton, 루트=`AppContext.BaseDirectory\Frame`(`:86-87`) |
| `SessionContext`, `FrameCatalogService` | Singleton(`:90-91`) |
| **화면 VM 전부** | **Transient**(진입마다 새 인스턴스) | `RegisterScreens`: Home/LoginGuest/FrameSelect/Guide/Capture/CutSelect/Result/QrPopup/FrameEditor/FramePicker/Settings/UserMgmt/Account/Diagnostics (`DoneViewModel`은 완료 화면 폐지로 제거) |
| `PreviewViewModel` | Transient |

- 백엔드 접근은 **feature flag 분기 없이 HTTP 경로 하나**다 — it15에서 레거시 Admin SDK 직결이 폐지됐다(`ServiceRegistration.cs:80`). 각 Http 구현은 설정에서 `BackendApiKey`·`StorageBucket`을 주입받고, `configured`는 `BackendBaseUrl`이 비어 있지 않은지로 판정한다(`:118-128`).
- `BackendSessionSynchronizer`가 JWT 홀더를 **소유**하고 `IBackendSession`으로 노출한다 — 토큰이 존재할 수 있는 모든 시점에 로그아웃 구독이 살아 있도록 보장하는 배선이다(`:113-116`, [30 §3.1](./30-backend-firebase-integration.md)).

### 2.4 View↔VM 매핑(DataTemplate 화면 스왑)

`App.xaml`의 `Application.Resources`에 VM 타입→View `DataTemplate`이 선언되어 있다(`App.xaml:38-76`). 셸이 `CurrentViewModel`을 바꾸면 `MainWindow`의 `ContentControl`(`MainWindow.xaml:15-17`)이 해당 `DataTemplate`으로 View를 자동 해결·스왑한다. 매핑 목록:

`HomeViewModel→HomeView`, `LoginGuestViewModel→LoginGuestView`, `FrameSelectViewModel→FrameSelectView`, `GuideViewModel→GuideView`, `CaptureViewModel→CaptureView`, `CutSelectViewModel→CutSelectView`, `ResultViewModel→ResultView`, `QrPopupViewModel→QrPopupView`, `DoneViewModel→DoneView`, `FrameEditorViewModel→FrameEditorView`, `SettingsViewModel→SettingsView`, `UserMgmtViewModel→UserMgmtView`, `AccountViewModel→AccountView`.

`App.xaml`에는 공용 컨버터도 등록되어 있다(`App.xaml:21-35`; 상세는 §6). `CameraTestWindow`·`DiagnosticsWindow`·`PinPromptWindow`·프레임 선택 모달은 상태머신 화면이 아닌 모달 `Window`라 `DataTemplate` 매핑이 아니라 다이얼로그 서비스로 직접 생성된다(§4.6).

---

## 3. 화면 상태머신 · 오버레이 · 상단바

### 3.1 AppState 목록

`AppState`(`AppState.cs`) enum **12개**: `Home`, `Login`, `FrameSelect`, `Guide`, `Capture`, `CutSelect`, `Result`, `Qr`, `Settings`, `UserMgmt`, `FrameEditor`, `Account`.

정상 촬영 흐름: `Home → (Login 선택적) → FrameSelect → Guide → Capture → CutSelect → Result → (Qr) → Home`.

> ⚠️ **완료 화면(종전 `Done`)은 폐지됐다.** 세션 완료는 상태가 아니라 셸 동작이다 —
> `AppShellViewModel.CompleteSession()` = 홈 복귀 + 완료 토스트(비모달, 5초 자동 소멸 + [확인]).
> 상태를 되살리면 화면이 하나 다시 끼어든다. 회귀 방지: `AppStateTests.Done_State_Is_Retired`.

### 3.2 전이표(SessionStateMachine)

`SessionStateMachine`(순수 정적 클래스, `SessionStateMachine.cs`)이 전이 규칙을 담당한다. `Forward` 딕셔너리(`:12-27`)가 각 상태의 사용자 액션 진행 대상:

| From | 진행 가능(Forward) |
| --- | --- |
| Home | FrameSelect, Login, Settings |
| Login | FrameSelect, FrameEditor, Settings |
| FrameSelect | Guide, FrameEditor |
| Guide | Capture |
| Capture | CutSelect |
| CutSelect | Result, Guide(재촬영=세션 전체) |
| Result | Qr *(완료는 Home 특례 — QR 미사용 시 즉시 완료)* |
| Qr | *(없음 — [완료]는 Home 특례)* |
| Settings | Login, FrameEditor |
| UserMgmt | Account(관리자 도구 복귀) |
| FrameEditor | FrameSelect, Settings, Login |
| Account | UserMgmt |

`CanTransition(from,to)`(`:33-40`) 특례: `to`가 **Home/Settings/Login/Account**이면 어디서든 허용(오버레이성 진입/복귀). 그 외 자기 자신 전이는 거부. 나머지는 `Forward` 검증.

- `IsSessionActive(state)`(`:46-52`): 유휴 감시 대상 = FrameSelect/Guide/Capture/CutSelect/Result/Qr. **FrameEditor는 로그인 필수 능동작업이라 제외**, Settings/Login도 제외.
- `IsTopBarVisible(state)`(`:58-59`): **Capture·Qr에서만 숨김**(몰입/모달), 그 외 표시.

셸의 실제 전이는 `AppShellViewModel.NavigateInternalAsync`(`AppShellViewModel.cs:121-146`): 검증 실패 시 거부 로그 후 false. 이탈 화면의 `OnLeaveAsync` → 상태·VM 교체 → 유휴 감시 갱신 → 진입 화면 `OnEnterAsync` 순. 예외는 삼켜 로그(무인 안정성).

### 3.3 오버레이(설정/로그인/계정) vs 화면

Settings/Login/Account는 별도 화면이지만 **오버레이성 진입**으로 다룬다(진입 전 상태를 복귀 지점으로 보존):

- `NavigateToOverlayAsync(target)`(`AppShellViewModel.cs:152-157`): 현재 상태가 Settings/Login이 아니면 `_returnState`에 저장 후 전이.
- `ReturnFromOverlay()`(`:164-170`): 저장된 `_returnState`로 **검증 면제** 복귀(진입의 역방향은 항상 합법). 세션 데이터는 Reset하지 않고 보존.
- 계정 페이지는 단일 `AppState.Account` + **진입 모드**(`AccountMode` = **Account**(내 정보 + PIN 변경) / **Admin**(관리자 도구·전역 한도·앱 종료))로 UI 분기(상태 폭증 방지, `AccountViewModel.cs:13-20`). 모드는 팝오버 항목이 지정한 뒤 VM 생성 시 주입된다.
  - ⚠️ **it15 폐지**: `AccountMode.PasswordChange`·`AccountCreate`는 없다 — 비밀번호 개념과 계정 생성 UI가 제거됐고, 신규 계정은 Google SSO 최초 로그인 시 서버가 `temp_user`로 자동 생성한다([60 §1.5](./60-auth-accounts-and-roles.md), [11 §15](./11-exe-app-features.md)).
  - 계정·관리자 도구 진입은 **PIN 게이트**(`AppShellViewModel.EnsurePinGateAsync`)를 통과해야 한다(설정 진입과 동일 PIN·동일 다이얼로그, fail-closed).

상단바 계정 팝오버(`MainWindow.xaml:54-75`)는 오버레이가 아니라 `Popup`으로, 로그인 시 계정 항목(**계정 관리 / 관리자 도구(파워만) / 로그아웃**)을 연다.

### 3.4 상단 바 표시 규칙

상단 바는 `MainWindow.xaml:20-79`의 오버레이 `Grid`(Height 72, 상단 정렬). 표시 여부는 `IsTopBarVisible`(`AppShellViewModel.cs:69` → `SessionStateMachine.IsTopBarVisible`). 구성:

- 좌: 홈 버튼(`IsHome`이면 숨김, `MainWindow.xaml:31-36`) + 계정 버튼(`AccountLabel`: 비로그인="로그인", 로그인=계정 ID).
- 우: 설정 기어 버튼.
- 계정 상태는 `SessionContext`(단일 소스)에서 직접 읽고 `CurrentUserChanged` 구독으로 갱신(`AppShellViewModel.cs:60-67`, `:102-109`). 미러 상태 없음.

### 3.5 홈 복귀 · 유휴

- `ReturnHome(reason, clearUser=false)`(`AppShellViewModel.cs:202-210`): 어디서든 Home 복귀. 촬영 세션 데이터는 항상 폐기(`Session.Reset(clearUser)`), 유휴 감시 정지. `clearUser=true`(유휴·세션완료=다음 손님)일 때만 로그아웃.
- 유휴 감시: `IdleWatchdog`(`System.Threading.Timer` 기반, `IdleWatchdog.cs`)가 세션 활성 상태 진입 시 `IdleWarningSeconds`(=120초, 2분) 카운트다운. 무동작 시 `IdleTimeout` 이벤트(스레드풀) → `Dispatcher.BeginInvoke(ShowIdleWarning)`.
- **경고 오버레이**(`MainWindow.xaml:81-103`): 스크림 + 카드, "10초 후 메인 화면" 카운트다운(`IdleCountdown` 순수 로직, `IdleCountdown.cs`). [이어서 진행하기]=경고 해제+타이머 재시작, [메인 화면으로]=즉시 홈. 카운트다운 0 → `ReturnHome(clearUser:false)`. **로그아웃 절대 금지**(`AppShellViewModel.cs:260`, it8 A1).
- `NotifyUserActivity`(`:216-220`): `MainWindow`의 `PreviewMouseDown`/`PreviewKeyDown`(`MainWindow.xaml:11-12`)에서 호출. 경고 표시 중에는 무시(버튼으로만 해제).

---

## 4. 카메라 캡처 파이프라인 구조

핵심 계약은 `ICameraService`(`ICameraService.cs:9-45`). 구상은 `OpenCvCameraService`(OpenCV DirectShow). "하나의 스트림에서 프리뷰+스틸+녹화를 분기, 거울반전·중앙 크롭은 프레임당 1회"가 설계 원칙(`ICameraService.cs:5-8`).

### 4.1 단일 스트림 → 3분기

전용 백그라운드 캡처 스레드(`IsBackground`, 이름 `MCPhoto.Capture`)가 프레임을 1회 읽어 **거울(FlipMode.Y) → 중앙 크롭** 가공을 1회 적용한 뒤 3소비자에 분기한다(OpenCvSharp `VideoCapture`, 요청 1080p/MJPEG/30fps):

1. **프리뷰**: `FrameReady` 이벤트로 가공된 `CameraFrame`(BGR24 버퍼+stride) 발행.
2. **녹화**: `_recording`이면 ffmpeg stdin에 프레임 기록. 백프레셔 시 프레임 드롭 허용(프리뷰 우선).
3. **스틸**: 대기 중인 `CaptureStillAsync`의 `TaskCompletionSource`가 있으면 버퍼를 **복제**해 `CapturedStill`로 채움(단일 프레임 원자적 완료).

`CameraFrame`(`CameraFrame.cs`)·`CapturedStill`(`ICameraService.cs:47-53`)는 모두 BGR24 + Width/Height(+Stride). `CameraDevice`는 `record(int Index, string Name)`(`ICameraService.cs:55-56`).

> 가정: `OpenCvCameraService`의 정확한 프레임 획득/FPS 측정 라인 세부는 하위 에이전트 분석 기반이며, 위 동작(분기·스레드·가공 순서)은 인터페이스 계약(`ICameraService.cs:5-8`)과 소비자 코드(`CaptureViewModel`, `CameraFramePresenter`)와 일치한다.

### 4.2 프레임 렌더(CameraFramePresenter + WriteableBitmap)

`CameraFramePresenter`(`CameraFramePresenter.cs`)가 `FrameReady`를 재사용 `WriteableBitmap`으로 `Image`에 렌더한다(PreviewView·CaptureView·CameraTestWindow 공유):

- `Attach(camera)`(`:31-36`)로 이벤트 구독, `OnFrameReady`(`:45-54`)는 **캡처 스레드**에서 `_latest`에 최신 프레임 저장 + `_queued` 플래그로 **프레임 스킵**(렌더 대기 중 새 프레임은 덮어쓰기), `Dispatcher.BeginInvoke(Render, RenderLatest)`.
- `RenderLatest`(`:56-75`)는 **UI 스레드**에서 실행: 크기 변화 시에만 새 `WriteableBitmap`(Bgr24) 생성, 매 프레임 `WritePixels`(stride 반영). 매 프레임 새 BitmapSource 생성 금지(GC 압력 회피).

### 4.3 거울/크롭

- 거울: 캡처 스레드가 `Cv2.Flip(..., FlipMode.Y)`(좌우반전) 적용. 런타임 토글은 `SetMirror`(`ICameraService.cs:29`, volatile).
- 크롭: `CropCalculator.CenterCrop(srcW, srcH, targetAspect)`(`CropCalculator.cs`)가 대표 슬롯 종횡비로 중앙 크롭 ROI 산출(경계 클램프). `targetAspect`는 `SetTargetAspect`(`ICameraService.cs:32`)로 변경. WYSIWYG: 프리뷰=스틸=녹화가 동일 가공.

### 4.4 스틸 캡처

`CaptureStillAsync(ct)`(`ICameraService.cs:35`)는 즉시 `Task`를 반환하고 TCS를 큐잉, 다음 프레임에서 캡처 스레드가 픽셀 복제본으로 채운다. `CancellationToken`으로 취소 지원. `CaptureViewModel`이 컷마다 호출(`CaptureViewModel.cs:154`).

### 4.5 ffmpeg 녹화(stdin 파이프)

- `StartRecording(outputPath)`(`ICameraService.cs:38`) → `FfmpegRunner`가 rawvideo BGR24 stdin 파이프로 ffmpeg 프로세스 기동(30fps). 프레임은 stdin에 직접 write(단일 라이터라 락 불필요, 백프레셔 시 드롭).
- `StopRecordingAsync()`(`ICameraService.cs:41`) → stdin flush+close로 EOF 신호, 프로세스 종료 대기(moov atom 완성), 타임아웃 시 kill.
- 녹화 인자는 `FfmpegArgs.BuildRecordArgs`(Core, `FfmpegArgs.cs`)가 InvariantCulture로 조립. `FfmpegRunner`는 `IsAvailable`(ffmpeg.exe 존재)로 안전 가드.

### 4.6 ICameraService Singleton 이유·제약

- **Singleton 이유**: 카메라 핸들·백그라운드 스레드 같은 하드웨어 리소스를 단일 소유해야 한다. 실촬영(`CaptureViewModel`), 라이브 프리뷰(`PreviewViewModel`), 카메라 테스트 모달(`CameraTestViewModel`)이 **같은 인스턴스**를 공유(`ServiceRegistration.cs:38`). Transient VM(`PreviewViewModel`)은 카메라를 소유/Dispose하지 않고 서비스 계층(`StopAsync`)·컨테이너(앱 종료)가 수명 관리(`PreviewViewModel.cs:9-13`).
- **StartAsync 멱등성**: 이미 running이면 무시하고 즉시 true 반환. 그래서 다른 화면이 카메라를 열어 둔 상태로 진입할 수 있는 카메라 테스트 모달은 `StopAsync → StartAsync(선택 인덱스)`를 명시적으로 선행한다(`CameraTestViewModel.cs:45-53`, 주석 `:44`). 장치 미연결/열기 실패는 예외 대신 false(`ICameraService.cs:21-23`).
- **다이얼로그 서비스**: `CameraTestDialogService`(Singleton)가 `CameraTestWindow`를 생성·`ShowDialog`·닫힌 뒤 `StopAsync`로 확실 해제(`CameraTestDialogService.cs:28-44`). VM은 Window/Application 미참조(`ICameraTestDialogService`).

### 4.7 Ready 게이트(안정적 프리뷰)

`PreviewReadiness`(`PreviewReadiness.cs`)가 "안정적 실사용 가능" 판정: 기본 **8 연속 프레임 + 최소 500ms 경과 + fps>0** 셋을 모두 충족해야 `IsReady`(전이 시 1회 true). `CaptureViewModel.WaitForStablePreviewAsync`·`CameraTestViewModel`이 8초 타임아웃과 함께 사용(무한 로딩 방지). 시퀀스(카운트다운)는 Ready 이후에만 시작(`CaptureViewModel.cs:79-98`).

### 4.8 합성·타임랩스(요약)

- 합성: `ICompositionService.ComposeAsync(frame, cuts, filter, outPath)`(`ICompositionService.cs`) — 프레임 배경 로드 → 슬롯 index 정렬 → 컷별 필터 적용 → 슬롯 커버 크롭 → 슬롯 크기 리사이즈 → 배경에 오버레이 → 파일 기록(포맷은 확장자로 결정). 필터는 컷 전체 일괄(`Filters.Apply`, None/Grayscale/Brightness/Beauty).
- 타임랩스: `ITimelapseService.CreateTimelapseAsync`(`TimelapseService`) — 세션 길이로 배속(`FfmpegArgs.ComputeSpeedFactor`, 목표 10~15초) 산출 후 ffmpeg `setpts` 변환. ffmpeg 부재 시 null. 상세는 [11 기능](./11-exe-app-features.md) §타임랩스.

### 4.9 스레딩·리소스 해제

| 대상 | 스레드/모델 | 해제 |
| --- | --- | --- |
| 프레임 획득·가공·분기 | 전용 캡처 스레드(단일) | `StopAsync`가 스레드 join(타임아웃) |
| 프리뷰 렌더 | UI Dispatcher(`Render` 우선순위), `_queued` 프레임 스킵 | `Presenter.Dispose()`=`Detach()` |
| 녹화 stdin | 캡처 스레드(단일 라이터) | `StopRecordingAsync` flush+close, 실패 시 kill |
| 합성/타임랩스 | ThreadPool(async) | Mat `using` 스코프 |
| 런타임 설정(거울/종횡비/running) | volatile 필드 | — |
| 스틸/녹화 상태 전이 | 락 가드 | — |

`CaptureViewModel.OnLeaveAsync`(`CaptureViewModel.cs:207-213`)는 세션/카운트다운 취소 → `StopRecordingAsync`(예외 무시) → `StopAsync`로 이탈 시 확실 정지.

---

## 5. 전역 예외 처리 · 데이터 폴더

### 5.1 전역 예외(무인 안정성)

`App.OnStartup`에서 3종 핸들러 등록(`App.xaml.cs:58-60`):

- `DispatcherUnhandledException`(UI 스레드): `e.Handled=true`(크래시 방지) 후 `TryReturnHome`(`App.xaml.cs:93-98`, `:111-122`) → `AppShellViewModel.ReturnHome("전역 예외 복구")`.
- `AppDomain.CurrentDomain.UnhandledException`: 로그만(`:100-103`).
- `TaskScheduler.UnobservedTaskException`: 로그 + `SetObserved()`(`:105-109`).

화면 전이(`NavigateInternalAsync`)와 각 VM 커맨드도 예외를 삼켜 로그하거나 상태 메시지로 노출(성공 오인 금지) → 키오스크 무인 동작 중 크래시 대신 홈 복귀/안내. `OnExit`(`:124-129`)에서 `Log.CloseAndFlush()` + `_host.Dispose()`.

### 5.2 데이터 폴더(쓰기 가능·Program Files 회피)

`App.DataFolder = %ProgramData%\MCPhoto`(`App.xaml.cs:17-19`). 하위:

- `logs/` — Serilog 일 롤링(`mcphoto-.log`, 14일).
- `sessions/{guid}/` — 세션 임시 산출물(`session.mp4`, `timelapse.mp4`, `final.{ext}`). 시작 시·세션 종료(`SessionContext.Reset`)·유휴 시 정리. `result`·`logs`는 정리 제외(`SessionWorkspace`).
- `cache/fallback_frame.png` — fallback 프레임 캐시(`FrameCatalogService.cs:38`).

설정 INI·브랜딩·로컬 프레임(`Frame/`)·로컬 결과 저장 경로는 별도(상세는 [12 설정/구성](./12-exe-app-settings-and-config.md)).

---

## 6. 컨버터 · 테마

### 6.1 컨버터(`Converters/CommonConverters.cs`)

`App.xaml:21-35`에 등록되어 바인딩에서 참조. 표시/색/판정 로직:

| 컨버터 | 용도 |
| --- | --- |
| `BoolToVisibilityConverter`/`InverseBoolToVisibilityConverter`/`InverseBoolConverter` | bool↔Visibility/반전 |
| `NullToVisibilityConverter` | null=Visible(placeholder) |
| `BoolToBrushConverter` | 선택 테두리(Brush.Accent, 테마 토큰) |
| `BoolToNoticeBrushConverter` | 저장 안내 색(true=Danger, false=Success) |
| `CameraStateToVisibilityConverter` | `CameraLoadState`==파라미터면 Visible(로딩/오류 오버레이) |
| `SlotAspectLabelConverter` | SlotAspect→"4:3"/"3:4"/"1:1" |
| `AspectRatioToHeightConverter` | 종횡비→높이(썸네일 컨테이너 WYSIWYG) |
| `StartsWithToVisibilityConverter` | 문자열 접두 일치 시 Visible |
| `FilePathToImageConverter` | 파일 경로→`BitmapImage`(OnLoad+IgnoreImageCache — **파일 잠금 방지**, 프레임 삭제 실패 회피) |
| `RoleLabelConverter` | `UserRole`→한글 라벨("임시 유저"/"사용자"/"고급 유저"/"매니저"/"관리자") |
| `FrameDeleteVisibilityConverter`(멀티) | 프레임 삭제 ✕ 노출: 게스트/번들/fallback/빈 Id=숨김, `local:`=본인 노출, 공용/DB=파워만 |
| `RoleActionVisibilityConverter`(멀티) | 사용자 관리 액션 노출: 대상이 행위자와 같거나 낮은 역할일 때만(형식 불일치면 Collapsed) |
| `AllTrueToVisibilityConverter`(멀티) | 모두 참일 때만 Visible |

### 6.2 테마(`Themes/*`)

`App.xaml:12-14`가 `Themes/Theme.xaml` 하나만 병합. `Theme.xaml`은 순서대로 `Colors → Brushes → Typography → Metrics → Controls`를 병합한다(`Theme.xaml:8-14`; Brushes는 Colors 참조, Metrics는 Color.Shadow 참조, Controls는 전부 참조). 자체 병합 구조인 이유: WPF 병합 딕셔너리 간 `StaticResource` 교차 참조 제약을 피하기 위함(라이트 테마, Direction A).

---

## 7. 참고

- 반영된 설계 결정 요약은 `docs/design/wpf-architecture.md`(전체 아키텍처)와 최근 이터레이션 설계에 근거한다: `wpf-it10-*`(서버 연동) · `wpf-it11-*`(재촬영·진단·업로드 진행률) · `wpf-it12-*` · `wpf-it13-temp-user-role-design.md` · `wpf-it14-settings-pin-gate-design.md` · `wpf-it15-{google-only-auth,frame-ux}-design.md` · `wpf-it16-advanced-user-role-design.md` · `wpf-backend-proxy-migration-design.md`(백엔드 프록시 전환) · `wpf-google-sso-design.md`.
- 과거 이터레이션(it2~it9) 설계 문서는 완료되어 제거됨(git 이력 참조). 본 문서는 실제 소스 기반으로 작성했으며, 세부 동작은 실제 소스가 진실의 소스다.
