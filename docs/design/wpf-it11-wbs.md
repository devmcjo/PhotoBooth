# it11 · WBS (대기 기능 #13~#16)

> 상세 설계: [wpf-it11-deferred-features-design.md](./wpf-it11-deferred-features-design.md)
> 각 Step은 **self-contained** — fresh 에이전트가 그 Step만 읽고 실행 가능.
> 검증 명령 공통: `dotnet build src/MCPhoto.App/MCPhoto.App.csproj -c Debug`(0 warning/0 error), `dotnet test tests/MCPhoto.Tests/MCPhoto.Tests.csproj`.
> 편의상 프로젝트에 `build-verify` 스킬이 있으면 그것으로 대체 가능.

---

## 검증된 사실 (verified facts)

- `CutSelectViewModel.Retake`(RelayCommand)가 이미 존재: `CaptureSession.ResetForRetake()` → `NavigateAsync(Guide)`. (`CutSelectViewModel.cs:79-85`)
- `SessionStateMachine.Forward[CutSelect] = { Result, Guide }` — CutSelect→Guide 합법, **CutSelect→Capture 불법**. (`SessionStateMachine.cs:19`)
- `AppSettings`: `Clamp()`/`Clone()`/`ClosestFrom` + `static readonly int[] AllowedCutCounts` 관례. INI 매핑은 `IniSettingsService.ReadInto/WriteFrom`에 `nameof` 키 1:1. (`AppSettings.cs`, `IniSettingsService.cs:129-183`)
- `SettingsViewModel`: AppSettings 전 항목 `[ObservableProperty]` 미러 + `LoadSettings/SaveSettings`, 게스트 게이트 `IsLoggedIn`/`IsGuest`. 옵션 배열은 `IReadOnlyList<int> XxxOptions { get; } = AppSettings.AllowedXxx`. (`SettingsViewModel.cs`)
- `SettingsView.xaml`: `SettingRow`/`RowLabel`/`GroupTitle`/`GroupDivider`/`Toggle`/`Button.Secondary` 스타일, 2열 그리드, [고급] 그룹에 "서버 연결" 상태 행. (`SettingsView.xaml`)
- `OpenCvCameraService.EnumerateDevices()`: 인덱스 0~7 프로빙 → `$"Camera {i}"`. `CameraDevice(int Index, string Name)` record. DI Singleton. (`OpenCvCameraService.cs:308-329`, `ServiceRegistration.cs:43`)
- `FfmpegRunner.IsAvailable`/`FfmpegPath` public, DI Singleton. `IFirebaseClient.IsInitialized`/`Bucket`, `FirebaseClient.KeyCandidatePaths()` static. (`FfmpegRunner.cs`, `IFirebaseClient.cs`, `FirebaseClient.cs:103-111`, `ServiceRegistration.cs:53,62-69`)
- 로그 경로 = `{App.DataFolder}\logs`, `App.DataFolder`=`%CommonApplicationData%\MCPhoto`. (`App.xaml.cs:18-19,31`)
- `IUploadService.UploadResultAsync(photoPath, timelapsePath, retentionHours, hostingBaseUrl, ct)` → `IFirebaseClient.UploadFileAsync(..., ct)` → `StorageClient.UploadObjectAsync(obj, stream, ...)`. **IProgress 없음**. `QrPopupViewModel.Retry`=`OnEnterAsync` 재호출. (`UploadService.cs`, `FirebaseClient.cs:137-155`, `QrPopupViewModel.cs`)
- 다이얼로그 서비스 관례: `ICameraTestDialogService`/`CameraTestDialogService`, `IServiceProvider`로 VM 해결 + `ShowDialog`, DI Singleton. (`ServiceRegistration.cs:35`)
- 테스트: xUnit, `EmptyServiceProvider`+Fake, 임시 INI 경로, 순수 로직 직접 테스트. (`SettingsViewModelTests.cs`, `QrPopupUploadTests.cs`, `AppStateTests.cs`)

## 미검증 가정 (open assumptions)

- **A1**: GCS `Google.Cloud.Storage.V1`가 `UploadObjectAsync(..., IProgress<IUploadProgress>)` 진행률을 지원한다 → **검증 단계: Step 12**(미지원 시 stage 진행률로 폴백).
- **A2**: WMI `Win32_PnPEntity(PNPClass='Camera'/'Image')` 열거 순서와 OpenCV 인덱스 순서가 대체로 일치한다(best-effort) → **검증 단계: Step 9**(순수 매핑 테스트) + 사용자 다대 육안 검증(코드 밖).
- **A3**: `explorer.exe`로 로그 폴더 열기가 개발/테스트 환경에서 예외 없이 동작한다 → **검증 단계: Step 6**(예외 미발생 스모크, 키오스크 정책은 범위 밖).
- **A4**: 컷별 재촬영 시 `ReplaceCut(index)` 버퍼 교체가 CutSelect 재진입 표시와 호환된다 → **검증 단계: Step 3**(세션 단위 테스트) + Step 5(VM 선택 복원).

---

## 구현 순서 (권장)

**독립 트랙 4개** — #15, #16, #14는 서로 독립. #13은 여러 계층에 걸쳐 가장 큼.

1. **#15 (Step 8~9)** — 가장 격리됨(Capture 계층만, 인터페이스 무변경). 먼저 착수해 리스크 조기 해소.
2. **#16 (Step 10~12)** — Core/Firebase/App 세로 관통이나 파일 적음. A1을 Step 12에서 조기 확인.
3. **#14 (Step 6~7)** — 신규 파일 위주, 기존 로직 저침습.
4. **#13 (Step 1~5)** — 설정→세션→플로우 다계층. Step 4(컷별 UI)는 **USER-DECISION 승인 후**에만.

> **병렬 실행 시 주의**: #13 Step 2와 #14 Step 7이 **동일 파일 `SettingsViewModel.cs`·`SettingsView.xaml`을 수정**한다. 순차 처리하거나 한 에이전트가 두 Step을 연속 담당할 것(머지 충돌 방지).

