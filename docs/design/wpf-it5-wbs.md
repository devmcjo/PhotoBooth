# MC포토 — 이터레이션 5 구현 WBS

| 항목 | 값 |
|------|-----|
| 대상 | `MCPhoto.sln` (.NET 8 WPF) — 이터레이션 5(계정 구조·WYSIWYG·QR·설정 레이아웃) |
| 설계 근거 | `docs/design/wpf-it5-design.md`, `docs/prd/iteration-5-account-preview-qr.md`, it2·it3·it4 설계 |
| 형식 | `docs/templates/WBS_BLUEPRINT.md` 준수 |
| 작성일 | 2026-07-21 |
| 빌드 검증 기준 | `dotnet build MCPhoto.sln -c Release`(error 0, 변경 프로젝트 warning 0) / `dotnet test` |

> 각 Step은 self-contained다. fresh 에이전트가 그 Step과 `wpf-it5-design.md`만 읽고 실행할 수 있게 작성했다.
> **모든 Step의 완료 기준은 headless(dotnet build/test·grep 정적확인)로만 판정한다.** UI 육안은 각 Step "사용자 확인 필요"로 분리, 전체 목록은 `wpf-it5-design.md` §11.
> ⚠️ **앱 실행 금지**(사용자 PC 사용 중 + UI 실행 차단 훅). 검증은 `dotnet build`/`dotnet test`/`grep`만.
> 색·토큰=라이트 A(it2 §2), 세션·유휴=it3, 편집기 좌표·종횡비=it4.

---

## 검증된 사실 (verified facts)

- **VF-1**: `ResultViewModel.Next`가 `EnableQrDelivery` 정확 체크(off→Done, on→Qr). 로컬 저장은 QR 분기 **이전**에 수행(`:116-123`) → saveLocalCopy on이면 업로드 실패해도 손실 0(VF-3). (근거: `ResultViewModel.cs:104-129`)
- **VF-2**: 실패 팝업은 `QrPopupViewModel.OnEnterAsync`가 진입 즉시 `UploadResultAsync` 시도 후 실패 시 위협 문구 + 화면 머무름(흐름 차단)(`:54,73`). **QR on일 때 업로드 실제 실패 시 발생.** (근거: `QrPopupViewModel.cs`)
- **VF-3**: 로컬 저장(saveLocalCopy on)은 QR과 독립·선행이라 업로드 실패해도 결과물 로컬 보존. (근거: `ResultViewModel.cs:104-129`)
- **VF-14**: B6 실체 = QR on인데 Storage 버킷 부재(버킷 목록 0, 404)로 업로드 실제 실패. 버킷 생성은 Blaze 필요=코드 범위 밖(외부 전제). 버킷 주입 경로(`AppSettings.StorageBucket`→`FirebaseClient(bucket:)`)는 이미 존재(it3). (근거: 오케스트레이터 진단, `FirebaseClient.cs:38-124`, `ServiceRegistration.cs`)
- **VF-4**: 캡처 스틸은 이미 슬롯 종횡비로 중앙 크롭 저장(`aspect=frame.Slots[0].AspectRatio`→StartAsync→CropCalculator). (근거: `CaptureViewModel.cs:50-52`, `CropCalculator.cs`)
- **VF-5**: 컷 선택 썸네일이 `Border 220×165`(4:3 고정)+`Image Stretch=UniformToFill` → 슬롯비율과 다르게 재크롭. B7 직접 원인. (근거: `CutSelectView.xaml:33-38`)
- **VF-7**: `DoneViewModel` 자동복귀(:21)·GoHome(:35)이 `clearUser:true`, 유휴(`AppShellViewModel.cs:192`)도 true. `ReturnHome(reason,clearUser=false)` 기본. (근거: `DoneViewModel.cs`, `AppShellViewModel.cs`)
- **VF-8**: 계정 기능이 `SettingsView`/`SettingsViewModel`에 있음([계정] 비번변경·[관리자] 계정생성/사용자관리/앱종료). (근거: `SettingsView.xaml:117-159`, `SettingsViewModel.cs`)
- **VF-9**: 계정 팝오버(MainWindow) [비번변경]·[관리자설정]이 `OpenAccountSettingsCommand`로 설정 페이지행(`AppShellViewModel.cs:221-224`). (근거: `MainWindow.xaml`, `AppShellViewModel.cs`)
- **VF-10**: 설정 `RowLabel Width=240` 고정+카드 MaxWidth720 → 긴 라벨 잘림(스크린샷). (근거: `setting_ng.png`, `SettingsView.xaml`)
- **VF-11**: 로그인 화면 자동 포커스 없음. (근거: `LoginGuestView.xaml.cs`)
- **VF-12**: it4 반영됨 — `EditorTransform`, `SlotAspect`(4:3/3:4/1:1), `AutoArrange(targetAspect)`. 슬롯 크기 슬라이더 없음. (근거: `EditorTransform.cs`, `FrameEditorViewModel.cs`, `SlotLayout.cs`)
- **VF-13**: 기존 테스트 — `SettingsTests`·`AppStateTests`·`SlotLayoutTests`·`EditorTransformTests`·`CropCalculatorTests`. (근거: `tests/MCPhoto.Tests/`)

