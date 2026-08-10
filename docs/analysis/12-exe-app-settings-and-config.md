# 12 · Exe 앱 설정 · 구성 · 기본값 · 브랜딩

| 항목 | 내용 |
| --- | --- |
| 문서 | 12-exe-app-settings-and-config.md |
| 범위 | `AppSettings` 전 항목·기본값·범위, INI 저장/폴백/신뢰성/외래 섹션 보존, `[Test]` 섹션(역할별 테스트 모드), 브랜딩(branding.ini), 빌드 정보(어셈블리 버전 리소스 + exe 타임스탬프), 표시 모드 즉시 적용, 창 위치 저장 |
| 최종 업데이트 | 2026-08-10 (it23 반영 — 외부 카메라 키 5종 실배선, `[Test]` 섹션 §7 신설, 외래 섹션 보존 §2.7 신설) · 이전 2026-07-29 (it15·it16) |
| 관련 소스 경로 | `src/MCPhoto.Core/Settings/**`, `src/MCPhoto.Core/Branding/**`, `src/MCPhoto.Core/Build/**`, `src/MCPhoto.App/branding.ini.sample`, `Directory.Build.props`, `src/MCPhoto.App/MainWindow.xaml.cs`, `src/MCPhoto.App/ServiceRegistration.cs` |
| 갱신 규칙 | `AppSettings` 필드 추가/기본값/Clamp 변경, INI 매핑(`IniSettingsService`) 변경, 폴백 경로(`SettingsPathResolver`) 변경, 브랜딩 로직/기본값 변경 시 이 문서를 갱신한다. |

관련 문서: [10 아키텍처](./10-exe-app-architecture.md) · [11 기능 상세](./11-exe-app-features.md) · 인덱스 [README](./README.md)

> ⚠️ **이 문서는 Windows 데스크톱 구현 참조다.** INI 파일 형식·3단 경로 폴백(`실행경로 → %ProgramData% → %LocalAppData%`)·`branding.ini`·어셈블리 버전 리소스·표시 모드/창 기하는 **Windows 고유 구현**이며 이식 대상이 아니다.
>
> **다른 플랫폼 클라이언트를 만든다면 [41 · 로컬 데이터·파일 포맷 규격](./41-local-data-and-file-formats.md)이 진실원이다** — 설정 **키 이름·기본값·범위·보정 규칙**은 계약이지만 **저장 형식·위치는 플랫폼 자유**다. 41번에 플랫폼별 저장 위치 대응표와 프레임 `.slots` 파일 포맷이 있다.

---

## 1. AppSettings 전 항목

정의: `AppSettings`(`AppSettings.cs`). INI 매핑: `IniSettingsService.ReadInto`(`:136`)/`WriteFrom`(`:174`). **INI 섹션명은 모두 `[MCPhoto]`**(`IniSettingsService.cs:11`). 대부분 INI 키는 프로퍼티명과 동일(`nameof`), 예외는 `WindowBounds` 4개(`WindowLeft/Top/Width/Height`).

값 범위·옵션 제약은 `AppSettings.Clamp()`(`:157-180`)가 로드/저장 시 강제한다.

