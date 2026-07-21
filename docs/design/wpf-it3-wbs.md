# MC포토 — 이터레이션 3 구현 WBS

| 항목 | 값 |
|------|-----|
| 대상 | `MCPhoto.sln` (.NET 8 WPF) — 이터레이션 3(버그 2 + UX 5) |
| 설계 근거 | `docs/design/wpf-it3-design.md`, `docs/prd/iteration-3-fixes-and-ux.md`, `docs/design/wpf-it2-design.md` |
| 형식 | `docs/templates/WBS_BLUEPRINT.md` 준수 |
| 작성일 | 2026-07-21 |
| 빌드 검증 기준 | `dotnet build MCPhoto.sln -c Release`(error 0, 변경 프로젝트 warning 0) / `dotnet test` |

> 각 Step은 self-contained다. fresh 에이전트가 그 Step과 `wpf-it3-design.md`만 읽고 실행할 수 있게 작성했다.
> **모든 Step의 완료 기준은 headless(dotnet build/test·grep 정적확인)로만 판정한다.** UI 육안은 완료 기준에 넣지 않고 각 Step의 trigger/non-goal에 "사용자 확인 필요" 항목으로 분리했으며, 전체 육안 목록은 `wpf-it3-design.md` §11에 있다.
> ⚠️ **앱 실행 금지**(사용자 PC 사용 중 + UI 실행 차단 훅). 검증은 `dotnet build`/`dotnet test`/`grep`만.
> 색상·컴포넌트 토큰은 라이트 테마 A(`wpf-it2-design.md` §2.3·§2.3.1·Controls.xaml)를 따른다.

---

## 검증된 사실 (verified facts)

- **VF-1**: 계정 상태가 이중 소스(`SessionContext.CurrentUser` + `AppShellViewModel.CurrentUser` 미러 + `SyncAccountFromSession()` 수동 동기화). (근거: `AppShellViewModel.cs:41-57,145`, `SettingsViewModel.cs:46-48`)
- **VF-4**: `ReturnHome`·`HomeViewModel.Start`가 `Session.Reset()`으로 `CurrentUser=null` → 홈 경유 시 로그인 풀림. (근거: `AppShellViewModel.cs:166-174`, `HomeViewModel.cs:20`, `SessionContext.cs:43-56`)
- **VF-5/6**: `SaveSettings`는 싱글턴 `_settings.Current`를 직접 수정 후 `Save()`, 소비처(`CaptureViewModel`·`FrameSelectViewModel`)는 매 세션 `Settings.Current`를 읽음(즉시 반영 구조). (근거: `SettingsViewModel.cs:122-143`, `CaptureViewModel.cs:43-52,74-91`)
- **VF-7**: `IniSettingsService.Save()`가 쓰기 예외를 삼켜(로그만) 실패해도 VM은 성공 토스트 표시. (근거: `IniSettingsService.cs:50-68`, `SettingsViewModel.cs:141`)
- **VF-9**: 암묵 `ComboBox` 스타일에 `ControlTemplate` 없음 → OS 기본 룩. (근거: `Controls.xaml:349-356`)
- **VF-10**: 로그인 화면에 `IsDefault`/Enter 처리 없음. (근거: `LoginGuestView.xaml:15-25`)
- **VF-11**: 카메라 준비 상태를 VM/서비스가 노출 안 함(`CameraAvailable` bool만). (근거: `CaptureViewModel.cs:26,40-59`, `ICameraService.cs`)
- **VF-12**: [바로 촬영]은 `Button.Primary` 텍스트 버튼. (근거: `CaptureView.xaml:32-34`)
- **VF-13**: 설정 항목이 Grid/StackPanel 혼재로 정렬선 없음, 그룹핑은 카드 3개뿐. (근거: `SettingsView.xaml:18-94`)
- **VF-14**: 기존 테스트 자산 — `SettingsTests`·`AccountTests`·`AppStateTests` 존재, `dotnet test`로 실행. (근거: `tests/MCPhoto.Tests/`)
- **VF-15**: `Save()` 호출부는 `SettingsViewModel.SaveSettings`와 `MainWindow.OnClosing`(창 위치 저장) 2곳. (근거: `SettingsViewModel.cs:139`, `MainWindow.xaml.cs:72`)

