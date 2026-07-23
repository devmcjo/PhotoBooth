# MC포토 — 이터레이션 9 구현 WBS 블루프린트

| 항목 | 값 |
|------|-----|
| 문서 | 이터레이션 9 구현 WBS(developer 실행용) |
| 작성일 | 2026-07-23 |
| 설계 준거 | `docs/design/wpf-it9-design.md` |
| 요구 준거 | `docs/prd/iteration-9-camera-branding.md` |

> ⚠️ **착수 전제**: 설계 §7의 **결정 필요 사항 D1~D4·D6**이 상위에서 확정되어야 한다(D5는 VF-16으로 해소). 각 Step의 "선행 조건"에 해당 결정 의존을 명시했다. 결정 미확정 항목은 **권장안 기준**으로 Step을 기술했으며, 변경 시 해당 파라미터만 교체한다.

---

## 검증된 사실 (verified facts)

- 카메라 인덱스는 TextBox `{Binding CameraDevice}` — `SettingsView.xaml:113-114`, VM `CameraDevice`(int) `SettingsViewModel.cs:37,149`.
- `ICameraService.EnumerateDevices()` 존재(인덱스 0~7 프로빙, `CameraDevice(int Index, string Name)` record) — `OpenCvCameraService.cs:308`, `ICameraService.cs:44,56`.
- `ICameraService`는 Singleton — `ServiceRegistration.cs:31`. `StartAsync`는 running이면 파라미터 무시 후 true — `OpenCvCameraService.cs:57`.
- 프리뷰 렌더 재사용 컴포넌트 `CameraFramePresenter(Image)` `Attach/Detach` — `CameraFramePresenter.cs`, `CaptureView.xaml.cs`.
- 플래시=화면 하양 오버레이(`FlashActive`), 스틸=`CaptureStillAsync`(호출자가 저장) — `CaptureView.xaml:46`, `CaptureViewModel.cs:148-155`, `OpenCvCameraService.cs:234`.
- sticky 바 겹침: 단일 셀에 좌 StackPanel/우 닫기 버튼 — `SettingsView.xaml:254-264`. 토스트 색=`BoolToNoticeBrush`(유지).
- 런타임 "MC포토" 노출 2곳: `MainWindow.xaml:8` Title, `HomeView.xaml:15` 홈 타이틀. (grep 전수)
- INI 인프라 재사용 가능: `IniFile.Parse/GetString`(범용), `SettingsPathResolver`(실행경로 우선 폴백) — `IniFile.cs`, `SettingsPathResolver.cs`.
- 설정 진입 시 카메라 점유 화면 없음(촬영만 사용, 촬영 중 설정 불가; Preview는 데드코드) — `HomeViewModel.cs` grep, `AppShellViewModel.cs:163-179`.
- XAML 리소스 headless 회귀 테스트 방식 존재 — `XamlResourceTests.cs`.
- 기존 테스트: `SettingsTests`, `SettingsViewModelTests`, `XamlResourceTests`, `PreviewReadinessTests` 등 — `tests/MCPhoto.Tests/`.

## 미검증 가정 (open assumptions)

- OA-2. 별도 Window가 App 테마 리소스를 상속해 스타일 해석 실패 없음 → **검증: Step 3**.
- OA-3. 브랜딩 값 XAML 노출(D4 권장=DynamicResource 주입)로 창 제목·홈 타이틀 모두 치환 → **검증: Step 5**.
- OA-4. 브랜딩 ini 부재/빈 값 시 "MC포토" 폴백 → **검증: Step 4**.
- OA-5. `EnumerateDevices` UI 블로킹 방지(Task.Run) → **검증: Step 2**.
- `InverseBoolToVis` 컨버터 존재 여부(없으면 추가) → **검증: Step 1**.

---

## Step 1: 카메라 목록 노출용 SettingsViewModel 확장 (바인딩 준비)