---

## #13 재촬영

### Step 1: AppSettings 재촬영 필드 + INI 매핑
- **Context Brief**: 포토부스 앱의 로컬 설정은 `AppSettings`(POCO) + INI 파일(`IniSettingsService`)로 저장된다. 재촬영 기능(전체/컷별)을 위해 설정 3필드를 추가한다. 신규 INI 키는 반드시 `AppSettings` 필드 + `Clamp()` + `Clone()` + `ReadInto/WriteFrom` 4곳에 동시 반영해야 한다(한 곳 누락 시 저장/복원/편집취소 중 하나가 깨짐).
- **대상 파일**: `src/MCPhoto.Core/Settings/AppSettings.cs`, `src/MCPhoto.Core/Settings/IniSettingsService.cs`
- **선행 조건**: 없음
- **구현 내용**: `AppSettings`에 `bool RetakeEnabled`(기본 false), `int RetakeLimit`(기본 1), `bool PerCutRetake`(기본 false), `static readonly int[] AllowedRetakeLimits = {1,2,3}` 추가. `Clamp()`에 `if (Array.IndexOf(AllowedRetakeLimits, RetakeLimit) < 0) RetakeLimit = ClosestFrom(RetakeLimit, AllowedRetakeLimits, 1);` 추가. `Clone()`에 3필드 복제. `IniSettingsService.ReadInto`/`WriteFrom`에 `nameof` 키 3개(`GetBool`/`GetInt`, `SetBool`/`SetInt`) 추가.
- **검증 명령**: `dotnet build src/MCPhoto.Core/MCPhoto.Core.csproj -c Debug` + `dotnet test tests/MCPhoto.Tests/MCPhoto.Tests.csproj --filter FullyQualifiedName~Settings`
- **완료 기준**:
  - [관측] `AppSettings` 기본 인스턴스에서 `RetakeEnabled=false, RetakeLimit=1, PerCutRetake=false`. `RetakeLimit=5` 후 `Clamp()` → 3, `=0` → 1. INI 저장→로드 왕복 후 3필드 값 보존(신규 테스트).
  - [non-goal] 기존 설정 필드(`CutCount` 등)의 기본값·Clamp·왕복 불변(기존 `SettingsTests` 통과 유지).
  - [trigger] 값 반영은 `Load()`/`Save()` 호출 시에만 — 필드 추가만으로 기존 INI 파일 파싱이 깨지지 않음(누락 키는 기본값 폴백).
- **롤백**: 이 커밋 revert(Step 2~5와 독립적으로 컴파일 가능).
- [ ] 완료

### Step 2: SettingsViewModel + SettingsView 재촬영 UI
- **Context Brief**: 설정 화면은 오버레이(`SettingsViewModel`↔`SettingsView.xaml`)로, AppSettings 전 항목을 `[ObservableProperty]`로 미러하고 `LoadSettings`/`SaveSettings`가 왕복한다. 재촬영 설정을 [앱 설정]의 "촬영" 그룹(좌열)에 계층형으로 추가한다: 상위 토글(재촬영 사용) → on일 때만 하위(횟수 콤보 1~3, 컷별 토글) 노출.
- **대상 파일**: `src/MCPhoto.App/ViewModels/SettingsViewModel.cs`, `src/MCPhoto.App/Views/SettingsView.xaml`
- **선행 조건**: Step 1(AppSettings 필드)
- **구현 내용**: VM에 `[ObservableProperty] bool _retakeEnabled; int _retakeLimit; bool _perCutRetake;` + `IReadOnlyList<int> RetakeLimitOptions { get; } = AppSettings.AllowedRetakeLimits;`. `LoadSettings`/`SaveSettings`에 3필드 왕복 추가. XAML: "촬영" 그룹(`LeftCol`) 하단에 "재촬영 사용" `Toggle`(`IsChecked={Binding RetakeEnabled}`), 그 아래 들여쓰기 `StackPanel`(`Visibility={Binding RetakeEnabled, Converter={StaticResource BoolToVis}}`)에 "↳ 재촬영 횟수 제한" `ComboBox`(`ItemsSource={Binding RetakeLimitOptions}` `SelectedItem={Binding RetakeLimit}`) + "↳ 컷별 재촬영" `Toggle`(`IsChecked={Binding PerCutRetake}`). 기존 QR 하위 토글 들여쓰기(Margin 20,0,0,0) 패턴 재사용.
- **검증 명령**: `dotnet build src/MCPhoto.App/MCPhoto.App.csproj -c Debug`(0 warning) + `dotnet test tests/MCPhoto.Tests/MCPhoto.Tests.csproj --filter FullyQualifiedName~SettingsViewModel`
- **완료 기준**:
  - [관측] 설정 저장 시 3필드가 INI에 기록되고 재로드 시 VM에 반영(신규/확장 테스트). `RetakeEnabled=true`로 저장→로드 후 `vm.RetakeEnabled=true`.
  - [non-goal] 기존 QR 하위 토글 연동(`_normalizing` 가드)·게스트 게이트 동작 불변(기존 `SettingsViewModelTests` 통과). 재촬영 필드는 게스트 게이트 대상 아님(촬영 옵션이므로 게스트도 저장 가능 — QR/Firebase만 게이트).
  - [trigger] 하위 옵션 노출은 `RetakeEnabled` 토글 on 시에만(off면 `Visibility=Collapsed`). 저장은 [저장] 버튼 클릭 시에만.
  - [MVVMTK0034 회피] `[ObservableProperty]` 백킹필드 직접참조 금지.
- **롤백**: 이 커밋 revert.
- [ ] 완료