## 미검증 가정 (open assumptions)

- **OA-1**: QR on 업로드 실패를 우아 처리(로컬 보존+정상 완료+비위협 안내)하면 세션이 팝업에 안 막히고 saveLocalCopy on 시 손실 0 → **검증: Step 1**. ⚠️ 실제 QR 성공은 Blaze+버킷 생성(외부 전제) 후에만 — 코드 범위 밖.
- **OA-2**: 썸네일 컨테이너를 슬롯 비율로 하면 컷 원본(이미 슬롯 비율)이 왜곡 없이 표시 → **검증: Step 2**.
- **OA-3**: Done clearUser:false가 유휴 로그아웃·다음손님 흐름 안 깨뜨림 → **검증: Step 3**.
- **OA-4**: 계정 전용 페이지(Account 상태+모드)가 상태머신·오버레이와 정합 → **검증: Step 4**.
- **OA-5**: 슬롯 일괄 스케일(70~130%)이 중심유지·클램프·종횡비유지 → **검증: Step 7**.

> 모든 가정이 검증 Step에 매핑됨(완결성 게이트 통과).

---

## 단계 의존 그래프 (병렬 식별)

```
Step 1 (B6 QR on 실패 우아 처리)  ── P1, 독립
Step 2 (B7 썸네일 WYSIWYG)       ── P1, 독립
Step 3 (B8 로그인 유지)          ── 독립(DoneViewModel·정책)
Step 4 (C1+C2 계정 구조 개편)    ── SettingsView/VM·AccountVM·상태머신·팝오버(큰 변경)
Step 5 (U7 설정 레이아웃)        ← Step 4 후(계정 섹션 제거된 SettingsView 위에서 재배치)
Step 6 (U8 로그인 포커스)        ── 독립
Step 7 (F1 슬롯 크기 슬라이더)   ── 독립(SlotLayout·FrameEditor)
```

- Step 1·2(P1)·3 우선. Step 5는 Step 4 후(같은 SettingsView, 계정 섹션 제거 후 재배치가 자연스러움). 나머지 독립.

---

## Step 1: B6 — QR on 업로드 실패의 우아한 처리 (정정: off 무시 아님)