| 키(프로퍼티) | 타입 | 기본값 | 범위·Clamp | INI 키 | 영향 |
| --- | --- | --- | --- | --- | --- |
| `CutCount` | int | **`0`=자동** | 허용 {6,8,10} **또는 `0`=자동**; 벗어나면 가장 가까운 허용값(동률 시 첫 값)으로 보정. **`0`은 최근접 보정에서 제외**(sentinel 보존, it17) — `-1` 등 다른 값은 종전대로 6으로 보정 | `CutCount` | 촬영 컷 수의 **의도**. 실제 촬영 컷 수는 프레임 슬롯 수 확정 후 `CaptureSession.Begin`이 산출(§1.4) |
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
| `ExternalCameraEnabled` | bool | **false** | — | `ExternalCameraEnabled` | 외부 카메라(DSLR) **실배선**(it23). on이면 촬영 세션이 DSLR 스틸을 시도하고, SDK·장비가 없으면 웹캠 단독으로 강등 + 사유 토스트. 프리뷰·타임랩스는 **항상 웹캠 전담** |
| `ExternalCameraModel` | string | **`NikonD5300`** | 미지 Id는 기본 모델로 보정 | `ExternalCameraModel` | 모델 레지스트리 Id(`ExternalCameraModels`). SDK 모듈 파일명(`Type0011.md3`)은 이 Id에서 유도된다 |
| `ExternalShutterSpeed` | string | **""**(미지정) | 트림만 — 도메인 검증은 적용 시점 | `ExternalShutterSpeed` | 셔터 속도 표시 문자열(예 `1/125`). 빈 값 = 카메라 현재값 유지. **인덱스가 아니라 문자열**(카메라 목록은 모드·SDK 버전에 따라 달라진다) |
| `ExternalAperture` | string | **""**(미지정) | 트림만 | `ExternalAperture` | 조리개(예 `f/5.6`) |
| `ExternalIso` | string | **""**(미지정) | 트림만 | `ExternalIso` | ISO(예 `400`) |
| `PhotoPrinterEnabled` | bool | **false** | — | `PhotoPrinterEnabled` | 사진 프린터 **준비 플래그**(it24 — placeholder에서 편집 가능으로 승격). 의미는 "인쇄 기능이 도입되면 이 프린터 구성을 사용한다"이고, 현재 런타임 효과는 **설정 화면의 프린터 하위 패널 노출뿐**이다(실제 인쇄는 비목표 — 화면이 상시 고지한다) |
| `PhotoPrinterName` | string | **""**(미선택) | 트림만 — **목록 대조 없음** | `PhotoPrinterName` | 선택된 설치 프린터 이름(Windows 프린터명, 연결 프린터는 `\\서버\이름`). ⚠️ 열거 목록에 없어도 **값을 지우지 않는다** — 프린터가 잠시 꺼진 상태에서 관리자 설정을 파괴하지 않기 위함이고, 검증은 사용 시점(인쇄 구현)의 몫이다 |
| `BackendBaseUrl` | string | **`https://asia-northeast3-mcphoto-955fb.cloudfunctions.net/api`**(운영 기본값 내장) | 트림 + 트레일링 `/` **부여**(`NormalizeBackend`) | `BackendBaseUrl` | 백엔드 API 주소. 빈 값이면 백엔드 미구성(업로드·로그인 불가) |
| `BackendApiKey` | string | **""** — 실제 기본값은 **exe 내장 키**(`AssemblyMetadata "MCPhoto.BackendApiKey"`)를 로드 시 주입 | 트림 | `BackendApiKey` | 배포 게이트 키(`X-MCPhoto-Client`). ⚠️ INI에 평문 — 유출 시 서버에서 해당 키만 폐기 |
| `GoogleClientId` | string | 운영 프로젝트 Desktop 클라이언트 ID 내장 | 트림 | `GoogleClientId` | Google SSO authorize URL 조립. **빈 값이면 로그인 화면의 "Google로 로그인" 버튼을 숨김**(SSO opt-out) |

보조 상수(`AppSettings.cs:38-44`): `AllowedCutCounts={6,8,10}`, `AllowedCountdownSecs={3,6,8,10}`, `AllowedRetakeLimits={1,2,3}`, `MinRetentionHours=1`, `MaxRetentionHours=72`, `MinSlots=1`, `MaxSlots=6`.

자동 컷 수 상수(`CutCountPolicy.cs`, it17): `AutoCutCount=0`(sentinel), `AutoMinimum=6`, `AutoMargin=2`. ⚠️ `AutoCutCount`는 **`AllowedCutCounts`에 넣지 않는다** — 넣으면 `CutCount=3` 같은 오입력이 6이 아니라 0(자동)으로 보정된다(|3-0|=|3-6| 동률 → 첫 값 승리).

> **it12 R1 — 로그인 전용 편집(게이트)**: 게스트(비로그인) 설정 화면에서 `MirrorMode`·`RetakeEnabled`·`RetakeLimit`·`FilterGrayscale`/`FilterBrightness`/`FilterBeauty`·`EnableQrDelivery`(+`SendPhoto`/`SendTimelapse`)·`HostingBaseUrl`·`StorageBucket`는 OFF 표시·컨트롤 비활성 + "로그인 필요" 인라인 노티 상시 표시(R3, hover 툴팁에서 개정)이며 저장 시 미기록(ini 원값 보존=클로버 금지). 게이트는 `SettingsViewModel`(VM)에만 존재 — `AppSettings` 모델은 전 필드 항상 직렬화되고, 촬영/필터 런타임은 `Settings.Current`(ini=관리자값)대로 동작한다(편집 권한만 제한, 기능은 불변).