## 미검증 가정 (open assumptions)

- **OA-1**: B1 로그아웃은 계정 미러 불일치 또는 세션 Reset 경유(단일 소스 통합으로 차단) → **검증: Step 1**(단위 + 사용자 육안).
- **OA-2**: B2 미반영은 INI 쓰기 실패의 성공 오인(소비처는 정상) → **검증: Step 2**(Save 반환 + 라운드트립).
- **OA-3**: ComboBox 커스텀 템플릿이 라이트 토큰으로 빌드·기존 바인딩 호환 → **검증: Step 4**(빌드) + 육안.
- **OA-4**: 카메라 준비 상태 노출이 캡처 파이프라인을 회귀시키지 않음 → **검증: Step 6**(빌드 + 기존 캡처 테스트).
- **OA-5**: Enter 처리가 PasswordBox code-behind와 충돌 없음 → **검증: Step 5**(빌드) + 육안.

> 모든 가정이 검증 Step에 매핑됨(완결성 게이트 통과).

---

## 단계 의존 그래프 (병렬 식별)

```
Step 1 (B1 계정 단일 소스)        ── 독립(핵심 버그)
Step 2 (B2 설정 저장 신뢰성)      ── 독립(핵심 버그)
Step 3 (U1 설정 레이아웃)         ← Step 2 선호(세그먼트 전환 시 함께)
Step 4 (U2 ComboBox 템플릿)       ── 독립(Controls.xaml). Step 3와 함께 보면 좋음
Step 5 (U3 로그인 Enter)          ── 독립
Step 6 (U4 카메라 로딩)           ── 독립(CaptureViewModel/View)
Step 7 (U5 셔터 버튼)             ← Step 6 선호(같은 CaptureView 편집 충돌 회피)
```

- Step 1·2는 P1 버그라 **최우선**. 나머지(3~7)는 서로 독립적이나 파일 충돌 회피 위해 Step 6→7 순서 권장(둘 다 `CaptureView.xaml`).

---

## Step 1: B1 — 계정 상태 단일 소스 승격 (로그인 세션 전역 유지)

- **Context Brief**: 로그인 후 설정 화면에 들어가면 로그아웃된 것처럼 보인다(B1). 원인은 계정 상태가 세 곳(`SessionContext.CurrentUser`, `AppShellViewModel.CurrentUser` 미러, `SettingsViewModel`의 세션 직접 참조)으로 분산되고 `SyncAccountFromSession()` 수동 동기화에 의존하며(VF-1), `Session.Reset()`이 `CurrentUser=null`로 로그인을 지우는 경로가 있기 때문(VF-4). 계정 상태를 싱글턴 단일 소스 + 변경 이벤트로 통합하고, 촬영 세션 Reset과 로그인 수명을 분리한다(설계 §2).
- **대상 파일**: `src/MCPhoto.App/SessionContext.cs`(계정 캡슐화·이벤트·`Reset(clearUser)`), `src/MCPhoto.App/AppShellViewModel.cs`(세션 이벤트 구독·미러/수동Sync 제거·Reset 정책), `src/MCPhoto.App/ViewModels/HomeViewModel.cs`(Start의 Reset을 로그인 보존으로), `src/MCPhoto.App/ViewModels/SettingsViewModel.cs`(세션 소스 구독 확인), `tests/MCPhoto.Tests/SessionServiceTests.cs`(신규).
- **선행 조건**: 없음.
- **구현 내용**:
  - `SessionContext`: `CurrentUser` set 캡슐화 + `event EventHandler? CurrentUserChanged`. `Login(User)`/`Logout()` 메서드(내부에서 이벤트 발행). `Reset(bool clearUser = false)`: 촬영 데이터(SelectedFrame·Capture·Filter·경로·Result)는 항상 폐기, `CurrentUser`는 `clearUser==true`일 때만 null(+이벤트).
  - `AppShellViewModel`: 생성자에서 `_session.CurrentUserChanged += ...` 구독 → 핸들러가 `OnPropertyChanged(IsLoggedIn/IsGuest/IsPower/AccountLabel)`. `CurrentUser` 미러 `[ObservableProperty]` 제거하고 계산 프로퍼티를 `_session.CurrentUser` 기반으로. `SyncAccountFromSession()` 제거(또는 no-op 후 호출부 정리). 로그인/로그아웃은 `_session.Login/Logout` 경유.
  - `ReturnHome` Reset 정책: 유휴 타임아웃(`OnIdleTimeout`)·세션 완료(Done 경유)는 `Reset(clearUser:true)`, 사용자 취소·오버레이 복귀는 로그인 보존(`clearUser:false`). `ReturnHome(string reason, bool clearUser)` 시그니처 또는 호출처별 분기.
  - `HomeViewModel.Start`: `Session.Reset(clearUser:false)`(촬영 데이터만 초기화, 로그인 보존).
  - `LoginGuestViewModel.Login`·`AppShellViewModel.Logout`: `_session.Login(user)`/`_session.Logout()` 사용, 수동 Sync 제거.
  - `Dispose`에서 이벤트 구독 해제.
  - 테스트(`SessionServiceTests`): `Login(u)` 후 `CurrentUser==u` + 이벤트 1회; `Reset(false)` 후 `CurrentUser==u`(보존) + 촬영 데이터 null; `Reset(true)`·`Logout()` 후 `CurrentUser==null` + 이벤트.