### Step 3: CaptureSession 재촬영 카운터 + SessionContext.RetakeTargetCut
- **Context Brief**: `CaptureSession`(순수 로직, 테스트 대상)은 컷 버퍼·선택을 관리한다. 재촬영 동작 규칙(전체 재촬영 후 컷별 봉인, 각 컷 1회, 횟수 제한)을 위해 세션 단위 카운터를 추가한다. `SessionContext`(싱글턴 공유 상태)에는 컷별 재촬영 대상 인덱스를 전달할 필드를 추가한다.
- **대상 파일**: `src/MCPhoto.Core/Capture/CaptureSession.cs`, `src/MCPhoto.App/SessionContext.cs`
- **선행 조건**: 없음(Step 1과 독립)
- **구현 내용**: `CaptureSession`에 `private int _fullRetakeCount; private readonly HashSet<int> _perCutRetaken = new();` + 속성 `FullRetakeCount`, `HasFullRetaken`(=`_fullRetakeCount>0`), `WasCutRetaken(int)`, `CanFullRetake(int limit)`(=`_fullRetakeCount<limit`), `CanPerCutRetake(int cutIndex, int limit)`(=`!HasFullRetaken && _perCutRetaken.Count<limit && !_perCutRetaken.Contains(cutIndex)`), `BeginFullRetake()`(컷·선택·컷별이력 clear + `_fullRetakeCount++`), `MarkCutRetaken(int)`, `ReplaceCut(int cutIndex, CapturedStill)`(범위 밖 false). `Discard()`에 `_fullRetakeCount=0; _perCutRetaken.Clear();` 추가. 기존 `ResetForRetake()`는 유지(회귀 방지). `SessionContext`에 `int? RetakeTargetCut { get; set; }` + `Reset()`에 `RetakeTargetCut = null;`.
- **검증 명령**: `dotnet build src/MCPhoto.Core/MCPhoto.Core.csproj -c Debug` + `dotnet test tests/MCPhoto.Tests/MCPhoto.Tests.csproj --filter FullyQualifiedName~CaptureSession`
- **완료 기준**:
  - [관측] `BeginFullRetake()` 후 `FullRetakeCount=1`, `HasFullRetaken=true`, `CanPerCutRetake(0,3)=false`. `CanFullRetake(1)`은 0회 때 true, 1회 후 false. `MarkCutRetaken(2)` 후 `WasCutRetaken(2)=true`, `CanPerCutRetake(2,3)=false`. `ReplaceCut(99,...)`=false. `Discard()` 후 카운터 0.
  - [non-goal] 기존 `AddCut`/`ToggleSelection`/`GetSelectedCuts`/`ResetForRetake`/`IsSelectionComplete` 동작 불변(기존 `CaptureSessionTests` 통과).
  - [trigger] 카운터 증가는 `BeginFullRetake()`/`MarkCutRetaken()` 명시 호출 시에만.
- **롤백**: 이 커밋 revert.
- [ ] 완료

### Step 4: 상태머신 CutSelect→Capture 전이 + CutSelectViewModel 재촬영 커맨드
- **Context Brief**: 전체 재촬영은 CutSelect→Guide(이미 합법). 컷별 재촬영은 특정 컷만 재촬영하려 CutSelect→Capture 전이가 필요하나 전이표에 없다. 전이표에 추가하고, `CutSelectViewModel`에 재촬영 가능 여부 속성·커맨드를 배선한다. **컷별 재촬영 버튼의 물리적 UI 배치는 USER-DECISION(Step 5b)이므로 이 Step은 VM 로직·전이·전체 재촬영 버튼 상태까지만.**
- **대상 파일**: `src/MCPhoto.Core/Navigation/SessionStateMachine.cs`, `tests/MCPhoto.Tests/AppStateTests.cs`, `src/MCPhoto.App/ViewModels/CutSelectViewModel.cs`
- **선행 조건**: Step 1(설정), Step 3(세션 카운터·RetakeTargetCut)
- **구현 내용**: `Forward[AppState.CutSelect] = new[] { AppState.Result, AppState.Guide, AppState.Capture }`. `AppStateTests`에 `CutSelect→Capture 합법` + `Capture→Result 여전히 불법` 테스트 추가. `CutSelectViewModel`: `RetakeEnabled`(=`_shell.Settings.Current.RetakeEnabled`), `CanFullRetake`(=`RetakeEnabled && Capture.CanFullRetake(limit)`), `PerCutRetakeAvailable`(=`RetakeEnabled && settings.PerCutRetake && !Capture.HasFullRetaken`), `CanRetakeCut(int)`, `[RelayCommand] RetakeSingleCut(CutThumbnail?)`(→`session.RetakeTargetCut=idx; NavigateAsync(Capture)`). 기존 `Retake`를 `if(!CanFullRetake)return; Capture.BeginFullRetake(); NavigateAsync(Guide)`로 교체. `OnEnterAsync` 끝에 `OnPropertyChanged`로 재촬영 속성 통지.
- **검증 명령**: `dotnet test tests/MCPhoto.Tests/MCPhoto.Tests.csproj --filter "FullyQualifiedName~AppState|FullyQualifiedName~CutSelect"`
- **완료 기준**:
  - [관측] `CanTransition(CutSelect, Capture)=true`, `CanTransition(Capture, Result)=false`(기존 유지). `RetakeEnabled=false`면 `vm.CanFullRetake=false`; 전체 재촬영 세션(`HasFullRetaken`)에서 `PerCutRetakeAvailable=false`.
  - [non-goal] 기존 정상 흐름 전이(`Normal_Flow_Is_Legal`)·홈 복귀·오버레이 특례 불변(전체 `AppStateTests` 통과). 기존 "다시 촬영" 버튼(전체 재촬영)의 기존 동작(→Guide)은 카운터만 추가되고 유지.
  - [trigger] 전체 재촬영은 `RetakeCommand`(버튼) 실행 + `CanFullRetake` 참일 때만 카운터 증가·전이. 컷별은 `RetakeSingleCutCommand` + `CanRetakeCut` 참일 때만.
