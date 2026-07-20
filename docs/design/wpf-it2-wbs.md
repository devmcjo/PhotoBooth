# MC포토 — 이터레이션 2 구현 WBS

| 항목 | 값 |
|------|-----|
| 대상 | `MCPhoto.sln` (.NET 8 WPF) — 이터레이션 2(UI 재디자인 + 설정/로그인/관리자 UI) |
| 설계 근거 | `docs/design/wpf-it2-design.md`, `docs/prd/iteration-2-ui-and-settings.md`, `docs/prd/photobooth-prd.md` v2.7 |
| 형식 | `docs/templates/WBS_BLUEPRINT.md` 준수 |
| 작성일 | 2026-07-20 |
| 빌드 검증 기준 | `dotnet build MCPhoto.sln -c Release`(error 0, 변경 프로젝트 warning 0) / `dotnet test` |

> 각 Step은 self-contained다. fresh 에이전트가 그 Step과 `wpf-it2-design.md`만 읽고 실행할 수 있게 작성했다.
> **⚠️ 색상 방향: Direction A(라이트, 화이트+로즈 `#FF4D79`+민트 `#37C9B0`) 확정(2026-07-20).** 다크 계승 전제는 폐기. 색 토큰·치환은 `wpf-it2-design.md` §2.3(라이트 팔레트)·§2.3.1(대비 규칙)·부록 A(역할 전환 매핑)를 따른다. 다크→라이트는 값 복사가 아니라 **역할 반전**(흰 텍스트를 흰 배경에 두지 말 것).
> **모든 Step의 완료 기준은 headless(dotnet build/test·grep 정적확인)로만 판정한다.** UI 육안은 완료 기준에 넣지 않고 각 Step의 trigger/non-goal에 "사용자 실행 육안" 항목으로 분리했으며, 전체 육안 목록은 `wpf-it2-design.md` §10에 있다.
> ⚠️ **앱 실행 금지**(사용자 PC 사용 중 + UI 실행 차단 훅). 검증은 `dotnet build`/`dotnet test`/`grep`만.

---

## 검증된 사실 (verified facts)

- **VF-1**: MVP 15화면·12VM·상태머신·DI·전역예외 구현 완료(리뷰 PASS). (근거: `src/` 열람)
- **VF-2**: 스타일/템플릿 리소스 전무 — `App.xaml`에 컨버터·DataTemplate만, 색은 화면마다 리터럴 하드코딩. (근거: `App.xaml`, 전 View XAML)
- **VF-3**: 현재(MVP) 팔레트는 다크로 일관되나(`#141018`/`#241E30`/`#C44B9B`/`#F5F0FA` 등) **폐기 대상** — Direction A 라이트로 전면 교체. 리터럴이 화면마다 하드코딩됨은 유효(라이트 토큰 치환 대상). (근거: View XAML 대조 + Direction A 확정)
- **VF-5**: 상단 바 없음 — `MainWindow`는 ContentControl + 좌상단 롱프레스 히트영역만. (근거: `MainWindow.xaml`)
- **VF-6**: 설정 편집은 `AdminView` 안에만 있고 진입은 롱프레스+로그인뿐 → 게스트·user 접근 불가. (근거: `AdminView.xaml`, `AdminViewModel.OnEnterAsync`)
- **VF-7**: 계정 생성 UI 없음 — `CreateAsync` 구현은 있으나 호출 View/VM 없음. (근거: VM 전수, `AccountService.cs`)
- **VF-8**: `AccountService.CreateAsync`가 역할 권한 규칙 미강제(role 인자 그대로 저장). (근거: `AccountService.CreateAsync`)
- **VF-9**: 자기 비번 변경 UI 없음(`ChangePasswordAsync`만 존재). (근거: VM 전수)
- **VF-10**: 홈 [촬영하기] → `AppState.Login` 강제. (근거: `HomeViewModel.Start`)
- **VF-11**: `FrameSelectViewModel`이 로그인 유무로 기본/커스텀 프레임 이미 분기. (근거: `FrameSelectViewModel.OnEnterAsync`)
- **VF-12**: `AppSettings.OutputFormat`·`DisplayMode`·`StorageBucket`는 어떤 편집 UI에도 없음. (근거: `AppSettings.cs` vs `AdminView.xaml`)
- **VF-14**: 기존 테스트 자산 존재 — `tests/MCPhoto.Tests/`에 `AppStateTests`, `AccountTests`, `SettingsTests` 등. `AppStateTests.Session_Active_States_Identified`가 `AppState.Admin` 참조, `AccountTests.Offline_Create_Throws`가 `CreateAsync(id,pw)` 2인자 호출. (근거: 테스트 파일 열람)
- **VF-15**: `MainWindow`는 `PreviewMouseDown/KeyDown`으로 전 영역 유휴 리셋(`OnAnyUserActivity`), `AppShellViewModel.NotifyUserActivity` 호출. 상단 바 추가해도 유휴 리셋 자동 충족. (근거: `MainWindow.xaml.cs`)

## 미검증 가정 (open assumptions)