- **Context Brief**: 촬영 완료 시 "전송에 실패했습니다. 네트워크 또는 Firebase 설정을 확인해 주세요." 위협적 팝업이 떠 흐름을 막는다(B6). **정정(오케스트레이터 진단): QR은 ON이고 업로드가 실제로 실패**한다 — 프로젝트에 Storage 버킷이 없어(버킷 목록 0, `.appspot.com`·`.firebasestorage.app` 둘 다 404) `FirebaseClient.UploadFileAsync`가 404 실패, `QrPopupViewModel`이 catch해 팝업(VF-14·VF-2). 버킷 생성은 **Blaze(결제) 필요=코드 범위 밖(외부 전제)**. 이 Step은 Blaze와 무관하게 **실패를 우아하게 처리**한다: 결과물 로컬 보존(이미 QR 이전 저장, VF-3), 세션 정상 완료(팝업이 흐름 비차단), 비위협 안내 + 재시도. 버킷 설정 경로(StorageBucket)는 이미 있으므로 재확인·문서화(설계 §2).
- **대상 파일**: `src/MCPhoto.App/ViewModels/QrPopupViewModel.cs`(실패 상태 + 비차단 완료), `src/MCPhoto.App/Views/QrPopupView.xaml`(업로드중/성공/실패 3상태 UI), `src/MCPhoto.App/Views/SettingsView.xaml`(StorageBucket 힌트 텍스트), `tests/MCPhoto.Tests/QrPopupUploadTests.cs`(신규, 목 IUploadService), `tests/MCPhoto.Tests/SettingsTests.cs`(StorageBucket 라운드트립).
- **선행 조건**: 없음.
- **구현 내용**:
  - `QrPopupViewModel`: 신규 `[ObservableProperty] bool _uploadFailed`. `OnEnterAsync`의 catch 블록에서 `UploadFailed=true`·`UploadSucceeded=false` + **비위협 문구**(saveLocalCopy on이면 "전송 실패 — 사진은 기기에 저장되었습니다", off면 "전송에 실패했습니다. 로컬 저장을 켜면 기기에 보관됩니다"). 화면에 머물되 **[완료] 버튼으로 Done 진행 가능**(흐름 비차단). 기존 [재시도](`RetryCommand`)·[홈으로]·[완료](`DoneCommand`) 유지, 실패 상태에서 [완료] 노출.
  - `QrPopupView.xaml`: `IsUploading`(업로드중)·`UploadSucceeded`(QR 이미지+만료 안내)·`UploadFailed`(안내 문구 + [재시도] + [완료]) 3상태를 Visibility로 분기. QR 이미지는 성공 시에만(§10 유지).
  - `SettingsView.xaml`: StorageBucket 입력 라벨/힌트에 예시 "예: mcphoto-955fb.firebasestorage.app"(신규 규약 지정 안내). (U7 Step 5와 파일 충돌 주의 — 힌트만 최소 추가하거나 Step 5에서 함께.)
  - **로컬 보존 순서 확인**(변경 아님, 계약): `ResultViewModel.Next`가 QR 분기 이전에 로컬 저장(saveLocalCopy on) 수행함을 코드로 확인(회귀 방지, 순서 유지).
  - 테스트: `QrPopupUploadTests`(목 `IUploadService`가 예외) → `UploadFailed==true`·`UploadSucceeded==false`·QR 이미지 null, [완료] 시 Done 네비. 성공 시 `UploadSucceeded==true`·QR 생성. `SettingsTests`: StorageBucket 저장→로드 라운드트립(신규 규약 문자열 보존).
- **검증 명령**: `dotnet test --filter QrPopupUploadTests` + `dotnet test --filter SettingsTests` + `dotnet build -c Release`(error 0, warning 0). `grep`로 `QrPopupViewModel`에 `UploadFailed`, `QrPopupView.xaml`에 3상태 분기.
- **완료 기준**:
  - [관측] `QrPopupUploadTests`(실패 시 UploadFailed·QR 없음·[완료] Done 진행; 성공 시 QR 생성)·`SettingsTests`(StorageBucket 라운드트립) 통과. 빌드 통과. `QrPopupViewModel`에 `UploadFailed` 상태·비위협 문구, `QrPopupView`에 3상태 UI(grep).
  - [non-goal] QR **성공 경로**(업로드 성공→QR 노출)는 **변경하지 않는다**(§10 유지). 로컬 저장 순서(QR 이전)·`ResultViewModel` 분기 불변. 버킷 생성·Blaze 전환은 코드 범위 밖(외부 전제). off일 때는 애초에 Qr에 안 감(ResultVM 기존 분기 유지).
  - [trigger] 실패 안내는 업로드 예외 시. [완료]로 Done 진행은 사용자 클릭 시(실패해도 막히지 않음). QR 이미지는 업로드 성공 시에만.
  - [사용자 확인 필요] QR on + 버킷 없음 → 위협 팝업 대신 "기기에 저장됨" 안내 + [완료] 진행 + [재시도]. saveLocalCopy on이면 로컬 보존. (Blaze+버킷 후 QR 성공 — 외부 전제.) (design §11-1)
- **롤백**: 이 Step 커밋 revert(QrPopup 실패 상태·View·힌트·테스트 원복).
- [ ] 완료

---

## Step 2: B7 — 컷 선택/프리뷰 WYSIWYG 크롭