- **롤백**: 이 커밋 revert(전이표 원복 시 컷별 재촬영만 비활성, 전체 재촬영은 유지).
- [ ] 완료

### Step 5: CaptureViewModel 단일 컷 재촬영 플로우 + CutSelect 선택 복원
- **Context Brief**: `CaptureViewModel.OnEnterAsync`는 전 컷 순차 촬영만 한다. 컷별 재촬영을 위해 `SessionContext.RetakeTargetCut`이 설정돼 진입하면 해당 컷 1장만 재촬영하고 CutSelect로 복귀하는 경로를 추가한다. 복귀 후 기존 선택 표시가 유지되도록 `CutSelectViewModel.OnEnterAsync`에 선택 순서 복원을 추가한다.
- **대상 파일**: `src/MCPhoto.App/ViewModels/CaptureViewModel.cs`, `src/MCPhoto.App/ViewModels/CutSelectViewModel.cs`
- **선행 조건**: Step 3(ReplaceCut·RetakeTargetCut·MarkCutRetaken), Step 4(전이·커맨드)
- **구현 내용**: `CaptureViewModel.OnEnterAsync` 시작부에 `if (session.RetakeTargetCut is int idx) { await RunSingleCutRetakeAsync(idx); session.RetakeTargetCut = null; return; }` 분기. `RunSingleCutRetakeAsync(int cutIndex)`: 카메라 `StartAsync`+`WaitForStablePreviewAsync`(기존 재사용) → `TotalCuts=1; CurrentCut=1` → `CountdownAsync(settings.CountdownSec, ct)` → 플래시/셔터음(기존 로직) → `CaptureStillAsync` → `session.Capture.ReplaceCut(cutIndex, still)` → `session.Capture.MarkCutRetaken(cutIndex)` → `NavigateAsync(CutSelect)`. **녹화·타임랩스 재생성 없음**(원본 세션 녹화 유지). `OnLeaveAsync`에서 `StopAsync`(기존)로 카메라 정지. `CutSelectViewModel.OnEnterAsync`: `Cuts` 재빌드 후 `session.Capture.Selection` 기준 각 `CutThumbnail.SelectionOrder` 복원(선택 순서 = `Selection` 리스트 인덱스+1).
- **검증 명령**: `dotnet build src/MCPhoto.App/MCPhoto.App.csproj -c Debug`(0 warning) + `dotnet test tests/MCPhoto.Tests/MCPhoto.Tests.csproj`
- **완료 기준**:
  - [관측] `RetakeTargetCut=1` 설정 후 Capture 진입 → 단일 컷 시퀀스(1컷)만 실행, `ReplaceCut(1,...)`로 버퍼 교체, `MarkCutRetaken(1)` 호출, CutSelect 복귀. CutSelect 재진입 시 이전 선택이 `SelectionOrder`로 복원됨(VM 단위 테스트: 선택 2개 후 컷 교체·재진입 → 선택 유지). 카메라 정지는 `OnLeaveAsync`에서.
  - [non-goal] `RetakeTargetCut=null`이면 기존 전체 촬영 시퀀스 그대로(녹화·N컷·타임랩스 불변). 전체 재촬영(→Guide) 경로 무영향.
  - [trigger] 단일 컷 모드는 `RetakeTargetCut != null`일 때만. 진입 후 1회성 소비(`= null`)해 재진입 시 전체 모드로 복귀.
- **롤백**: 이 커밋 revert(Step 4 전이표는 유지되나 컷별 진입 시 전체 촬영으로 폴백 — 무해하나 revert 권장).
- [ ] 완료

### Step 5b: [USER-DECISION-REQUIRED] 컷별 재촬영 버튼 UI (CutSelectView)
- **Context Brief**: 컷별 재촬영을 사용자가 트리거할 버튼을 CutSelect 화면에 배치한다. **버튼의 물리적 위치·인터랙션은 제품 결정**(썸네일 우하단 ↺ 오버레이 vs 별도 "이 컷 다시 찍기" 모드). 오케스트레이터의 USER-DECISION 승인 전에는 착수 금지.
- **대상 파일**: `src/MCPhoto.App/Views/CutSelectView.xaml`
- **선행 조건**: Step 5, **USER-DECISION 승인**
- **구현 내용**(기본 제안, 승인 시): 각 썸네일 `DataTemplate` 우하단에 작은 ↺ `Button`(`Command={Binding DataContext.RetakeSingleCutCommand, RelativeSource=AncestorType=ListBox}` `CommandParameter={Binding}`, `Visibility`는 `DataContext.PerCutRetakeAvailable`와 항목별 `CanRetakeCut`을 반영 — 항목별 상태는 `CutThumbnail`에 `bool CanRetake` 추가하거나 VM에서 갱신). 전체 재촬영 "다시 촬영" 버튼에 `IsEnabled={Binding CanFullRetake}` + `Visibility={Binding RetakeEnabled, Converter={StaticResource BoolToVis}}` 적용.
- **검증 명령**: `dotnet build src/MCPhoto.App/MCPhoto.App.csproj -c Debug`(0 warning). 육안: 재촬영 on 세션에서 썸네일 ↺ 표시·클릭 시 해당 컷 재촬영.
- **완료 기준**:
  - [관측] 재촬영 설정 on + 컷별 on 세션에서 각 썸네일에 ↺ 버튼 노출, 클릭 시 해당 컷만 재촬영 후 복귀. 전체 재촬영 버튼은 `RetakeEnabled` off면 숨김, limit 도달 시 Disable.
  - [non-goal] 재촬영 off(기본)면 재촬영 UI 일절 미노출 — 기존 CutSelect 화면 외형 불변. 전체 재촬영 1회 세션에서 컷별 ↺ 숨김.
  - [trigger] 재촬영은 버튼 클릭 시에만 — 썸네일 선택(`ToggleCut`)과 재촬영(`RetakeSingleCut`)이 혼동되지 않도록 히트 영역 분리.