- **OA-1**: 스타일을 `ResourceDictionary`로 추출·병합해도 빌드 통과·화면 동등/개선 → **검증: Step 2**(빌드) + 사용자 육안(§design 10).
- **OA-2**: 상단 바 셸 오버레이가 상태머신과 충돌 없음 → **검증: Step 3**(`AppStateTests` 신규 + 빌드).
- **OA-3**: `AppState.Settings` 신설·"어디서든 진입/복귀"가 전이표로 표현 가능 → **검증: Step 4**(`AppStateTests`).
- **OA-4**: `CreateAsync` 역할 게이트 추가가 기존 시드/로그인/테스트를 깨지 않음 → **검증: Step 6**(`AccountTests` 개정 + 빌드).
- **OA-5**: 세로 레이아웃을 VSM/트리거로 대응(전용 UserControl 스왑 불필요) → **검증: Step 8**(빌드) + 사용자 육안.
- **OA-6**: 신규 노출 설정 항목(OutputFormat/DisplayMode/StorageBucket)이 기존 INI 라운드트립/Clamp로 저장·복원 → **검증: Step 5**(`SettingsTests` 확장).

> 모든 가정이 검증 Step에 매핑됨(완결성 게이트 통과).

---

## 단계 의존 그래프 (병렬 식별)

```
Step 1 (디자인 시스템 리소스 딕셔너리 골격)
  └─ Step 2 (컨트롤 스타일·템플릿 완성)
        ├─ Step 3 (상단 바 네비게이션 + 셸 확장)   ─┐
        ├─ Step 5 (설정 [앱설정] 섹션)             ─┤ (Step2 후)
        ├─ Step 7 (촬영 진입 흐름 변경)            ─┤
        └─ Step 8 (전 화면 디자인 치환 + 세로)     ─┘
Step 4 (상태머신 Settings 신설)  ← Step1 무관, 독립. Step3·5·7이 참조
Step 6 (계정 서비스 역할 게이트 + 계정/비번변경 UI) ← Step 4(Settings 상태), Step 5(설정 섹션)
Step 9 (통합 정리·롱프레스 제거·최종 빌드)         ← 전 단계
```

- **Step 4는 Step 1과 독립**(순수 로직) → 병렬 착수 가능. Step 3·5·7이 Step 4 산출(`AppState.Settings`)을 참조하므로 Step 4를 먼저 끝내는 것을 권장.

---

## Step 1: 디자인 시스템 리소스 딕셔너리 골격 (색·타이포·간격)

- **Context Brief**: 현재 앱은 스타일 리소스가 전무하고 색을 화면마다 리터럴로 하드코딩한다(VF-2). 재사용 가능한 디자인 시스템의 토대인 색 팔레트·브러시·타이포·간격 리소스를 만든다. 이 Step은 **원자 토큰만** 정의하고 컨트롤 스타일(버튼 등)은 Step 2에서 얹는다. 팔레트는 기존 값을 계승(VF-3)하며 상태·시맨틱 색을 보강한다.
- **대상 파일**: `src/MCPhoto.App/Themes/Colors.xaml`, `Brushes.xaml`, `Typography.xaml`, `Metrics.xaml`, `Theme.xaml`(신규), `src/MCPhoto.App/App.xaml`(MergedDictionaries 등록).
- **선행 조건**: 없음.
- **구현 내용**:
  - `Colors.xaml`: `wpf-it2-design.md` §2.3 표의 색을 `<Color x:Key="...">`로 정의(순수 Color).
  - `Brushes.xaml`: 각 Color를 참조하는 `SolidColorBrush`(키 = `Brush.Bg`, `Brush.Surface`, `Brush.Accent` … 설계 §2.3 전체). Scrim/Glass는 알파 포함.
  - `Typography.xaml`: `FontFamily x:Key="Font.Primary"`(`Segoe UI, Malgun Gothic`) + 설계 §2.4의 `Text.Display/H1/H2/Title/Body/Label/Caption` `Style TargetType=TextBlock`(x:Key, Foreground/FontSize/FontWeight/FontFamily).
  - `Metrics.xaml`: 설계 §2.5의 Spacing(double/Thickness), CornerRadius, Touch 크기, `Shadow.Card`(DropShadowEffect) 리소스.
  - `Theme.xaml`: 위 4개를 `MergedDictionaries`로 묶음.
  - `App.xaml`: `Application.Resources`를 `ResourceDictionary` + `MergedDictionaries`(`Themes/Theme.xaml`)로 감싸고, **기존 컨버터·DataTemplate는 그 아래에 그대로 유지**(제거 금지).
- **검증 명령**: `dotnet build MCPhoto.sln -c Release`(error 0, App 프로젝트 warning 0). `grep`로 `Themes/Theme.xaml` 참조가 `App.xaml`에 존재 확인.
- **완료 기준**:
  - [관측] 빌드 error 0·warning 0. `App.xaml`에 `Themes/Theme.xaml` MergedDictionary 등록, 기존 컨버터 6개·DataTemplate 12개 리소스 키가 여전히 존재(grep). 새 브러시/타이포/메트릭 키가 `Theme.xaml` 병합으로 해석됨(빌드가 XAML 참조 무결성 보증).
  - [non-goal] 이 Step에서 **화면 XAML은 수정하지 않는다**(치환은 Step 8). 기존 DataTemplate/컨버터 리소스 삭제·이름변경 없음.
  - [trigger] 리소스는 App 시작 시 병합만 됨. 시각적 변화는 사용자 실행 시에만 관측(이 Step 완료 기준 아님).
- **롤백**: `Themes/` 삭제 + `App.xaml`을 원복(MergedDictionaries 제거, 기존 리소스만).
- [ ] 완료

---

## Step 2: 컨트롤 스타일·템플릿 완성 (버튼/입력/토글/카드)

