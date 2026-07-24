# MCPhoto 이터레이션12 WBS 블루프린트 (wpf-it12-wbs.md)

> 설계 원본: `docs/design/wpf-it12-design.md` (R1~R4 상세·근거 file:line 포함)
> 대상: MCPhoto (WPF / .NET 8, MVVM=CommunityToolkit.Mvvm)
> 각 Step은 **self-contained** — 대화 컨텍스트 없는 fresh 에이전트가 그 단계만 읽고 실행 가능.

---

## 검증된 사실 (verified facts)

- 기존 QR 게이트는 3지점 구현: `LoadSettings` 게스트 강제 off(`SettingsViewModel.cs:192-198`),
  `SaveSettings` 게스트 미기록(`:250-255`, `:262`, `:266`), XAML `IsEnabled="{Binding IsLoggedIn}"`
  (`SettingsView.xaml:195`, `:312`, `:316`). 회귀 테스트 `Guest_Qr_Forced_Off`/
  `Guest_Save_Preserves_Ini_Qr_And_Firebase`(`SettingsViewModelTests.cs:171-198`).
- 게이트는 VM 계층에만 존재. `AppSettings` 모델은 항상 전 필드 직렬화(`AppSettings.cs:176-206`).
  모델 라운드트립 테스트(`SettingsTests.cs`/`FiltersTests.cs`/`CutSelectViewModelTests.cs`)는 게이트 밖.
- `MirrorMode`/`RetakeEnabled`/`RetakeLimit`/`FilterGrayscale`/`FilterBrightness`/`FilterBeauty`에는
  `partial void On...Changed` 핸들러가 없음(`SettingsViewModel.cs` 전체 확인) → 로드 시 강제 off 부작용 없음.
- `Retake_Settings_Save_And_Load_RoundTrip`(`SettingsViewModelTests.cs:112-129`)는 **게스트** VM으로
  재촬영 저장을 단언 → R1에서 게이트 적용 시 실패한다(수정 필요).
- `IniBuildInfoService.DisplayText`는 `v{Version}`·`Site`·`BuildDate`를 `"  ·  "`로 조인, BuildDate 추가는
  `IniBuildInfoService.cs:34`. `Version`/`BuildDate`/`Site` 프로퍼티·`bldinfo.ini` 키는 별도(`:20`, `:23-25`).
  테스트 `DisplayText_Joins_Present_Parts_Only`(`BuildInfoServiceTests.cs:42-52`).
- 반응형 폴백 `OnTwoColSizeChanged`(`SettingsView.xaml.cs:15-33`)는 `LeftCol`/`RightCol`/`ColGap`/
  `TwoColArea.ColumnDefinitions[2]`만 조작 — 개별 자식 미참조 → R2 이동 무영향.
- Toggle 스타일 `Themes/Controls.xaml:477-509`(`x:Key="Toggle"`). `BasedOn="{StaticResource ...}"` 파생은
  `SettingsView.xaml:9,23`에서 이미 사용(테마 키 도달 가능).
- 헤드리스 XAML 정적 키 검증 인프라 존재: `XamlResourceTests.cs`(STA, 창 미표시), `DiagnosticsWindow` 패턴
  `:188-218`, App 컨버터 키 목록 `:198-203`.
- 편집 대상 파일 인코딩: **전부 UTF-8 BOM 없음**(`.cs`는 `usi`, `.xaml`은 `<`로 시작 — BOM `EF BB BF` 부재).
- Baseline: 빌드 0 error / 0 warning, 테스트 341 passed.

## 미검증 가정 (open assumptions)

- (A1) `MirrorMode`/`RetakeEnabled`/필터를 로드 시 강제 off해도 QR과 동일하게 다른 화면 동작에 부작용이
  없다. → 검증 단계: **Step 2** (신규 VM 테스트 green + 전체 `dotnet test`).
- (A2) `Toggle.Gated`(BasedOn `Toggle`) 로컬 스타일이 `SettingsView` 런타임에서 StaticResource 해석되고
  XamlParseException이 없다. → 검증 단계: **Step 4** (headless 정적 키 검증 + 빌드).
- (A3) `DataTrigger Binding="{Binding IsGuest}"`가 로그인 시 툴팁 미노출/게스트 시 노출로 동작한다(수동 관측
  또는 논리). → 검증 단계: **Step 4** (완료 기준의 관측 항목 — 수동 스모크 + 정적 검증으로 회귀 방지).
