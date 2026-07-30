# 12 · Exe 앱 설정 · 구성 · 기본값 · 브랜딩

| 항목 | 내용 |
| --- | --- |
| 문서 | 12-exe-app-settings-and-config.md |
| 범위 | `AppSettings` 전 항목·기본값·범위, INI 저장/폴백/신뢰성, 브랜딩(branding.ini), 빌드 정보(bldinfo.ini), 표시 모드 즉시 적용, 창 위치 저장 |
| 최종 업데이트 | 2026-07-29 (it15·it16 반영 — 백엔드/SSO 설정 키 추가, 브랜딩 기본값·샘플 정정) |
| 관련 소스 경로 | `src/MCPhoto.Core/Settings/**`, `src/MCPhoto.Core/Branding/**`, `src/MCPhoto.Core/Build/**`, `src/MCPhoto.App/branding.ini.sample`, `src/MCPhoto.App/bldinfo.ini`, `src/MCPhoto.App/MainWindow.xaml.cs`, `src/MCPhoto.App/ServiceRegistration.cs` |
| 갱신 규칙 | `AppSettings` 필드 추가/기본값/Clamp 변경, INI 매핑(`IniSettingsService`) 변경, 폴백 경로(`SettingsPathResolver`) 변경, 브랜딩 로직/기본값 변경 시 이 문서를 갱신한다. |

관련 문서: [10 아키텍처](./10-exe-app-architecture.md) · [11 기능 상세](./11-exe-app-features.md) · 인덱스 [README](./README.md)

> ⚠️ **이 문서는 Windows 데스크톱 구현 참조다.** INI 파일 형식·3단 경로 폴백(`실행경로 → %ProgramData% → %LocalAppData%`)·`branding.ini`·`bldinfo.ini`·표시 모드/창 기하는 **Windows 고유 구현**이며 이식 대상이 아니다.
>
> **다른 플랫폼 클라이언트를 만든다면 [41 · 로컬 데이터·파일 포맷 규격](./41-local-data-and-file-formats.md)이 진실원이다** — 설정 **키 이름·기본값·범위·보정 규칙**은 계약이지만 **저장 형식·위치는 플랫폼 자유**다. 41번에 플랫폼별 저장 위치 대응표와 프레임 `.slots` 파일 포맷이 있다.

---

## 1. AppSettings 전 항목

정의: `AppSettings`(`AppSettings.cs`). INI 매핑: `IniSettingsService.ReadInto`(`:136`)/`WriteFrom`(`:174`). **INI 섹션명은 모두 `[MCPhoto]`**(`IniSettingsService.cs:11`). 대부분 INI 키는 프로퍼티명과 동일(`nameof`), 예외는 `WindowBounds` 4개(`WindowLeft/Top/Width/Height`).

값 범위·옵션 제약은 `AppSettings.Clamp()`(`:157-180`)가 로드/저장 시 강제한다.