- **검증 명령**: `dotnet test --filter SessionServiceTests` + `dotnet build -c Release`(error 0, warning 0). `grep`로 `SyncAccountFromSession` 잔존 호출 0, `_session.CurrentUser =` 직접 대입이 `Login/Logout/Reset` 외에 없음.
- **완료 기준**:
  - [관측] `SessionServiceTests` 통과(로그인 보존/명시 로그아웃/이벤트 발행). 빌드 통과. `grep`: 계정 상태 진입점이 `Login/Logout/Reset(clearUser)`로 단일화(직접 `CurrentUser=` 대입 잔존 0), `SyncAccountFromSession` 제거됨.
  - [non-goal] 촬영 세션 데이터(프레임·컷·결과)는 화면 이동/설정 진입 시 **폐기되지 않아야** 하는 게 아니라 — 오버레이 복귀는 보존(it2 §5.3 유지), Home 복귀만 폐기. 로그인은 유휴·완료·명시 로그아웃 외에는 유지. 상태머신 전이표 불변.
  - [trigger] 로그아웃은 (a)명시적 Logout, (b)유휴 타임아웃, (c)세션 완료(Done→Home) 시에만. 설정 진입·취소·화면 이동으로는 로그아웃 안 됨.
  - [사용자 확인 필요] 로그인→설정 진입/복귀 로그인 유지, [촬영하기] 후 로그인 유지, 유휴/완료 후 로그아웃(design §11-1).
- **롤백**: 이 Step 커밋 revert(SessionContext·Shell·Home·Settings·테스트 원복).
- [ ] 완료

---

## Step 2: B2 — 설정 저장 신뢰성 (INI 영속 + 실패 노출 + 즉시 반영 보증)