- (A4) R2 이동 후에도 `x:Name`(LeftCol/RightCol/ColGap) 유지로 반응형 폴백이 동작한다. → 검증 단계:
  **Step 3** (빌드 + headless 로드; 폴백은 수동 리사이즈 스모크 권장).

---

## Step 1: R4 — 버전 표기에서 BuildDate 제외

- **Context Brief**: 우하단 버전 캡션이 `v{Version} · {Site} · {BuildDate}`로 표기된다. 업데이트 지연 시
  오래된 앱으로 오인될 위험이 있어 **표기에서만** BuildDate를 뺀다. BuildDate 프로퍼티·`bldinfo.ini` 키·
  로드 로직은 그대로 유지(모델 보존). 다른 요구와 독립적인 최소 변경.
- **대상 파일**: `src/MCPhoto.Core/Build/IniBuildInfoService.cs`,
  `src/MCPhoto.Core/Build/IBuildInfoService.cs`(doc comment),
  `tests/MCPhoto.Tests/BuildInfoServiceTests.cs`
- **선행 조건**: 없음
- **구현 내용**:
  1. `IniBuildInfoService.DisplayText`(`:28-37`)에서 BuildDate 조인 라인(`:34`
     `if (!string.IsNullOrWhiteSpace(BuildDate)) parts.Add(BuildDate);`) 삭제. 구분자 `"  ·  "` 유지.
     결과: `v1.0.0  ·  Beta`(Site 있음) / `v2.1.0`(Site 없음). `Version`/`BuildDate`/`Site` 프로퍼티,
     `KeyBuildDate`, 로드 로직(`:58-59`)은 **변경 금지**.
  2. doc comment 예시 갱신: `IBuildInfoService.cs:19`, `IniBuildInfoService.cs:27`의
     `"v1.0.0 · Beta · 2026-07-23"` → `"v1.0.0 · Beta"`.
  3. `BuildInfoServiceTests.DisplayText_Joins_Present_Parts_Only`(`:42-52`) 기대값 수정:
     full 케이스 `"v1.0.0  ·  Beta  ·  2026-07-23"` → `"v1.0.0  ·  Beta"`. verOnly `"v2.1.0"`은 불변.
- **검증 명령**:
  - `dotnet test tests/MCPhoto.Tests/MCPhoto.Tests.csproj --filter "FullyQualifiedName~BuildInfoServiceTests"`
  - 또는 `build-verify` 스킬(빌드 0 warning + 테스트)
- **완료 기준**:
  - [관측] `DisplayText`가 full 입력에서 `"v1.0.0  ·  Beta"`(BuildDate 없음), verOnly에서 `"v2.1.0"` 반환.
    `BuildInfoServiceTests` 전부 green.
  - [non-goal] `Valid_Values_Are_Loaded`(`:27-39`) 여전히 통과 — `BuildDate` 프로퍼티가 `"2026-07-23"`로
    로드됨(필드·ini 키 보존 증거). `bldinfo.ini` 스키마 불변.
  - [trigger] 표기 변경은 코드 상수 로직 변경으로만 발생 — ini 값 변경 없이 표기만 바뀜.
- **롤백**: 이 단계 커밋 revert (다른 Step과 독립).
- [ ] 완료

---

## Step 2: R1 — SettingsViewModel 권한 게이트 확대 (VM, 순수 C#)

- **Context Brief**: 게스트(비로그인)의 설정 편집 권한을 `MirrorMode`·`RetakeEnabled`·`RetakeLimit`·
  필터 3종(`FilterGrayscale`/`FilterBrightness`/`FilterBeauty`)까지 제한한다. **기존 QR 게이트와 동일
  메커니즘**: 로드 시 게스트면 소스단 off 표시, 저장 시 게스트면 해당 필드 미기록(ini 원값 보존=클로버
  금지). 이 게이트는 "편집 권한"만 제한하며 런타임(촬영/필터)은 `Settings.Current`(ini)를 읽으므로 관리자
  설정값대로 동작한다(QR과 동일). 게이트는 VM에만 있고 `AppSettings` 모델은 불변.
- **대상 파일**: `src/MCPhoto.App/ViewModels/SettingsViewModel.cs`,
  `tests/MCPhoto.Tests/SettingsViewModelTests.cs`