| 키(프로퍼티) | 타입 | 기본값 | 범위·Clamp | INI 키 | 영향 |
| --- | --- | --- | --- | --- | --- |
| `CutCount` | int | **6** | 허용 {6,8,10} **또는 `0`=자동**; 벗어나면 가장 가까운 허용값(동률 시 첫 값)으로 보정. **`0`은 최근접 보정에서 제외**(sentinel 보존, it17) — `-1` 등 다른 값은 종전대로 6으로 보정 | `CutCount` | 촬영 컷 수의 **의도**. 실제 촬영 컷 수는 프레임 슬롯 수 확정 후 `CaptureSession.Begin`이 산출(§1.4) |
| `CountdownSec` | int | **6** | 허용 {3,6,8,10}; 최근접 보정 | `CountdownSec` | 컷당 카운트다운 초 |
| `MirrorMode` | bool | **true** | — | `MirrorMode` | 거울(좌우반전) 프리뷰=저장 WYSIWYG |
| `FlashMode` | bool | **false** | — | `FlashMode` | 셔터 직전 하양 화면 플래시 |
| `ShutterSound` | bool | **false** | — | `ShutterSound` | 촬영 순간 셔터 효과음(`Assets\shutter.wav` 있으면 사용, 없으면 합성음) |
| `RetakeEnabled` | bool | **false** | — | `RetakeEnabled` | 재촬영 사용(상위). off면 재촬영 UI 전부 미노출 (it11 #13) |
| `RetakeLimit` | int | **1** | 허용 {1,2,3}; 최근접 보정 | `RetakeLimit` | 전체 재촬영 횟수 상한(RetakeEnabled on일 때만 의미) (it11 #13) |
| `OutputFormat` | enum `OutputFormat` | **Jpg** | {Jpg, Png} | `OutputFormat` | 최종 이미지 포맷/확장자 |
| `RetentionHours` | int | **24** | `Math.Clamp(1, 72)` | `RetentionHours` | 업로드 결과물 보관(만료) 시간, QR 고지 문구 |
| `EnableQrDelivery` | bool | **true** | QR 정규화(하위 둘 다 off면 false) | `EnableQrDelivery` | QR 전송(업로드+QR+다운로드 페이지) on/off |
| `SendPhoto` | bool | **true** | QR 정규화 대상 | `SendPhoto` | QR 전송에 사진(최종 합성) 포함 |
| `SendTimelapse` | bool | **true** | QR 정규화 대상 | `SendTimelapse` | QR 전송에 타임랩스 포함 |
| `FilterGrayscale` | bool | **true** | — | `FilterGrayscale` | 결과 화면 "흑백" 필터 노출 |
| `FilterBrightness` | bool | **true** | — | `FilterBrightness` | 결과 화면 "밝게" 필터 노출 |
| `FilterBeauty` | bool | **true** | — | `FilterBeauty` | 결과 화면 "뷰티" 필터 노출 |
| `SaveLocalCopy` | bool | **true**(오늘 확정) | — | `SaveLocalCopy` | 결과물 로컬 저장 on/off(QR과 독립) |
| `LocalSavePath` | string | **""**(빈 값) | — | `LocalSavePath` | 빈 값이면 런타임 `{실행경로}\result` |
| `DisplayMode` | enum `DisplayMode` | **Windowed**(오늘 확정, 개발용) | {Fullscreen, Windowed} | `DisplayMode` | 창 표시 모드(§4) |
| `WindowBounds` | `WindowBounds` | Left/Top=NaN, Width=1280, Height=720 | Width≥1280, Height≥720; Left/Top는 미클램프 | `WindowLeft`,`WindowTop`,`WindowWidth`,`WindowHeight` | 창모드 크기·위치(§5) |
| `CameraDevice` | int | **0** | 음수면 0 | `CameraDevice` | 사용할 웹캠 장치 인덱스 |
| `HostingBaseUrl` | string | **`https://mcphoto-955fb.web.app`**(운영 기본값 내장) | 트레일링 `/` 제거 | `HostingBaseUrl` | 다운로드 페이지 URL 조립 base |
| `StorageBucket` | string | **`mcphoto-955fb.firebasestorage.app`**(운영 기본값 내장) | — | `StorageBucket` | 토큰 URL 조립용 버킷명. 실제 값은 업로드 prepare 응답의 `bucket`으로 갱신된다([30 §5.3](./30-backend-firebase-integration.md)) |
| `ExternalCameraEnabled` | bool | **false** | — | `ExternalCameraEnabled` | 외부 카메라(DSLR) — **미지원 스캐폴드**(INI 저장만, 실기능 미배선) |
| `PhotoPrinterEnabled` | bool | **false** | — | `PhotoPrinterEnabled` | 사진 프린터 — **미지원 스캐폴드**(동상) |
| `BackendBaseUrl` | string | **`https://asia-northeast3-mcphoto-955fb.cloudfunctions.net/api`**(운영 기본값 내장) | 트림 + 트레일링 `/` **부여**(`NormalizeBackend`) | `BackendBaseUrl` | 백엔드 API 주소. 빈 값이면 백엔드 미구성(업로드·로그인 불가) |
| `BackendApiKey` | string | **""** — 실제 기본값은 **exe 내장 키**(`AssemblyMetadata "MCPhoto.BackendApiKey"`)를 로드 시 주입 | 트림 | `BackendApiKey` | 배포 게이트 키(`X-MCPhoto-Client`). ⚠️ INI에 평문 — 유출 시 서버에서 해당 키만 폐기 |
| `GoogleClientId` | string | 운영 프로젝트 Desktop 클라이언트 ID 내장 | 트림 | `GoogleClientId` | Google SSO authorize URL 조립. **빈 값이면 로그인 화면의 "Google로 로그인" 버튼을 숨김**(SSO opt-out) |

보조 상수(`AppSettings.cs:38-44`): `AllowedCutCounts={6,8,10}`, `AllowedCountdownSecs={3,6,8,10}`, `AllowedRetakeLimits={1,2,3}`, `MinRetentionHours=1`, `MaxRetentionHours=72`, `MinSlots=1`, `MaxSlots=6`.

자동 컷 수 상수(`CutCountPolicy.cs`, it17): `AutoCutCount=0`(sentinel), `AutoMinimum=6`, `AutoMargin=2`. ⚠️ `AutoCutCount`는 **`AllowedCutCounts`에 넣지 않는다** — 넣으면 `CutCount=3` 같은 오입력이 6이 아니라 0(자동)으로 보정된다(|3-0|=|3-6| 동률 → 첫 값 승리).

> **it12 R1 — 로그인 전용 편집(게이트)**: 게스트(비로그인) 설정 화면에서 `MirrorMode`·`RetakeEnabled`·`RetakeLimit`·`FilterGrayscale`/`FilterBrightness`/`FilterBeauty`·`EnableQrDelivery`(+`SendPhoto`/`SendTimelapse`)·`HostingBaseUrl`·`StorageBucket`는 OFF 표시·컨트롤 비활성 + "로그인 필요" 인라인 노티 상시 표시(R3, hover 툴팁에서 개정)이며 저장 시 미기록(ini 원값 보존=클로버 금지). 게이트는 `SettingsViewModel`(VM)에만 존재 — `AppSettings` 모델은 전 필드 항상 직렬화되고, 촬영/필터 런타임은 `Settings.Current`(ini=관리자값)대로 동작한다(편집 권한만 제한, 기능은 불변).

### 1.1 코드에 내장된 기본값(운영자 INI 입력 불요)

- `DisplayMode = DisplayMode.Windowed` — **개발 기간 기본**(배포 시 Fullscreen으로 되돌릴 것; `AppSettings.cs:102-103` 주석).
- `SaveLocalCopy = true` — QR 전송과 독립(`AppSettings.cs:95-96`).
- `HostingBaseUrl = "https://mcphoto-955fb.web.app"`(`AppSettings.cs:122-123`), `StorageBucket = "mcphoto-955fb.firebasestorage.app"`(`:125-130`). 신규 프로젝트는 보통 `{project}.firebasestorage.app`, 레거시는 `{project}.appspot.com`.
- `BackendBaseUrl`(`:137`)·`GoogleClientId`(`:152`)도 **운영 프로젝트 값이 내장**되어 있어 보통 INI에 적지 않는다. 다른 백엔드/구글 프로젝트를 쓸 때만 해당 키로 오버라이드한다(공개값이라 하드코딩 무해).
- `BackendApiKey`는 코드 기본값이 빈 문자열이고, **publish 시 exe에 내장된 키**(`-p:BackendApiKeyDefault`)를 `IniSettingsService`가 로드 시 주입한다. INI에 값이 있으면 그 값이 우선하며, 저장 시 INI에 다시 쓰지는 않는다(평문 유출 방지, `IniSettingsService.cs:16-18,36-37`).

### 1.2 enum·WindowBounds 정의

- `DisplayMode`(`AppSettings.cs:3-8`): `Fullscreen`(0), `Windowed`(1). 기본 Windowed.
- `OutputFormat`(`:10-15`): `Jpg`(0), `Png`(1). 기본 Jpg.
- `WindowBounds`(`:17-27`): `Left=NaN`, `Top=NaN`, `Width=1280`, `Height=720`, 파생 `HasPosition = !IsNaN(Left) && !IsNaN(Top)`(위치 미저장이면 화면 중앙).

### 1.3 Clamp / QR 정규화 세부

- `Clamp()`(`AppSettings.cs:159-184`): CutCount/CountdownSec/RetakeLimit 최근접 보정(`ClosestFrom`) → RetentionHours 1~72 → WindowBounds Width/Height 하한 → CameraDevice≥0 → HostingBaseUrl 트레일링 슬래시 **제거** → `NormalizeBackend()` → `NormalizeQr()`.
  - **it17**: CutCount 보정에는 자동 sentinel 가드가 **선행**한다 — `if (!CutCountPolicy.IsAuto(CutCount) && Array.IndexOf(...) < 0)`. `Clamp()`는 로드·저장 양쪽에서 불리므로(`IniSettingsService.Load()`/`Save()`) 가드가 없으면 `ClosestFrom(0,{6,8,10})`이 0을 6으로 덮어써 저장 왕복 1회에 "자동" 설정이 소멸한다.
- `NormalizeBackend()`(`:187-199`): `BackendBaseUrl`·`BackendApiKey`·`GoogleClientId` 트림 + base URL이 **슬래시로 끝나도록 보정**(`HttpClient.BaseAddress`가 상대 경로를 안전히 결합하도록). ⚠️ `HostingBaseUrl`(슬래시 제거)과 방향이 반대다 — 용도가 다르다(URL 문자열 조립 vs HttpClient base). base URL이 비면 보정하지 않고 그대로 둔다(미구성은 런타임 호출 실패로 드러남).
- `NormalizeQr()` = `QrDeliveryPolicy.Normalize`: `EnableQrDelivery && !SendPhoto && !SendTimelapse`이면 `EnableQrDelivery=false`(하위 토글 값은 보존). off→on 재활성 시 하위 둘 다 on 강제(`QrDeliveryPolicy.OnReEnabled`)는 UI(`SettingsViewModel`)에서 처리(`AppSettings`/`IniSettingsService` 자체엔 없음).
- `Clone()`(`:162-189`): 편집 취소 대비 얕은 복제(WindowBounds는 새 인스턴스).

### 1.4 촬영 컷 수 — 설정 의도 vs 실제 컷 수 (it17)

`CutCount`는 **의도**만 담는다. 실제 촬영 컷 수는 프레임(=슬롯 수)이 확정된 뒤 산출된다.

```
AppSettings.CutCount        ← 의도  : 0(자동) | 6 | 8 | 10   (INI 왕복 대상)
        │  FrameSelectViewModel.Next() → CaptureSession.Begin(frame, cutCount)
        ▼  CutCountPolicy.Resolve(cutCount, frame.Slots.Count)   ← 유일한 해석 지점
CaptureSession.CutCount     ← 실효값: 6 | 7 | 8 | 10 | …       (Guide·Capture가 읽음)
```

- 자동(`CutCount=0`): `실제 = max(6, 슬롯 수 + 2)` — 슬롯보다 2장 여유를 확보해 컷 선택의 여지를 남긴다.
- 고정(`6`/`8`/`10`): `실제 = max(설정값, 슬롯 수)` — 종전 동작 그대로("컷 수 ≥ 슬롯 수" 불변).
- 슬롯 1~4개에서는 최소 6이 이미 +2를 초과하므로 자동과 고정 6이 같다. 실질 차이는 **슬롯 5개(→7컷)·6개(→8컷)** 에서만 발생한다.
- 실효값은 `AppSettings`를 거치지 않으므로 `AllowedCutCounts` 검사에 닿지 않는다 — 7컷이 정상 동작한다(촬영 루프·`WrapPanel` 컷 선택·합성은 모두 임의 정수 N을 견딘다).
- 세션은 `CaptureSession.IsAutoCutCount`로 시작 시점의 의도를 기억한다(설정은 세션 도중 오버레이로 변경될 수 있으므로 다시 읽지 않는다). Guide 화면의 "(자동)" 배지가 이 값을 쓴다.

> 정정 주의: Clamp에는 `Left`/`Top`(창 위치) 클램프가 **없다**. 창 위치는 `MainWindow.OnClosing`에서만 갱신되며, INI에는 NaN이어도 `WindowLeft`/`WindowTop` 키가 항상 기록된다(`IniSettingsService.cs:177-178`, `WindowBounds` 미저장이면 값이 NaN 문자열로 직렬화되고 재로드 시 다시 NaN 폴백).

---

## 2. INI 저장 위치 · 폴백 체인 · 신뢰성

### 2.1 파일명·후보 경로

- 파일명 `MCPhoto.ini`(`SettingsPathResolver.FileName`).
- 후보(우선순위, `SettingsPathResolver.DefaultCandidates`, `SettingsPathResolver.cs:30-36`):
  1. **실행 경로** `{AppContext.BaseDirectory}\MCPhoto.ini`
  2. `%ProgramData%\MCPhoto\MCPhoto.ini`
  3. `%LocalAppData%\MCPhoto\MCPhoto.ini`

`IniSettingsService`가 `AppContext.BaseDirectory` / `CommonApplicationData` / `LocalApplicationData`를 넘겨 후보를 만든다(`IniSettingsService.cs:88-92`).

### 2.2 쓰기 가능 판정·초기 경로 선택

- 초기 경로: `ResolveWritable(후보, CanWrite)`(`SettingsPathResolver.cs:18-27`) — 후보를 순서대로 시도, **쓰기 가능한 첫 경로** 선택. 하나도 없으면 1순위(실행 경로) 반환(Save 폴백이 재시도).
- `CanWrite`(`IniSettingsService.cs:94-108`): **실제 I/O로 판정** — 디렉터리 생성 → 임시 프로브 파일(`.mcphoto_write_probe_{guid}`) 쓰기·삭제 성공 여부. 예외 시 false.

### 2.3 저장 폴백 체인·경로 승격

`Save()`(`IniSettingsService.cs:50-76`):

1. `Clamp()` 후 직렬화.
2. `FallbackPaths()`(`:78-86`) = 현재 `_iniPath` → DefaultCandidates(중복 제외) 순으로 `TryWrite`(`:110-125`) 시도.
3. **첫 성공 경로를 `_iniPath`로 승격**(다음 저장·로드가 같은 위치 사용).
4. 전부 실패 시 error 로그 + **`false` 반환**.

폴백 순서 요약: **실행 경로 → %ProgramData%\MCPhoto → %LocalAppData%\MCPhoto**.

### 2.4 저장 신뢰성(성공 오인 금지)

- `Save()`는 **bool 반환**(성공/전부실패). 예외는 삼켜 로그하되, 반환값으로 실패를 정직하게 알린다.
- `SettingsViewModel.SaveSettings`(`SettingsViewModel.cs:188-226`)는 반환 bool을 확인해 성공 토스트 vs "저장 위치에 쓸 수 없습니다" 오류 토스트로 분기(색상도 성공=Success/실패=Danger, `BoolToNoticeBrushConverter`). 성공 오인 절대 금지(it3 §3).
- `MainWindow.OnClosing`의 창 위치 저장은 반환값 무시(`_ = _settings.Save()`, `MainWindow.xaml.cs:74`) — 부수적 저장이라 실패해도 무해.

### 2.5 로드 신뢰성

- `Load()`(`IniSettingsService.cs:28-48`): 파일 없으면 기본값, 손상 파일이면 warning 로그 후 기본값 진행(크래시 금지) → `Clamp()`. `Current`(`:26`)는 lazy(`??= Load()`), 이후 인메모리 캐시. 호출부가 `Current`를 직접 수정하고 `Save()`로 flush.

### 2.6 INI 파서(IniFile)

- 자체 경량 파서(`IniFile.cs`, 외부 의존 없음): 섹션(`[X]`)→키→값 딕셔너리, 키 **대소문자 무시**(`OrdinalIgnoreCase`), `;`/`#` 주석·빈 줄·손상 줄(‘=’ 없음) 무시(예외 없음). 타입별 안전 파서(`GetInt/Double/Bool/Enum`)는 실패 시 fallback 반환. bool은 `true/1/on/yes`·`false/0/off/no` 인식. 직렬화(`ToString`)는 기본(무명) 섹션 먼저.

---

## 3. 브랜딩(앱 이름)

- 정의: `IBrandingService`(프로퍼티 `AppName`·`Subtitle`) · `IniBrandingService`(`IniBrandingService.cs`). DI Singleton(`ServiceRegistration.cs:35`), 시작 1회 로드.
- **파일**: `branding.ini`, 섹션 `[Branding]`, 키 `AppName`(앱 이름) · `Subtitle`(홈 화면 소제목).
- **탐색 경로 순서**(`Candidates`, `IniBrandingService.cs:59-64`):
  1. 실행 경로 `{AppContext.BaseDirectory}\branding.ini`
  2. `%ProgramData%\MCPhoto\branding.ini`
  존재하는 첫 파일 사용.
- **폴백**: 파일 부재 / 빈 값 / 손상·예외 → 기본값 **AppName="MC Photo"**(`DefaultAppName`) · **Subtitle="self custom photobooth"**(`DefaultSubtitle`). 두 키는 독립 폴백(한 키만 비어도 그 키만 기본값). 어떤 실패에도 크래시 금지.
- **인코딩**: UTF-8 명시 읽기(`File.ReadAllText(resolved, Encoding.UTF8)`, `:32`) — 한글 이름·메모장 인코딩 편차 대비.
- **로딩 시점·적용**: `App.OnStartup`이 `AppName`→`Resources["Branding.AppName"]`, `Subtitle`→`Resources["Branding.Subtitle"]`에 주입(창 생성 **전**, `App.xaml.cs`) → `App.xaml` 기본값을 덮어씀 → `DynamicResource`로 창 제목(`MainWindow.xaml`)·홈 타이틀(`HomeView.xaml`, `Branding.AppName`)·홈 소제목(`HomeView.xaml`, `Branding.Subtitle`)에 반영. 변경은 앱 재시작 필요(읽기 전용).
- **동봉 샘플**: `branding.ini.sample`(빌드 시 실행 폴더 복사, `MCPhoto.App.csproj:76`). 현재 내용은 3줄뿐이다 — `[Branding]` / `AppName=MC Photo` / `Subtitle=(prototype)`. 고객은 이 파일을 `branding.ini`로 리네임해 값만 바꾸면 된다.

---

## 4. 표시 모드 즉시 적용 메커니즘

- 적용 지점: `MainWindow.ApplyDisplaySettings`(`MainWindow.xaml.cs:34-63`).
  - **Fullscreen**: `WindowStyle=None`, `ResizeMode=NoResize`, `WindowState=Maximized`.
  - **Windowed**: `SingleBorderWindow` + `CanResize` + `Normal`, `Width/Height=WindowBounds`, `HasPosition`이면 `Manual`+Left/Top, 아니면 `CenterScreen`.
- **재시작 없이 즉시 반영**: 설정 저장(`SettingsViewModel.SaveSettings`)이 성공하면 `AppShellViewModel.RequestApplyDisplayMode()`(`AppShellViewModel.cs:81`)가 `DisplayModeApplyRequested` 이벤트 발행 → `MainWindow` 생성자에서 구독(`MainWindow.xaml.cs:24`)해 `ApplyDisplaySettings` 재실행.
- 초기 적용: `MainWindow` 생성자에서 1회(`:22`), `Loaded`에서 `_shell.Startup()`(`:25`).

---

## 5. 창 위치·크기 저장(WindowBounds)

- 저장 시점: `MainWindow.OnClosing`(`MainWindow.xaml.cs:65-78`). **창모드 + `WindowState==Normal`**일 때만 현재 Left/Top/Width/Height를 `WindowBounds`에 기록 후 `_settings.Save()`(반환값 무시).
- 복원: `ApplyDisplaySettings`가 창모드에서 `WindowBounds.Width/Height` 적용, `HasPosition`이면 Left/Top로 수동 위치, 아니면 화면 중앙.
- INI 매핑: `WindowLeft/WindowTop/WindowWidth/WindowHeight`(`IniSettingsService.cs:150-153`, `:177-180`). Clamp에서 Width≥1280/Height≥720 하한(`AppSettings.cs:125-126`).

---

## 6. 빌드 정보(bldinfo.ini) — 앱 버전 표기

- 정의: `IBuildInfoService`(프로퍼티 `Version`·`BuildDate`·`Site` + `DisplayText`) · `IniBuildInfoService`(`IniBuildInfoService.cs`). DI Singleton(`ServiceRegistration.cs:37`), 시작 1회 로드. **버전을 소스코드에 하드코딩하지 않기 위한 외부 파일**(brandbranding과 동일 패턴).
- **파일**: `bldinfo.ini`, 섹션 `[General]`, 키 `Version`(예 `1.0.0`) · `BuildDate`(예 `2026-07-23`) · `Site`(예 `Beta`).
- **탐색 경로 순서**(`Candidates`): ① 실행 경로 `{AppContext.BaseDirectory}\bldinfo.ini` → ② `%ProgramData%\MCPhoto\bldinfo.ini`. 존재하는 첫 파일 사용.
- **폴백**: 파일/키 부재·손상 → `Version="0.0.0"`, `BuildDate`·`Site` 빈 문자열. 크래시 금지. UTF-8 명시 읽기, `IniFile` 파서 재사용(`;`/`#` 주석 무시).
- **표기**: `DisplayText`(예 `v1.0.0 · Beta`)를 **앱 하단 우측에 로그인 여부와 무관하게 상시** 노출(`MainWindow.xaml`의 흐린 캡션 + `AppShellViewModel.VersionText`, 클릭 비간섭). (it12 R4: `BuildDate`는 표기에서 제외 — 업데이트 지연 시 오래된 앱으로 보일 위험. 프로퍼티·ini 키·로드 로직은 유지)
- **배포**: 실행 폴더 동봉(`MCPhoto.App.csproj:82`의 `None CopyToOutputDirectory`) + `publish.ps1`이 publish 산출물에 명시 복사(`publish.ps1:81-90` — 원본 부재 시 경고만 하고 계속, 앱은 폴백 버전 표시). `.gitignore`는 `*.ini` 무시 + `!bldinfo.ini` 예외로 추적(`.gitignore:58`). **버전 값은 배포 시 사용자가 직접 관리**한다(현재 값: `1.1.3` / `Beta`).
- 근거: `IBuildInfoService.cs`, `IniBuildInfoService.cs`, `MainWindow.xaml`, `AppShellViewModel.cs`, `publish.ps1`, `bldinfo.ini`.

---

## 7. 참고

- 설정 화면 UI 구성·항목별 편집 동작은 [11 기능 상세](./11-exe-app-features.md) §11(설정 화면), QR 토글 연동 규칙은 §8.3.
- 데이터 폴더·로그·세션 임시 폴더는 [10 아키텍처](./10-exe-app-architecture.md) §5.2. (설정 INI는 실행 경로 우선, 로그는 항상 `%ProgramData%\MCPhoto\logs`.)
- 본 문서 수치·경로는 **실제 소스가 진실의 소스**다(과거 이터레이션 설계 문서는 git 이력 참조).