- **Context Brief**: 컷 선택 화면 썸네일이 슬롯과 다른 크기·크롭으로 보인다(B7). 캡처 스틸은 이미 슬롯 종횡비로 중앙 크롭돼 저장되나(VF-4), 썸네일 컨테이너가 4:3 고정(220×165)+UniformToFill이라 재크롭돼 슬롯과 달라진다(VF-5). 썸네일·프리뷰 컨테이너를 슬롯 종횡비로 맞추고 Uniform으로 표시해 WYSIWYG를 회복한다(설계 §3).
- **대상 파일**: `src/MCPhoto.App/ViewModels/CutSelectViewModel.cs`(`SlotAspectRatio` 노출), `src/MCPhoto.App/Views/CutSelectView.xaml`(썸네일 비율·Stretch), (선택)`src/MCPhoto.App/Converters/CommonConverters.cs`(비율→높이 컨버터).
- **선행 조건**: 없음.
- **구현 내용**:
  - `CutSelectViewModel`: `double SlotAspectRatio` 노출(= 대표 슬롯 `Frame.Slots[0].AspectRatio`, `OnEnterAsync`에서 세팅). 세로 3:4면 0.75, 가로 4:3이면 1.333, 1:1이면 1.0.
  - `CutSelectView.xaml`: 썸네일 `Border` 고정 `Width=220 Height=165` 제거 → **고정 폭(예 200) + 종횡비 유지 높이**(폭/aspect). `Image Stretch=UniformToFill`→**`Uniform`**(컨테이너=컷 비율이라 왜곡·잘림 없음). 비율 적용은 컨버터(폭·aspect→높이) 또는 `Viewbox`.
  - 선택: `AspectRatioToHeightConverter`(폭·ratio→height) 또는 항목 컨테이너에 `Grid`+비율. WrapPanel 항목 크기 일관 유지.
- **검증 명령**: `dotnet build -c Release`(error 0, warning 0). `dotnet test --filter CropCalculatorTests`(크롭 회귀 없음). `grep`로 `CutSelectViewModel`에 `SlotAspectRatio`, `CutSelectView.xaml`에 `Stretch="Uniform"` + 고정 220×165 제거.
- **완료 기준**:
  - [관측] 빌드·`CropCalculatorTests` 통과. `CutSelectViewModel`에 `SlotAspectRatio` 노출(grep). `CutSelectView.xaml`이 썸네일을 슬롯 비율 컨테이너 + `Stretch=Uniform`으로(grep: `UniformToFill` 제거, 220×165 고정 제거).
  - [non-goal] 캡처·합성 크롭 파이프라인(`CropCalculator`·`CaptureViewModel`)은 **변경하지 않는다**(이미 슬롯 비율). 컷 선택 로직(슬롯 수만큼 선택)은 불변.
  - [trigger] 썸네일 비율은 대표 슬롯 종횡비(진입 시 세팅)로. 표시는 Uniform.
  - [사용자 확인 필요] 썸네일이 슬롯과 동일 종횡비·모양, 라이브 프리뷰·합성과 일치, 4:3/3:4/1:1 반영(design §11-2).
- **롤백**: 이 Step 커밋 revert(VM·View·컨버터 원복).
- [ ] 완료

---

## Step 3: B8 — 촬영 종료 후 로그인 유지

- **Context Brief**: 촬영 세션 종료 후 자동 로그아웃되는데, 사용자가 로그인 유지로 확정(B8). it3에서 세션 완료(Done→Home)를 `clearUser:true`(다음 손님 로그아웃)로 했으나, 이를 `clearUser:false`(로그인 보존)로 바꾼다. 로그아웃은 계정 메뉴 수동 또는 유휴 타임아웃만. 세션 촬영 데이터 초기화는 유지(설계 §4).
- **대상 파일**: `src/MCPhoto.App/ViewModels/DoneViewModel.cs`(clearUser true→false 2곳), `tests/MCPhoto.Tests/`(세션 Reset 정책 테스트 — 기존 `SessionServiceTests`/`AppStateTests` 확장 가능하면).
- **선행 조건**: 없음.
- **구현 내용**:
  - `DoneViewModel`: 자동 복귀(`:21`)·`GoHome`(`:35`)의 `ReturnHome(..., clearUser: true)` → `clearUser: false`. 주석 갱신(it5 §4: 촬영 후 로그인 유지, PRD 원안 갱신).
  - 유휴 타임아웃(`AppShellViewModel.cs:192`) `clearUser: true` **유지**(무인 보호). 사용자 취소(`GoHome` 커맨드)는 기본 false(이미 보존).
  - 세션 데이터: `SessionContext.Reset(clearUser)`가 촬영 데이터(프레임·컷·결과)는 항상 폐기(it3 §2.2 그대로), `clearUser:false`면 CurrentUser만 보존.
  - 테스트: `SessionContext.Reset(false)` 후 `CurrentUser` 유지 + `SelectedFrame`/`Capture.Cuts` 비워짐; `Reset(true)` 후 `CurrentUser` null. (기존 SessionServiceTests에 케이스 있으면 확인·보강.)