- **선행 조건**: 없음 (Step 1과 병렬 가능)
- **구현 내용**:
  1. `LoadSettings` 게스트 블록(`:193-198`)에 강제 off 추가(생성된 프로퍼티명으로 접근, MVVMTK0034 회피):
     ```csharp
     MirrorMode = false;
     RetakeEnabled = false;
     FilterGrayscale = false;
     FilterBrightness = false;
     FilterBeauty = false;
     ```
     (`RetakeLimit`는 강제하지 않음 — int·하위·숨김.)
  2. `SaveSettings`에서 무조건 기록되던 필드를 게이트로 감싼다:
     - `s.MirrorMode = MirrorMode;`(`:244`) → `if (!IsGuest) s.MirrorMode = MirrorMode;`
     - `s.RetakeEnabled`(`:247`) + `s.RetakeLimit`(`:248`) → `if (!IsGuest) { s.RetakeEnabled = ...;
       s.RetakeLimit = ...; }`
     - `s.FilterGrayscale/Brightness/Beauty`(`:256-258`) → `if (!IsGuest) { ... }`
     - 기존 QR/Firebase 게이트(`:250-255`, `:262`, `:266`)는 **그대로 유지**.
  3. 오래된 주석 `:247`("촬영 옵션(게스트 게이트 대상 아님)") 삭제/수정.
  4. 테스트:
     - **수정**: `Retake_Settings_Save_And_Load_RoundTrip`(`:112-129`)를 **로그인 세션**으로 재작성
       (`session.Login(new User{ Id="admin", Password="pw", Role=UserRole.Admin })` 후 VM 생성 —
       `LoggedIn_OpenDiagnostics_Shows_Dialog_Once`(`:238-254`)의 생성 패턴 참조). 로그인 사용자는 재촬영
       저장 가능 단언 유지.
     - **신규** `Guest_Save_Preserves_Ini_Mirror_Retake_Filters`: ini에 관리자값(예:
       `RetakeEnabled=true, RetakeLimit=3, FilterGrayscale=false`) 저장 → 게스트 VM `OnEnterAsync` →
       `SaveSettingsCommand.Execute(null)` → 재로드 시 ini 원값 보존 단언(`Guest_Save_Preserves_Ini_Qr_And_Firebase`
       `:181-198` 동형).
     - **신규** `Guest_Gated_Fields_Forced_Off_On_Load`: 게스트 로드 후 `vm.MirrorMode==false`,
       `vm.RetakeEnabled==false`, 필터 3종 false 단언.
     - **신규** `LoggedIn_Saves_Mirror_Retake_Filters`: 로그인 VM에서 값 편집 → 저장 → 재로드 라운드트립.
- **검증 명령**:
  - `dotnet test tests/MCPhoto.Tests/MCPhoto.Tests.csproj --filter "FullyQualifiedName~SettingsViewModelTests"`
  - 이어서 전체 `dotnet test`(A1 회귀 확인) + `build-verify`(0 warning).
- **완료 기준**:
  - [관측] 신규 3개 + 수정 1개 테스트 green. 게스트 저장 후 재로드 시 `MirrorMode/RetakeEnabled/RetakeLimit/
    Filter*` ini 원값 보존. 로그인 사용자는 정상 저장.
  - [non-goal] 게이트 **비대상** 필드(`CutCount`/`CountdownSec`/`FlashMode`/`ShutterSound`/`SaveLocalCopy`/
    `RetentionHours`/`LocalSavePath`/`OutputFormat`/`DisplayMode`/`CameraDevice`)는 게스트도 여전히 저장됨.
    모델 라운드트립 테스트(`SettingsTests`/`FiltersTests`/`CutSelectViewModelTests`) 전부 통과(무영향).
    전체 `dotnet test` 341 이상 all green, 0 warning.
  - [trigger] 강제 off는 게스트 로드 시에만, 미기록은 게스트 저장 시에만. 로그인 세션에서는 게이트 미발동.
- **롤백**: 이 단계 커밋 revert (Step 3/4 미착수 시 XAML 무변이라 독립 복구 가능).
- [ ] 완료

---

## Step 3: R2 — SettingsView 레이아웃 재배치 (XAML)