- **롤백**: 이 커밋 revert(VM/세션/전이는 유지, UI만 제거).
- [ ] 완료

---

## #14 진단/상태 화면

### Step 6: LogFolderService + DiagnosticsViewModel + DiagnosticsWindow
- **Context Brief**: 관리자 트러블슈팅용 진단 화면을 **모달 다이얼로그**로 만든다(AppState 미추가). 카메라 연결·ffmpeg 사용가능·Firebase 초기화 상태를 표시하고 로그 폴더 경로·열기를 제공한다. 로그 경로=`{App.DataFolder}\logs`. `FfmpegRunner`(DI Singleton)의 `IsAvailable`/`FfmpegPath`, `IFirebaseClient.IsInitialized`/`Bucket`, `FirebaseClient.KeyCandidatePaths()` static을 사용한다. VM은 UI 타입(Visibility/Brush) 미의존.
- **대상 파일**: `src/MCPhoto.App/Services/ILogFolderService.cs`(신규), `.../LogFolderService.cs`(신규), `src/MCPhoto.App/ViewModels/DiagnosticsViewModel.cs`(신규), `src/MCPhoto.App/Views/DiagnosticsWindow.xaml`(+`.xaml.cs`, 신규)
- **선행 조건**: 없음
- **구현 내용**: `ILogFolderService`(`string LogFolderPath { get; }`, `void OpenLogFolder()`) + `LogFolderService`(경로=`Path.Combine(App.DataFolder, "logs")`; `OpenLogFolder`=`Directory.CreateDirectory`+`Process.Start(explorer.exe, "\"{path}\"", UseShellExecute=true)`, try/catch로 크래시 금지). `DiagnosticsViewModel : ObservableObject` — 생성자 `(ICameraService camera, FfmpegRunner ffmpeg, IFirebaseClient firebase, ILogFolderService logFolder, ILogger? logger=null)`; 속성 `IsCheckingCamera`/`CameraCount`/`CameraSummary`/`ObservableCollection<CameraDevice> Cameras`, `FfmpegAvailable`/`FfmpegPath`, `FirebaseInitialized`/`FirebaseBucket`/`FirebaseKeyCandidates`, `LogFolderPath`; `[RelayCommand] RefreshCameras`(=`Task.Run(EnumerateDevices)` 후 컬렉션 채우기·`IsCheckingCamera` 토글), `[RelayCommand] OpenLogFolder`(=`_logFolder.OpenLogFolder()`), `[RelayCommand] Close`(창 닫기 — code-behind 이벤트 또는 `Window.DialogResult`). `DiagnosticsWindow`: 디자인 시스템 리소스(`Card`/`Text.*`/`Button.*`/기존 DataTrigger 색) 사용, 4섹션(§3.14.8) 표시.
- **검증 명령**: `dotnet build src/MCPhoto.App/MCPhoto.App.csproj -c Debug`(0 warning) + `dotnet test tests/MCPhoto.Tests/MCPhoto.Tests.csproj --filter FullyQualifiedName~Diagnostics`
- **완료 기준**:
  - [관측] `DiagnosticsViewModel` 단위 테스트(신규): Fake 카메라 2대 → `RefreshCameras` 후 `CameraCount=2`, `Cameras.Count=2`; FakeFirebaseClient `IsInitialized=true, Bucket="b"` → `FirebaseInitialized=true, FirebaseBucket="b"`; `LogFolderPath` = `{DataFolder}\logs`(Fake `ILogFolderService`로 주입). `OpenLogFolder` 호출 시 예외 미발생(A3 스모크).
  - [non-goal] 진단 VM은 카메라 라이브 프리뷰(`StartAsync`)를 켜지 않음 — 카메라 점유 없음(열거만). 기존 화면·상태머신·유휴 감시 무변경(AppState 미추가).
  - [trigger] 카메라 검사는 `RefreshCamerasCommand` 실행 시에만(진입 시 다이얼로그 서비스가 1회 호출 — Step 7).
- **롤백**: 신규 파일 삭제(등록 전이므로 다른 코드 무영향).
- [ ] 완료

### Step 7: 진단 다이얼로그 서비스 + DI 등록 + SettingsView 진입 버튼
- **Context Brief**: Step 6의 진단 화면을 `SettingsView`의 [고급] 그룹 하단 버튼으로 진입시킨다. VM이 Window를 직접 열지 않도록 `IDiagnosticsDialogService`(기존 `ICameraTestDialogService` 패턴)를 만들고 DI 등록 후 `SettingsViewModel`에 배선한다. roadmap: 로그인 상태에서만.
- **대상 파일**: `src/MCPhoto.App/Services/IDiagnosticsDialogService.cs`(+구현, 신규), `src/MCPhoto.App/ServiceRegistration.cs`, `src/MCPhoto.App/ViewModels/SettingsViewModel.cs`, `src/MCPhoto.App/Views/SettingsView.xaml`, `tests/MCPhoto.Tests/SettingsViewModelTests.cs`
- **선행 조건**: Step 6(`DiagnosticsViewModel`/`Window`/`ILogFolderService`)
- **구현 내용**: `IDiagnosticsDialogService`(`Task ShowAsync()`) + `DiagnosticsDialogService`(`IServiceProvider`로 `DiagnosticsViewModel` 해결 → `DiagnosticsWindow{DataContext=vm, Owner=MainWindow}` → 진입 시 `RefreshCamerasCommand.ExecuteAsync(null)` → `ShowDialog()`). `ServiceRegistration.Register`에 `AddSingleton<ILogFolderService, LogFolderService>()`, `AddSingleton<IDiagnosticsDialogService, DiagnosticsDialogService>()`, `AddTransient<DiagnosticsViewModel>()`. `SettingsViewModel` 생성자에 `IDiagnosticsDialogService diagnostics` 추가(**테스트 헬퍼 `MakeVm`도 Fake 추가**) + `[RelayCommand] OpenDiagnostics`(`if(!IsLoggedIn)return; try{await _diagnostics.ShowAsync();}catch{...}`). `SettingsView.xaml` [고급] 그룹 "서버 연결" 행 아래에 `Button Content="진단·상태"`(`Command={Binding OpenDiagnosticsCommand}` `IsEnabled={Binding IsLoggedIn}` `Style=Button.Secondary`).
- **검증 명령**: `dotnet build src/MCPhoto.App/MCPhoto.App.csproj -c Debug`(0 warning) + `dotnet test tests/MCPhoto.Tests/MCPhoto.Tests.csproj`
- **완료 기준**:
  - [관측] 로그인 사용자로 `OpenDiagnosticsCommand` 실행 시 Fake `IDiagnosticsDialogService.ShowAsync` 1회 호출(테스트). 게스트면 no-op(호출 0회).
  - [non-goal] 기존 `SettingsViewModelTests`(QR 연동·게스트 게이트·카메라 열거·서버 상태) 전부 통과 유지 — 생성자 변경으로 인한 헬퍼만 갱신, 로직 회귀 0. 진단 버튼이 설정 저장/닫기 흐름에 영향 없음.
  - [trigger] 진단 다이얼로그는 [진단·상태] 버튼 클릭 + 로그인 상태에서만 열림 — 게스트는 버튼 Disable.