- **Context Brief**: Step 1의 색·타이포·간격 토큰 위에, 재사용 컨트롤 스타일을 정의한다. 키오스크 터치 기준(최소 48px, 100ms 피드백)과 상태별(hover/press/disabled/focus) 트리거를 포함한다. 이후 모든 화면이 이 키를 참조한다.
- **대상 파일**: `src/MCPhoto.App/Themes/Controls.xaml`(신규), `Theme.xaml`(Controls 병합 추가).
- **선행 조건**: Step 1.
- **구현 내용**: 설계 §2.6 표의 스타일을 정의.
  - 버튼(키 기반): `Button.Primary`, `Button.Secondary`, `Button.Ghost`, `Button.Danger`, `Button.Icon`, `Button.Filter`, `Button.FrameCard`. 각 `ControlTemplate`에 `Border`(CornerRadius=Metrics 키) + `ContentPresenter`, `Trigger`(IsMouseOver→hover 브러시, IsPressed→press 브러시+`ScaleTransform` 0.98, IsEnabled=false→Disabled 브러시). Primary 높이 `Touch.CTA`(56), Icon 56×56.
  - 입력(암묵 스타일): `TextBox`/`PasswordBox` TargetType 전역 스타일(`Surface.Alt` 배경, `Radius.S`, focus 시 `Accent` 테두리), `ComboBox`·`CheckBox` 기본 룩.
  - `Toggle`(ToggleButton 기반 스위치 템플릿, x:Key): off=`Surface.Alt`, on=`Accent`, 원형 thumb 애니메이션(`Storyboard`), 최소 히트영역 48.
  - `Card`(Border 스타일 x:Key): `Surface` 배경, `Radius.M`, `Border` 1px, 패딩 `Space.L`, `Shadow.Card`.
  - `ScreenTitle`(TextBlock 스타일, `Text.H1` 기반).
  - 애니메이션 리소스(설계 §2.7): 버튼 press scale, 카운트다운 pulse, 플래시 storyboard는 각 화면에서 참조하도록 공용 `Storyboard`/`Style`로 정의(가능 범위).
- **검증 명령**: `dotnet build MCPhoto.sln -c Release`(error 0, warning 0). `grep`로 `Controls.xaml`에 `Button.Primary`·`Toggle`·`Card` 키 존재 확인.
- **완료 기준**:
  - [관측] 빌드 error 0·warning 0. `Controls.xaml`에 설계 §2.6의 모든 스타일 키 정의됨(grep 확인: `Button.Primary`, `Button.Secondary`, `Button.Ghost`, `Button.Danger`, `Button.Icon`, `Button.Filter`, `Button.FrameCard`, `Toggle`, `Card`, `ScreenTitle`). `Theme.xaml`에 `Controls.xaml` 병합.
  - [non-goal] 화면 XAML 미수정(Step 8). 암묵 스타일은 입력류(TextBox/PasswordBox/ComboBox/CheckBox)에만 적용하고 Button에는 암묵 스타일을 두지 않음(기존 인라인 버튼이 의도치 않게 바뀌지 않도록).
  - [trigger] 스타일 적용은 각 화면이 `Style="{StaticResource ...}"`로 명시 참조할 때만(버튼). 시각 확인은 사용자 실행 시.
- **롤백**: `Controls.xaml` 삭제 + `Theme.xaml`에서 병합 제거(Step 1 상태로 무해 복귀).
- [ ] 완료

---

## Step 3: 상단 바 네비게이션 + 셸(AppShellViewModel) 확장

- **Context Brief**: 현재 `MainWindow`는 콘텐츠 + 좌상단 롱프레스 히트영역뿐이다(VF-5). 요구 1: 좌상단 로그인/계정 버튼, 우상단 설정 버튼을 오버레이 상단 바로 추가한다. 셸 VM에 계정 상태·상단 바 가시성·오버레이 네비게이션을 노출한다(설계 §3). 롱프레스 제거는 Step 9에서 마무리(여기선 상단 바 추가에 집중).
- **대상 파일**: `src/MCPhoto.App/MainWindow.xaml`(상단 바 오버레이 Grid 추가), `src/MCPhoto.App/AppShellViewModel.cs`(프로퍼티·커맨드 추가), (신규 사용자 컨트롤 원하면) `src/MCPhoto.App/Views/TopBarView.xaml`.
- **선행 조건**: Step 2(버튼 스타일), Step 4(`AppState.Settings` — Settings 네비 대상). Step 4 미완 시 설정 버튼 커맨드는 스텁으로 두고 Step 4 후 연결.
- **구현 내용**:
  - `AppShellViewModel`에 추가: `[ObservableProperty] User? _currentUser`(또는 Session 변경 통지 래핑) + 파생 `IsLoggedIn`, `AccountLabel`("로그인"/계정 ID), `IsPower`; `bool IsTopBarVisible`(현재 상태 기반 — Capture/Qr에서 false); 커맨드 `OpenSettingsCommand`(→`NavigateToOverlayAsync(AppState.Settings)`), `OpenAccountCommand`(비로그인→Login 오버레이, 로그인→팝오버 토글), `LogoutCommand`.
  - `NavigateToOverlayAsync(AppState)`: 현재 상태를 `_returnState`에 저장 후 전이. `ReturnFromOverlay()`: `_returnState`로 복귀(없으면 Home). (설계 §5.3)
  - `MainWindow.xaml`: `RootGrid`에 상단 바 레이어 추가(`VerticalAlignment=Top`, 높이 ~72). 좌=`Button.Icon`(AccountLabel, `OpenAccountCommand`) + 계정 팝오버(Popup: 역할·비번변경·로그아웃·(power)관리자). 우=`Button.Icon`(설정 아이콘, `OpenSettingsCommand`). `Visibility`는 `IsTopBarVisible` 바인딩.
  - 상단 바 콘텐츠가 화면 콘텐츠를 가리지 않도록: 정적 화면은 상단 패딩을 갖거나 상단 바가 반투명 오버레이. (설계 §3.1)
  - `CurrentUser` 통지: `Session.CurrentUser` 변경 시 셸 프로퍼티 갱신(로그인/로그아웃 시 `OnPropertyChanged`).