> **it23 → it24 — 외부 장치 편집 게이트(`CanConfigureExternalCamera`)**: `ExternalCameraEnabled`·`ExternalCameraModel`·`ExternalShutterSpeed`/`ExternalAperture`/`ExternalIso` **+ it24에서 편입된 `PhotoPrinterEnabled`·`PhotoPrinterName`** = **7키**는 **User 이상**(TempUser 제외)만 편집·저장한다. 역할 판정은 서수 부등식이 아니라 **명시 열거**다(`UserRoleExtensions.CanConfigureExternalCamera` — 역할이 추가될 때 권한이 조용히 따라 움직이는 것을 막는다).
> - it24 게이트 통일 근거: 종전 프린터 2키는 `!IsGuest` 블록에 있었으나 UI가 `IsEnabled="False"`라 TempUser도 값을 바꿀 수단이 없었다(기록값은 항상 Load 원값) — 게이트를 좁혀도 **관측 가능한 행동 차이가 없다**.
> - ⚠️ 다른 게이트와 **다른 점**: 편집 불가 세션에서도 로드 시 **강제 off 하지 않는다**. it24부터 섹션은 게스트에게도 **보이되 읽기 전용**이므로(§11 설정 화면), off로 표시하면 운영 상태를 오해하게 된다 — ini 원값을 그대로 보여 주고 저장 시 미기록으로 원값을 보존한다.
> - ⚠️ **편집 게이트이지 동작 게이트가 아니다**: 촬영 세션이 DSLR을 쓰는지는 ini의 `ExternalCameraEnabled` 기준이며 **게스트(손님) 세션에도 적용**된다 — 손님이 장비 구성을 바꿀 수는 없지만 그 장비로 찍히는 것은 당연하다는 키오스크 모델이다. 이것이 게스트에게도 원값을 보여 주는 이유다.
> - ⚠️ **[장치 검색]·[프린터 다시 검색]은 이 게이트가 아니라 `IsLoggedIn`**이다(TempUser 포함, 게스트 제외) — 검색은 상태를 바꾸지 않는 진단이라 진단·상태 모달과 같은 눈높이다.

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

- `Clamp()`(`AppSettings.cs:163-188`): CutCount/CountdownSec/RetakeLimit 최근접 보정(`ClosestFrom`) → RetentionHours 1~72 → WindowBounds Width/Height 하한 → CameraDevice≥0 → HostingBaseUrl 트레일링 슬래시 **제거** → `NormalizeBackend()` → `NormalizeQr()`.
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

- **기본값이 자동(`0`)이다.** 신규 설치·INI 키 누락·파싱 실패는 모두 자동으로 시작한다. 단 `CutCount=6`이 **이미 기록된 기존 INI**는 그 명시값이 우선이므로 여전히 6컷이다(6은 유효한 사용자 선택과 구분할 수 없어 자동 마이그레이션하지 않는다) — 자동으로 되돌리려면 설정 화면에서 "자동"을 고르거나 INI의 `CutCount` 줄을 지운다.
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

### 2.7 외래 섹션 보존 (it23)

- **소유 경계**: `[MCPhoto]` 섹션은 `IniSettingsService`가 **전적으로 소유**한다 — 매핑되지 않은 키는 저장 시 사라진다(오탈자 키·폐기 키가 영구히 남는 것을 막는다). 그 밖의 **모든 섹션은 외래(foreign)** 이며 저장 시 원문 그대로 실려 나간다(`IniFile.AdoptMissingSections`).
- **없으면 생기는 결함**: 종전 `Save()`는 빈 `IniFile`에 자기 섹션만 채워 파일을 통째로 덮어썼다. `MainWindow.OnClosing`이 **앱 종료마다 무조건** `Save()`를 부르므로, 사람이 손으로 넣은 `[Test]`(§7)·`[Branding]` 류 섹션이 **첫 종료에 사라졌다**. 원인 추적이 매우 어려운 결함이라 회귀 테스트로 못 박혀 있다.
- **왜 로드 시점 스냅샷이 아닌가**: 저장은 폴백 체인(§2.3)으로 **다른 경로**에 쓸 수 있고 그 파일에는 다른 외래 섹션이 있다. 또 앱 실행 중 INI를 손으로 고치는 것이 테스트 모드의 정상 사용 패턴인데, 스냅샷은 그 편집을 되돌린다. → **쓰려는 그 경로의 현재 파일**을 읽어 채취한다.
- ⚠️ **폴백 경로마다 다시 조립한다.** 한 번 만든 문자열을 재사용하면 1순위 파일의 외래 섹션이 2순위 파일로 **이식**된다(엉뚱한 위치에 `[Test]`가 복제된다).
- 대상 파일 읽기 실패(없음·잠김·손상)는 **무시하고 저장을 계속**한다 — 외래 섹션 보존은 부가 기능이고 설정 저장을 막을 이유가 못 된다(크래시 금지 원칙 승계). 이때 외래 섹션만 유실되고 Warning 로그가 남는다.
- **키 단위 병합은 하지 않는다**(섹션 단위가 경계). `[MCPhoto]` 안의 미매핑 키를 되살리면 위 소유 경계가 무의미해진다.

