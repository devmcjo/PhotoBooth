# MCPhoto 이터레이션12 설계 (wpf-it12-design.md)

> 대상: MCPhoto (WPF / .NET 8, MVVM=CommunityToolkit.Mvvm)
> 루트: `E:\Study\photobooth` · 빌드 baseline **0 error / 0 warning**, 테스트 **341 passed**
> 성격: **설계 문서(코드 구현 금지)**. 구현 단계는 `docs/design/wpf-it12-wbs.md` 참조.

---

## 0. 개요

이번 이터레이션은 **설정 화면의 권한 게이트 확대·레이아웃 정리·버전 표기 정리** 4건이다. 모두
기존에 이미 존재하는 메커니즘(QR 게이트, 디자인 시스템 Toggle, `IniBuildInfoService`)을 **그대로
확장/축소**하는 저위험 변경이며, 신규 서비스·신규 상태머신·비동기 흐름 추가가 **없다**.

| ID | 요구 요지 | 주 변경 파일 | 계층 |
|----|----------|-------------|------|
| R1 | 로그인 전용 설정 확대(거울모드·재촬영·필터 3종) — QR 게이트와 동일 메커니즘 | `SettingsViewModel.cs`, `SettingsView.xaml` | VM + View |
| R2 | 설정 레이아웃 재배치 — QR 전송·로컬 저장 → "출력·전송" 그룹으로 이동 | `SettingsView.xaml` | View |
| R3 | 게스트 hover 툴팁 "로그인 후 이용 가능합니다." (`ShowOnDisabled`) | `SettingsView.xaml` | View |
| R4 | 버전 표기에서 BuildDate 제외 (`v{Version} · {Site}`) | `IniBuildInfoService.cs` | Core |

**범위 밖(건드리지 않음)**: 컷별 재촬영(USER-DECISION 대기), bldinfo 값 관리(사용자), 비밀번호 암호화.

**전제/제약**: 디자인 시스템(`Themes/*.xaml`) 준수, MVVMTK0034 회피(관측 프로퍼티는 생성된 프로퍼티명으로
접근 — `_mirrorMode` 아닌 `MirrorMode`), 빌드 0 warning 유지, 파일 인코딩 **UTF-8(BOM 없음) 보존**(§7 확인).

---

## 1. 검증된 사실 (근거 file:line)

### 1.1 기존 QR 게이트 메커니즘 = R1의 모범(그대로 확장)

게스트 편집 차단은 **3지점**으로 구현되어 있다. R1은 동일 3지점을 확장할 뿐 새 메커니즘을 만들지 않는다.

1. **소스단 OFF 표시** — `SettingsViewModel.LoadSettings`, 로드 말미에 게스트면 강제 off
   (`src/MCPhoto.App/ViewModels/SettingsViewModel.cs:192-198`):
   ```csharp
   if (IsGuest)
   {
       EnableQrDelivery = false;
       SendPhoto = false;
       SendTimelapse = false;
   }
   ```
2. **저장 시 ini 원값 보존(클로버 금지)** — `SaveSettings`가 게스트면 해당 필드 미기록
   (`:250-255` QR 3필드, `:262` HostingBaseUrl, `:266` StorageBucket):
   ```csharp
   if (!IsGuest)
   {
       s.EnableQrDelivery = EnableQrDelivery;
       s.SendPhoto = SendPhoto;
       s.SendTimelapse = SendTimelapse;
   }
   ...
   if (!IsGuest) s.HostingBaseUrl = HostingBaseUrl;
   ...
   if (!IsGuest) s.StorageBucket = StorageBucket;
   ```
3. **컨트롤 Disable** — XAML에서 `IsEnabled="{Binding IsLoggedIn}"`
   (QR 토글 `src/MCPhoto.App/Views/SettingsView.xaml:195`, HostingBaseUrl TextBox `:312`, StorageBucket TextBox `:316`).

권한 프로퍼티: `IsLoggedIn => _shell.IsLoggedIn`(`SettingsViewModel.cs:64`), `IsGuest => !_shell.IsLoggedIn`(`:66`).
둘 다 **설정 진입 중 불변**(주석 `:63`) → `INotifyPropertyChanged` 불필요.

회귀 테스트: `Guest_Qr_Forced_Off`(`tests/MCPhoto.Tests/SettingsViewModelTests.cs:171-179`),
`Guest_Save_Preserves_Ini_Qr_And_Firebase`(`:181-198`).

### 1.2 게이트는 "편집 권한"만 제한 — 런타임 동작 불변