- **검증 명령**: `dotnet test`(세션 Reset 정책 + 회귀) + `dotnet build -c Release`(error 0, warning 0). `grep`로 `DoneViewModel`에 `clearUser: true` 잔존 0(false로 변경), `AppShellViewModel` 유휴는 `clearUser: true` 유지.
- **완료 기준**:
  - [관측] 빌드·테스트 통과. `DoneViewModel`의 `ReturnHome` 호출이 `clearUser: false`(grep: DoneViewModel에 `clearUser: true` 없음). 유휴 타임아웃은 여전히 `clearUser: true`(grep). `Reset(false)` 시 로그인 보존·촬영 데이터 폐기 테스트 통과.
  - [non-goal] 유휴 타임아웃 로그아웃은 **유지**(무인 보호). 세션 촬영 데이터 초기화는 **유지**(clearUser만 false). 사용자 취소 경로 불변.
  - [trigger] 로그아웃은 (a)계정 메뉴 수동, (b)유휴 타임아웃만. 세션 완료(Done)로는 로그아웃 안 됨.
  - [사용자 확인 필요] 로그인→촬영 완료→홈 시 로그인 유지, 유휴 만료 시 로그아웃(design §11-3).
- **롤백**: 이 Step 커밋 revert(DoneViewModel clearUser 원복).
- [ ] 완료

---

## Step 4: C1+C2 — 계정/설정 구조 개편 (설정에서 계정 제거 + 계정 전용 페이지)

- **Context Brief**: 설정 페이지에 계정 기능(비번 변경)·관리자 기능(계정 생성·사용자 관리·앱 종료)이 섞여 있다(VF-8). C1: 설정 페이지를 앱 설정(AppSettings)만으로 축소. C2: 계정 기능을 좌상단 계정 버튼 메뉴의 항목별 전용 페이지로 이전. 단일 `AppState.Account`+진입 모드로 상태 폭증을 막는다(설계 §5).
- **대상 파일**: `src/MCPhoto.Core/Navigation/AppState.cs`(`Account` 신규), `SessionStateMachine.cs`(오버레이 특례에 Account), `src/MCPhoto.App/ViewModels/AccountViewModel.cs`·`Views/AccountView.xaml`(+`.cs`)(신규), `src/MCPhoto.App/ViewModels/SettingsViewModel.cs`(계정/관리자 로직 제거), `src/MCPhoto.App/Views/SettingsView.xaml`(계정/관리자 섹션 제거), `src/MCPhoto.App/AppShellViewModel.cs`(계정 페이지 네비+모드, 팝오버 커맨드), `src/MCPhoto.App/MainWindow.xaml`(팝오버 항목→계정 페이지), `src/MCPhoto.App/ServiceRegistration.cs`(AccountViewModel 등록), `App.xaml`(DataTemplate), `tests/MCPhoto.Tests/AppStateTests.cs`(Account 전이).
- **선행 조건**: 없음. (Step 5가 이 결과 위에서 SettingsView 재배치.)
- **구현 내용**:
  - `AppState.Account` 추가. `SessionStateMachine.CanTransition` 오버레이 특례에 `Account` 포함(Home/Settings/Login처럼 어디서든 진입) 또는 오버레이 네비로 처리. `IsSessionActive`·`IsTopBarVisible` 영향 확인(Account는 유휴 비대상, 상단바 표시).
  - `AccountViewModel`: 진입 모드(enum `AccountMode { PasswordChange, UserManagement, AccountCreate }`) 수신. 비번 변경(2회 확인, actingRole 무관 자기 비번)·계정 생성(역할 규칙 actingRole 게이트 it2 §7)·사용자 관리(기존 UserMgmt 재사용 or 통합). `SettingsViewModel`에서 관련 로직·필드 이전.
  - `AccountView.xaml`: 모드별 UI 표시(DataTrigger 또는 서브 ContentControl). 라이트 토큰. 비번 2개 PasswordBox(code-behind 전달, 기존 패턴).
  - `SettingsViewModel`·`SettingsView`: [계정]·[관리자] 섹션·커맨드·필드 **제거**. 앱설정만. **앱 종료** 버튼은 AccountView(관리 모드)로 이전 또는 설정 하단 유지 — 설계 §5.1 결정=관리 페이지 이전.
  - `AppShellViewModel`: `NavigateToAccountAsync(AccountMode mode)`(오버레이 진입+모드 저장) 또는 `AccountEntryMode` 필드. `OpenAccountSettingsCommand`(팝오버 비번변경) → `NavigateToAccountAsync(PasswordChange)`. 관리자 항목 → `NavigateToAccountAsync(AccountCreate/UserManagement)`. `Logout`은 현행(즉시).
  - `MainWindow.xaml` 팝오버: [비밀번호 변경]→계정(PasswordChange), [사용자 관리]/[계정 생성](power)→계정(해당 모드), [로그아웃]→Logout. Visibility(로그인/power) 유지.
  - `ServiceRegistration`: `AddTransient<AccountViewModel>()`. `App.xaml`: `AccountViewModel`→`AccountView` DataTemplate.
  - 테스트(`AppStateTests`): `CanTransition(*, Account)` 특례 확인, `IsSessionActive(Account)==false`, `IsTopBarVisible(Account)==true`. 회귀(기존 전이) 없음.