---

## 3. 브랜딩(앱 이름)

- 정의: `IBrandingService`(프로퍼티 `AppName`·`Subtitle`) · `IniBrandingService`(`IniBrandingService.cs`). DI Singleton(`ServiceRegistration.cs:35`), 시작 1회 로드.
- **파일**: `branding.ini`, 섹션 `[Branding]`, 키 `AppName`(앱 이름) · `Subtitle`(홈 화면 소제목).
- **탐색 경로 순서**(`Candidates`, `IniBrandingService.cs:59-64`):
  1. 실행 경로 `{AppContext.BaseDirectory}\branding.ini`
  2. `%ProgramData%\MCPhoto\branding.ini`
  존재하는 첫 파일 사용.
- **폴백**: 파일 부재 / 빈 값 / 손상·예외 → 기본값 **AppName="MCPhoto"**(`DefaultAppName`) · **Subtitle="self custom photobooth"**(`DefaultSubtitle`). 두 키는 독립 폴백(한 키만 비어도 그 키만 기본값). 어떤 실패에도 크래시 금지.
- **인코딩**: UTF-8 명시 읽기(`File.ReadAllText(resolved, Encoding.UTF8)`, `:32`) — 한글 이름·메모장 인코딩 편차 대비.
- **로딩 시점·적용**: `App.OnStartup`이 `AppName`→`Resources["Branding.AppName"]`, `Subtitle`→`Resources["Branding.Subtitle"]`에 주입(창 생성 **전**, `App.xaml.cs`) → `App.xaml` 기본값을 덮어씀 → `DynamicResource`로 창 제목(`MainWindow.xaml`)·홈 타이틀(`HomeView.xaml`, `Branding.AppName`)·홈 소제목(`HomeView.xaml`, `Branding.Subtitle`)에 반영. 변경은 앱 재시작 필요(읽기 전용).
- **동봉 샘플**: `branding.ini.sample`(빌드 시 실행 폴더 복사, `MCPhoto.App.csproj:76`). 현재 내용은 3줄뿐이다 — `[Branding]` / `AppName=MCPhoto` / `Subtitle=(prototype)`. 고객은 이 파일을 `branding.ini`로 리네임해 값만 바꾸면 된다.

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

## 6. 빌드 정보 — 앱 버전 표기 (it18: 외부 파일 폐기)