- **검증 명령**: `dotnet build MCPhoto.sln -c Release`(error 0, warning 0). `dotnet test --filter AppStateTests`(신규 케이스: `IsTopBarVisible` 계산 로직을 순수 메서드로 뽑아 테스트 — 예: Capture/Qr=false, Home/FrameSelect/Settings=true).
- **완료 기준**:
  - [관측] 빌드 통과. `AppShellViewModel`에 `IsLoggedIn`/`AccountLabel`/`IsTopBarVisible`/`OpenSettingsCommand`/`OpenAccountCommand` 존재(grep). 상단 바 가시성 로직 단위 테스트 통과(Capture·Qr에서 숨김, 정적 화면에서 표시).
  - [non-goal] 상단 바는 촬영(Capture)·QR 팝업에서 **표시되지 않는다**(테스트로 고정). 상단 바 버튼이 상태머신 불법 전이를 유발하지 않음(오버레이 네비만).
  - [trigger] 설정 페이지 이동은 설정 버튼 클릭 시에만, 계정 팝오버는 계정 버튼 클릭 시에만. 육안(버튼 위치·팝오버)은 사용자 실행 시 확인(§design 10).
  - [사용자 확인 필요] 상단 바 버튼 위치·터치·팝오버 표시(design §10-2).
- **롤백**: `MainWindow.xaml` 상단 바 레이어 제거 + `AppShellViewModel` 추가분 revert(Step 2 상태).
- [ ] 완료

---

## Step 4: 상태머신 — Settings 상태 신설 + 오버레이 진입/복귀 규칙

- **Context Brief**: 요구는 새 설정 페이지(어디서든 진입)와 촬영 게스트 직행을 요구한다. 순수 로직인 `SessionStateMachine`에 `AppState.Settings`를 추가하고 전이 규칙을 개정한다(설계 §5.2). `AppState.Admin`은 설정 페이지로 흡수되므로 제거한다. 이 Step은 로직·enum·테스트만 다루며 UI는 Step 3·5가 소비한다.
- **대상 파일**: `src/MCPhoto.Core/Navigation/AppState.cs`(Settings 추가, Admin 제거), `src/MCPhoto.Core/Navigation/SessionStateMachine.cs`(전이표·특례 개정), `tests/MCPhoto.Tests/AppStateTests.cs`(개정), `src/MCPhoto.App/AppShellViewModel.cs`(CreateViewModel의 Admin→Settings 매핑), `src/MCPhoto.App/App.xaml`(DataTemplate Admin→Settings).
- **선행 조건**: 없음(Step 1과 독립).
- **구현 내용**:
  - `AppState`: `Settings` 추가. `Admin` **제거**(설계 §4.5 결정). `UserMgmt`·`FrameEditor` 유지.
  - `SessionStateMachine.Forward` 개정(설계 §5.2 diff): `Home={FrameSelect, Login, Settings}`, `Login={FrameSelect, FrameEditor, Settings}`, `Settings={Login, UserMgmt, FrameEditor}`, `UserMgmt={Settings}`, `FrameEditor={FrameSelect, Settings, Login}`. `Admin` 항목 삭제.
  - `CanTransition` 특례: `to==Home` 항상 허용(기존) + `to==Settings` 항상 허용 + `to==Login` 항상 허용(상단 바 오버레이 진입, 설계 §5.2). 특례는 이 3개로 한정.
  - `IsSessionActive`: `Settings`·`Login`을 유휴 감시 **비대상**으로(추가). `FrameEditor` 등 기존 유지. `Admin` 제거로 참조 삭제.
  - `AppShellViewModel.CreateViewModel`·`App.xaml` DataTemplate: `AdminViewModel`→`SettingsViewModel` 매핑으로 교체(SettingsViewModel은 Step 5에서 실체화 — 이 Step에서는 컴파일 위해 최소 스텁 또는 Step 5와 병합 진행).
  - **테스트 개정(VF-14)**: `AppStateTests.Session_Active_States_Identified`의 `AppState.Admin` 참조를 제거하고 `Settings`(비대상)·`Login`(비대상) 케이스 추가. 신규: `Settings_Reachable_From_Anywhere`(모든 from→Settings 합법), `Login_Reachable_From_Anywhere`, `Home_FrameSelect_Legal`(Home→FrameSelect 합법=게스트 직행 전제).
- **검증 명령**: `dotnet test --filter AppStateTests`(개정·신규 케이스 통과). `dotnet build -c Release`(Admin 심볼 잔존 참조 0 — 빌드가 보증).
- **완료 기준**:
  - [관측] `AppStateTests` 전 케이스 통과. 모든 상태에서 `CanTransition(from, Settings)`·`CanTransition(from, Login)`·`CanTransition(from, Home)`가 true. `Home→FrameSelect` true. `AppState.Admin` 심볼이 코드베이스에서 제거됨(grep `AppState.Admin` = 0, 빌드 성공).
  - [non-goal] 촬영 흐름(Guide→Capture→CutSelect→Result) 전이는 **변경되지 않음**(기존 테스트 그대로 통과). `to==Settings/Login/Home` 외의 불법 전이는 여전히 거부(예: `Home→Capture` false).
  - [trigger] 상태 전이는 `NavigateAsync`/`NavigateToOverlayAsync` 호출 시에만. 특례 3개 외 진입은 Forward 표에만 의존.