- **검증 명령**: `dotnet test --filter AppStateTests` + `dotnet build -c Release`(error 0, warning 0). `grep`로 `SettingsView.xaml`에 계정/관리자 섹션 없음(비번변경·계정생성 바인딩 제거), `AccountView`/`AccountViewModel` 존재, 팝오버가 계정 페이지행.
- **완료 기준**:
  - [관측] `AppStateTests` 통과(Account 전이 특례·유휴 비대상·상단바 표시). 빌드 통과. `SettingsView`/`SettingsViewModel`에 계정·관리자 로직 없음(grep: ChangePassword·CreateAccount·CreatableRoles 제거). `AccountViewModel`/`AccountView`에 이전됨. 팝오버 항목이 계정 페이지(모드)로 네비(grep).
  - [non-goal] 계정 기능 **자체는 사라지지 않는다**(설정→계정 페이지로 이전). `IAccountService`·역할 게이트(it2 §7) 로직 불변. AppSettings 편집(설정)은 그대로.
  - [trigger] 계정 페이지 진입은 팝오버 항목 클릭 시(모드별). 설정 페이지는 앱설정만.
  - [사용자 확인 필요] 설정에 계정 섹션 없음, 계정 메뉴 항목별 전용 페이지, 역할 규칙(design §11-4).
- **롤백**: 이 Step 커밋 revert(AppState·AccountVM/View·SettingsVM/View·팝오버·DI·테스트 원복).
- [ ] 완료

---

## Step 5: U7 — 설정 레이아웃 재수정 (라벨 잘림 해결 + PC 밀도)

- **Context Brief**: 설정 화면 라벨이 잘린다("컷당 카ᶕ", "카메라 ᶕ" 등, 스크린샷). `RowLabel Width=240` 고정+카드 MaxWidth720이라 긴 라벨이 값 컨트롤에 눌려 잘림(VF-10). 라벨 잘림을 해결하고 PC 데스크톱 밀도로 정돈한다(설계 §6). Step 4로 계정 섹션이 제거된 SettingsView 위에서 앱설정만 재배치.
- **대상 파일**: `src/MCPhoto.App/Views/SettingsView.xaml`.
- **선행 조건**: Step 4(계정 섹션 제거된 SettingsView).
- **구현 내용**:
  - **라벨 잘림 해결**: `RowLabel` 고정 `Width=240` 제거 → 2열 `Grid`(`ColumnDefinitions Auto,*`: 라벨 Auto=내용 폭, 컨트롤 `*`) 또는 라벨폭 넉넉히(280+). 라벨 `TextTrimming` 없음·`TextWrapping` 필요 시 Wrap. 라벨이 안 잘리게.
  - **PC 밀도(it4 §5·it5 §6)**: 카드 `MaxWidth` 720→**960~1040**, 항목 **2열 그리드**(짧은 항목 좌/우), 조밀 행(`Space.S`~`Space.M`), 컨트롤 높이 36·토글 44, **최소 히트 40 유지**. 좁은 폭 1열 폴백(반응형).
  - 그룹 소제목·구분선 유지. 라이트 토큰만(하드코딩 색 금지). it3 저장 성공/실패 토스트(`BoolToNoticeBrush`) 유지.