- **Context Brief**: 설정 화면 [앱 설정] 카드에서 **QR 전송(+사진/타임랩스 하위 토글)**과 **로컬 저장 토글**을
  Block 1 우열(장치·표시)에서 Block 2 좌열(출력·전송)으로 옮긴다. 장치·표시에는 카메라 장치·표시 모드만
  남긴다. 출력·전송 내부 순서: 출력 포맷 → QR 전송(+하위) → 로컬 저장 → 로컬 저장 경로 → 보관 시간.
  바인딩 경로·VM 멤버는 불변(위치만 이동). 반응형 폴백 코드비하인드는 `x:Name`만 참조하므로 무영향.
- **대상 파일**: `src/MCPhoto.App/Views/SettingsView.xaml`
- **선행 조건**: 없음 (그러나 **Step 4보다 먼저** — 같은 파일 순차 편집)
- **구현 내용**:
  1. `RightCol`(장치·표시, `:137-225`)에서 QR 전송 행(`:187-196`), QR 하위 StackPanel(`:197-216`),
     로컬 저장 행(`:217-224`)을 **잘라낸다**. 잔류: 카메라 장치(`:139-167`), 카메라 없음 안내(`:168-175`),
     표시 모드(`:176-186`).
  2. Block 2 좌열 출력·전송 StackPanel(`:239-262`)에 아래 순서로 재배치:
     `출력 포맷`(기존) → **QR 전송 행 + QR 하위 StackPanel**(이동) → **로컬 저장 행**(이동) →
     `로컬 저장 경로`(기존 FullRow) → `보관 시간(1~72h)`(기존). 각 행의 `Grid`/`Style`/바인딩은 원본 그대로
     복사(경로 변경 없음).
  3. QR 전송 토글 바인딩 보존: `IsChecked="{Binding EnableQrDelivery}"`,
     `IsEnabled="{Binding IsLoggedIn}"`. QR 하위 StackPanel의
     `Visibility="{Binding EnableQrDelivery, Converter={StaticResource BoolToVis}}"` 유지.
     로컬 저장 `IsChecked="{Binding SaveLocalCopy}"` 유지.
  4. `TwoColArea`/`LeftCol`/`RightCol`/`ColGap` `x:Name`과 코드비하인드(`SettingsView.xaml.cs`) **변경 금지**.
  5. Style 변경 금지(이 단계는 위치 이동만 — 게이트/툴팁은 Step 4).
- **검증 명령**:
  - `build-verify` 스킬 (빌드 0 warning)
  - 전체 `dotnet test`(기존 XAML 회귀 테스트 green 유지)
  - 수동 스모크(권장): 앱 실행 → 설정 진입 → 창 폭 축소로 Block 1 1열 폴백 정상 확인(A4).
- **완료 기준**:
  - [관측] 빌드 0 error/0 warning. 설정 화면에서 QR 전송(+하위)·로컬 저장이 "출력·전송" 그룹에 표시되고,
    "장치·표시"에는 카메라 장치·표시 모드만 남는다. QR 하위는 QR on일 때만 노출(기존 동작 유지).
  - [non-goal] VM/바인딩 경로 변경 없음(`EnableQrDelivery`/`SendPhoto`/`SendTimelapse`/`SaveLocalCopy`
    바인딩 그대로). 반응형 1열 폴백 정상(x:Name 3종 유지). 고급 섹션·서버 연결·진단 버튼 불변.
  - [trigger] 레이아웃 변경은 XAML 트리 이동으로만 — 런타임 상태·저장 동작에 영향 없음.
- **롤백**: 이 단계 커밋 revert. (Step 2와 파일이 달라 독립 복구 가능.)
- [ ] 완료

---

## Step 4: R1(XAML 게이트) + R3(게스트 게이트 안내)

> **개정(2026-07-24)**: R3는 hover 툴팁(`Toggle.Gated`) 대신 **인라인 노티**로 구현됨 — 게이트 토글 좌측에 게스트 전용 "로그인 필요" 캡션(`GuestGateNote`, `IsGuest` 시 Visible)을 상시 노출. 아래 툴팁 서술은 초안 기록.