- **롤백**: 이 Step 커밋 revert(AppState/StateMachine/테스트 원복). Admin 상태 복원.
- [ ] 완료

---

## Step 5: 설정 페이지 신설 — [앱 설정] 섹션 (게스트 포함, AppSettings 전 항목)

- **Context Brief**: 최우선 결함(요구 2.1) — 설정을 수정할 접근 가능한 UI가 없다. 게스트 포함 누구나 `AppSettings` 전 항목을 편집·저장할 수 있는 설정 페이지를 만든다. 기존 `AdminViewModel`의 설정 편집 로직(LoadSettings/SaveSettings)을 `SettingsViewModel`로 승격하고, 미노출 항목(OutputFormat/DisplayMode/StorageBucket, VF-12)을 추가한다. 저장은 기존 `ISettingsService.Save`(INI flush) 그대로.
- **대상 파일**: `src/MCPhoto.App/ViewModels/SettingsViewModel.cs`(신규, AdminViewModel 로직 승격), `src/MCPhoto.App/Views/SettingsView.xaml`(+`.xaml.cs`, 신규), `src/MCPhoto.App/ServiceRegistration.cs`(SettingsViewModel 등록), `tests/MCPhoto.Tests/SettingsTests.cs`(신규 항목 라운드트립 확장).
- **선행 조건**: Step 2(스타일), Step 4(`AppState.Settings`).
- **구현 내용**:
  - `SettingsViewModel`: `AdminViewModel`의 앱설정 필드·`LoadSettings`/`SaveSettings`를 이관·확장. 추가 필드: `OutputFormat`(JPG/PNG), `DisplayMode`(Fullscreen/Windowed), `StorageBucket`. 저장 시 `AppSettings.Clamp()` 호출 후 `_settings.Save()`. 저장 성공 시 `SavedNotice`(Success, 3초 후 소멸 로직은 View 스토리보드/타이머).
  - 섹션 노출 플래그: `IsGuest`/`IsLoggedIn`/`IsPower`(`Session.CurrentUser` 기반). [앱 설정] 섹션은 항상 표시. [계정]·[관리자] 섹션의 실 구현은 Step 6, 여기서는 빈 컨테이너 + 조건부 Visibility 골격만.
  - `SettingsView.xaml`: 설계 §4.1 구조(스크롤 + 섹션 카드 스택). [앱 설정] 카드에 설계 §4.2 표의 전 항목 컨트롤(Toggle/ComboBox/Slider/TextBox), [저장]·[닫기](→`ReturnFromOverlay`). 디자인 시스템 스타일 사용.
  - `ServiceRegistration`: `services.AddTransient<SettingsViewModel>()` 추가(AdminViewModel 등록은 Step 9에서 제거).
  - **테스트(OA-6)**: `SettingsTests`에 OutputFormat·DisplayMode·StorageBucket 저장→로드 라운드트립 케이스 추가(값 유지). 잘못된 값 클램프 유지 확인.
- **검증 명령**: `dotnet test --filter SettingsTests`(신규 라운드트립 포함 통과). `dotnet build -c Release`(error 0, warning 0). `grep`로 `SettingsView.xaml`에 OutputFormat·StorageBucket·MirrorMode 바인딩 존재 확인.
- **완료 기준**:
  - [관측] `SettingsTests` 통과(신규 3항목 저장→새 인스턴스 로드 시 값 유지, 범위 초과 클램프). 빌드 통과. `SettingsView`에 AppSettings 전 항목(설계 §4.2 표)의 바인딩이 존재(grep: `CutCount`, `CountdownSec`, `MirrorMode`, `FlashMode`, `OutputFormat`, `EnableQrDelivery`, `SaveLocalCopy`, `LocalSavePath`, `RetentionHours`, `CameraDevice`, `DisplayMode`, `HostingBaseUrl`, `StorageBucket`).
  - [non-goal] 저장은 [저장] 버튼에서만(입력 중 실시간 파일쓰기 없음). [계정]·[관리자] 섹션 실기능은 이 Step 아님(Step 6). 게스트가 [관리자] 섹션을 볼 수 없음(Visibility 골격).
  - [trigger] INI 저장은 SaveSettings 커맨드 실행 시에만. 설정 페이지 진입은 상단 바 설정 버튼(Step 3) 또는 `Home→Settings` 전이 시.
  - [사용자 확인 필요] 게스트 설정 수정→저장→재시작 복원(design §10-4).
- **롤백**: 신규 파일 삭제 + ServiceRegistration/App.xaml에서 Settings 매핑 제거 + SettingsTests 신규 케이스 제거.
- [ ] 완료

---

## Step 6: 계정 서비스 역할 게이트 + 설정 [계정]·[관리자] 섹션 (비번변경·계정생성·사용자관리)