- **Context Brief**: 설정 화면의 카메라 장치 선택을 TextBox(인덱스 직접 입력)에서 ComboBox(연결 장치 선택)로 바꾸기 위한 VM 측 준비. `ICameraService`는 이미 존재하는 `EnumerateDevices()`(인덱스 0~7 프로빙, `CameraDevice(int Index,string Name)` record)를 제공하고 DI Singleton이다. 이 Step은 VM에 목록/상태 프로퍼티와 열거 로직만 추가한다(XAML은 Step 2).
- **대상 파일**: `src/MCPhoto.App/ViewModels/SettingsViewModel.cs`; (컨버터 부재 시) `src/MCPhoto.App/Converters/*` 또는 `Themes/Controls.xaml`.
- **선행 조건**: 없음. (D2·D3는 Step 3에서만 필요 — 이 Step은 무관)
- **구현 내용**:
  1. 생성자에 `ICameraService camera` 파라미터 추가(DI가 Singleton 주입; VM은 소유·Dispose 안 함 — `PreviewViewModel` 관례).
  2. 프로퍼티 추가: `ObservableCollection<CameraDevice> CameraDevices { get; }`, `[ObservableProperty] bool _hasCamera`, `[ObservableProperty] bool _isEnumeratingCameras`.
  3. `RefreshCamerasAsync()` 메서드: `IsEnumeratingCameras=true` → `var devices = await Task.Run(() => _camera.EnumerateDevices())` → `CameraDevices` 갱신 → `HasCamera = Count>0` → 저장된 `CameraDevice`가 목록에 없고 `HasCamera`면 `CameraDevice=CameraDevices[0].Index`로 보정(목록 비면 값 유지) → `IsEnumeratingCameras=false`.
  4. `OnEnterAsync`에서 `LoadSettings()` 후 `await RefreshCamerasAsync()` 호출(기존 `OnEnterAsync`는 동기 → `async Task`로 변경, `LoadSettings()`는 그대로).
  5. `InverseBoolToVis` 컨버터가 없으면 추가(있으면 재사용). `grep InverseBoolToVis` / `BoolToVis` 정의 위치 확인.
- **검증 명령**: `build-verify` 스킬(또는 `dotnet build src/MCPhoto.App/MCPhoto.App.csproj -c Debug`) + `dotnet test tests/MCPhoto.Tests/MCPhoto.Tests.csproj --filter "FullyQualifiedName~SettingsViewModel"`; `grep -n "InverseBoolToVis\|CameraDevices\|HasCamera" src/MCPhoto.App/ViewModels/SettingsViewModel.cs`.
- **완료 기준**:
  - [관측] 빌드 error 0 + 변경 프로젝트 warning 0. `SettingsViewModel`에 `CameraDevices`/`HasCamera`/`IsEnumeratingCameras`/`RefreshCamerasAsync` 존재. 기존 `SettingsViewModelTests` 통과.
  - [non-goal] `CameraDevice`(int) 프로퍼티·저장 로직(`SaveSettings`) 시그니처/키 불변. 기존 설정 저장/로드 회귀 없음.
  - [trigger] 열거는 `OnEnterAsync`(설정 화면 진입) 시에만 실행 — 생성자/백그라운드 자동 반복 없음.
- **롤백**: 이 Step 커밋 revert(Step 2와 독립 — XAML 미변경이므로 VM만 되돌림).
- [ ] 완료

---

## Step 2: SettingsView 카메라 행 ComboBox 교체 + 없음 안내

