# 12 · Exe 앱 설정 · 구성 · 기본값 · 브랜딩

| 항목 | 내용 |
| --- | --- |
| 문서 | 12-exe-app-settings-and-config.md |
| 범위 | `AppSettings` 전 항목·기본값·범위, INI 저장/폴백/신뢰성, 브랜딩(branding.ini), 표시 모드 즉시 적용, 창 위치 저장 |
| 최종 업데이트 | 2026-07-23 |
| 관련 소스 경로 | `src/MCPhoto.Core/Settings/**`, `src/MCPhoto.Core/Branding/**`, `src/MCPhoto.App/branding.ini.sample`, `src/MCPhoto.App/MainWindow.xaml.cs`, `src/MCPhoto.App/ServiceRegistration.cs` |
| 갱신 규칙 | `AppSettings` 필드 추가/기본값/Clamp 변경, INI 매핑(`IniSettingsService`) 변경, 폴백 경로(`SettingsPathResolver`) 변경, 브랜딩 로직/기본값 변경 시 이 문서를 갱신한다. |

관련 문서: [10 아키텍처](./10-exe-app-architecture.md) · [11 기능 상세](./11-exe-app-features.md) · 인덱스 [README](./README.md)

---

## 1. AppSettings 전 항목

정의: `AppSettings`(`AppSettings.cs`). INI 매핑: `IniSettingsService.ReadInto`/`WriteFrom`(`IniSettingsService.cs:129-181`). **INI 섹션명은 모두 `[MCPhoto]`**(`IniSettingsService.cs:11`). 대부분 INI 키는 프로퍼티명과 동일(`nameof`), 예외는 `WindowBounds` 4개(`WindowLeft/Top/Width/Height`).

값 범위·옵션 제약은 `AppSettings.Clamp()`(`:115-133`)가 로드/저장 시 강제한다.

| 키(프로퍼티) | 타입 | 기본값 | 범위·Clamp | INI 키 | 영향 |
| --- | --- | --- | --- | --- | --- |
| `CutCount` | int | **6** | 허용 {6,8,10}; 벗어나면 가장 가까운 허용값(동률 시 첫 값)으로 보정 | `CutCount` | 촬영 컷 수(가이드/촬영) |
| `CountdownSec` | int | **6** | 허용 {3,6,8,10}; 최근접 보정 | `CountdownSec` | 컷당 카운트다운 초 |
| `MirrorMode` | bool | **true** | — | `MirrorMode` | 거울(좌우반전) 프리뷰=저장 WYSIWYG |
| `FlashMode` | bool | **false** | — | `FlashMode` | 셔터 직전 하양 화면 플래시 |
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
| `HostingBaseUrl` | string | **`https://mcphoto-955fb.web.app`**(오늘 확정, 개발 기본값) | 트레일링 `/` 제거 | `HostingBaseUrl` | 다운로드 페이지 URL 조립 base |
| `StorageBucket` | string | **`mcphoto-955fb.firebasestorage.app`**(오늘 확정, 개발 기본값) | — | `StorageBucket` | Firebase Storage 버킷(빈 값이면 project_id 유도) |

보조 상수(`AppSettings.cs:36-41`): `AllowedCutCounts={6,8,10}`, `AllowedCountdownSecs={3,6,8,10}`, `MinRetentionHours=1`, `MaxRetentionHours=72`, `MinSlots=1`, `MaxSlots=6`.

### 1.1 오늘(2026-07-23) 확정 기본값

- `DisplayMode = DisplayMode.Windowed` — **개발 기간 기본**(배포 시 Fullscreen으로 되돌릴 것; `AppSettings.cs:91-92` 주석).
- `SaveLocalCopy = true` — QR 전송과 독립(`AppSettings.cs:84-85`).
- `HostingBaseUrl = "https://mcphoto-955fb.web.app"` — 개발 기본값 하드코딩(`AppSettings.cs:102-103`).
- `StorageBucket = "mcphoto-955fb.firebasestorage.app"` — 개발 기본값 하드코딩(`AppSettings.cs:105-110`). 신규 프로젝트는 보통 `{project}.firebasestorage.app`, 레거시는 `{project}.appspot.com`.

### 1.2 enum·WindowBounds 정의