- **Context Brief**: 요구 2.2(자기 비번 2회 확인 변경)·2.3(계정 생성 역할 규칙, 사용자 관리)이 미구현이다(VF-7·8·9). `AccountService.CreateAsync`에 호출자 역할 게이트를 강제(설계 §7)하고, 설정 페이지의 [계정]·[관리자] 섹션을 실체화한다. 기존 `UserMgmtViewModel`은 재사용한다.
- **대상 파일**: `src/MCPhoto.Core/Accounts/IAccountService.cs`(CreateAsync 시그니처), `src/MCPhoto.Firebase/AccountService.cs`(게이트 구현), `src/MCPhoto.App/ViewModels/SettingsViewModel.cs`(계정/관리자 로직 추가), `src/MCPhoto.App/Views/SettingsView.xaml`([계정]·[관리자] 카드), `src/MCPhoto.App/Views/UserMgmtView.xaml`(디자인 적용·설정 하위 배치), `tests/MCPhoto.Tests/AccountTests.cs`(게이트 케이스 + 기존 `Offline_Create_Throws` 시그니처 갱신).
- **선행 조건**: Step 4(Settings/UserMgmt 상태), Step 5(SettingsViewModel·SettingsView 골격).
- **구현 내용**:
  - `IAccountService.CreateAsync`: `(string id, string password, UserRole role, UserRole actingRole, CancellationToken ct=default)`로 확장(설계 §7).
  - `AccountService.CreateAsync`: 규칙 강제(admin→{user,manager}, manager→{user}, 그 외 거부; admin→admin 거부). 위반 시 `UnauthorizedAccessException`. Firebase 미초기화 시 기존 `InvalidOperationException` 유지(단, 게이트 검사를 먼저 수행해 권한 위반이 우선 예외). `EnsureSeedAccountAsync`는 게이트 우회(내부).
  - `SettingsViewModel` [계정] 섹션: `IsLoggedIn`일 때 비번 변경 카드 로직 — 새 비번 + 확인 2개 입력(View의 PasswordBox는 바인딩 불가라 code-behind에서 VM 메서드로 전달, 기존 AdminView.xaml.cs `OnPasswordChanged` 패턴 재사용). 불일치·빈 값 검사 후 `ChangePasswordAsync(CurrentUser.Id, newPw)`. 결과 메시지(Danger/Success).
  - `SettingsViewModel` [관리자] 섹션: `IsPower`일 때 표시. `CreatableRoles`(로그인 역할로 산출: admin→[User,Manager], manager→[User]) 바인딩. 계정 생성 커맨드가 `CreateAsync(newId, newPw, selectedRole, actingRole: CurrentUser.Role)` 호출. 미로그인/비power 시 "관리자 로그인" 카드(→Login 오버레이). [사용자 관리]→`NavigateAsync(AppState.UserMgmt)`. [앱 종료] 이관(`Application.Current.Shutdown`).
  - `UserMgmtView`: 디자인 시스템 스타일 적용, [뒤로]=`AppState.Settings`(Step 4에서 전이 이미 허용). VM은 그대로.
  - **테스트(OA-4)**: `AccountTests`에 게이트 케이스 추가(admin→user OK, admin→manager OK, admin→admin 거부, manager→user OK, manager→manager 거부, user→any 거부). 기존 `Offline_Create_Throws`를 새 시그니처(actingRole=Admin)로 갱신 — 미초기화 시 여전히 예외.
- **검증 명령**: `dotnet test --filter AccountTests`(게이트 케이스 + 갱신 통과). `dotnet build -c Release`(error 0, warning 0). `grep`로 `SettingsView.xaml`에 비번변경(2개 PasswordBox)·계정생성·역할 ComboBox 존재 확인.
- **완료 기준**:
  - [관측] `AccountTests` 통과: `CreateAsync(role=Manager, actingRole=Admin)` 성공, `actingRole=Manager, role=Manager` `UnauthorizedAccessException`, `actingRole=Admin, role=Admin` 거부, `actingRole=User` 거부. 빌드 통과. SettingsView에 계정/관리자 섹션 컨트롤 존재(grep).
  - [non-goal] `user`·게스트는 [관리자] 섹션을 볼 수 없음(Visibility). manager는 역할 선택에 Manager/Admin이 **나타나지 않음**(`CreatableRoles`). 비번 변경은 두 입력이 일치할 때만 적용(불일치 시 서비스 호출 없음). 시드 admin 생성 경로는 게이트에 막히지 않음(EnsureSeed 우회).
  - [trigger] 계정 생성은 [생성] 버튼 + 유효 입력 + 권한 통과 시에만. 비번 변경은 [변경] 버튼 + 2회 일치 시에만. 사용자 관리 진입은 power [사용자 관리] 클릭 시에만.
  - [사용자 확인 필요] power 로그인→계정 생성(역할 규칙)·비번 2회 확인 변경 동작(design §10-4).
- **롤백**: 이 Step 커밋 revert(CreateAsync 시그니처·게이트·UI·테스트 원복). Step 5의 빈 섹션 골격 상태로 복귀.
- [ ] 완료

---

## Step 7: 촬영 진입 흐름 변경 (게스트 자동 진입, 로그인/게스트 선택 제거)

- **Context Brief**: 요구 3 — 홈 [촬영하기]가 로그인/게스트 선택을 강제한다(VF-10). 선택 없이 게스트로 프레임 선택에 직행하도록 바꾼다. `FrameSelectViewModel`은 이미 로그인 유무로 분기하므로(VF-11) 로직 재사용. 기존 로그인 화면은 상단 바(Step 3)·프레임 선택 유도로 진입하는 로그인 전용 화면으로 축소한다.
- **대상 파일**: `src/MCPhoto.App/ViewModels/HomeViewModel.cs`(Start 대상 변경), `src/MCPhoto.App/ViewModels/LoginGuestViewModel.cs`(게스트 커맨드 제거·복귀 로직), `src/MCPhoto.App/Views/LoginGuestView.xaml`(게스트 버튼 제거·디자인), `src/MCPhoto.App/AppShellViewModel.cs`(로그인 성공 후 복귀).
- **선행 조건**: Step 2(스타일), Step 3(오버레이 네비/복귀), Step 4(전이표).
- **구현 내용**:
  - `HomeViewModel.Start()`: `NavigateAsync(AppState.Login)` → `NavigateAsync(AppState.FrameSelect)`. `Session.Reset()` 유지(게스트=CurrentUser null).
  - `LoginGuestViewModel`: `ContinueAsGuestCommand` 제거(참조·버튼 정리). `LoginCommand` 성공 시 `Session.CurrentUser` 설정 후 `_shell.ReturnFromOverlay()`(상단 바 진입 시 원 화면 복귀; 프레임 선택 유도 진입 시 `FrameSelect` 재진입해 커스텀 프레임 로드). `Cancel`은 `ReturnFromOverlay`.
  - `LoginGuestView.xaml`: "게스트로 계속" 버튼 삭제, 제목을 "로그인"으로, 디자인 시스템 적용. (리네이밍은 선택 — 파일명 유지해도 무방, 리스크 최소 위해 유지 권장.)
  - `AppShellViewModel`: 로그인 오버레이 진입 시 `_returnState` 저장(Step 3에서 구현됨), 성공 후 복귀. 프레임 선택에서 로그인했다면 `FrameSelect`로 복귀(재진입 시 `OnEnterAsync`가 커스텀 프레임 로드).