- **Context Brief**: Step 1에서 준비한 `CameraDevices`/`HasCamera` 바인딩을 사용해 카메라 장치 선택을 ComboBox로 바꾼다. 기존 인덱스 저장(`CameraDevice` int)과 호환되도록 ComboBox `SelectedValuePath="Index"` + `SelectedValue="{Binding CameraDevice}"`. 연결 장치 0개면 ComboBox·테스트 버튼 Disable + 안내 문구.
- **대상 파일**: `src/MCPhoto.App/Views/SettingsView.xaml`(라인 108-115 "카메라 장치 인덱스" 행 영역).
- **선행 조건**: Step 1(VM 프로퍼티·컨버터).
- **구현 내용**: 설계 §2.1 XAML로 교체 — 라벨 "카메라 장치", `<StackPanel Orientation=Horizontal>` 안에 `ComboBox`(ItemsSource=`CameraDevices`, DisplayMemberPath=`Name`, SelectedValuePath=`Index`, SelectedValue=`CameraDevice`, IsEnabled=`HasCamera`) + `Button`("테스트", Command=`OpenCameraTestCommand`, IsEnabled=`HasCamera`). 아래에 `HasCamera=false`일 때만 보이는 안내 `TextBlock`(`InverseBoolToVis`). 기존 파일 인코딩(UTF-8) 유지.
- **검증 명령**: `dotnet test tests/MCPhoto.Tests/MCPhoto.Tests.csproj --filter "FullyQualifiedName~XamlResource"`(테마 리소스 해석 회귀) + `build-verify`. 육안: 설정 화면에서 카메라 여러 대/0대 상태 확인(0대는 임시로 `EnumerateDevices` mock 또는 카메라 분리).
- **완료 기준**:
  - [관측] 설정 화면 진입 시 연결된 카메라가 ComboBox에 나열되고(2대면 2개 항목), 선택 시 `CameraDevice`(int) 값이 해당 인덱스로 갱신됨. 카메라 0대면 ComboBox·테스트 버튼 비활성 + "연결된 카메라가 없습니다" 문구 표시. `XamlResourceTests` 통과(스타일 미해결 없음).
  - [non-goal] 다른 설정 행(촬영 컷/카운트다운/필터/QR 등) 레이아웃·바인딩 불변. `OpenCameraTestCommand`가 아직 없으면 이 Step에서는 버튼이 비어도 됨(Step 3에서 커맨드 연결) — 단 바인딩 경고가 build warning 유발 안 하도록 주의.
  - [trigger] 목록 갱신은 화면 진입 시(Step 1 `OnEnterAsync`)에만.
- **롤백**: 이 Step 커밋 revert(라인 영역만 원복 → TextBox 복귀).
- [ ] 완료

---

## Step 3: 카메라 테스트 모달(CameraTestWindow + CameraTestViewModel)

- **Context Brief**: 선택한 카메라로 실제 촬영과 동일한 화면(라이브 프리뷰 + 셔터 + 플래시)을 별도 모달 Window에서 테스트한다. 저장은 하지 않는다. 프리뷰 렌더는 기존 `CameraFramePresenter(Image)`를 재사용하고, 플래시/스틸은 `CaptureViewModel` 패턴을 따른다. 카메라는 DI Singleton이므로 모달 오픈 시 `StopAsync`→`StartAsync(선택인덱스)`, 닫기 시 `StopAsync`로 리소스를 확실히 해제한다. 설정 진입 시 카메라 점유 화면은 없음이 확인됨(설계 VF-16).
- **대상 파일**: 신규 `src/MCPhoto.App/Views/CameraTestWindow.xaml`(+`.xaml.cs`), 신규 `src/MCPhoto.App/ViewModels/CameraTestViewModel.cs`; `src/MCPhoto.App/ViewModels/SettingsViewModel.cs`(`OpenCameraTestCommand`); (D2=(A)이면) 신규 `ICameraTestDialogService`+구현 및 `ServiceRegistration.cs` 등록.
- **선행 조건**: Step 1(VM·카메라 주입). **결정 D2(모달 오픈 방식)·D3(충돌 순서=권장 (A))** 확정. Step 2와 병렬 가능하나 `OpenCameraTestCommand` 연결은 Step 2 버튼과 맞물림.
- **구현 내용**: 설계 §2.2대로 —
  - `CameraTestViewModel`: `ICameraService`(Camera 노출)·`ISettingsService` 주입, 생성 시 선택 `deviceIndex` 주입. `StartAsync`(await `StopAsync`→`StartAsync(idx, 3:4, MirrorMode)`→`PreviewReadiness` 기반 Ready 게이트 8s), `ShootTestCommand`(FlashMode면 `FlashActive` 펄스 120ms→`CaptureStillAsync` 결과 폐기), `CloseCommand`(`RequestClose` 이벤트), `StopAsync`.
  - `CameraTestWindow.xaml`: 프리뷰 `Image`(x:Name=PreviewImage), 상시 노티("테스트 화면입니다 · 촬영 결과는 저장되지 않습니다"), 셔터 버튼, 닫기, 플래시 오버레이, 로딩/실패 오버레이. App 테마 리소스 상속(별도 ResourceDictionary 병합 불필요 — 검증 대상).
  - `CameraTestWindow.xaml.cs`: `CameraFramePresenter(PreviewImage)` 생성, `DataContextChanged`에서 `Attach(vm.Camera)`, `Closed`에서 `Detach`.
  - 모달 오픈(D2=(A) 서비스 or (B) 직접): `Owner=MainWindow`, `WindowStartupLocation=CenterOwner`, `win.Closing += StopAsync`, `vm.RequestClose += win.Close`, `await vm.StartAsync()` 후 `ShowDialog()`.