게이트는 **VM 계층에만** 존재한다. 도메인 모델 `AppSettings`에는 게이트가 없다 — 필드는 항상
직렬화/복원된다(`src/MCPhoto.Core/Settings/AppSettings.cs`의 `Clone()` `:176-206`, INI 저장/로드).
따라서 관리자가 켜둔 값은 ini에 그대로 남고, **촬영·결과 런타임은 `Settings.Current`(ini)를 읽으므로
게스트 세션도 관리자 설정값대로 동작**한다. 모델 계층 라운드트립 테스트
(`SettingsTests.cs`, `FiltersTests.cs`, `CutSelectViewModelTests.cs`)는 VM 게이트를 통과하지 않으므로
R1의 영향을 받지 않는다(확인: 이들 테스트는 `AppSettings`/`IniSettingsService`를 직접 사용).

### 1.3 R1이 깨뜨리는 기존 테스트 1건 (반드시 수정)

`Retake_Settings_Save_And_Load_RoundTrip`(`SettingsViewModelTests.cs:112-129`)는 **게스트 VM**으로
`RetakeEnabled=true; RetakeLimit=3` 저장 후 ini에서 값이 유지되는지 검증한다(주석 `:127` "게스트여도 촬영
옵션은 저장됨"). R1에서 `RetakeEnabled`(및 `RetakeLimit`)가 게스트 게이트 대상이 되면 게스트 저장 시
미기록 → 이 단언이 실패한다. **로그인 세션으로 전환**해 재작성해야 한다(§2.5).

> 코드 주석도 함께 갱신: `SaveSettings`의 "it11 #13: 촬영 옵션(게스트 게이트 대상 아님)"(`:247`)은 R1 이후
> 사실과 반대가 되므로 제거/수정.

### 1.4 레이아웃 현황

`SettingsView.xaml` [앱 설정] 카드는 2개 2열 블록 + 고급 섹션으로 구성:

- **Block 1**(`TwoColArea`, `x:Name` 기반 반응형 1열 폴백): 좌 `LeftCol`=촬영(`:64-131`),
  우 `RightCol`=장치·표시(`:137-225`). 우열에 **QR 전송(+사진/타임랩스 하위)**(`:187-216`)과
  **로컬 저장 토글**(`:217-224`)이 있다.
- **Block 2**(일반 Grid, 폴백 없음): 좌=출력·전송(출력 포맷/보관 시간/로컬 저장 경로, `:238-262`),
  우=필터(원본/흑백/밝게/뷰티, `:267-303`).
- **고급**(`:308-349`): 다운로드 페이지 Base URL / Storage 버킷 / 서버 연결 / 진단·상태 버튼.

반응형 폴백 코드비하인드 `OnTwoColSizeChanged`(`src/MCPhoto.App/Views/SettingsView.xaml.cs:15-33`)는
**`x:Name`(`LeftCol`/`RightCol`/`ColGap`)와 `TwoColArea.ColumnDefinitions[2]`만** 조작한다 — 개별
자식 컨트롤을 참조하지 않는다. 따라서 R2가 QR/로컬 저장을 `RightCol` 밖으로 옮겨도 폴백 로직은 무영향.

### 1.5 버전 표기(R4)

`IniBuildInfoService.DisplayText`(`src/MCPhoto.Core/Build/IniBuildInfoService.cs:28-37`)가
`v{Version}` · `Site` · `BuildDate`를 `"  ·  "`(양쪽 공백 2)로 조인한다. BuildDate 추가는 `:34`.
`Version`/`BuildDate`/`Site` 프로퍼티와 `bldinfo.ini` 키는 별도 존재(`:23-25`, `KeyBuildDate=:20`).
표기 소비 지점: `AppShellViewModel.VersionText => _buildInfo?.DisplayText`(`AppShellViewModel.cs:85`),
`MainWindow.xaml:84` 우하단 캡션. 테스트: `DisplayText_Joins_Present_Parts_Only`
(`tests/MCPhoto.Tests/BuildInfoServiceTests.cs:42-52`), 필드 로드 테스트 `Valid_Values_Are_Loaded`(`:27-39`).

### 1.6 디자인 시스템·인프라

- Toggle 스타일: `Themes/Controls.xaml:477-509`(`x:Key="Toggle"`), `IsEnabled=False` 트리거 존재(`:502-504`).
- `BasedOn="{StaticResource ...}"`로 테마 스타일을 파생하는 패턴은 이 파일 내에서 이미 사용
  (`SettingsView.xaml:9`, `:23` — `Text.Body`/`Text.Title` 파생). App 병합 딕셔너리(`App.xaml:13`
  → `Theme.xaml`)가 런타임 조회로 도달하므로 View 로컬 리소스에서 테마 키 `BasedOn` 안전.
- 헤드리스 XAML 회귀 테스트 인프라 존재(`tests/MCPhoto.Tests/XamlResourceTests.cs`) — STA 스레드에서
  창을 띄우지 않고 StaticResource 미해결을 검출. `DiagnosticsWindow`용 정적 키 검증 패턴(`:188-218`)을
  `SettingsView`에 복제 가능(§4.4, WBS Step 4 검증).
- 컨버터 등록: `App.xaml:21-33`(`BoolToVis` 등). 신규 컨버터 불필요.

---

## 2. R1 — 로그인 전용 설정 확대

### 2.1 게이트 대상 최종 집합

| 필드 | it11 이전 | it12(R1) | 비고 |
|------|----------|----------|------|
| `EnableQrDelivery` / `SendPhoto` / `SendTimelapse` | 게이트 O | 게이트 O(유지) | 기존 |
| `HostingBaseUrl` / `StorageBucket` | 게이트 O | 게이트 O(유지) | 기존(고급 섹션 TextBox) |
| **`MirrorMode`** | 게이트 X | **게이트 O(신규)** | 촬영 그룹 토글 |
| **`RetakeEnabled`** | 게이트 X | **게이트 O(신규)** | 촬영 그룹 토글(상위) |
| **`RetakeLimit`** | 게이트 X | **게이트 O(신규, 기본안)** | 재촬영 하위 — §2.4 결정 |
| **`FilterGrayscale` / `FilterBrightness` / `FilterBeauty`** | 게이트 X | **게이트 O(신규)** | 필터 그룹 토글 |

게이트 **비대상**(게스트도 편집·저장): `CutCount`, `CountdownSec`, `FlashMode`, `ShutterSound`,
`SaveLocalCopy`, `RetentionHours`, `LocalSavePath`, `OutputFormat`, `DisplayMode`, `CameraDevice`.
(요구 R1은 거울모드·재촬영·필터만 지정. 그 외는 현행 유지.)

### 2.2 런타임 동작 불변 (설계 명시 — R1 핵심)

R1 게이트는 **"설정 편집 권한"만** 제한한다. QR과 동일하게:

- 게스트 화면에서는 거울모드·재촬영·필터가 **OFF로 표시되고 컨트롤이 Disable**된다.
- 게스트가 [저장]해도 해당 필드는 **ini에 기록되지 않아 관리자 값이 보존**된다(클로버 금지).
- **실제 촬영·필터 런타임은 `Settings.Current`(ini)를 읽으므로**, 관리자가 켜둔 거울모드/재촬영/필터는
  **게스트 세션에서도 그대로 동작**한다. 게스트에게 off로 보이는 것은 "네가 못 바꾼다"는 편집 UI 표시일 뿐,
  기능이 꺼진다는 뜻이 아니다(QR과 동일 의미).

### 2.3 `LoadSettings` 변경 (소스단 OFF 표시)

게스트 강제 off 블록(`SettingsViewModel.cs:193-198`)을 확장:

```csharp
if (IsGuest)
{
    EnableQrDelivery = false;
    SendPhoto = false;
    SendTimelapse = false;
    // it12 R1: 편집 권한 게이트 확대(표시 전용 off, ini 원값은 SaveSettings에서 보존)
    MirrorMode = false;
    RetakeEnabled = false;
    FilterGrayscale = false;
    FilterBrightness = false;
    FilterBeauty = false;
}
```

- `RetakeLimit`는 int이며 재촬영 하위(상위 off 시 UI 숨김)이므로 강제하지 않는다 — 로드값 유지(무해).
- 이 블록은 `_normalizing=true` 구간(`:167-200`) 안에 있어 QR 연동 콜백이 억제된다. `MirrorMode`/
  `RetakeEnabled`/필터에는 `partial void On...Changed` 핸들러가 없어 부작용 없음(확인: `SettingsViewModel.cs`
  전체에 이들 프로퍼티의 `partial` 핸들러 부재).

### 2.4 `SaveSettings` 변경 (ini 원값 보존)

무조건 기록되던 필드를 게이트로 감싼다:

```csharp
// (기존) s.MirrorMode = MirrorMode;        → 게이트
if (!IsGuest) s.MirrorMode = MirrorMode;

// (기존) s.RetakeEnabled = RetakeEnabled;  → 게이트
// (기존) s.RetakeLimit   = RetakeLimit;    → 게이트(§ 결정)
if (!IsGuest)
{
    s.RetakeEnabled = RetakeEnabled;
    s.RetakeLimit = RetakeLimit;
}

// (기존) s.FilterGrayscale/Brightness/Beauty = ...  → 게이트
if (!IsGuest)
{
    s.FilterGrayscale = FilterGrayscale;
    s.FilterBrightness = FilterBrightness;
    s.FilterBeauty = FilterBeauty;
}
```

기존 QR 게이트 블록(`:250-255`)과 Firebase 게이트(`:262`, `:266`)는 그대로 둔다. 오래된 주석
`:247`("촬영 옵션(게스트 게이트 대상 아님)")은 삭제/수정.

**`RetakeLimit` 게이트 결정(기본안, USER-DECISION 아님)**: R1 요구는 `RetakeEnabled`만 명시하나,
`RetakeLimit`는 재촬영의 하위 설정이고 상위 off 시 UI가 숨겨진다(`SettingsView.xaml:119-120`).
`RetakeEnabled`만 게이트하고 `RetakeLimit`을 방치해도 클로버는 발생하지 않지만(게스트의 VM `RetakeLimit`은
로드값과 동일 → 재기록해도 무변), **재촬영을 한 단위로 묶어 함께 게이트**하는 편이 일관적이고 향후
회귀 위험이 낮다. → 기본안: `RetakeLimit`도 게이트.

### 2.5 테스트 영향·계획

- **수정(필수)**: `Retake_Settings_Save_And_Load_RoundTrip`(`SettingsViewModelTests.cs:112-129`)를
  로그인 세션으로 재작성(게스트 → `session.Login(User{Role=Admin/User})`). 로그인 사용자는 재촬영을
  저장할 수 있어야 한다는 의미로 단언 유지.
- **신규(권장)**:
  - `Guest_Save_Preserves_Ini_Mirror_Retake_Filters` — ini에 관리자값(MirrorMode=false로 명시 저장 후
    또는 기본 true 유지) 세팅 → 게스트 VM 저장 → ini 원값 보존 단언(클로버 금지). QR 보존 테스트와 동형.
  - `Guest_Gated_Fields_Forced_Off_On_Load` — 게스트 로드 시 `MirrorMode/RetakeEnabled/Filter*`가
    표시 off인지 단언(QR `Guest_Qr_Forced_Off`와 동형).
  - `LoggedIn_Saves_Mirror_Retake_Filters` — 로그인 VM에서 값 편집 → ini 기록 라운드트립.
- **무영향(확인)**: `SettingsTests.cs`/`FiltersTests.cs`/`CutSelectViewModelTests.cs`의 모델 라운드트립은
  VM 게이트 밖 → 변경 없음.

---

## 3. R2 — 설정 레이아웃 재배치

### 3.1 이동 대상

`Block 1` 우열(장치·표시)의 **QR 전송(+사진/타임랩스 하위)**과 **로컬 저장 토글**을 `Block 2` 좌열
(출력·전송)으로 이동한다. 나머지(카메라 장치, 표시 모드)는 장치·표시에 잔류.

### 3.2 재배치 후 도식 (권장안)

```
┌─────────────────────────────── [앱 설정] 카드 ───────────────────────────────┐
│ Block 1  (TwoColArea · 반응형 1열 폴백 — x:Name/코드비하인드 그대로)             │
│ ┌─ 좌 LeftCol: 촬영 ─────────────┐   ┌─ 우 RightCol: 장치·표시 ──────────────┐ │
│ │ 촬영 컷 수                       │   │ 카메라 장치 [▼] [↻][테스트]           │ │
│ │ 컷당 카운트다운(초)              │   │ (카메라 없음 안내 — 22px 예약)        │ │
│ │ 거울모드(좌우반전) *             │   │ 표시 모드 [▼]                         │ │
│ │ 플래시                          │   │                                       │ │
│ │ 셔터음                          │   │                                       │ │
│ │ 재촬영 사용 *                    │   │                                       │ │
│ │   ↳ 재촬영 횟수 제한(조건부)     │   │                                       │ │
│ └────────────────────────────────┘   └───────────────────────────────────────┘ │
│ ───────────────────────────── (GroupDivider) ─────────────────────────────────  │
│ Block 2  (일반 2열 · 폴백 없음)                                                  │
│ ┌─ 좌: 출력·전송 ────────────────┐   ┌─ 우: 필터 ────────────────────────────┐ │
│ │ 출력 포맷 [▼]                   │   │ 원본(항상 제공)  [ON·비활성]          │ │
│ │ QR 전송 *                       │   │ 흑백 *                                │ │
│ │   ↳ 사진 전송(조건부)           │   │ 밝게 *                                │ │
│ │   ↳ 타임랩스 전송(조건부)       │   │ 뷰티 *                                │ │
│ │ 로컬 저장                        │   │                                       │ │
│ │ 로컬 저장 경로 [_______] (전폭)  │   │                                       │ │
│ │ 보관 시간(1~72h)                 │   │                                       │ │
│ └────────────────────────────────┘   └───────────────────────────────────────┘ │
│ ───────────────────────────── (GroupDivider) ─────────────────────────────────  │
│ 고급: 다운로드 페이지 Base URL / Storage 버킷 / 서버 연결 / [진단·상태]           │
└──────────────────────────────────────────────────────────────────────────────┘
  * = 게스트 게이트 대상(OFF 표시 + Disable + hover 툴팁, §2·§4)
```

### 3.3 출력·전송 내부 순서 근거 (그룹 응집도)

출력→전송→저장→경로→보관의 자연스러운 읽기 순서:
1. **출력 포맷** (결과물 형태)
2. **QR 전송**(+사진/타임랩스 하위) — 원격 전달
3. **로컬 저장**(토글) — 로컬 보존
4. **로컬 저장 경로** — 3의 대상 위치(바로 아래 배치로 응집)
5. **보관 시간(1~72h)** — 저장/전송 결과물의 보관 정책(공통 tail)

로컬 저장 토글과 로컬 저장 경로가 인접해 응집도가 좋아진다(기존엔 다른 블록에 분리되어 있었음).

### 3.4 균형·응집 판단

- Block 1: 장치·표시가 QR/로컬 저장을 잃어 3행으로 짧아지지만, 카메라 행이 버튼 2개+안내 22px 예약으로
  세로가 높아 촬영열(6~7행)과 시각적 균형이 크게 깨지지 않는다.
- Block 2: 출력·전송이 4~7행(QR 하위 조건부), 필터 4행으로 준균형. QR 하위 토글은 조건부 노출이라
  평상시 높이 차이는 작다.
- 그룹 경계 재구성: "장치·표시"는 장치·표시만 남기고, "출력·전송"에 출력물의 산출·전달·저장을 모은다 —
  의미상 QR 전송과 로컬 저장은 출력·전송에 속하는 게 더 명확하다(요구 R2 취지와 일치).

### 3.5 바인딩 보존 표 (이동 시 유지되어야 하는 것)

| 이동 컨트롤 | 유지 바인딩 | 조건부 노출 |
|-------------|------------|-------------|
| QR 전송 토글 | `IsChecked={Binding EnableQrDelivery}`, `IsEnabled={Binding IsLoggedIn}` | — |
| ↳ 사진 전송 | `IsChecked={Binding SendPhoto}` | 부모 `Visibility={Binding EnableQrDelivery, Converter=BoolToVis}` |
| ↳ 타임랩스 전송 | `IsChecked={Binding SendTimelapse}` | 동상 |
| 로컬 저장 토글 | `IsChecked={Binding SaveLocalCopy}` | — |

VM(`SettingsViewModel`) 멤버는 이동과 무관하게 동일 — R2는 **XAML 트리 위치만** 변경, 바인딩 경로 불변.
QR 연동 정규화(`OnSendPhotoChanged`/`OnEnableQrDeliveryChanged`, `:204-222`)도 VM에 있어 무영향.

### 3.6 반응형 폴백 무영향 (근거)

`OnTwoColSizeChanged`(`SettingsView.xaml.cs:15-33`)는 `LeftCol`/`RightCol`/`ColGap`/`TwoColArea`의
`Grid.Column`/`Grid.Row`/`ColumnDefinitions[2]`만 조작 — 이동되는 자식(QR/로컬 저장)을 직접 참조하지
않는다. 이동 후에도 `x:Name` 3종이 유지되므로 폴백 정상. **주의**: Block 2는 폴백 대상이 아니므로(코드비하인드
미연결), 이동한 QR/로컬 저장은 좁은 폭에서 Block 2의 정적 2열 레이아웃을 따른다(기존 출력·전송/필터와 동일 취급).

---

## 4. R3 — 게스트 hover 툴팁

### 4.1 기술 이슈

비활성(`IsEnabled=False`) 컨트롤은 기본적으로 툴팁을 표시하지 않는다. 게스트 게이트 토글은 모두 Disable
상태이므로 `ToolTipService.ShowOnDisabled="True"`가 **필수**다. 또한 **게스트일 때만** 툴팁을 노출하고
로그인 시에는 노출하지 않아야 한다.

### 4.2 설계: 파생 스타일 `Toggle.Gated`

`SettingsView.xaml`의 `<UserControl.Resources>`에 테마 `Toggle`을 `BasedOn`으로 파생한 로컬 스타일 추가:

```xml
<!-- it12 R3: 게스트 게이트 토글 공용 스타일. 로그인 시 ToolTip 미설정(=툴팁 없음),
     게스트일 때만 안내 노출. 비활성 컨트롤에도 뜨도록 ShowOnDisabled=True. -->
<Style x:Key="Toggle.Gated" TargetType="ToggleButton" BasedOn="{StaticResource Toggle}">
    <Setter Property="ToolTipService.ShowOnDisabled" Value="True" />
    <Style.Triggers>
        <DataTrigger Binding="{Binding IsGuest}" Value="True">
            <Setter Property="ToolTip" Value="로그인 후 이용 가능합니다." />
        </DataTrigger>
    </Style.Triggers>
</Style>
```

동작 원리:
- **로그인**: `IsGuest=false` → DataTrigger 미발동 → `ToolTip`은 unset(null) → `ShowOnDisabled`가 true여도
  표시할 내용이 없어 **툴팁 없음**(요구 충족). 컨트롤도 활성.
- **게스트**: `IsGuest=true` → `ToolTip="로그인 후 이용 가능합니다."` 설정 + 컨트롤 Disable +
  `ShowOnDisabled` → hover 시 **툴팁 노출**.
- `IsGuest`는 VM 상수(진입 중 불변, `SettingsViewModel.cs:63`)라 `PropertyChanged` 불필요. DataTrigger의
  바인딩 소스는 ToggleButton이 상속한 DataContext(=`SettingsViewModel`).

키 이름 `Toggle.Gated`는 점 표기 관례(`Text.Body`, `Button.Primary`)와 일치, 기존 키와 충돌 없음(확인).

### 4.3 적용 대상 및 일관성 권고

요구는 거울모드·재촬영·QR 3개를 명시하나, **모든 게스트 게이트 토글에 동일 적용**을 권고한다(일관성):

| 토글 | Style | IsEnabled |
|------|-------|-----------|
| 거울모드 (`:90`) | `Toggle.Gated` | `{Binding IsLoggedIn}` (신규) |
| 재촬영 사용 (`:116`) | `Toggle.Gated` | `{Binding IsLoggedIn}` (신규) |
| QR 전송 (`:194-195`) | `Toggle.Gated` | `{Binding IsLoggedIn}` (기존 유지) |
| 흑백 (`:285`) | `Toggle.Gated` | `{Binding IsLoggedIn}` (신규) |
| 밝게 (`:293`) | `Toggle.Gated` | `{Binding IsLoggedIn}` (신규) |
| 뷰티 (`:301`) | `Toggle.Gated` | `{Binding IsLoggedIn}` (신규) |

**권고 이유**: 게스트가 흑백/밝게/뷰티를 hover했을 때만 아무 설명이 없으면 "왜 안 눌리지?" 혼란이
생긴다. 6개 모두 동일 안내를 주는 편이 더 일관적이고 학습 비용이 낮다.

**적용 제외(의도적)**:
- "원본(항상 제공)" 토글(`:276-277`)은 `IsChecked=True IsEnabled=False`로 **항상 비활성 표시자**(게스트
  게이트가 아니라 "원본은 늘 제공됨" 안내). `Toggle`(비파생) 유지, 툴팁 없음. → 게이트 토글로 오인해
  `Toggle.Gated`를 적용하지 말 것.
- QR 하위(사진/타임랩스), 재촬영 횟수 제한 콤보: 게스트에겐 부모 off로 **완전히 숨겨짐**
  (`Visibility` 바인딩) → 툴팁 대상 아님.
- 고급 섹션 TextBox(HostingBaseUrl/StorageBucket): 요구 범위(토글)에서 벗어나며 고급 섹션은 게스트
  발견성이 낮다. 기본안은 미적용. (원하면 동일 문구 툴팁을 TextBox에도 확장 가능 — §9 참고.)

### 4.4 XAML 회귀 안전망 (권장)

`Toggle.Gated`는 `BasedOn Toggle`을 참조한다. `XamlResourceTests`의 정적 키 검증 패턴(`:188-218`)을
`SettingsView`에 복제한 `SettingsView_StaticResource_Keys_Resolve_In_Theme` 테스트를 추가하면,
로컬 키(`RowLabel`/`SettingRow`/`GroupTitle`/`GroupDivider`/`Toggle.Gated`)와 App 컨버터 키를 제외한
모든 테마 참조가 해석되는지 headless로 검증할 수 있다(창 미표시). WBS Step 4 검증에 포함.

---

## 5. R4 — 버전 표기에서 BuildDate 제외

### 5.1 변경

`IniBuildInfoService.DisplayText`(`:28-37`)에서 BuildDate 조인(`:34`)만 제거:

```csharp
public string DisplayText
{
    get
    {
        var parts = new List<string> { $"v{Version}" };
        if (!string.IsNullOrWhiteSpace(Site)) parts.Add(Site);
        // it12 R4: BuildDate는 표기에서 제외(업데이트 지연 시 오래된 앱으로 보일 위험).
        //          BuildDate 프로퍼티/ini 키는 유지 — 표기에서만 뺀다.
        return string.Join("  ·  ", parts);
    }
}
```

- 결과: `v1.0.0  ·  Beta`(Site 있을 때), `v2.1.0`(Site 없을 때). 구분자는 기존 `"  ·  "` 유지.
- **보존**: `Version`/`BuildDate`/`Site` 프로퍼티(`:23-25`), `KeyBuildDate`(`:20`), `bldinfo.ini`의
  `BuildDate` 키, 로드 로직(`:58-59`) 모두 그대로. 모델·bldinfo.ini 불변.
- 인터페이스 doc comment(`IBuildInfoService.cs:19`)와 서비스 요약(`IniBuildInfoService.cs:27`)의
  예시 `"v1.0.0 · Beta · 2026-07-23"` → `"v1.0.0 · Beta"`로 갱신.

### 5.2 테스트

- **수정**: `DisplayText_Joins_Present_Parts_Only`(`BuildInfoServiceTests.cs:42-52`) — full 케이스 기대값
  `"v1.0.0  ·  Beta  ·  2026-07-23"` → `"v1.0.0  ·  Beta"`. verOnly 케이스 `"v2.1.0"`은 불변.
- **무영향(확인 단언)**: `Valid_Values_Are_Loaded`(`:27-39`)는 `BuildDate` 프로퍼티가 여전히 로드됨을
  검증 — R4 후에도 통과해야 한다(필드 보존 증거).

---

## 6. 스레딩 · 메모리 누수 · 성능

- **스레딩**: 변경 없음. R1은 동기 프로퍼티 대입(UI 스레드), 기존 async(`RefreshCamerasAsync` 등) 미변경.
  R2/R3/R4는 XAML/순수 문자열 로직 — Dispatcher/Task 신규 없음.
- **누수**: 신규 이벤트 구독 **없음**. R3의 `DataTrigger`는 `IsGuest`(상수) 원웨이 바인딩 — 약참조/구독
  해제 이슈 없음. `Toggle.Gated`는 스타일 리소스로 뷰 수명과 동일. VM에 `IDisposable` 추가 불필요.
- **성능**: 툴팁/스타일은 비용 무시 수준. 대량 컬렉션·가상화와 무관.

---

## 7. 리소스 · 인코딩 · 코딩 규칙

- **리소스 키**: 신규 키는 `Toggle.Gated` 1개(SettingsView 로컬). 테마·App 키와 충돌 없음(확인).
  신규 브러시/컨버터 없음(기존 팔레트·컨버터만 사용).
- **인코딩(중요)**: 편집 대상 파일은 모두 **UTF-8 BOM 없음**으로 확인됨
  (`SettingsViewModel.cs`/`IniBuildInfoService.cs`/테스트 `.cs`는 `usi...`로 시작, `SettingsView.xaml`/
  `Controls.xaml`은 `<`로 시작 — BOM `EF BB BF` 없음). **모든 편집에서 BOM을 추가하지 말 것**
  (Edit 도구는 기존 인코딩을 보존함). 한글 문자열(툴팁·주석) 포함 시에도 UTF-8 유지.
- **MVVMTK0034 회피**: R1 게이트 코드는 생성된 프로퍼티명(`MirrorMode`, `FilterGrayscale` 등)으로만
  접근 — 백킹 필드(`_mirrorMode`) 직접 접근 금지. 기존 `LoadSettings`/`SaveSettings`가 이미 이 규칙을
  따르므로 동형 유지.

---

## 8. 테스트 계획 요약

| 요구 | 테스트 | 유형 | 파일 |
|------|--------|------|------|
| R1 | `Retake_Settings_Save_And_Load_RoundTrip` (로그인으로 재작성) | 수정 | `SettingsViewModelTests.cs` |
| R1 | `Guest_Save_Preserves_Ini_Mirror_Retake_Filters` | 신규 | `SettingsViewModelTests.cs` |
| R1 | `Guest_Gated_Fields_Forced_Off_On_Load` | 신규 | `SettingsViewModelTests.cs` |
| R1 | `LoggedIn_Saves_Mirror_Retake_Filters` | 신규 | `SettingsViewModelTests.cs` |
| R2 | 빌드 + `SettingsView` headless 정적 키 검증(§4.4) | 신규/빌드 | `XamlResourceTests.cs` |
| R3 | `SettingsView` headless 정적 키 검증(`Toggle.Gated` 포함) | 신규 | `XamlResourceTests.cs` |
| R4 | `DisplayText_Joins_Present_Parts_Only` (기대값 갱신) | 수정 | `BuildInfoServiceTests.cs` |
| R4 | `Valid_Values_Are_Loaded` (BuildDate 프로퍼티 보존 확인) | 무변(통과 확인) | `BuildInfoServiceTests.cs` |

전체 회귀: `dotnet test` (baseline 341 → R1 신규 3 + R3/R4 조정으로 증가, 전부 green + 0 warning).

---

## 9. USER-DECISION / 기본안

이번 요구는 진짜 사용자 결정이 필요한 항목이 **없다**. 아래는 architect가 확정한 기본안(합리적 디폴트):

- **[기본안 확정] `RetakeLimit` 게이트**: 재촬영을 한 단위로 묶어 게이트(§2.4). 클로버 위험 0, 일관성 상승.
- **[기본안 확정] 툴팁 필터 확장**: 게스트 게이트 토글 6종 전부에 `Toggle.Gated` 적용(§4.3). 일관성.
- **[기본안 확정] 고급 TextBox 툴팁 미적용**: 요구 범위(토글) 준수, 발견성 낮음. 필요 시 확장 가능.
- **[기본안 확정] 출력·전송 내부 순서**: 출력 포맷 → QR → 로컬 저장 → 경로 → 보관 시간(§3.3). 조정 가능.

> 위 4건은 요구 R1~R3의 "부자연스러우면 재구성 가능/일관성 관점 판단해 권고" 지시에 따른 확정안이다.
> 사용자가 다른 배치/범위를 원하면 해당 지점만 조정하면 되며 나머지 설계는 불변.

---

## 10. 품질 자체 점검

- [x] 모든 게이트 대상이 3지점(Load/Save/XAML IsEnabled)에서 일관되게 처리됨
- [x] 바인딩·명령에 누락된 VM 멤버 없음(R2는 위치만 이동, 경로 불변)
- [x] 신규 이벤트 구독 없음 → 누수 위험 0
- [x] UI 스레드/백그라운드 경계 변경 없음
- [x] 리소스 키 충돌 없음(`Toggle.Gated` 신규 1개, 로컬)
- [x] 게스트/로그인 양측 툴팁 동작 정의(로그인 시 미노출 보장)
- [x] 런타임 동작 불변(편집 권한만 제한) 명시 — R1 핵심
- [x] 깨지는 기존 테스트 1건 식별·수정안 제시(§1.3)
- [x] BuildDate 필드/키 보존, 표기만 변경(§5)
- [x] 파일 인코딩(UTF-8 no BOM) 보존 명시
- [x] developer가 추가 질문 없이 구현 가능한 상세도(코드 스니펫·file:line 근거)

---

## 11. 권장 구현 순서

1. **R4**(Core, 최소·격리) — `IniBuildInfoService.DisplayText` + 테스트. 다른 요구와 무관, 빠른 green.
2. **R1 VM**(순수 C#) — `SettingsViewModel` 게이트 확대 + 영향 테스트 수정/신규. XAML 없이 단위 검증.
3. **R2**(XAML 레이아웃) — QR/로컬 저장 이동·재배치. 빌드 + headless XAML 로드.
4. **R1 XAML + R3**(XAML) — 게이트 토글 `IsEnabled` + `Toggle.Gated` 스타일·툴팁. 정적 키 검증.
5. **문서 동기화** — `docs/analysis/11-exe-app-features.md`(`:192-193`, `:257`),
   `docs/analysis/12-exe-app-settings-and-config.md`(`:154`)의 레이아웃·버전 표기 기술 갱신.

> 3·4는 같은 파일(`SettingsView.xaml`)을 순차 편집한다(선행: 3 → 4). 2는 1과 독립(병렬 가능).
> 상세 단계·검증 명령·완료 기준은 `docs/design/wpf-it12-wbs.md` 참조.