- **검증 명령**: `dotnet build -c Release`(error 0, warning 0). `dotnet test --filter AppStateTests`(Home→FrameSelect 합법 케이스는 Step 4에서 추가됨, 유지 통과). `grep`로 `HomeViewModel`에 `AppState.FrameSelect` 전이, `LoginGuestView.xaml`에 "게스트" 문자열 부재 확인.
- **완료 기준**:
  - [관측] 빌드·`AppStateTests` 통과. `HomeViewModel.Start`가 `AppState.FrameSelect`로 전이(grep). `LoginGuestViewModel`에 `ContinueAsGuest` 심볼 없음(grep=0). `LoginGuestView.xaml`에 게스트 버튼 없음.
  - [non-goal] 게스트 세션은 로그인 없이 프레임 선택→촬영 진행 가능(FrameSelectViewModel 기존 분기 유지, 기본 프레임만). 로그인은 여전히 커스텀 프레임/계정 기능의 전제(제거 아님). 홈 외 진입점(상단 바 로그인)은 유지.
  - [trigger] 촬영 세션 시작은 홈 [촬영하기] 클릭 시 FrameSelect 직행. 로그인은 상단 바/프레임 선택 유도 시에만.
  - [사용자 확인 필요] 홈→촬영하기→선택 없이 프레임 선택(게스트) 직행(design §10-3).
- **롤백**: 이 Step 커밋 revert(HomeViewModel/LoginGuestViewModel/View 원복). 로그인/게스트 선택 화면 복원.
- [ ] 완료

---

## Step 8: 전 화면 디자인 시스템 치환 + 가로/세로 레이아웃

- **Context Brief**: 요구 4 — UI 전면 재디자인. Step 1·2의 디자인 시스템을 기존 전 화면에 적용한다. 색 리터럴을 브러시 키로, 인라인 버튼 속성을 스타일 키로 치환하고(설계 §8·부록 A), 주요 화면에 가로/세로 레이아웃 대응을 넣는다. 신규 화면(Settings/Login/상단 바)은 이미 디자인 적용됨(Step 3·5·6·7).
- **대상 파일**: `src/MCPhoto.App/Views/HomeView.xaml`, `FrameSelectView.xaml`, `GuideView.xaml`, `CaptureView.xaml`, `CutSelectView.xaml`, `ResultView.xaml`, `QrPopupView.xaml`, `DoneView.xaml`, `FrameEditorView.xaml`, `PreviewView.xaml`, `UserMgmtView.xaml`(디자인 미적용분), `MainWindow.xaml`(배경 토큰화). `AppShellViewModel.cs`(Orientation 노출).
- **선행 조건**: Step 2. (신규 화면 관련은 Step 3·5·6·7.)
- **구현 내용**:
  - 색 리터럴 → 브러시 키 치환(부록 A 매핑표). 인라인 `Background/Foreground/Padding/FontSize` 버튼 → `Style="{StaticResource Button.Primary/Secondary/Ghost/...}"`. 제목 TextBlock → `Text.H1` 스타일. 필터 칩 → `Button.Filter`(선택 표시). 프레임 카드 → `Button.FrameCard`/`Card`.
  - CaptureView: 카운트다운 pulse·플래시 storyboard 적용(설계 §2.7). 촬영 배경은 몰입 위해 검정 유지(부록 A 예외 명시).
  - `AppShellViewModel`: `MainWindow.SizeChanged`→`Orientation`(Landscape/Portrait) 노출. 주요 화면(ResultView·SettingsView·FrameEditorView)에 `DataTrigger`(Orientation)로 2열↔2행 재배치. 나머지는 중앙 정렬 폴백(OA-5 완화책).
  - 전이 페이드(설계 §2.7): `MainWindow`의 `ContentControl` 콘텐츠 교체 시 opacity 페이드(스타일/트리거).
- **검증 명령**: `dotnet build -c Release`(error 0, warning 0). `grep -n "#[0-9A-Fa-f]\{6\}"` 를 각 View XAML에 실행해 남은 색 리터럴이 **의도된 예외(촬영 검정 배경·프레임/프리뷰 관련)만** 남았는지 정적 확인.
- **완료 기준**:
  - [관측] 빌드 error 0·warning 0. 각 View XAML의 색 리터럴이 브러시 키로 치환됨(grep: 잔존 `#RRGGBB`가 촬영 배경 `#000`/스크림 등 부록 A 예외 목록에 한정). 버튼이 스타일 키 참조(grep: `Style="{StaticResource Button.`). `AppShellViewModel`에 `Orientation` 존재.
  - [non-goal] 화면의 **기능·바인딩·커맨드는 변경하지 않는다**(순수 프레젠테이션 치환). 상태머신·VM 로직 불변. 촬영 프리뷰/카운트다운 렌더 표면에 무거운 Effect 추가 금지(성능).
  - [trigger] 레이아웃 재배치는 창 종횡비 변화(Orientation 바인딩) 시에만. 전이 페이드는 화면 전환 시에만.
  - [사용자 확인 필요] 전 화면 일관 디자인·올드함 해소, 가로/세로 레이아웃, 애니메이션 자연스러움(design §10-1·5·6).