- **검증 명령**: `build-verify` + 신규 headless 리소스 테스트(선택): `XamlResourceTests` 방식으로 `pack://.../Views/CameraTestWindow.xaml` 로드가 예외 없이 되는지(스타일 상속 확인). 육안: 설정→테스트 버튼→모달 프리뷰 표시→셔터 시 플래시(FlashMode on일 때)→닫기 후 카메라 해제(재열기 정상).
- **완료 기준**:
  - [관측] 테스트 버튼 클릭 시 모달 Window가 열리고 선택 카메라의 라이브 프리뷰가 표시된다. "테스트 화면입니다…" 노티가 항상 보인다. 셔터 클릭 시 (설정 FlashMode on이면) 흰 플래시 펄스가 재현된다. 모달을 닫으면 카메라가 해제되고(로그/재오픈 정상), 파일이 저장되지 않는다(result/sessions 폴더에 신규 파일 없음).
  - [non-goal] 테스트 촬영 결과가 디스크에 저장·업로드·합성되지 않는다. 설정값(FlashMode 등)이 테스트로 인해 변경되지 않는다. 실제 촬영 경로(`CaptureViewModel`)·홈은 영향 없다.
  - [trigger] 카메라 시작은 "테스트" 버튼 클릭 시에만. 셔터 촬영은 셔터 버튼 클릭 시에만(자동 촬영 없음). 카메라 해제는 모달 Close 시.
- **롤백**: 신규 파일 삭제 + `SettingsViewModel`의 `OpenCameraTestCommand`·`ServiceRegistration` 등록 revert(Step 1·2와 독립).
- [ ] 완료

---

## Step 4: 브랜딩 서비스(IBrandingService + branding.ini 로드)

- **Context Brief**: 앱 이름을 외부 ini로 바꿀 수 있게 한다. 기본 "MC포토", 파일 없거나 값 비면 폴백. 기존 `IniFile`(범용 파서)·`SettingsPathResolver`(실행경로 우선) 인프라를 재사용한다. 이 Step은 서비스만 만든다(UI 치환은 Step 5).
- **대상 파일**: 신규 `src/MCPhoto.Core/Branding/BrandingOptions.cs`, `IBrandingService.cs`, `IniBrandingService.cs`; `src/MCPhoto.App/ServiceRegistration.cs`(등록); 신규 테스트 `tests/MCPhoto.Tests/BrandingServiceTests.cs`; (샘플) `branding.ini`.
- **선행 조건**: **결정 D1(파일 위치=권장 실행경로)·D6-A(인코딩=UTF-8 명시)** 확정.
- **구현 내용**: 설계 §4.1 —
  - `BrandingOptions { string AppName = "MC포토"; }`.
  - `IBrandingService { string AppName { get; } }`.
  - `IniBrandingService(string? path=null, ILogger?=null)`: `path ?? ResolvePath()`(D1) → `File.Exists`면 `IniFile.Parse(File.ReadAllText(p, Encoding.UTF8))`(D6-A) → `GetString("Branding","AppName","MC포토")`, 공백 아니면 `Trim()` 적용. 예외 시 기본값(크래시 금지, `IniSettingsService` 패턴).
  - `ServiceRegistration`: `services.AddSingleton<IBrandingService, IniBrandingService>();`.
  - 테스트: (1) 파일 없음→"MC포토", (2) `AppName=` 빈 값→"MC포토", (3) `AppName=우리동네 포토부스`→그 값, (4) 손상 ini→크래시 없이 "MC포토". 임시 경로 주입(생성자 `path` 파라미터).