- `DisplayMode`(`AppSettings.cs:3-8`): `Fullscreen`(0), `Windowed`(1). 기본 Windowed.
- `OutputFormat`(`:10-15`): `Jpg`(0), `Png`(1). 기본 Jpg.
- `WindowBounds`(`:17-27`): `Left=NaN`, `Top=NaN`, `Width=1280`, `Height=720`, 파생 `HasPosition = !IsNaN(Left) && !IsNaN(Top)`(위치 미저장이면 화면 중앙).

### 1.3 Clamp / QR 정규화 세부

- `Clamp()`(`AppSettings.cs:115-133`): CutCount/CountdownSec 최근접 보정(`ClosestFrom`, `:148-159`) → RetentionHours 1~72 → WindowBounds Width/Height 하한 → CameraDevice≥0 → HostingBaseUrl 트레일링 슬래시 제거 → `NormalizeQr()`.
- `NormalizeQr()`(`:139-146`) = `QrDeliveryPolicy.Normalize`: `EnableQrDelivery && !SendPhoto && !SendTimelapse`이면 `EnableQrDelivery=false`(하위 토글 값은 보존). off→on 재활성 시 하위 둘 다 on 강제(`QrDeliveryPolicy.OnReEnabled`)는 UI(`SettingsViewModel`)에서 처리(`AppSettings`/`IniSettingsService` 자체엔 없음).
- `Clone()`(`:162-189`): 편집 취소 대비 얕은 복제(WindowBounds는 새 인스턴스).

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

- 정의: `IBrandingService`(프로퍼티 `AppName`·`Subtitle`) · `IniBrandingService`(`IniBrandingService.cs`). DI Singleton(`ServiceRegistration.cs:29`), 시작 1회 로드.
- **파일**: `branding.ini`, 섹션 `[Branding]`, 키 `AppName`(앱 이름) · `Subtitle`(홈 화면 소제목).
- **탐색 경로 순서**(`Candidates`, `IniBrandingService.cs:59-64`):
  1. 실행 경로 `{AppContext.BaseDirectory}\branding.ini`
  2. `%ProgramData%\MCPhoto\branding.ini`
  존재하는 첫 파일 사용.
- **폴백**: 파일 부재 / 빈 값 / 손상·예외 → 기본값 **AppName="MC포토"**(`DefaultAppName`) · **Subtitle="셀프 포토부스"**(`DefaultSubtitle`). 두 키는 독립 폴백(한 키만 비어도 그 키만 기본값). 어떤 실패에도 크래시 금지.
- **인코딩**: UTF-8 명시 읽기(`File.ReadAllText(resolved, Encoding.UTF8)`, `:32`) — 한글 이름·메모장 인코딩 편차 대비.
- **로딩 시점·적용**: `App.OnStartup`이 `AppName`→`Resources["Branding.AppName"]`, `Subtitle`→`Resources["Branding.Subtitle"]`에 주입(창 생성 **전**, `App.xaml.cs`) → `App.xaml` 기본값을 덮어씀 → `DynamicResource`로 창 제목(`MainWindow.xaml`)·홈 타이틀(`HomeView.xaml`, `Branding.AppName`)·홈 소제목(`HomeView.xaml`, `Branding.Subtitle`)에 반영. 변경은 앱 재시작 필요(읽기 전용).
- **동봉 샘플**: `branding.ini.sample`(빌드 시 실행 폴더 복사, `MCPhoto.App.csproj`). 내용은 `[Branding]` + `AppName=우리동네 포토부스` · `Subtitle=추억을 남기는 순간` 예시와 사용 안내(`branding.ini.sample`).

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

## 6. 참고

- 설정 화면 UI 구성·항목별 편집 동작은 [11 기능 상세](./11-exe-app-features.md) §11(설정 화면), QR 토글 연동 규칙은 §8.3.
- 데이터 폴더·로그·세션 임시 폴더는 [10 아키텍처](./10-exe-app-architecture.md) §5.2. (설정 INI는 실행 경로 우선, 로그는 항상 `%ProgramData%\MCPhoto\logs`.)
- 반영된 설계 근거: `docs/design/wpf-it6/it9-design.md`(INI 폴백 체인·저장 신뢰성·브랜딩·표시 모드). 본 문서 수치·경로는 실제 소스가 진실의 소스다.