- **롤백**: 이 커밋 revert(Step 6 파일은 미등록 상태로 잔존 — 무해).
- [ ] 완료

---

## #15 카메라 FriendlyName

### Step 8: System.Management 의존성 + CameraNameProbe(WMI + 순수 매핑)
- **Context Brief**: 현재 카메라 목록은 `"Camera {index}"`로만 표시돼 여러 대 구분이 안 된다. WMI(`Win32_PnPEntity`, `PNPClass='Camera'/'Image'`)로 FriendlyName을 best-effort 조회한다. **핵심 리스크**: WMI 열거 순서와 OpenCV 인덱스 순서가 일치한다는 보장이 없어(A2) 이름이 틀릴 수 있으므로, 이름은 표시용 best-effort이고 동작은 인덱스 기준 유지 + 실패 시 인덱스 라벨 폴백. WMI I/O와 순수 매핑 로직을 분리해 매핑만 단위 테스트한다.
- **대상 파일**: `src/MCPhoto.Capture/MCPhoto.Capture.csproj`, `src/MCPhoto.Capture/CameraNameProbe.cs`(신규)
- **선행 조건**: 없음
- **구현 내용**: `MCPhoto.Capture.csproj`에 `<PackageReference Include="System.Management" Version="8.0.0" />`(net8 호환 버전; 실제 복원 가능 버전 확인). `CameraNameProbe`(internal static): `TryGetImagingDeviceNames(ILogger?)` = `ManagementObjectSearcher("SELECT Name FROM Win32_PnPEntity WHERE PNPClass = 'Camera' OR PNPClass = 'Image'")` 순회해 `Name` 수집, 예외 시 `Array.Empty<string>()`. **순수 매핑 헬퍼** `ComposeDevices(IReadOnlyList<int> openIndices, IReadOnlyList<string> friendlyNames)`: openIndices 순서대로 friendlyNames를 순차 매핑(부족분 `"Camera {index}"` 폴백), 동일 이름 중복 시 `"{name} (#{index})"` 접미.
- **검증 명령**: `dotnet build src/MCPhoto.Capture/MCPhoto.Capture.csproj -c Debug`(복원+0 warning) + `dotnet test tests/MCPhoto.Tests/MCPhoto.Tests.csproj --filter FullyQualifiedName~CameraName`
- **완료 기준**:
  - [관측] `ComposeDevices([0,1], ["Logitech","Elgato"])` → `[(0,"Logitech"),(1,"Elgato")]`. 이름 부족 `ComposeDevices([0,1],["A"])` → `[(0,"A"),(1,"Camera 1")]`. 빈 이름 → 전부 `"Camera {i}"`. 중복 `ComposeDevices([0,1],["Cam","Cam"])` → `[(0,"Cam (#0)"),(1,"Cam (#1)")]`(신규 테스트). `System.Management` 복원 성공.
  - [non-goal] `TryGetImagingDeviceNames` 실패 경로가 예외를 던지지 않고 빈 목록 반환(폴백 안전). 매핑 헬퍼는 WMI/OpenCV I/O 미접촉(순수).
  - [trigger] WMI 조회는 `TryGetImagingDeviceNames` 호출 시에만 — 매핑 헬퍼는 입력만으로 결정적.
- **롤백**: 이 커밋 revert(csproj·신규 파일; Step 9 미적용이면 `EnumerateDevices`는 기존 동작 유지).
- [ ] 완료

### Step 9: OpenCvCameraService.EnumerateDevices가 FriendlyName 사용
- **Context Brief**: Step 8의 `CameraNameProbe`를 `EnumerateDevices()`에 연결한다. 인덱스 프로빙(동작 기준)은 그대로 두고, 열린 장치 순서대로 WMI 이름을 매핑한다. 인터페이스(`ICameraService.EnumerateDevices`)·`CameraDevice` record는 무변경 — `Name` 값만 개선된다.
- **대상 파일**: `src/MCPhoto.Capture/OpenCvCameraService.cs`
- **선행 조건**: Step 8(`CameraNameProbe.ComposeDevices`/`TryGetImagingDeviceNames`)
- **구현 내용**: `EnumerateDevices()`에서 (1) `var names = CameraNameProbe.TryGetImagingDeviceNames(_logger);` (2) 기존 인덱스 프로빙으로 열린 인덱스 목록(`openIndices`) 수집 (3) `return CameraNameProbe.ComposeDevices(openIndices, names);`. 기존 `cap.Release()`·예외 무시 로직 유지.
- **검증 명령**: `dotnet build src/MCPhoto.Capture/MCPhoto.Capture.csproj -c Debug`(0 warning) + `dotnet test tests/MCPhoto.Tests/MCPhoto.Tests.csproj`. 육안(사용자 액션 A2): 웹캠 2대 연결 → 설정 카메라 콤보에 실제 장치명 2개 표시.
- **완료 기준**:
  - [관측] 빌드/테스트 통과. `EnumerateDevices` 반환 `CameraDevice.Name`이 WMI 성공 시 실제 장치명(육안). 장치 선택(인덱스) 동작 불변.
  - [non-goal] `ICameraService` 시그니처·`CameraDevice` record 불변 → `SettingsViewModelTests.FakeCameraService`·`SettingsView`(`DisplayMemberPath="Name"`) 무영향. WMI 실패 시 `"Camera {i}"` 폴백(현행 동작, 회귀 0).
  - [trigger] 이름 조회는 `EnumerateDevices()`(설정/진단 진입 시 `RefreshCameras`) 호출 시에만.