- **검증 명령**: `dotnet test tests/MCPhoto.Tests/MCPhoto.Tests.csproj --filter "FullyQualifiedName~Branding"` + `build-verify`.
- **완료 기준**:
  - [관측] `BrandingServiceTests` 4케이스 통과. `IBrandingService.AppName`이 ini 값(한글 포함) 또는 폴백 "MC포토"를 정확히 반환. 손상/부재 파일에서 예외 없이 폴백.
  - [non-goal] 기존 설정 로드/저장(`IniSettingsService`) 동작 불변(브랜딩 ini는 별도 파일). 다국어 문자열 카탈로그·리소스 사전 전환 미도입.
  - [trigger] 로드는 서비스 생성(=DI 시작 시) 1회. 런타임 재로드 없음.
- **롤백**: 신규 파일·등록·테스트 삭제(Step 5와 독립 — UI 미변경).
- [ ] 완료

---

## Step 5: 브랜딩 UI 치환(창 제목·홈 타이틀)

- **Context Brief**: Step 4의 `IBrandingService.AppName`을 실제 UI에 반영한다. 런타임 노출 2곳(창 제목 `MainWindow.xaml:8`, 홈 타이틀 `HomeView.xaml:15`)을 브랜딩 값으로 치환한다. 권장 방식(D4=(A)): 앱 시작 시 `Application.Resources["Branding.AppName"]`에 값을 주입하고 XAML에서 `{DynamicResource Branding.AppName}` 바인딩.
- **대상 파일**: `src/MCPhoto.App/App.xaml.cs`(리소스 주입), `src/MCPhoto.App/MainWindow.xaml`(Title), `src/MCPhoto.App/Views/HomeView.xaml`(타이틀 TextBlock).
- **선행 조건**: Step 4(`IBrandingService`). **결정 D4(노출 방식=권장 (A))** 확정.
- **구현 내용**: 설계 §4.2 —
  - `App.OnStartup`에서 `_host` build 후, `MainWindow` 해결/`Show` **전에**: `var branding = _host.Services.GetRequiredService<IBrandingService>(); Application.Current.Resources["Branding.AppName"] = branding.AppName;`.
  - `MainWindow.xaml:8`: `Title="MC포토"` → `Title="{DynamicResource Branding.AppName}"`.
  - `HomeView.xaml:15`: `Text="MC포토"` → `Text="{DynamicResource Branding.AppName}"`.
  - 기존 파일 인코딩(UTF-8) 유지.
  - (D4=(B)/(C)면 해당 방식으로 대체 — code-behind Title 대입 / VM 프로퍼티 바인딩.)
- **검증 명령**: `build-verify` + `XamlResourceTests`(리소스 해석). 육안: (1) branding.ini 없음→창 제목·홈 "MC포토", (2) `AppName=테스트부스` 넣고 재시작→창 제목·홈 모두 "테스트부스".
- **완료 기준**:
  - [관측] branding.ini의 `AppName`을 바꿔 앱을 재시작하면 창 제목(작업표시줄 포함)과 홈 화면 타이틀이 그 이름으로 표시된다. ini 없거나 빈 값이면 두 곳 모두 "MC포토".
  - [non-goal] 문서/인스톨러/웹/`Directory.Build.props`의 "MC포토"는 변경하지 않는다(런타임 UI 아님). 홈 레이아웃(Text.Display 스타일)은 유지 — 이름이 길어 넘칠 때의 레이아웃 대응은 별도(문서 주석으로 최대 길이 가이드).
  - [trigger] 브랜딩 반영은 앱 시작 시 리소스 주입 1회에 의존 — 실행 중 ini 편집 즉시 반영은 non-goal.
