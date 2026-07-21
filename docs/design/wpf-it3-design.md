# MC포토 — 이터레이션 3 설계 (버그 수정 + UX 개선)

| 항목 | 값 |
|------|-----|
| 문서 | WPF 이터레이션 3 설계 본문 |
| 작성일 | 2026-07-21 |
| 상태 | 초안 v1 (구현 착수 전) |
| 1차 준거 | `docs/prd/iteration-3-fixes-and-ux.md` |
| 상위 준거 | `docs/design/wpf-it2-design.md`(라이트 테마 A·토큰·상태머신), PRD v2.7 §9 |
| 구현 WBS | `docs/design/wpf-it3-wbs.md` |
| 코드 베이스 | `E:\Study\photobooth\src\` (it2 구현 반영 완료 상태) |

> 이터레이션 3은 사용자가 it2 빌드를 직접 테스트하고 발견한 **버그 2건(P1)**과 **UX 개선 5건(P2)**을 다룬다. 신규 기능·범위 확장 없음. it2의 라이트 디자인 시스템·상태머신·오버레이 네비게이션 위에서 진행한다.

---

## 0. 검증된 사실 / 미검증 가정

### 검증된 사실 (코드 인스펙션으로 직접 확인)

- **VF-1. 계정 상태가 이중 소스로 분산돼 있다**: 로그인 사용자는 `SessionContext.CurrentUser`(싱글턴)에 저장되지만, 상단 바는 `AppShellViewModel.CurrentUser`(별도 `[ObservableProperty]`)를 읽고, `SettingsViewModel`은 다시 `_shell.Session.CurrentUser`를 직접 읽는다. 셸의 `CurrentUser`는 `SyncAccountFromSession()` **수동 호출**로만 세션과 동기화된다(`AppShellViewModel.cs:145`). 즉 **단일 소스가 아니라 "세션 + 셸 미러 + 수동 동기화"** 구조다. (근거: `AppShellViewModel.cs:41-57,145`, `SettingsViewModel.cs:46-48`, `SessionContext.cs:13`)
- **VF-2. 모든 화면 VM은 Transient**: `ServiceRegistration.RegisterScreens`가 `SettingsViewModel`·`LoginGuestViewModel` 등 전 화면 VM을 `AddTransient`로 등록(`ServiceRegistration.cs:73-87`). 화면 진입마다 `CreateViewModel`이 새 인스턴스를 생성(`AppShellViewModel.cs:148-163`). → **화면 VM에 상태를 담으면 재진입 시 소멸**한다. 단 로그인 상태는 VM이 아니라 싱글턴 `SessionContext`에 있어 이 자체로는 유실되지 않아야 한다(VF-1의 미러 동기화가 관건).
- **VF-3. `SettingsViewModel.OnEnterAsync`는 세션에서 로그인 상태를 다시 읽는다**: `IsGuest/IsLoggedIn/IsPower`는 `_shell.Session.CurrentUser` 기반 계산 프로퍼티이고, `OnEnterAsync`가 이들을 `OnPropertyChanged`로 통지한다(`SettingsViewModel.cs:46-48,84-100`). → 세션에 사용자가 살아있으면 설정 화면도 로그인으로 보여야 정상. **로그아웃처럼 보이면 세션 자체가 비었거나(어딘가 Reset), 통지 타이밍/소스 불일치**다.
- **VF-4. Home 진입·로그아웃·유휴·예외가 `Session.Reset()`을 호출**: `ReturnHome`(`AppShellViewModel.cs:166-174`)이 `_session.Reset()`으로 `CurrentUser=null`. `HomeViewModel.Start`도 `Session.Reset()` 호출(`HomeViewModel.cs:20`). 유휴 타임아웃(`OnIdleTimeout`)·전역 예외(`App.xaml.cs`)도 `ReturnHome` 경유. → **홈을 거치면 로그인이 반드시 풀린다.** 설정 진입 자체(`OpenSettings`→`NavigateToOverlayAsync`)는 Reset을 호출하지 않는다.
- **VF-5. `SaveSettings`는 싱글턴 `_settings.Current`를 직접 수정 후 `Save()` 호출**: `SettingsViewModel.SaveSettings`(`SettingsViewModel.cs:122-143`)가 `_settings.Current`의 필드를 세팅하고 `_settings.Save()`. `IniSettingsService.Save()`는 `_current`를 INI로 직렬화(`IniSettingsService.cs:50-68`). `Current`는 `_current ??= Load()`로 캐시(`IniSettingsService.cs:26`). → **설정값 인스턴스는 소비처와 동일**(싱글턴). 즉 값 반영은 구조상 되어야 한다.
- **VF-6. 런타임 소비처는 매번 `Settings.Current`를 읽는다**: `CaptureViewModel.OnEnterAsync`가 `_shell.Settings.Current`에서 `CameraDevice`·`MirrorMode`·`CountdownSec`·`FlashMode`를 읽어 사용(`CaptureViewModel.cs:43,52,74,87,91`). `FrameSelectViewModel.Next`도 `Settings.Current.CutCount`를 읽어 `Capture.Begin`. → **다음 촬영 시점에 갱신값을 읽는다**(시작 시 1회 캐시가 아님). 값이 안 먹힌다면 원인은 소비처가 아니라 (a) INI 미기록 또는 (b) **저장 시점에 VM 프로퍼티가 UI 값을 못 받았다**.
- **VF-7. `SaveSettings`가 `Save()` 예외를 삼킨다**: `IniSettingsService.Save`가 `catch (Exception)`으로 로그만 남기고 무시(`IniSettingsService.cs:64-67`). `%ProgramData%\MCPhoto\` 쓰기 실패 시 **사용자에게 성공 토스트가 뜨는데 실제로는 미기록**될 수 있다("저장되었습니다" 표시는 `SaveSettings`가 항상 실행). (근거: `SettingsViewModel.cs:141`, `IniSettingsService.cs:64-67`)
- **VF-8. 설정 ComboBox 3종이 값 타입 `SelectedItem` 바인딩**: 컷수/카운트다운은 `ItemsSource=int 리스트`, `SelectedItem="{Binding CutCount}"`(int). 출력포맷/표시모드는 enum. `SelectedItem` 매칭은 항목 컬렉션의 요소와 바인딩 값의 **`Equals` 동등성**으로 이뤄진다. int/enum은 값 동등성이 성립하므로 초기 선택은 표시되나, **양방향 기본이 `SelectedItem`이라 사용자가 안 건드리면 값 그대로**다(이건 정상). 실제 문제는 U2에서 ComboBox에 `ControlTemplate`이 없어 기본 클래식 룩 + 일부 환경에서 드롭다운 팝업 렌더 이슈 가능성. (근거: `SettingsView.xaml:21-23,28-30,47-49,81-83`, `Controls.xaml:349-356`)
- **VF-9. ComboBox 스타일은 최소치(템플릿 없음)**: `Controls.xaml:349-356`의 암묵 `ComboBox` 스타일은 FontFamily·Foreground·MinHeight·Padding만 지정하고 **`ControlTemplate`이 없다**. → WPF 기본(Aero/classic) 템플릿이 그대로 노출돼 라이트 테마와 이질적("투박함"). U2의 직접 원인. (근거: `Controls.xaml:349-356`)
- **VF-10. 로그인 화면에 Enter 처리 없음**: `LoginGuestView.xaml`의 로그인 버튼에 `IsDefault` 없음, `PasswordBox`/`TextBox`에 `KeyBinding` 없음(`LoginGuestView.xaml:15-25`). → Enter로 로그인 불가(U3). PasswordBox는 바인딩 불가라 code-behind `OnPasswordChanged`로 VM에 전달 중(`LoginGuestView.xaml:18`). (근거: `LoginGuestView.xaml`)
- **VF-11. 카메라 준비 상태를 VM이 노출하지 않는다**: `CaptureViewModel.OnEnterAsync`가 `await _camera.StartAsync(...)`를 호출하는데(`CaptureViewModel.cs:52`), 이 await가 끝날 때까지(카메라 초기화) UI에 **로딩 표시가 없다**. `CameraAvailable`(bool)만 있고 "준비 중" 상태가 없다. `ICameraService`에도 준비 완료 신호는 `StartAsync` 반환 + `FrameReady` 첫 이벤트뿐(`ICameraService.cs:11,23`). (근거: `CaptureViewModel.cs:40-59`, `CaptureView.xaml`)
- **VF-12. [바로 촬영]은 텍스트 버튼**: `CaptureView.xaml:32-34`가 `Button.Primary` 스타일 + "바로 촬영" 텍스트. 셔터 아이콘 없음(U5). (근거: `CaptureView.xaml`)
- **VF-13. 설정 레이아웃은 `Grid`에 라벨+오른쪽 컨트롤을 겹쳐 배치**: 각 항목이 `<Grid>` 안에 `TextBlock`(좌) + `ComboBox/Toggle`(우, `HorizontalAlignment=Right`)로 구성돼 정렬 기준이 항목마다 제각각(일부는 `StackPanel` 세로 배치). 그룹 구분은 카드 3개뿐, 카드 내부 항목 간 시각적 그룹핑·정렬 그리드가 없다(`SettingsView.xaml:18-94`). → U1의 "배열이 이상함". (근거: `SettingsView.xaml`)

### 미검증 가정 (구현 시 검증 — WBS Step 매핑)

- **OA-1. B1의 로그아웃 트리거는 "설정 진입 자체"가 아니라 계정 미러 동기화 누락 또는 세션 Reset 경유다** → 단일 소스 통합으로 근본 차단. 검증: **WBS Step 1**(단위 테스트 + 사용자 재현 확인).
- **OA-2. B2의 미반영은 INI 쓰기 실패(무시된 예외) 또는 저장 성공 오인이다**(VF-7). 소비처는 정상(VF-6) → 저장 결과를 검증·노출하면 해소. 검증: **WBS Step 2**(Save 성공/실패 반환 + 라운드트립 테스트).
- **OA-3. ComboBox 커스텀 `ControlTemplate`(Popup 포함)이 라이트 토큰으로 빌드되고 기존 바인딩과 호환된다** → 검증: **WBS Step 4**(빌드 + 사용자 육안).
- **OA-4. 카메라 준비 상태를 `ICameraService`/VM에 노출해도 캡처 파이프라인 동작이 바뀌지 않는다** → 검증: **WBS Step 6**(빌드 + 기존 캡처 테스트 회귀 없음).
- **OA-5. `IsDefault`/`KeyBinding` Enter 처리가 PasswordBox code-behind 흐름과 충돌하지 않는다** → 검증: **WBS Step 5**(빌드 + 사용자 육안).

---

## 1. 요구 → 설계 매핑 (한눈에)

| 요구 | 근본 원인(VF) | 설계 조치 | WBS Step |
|---|---|---|---|
| **B1** 로그인 세션 미유지 | 계정 이중 소스 + 수동 동기화(VF-1), 홈/Reset이 세션 비움(VF-4) | 계정 상태를 **단일 소스(ISessionService 싱글턴)로 승격**, 셸·설정·상단바가 옵저버블로 동일 소스 구독 | §2, Step 1 |
| **B2** 설정 저장·미적용 | Save 예외 무시로 성공 오인(VF-7), 소비처는 정상(VF-6) | `Save()`가 **성공 여부 반환**, 실패 시 오류 토스트. 라운드트립·소비 검증 | §3, Step 2 |
| **U1** 설정 레이아웃 정돈 | 항목별 제각각 배치(VF-13) | 2열 정렬 그리드 + 그룹 소제목 + 토큰 간격 규약 | §4, Step 3 |
| **U2** ComboBox 세련화 | 템플릿 없음, 클래식 룩(VF-9) | 라이트 `ComboBox` `ControlTemplate`(라운드·보더·화살표·hover/focus·드롭다운 항목) | §5, Step 4 |
| **U3** 로그인 Enter | Enter 처리 없음(VF-10) | 로그인 버튼 `IsDefault` + PasswordBox `KeyDown`→커맨드 | §6, Step 5 |
| **U4** 카메라 로딩 표시 | 준비 상태 미노출(VF-11) | `CameraState`(Initializing/Ready/Failed) VM 노출 + 로딩 오버레이 | §7, Step 6 |
| **U5** 셔터 버튼 | 텍스트 버튼(VF-12) | 원형 셔터 버튼(Path/Ellipse) + "바로 촬영" 라벨 | §8, Step 7 |

---

## 2. B1 — 로그인 세션 전역 유지 (근본 원인 + 수정 설계)

### 2.1 근본 원인

계정 상태의 **진실 소스가 하나가 아니다**(VF-1). 현재:
- `SessionContext.CurrentUser` — 실제 저장소(싱글턴).
- `AppShellViewModel.CurrentUser` — 상단 바가 보는 미러. `SyncAccountFromSession()` 수동 호출로만 갱신.
- `SettingsViewModel`은 세 번째로 `_shell.Session.CurrentUser`를 직접 참조.

이 구조는 "동기화 호출을 한 곳이라도 빠뜨리면 화면 간 상태가 어긋난다". 실제 로그아웃 트리거는 두 갈래가 가능하다(OA-1):
1. **미러 불일치**: 로그인은 세션에 반영됐으나 특정 진입 경로에서 셸 미러가 갱신 안 돼 상단 바·팝오버가 로그아웃으로 보임.
2. **세션 Reset 경유**(VF-4): 로그인 후 홈을 거치는 흐름(예: 로그인 → 어떤 이유로 `ReturnHome`)에서 `Session.Reset()`이 `CurrentUser=null`. 설정 화면은 빈 세션을 읽어 로그아웃 표시.

어느 쪽이든 **단일 소스 + 자동 통지**로 통합하면 구조적으로 차단된다.

### 2.2 수정 설계 — 계정 상태 단일 소스 승격

**`ISessionService`(신규, 싱글턴)를 계정 상태의 유일한 진실 소스로 둔다.** 최소 침습을 위해 기존 `SessionContext`를 확장하는 방식과 별도 서비스로 빼는 방식 두 안이 있는데, **`SessionContext`에 계정 로그인/로그아웃을 캡슐화하고 변경 이벤트를 노출**하는 안을 채택한다(기존 싱글턴·참조를 재활용, DI 변경 최소).

- `SessionContext`(또는 이를 감싸는 `ISessionService`)에 추가:
  - `User? CurrentUser`는 유지하되 **`set`을 캡슐화**하고 `event EventHandler? CurrentUserChanged` 발행.
  - `Login(User)` / `Logout()` 메서드로 진입점 단일화(직접 `CurrentUser=` 대입 금지 규약).
  - **핵심**: `Reset()`이 촬영 세션 데이터(프레임·컷·결과)만 폐기하고 **`CurrentUser`는 보존**하도록 분리한다. 로그인은 "키오스크 촬영 세션"보다 상위 수명(앱 사용 동안 유지). 현재 `Reset()`이 `CurrentUser=null`을 하는 게 B1의 핵심 유발점(VF-4)이므로, **`Reset()`에서 `CurrentUser=null` 제거**하고 로그아웃은 명시적 `Logout()`으로만.
    - 단, 홈 대기 복귀 시 "다음 손님"을 위해 로그아웃이 필요한 키오스크 시나리오가 있다 → **`Reset(clearUser: bool)` 파라미터**로 구분: 유휴 타임아웃·세션 완료(다음 손님)는 `clearUser:true`, 화면 이동·설정 진입·취소는 `clearUser:false`. 기본은 보존(false), 명시적 로그아웃·키오스크 리셋만 true.
- `AppShellViewModel.CurrentUser` 미러 제거 또는 **`ISessionService.CurrentUserChanged` 구독으로 자동 갱신**. 상단 바 바인딩(`AccountLabel`/`IsLoggedIn`/`IsPower`)은 셸이 세션 이벤트를 받아 `OnPropertyChanged`. `SyncAccountFromSession()` 수동 호출 산재 제거.
- `SettingsViewModel`도 동일 소스(`ISessionService`) 구독 — `OnEnterAsync`의 수동 통지는 유지하되 소스가 단일화됐으므로 항상 일관.

### 2.3 상태머신·네비게이션 영향

- **`ReturnHome`의 세션 폐기 정책 변경**: 유휴 타임아웃·세션 완료(Done→Home)는 `Reset(clearUser:true)`(다음 손님), 사용자 취소·오버레이 복귀는 로그인 보존. `ReturnFromOverlay`는 이미 Reset을 안 하므로 무관(설정→복귀 시 로그인 유지, it2 §5.3 그대로).
- `HomeViewModel.Start`의 `Session.Reset()`은 **촬영 데이터만 초기화**(로그인 보존) — 로그인 사용자가 [촬영하기] 시 커스텀 프레임을 쓰려면 로그인이 유지돼야 한다. 현재 `Reset()`이 로그인을 지워 [촬영하기] 후 게스트가 되는 것도 부수적 버그였다.
- 전이표(`SessionStateMachine`)는 변경 없음.

### 2.4 검증 포인트(headless)

- 단위: `ISessionService.Login(u)` 후 `CurrentUser==u`, `Reset(clearUser:false)` 후에도 `CurrentUser==u`, `Reset(clearUser:true)` 또는 `Logout()` 후 `null`, `CurrentUserChanged` 발행 횟수.
- 사용자 확인(육안): 로그인 → 설정 진입 → 로그인 유지, 상단 바 계정 라벨 유지.

---

## 3. B2 — 설정 저장·적용 (근본 원인 + 수정 설계)

### 3.1 근본 원인

저장 파이프라인은 인스턴스 공유 구조상 정상(VF-5·6)인데, **`IniSettingsService.Save()`가 쓰기 예외를 삼켜(VF-7)** 실패해도 `SettingsViewModel`이 무조건 "저장되었습니다" 토스트를 띄운다(`SettingsViewModel.cs:141`). 즉 사용자는 "저장했다"고 믿지만 재시작 후 복원 안 됨 → "적용 안 됨"으로 체감. `%ProgramData%\MCPhoto\` 권한/경로 문제나 파일 잠금이 원인일 수 있다. (런타임 세션 반영은 VF-6대로 이미 정상이라 "다음 촬영에 즉시 반영"은 대체로 동작하나, 재시작 복원이 깨지면 전체가 "미적용"으로 보인다.)

### 3.2 수정 설계

- **`ISettingsService.Save()`가 성공 여부를 반환**(`bool Save()`) 또는 실패 시 예외 전파(호출자 처리). 채택: `bool Save()`(예외는 내부 로깅 유지하되 false 반환).
- `SettingsViewModel.SaveSettings`: `Save()` 반환값으로 분기 — 성공 시 `Success` 토스트, **실패 시 `Danger` 토스트**("저장 위치에 쓸 수 없습니다: {경로}"). 사용자가 실패를 인지.
- **경로 폴백 강화**: `ResolveDefaultPath`가 `%ProgramData%` 실패 시 실행 경로로 폴백하는데(`IniSettingsService.cs:117-131`), **실제 쓰기 시점에도** `%ProgramData%` 쓰기가 막히면 실행 경로/`%LocalAppData%`로 재시도하는 폴백 체인을 `Save()`에 추가. 최종 실패만 false.
- **즉시 반영 보증(명시화)**: 저장 후 `_settings.Current`가 갱신값을 보유함은 이미 성립(VF-5). 문서에 "런타임 소비처(`CaptureViewModel`·`FrameSelectViewModel`)는 매 세션 `Settings.Current`를 읽으므로 다음 촬영에 즉시 반영"을 계약으로 명기. 추가 작업 불필요(회귀 방지 위해 테스트만 보강).
- **컷수·카운트다운 세그먼트 대안(선택)**: ComboBox 대신 세그먼트(`Segment` 스타일 이미 존재, `Controls.xaml:403`)로 바꾸면 값 바인딩 혼동 여지 제거 + 터치 UX 향상. U1과 함께 적용 권장(§4).

### 3.3 검증 포인트(headless)

- 단위(`SettingsTests` 확장): 값 변경 → `Save()` == true → 새 `IniSettingsService` 인스턴스 `Load()` 시 값 유지(라운드트립). 쓰기 불가 경로 주입 시 `Save()` == false(예외 아님). 클램프 유지.
- 사용자 확인(육안): 설정 변경·저장 → 재시작 → 값 복원. 저장 실패 경로에서 오류 토스트.

---

## 4. U1 — 설정 화면 레이아웃 정돈 (디자인 스펙)

현재 항목이 `Grid`(좌 라벨/우 컨트롤)와 `StackPanel`(세로)로 혼재해 정렬선이 없다(VF-13). 라이트 토큰으로 정돈한다.

### 4.1 레이아웃 규약

- **일관 행 템플릿**: 모든 설정 항목을 동일한 2열 그리드 행으로 통일 — `ColumnDefinitions: Auto/*`가 아니라 **라벨 고정폭 + 컨트롤 우측 정렬**. 라벨 열 너비 통일(예: `240`), 컨트롤은 우측 정렬 고정폭. 짧은 값(컷수·토글)과 긴 값(경로·URL TextBox)은 **행 유형 2종**으로 분리:
  - **인라인 행**(라벨 좌 · 컨트롤 우): 컷수/카운트다운/거울/플래시/포맷/QR/로컬저장/표시모드 — 값이 짧음.
  - **스택 행**(라벨 위 · 전폭 입력 아래): 로컬저장경로/보관시간/카메라장치/HostingURL/StorageBucket — 텍스트 입력.
- **그룹 소제목**: [앱 설정] 카드 내부를 논리 그룹으로 나눔 — "촬영"(컷수·카운트다운·거울·플래시), "출력·전송"(포맷·QR·로컬저장·경로·보관), "장치·표시"(카메라·표시모드), "고급"(HostingURL·StorageBucket). 각 그룹은 `Text.Title` 소제목 + 구분선(`Brush.Divider`).
- **간격 토큰**: 행 간 `Space.M`(16), 그룹 간 `Space.L`(24), 카드 패딩 `Pad.L`. 라벨-컨트롤 수직 정렬 `VerticalAlignment=Center`.
- **정렬**: 인라인 행의 컨트롤 우측 끝 라인을 통일(고정폭 컨트롤 + 우측 정렬). 토글/콤보 폭 표준화(콤보 140, 토글 56).

### 4.2 컷수·카운트다운을 세그먼트로 (권장)

값이 3~4개 고정 옵션이므로 ComboBox보다 **세그먼트 컨트롤**(`Segment` 스타일)이 터치·가독에 유리하고 B2의 값 바인딩 혼동도 없앤다. 세그먼트는 `ToggleButton` 그룹 + VM의 옵션 리스트 바인딩(`ItemsControl` + `Segment` 스타일, 선택은 VM 프로퍼티와 동기). U1·B2 공동 개선.

> 이 세그먼트 전환은 선택 개선이다. 최소 구현은 U2의 ComboBox 재스타일만으로도 요구 충족. WBS에서 세그먼트는 Step 3(레이아웃) 내 선택 항목으로 둔다.

---

## 5. U2 — ComboBox 세련화 (컴포넌트 스펙)

현재 ComboBox는 `ControlTemplate`이 없어 OS 기본 룩(VF-9). 라이트 테마 `ComboBox` 템플릿을 `Controls.xaml`에 추가한다.

### 5.1 ComboBox ControlTemplate 스펙 (라이트)

- **ToggleButton(닫힌 박스)**: `Brush.Bg`(흰) 배경 + `Brush.Border` 1px + `Radius.S`(8). 높이 `Touch.Min`(48). 패딩 `10,8`. 우측 **화살표 글리프**(Path chevron ▾, `Brush.Text.Tertiary`). hover=`Brush.Surface.Hover` 배경 또는 보더 강조, focus/open=`Brush.Accent` 2px 보더(입력류와 일관).
- **ContentSite**: 선택 항목 표시(`ContentPresenter`), `Brush.Text.Primary`.
- **Popup(드롭다운)**: `Brush.Bg` 배경 + `Brush.Border` 1px + `Radius.S` + `Shadow.Pop`(라이트 soft shadow). `MaxHeight` 240 + 스크롤. `Placement=Bottom`.
- **ComboBoxItem 스타일**: 높이 ≥ `Touch.Min` 근접(터치), 패딩 `12,10`, `Brush.Text.Primary`. hover=`Brush.Surface.Hover`, 선택(IsHighlighted/IsSelected)=`Brush.Accent.Soft` 배경 + `Brush.Accent.Text` 텍스트. `Radius.S` 살짝.
- **화살표**: 열림 시 chevron 180° 회전(`RenderTransform`, 선택). 애니메이션은 절제(즉시 또는 100ms).
- **접근성**: 화살표·텍스트 대비 §it2 2.3.1 준수(Muted 텍스트 금지, 항목 텍스트는 Ink).
- **암묵 vs 키**: 기존이 암묵 `ComboBox` 스타일이므로 **암묵 스타일에 `Template` 추가**(설정 화면 콤보 전부 자동 적용). `ComboBoxItem`도 암묵 스타일.

### 5.2 성능

- ComboBox 항목 수가 적어(2~4개) 가상화 불필요. Popup은 표준 `Popup` 사용(리소스 경량).

---

## 6. U3 — 로그인 Enter 키 (스펙)

- **로그인 버튼 `IsDefault="True"`**: `LoginGuestView.xaml`의 로그인 `Button`에 `IsDefault="True"` 추가 → 화면에 기본 버튼이 되어 Enter가 커맨드를 실행. 단, `IsDefault`는 포커스가 다른 기본처리 컨트롤에 있지 않을 때 동작.
- **PasswordBox Enter 보증**: `PasswordBox`는 텍스트 입력 컨트롤이라 Enter가 삼켜질 수 있으므로, **`PasswordBox`·`TextBox`에 `KeyDown`(또는 `InputBinding` Key=Enter)** 처리로 `LoginCommand` 실행 보강. code-behind에서 이미 `OnPasswordChanged`로 VM에 전달 중이므로, `PasswordInput.KeyDown`에서 Enter 시 `(_vm)?.LoginCommand.Execute(null)` 호출(MVVM 유지 위해 커맨드 실행).
  - MVVM 순수성 대안: `TextBox`/`PasswordBox`를 감싼 컨테이너에 `KeyBinding Key=Enter Command=LoginCommand`. PasswordBox가 Enter를 처리 안 하도록 하려면 code-behind가 더 확실 — **code-behind KeyDown → 커맨드 실행** 채택(최소 코드, 확실).
- **IsBusy 가드**: `LoginCommand`가 이미 `IsBusy` 가드(중복 방지) 보유(`LoginGuestViewModel.cs:35`). Enter 연타 안전.

---

## 7. U4 — 카메라 로딩 대기 표시 (스펙)

### 7.1 준비 상태 노출

- **`CaptureViewModel`에 `CameraState` 노출**: enum `CameraLoadState { Initializing, Ready, Failed }`. `OnEnterAsync`에서 `StartAsync` 호출 **전** `Initializing`, `FrameReady` 첫 프레임 수신 시 `Ready`, `StartAsync`가 false면 `Failed`.
  - 첫 프레임 감지: `_camera.FrameReady`를 한 번 구독해 최초 발생 시 `Ready`로 전환 후 구독 해제(또는 `IsRunning` + 첫 프레임 플래그). `ICameraService`에 이미 `FrameReady` 이벤트·`IsRunning` 존재(`ICameraService.cs:11,17`)라 인터페이스 변경 최소.
  - 대안(더 명시적): `ICameraService`에 `event EventHandler? PreviewReady`(첫 가공 프레임 발행 시 1회) 추가. 채택: **기존 `FrameReady` 첫 수신으로 판정**(인터페이스 무변경, VM에서 처리) — 리스크 최소. `PreviewReady` 추가는 선택.
- `CameraAvailable`(기존 bool)은 `Failed` 판정과 중복 → `CameraState`로 흡수하거나 병행(하위호환).

### 7.2 로딩 오버레이 (View)

- `CaptureView.xaml`에 **로딩 오버레이**: `CameraState==Initializing`일 때 표시. 반투명 스크림(`Brush.Scrim`) 위 중앙에 **스피너 + "카메라 준비 중…" 안내문**(`Brush.OnAccent`, 촬영 배경이 다크라 밝게).
  - 스피너: `Storyboard`로 회전하는 링(`Ellipse`/`Arc` `RotateTransform` 무한 회전) 또는 원형 인디케이터. 코드비하인드 없이 XAML `Storyboard`.
  - 준비되면(`Ready`) 오버레이 `Collapsed`(페이드아웃 0.2s 선택).
  - `Failed`이면 오버레이 대신 기존 `StatusMessage`("카메라를 찾을 수 없습니다") 표시.
- 컨버터: `CameraState`→Visibility(Initializing만 Visible). enum-to-visibility 컨버터 신규 또는 파라미터 컨버터.

### 7.3 상태 전이 주의

- 카운트다운/촬영 시퀀스(`RunCaptureSequenceAsync`)는 `Ready` 이후 시작해야 자연스럽다. 현재는 `StartAsync` 성공 직후 시퀀스 시작 — 첫 프레임 전 카운트다운이 시작될 수 있다. **`Ready` 전까지 시퀀스 시작을 지연**(첫 프레임 대기)하는 것이 UX상 옳다(로딩 중 카운트다운 방지). WBS Step 6에서 시퀀스 시작을 `Ready` 이후로 게이트.

---

## 8. U5 — 셔터 버튼 (컴포넌트 스펙)

### 8.1 셔터 버튼 디자인

기존 "바로 촬영" 텍스트 버튼(`CaptureView.xaml:32-34`)을 **원형 셔터 버튼**으로 교체.

- **모양**: 이중 원 셔터 — 바깥 링(흰 테두리 `Brush.OnAccent`, 투명/반투명 내부) + 안쪽 원(로즈 `Brush.Accent` 채움). 지름 88~96px(터치 크게). 촬영 화면이 다크라 흰 링이 잘 보임.
  - 구현: `Button` + `ControlTemplate`(`Grid` 안 `Ellipse` 2개). 스타일 키 `Button.Shutter`를 `Controls.xaml`에 추가.
  - 상태: hover=안쪽 원 약간 확대 or `Accent.Hover`, press=안쪽 원 축소(0.9 `ScaleTransform`)로 셔터 눌림감. disabled 불필요(항상 활성).
- **라벨**: 셔터 **아래**에 작게 "바로 촬영"(`Text.Caption`, `Brush.OnAccent` 반투명). 셔터+라벨을 `StackPanel`(세로)로 묶어 하단 중앙 배치(`VerticalAlignment=Bottom`, `Margin 0,0,0,40`).
- **커맨드**: 기존 `ShootNowCommand` 그대로 바인딩(로직 무변경).
- **접근성**: `AutomationProperties.Name="바로 촬영"`.

### 8.2 배치·간섭

- 셔터는 하단 중앙, 취소는 우상단(기존 유지), 카운트다운은 중앙(기존). 셔터가 카운트다운 숫자와 겹치지 않게 하단 여백 확보.

---

## 9. 리소스·파일 변경 요약

| 파일 | 변경 | 요구 |
|---|---|---|
| `src/MCPhoto.App/SessionContext.cs`(또는 신규 `ISessionService`) | 계정 단일 소스·`CurrentUserChanged`·`Login/Logout`·`Reset(clearUser)` | B1 |
| `src/MCPhoto.App/AppShellViewModel.cs` | 세션 이벤트 구독, 미러/수동 동기화 제거, Reset 정책 | B1 |
| `src/MCPhoto.App/ViewModels/HomeViewModel.cs` | `Start`의 Reset을 로그인 보존으로 | B1 |
| `src/MCPhoto.App/ViewModels/SettingsViewModel.cs` | 세션 소스 구독, Save 성공/실패 토스트 분기 | B1·B2 |
| `src/MCPhoto.Core/Settings/ISettingsService.cs`·`IniSettingsService.cs` | `bool Save()` + 쓰기 폴백 체인 | B2 |
| `src/MCPhoto.App/Views/SettingsView.xaml` | 2열 정렬·그룹 소제목·간격 토큰(+세그먼트 선택) | U1 |
| `src/MCPhoto.App/Themes/Controls.xaml` | `ComboBox`/`ComboBoxItem` `ControlTemplate`, `Button.Shutter` | U2·U5 |
| `src/MCPhoto.App/Views/LoginGuestView.xaml`(+`.cs`) | 로그인 `IsDefault` + PasswordBox Enter→커맨드 | U3 |
| `src/MCPhoto.App/ViewModels/CaptureViewModel.cs` | `CameraState` 노출 + 첫 프레임 Ready 판정 + 시퀀스 게이트 | U4 |
| `src/MCPhoto.App/Views/CaptureView.xaml` | 로딩 오버레이(스피너+안내), 셔터 버튼+라벨 | U4·U5 |
| `src/MCPhoto.App/Converters/CommonConverters.cs` | `CameraState`→Visibility 컨버터(신규) | U4 |
| `tests/MCPhoto.Tests/` | `SessionServiceTests`(신규), `SettingsTests`(Save 반환·폴백 확장) | B1·B2 |

---

## 10. 리스크 & 완화

| # | 리스크 | 영향 | 완화 | 검증 |
|---|---|---|---|---|
| R1 | 세션 Reset 정책 변경이 "다음 손님 로그아웃"을 깨뜨림 | 이전 손님 계정 잔존 | `Reset(clearUser:true)`를 유휴·세션완료(Done→Home)에 명시 적용, 취소/오버레이는 보존 | Step 1 단위 |
| R2 | 계정 단일 소스 전환 중 상단바/설정 바인딩 누락 | 상태 표시 불일치 | 이벤트 구독으로 자동 통지, 수동 Sync 산재 제거 후 grep | Step 1 빌드·grep |
| R3 | `bool Save()` 시그니처 변경이 기존 호출부(MainWindow.OnClosing 등) 파손 | 빌드 실패 | 호출부(`MainWindow.xaml.cs:72`) 동시 갱신, 반환 무시 허용 | Step 2 빌드 |
| R4 | ComboBox 커스텀 템플릿의 Popup 렌더/바인딩 회귀 | 콤보 오동작 | 표준 파트 이름(`PART_EditableTextBox` 불요, non-editable) 준수, 기존 SelectedItem 바인딩 유지 | Step 4 빌드·육안 |
| R5 | 카메라 첫 프레임 Ready 판정이 장치 없음/지연 시 무한 로딩 | 로딩 안 사라짐 | `StartAsync` false 즉시 `Failed`, 타임아웃(예 8초) 후 `Failed` 폴백 | Step 6 빌드·육안 |
| R6 | PasswordBox Enter code-behind가 MVVM 위반 우려 | 리뷰 지적 | 커맨드 실행만(로직은 VM), 기존 OnPasswordChanged 패턴과 동일 수준 | Step 5 리뷰 |

---

## 11. 사용자 확인 필요 목록 (UI 육안 — headless 불가)

> WBS의 완료 기준은 전부 headless(build/test/grep). 아래는 구현 후 사용자가 실행해 육안 확인할 항목(각 Step trigger/non-goal로 분리).

1. **B1**: 로그인 후 설정 화면 진입·복귀 시 로그인 유지, 상단 바 계정 라벨 유지. [촬영하기] 후에도 로그인 유지(커스텀 프레임 노출). 유휴 타임아웃·완료 후 홈 복귀 시에는 로그아웃(다음 손님).
2. **B2**: 설정 변경·저장 → 재시작 후 값 복원. 저장 실패 경로(쓰기 불가)에서 오류 토스트. 변경한 컷수/카운트다운/거울/로컬저장이 다음 촬영에 실제 반영.
3. **U1**: 설정 화면 항목이 정렬·그룹핑되어 읽기 쉬운지.
4. **U2**: ComboBox가 라이트 테마와 조화(라운드·화살표·hover·드롭다운 항목)되는지.
5. **U3**: 로그인 화면에서 비번 입력 후 Enter로 로그인되는지.
6. **U4**: 촬영 진입 시 "카메라 준비 중" 로딩이 뜨고 프리뷰 준비되면 사라지는지. 로딩 중 카운트다운이 시작되지 않는지.
7. **U5**: [바로 촬영]이 원형 셔터 버튼 + 라벨로 보이고 눌림감이 있는지.

## 부록. 참고

- it2 디자인 시스템·토큰: `docs/design/wpf-it2-design.md` §2(라이트 팔레트·§2.3.1 대비·Controls)
- 상태머신·오버레이 네비: `docs/design/wpf-it2-design.md` §5, `SessionStateMachine.cs`