- **롤백**: 이 커밋 revert(`EnumerateDevices`를 기존 인덱스 라벨 버전으로 원복).
- [ ] 완료

---

## #16 업로드 진행률/재시도

### Step 10: UploadProgress 모델 + IUploadService/IFirebaseClient 진행률 시그니처
- **Context Brief**: QR 업로드(특히 대용량 타임랩스)에 진행률을 표시하려 `IProgress<T>`를 배선한다(UploadService→QrPopupViewModel). Core에 UI 무의존 진행 모델을 만들고, 인터페이스에 **선택 파라미터**(하위호환)로 IProgress를 추가한다. `IFirebaseClient`의 파일 단위 진행률은 GCS SDK 지원 여부(A1)에 의존하므로 Step 12에서 확인 — 이 Step은 시그니처만.
- **대상 파일**: `src/MCPhoto.Core/Upload/UploadProgress.cs`(신규), `src/MCPhoto.Core/Upload/IUploadService.cs`, `src/MCPhoto.Core/Upload/IFirebaseClient.cs`, 목 구현: `tests/MCPhoto.Tests/QrPopupUploadTests.cs`(StubUploadService), `tests/MCPhoto.Tests/UploadServiceTests.cs`·`FakeFirebaseClient.cs`
- **선행 조건**: 없음
- **구현 내용**: `public sealed record UploadProgress(UploadStage Stage, double Fraction, string? Label = null);` + `public enum UploadStage { Photo, Timelapse, Finalizing }`. `IUploadService.UploadResultAsync`에 `IProgress<UploadProgress>? progress = null`을 `ct` **앞**에 추가. `IFirebaseClient.UploadFileAsync`에 `IProgress<double>? fileProgress = null`을 `ct` 앞에 추가. 모든 구현·목(`StubUploadService`, `FakeFirebaseClient`, `UploadService`, `FirebaseClient`)의 시그니처 갱신(본문은 무시/기존 유지 — 배선은 Step 11~12).
- **검증 명령**: `dotnet build MCPhoto.sln -c Debug`(전체 솔루션 0 warning) + `dotnet test tests/MCPhoto.Tests/MCPhoto.Tests.csproj`
- **완료 기준**:
  - [관측] 전체 솔루션 컴파일 통과. 기존 4인자 위치 호출(`UploadResultAsync(a,b,c,d)`)·`StubUploadService`가 새 시그니처로 컴파일. 기존 `QrPopupUploadTests`/`UploadServiceTests` 전부 통과(동작 불변).
  - [non-goal] 업로드 동작·URL·문서 생성 로직 불변(진행률 파라미터 null 전달 시 기존과 동일 경로). 진행률 아직 표시 안 함.
  - [trigger] 진행률 보고는 이후 Step에서 배선 — 이 Step은 시그니처만.
- **롤백**: 이 커밋 revert(신규 record 삭제 + 시그니처 원복).
- [ ] 완료

### Step 11: UploadService stage 진행 보고 + QrPopupViewModel 진행률 표시
- **Context Brief**: Step 10 시그니처로 `UploadService`가 단계 진행률을 발행하고, `QrPopupViewModel`이 `Progress<T>`로 받아 진행 바를 표시한다. `Progress<T>`는 생성 스레드의 SynchronizationContext로 콜백을 마샬링하므로 UI 스레드에서 실행되는 `OnEnterAsync`에서 생성해야 안전하다(백그라운드 생성 금지).
- **대상 파일**: `src/MCPhoto.Firebase/UploadService.cs`, `src/MCPhoto.App/ViewModels/QrPopupViewModel.cs`, `src/MCPhoto.App/Views/QrPopupView.xaml`, `tests/MCPhoto.Tests/QrPopupUploadTests.cs`
- **선행 조건**: Step 10
- **구현 내용**: `UploadService.UploadResultAsync`: 사진 업로드 전후 `progress?.Report(new UploadProgress(UploadStage.Photo, 0.0/1.0))`, 타임랩스 동일(`Timelapse`), 문서 생성 전 `Finalizing`. `QrPopupViewModel`: `[ObservableProperty] double _uploadProgress; string _progressLabel; bool _isIndeterminate = true;` + **순수 static** `ComputeOverall(UploadStage stage, double frac, bool hasPhoto, bool hasTimelapse)`(전송 미디어 기준 정규화) + `OnUploadProgress(UploadProgress)`(=`IsIndeterminate=false; UploadProgress=ComputeOverall(...); ProgressLabel=p.Label ?? StageLabel(p.Stage)`). `OnEnterAsync`에서 `var progress = new Progress<UploadProgress>(OnUploadProgress);`(UI 스레드) → `UploadResultAsync(..., progress)`. 진입 시 `UploadProgress=0; IsIndeterminate=true`. `QrPopupView.xaml` "①업로드 중" 블록에 `ProgressBar`(Min 0/Max 1/`Value={Binding UploadProgress}`/`IsIndeterminate={Binding IsIndeterminate}`, Foreground=`Brush.Accent`) + `ProgressLabel` TextBlock.
- **검증 명령**: `dotnet build src/MCPhoto.App/MCPhoto.App.csproj -c Debug`(0 warning) + `dotnet test tests/MCPhoto.Tests/MCPhoto.Tests.csproj --filter FullyQualifiedName~QrPopup`
- **완료 기준**:
  - [관측] `ComputeOverall` 순수 테스트: 둘 다 전송 시 Photo 1.0→전체 0.5, 사진만이면 Photo 1.0→전체 1.0, 경계 0/1(신규 테스트). `StubUploadService`가 `progress.Report` 몇 회 호출하도록 개선 → `OnEnterAsync` 후 `vm.UploadProgress>0`, `vm.IsIndeterminate=false`. 성공 시 QR 생성(기존 테스트 유지).
  - [non-goal] 업로드 실패 경로(`UploadFailed`·비위협 안내·`Retry`)·성공 QR 생성 불변(기존 `QrPopupUploadTests` 전부 통과). 재시도(`OnEnterAsync` 재호출)가 진행률·상태 초기화.
  - [trigger] 진행 바는 `IsUploading=true`일 때만 표시. 진행률 갱신은 `Report` 콜백 시에만(UI 스레드 마샬링).