- **Context Brief**: 설정을 바꾸고 저장해도 반영이 안 된다(B2). 저장 파이프라인은 싱글턴 인스턴스 공유라 런타임 반영은 구조상 정상(VF-5·6)이나, `IniSettingsService.Save()`가 쓰기 예외를 삼켜(VF-7) 실패해도 "저장되었습니다"가 떠 사용자가 성공으로 오인하고 재시작 후 복원이 깨진다. `Save()`가 성공 여부를 반환하고 실패를 사용자에게 알리며, 쓰기 경로 폴백을 강화한다(설계 §3).
- **대상 파일**: `src/MCPhoto.Core/Settings/ISettingsService.cs`(`bool Save()`), `src/MCPhoto.Core/Settings/IniSettingsService.cs`(반환·폴백 체인), `src/MCPhoto.App/ViewModels/SettingsViewModel.cs`(성공/실패 토스트 분기), `src/MCPhoto.App/MainWindow.xaml.cs`(OnClosing 호출부 반환 무시), `tests/MCPhoto.Tests/SettingsTests.cs`(확장).
- **선행 조건**: 없음.
- **구현 내용**:
  - `ISettingsService.Save()` → `bool Save()`(성공 true). `IniSettingsService.Save`: 기존 로직 + 쓰기 실패 시 폴백 경로(`%ProgramData%` → 실행경로 → `%LocalAppData%\MCPhoto\`) 순차 재시도, 모두 실패 시 false 반환(예외는 로깅 후 삼키되 false). 성공 경로를 로그.
  - `SettingsViewModel.SaveSettings`: `var ok = _settings.Save();` 후 `ok ? Success 토스트 : Danger 토스트("저장 위치에 쓸 수 없습니다")`. `ShowNotice`를 성공/실패 색 구분 가능하게(또는 별도 오류 필드). LoadSettings는 성공 시에만 or 항상(클램프 반영) — 성공 시 유지.
  - `MainWindow.OnClosing`: `_settings.Save()` 반환 무시(void 취급 가능, 경고 없게 `_ =` 또는 그대로).
  - 테스트(`SettingsTests` 확장): 값 변경 → `Save()==true` → 새 인스턴스 `Load()` 라운드트립 값 유지(기존 테스트에 반환 assert 추가). 쓰기 불가 경로(존재하지 않는 드라이브 등 주입 가능한 경로) → `Save()==false`(예외 아님). 클램프 유지 확인.
- **검증 명령**: `dotnet test --filter SettingsTests`(라운드트립 + 반환값 + 실패 폴백) + `dotnet build -c Release`(error 0, warning 0). `grep`로 `SettingsViewModel`이 `Save()` 반환값을 분기 사용.
- **완료 기준**:
  - [관측] `SettingsTests` 통과: `Save()` 성공 시 true + 새 인스턴스에서 값 복원, 쓰기 불가 경로 시 false(크래시 없음). 빌드 통과. `SaveSettings`가 반환값으로 성공/실패 토스트 분기(grep).
  - [non-goal] 저장은 [저장] 버튼에서만(입력 중 파일쓰기 없음). 소비처 코드(`CaptureViewModel`/`FrameSelectViewModel`)는 **변경하지 않음**(이미 `Settings.Current`를 매 세션 읽어 즉시 반영). 클램프 규칙 불변.
  - [trigger] INI 기록은 `SaveSettings` 실행 시. 실패 토스트는 `Save()==false`일 때만.
  - [사용자 확인 필요] 저장 후 재시작 복원, 실패 경로 오류 토스트, 변경값 다음 촬영 반영(design §11-2).
- **롤백**: 이 Step 커밋 revert(시그니처·폴백·토스트·테스트 원복). `Save()` void로 복귀.
- [ ] 완료

---

## Step 3: U1 — 설정 화면 레이아웃 정돈

- **Context Brief**: 설정 화면 항목이 Grid/StackPanel 혼재로 정렬선이 없고 그룹핑이 부족하다(VF-13, U1). 라이트 토큰으로 2열 정렬 + 그룹 소제목 + 일관 간격으로 정돈한다(설계 §4). 순수 XAML 레이아웃 변경(VM 무관).
- **대상 파일**: `src/MCPhoto.App/Views/SettingsView.xaml`. (선택: 컷수·카운트다운 세그먼트 전환 시 `SettingsView.xaml`만 — VM의 옵션 리스트는 이미 존재.)
- **선행 조건**: Step 2 선호(세그먼트로 바꾸면 B2 값 바인딩 혼동 제거). 필수 아님.
- **구현 내용**:
  - [앱 설정] 카드 내부를 그룹으로 분할: "촬영"(컷수·카운트다운·거울·플래시), "출력·전송"(포맷·QR·로컬저장·경로·보관), "장치·표시"(카메라·표시모드), "고급"(HostingURL·StorageBucket). 각 그룹 `Text.Title` 소제목 + `Brush.Divider` 구분선.
  - 인라인 행(짧은 값): 일관 2열(라벨 좌 `VerticalAlignment=Center` + 컨트롤 우측 정렬 고정폭). 컨트롤 폭 표준화(콤보 140, 토글 56). 행 간 `Space.M`.
  - 스택 행(텍스트 입력): 라벨 위 + 전폭 TextBox 아래.
  - (선택) 컷수·카운트다운을 `ItemsControl` + `Segment` 스타일 세그먼트로. VM `CutCountOptions`/`CountdownOptions` 바인딩, 선택은 `CutCount`/`CountdownSec`와 동기(RadioButton 그룹 or 커맨드).
  - 토큰만 사용(하드코딩 색·마진 금지): `Brush.*`/`Space.*`/`Pad.*`/`Radius.*`/`Text.*` 스타일.
- **검증 명령**: `dotnet build -c Release`(error 0, warning 0). `grep`로 `SettingsView.xaml`에 하드코딩 색 리터럴(`#`) 없음(토큰만), 그룹 소제목(`Text.Title`) ≥ 3, `Brush.Divider` 사용.
- **완료 기준**:
  - [관측] 빌드 통과. `SettingsView.xaml`에 그룹 소제목 3개 이상 + 구분선, 색·간격이 토큰 참조(grep: `#RRGGBB` 리터럴 0). 모든 [앱 설정] 항목 바인딩이 유지됨(grep: CutCount·MirrorMode·OutputFormat·StorageBucket 등 §it2 4.2 전 항목).
  - [non-goal] 설정 **바인딩·커맨드·VM은 변경하지 않는다**(레이아웃만). 항목 누락 없음(전 항목 유지). 계정/관리자 섹션 조건부 표시 로직 불변.
  - [trigger] 저장은 여전히 [저장] 버튼만. 세그먼트 선택은 VM 프로퍼티 갱신(저장 시 반영).
  - [사용자 확인 필요] 정렬·그룹핑 가독성(design §11-3).
- **롤백**: 이 Step 커밋 revert(`SettingsView.xaml` 원복).
- [ ] 완료

---

## Step 4: U2 — ComboBox 세련된 스타일/템플릿

- **Context Brief**: 설정 ComboBox가 `ControlTemplate` 없이 OS 기본 룩이라 라이트 테마와 이질적이다(VF-9, U2). 라이트 `ComboBox`/`ComboBoxItem` `ControlTemplate`을 `Controls.xaml`에 추가한다(설계 §5).
- **대상 파일**: `src/MCPhoto.App/Themes/Controls.xaml`(암묵 `ComboBox`·`ComboBoxItem` 스타일에 Template 추가).
- **선행 조건**: 없음(Controls.xaml 독립).
- **구현 내용**:
  - 암묵 `ComboBox` 스타일에 `ControlTemplate`: 닫힌 박스=`ToggleButton`(`Brush.Bg` 배경 + `Brush.Border` 1px + `Radius.S`, 높이 `Touch.Min`, 우측 chevron Path `Brush.Text.Tertiary`), hover=`Brush.Surface.Hover`, focus/open=`Brush.Accent` 2px 보더. `ContentPresenter`(선택값, `Brush.Text.Primary`). `ToggleButton.IsChecked`↔`ComboBox.IsDropDownOpen` 바인딩. `Popup`(`PART_Popup`, `IsOpen` TemplateBinding) + 드롭다운 컨테이너(`Brush.Bg` + `Brush.Border` + `Radius.S` + `Shadow.Pop`, MaxHeight 240 + ScrollViewer + ItemsPresenter).
  - 암묵 `ComboBoxItem` 스타일 + Template: 높이 터치, 패딩 `12,10`, `Brush.Text.Primary`, hover(`IsHighlighted`)=`Brush.Surface.Hover`, 선택(`IsSelected`)=`Brush.Accent.Soft` + `Brush.Accent.Text`.
  - 표준 파트명 사용(`PART_Popup`, `ContentSite`), non-editable 전제(설정 콤보는 편집 불가). `Grid.IsSharedSizeScope` 불요.
  - 접근성·토큰: §it2 2.3.1(Muted 텍스트 금지, 항목 Ink).
- **검증 명령**: `dotnet build -c Release`(error 0, warning 0). `grep`로 `Controls.xaml`에 `ComboBox` `ControlTemplate` + `PART_Popup` + `ComboBoxItem` 스타일 존재.
- **완료 기준**:
  - [관측] 빌드 통과. `Controls.xaml`에 `ComboBox` `ControlTemplate`(Popup·chevron·hover/focus 트리거)과 `ComboBoxItem` 스타일 정의(grep: `PART_Popup`, `TargetType="ComboBoxItem"`). 기존 `SettingsView`의 ComboBox 바인딩(`ItemsSource`/`SelectedItem`) 변경 없이 자동 적용(암묵 스타일).
  - [non-goal] ComboBox의 **기능·바인딩은 변경하지 않는다**(스타일만). editable ComboBox 지원 불필요(설정은 non-editable). 다른 입력류(TextBox/PasswordBox) 스타일 영향 없음.
  - [trigger] 드롭다운은 클릭/포커스 시 열림(표준 동작). 스타일 적용은 암묵(전역).
  - [사용자 확인 필요] 콤보 룩·드롭다운 항목·hover/focus(design §11-4).
- **롤백**: 이 Step 커밋 revert(`Controls.xaml` ComboBox 부분 원복 → 최소 스타일).
- [ ] 완료

---

## Step 5: U3 — 로그인 화면 Enter 키

- **Context Brief**: 로그인 화면에서 Enter로 로그인이 안 된다(VF-10, U3). 로그인 버튼 `IsDefault` + PasswordBox Enter 처리로 Enter=로그인을 보장한다(설계 §6). PasswordBox는 바인딩 불가라 이미 code-behind `OnPasswordChanged`로 VM에 전달 중.
- **대상 파일**: `src/MCPhoto.App/Views/LoginGuestView.xaml`(로그인 버튼 `IsDefault`), `src/MCPhoto.App/Views/LoginGuestView.xaml.cs`(PasswordBox KeyDown→커맨드).
- **선행 조건**: 없음.
- **구현 내용**:
  - `LoginGuestView.xaml`: 로그인 `Button`에 `IsDefault="True"`.
  - `LoginGuestView.xaml.cs`: `PasswordInput.KeyDown`(및 아이디 TextBox) 핸들러 — `e.Key == Key.Enter`이면 VM의 `LoginCommand.Execute(null)`(CanExecute 확인). 기존 `OnPasswordChanged`가 VM `Password`를 이미 갱신하므로 Enter 시점에 값 준비됨. IsBusy 가드는 커맨드 내부(기존).
  - MVVM: 커맨드 실행만(로직은 VM), 로직 code-behind 금지.
- **검증 명령**: `dotnet build -c Release`(error 0, warning 0). `grep`로 `LoginGuestView.xaml`에 `IsDefault="True"`, `.xaml.cs`에 `Key.Enter` + `LoginCommand`.
- **완료 기준**:
  - [관측] 빌드 통과. 로그인 버튼 `IsDefault="True"`(grep), code-behind에 Enter→`LoginCommand` 실행(grep). VM 로직 변경 없음.
  - [non-goal] 로그인 **로직은 VM에 유지**(code-behind는 커맨드 실행만). Enter가 다른 커맨드/네비를 트리거하지 않음. IsBusy 중 Enter 연타로 중복 로그인 안 됨(기존 가드).
  - [trigger] 로그인은 버튼 클릭 또는 Enter 시. Enter는 로그인 화면에서만(다른 화면 무관).
  - [사용자 확인 필요] 비번 입력 후 Enter로 로그인(design §11-5).
- **롤백**: 이 Step 커밋 revert(XAML·code-behind 원복).
- [ ] 완료

---

## Step 6: U4 — 카메라 로딩 대기 표시

- **Context Brief**: 촬영 진입 시 카메라 초기화까지 로딩 표시가 없다(VF-11, U4). `CaptureViewModel`에 카메라 준비 상태(`Initializing/Ready/Failed`)를 노출하고, 프리뷰 준비 전 로딩 오버레이(스피너+안내)를 표시한다. 준비 전 카운트다운 시작을 지연한다(설계 §7).
- **대상 파일**: `src/MCPhoto.App/ViewModels/CaptureViewModel.cs`(`CameraLoadState` 노출·첫 프레임 Ready 판정·시퀀스 게이트), `src/MCPhoto.App/Views/CaptureView.xaml`(로딩 오버레이+스피너), `src/MCPhoto.App/Converters/CommonConverters.cs`(`CameraState`→Visibility 컨버터), `src/MCPhoto.App/App.xaml`(컨버터 등록).
- **선행 조건**: 없음. (Step 7과 같은 `CaptureView.xaml` 편집 → Step 6 먼저 권장.)
- **구현 내용**:
  - `CaptureViewModel`: enum `CameraLoadState { Initializing, Ready, Failed }` + `[ObservableProperty] CameraLoadState _cameraState`. `OnEnterAsync`: `CameraState=Initializing` → `StartAsync` false면 `Failed`+StatusMessage, true면 첫 `FrameReady` 수신 시 `Ready`(1회 구독 후 해제). **캡처 시퀀스(`RunCaptureSequenceAsync`) 시작을 `Ready` 이후로 게이트**(첫 프레임 대기). 타임아웃(예 8초) 내 첫 프레임 없으면 `Failed`(무한 로딩 방지, R5).
  - `CaptureView.xaml`: `CameraState==Initializing`일 때 로딩 오버레이(스크림 `Brush.Scrim` + 중앙 스피너 `Storyboard` 회전 + "카메라 준비 중…" `Brush.OnAccent`). `Ready`면 Collapsed. `Failed`이면 기존 `StatusMessage` 표시. 컨버터로 `CameraState`→Visibility.
  - `CommonConverters.cs`: `CameraStateToVisibilityConverter`(파라미터로 대상 상태 지정 or Initializing 전용). `App.xaml` 리소스 등록.
  - 스피너는 XAML `Storyboard`(코드비하인드 없이). 촬영 렌더 성능 영향 없게 단순 회전.
- **검증 명령**: `dotnet build -c Release`(error 0, warning 0) + `dotnet test`(기존 캡처 관련 테스트 `CaptureSessionTests` 등 회귀 없음). `grep`로 `CaptureViewModel`에 `CameraLoadState`·`Ready` 전환, `CaptureView.xaml`에 로딩 오버레이.
- **완료 기준**:
  - [관측] 빌드·기존 테스트 통과. `CaptureViewModel`에 `CameraState`(Initializing/Ready/Failed) 노출 + 첫 프레임 Ready 전환 + 시퀀스 Ready 게이트(grep). `CaptureView.xaml`에 로딩 오버레이(스피너+안내) + `CameraState` 바인딩.
  - [non-goal] 캡처 파이프라인 로직(크롭·녹화·스틸)은 **변경하지 않는다**. `ICameraService` 인터페이스 변경 없음(기존 `FrameReady`/`IsRunning` 사용). 로딩 오버레이가 프리뷰 프레임레이트에 영향 주는 무거운 Effect 금지.
  - [trigger] 로딩 표시는 `Initializing` 동안만, 첫 프레임(`Ready`) 시 사라짐, 실패 시 `Failed` 메시지. 카운트다운 시퀀스는 `Ready` 이후 시작.
  - [사용자 확인 필요] 로딩 표시→프리뷰 준비 시 사라짐, 로딩 중 카운트다운 미시작(design §11-6).
- **롤백**: 이 Step 커밋 revert(VM·View·컨버터 원복). `CameraAvailable` 기존 동작 복귀.
- [ ] 완료

---

## Step 7: U5 — [바로 촬영] 셔터 버튼

- **Context Brief**: [바로 촬영]이 텍스트 버튼이다(VF-12, U5). 원형 셔터 버튼(이중 원) + 아래 "바로 촬영" 라벨로 교체한다(설계 §8). 커맨드(`ShootNowCommand`)는 그대로.
- **대상 파일**: `src/MCPhoto.App/Themes/Controls.xaml`(`Button.Shutter` 스타일 신규), `src/MCPhoto.App/Views/CaptureView.xaml`(버튼 교체 + 라벨).
- **선행 조건**: Step 6 선호(같은 `CaptureView.xaml`).
- **구현 내용**:
  - `Controls.xaml`: `Button.Shutter` 스타일 — `ControlTemplate`에 `Grid` + 바깥 링(`Ellipse` Stroke `Brush.OnAccent` 3px, 투명 채움) + 안쪽 원(`Ellipse` Fill `Brush.Accent`). 지름 ~88. hover=안쪽 `Accent.Hover`, press=안쪽 `ScaleTransform` 0.9(눌림감). `Cursor=Hand`, `FocusVisualStyle={x:Null}`.
  - `CaptureView.xaml`: 기존 "바로 촬영" 버튼을 `StackPanel`(세로, 하단 중앙)로 교체 — `Button Style=Button.Shutter Command=ShootNowCommand AutomationProperties.Name="바로 촬영"` + 아래 `TextBlock "바로 촬영"`(`Text.Caption`, `Brush.OnAccent` 반투명). 하단 여백 `Margin 0,0,0,40`.
  - 카운트다운·취소와 겹치지 않게 배치(셔터 하단 중앙, 취소 우상단 유지).
- **검증 명령**: `dotnet build -c Release`(error 0, warning 0). `grep`로 `Controls.xaml`에 `Button.Shutter`, `CaptureView.xaml`에 `Button.Shutter` 사용 + `ShootNowCommand` 유지 + "바로 촬영" 라벨.
- **완료 기준**:
  - [관측] 빌드 통과. `Button.Shutter` 스타일 정의(이중 Ellipse·press scale, grep). `CaptureView.xaml`이 셔터 버튼(`ShootNowCommand` 바인딩 유지) + "바로 촬영" 라벨 사용. 기존 텍스트 버튼 제거.
  - [non-goal] `ShootNowCommand` **로직 변경 없음**(버튼 표현만). 취소·카운트다운·플래시 요소 불변. 셔터가 다른 요소를 가리지 않음.
  - [trigger] 셔터 클릭 = `ShootNowCommand`(기존과 동일 동작). press 애니메이션은 누를 때만.
  - [사용자 확인 필요] 셔터 룩·라벨·눌림감(design §11-7).
- **롤백**: 이 Step 커밋 revert(`Controls.xaml`·`CaptureView.xaml` 원복 → 텍스트 버튼).
- [ ] 완료

---

## 완결성 게이트 (자체 검사)

- [x] 검증된 사실(VF-1~15) / 미검증 가정(OA-1~5) 분리됨
- [x] 모든 가정에 검증 Step 매핑됨 (OA-1→1, OA-2→2, OA-3→4, OA-4→6, OA-5→5)
- [x] 모든 Step(1~7)에 7개 필수 필드(Context Brief/대상 파일/선행 조건/구현 내용/검증 명령/완료 기준/롤백)
- [x] 모든 완료 기준이 관측 기반 3문 형식(관측·non-goal·trigger). UI Step은 "사용자 확인 필요" 포함
- [x] 검증 명령이 자동 실행 가능(`dotnet build -c Release`/`dotnet test --filter`/`grep`) — **앱 실행 없음**
- [x] UI 육안은 각 Step "사용자 확인 필요" + `wpf-it3-design.md` §11에 집약

## 진행 상태 어휘 (developer 보고 시)

`inspected` / `changed locally` / `verified locally`(build+test 통과) / `committed` / `pushed` / `blocked`(사유 명시 필수)