- **Context Brief**: 게스트 게이트 토글에 대해 (a) `IsEnabled="{Binding IsLoggedIn}"`로 컨트롤을 비활성화하고,
  (b) 게스트가 hover하면 "로그인 후 이용 가능합니다." 툴팁을 노출한다. 비활성 컨트롤은 기본적으로 툴팁이 안
  뜨므로 `ToolTipService.ShowOnDisabled="True"`가 필수. 로그인 시에는 툴팁이 뜨지 않아야 한다. 이를 위해
  테마 `Toggle`을 `BasedOn`으로 파생한 로컬 스타일 `Toggle.Gated`(게스트일 때만 `DataTrigger`로 ToolTip
  설정)를 만들어 게이트 토글 6종에 적용한다.
- **대상 파일**: `src/MCPhoto.App/Views/SettingsView.xaml`,
  `tests/MCPhoto.Tests/XamlResourceTests.cs`(정적 키 검증 추가)
- **선행 조건**: **Step 3 완료**(같은 파일, 레이아웃 확정 후 스타일 적용)
- **구현 내용**:
  1. `SettingsView.xaml`의 `<UserControl.Resources>`(`:7-32`)에 스타일 추가:
     ```xml
     <Style x:Key="Toggle.Gated" TargetType="ToggleButton" BasedOn="{StaticResource Toggle}">
         <Setter Property="ToolTipService.ShowOnDisabled" Value="True" />
         <Style.Triggers>
             <DataTrigger Binding="{Binding IsGuest}" Value="True">
                 <Setter Property="ToolTip" Value="로그인 후 이용 가능합니다." />
             </DataTrigger>
         </Style.Triggers>
     </Style>
     ```
  2. 게이트 토글 6종에 `Style="{StaticResource Toggle.Gated}"` 적용 + `IsEnabled="{Binding IsLoggedIn}"`:
     - 거울모드(`:90`) — Style 교체 + IsEnabled 추가
     - 재촬영 사용(`:116`) — Style 교체 + IsEnabled 추가
     - QR 전송(Step 3에서 출력·전송으로 이동됨) — Style 교체(IsEnabled는 이미 있음)
     - 흑백(`:285`)/밝게(`:293`)/뷰티(`:301`) — Style 교체 + IsEnabled 추가
  3. **적용 금지**: "원본(항상 제공)" 토글(`:276-277`, `IsChecked=True IsEnabled=False`)은 게이트가 아니라
     상시 표시자 — `Toggle`(비파생) 유지, 툴팁 없음. QR 하위/재촬영 횟수 콤보는 게스트에게 숨겨지므로 대상 아님.
  4. `XamlResourceTests.cs`에 `SettingsView_StaticResource_Keys_Resolve_In_Theme` 추가
     (`DiagnosticsWindow` 패턴 `:188-218` 복제): `SettingsView.xaml`에서 참조된 StaticResource 중 로컬 키
     (`RowLabel`/`SettingRow`/`FullRow`/`GroupTitle`/`GroupDivider`/`Toggle.Gated`)와 App 컨버터 키
     (`:198-203` 목록)를 제외한 나머지가 테마에서 전부 해석되는지 검증(창 미표시, STA).
- **검증 명령**:
  - `dotnet test tests/MCPhoto.Tests/MCPhoto.Tests.csproj --filter "FullyQualifiedName~XamlResourceTests"`
  - `build-verify`(0 warning) + 전체 `dotnet test`.
  - 수동 스모크(A3, 권장): 게스트로 설정 진입 → 거울모드/재촬영/QR/필터 토글 hover → 툴팁 노출 확인.
    로그인 후 동일 토글 hover → 툴팁 미노출 + 컨트롤 활성 확인.
- **완료 기준**:
  - [관측] 게스트: 6개 게이트 토글이 Disable + off 표시 + hover 시 "로그인 후 이용 가능합니다." 노출.
    로그인: 동일 토글 활성 + hover 시 툴팁 없음. `SettingsView_StaticResource_Keys_Resolve_In_Theme`
    green(모든 테마 참조 해석, `Toggle.Gated` 포함 XamlParseException 없음). 빌드 0 warning.
  - [non-goal] "원본(항상 제공)" 토글은 툴팁 없음·계속 비활성 표시자로 유지. QR 하위/재촬영 콤보에 툴팁 없음.
    고급 섹션 TextBox 변경 없음. 로그인 상태에서 어떤 게이트 토글도 툴팁을 표시하지 않음(negative case).
  - [trigger] 툴팁은 `IsGuest=true`일 때만 `DataTrigger`로 활성 — 로그인 시 ToolTip unset(null)이라 미노출.
    컨트롤 비활성화는 `IsLoggedIn=false`일 때만.