- 정의: `IBuildInfoService`(프로퍼티 `Version`·`BuildDate` + `DisplayText`) · `AssemblyBuildInfoService`(`AssemblyBuildInfoService.cs`). DI Singleton(`ServiceRegistration.cs`), 시작 1회 확정 후 불변. **값은 실행 파일 자신에서 나온다 — 외부 파일 없음.**
- **버전 출처**: 엔트리 어셈블리의 `AssemblyName.Version` 앞 3자리(`ToString(3)`). 원천은 `Directory.Build.props`의 `<Version>`이며 `AssemblyVersion`·`FileVersion`이 `$(Version).0`으로 파생된다 → **exe 파일 속성의 버전 리소스와 앱 표기가 항상 일치**한다. 릴리스 시 `<Version>` 한 줄만 올린다(현재 `1.1.6` → 파일 버전 `1.1.6.0`, 제품 버전 `1.1.6`).
- **⚠️ `IncludeSourceRevisionInInformationalVersion=false` 필수**: .NET SDK는 기본적으로 `AssemblyInformationalVersion`(= exe 파일 속성의 **제품 버전**)에 `+{git 커밋 SHA}`를 덧붙인다 → `1.1.6+c4469825f411…`. 운영자가 파일 속성에서 읽는 값이므로 이 속성으로 끈다. **표기 코드가 `InformationalVersion`을 쓰지 않는 이유도 같다** — `AssemblyName.Version`(4자리 숫자)은 해시가 섞일 수 없다.
- **빌드 시각 출처**: exe 파일의 `LastWriteTime`을 `yyyy-MM-dd HH:mm`(로컬)으로. 경로는 `Environment.ProcessPath`다 — `Assembly.Location`은 **단일 파일 퍼블리시에서 빈 문자열**이라 쓸 수 없다. `CreationTime`을 쓰지 않는 이유: 설치·복사 시점으로 덮어써져 "설치 시각"이 된다. `LastWriteTime`은 Inno Setup이 원본 시각을 보존하므로 배포 후에도 빌드 시각으로 남는다.
- **폴백**: 버전 확인 불가 → `"0.0.0"`. exe 경로 부재·읽기 실패 → `BuildDate` 빈 문자열. 어떤 경우에도 크래시 없음(예외는 삼켜 로그).
- **표기**: `DisplayText`(예 `v1.1.6`)를 **앱 하단 우측에 로그인 여부와 무관하게 상시** 노출(`MainWindow.xaml`의 흐린 캡션 + `AppShellViewModel.VersionText`, 클릭 비간섭). 진단 화면 "개발자 문의" 카드는 `Version`과 `BuildDate`(시각 포함)를 함께 보여준다([11 §17](./11-exe-app-features.md)).
- **배포**: 동봉할 파일이 없다. `publish.ps1`은 복사 대신 산출된 exe의 버전 리소스·타임스탬프를 콘솔에 출력해 무엇이 나갔는지 확인만 시켜 준다.
- **폐기 이력(it18)**: 종전에는 `bldinfo.ini`(`[General]` `Version`·`BuildDate`·`Site`)를 실행 경로 → `%ProgramData%\MCPhoto` 순으로 찾아 읽었다. 제거 이유 — ① 산출물의 ini를 따로 교체해야 해서 **exe 리소스 버전(1.0.0.0)과 표기 버전이 어긋나는 이중 관리**였고, ② `Site`(배포 채널)는 개발·알파 서버를 운영하지 않는 이 프로젝트에서 값이 `Beta`로 고정된 무의미한 표기였고, ③ 빌드일은 사람이 손으로 갱신해야 해서 실제 빌드 시점과 어긋날 수 있었다. `IniBuildInfoService`·`bldinfo.ini`·`Site` 프로퍼티·`.gitignore`의 `!bldinfo.ini` 예외·`publish.ps1`의 복사 단계가 모두 삭제됐다.
- 근거: `IBuildInfoService.cs`, `AssemblyBuildInfoService.cs`, `Directory.Build.props`, `MainWindow.xaml`, `AppShellViewModel.cs`, `DiagnosticsViewModel.cs`, `publish.ps1`.

---

## 7. `[Test]` 섹션 — 로그인 없는 역할별 테스트 모드 (it23)

QA·개발이 **로그인 없이** 특정 역할의 화면을 그대로 띄우기 위한 설정이다. `TestMode=1`이면 앱이 부팅 시 가짜 계정을 세션에 태우고, 그 시점부터 **모든 역할 게이트가 실제 로그인과 동일하게 동작**한다.

- **파일·섹션**: `MCPhoto.ini`(§2.1의 같은 파일), 섹션 `[Test]`. 앱이 이 섹션을 **쓰지는 않는다** — 사람이 손으로 적고, 앱은 읽기만 하며 저장 시 원문을 보존한다(§2.7).
- **키 이름·값 모두 대소문자를 무시한다.** `role=admin`·`Role=Admin` 둘 다 유효하다(값은 트림 + 소문자 정규화 후 대조).
- ⚠️ **주석은 보존되지 않는다.** 앱이 INI를 다시 쓸 때 `;`/`#` 줄은 사라진다(§2.6 파서 규약) — 설명은 이 문서에 두고 INI에는 값만 적는다.