- **롤백**: 3개 파일의 해당 라인 revert("MC포토" 하드코딩 복귀; Step 4 서비스는 남아도 무해).
- [ ] 완료

---

## Step 6: 설정 저장/닫기 sticky 바 겹침 수정

- **Context Brief**: 설정 화면 하단 sticky 바에서 좌측(저장 버튼+안내 토스트)과 우측 닫기 버튼이 같은 Grid 셀에 겹쳐 있어, 안내문이 길면 겹친다. 2열 Grid(`*`/`Auto`)로 분리해 겹침을 제거한다. sticky 동작·토스트 색 분기는 유지한다.
- **대상 파일**: `src/MCPhoto.App/Views/SettingsView.xaml`(라인 254-264 sticky Grid 내부).
- **선행 조건**: 없음(다른 Step과 독립 — 병렬 가능).
- **구현 내용**: 설계 §3 XAML로 교체 — 내부 `Grid`에 `ColumnDefinitions`(`*`, `Auto`) 추가, 좌 `StackPanel`(Grid.Column=0: 저장+SavedNotice TextBlock, TextBlock에 `TextTrimming="CharacterEllipsis"`), 우 닫기 `Button`(Grid.Column=1, HorizontalAlignment=Right). 바깥 sticky `Border`(Grid.Row=1, 그림자·구분선)·`SaveSettingsCommand`/`CloseCommand`/`BoolToNoticeBrush`는 불변. 인코딩 유지.
- **검증 명령**: `dotnet test tests/MCPhoto.Tests/MCPhoto.Tests.csproj --filter "FullyQualifiedName~XamlResource"` + `build-verify`. 육안: 설정 화면에서 저장 클릭→긴 오류 토스트("저장 위치에 쓸 수 없습니다…") 표시 시 닫기 버튼과 겹치지 않음, 창 폭을 최소(1280)로 줄여도 겹침 없음.
- **완료 기준**:
  - [관측] 저장 성공/실패 토스트가 표시된 상태에서 닫기 버튼과 겹치지 않는다(토스트가 길면 좌 열 안에서 말줄임). 창 폭 최소에서도 겹침 없음. 저장·닫기 커맨드 정상 동작. 토스트 색(성공=민트/실패=로즈) 유지.
  - [non-goal] sticky 바의 위치(하단 고정, ScrollViewer 밖)·그림자·구분선·토스트 타이머(3s/6s) 불변. 다른 설정 섹션 불변.
  - [trigger] 토스트 표시는 저장 버튼 클릭 시에만(기존 동작 유지).
- **롤백**: 이 Step 커밋 revert(sticky Grid 원복).
- [ ] 완료

---

## 완결성 게이트 (developer 전달 전 자체 검사)

- [x] 검증된 사실 / 미검증 가정 분리
- [x] 모든 가정에 검증 Step 매핑(OA-2→S3, OA-3→S5, OA-4→S4, OA-5→S2, 컨버터→S1)
- [x] 모든 Step 7필드 채움
- [x] 완료 기준 3문 형식(UI Step S2·S3·S5·S6은 non-goal·trigger 포함)
- [x] 검증 명령 자동 실행 가능(build-verify / dotnet test --filter / grep)
- [ ] **결정 D1~D4·D6 확정 후 각 Step 파라미터 최종 고정** — 미확정 시 developer 전달 보류(권장안 기준 기술됨)

## 권장 구현 순서

1. **Step 6**(독립·저위험, 즉시 가능) →
2. **Step 4 → Step 5**(브랜딩, D1·D4·D6 확정 후) →
3. **Step 1 → Step 2 → Step 3**(카메라, D2·D3 확정 후; Step 3이 최대 리스크)

> Step 6·4는 결정 의존이 적어(6은 없음, 4는 D1/D6) 먼저 착수 가능. 카메라(1-3)는 D2·D3 확정 후.