- **롤백**: 이 단계 커밋 revert (Step 3 상태로 복귀).
- [ ] 완료

---

## Step 5: 분석 문서 동기화

- **Context Brief**: R2(레이아웃)·R4(버전 표기) 변경이 기존 분석 문서의 기술과 어긋난다. 문서 세트는
  기능/구성 변경 시 함께 갱신하는 규약이 있어 실제 상태와 일치시킨다.
- **대상 파일**: `docs/analysis/11-exe-app-features.md`, `docs/analysis/12-exe-app-settings-and-config.md`
- **선행 조건**: Step 3(R2)·Step 1(R4) 완료
- **구현 내용**:
  1. `11-exe-app-features.md:192`("장치·표시: 카메라 장치…, 표시 모드…, **QR 전송(+하위…), 로컬 저장**") →
     QR 전송·로컬 저장을 출력·전송 항목으로 이동 반영. `:193`("출력·전송: 출력 포맷, 보관 시간, 로컬 저장
     경로")에 QR 전송·로컬 저장 추가.
  2. `11-exe-app-features.md:257` 및 `12-exe-app-settings-and-config.md:154`의 버전 표기 예시
     `"v1.0.0 · Beta · 2026-07-23"` → `"v1.0.0 · Beta"`로 갱신(단, `bldinfo.ini`의 `BuildDate` 키 설명은 유지).
  3. (선택) `12-...config.md`의 설정 표에 R1 게이트 대상(거울모드·재촬영·필터가 로그인 전용 편집)임을
     각주로 반영.
- **검증 명령**:
  - `grep -rn "2026-07-23" docs/analysis/11-exe-app-features.md docs/analysis/12-exe-app-settings-and-config.md`
    → 버전 표기 예시 문맥에서 잔존 없음(bldinfo 예시 값 문맥은 허용).
  - `grep -n "QR 전송" docs/analysis/11-exe-app-features.md` → 출력·전송 항목 아래에 위치.
- **완료 기준**:
  - [관측] 두 문서의 레이아웃 기술이 실제 XAML(출력·전송 그룹에 QR/로컬 저장)과 일치, 버전 표기 예시가
    `v1.0.0 · Beta`로 갱신.
  - [non-goal] `bldinfo.ini` 키(`BuildDate`) 설명·폴백 기술은 삭제하지 않음(필드 보존 사실 유지).
  - [trigger] 문서 갱신은 Step 1/3 코드 변경 반영 목적 — 코드/동작에 영향 없음.
- **롤백**: 이 단계 커밋 revert (문서만, 코드 무관).
- [ ] 완료

---

## 완결성 게이트 (developer 전달 전 자체 검사)

- [x] 검증된 사실 / 미검증 가정 목록 분리
- [x] 모든 가정(A1~A4)에 검증 단계 매핑(A1→S2, A2→S4, A3→S4, A4→S3)
- [x] 모든 Step에 7개 필수 필드(Context Brief/대상 파일/선행 조건/구현 내용/검증 명령/완료 기준/롤백) 채움
- [x] 모든 완료 기준이 관측 기반 3문 형식(UI Step 3·4는 non-goal·trigger 포함)
- [x] 검증 명령이 자동 실행 가능(`dotnet test --filter`, `build-verify`, `grep`)

## 단계 의존성 요약

```
Step 1 (R4, Core)      ─┐
Step 2 (R1, VM)        ─┼─ 병렬 가능
Step 3 (R2, XAML)      ─┘   ↓ (같은 파일)
Step 4 (R1 XAML + R3)  ──── Step 3 선행 필수
Step 5 (문서)          ──── Step 1·3 선행
```

## 인코딩 규칙 (전 Step 공통)

편집 대상은 전부 **UTF-8 BOM 없음**이다. Edit 도구는 기존 인코딩을 보존하므로 **BOM을 추가하지 말 것**.
한글 문자열(툴팁 "로그인 후 이용 가능합니다.", 주석) 삽입 시에도 UTF-8 유지. MVVMTK0034 회피를 위해
관측 프로퍼티는 생성된 프로퍼티명(`MirrorMode` 등)으로만 접근.