- **검증 명령**: `dotnet build -c Release`(error 0, warning 0). `grep`로 `SettingsView.xaml`에 `RowLabel Width="240"` 제거, 2열 그리드(`ColumnDefinition`), 하드코딩 색(`#`) 0, 앱설정 전 항목 바인딩 유지(CutCount·MirrorMode·OutputFormat·CameraDevice·DisplayMode·StorageBucket 등).
- **완료 기준**:
  - [관측] 빌드 통과. `SettingsView.xaml`이 라벨 고정폭 240 제거 + 2열 그리드 + 색 토큰 참조(grep: `#RRGGBB` 0). 앱설정 전 항목 바인딩 유지(누락 0). 계정/관리자 섹션 없음(Step 4).
  - [non-goal] 설정 **VM·바인딩·저장 로직은 변경하지 않는다**(레이아웃만). 앱설정 항목 누락 없음. it3 토스트 색 분기 유지.
  - [trigger] 저장은 [저장] 버튼만. 2열→1열은 좁은 폭에서만.
  - [사용자 확인 필요] 라벨 안 잘림, PC 밀도·정렬, 터치 가능(design §11-5).
- **롤백**: 이 Step 커밋 revert(SettingsView 원복).
- [ ] 완료

---

## Step 6: U8 — 로그인 페이지 아이디 자동 포커스

- **Context Brief**: 로그인 페이지 진입 시 포커스가 아이디 입력창에 안 잡힌다(VF-11, U8). 진입 즉시 아이디 TextBox에 포커스를 준다. MVVM 유지(View 책임, 로직 없음)(설계 §7).
- **대상 파일**: `src/MCPhoto.App/Views/LoginGuestView.xaml`(FocusManager 또는 x:Name), `src/MCPhoto.App/Views/LoginGuestView.xaml.cs`(Loaded 포커스, 필요 시).
- **선행 조건**: 없음.
- **구현 내용**:
  - 아이디 `TextBox`에 `x:Name="IdTextBox"`. **`FocusManager.FocusedElement`**를 루트에 선언적 지정(`FocusManager.FocusedElement="{Binding ElementName=IdTextBox}"`) 우선.
  - 오버레이 재진입(DataTemplate 스왑)에 선언적 포커스가 안 잡히면 `Loaded`에서 `Dispatcher.BeginInvoke(() => { IdTextBox.Focus(); Keyboard.Focus(IdTextBox); })` 보강. code-behind는 포커스 지정만.
- **검증 명령**: `dotnet build -c Release`(error 0, warning 0). `grep`로 `LoginGuestView.xaml`에 `FocusManager.FocusedElement` 또는 `.xaml.cs`에 `Loaded`+`Focus`.
- **완료 기준**:
  - [관측] 빌드 통과. `LoginGuestView`에 아이디 포커스 지정(grep: `FocusManager.FocusedElement` 또는 `IdTextBox.Focus`). VM 로직 변경 없음.
  - [non-goal] 로그인 **로직은 VM 유지**(포커스만 View). 다른 화면 포커스 영향 없음. PasswordBox Enter(it3 U3) 동작 유지.
  - [trigger] 포커스는 로그인 페이지 진입(Loaded/표시) 시.
  - [사용자 확인 필요] 로그인 진입 즉시 아이디 입력창 커서(design §11-6).
- **롤백**: 이 Step 커밋 revert(포커스 지정 제거).
- [ ] 완료

---

## Step 7: F1 — 프레임 편집기 슬롯 크기 슬라이더 (70~130% 일괄 스케일)