- **롤백**: 이 Step 커밋 revert(View XAML·Orientation 원복). 기능 영향 없음(프레젠테이션만).
- [ ] 완료

---

## Step 9: 통합 정리 — 롱프레스 제거·Admin 잔재 정리·최종 빌드

- **Context Brief**: 요구 1의 "롱프레스 관리자 진입 통합/정리" 결정(설계 §3.4: 폐지)을 마무리한다. `MainWindow`의 롱프레스 히트영역·타이머 코드와 `AdminViewModel`/`AdminView` 잔재를 제거하고, 전체 솔루션 빌드·테스트로 회귀를 확인한다. 관리자 진입은 설정 페이지 [관리자] 섹션(Step 6)으로 완전 대체된 상태여야 한다.
- **대상 파일**: `src/MCPhoto.App/MainWindow.xaml`(AdminCorner 제거), `src/MCPhoto.App/MainWindow.xaml.cs`(롱프레스 핸들러·타이머 제거), `src/MCPhoto.App/ViewModels/AdminViewModel.cs`·`src/MCPhoto.App/Views/AdminView.xaml`(+`.cs`)(제거), `src/MCPhoto.App/ServiceRegistration.cs`(AdminViewModel 등록 제거), `src/MCPhoto.App/App.xaml`(AdminViewModel DataTemplate 제거).
- **선행 조건**: Step 3·6(상단 바 설정 버튼·설정 관리자 섹션이 동작해야 롱프레스 안전 제거). 전 Step.
- **구현 내용**:
  - `MainWindow.xaml`: `AdminCorner` Border 제거. `MainWindow.xaml.cs`: `OnAdminCornerDown/Up`·`_longPressTimer`·`AdminLongPressSeconds` 제거. `OnAnyUserActivity`(유휴 리셋)·`ApplyDisplaySettings`·`OnClosing`은 유지.
  - `AdminViewModel`·`AdminView.xaml`/`.xaml.cs` 삭제(로직·비번핸들러는 Step 5·6에서 SettingsViewModel/View로 이관 완료 전제). `ServiceRegistration`에서 `AddTransient<AdminViewModel>()` 제거, `App.xaml`에서 `AdminViewModel` DataTemplate 제거.
  - 잔재 참조 grep 정리: `AdminViewModel`·`AppState.Admin`·`AdminCorner` 참조 0.
- **검증 명령**: `dotnet build MCPhoto.sln -c Release`(error 0, warning 0). `dotnet test`(전체 통과). `grep`로 `AdminViewModel`·`AdminCorner`·`_longPressTimer`·`AppState.Admin` 참조 0 확인.
- **완료 기준**:
  - [관측] 전체 빌드 error 0·warning 0, `dotnet test` 전 케이스 통과. `AdminViewModel`/`AdminView`/`AdminCorner`/롱프레스 심볼이 코드베이스에 없음(grep=0). 관리자 기능은 SettingsViewModel [관리자] 섹션에만 존재.
  - [non-goal] 관리자 기능 자체가 사라지지 **않음**(설정 페이지로 이전됨, Step 6). 설정/촬영/계정 등 기존 기능 회귀 없음(전체 테스트 통과로 보증). 유휴 리셋·창 복원·전역 예외 핸들러는 유지.
  - [trigger] 관리자 진입은 이제 설정→[관리자] 로그인 게이트 통과 시에만(롱프레스 경로 제거됨).
  - [사용자 확인 필요] 롱프레스 폐지 후 설정→관리자 로그인으로 관리자 진입 가능(design §10-7).
- **롤백**: 이 Step 커밋 revert(롱프레스·Admin 잔재 복원). 단, 관리자 기능은 설정 페이지에도 있으므로 이중 진입 상태가 됨(무해).
- [ ] 완료

---

## 완결성 게이트 (자체 검사)

- [x] 검증된 사실(VF-1~15) / 미검증 가정(OA-1~6) 목록 분리됨
- [x] 모든 가정에 검증 Step 매핑됨 (OA-1→2, OA-2→3, OA-3→4, OA-4→6, OA-5→8, OA-6→5)
- [x] 모든 Step(1~9)에 7개 필수 필드 채워짐 (Context Brief / 대상 파일 / 선행 조건 / 구현 내용 / 검증 명령 / 완료 기준 / 롤백)
- [x] 모든 완료 기준이 관측 기반 3문 형식(관측·non-goal·trigger). UI Step(3/5/6/7/8/9)은 non-goal·trigger + 사용자 확인 필요 항목 포함
- [x] 검증 명령이 자동 실행 가능(`dotnet build -c Release`/`dotnet test --filter`/`grep`) — **앱 실행 없음**
- [x] UI 육안 항목은 각 Step "사용자 확인 필요"로 분리 + `wpf-it2-design.md` §10에 집약

## 진행 상태 어휘 (developer 보고 시)

`inspected` / `changed locally` / `verified locally`(build+test 통과) / `committed` / `pushed` / `blocked`(사유 명시 필수)