| 키 | 타입 | 기본값 | 잘못된 값의 처리 | 의미 |
|----|------|--------|------------------|------|
| `TestMode` | bool | **0**(false) | 인식 불가 → **false**(안전측) | 마스터 스위치. 1이면 **무조건 로그인된 것으로 판단** |
| `Id` | string | `testuser` | 공백 → 기본값 | `User.Id`. 상단바 툴팁·아바타 이니셜·진단 계정 요약·사용자 관리 `IsSelf` 판정에 쓰이는 **화면에 보이는 값** |
| `Email` | string | `test@email.com` | `@` 없음·공백 → 기본값 + Warning | ⚠️ **표시용이 아니다.** 개인 프레임 로컬 저장 경로·`.slots` `#owner` 서명 값이라, 형식이 어긋나면 프레임 저장·소유 판정이 조용히 틀어진다 |
| `Role` | enum | `advanced_user` | 목록 밖 → 기본값 + Warning + **배너에 실제 역할 표시** | `temp_user` \| `user` \| `advanced_user` \| `manager` \| `admin` |
| `Pin` | string | (없음) | 4자리 숫자 아님 → 없음 취급 + Warning | **없으면 PIN 게이트 생략, 있으면 게이트를 띄우고 로컬 대조**(서버 호출 없음). 게이트 UI 자체(입력 검증·5회 실패 자동 닫힘·쿨다운)를 테스트할 수단 |
| `QrBlocked` | bool | **0** | 인식 불가 → false | TempUser 역할의 가장 특징적인 UI(QR 편집 차단 + 사유 문구)를 재현한다. `Role=temp_user`와 함께 쓸 때만 의미 |
| `QrBlockReason` | enum | `count` | 목록 밖 → `count` + Warning | `time` \| `count`. 설정 화면 문구가 사유별로 다르다 |

작성 예시:

```ini
[Test]
TestMode=1
Id=testadmin
Email=test@email.com
Role=admin
Pin=1234
```

**동작 규약**

- **역할 게이트는 자동으로 따라온다.** 계정 진실 소스(`SessionContext`) 한 곳에 태우므로 `IsPower`·`CanWriteFrames`·`CanConfigureExternalCamera`·`HierarchyRank` 판정이 실제 로그인과 동일하게 적용된다.
- **서버 권한은 0이다.** 테스트 모드에는 JWT가 없다(가짜 토큰을 **의도적으로 만들지 않는다** — 넣으면 모든 서버 호출이 401을 받고 "로그인이 만료되었습니다"라는 **거짓 원인**이 표시된다). 사용자 관리·프레임 서버 동기화·QR 업로드 등 서버 의존 화면은 크래시 없이 "로그인이 필요합니다" 계열 안내로 떨어진다.
- **경고 배너가 상시 노출된다.** 이 기능은 릴리스 빌드에도 포함되므로(`#if DEBUG` 격리 없음), `TestMode=1`이면 화면 최상단에 닫을 수 없는 배너가 뜬다. 실운영 오투입을 즉시 드러내기 위한 것이며 유휴 경고 스크림에도 가려지지 않는다.
- **실계정으로 새지 않는다.** 테스트 계정 판정은 값 비교가 아니라 **참조 동일성**(`ITestModeService.IsTestUser`)이다. 즉 `TestMode=1`을 켠 채 실제 Google SSO로 로그인하면 — 이메일·Id·역할이 우연히 전부 같아도 — 그 계정은 **정상 PIN 게이트·정상 서버 경로**를 탄다. 테스트 모드 INI를 켜둔 채 실계정으로 작업할 수 있다(배너는 계속 표시).
- 로그아웃은 정상 동작하며(게스트 상태도 테스트 대상), 테스트 모드에서는 로그인 화면에 **[테스트 계정으로 로그인]** 버튼이 노출되어 앱 재시작 없이 되돌아올 수 있다.

⚠️ **보안**: INI를 쓸 수 있는 사람은 인증 없이 관리자 역할의 **화면**에 도달할 수 있다. 단 게스트도 원래 설정 화면에 무가드로 진입하며, 서버 권위 영역(계정 DB·프레임 정본·업로드 정원)은 토큰이 없어 **건드릴 수 없다**.

---

## 8. 참고

- 설정 화면 UI 구성·항목별 편집 동작은 [11 기능 상세](./11-exe-app-features.md) §11(설정 화면), QR 토글 연동 규칙은 §8.3.
- 데이터 폴더·로그·세션 임시 폴더는 [10 아키텍처](./10-exe-app-architecture.md) §5.2. (설정 INI는 실행 경로 우선, 로그는 항상 `%ProgramData%\MCPhoto\logs`.)
- 본 문서 수치·경로는 **실제 소스가 진실의 소스**다(과거 이터레이션 설계 문서는 git 이력 참조).