- **Context Brief**: 프레임 편집기에 슬롯 크기 조절이 없다(VF-12, F1). 70~130%(기본 100%) 슬라이더 + % 표시로 **배치된 모든 슬롯을 동일 배율로 일괄 조정**한다. 각 슬롯 중심 유지, 종횡비 유지(it4 B4), 경계 클램프(설계 §8). 스케일은 순수 함수로 단위 테스트한다.
- **대상 파일**: `src/MCPhoto.Core/Frames/SlotLayout.cs`(`ScaleSlots` 순수함수), `src/MCPhoto.App/ViewModels/FrameEditorViewModel.cs`(`SlotScalePercent`·`_baseSlots`·스케일 적용), `src/MCPhoto.App/Views/FrameEditorView.xaml`(Slider+% 표시), `tests/MCPhoto.Tests/SlotLayoutTests.cs`(확장).
- **선행 조건**: 없음. (it4 SlotAspect/EditorTransform과 공존.)
- **구현 내용**:
  - `SlotLayout.ScaleSlots(IReadOnlyList<Slot> baseSlots, double factor, int frameW, int frameH)` 순수함수: 각 슬롯 `newW=round(w*factor)`·`newH=round(h*factor)`, **중심 유지**(`newX=cx-newW/2`, `cx=x+w/2`), `ClampToFrame`. 새 리스트 반환. 종횡비는 w·h 동일 배율이라 자동 유지.
  - `FrameEditorViewModel`: `[ObservableProperty] double _slotScalePercent = 100`(범위 70~130 클램프) + `_baseSlots`(자동 배치 원본 보관). `OnSlotScalePercentChanged` → `Slots` = `ScaleSlots(_baseSlots, SlotScalePercent/100, FrameWidth, FrameHeight)` → `UpdateCanSave`. `ArrangeSlots`(개수·종횡비 변경)가 `_baseSlots` 재설정 + 현재 factor 재적용. 드래그 종료 시 해당 슬롯의 `_baseSlots` 위치 갱신(스케일 기준 일치).
  - `FrameEditorView.xaml`: 컨트롤 패널에 `Slider`(Minimum 70, Maximum 130, Value `{Binding SlotScalePercent}`) + `TextBlock`(`{Binding SlotScalePercent}%` 또는 StringFormat). 라이트 토큰. (Slider 스타일은 기본 or Controls에 추가 — 선택.)
  - 스케일 후 겹침 가능 → `IsValid` 게이트(기존)로 저장 차단·안내.
  - 테스트(`SlotLayoutTests` 확장): `ScaleSlots(slots, 1.3, W, H)` → 크기 1.3배(±1px)·중심 유지(±1px)·경계 내. `0.7`→0.7배. 경계 초과 시 클램프. 종횡비 유지(w/h 비율 불변).
- **검증 명령**: `dotnet test --filter SlotLayoutTests`(ScaleSlots 케이스) + `dotnet build -c Release`(error 0, warning 0). `grep`로 `SlotLayout`에 `ScaleSlots`, `FrameEditorViewModel`에 `SlotScalePercent`·`_baseSlots`, `FrameEditorView.xaml`에 Slider+% 표시.
- **완료 기준**:
  - [관측] `SlotLayoutTests` 통과(스케일 배율·중심 유지·클램프·종횡비 유지). 빌드 통과. `SlotScalePercent`(70~130)·`ScaleSlots`·Slider+% UI 존재(grep).
  - [non-goal] 개별 드래그 이동(it4 절대 위치)은 **변경하지 않는다**(스케일은 크기만 일괄). 종횡비 선택(it4 B4)·EditorTransform 불변. 스케일이 프레임 경계를 넘지 않음(클램프).
  - [trigger] 일괄 스케일은 슬라이더 값 변경 시. `_baseSlots` 기준(누적 오차 방지). 재배치는 개수·종횡비 변경 시.
  - [사용자 확인 필요] 슬라이더 70~130%·% 표시, 전 슬롯 동일 크기·비율 유지·경계 안 넘음(design §11-7).
- **롤백**: 이 Step 커밋 revert(ScaleSlots·VM·View·테스트 원복).
- [ ] 완료

---

## 완결성 게이트 (자체 검사)

- [x] 검증된 사실(VF-1~14) / 미검증 가정(OA-1~5) 분리됨
- [x] 모든 가정에 검증 Step 매핑됨 (OA-1→1, OA-2→2, OA-3→3, OA-4→4, OA-5→7)
- [x] 모든 Step(1~7)에 7개 필수 필드
- [x] 모든 완료 기준이 관측 기반 3문 형식(관측·non-goal·trigger). UI Step은 "사용자 확인 필요" 포함
- [x] 검증 명령이 자동 실행 가능(`dotnet build -c Release`/`dotnet test --filter`/`grep`) — **앱 실행 없음**
- [x] 검증 로직(QR 실패 처리·슬롯 스케일·설정 라운드트립·계정 전이) 단위 테스트화(`QrPopupUploadTests`·`SlotLayoutTests`·`SettingsTests`·`AppStateTests`)
- [x] UI 육안은 각 Step "사용자 확인 필요" + `wpf-it5-design.md` §11에 집약

## 진행 상태 어휘 (developer 보고 시)

`inspected` / `changed locally` / `verified locally`(build+test 통과) / `committed` / `pushed` / `blocked`(사유 명시 필수)