- **롤백**: 이 커밋 revert(시그니처는 Step 10 유지, 보고·표시만 제거).
- [ ] 완료

### Step 12: [A1 검증] FirebaseClient 파일 단위 진행률(GCS SDK) 또는 stage 폴백
- **Context Brief**: 대용량 타임랩스의 세밀한 바이트 진행률을 위해 `FirebaseClient.UploadFileAsync`에 GCS SDK 진행률을 배선한다. **A1 검증 필수**: `Google.Cloud.Storage.V1`의 `StorageClient.UploadObjectAsync`가 `IProgress<Google.Apis.Upload.IUploadProgress>` 인자를 지원하는지 먼저 확인. 지원하면 배선, 미지원이면 `IFirebaseClient`의 `IProgress<double>` 파라미터를 제거(롤백)하고 Step 11의 stage 진행률만 유지한다.
- **대상 파일**: `src/MCPhoto.Firebase/FirebaseClient.cs`, `src/MCPhoto.Firebase/UploadService.cs`(파일 진행→stage 진행 합성), (A1 실패 시)`src/MCPhoto.Core/Upload/IFirebaseClient.cs` 원복
- **선행 조건**: Step 10, Step 11
- **구현 내용**: **먼저 SDK API 확인** — `grep`/IntelliSense로 `UploadObjectAsync` 오버로드에 `IProgress<IUploadProgress>` 존재 확인(또는 `UploadObjectOptions` + progress 콜백). **지원 시**: `FirebaseClient.UploadFileAsync`에서 `var fileLen = new FileInfo(localFilePath).Length;` + `new Progress<IUploadProgress>(p => fileProgress?.Report(Math.Clamp(p.BytesSent/(double)fileLen,0,1)))`를 `UploadObjectAsync`에 전달. `UploadService`가 파일별 `IProgress<double>`를 stage fraction으로 변환해 상위 `progress`에 합성. **미지원 시(A1 실패)**: `IFirebaseClient.UploadFileAsync`의 `IProgress<double>` 파라미터 제거, `FirebaseClient`/`FakeFirebaseClient` 원복, Step 11의 stage 진행률(사진/타임랩스/마무리 3단계)만 최종안으로 확정.
- **검증 명령**: `dotnet build MCPhoto.sln -c Debug`(0 warning) + `dotnet test tests/MCPhoto.Tests/MCPhoto.Tests.csproj`. 실연동(사용자 액션): 실제 버킷 업로드 시 타임랩스 진행 바가 부드럽게 증가(지원 시) 또는 3단계 점프(폴백).
- **완료 기준**:
  - [관측] A1 결과 명시(지원/미지원)를 커밋 메시지·이 WBS에 기록. 지원 시 `FileInfo` 크기 기반 0~1 진행률이 상위로 전달됨(FakeFirebaseClient가 진행률 시뮬레이트하는 테스트). 미지원 시 시그니처 원복 + stage 진행률 테스트만 유지. 어느 경우든 빌드·테스트 통과.
  - [non-goal] 업로드 결과(URL·문서·성공/실패 분기) 불변. A1 미지원이어도 Step 11의 stage 진행률은 동작 유지.
  - [trigger] 파일 진행률은 실제 업로드 중 SDK 콜백 시에만(지원 시). 폴백 시 stage 경계에서만 갱신.
- **롤백**: 이 커밋 revert(Step 11 stage 진행률로 폴백 — 기능 저하 없이 세밀도만 감소).
- [ ] 완료

---

## 완결성 게이트 (자체 검사)

- [x] 검증된 사실 / 미검증 가정 분리됨
- [x] 모든 가정(A1~A4)에 검증 단계 매핑됨(A1→Step12, A2→Step9, A3→Step6, A4→Step3/5)
- [x] 모든 Step에 7개 필수 필드 존재
- [x] 모든 완료 기준이 관측 기반 3문 형식(UI Step 2·5b·6·7·11은 non-goal·trigger 포함)
- [x] 검증 명령이 자동 실행 가능(`dotnet build`/`dotnet test --filter`)

## USER-DECISION 요약 (부모 전달용)

| 기능 | 태그 | 이번 진행 여부 |
|------|------|----------------|
| #13 | Step 1~5 `[AUTONOMOUS-OK]` / **Step 5b `[USER-DECISION-REQUIRED]`**(컷별 재촬영 버튼 UI 배치) | Step 1~5 진행, Step 5b는 승인 대기 |
| #14 | `[AUTONOMOUS-OK]` | 전체 진행 |
| #15 | `[AUTONOMOUS-OK]`(best-effort, 다대 육안 검증은 사용자) | 전체 진행 |
| #16 | `[AUTONOMOUS-OK]`(A1은 Step 12에서 코드로 검증) | 전체 진행 |
