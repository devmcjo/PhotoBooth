# MC포토 — 이터레이션 9 설계 (카메라 설정·테스트 모달 · 설정 버튼 레이아웃 · 앱 이름 외부화)

| 항목 | 값 |
|------|-----|
| 문서 | WPF 이터레이션 9 설계 본문 |
| 작성일 | 2026-07-23 |
| 상태 | 초안 v1 (구현 착수 전, **결정 필요 사항 미확정**) |
| 1차 준거 | `docs/prd/iteration-9-camera-branding.md` |
| 상위 준거 | it2~it8, PRD v2.7 §9(#31 장치, #27 제품명) |
| 구현 WBS | `docs/design/wpf-it9-wbs.md` |
| 코드 베이스 | `E:\Study\photobooth\src\`. it2~it8 반영 |

> 배치: **C1 카메라 설정(ComboBox + 실촬영 동일 테스트 모달)**, **C2 설정 저장/닫기 버튼 겹침 수정**, **C3 앱 이름 외부 설정화(브랜딩)**. 다국어 전면지원·장치 FriendlyName 신규 의존성은 비범위.

---

## 0. 검증된 사실 / 미검증 가정

### 검증된 사실 (코드 인스펙션으로 직접 확인)

- **VF-1. 카메라 장치 인덱스는 TextBox**: `SettingsView.xaml:113-114`가 `TextBox ... Text="{Binding CameraDevice}"`. VM은 `SettingsViewModel.CameraDevice`(int, ObservableProperty, line 37). 저장은 `SaveSettings`에서 `s.CameraDevice = CameraDevice`(line 149). (근거: `SettingsView.xaml`, `SettingsViewModel.cs`)
- **VF-2. `EnumerateDevices()`는 이미 존재하며 인덱스 0~7 프로빙**: `OpenCvCameraService.EnumerateDevices()`(line 308-329)가 `new VideoCapture(i, DSHOW)` 열림 여부로 `CameraDevice(i, "Camera {i}")`(record) 리스트 반환. `ICameraService` 인터페이스에도 선언됨(`ICameraService.cs:44`). **DShow 장치 이름 열거 API가 제한적이어서 인덱스만 사용**(주석 명시). (근거: `OpenCvCameraService.cs:308`, `ICameraService.cs:44,56`)
- **VF-3. `ICameraService`는 Singleton**: `ServiceRegistration.cs:31` `AddSingleton<ICameraService, OpenCvCameraService>()`. → 홈 프리뷰(`PreviewViewModel`)·촬영(`CaptureViewModel`)·테스트 모달이 **동일 단일 인스턴스**를 공유. (근거: `ServiceRegistration.cs:31`)
- **VF-4. `StartAsync`는 이미 실행 중이면 파라미터 무시하고 true 반환**: `OpenCvCameraService.cs:57` `if (_running) return Task.FromResult(true);`. → 이미 카메라가 켜져 있으면 **다른 deviceIndex로 재시작 불가**. deviceIndex 변경은 반드시 `StopAsync` 선행이 필요. (근거: `OpenCvCameraService.cs:55-72`)
- **VF-5. 프리뷰 렌더는 재사용 컴포넌트 `CameraFramePresenter`**: `Image` 하나를 받아 `ICameraService.FrameReady`를 구독→재사용 `WriteableBitmap`에 커밋(`Attach(ICameraService)`/`Detach()`). `PreviewView`·`CaptureView`가 공유(`CameraFramePresenter.cs:9-11` 주석). → 테스트 모달도 `Image`+`CameraFramePresenter`로 프리뷰를 그대로 재사용 가능. (근거: `CameraFramePresenter.cs`, `CaptureView.xaml.cs:17,26`)
- **VF-6. 플래시 효과는 CaptureView의 순수 오버레이 방식**: `CaptureView.xaml:46-47` 흰 Border(`FlashOverlay`) `Visibility={Binding FlashActive}`. 시퀀스에서 `FlashActive=true`→`Task.Delay(120)`→스틸→`FlashActive=false`(`CaptureViewModel.cs:148-156`). 카메라 하드웨어 플래시가 아니라 **화면 하양 펄스**. → 테스트 모달에서 그대로 재현 가능. (근거: `CaptureView.xaml`, `CaptureViewModel.cs`)
- **VF-7. 스틸 캡처는 `CaptureStillAsync`(다음 프레임 1장 확정)**: `OpenCvCameraService.cs:234` `_pendingStill` TCS. 저장/합성은 호출자(`CaptureViewModel.RunCaptureSequenceAsync`)가 `session.Capture.AddCut(still)`로 수행 — 서비스는 저장 안 함. → 테스트 모달은 `CaptureStillAsync`를 호출하되 결과를 **버리면** 저장 없이 셔터 재현 가능. (근거: `OpenCvCameraService.cs:234`, `CaptureViewModel.cs:154-155`)
- **VF-8. 설정 sticky 바는 열 정의 없는 단일 Grid 셀 겹침**: `SettingsView.xaml:254-264`. `Grid`(ColumnDefinitions 없음) 안에 좌 `StackPanel`(HorizontalAlignment=Left: 저장 버튼+SavedNotice 텍스트)와 우 `Button`("닫기", HorizontalAlignment=Right)이 **같은 셀에 겹쳐** 배치. 안내문(SavedNotice)이 길어지면 닫기 버튼과 겹침. (근거: `SettingsView.xaml:254-264`)
- **VF-9. 토스트 색 분기·sticky 동작은 VM/Converter가 담당**: `SavedNotice`(text)·`SavedNoticeIsError`(bool)→`BoolToNoticeBrush` 컨버터로 성공(민트)/실패(로즈) 분기. sticky Border는 `Grid.Row=1`(ScrollViewer 밖). 이 로직은 C2에서 **변경 없이 유지**. (근거: `SettingsView.xaml:251-260`, `SettingsViewModel.cs:173-186`)
- **VF-10. 런타임 UI에 "MC포토"가 노출되는 지점은 정확히 2곳**: `MainWindow.xaml:8` `Title="MC포토"`(창 제목), `HomeView.xaml:15` `<TextBlock Text="MC포토" Style="Text.Display">`(홈 타이틀). 그 외 "MC포토"는 문서(`docs/`)·인스톨러(`installer/MCPhoto.iss`)·웹(`web/`)·빌드 메타(`Directory.Build.props:13` `<Product>`)로 **런타임 WPF UI 아님**. (근거: `grep MC포토` 전수)
- **VF-11. 설정 INI는 실행경로 우선 폴백 체인 사용**: `IniSettingsService`가 `SettingsPathResolver.DefaultCandidates(exeDir → %ProgramData%\MCPhoto → %LocalAppData%\MCPhoto)`로 쓰기 가능한 첫 경로 선택. `IniFile`은 범용 파서(`Parse`/`GetString`/섹션·키 대소문자 무시, 손상 라인 무시). → 브랜딩 파일도 `IniFile` 재사용 가능. (근거: `SettingsPathResolver.cs`, `IniSettingsService.cs`, `IniFile.cs`)
- **VF-12. 앱 데이터 폴더는 `App.DataFolder`(%CommonApplicationData%\MCPhoto)**: 로그·세션 임시가 여기. 실행경로는 `AppContext.BaseDirectory`. (근거: `App.xaml.cs:18`, `IniSettingsService.cs:91`)
- **VF-13. XAML 리소스 headless 회귀 테스트 존재**: `XamlResourceTests`가 STA 스레드에서 `pack://` 딕셔너리 로드 + 키 해석 검증(창 미표시). → 브랜딩을 XAML 리소스로 노출하면 이 방식으로 검증 가능. (근거: `XamlResourceTests.cs`)
- **VF-14. `SettingsViewModel`은 `AppShellViewModel`·`ISettingsService`를 생성자 주입, DI Transient**: `ServiceRegistration.cs:89`. Close는 `_shell.ReturnFromOverlay()`. → 테스트 모달 오픈 커맨드/카메라 서비스 주입을 여기에 추가 가능. (근거: `SettingsViewModel.cs:54`, `ServiceRegistration.cs`)
- **VF-15. 설정 화면 진입 경로는 오버레이(같은 창 내 화면 스왑)**: 상단 바 ⚙ → `OpenSettings` → `NavigateToOverlayAsync(Settings)`. 별도 Window 아님. → **테스트 모달만 별도 Window**가 됨(설계 신규). (근거: `AppShellViewModel.cs:272-277`, `MainWindow.xaml:37-42`)
- **VF-16. 라이브 프리뷰로 카메라를 켜는 화면은 촬영(`CaptureView`)뿐**: `HomeViewModel`은 카메라 미사용(`grep` — camera/preview 참조 0). `PreviewViewModel`/`PreviewView`는 DI 등록(`ServiceRegistration.cs:32`)·XAML 존재하나 **어떤 `AppState`에도 매핑 안 됨**(`AppShellViewModel.CreateViewModel` switch에 `AppState.Preview` 없음) → 실사용 데드코드. 촬영 중에는 상단 바 숨김(VF-15)이라 설정 진입 불가. **⇒ 설정 진입 시점에 카메라를 점유한 화면은 없다 → 테스트 모달·`EnumerateDevices`의 실질 충돌 원천 없음(D5 해소).** (근거: `HomeViewModel.cs` grep, `AppShellViewModel.cs:163-179`, `ServiceRegistration.cs:32`)

### 미검증 가정 (구현 시 검증 — WBS Step 매핑)

- **OA-1. (대부분 해소, VF-16)** 설정 진입 시 카메라 점유 화면은 없다(촬영만 켜고, 촬영 중 설정 진입 불가). 남는 유일한 잠재 충돌은 테스트 모달을 **연속으로 여닫을 때** 직전 `StopAsync`가 완전히 끝나기 전 재오픈하는 경우 → **검증: Step 3**(모달 오픈=`StopAsync`(await)→`StartAsync(선택인덱스)`, 닫기=`StopAsync`(await)). ※ **결정 D3**는 방어적 순서 확정용으로 유지(리스크는 VF-16으로 낮아짐).
- **OA-2. 테스트 모달을 별도 Window로 띄우면 STA/Owner 관계·리소스 딕셔너리 상속이 정상 동작한다** — 새 Window가 App 리소스(Theme)를 상속해 스타일 해석 실패 없음 → **검증: Step 3**(모달 육안 + `XamlResourceTests` 유사 headless 로드).
- **OA-3. 브랜딩 문자열을 XAML 정적 리소스(`{DynamicResource}` 또는 시작 시 주입)로 바인딩 시 창 제목·홈 타이틀이 모두 치환된다** → **검증: Step 5**(브랜딩 ini 값 주입 후 Title·홈 타이틀 육안 + 서비스 단위 테스트).
- **OA-4. 브랜딩 ini 부재/빈 값 시 "MC포토" 폴백이 동작한다** → **검증: Step 4**(BrandingService 단위 테스트: 파일 없음/빈 값/정상 값 3케이스).
- **OA-5. `EnumerateDevices()`가 UI 스레드에서 호출돼도 허용 가능한 지연(장치 0~7 프로빙, 각 VideoCapture open/close)** — 설정 진입 시 UI 블로킹 여부 → **검증: Step 2**(`Task.Run` 백그라운드 열거 + 로딩 표시, 실측). ※ **결정 필요 사항 D5 참조**.

---

## 1. 요구 → 설계 매핑 (한눈에)

| 요구 | 현황(VF) | 설계 조치 | WBS Step |
|---|---|---|---|
| **C1** 카메라 ComboBox + Disable | TextBox 인덱스(VF-1), Enumerate 존재(VF-2) | `CameraDevices` 목록 노출, ComboBox `SelectedValuePath=Index`, 빈 목록 시 Disable+안내 | §2, Step 1·2 |
| **C1** 실촬영 동일 테스트 모달 | 프리뷰/플래시/스틸 재사용 가능(VF-5·6·7) | 별도 Window(`CameraTestWindow`) + `CameraTestViewModel`, `CameraFramePresenter` 재사용, 테스트 노티 상시, 셔터=플래시 재현·저장 안 함, 닫기=StopAsync | §2, Step 3 |
| **C1** 리소스 충돌·해제 | Singleton(VF-3), Start 재시작 불가(VF-4) | 모달 오픈=`StopAsync`→`StartAsync(선택 인덱스)`, 닫기=`StopAsync`(+ **결정 D3**) | §2, Step 3 |
| **C2** 저장/닫기 겹침 | 단일 셀 겹침(VF-8) | sticky Grid를 2열(`*`/`Auto`)로 분리, 좌=저장+토스트, 우=닫기, 토스트 `TextTrimming` | §3, Step 6 |
| **C3** 앱 이름 외부화 | 하드코딩 2곳(VF-10), INI 인프라 재사용(VF-11) | `branding.ini`→`IBrandingService`(시작 1회 로드)→XAML 리소스/바인딩, 폴백 "MC포토" | §4, Step 4·5 |

---

## 2. C1 — 카메라 설정 (ComboBox + 테스트 모달)

### 2.1 ComboBox 교체 (Step 1·2)

**SettingsViewModel 변경:**

```csharp
// 신규 필드/프로퍼티
private readonly ICameraService _camera; // 생성자 주입 추가(DI Singleton)

/// <summary>연결된 카메라 목록(설정 진입 시 백그라운드 열거). 빈 목록이면 ComboBox Disable.</summary>
public ObservableCollection<CameraDevice> CameraDevices { get; } = new();

/// <summary>카메라 연결 여부(ComboBox IsEnabled 바인딩). 빈 목록=false.</summary>
[ObservableProperty] private bool _hasCamera;

/// <summary>카메라 열거 진행 중(로딩 표시·재열거 버튼 비활성).</summary>
[ObservableProperty] private bool _isEnumeratingCameras;
```

- 기존 `CameraDevice`(int) **프로퍼티는 유지**(설정 저장 키 호환). ComboBox는 `SelectedValuePath="Index"`, `SelectedValue="{Binding CameraDevice}"`로 바인딩 → 선택 시 `CameraDevice`(int) 갱신, 저장 로직(VF-1) 그대로 동작.
- `OnEnterAsync`(또는 별도 `RefreshCamerasAsync`)에서:
  ```csharp
  IsEnumeratingCameras = true;
  var devices = await Task.Run(() => _camera.EnumerateDevices()); // UI 블로킹 방지(OA-5)
  CameraDevices.Clear();
  foreach (var d in devices) CameraDevices.Add(d);
  HasCamera = CameraDevices.Count > 0;
  // 저장된 CameraDevice가 목록에 없으면 첫 장치로 보정(있을 때만). 없으면 값 유지(재연결 대비).
  if (HasCamera && CameraDevices.All(d => d.Index != CameraDevice))
      CameraDevice = CameraDevices[0].Index;
  IsEnumeratingCameras = false;
  ```
- **주의(리소스 충돌)**: `EnumerateDevices()`는 `new VideoCapture(i)`를 열고 닫는다. 홈 프리뷰가 카메라를 점유 중이면 프로빙이 해당 장치를 못 열 수 있음 → **결정 D5** 참조(설정 진입 시 프리뷰 정지 여부).

**SettingsView.xaml 변경(라인 113-114 영역):**

```xml
<TextBlock Grid.Column="0" Text="카메라 장치" Style="{StaticResource RowLabel}" />
<StackPanel Grid.Column="1" Orientation="Horizontal" HorizontalAlignment="Right">
    <ComboBox Width="200" ItemsSource="{Binding CameraDevices}"
              DisplayMemberPath="Name" SelectedValuePath="Index"
              SelectedValue="{Binding CameraDevice}"
              IsEnabled="{Binding HasCamera}" />
    <Button Content="테스트" Style="{StaticResource Button.Secondary}" Margin="8,0,0,0"
            Command="{Binding OpenCameraTestCommand}" IsEnabled="{Binding HasCamera}" />
</StackPanel>
<!-- 카메라 없음 안내(HasCamera=false일 때만). RowLabel 아래 캡션. -->
<TextBlock Grid.Column="1" Text="연결된 카메라가 없습니다. 연결 후 다시 열어 주세요."
           Style="{StaticResource Text.Caption}" Foreground="{StaticResource Brush.Text.Muted}"
           Visibility="{Binding HasCamera, Converter={StaticResource InverseBoolToVis}}" />
```

> ⚠️ `InverseBoolToVis` 컨버터가 없으면 신규 추가 필요(또는 기존 컨버터 재확인). **결정 아님** — Step 1에서 컨버터 존재 확인 후 없으면 추가(WBS에 명시).

### 2.2 카메라 테스트 모달 (Step 3)

**신규 파일:**
- `src/MCPhoto.App/Views/CameraTestWindow.xaml` (+ `.xaml.cs`)
- `src/MCPhoto.App/ViewModels/CameraTestViewModel.cs`

**CameraTestWindow.xaml (실촬영 화면 축약 재현):**

```xml
<Window x:Class="MCPhoto.App.Views.CameraTestWindow"
        Title="카메라 테스트" Width="960" Height="720"
        WindowStartupLocation="CenterOwner"
        Background="{StaticResource Brush.CaptureBg}">
    <Grid>
        <!-- 프리뷰(CaptureView와 동일 렌더: CameraFramePresenter 재사용) -->
        <Image x:Name="PreviewImage" Stretch="Uniform"
               RenderOptions.BitmapScalingMode="LowQuality" />

        <!-- 상시 노티(요구: "테스트 화면입니다" — 저장 안 됨 명시) -->
        <Border Background="{StaticResource Brush.Scrim}" CornerRadius="6" Padding="14,8"
                HorizontalAlignment="Center" VerticalAlignment="Top" Margin="0,20,0,0">
            <TextBlock Text="테스트 화면입니다 · 촬영 결과는 저장되지 않습니다"
                       Foreground="{StaticResource Brush.OnAccent}" FontSize="16" />
        </Border>

        <!-- 셔터(플래시 재현) -->
        <StackPanel HorizontalAlignment="Center" VerticalAlignment="Bottom" Margin="0,0,0,40">
            <Button Style="{StaticResource Button.Shutter}" Command="{Binding ShootTestCommand}"
                    HorizontalAlignment="Center" AutomationProperties.Name="테스트 촬영" />
            <TextBlock Text="테스트 촬영" Foreground="{StaticResource Brush.OnAccent}" Opacity="0.85"
                       FontSize="13" HorizontalAlignment="Center" Margin="0,8,0,0" />
        </StackPanel>

        <!-- 닫기 -->
        <Button Content="닫기" Command="{Binding CloseCommand}"
                Foreground="{StaticResource Brush.OnAccent}" Background="{StaticResource Brush.Scrim}"
                BorderThickness="0" Padding="16,8" Cursor="Hand"
                HorizontalAlignment="Right" VerticalAlignment="Top" Margin="20" />

        <!-- 플래시 오버레이(CaptureView와 동일 방식, VF-6) -->
        <Border x:Name="FlashOverlay" Background="{StaticResource Brush.OnAccent}" Opacity="0"
                Visibility="{Binding FlashActive, Converter={StaticResource BoolToVis}}" />

        <!-- 카메라 로딩/실패 오버레이(Ready 게이트, CaptureView 패턴 축약) -->
        <Grid Background="{StaticResource Brush.Scrim}"
              Visibility="{Binding IsLoading, Converter={StaticResource BoolToVis}}">
            <TextBlock Text="{Binding LoadingMessage}" Foreground="{StaticResource Brush.OnAccent}"
                       FontSize="18" HorizontalAlignment="Center" VerticalAlignment="Center" />
        </Grid>
    </Grid>
</Window>
```

**CameraTestWindow.xaml.cs** — `CaptureView.xaml.cs` 패턴(VF-5) 재사용:
```csharp
public partial class CameraTestWindow : Window
{
    private CameraFramePresenter? _presenter;
    public CameraTestWindow()
    {
        InitializeComponent();
        _presenter = new CameraFramePresenter(PreviewImage);
        DataContextChanged += (_, _) => {
            if (DataContext is CameraTestViewModel vm) _presenter?.Attach(vm.Camera);
        };
        Closed += (_, _) => _presenter?.Detach(); // Window Closed에서 Detach(구독 해제, 누수 방지)
    }
}
```

**CameraTestViewModel.cs** — 카메라 라이프사이클 소유:
```csharp
public sealed partial class CameraTestViewModel : ObservableObject
{
    private readonly ICameraService _camera;      // DI Singleton (촬영과 공유, VF-3)
    private readonly ISettingsService _settings;  // FlashMode 확인용(VF-6)
    private readonly int _deviceIndex;            // 설정에서 선택된 인덱스
    private CancellationTokenSource? _cts;

    public ICameraService Camera => _camera; // View가 Presenter Attach
    [ObservableProperty] private bool _flashActive;
    [ObservableProperty] private bool _isLoading = true;
    [ObservableProperty] private string _loadingMessage = "카메라 준비 중…";

    // 생성자: 선택 인덱스 주입(팩토리/직접 생성 — 결정 D2 방식에 따름)

    public async Task StartAsync()
    {
        // OA-1/D3: 기존 점유 해제 후 선택 인덱스로 재시작(StartAsync는 running이면 무시하므로, VF-4)
        await _camera.StopAsync();
        bool ok = await _camera.StartAsync(_deviceIndex, 3.0/4.0, _settings.Current.MirrorMode);
        if (!ok) { IsLoading = true; LoadingMessage = "카메라를 열 수 없습니다."; return; }
        // Ready 게이트: PreviewReadiness 재사용(CaptureViewModel 패턴). 타임아웃 시 실패 표시.
        IsLoading = !(await WaitForStablePreviewAsync(8000));
        if (IsLoading) LoadingMessage = "카메라 준비에 실패했습니다.";
    }

    [RelayCommand]
    private async Task ShootTest()
    {
        // 플래시 옵션 확인 후 재현(저장 안 함, VF-6·7)
        if (_settings.Current.FlashMode) { FlashActive = true; await Task.Delay(120); }
        var still = await _camera.CaptureStillAsync(); // 결과 즉시 폐기(저장/합성 없음)
        _ = still;
        FlashActive = false;
    }

    [RelayCommand] private void Close() => RequestClose?.Invoke(); // View가 Window.Close()
    public event Action? RequestClose;

    public async Task StopAsync() => await _camera.StopAsync(); // Window Closing에서 호출
}
```

- **모달 오픈(SettingsViewModel):**
  ```csharp
  [RelayCommand]
  private async Task OpenCameraTest()
  {
      var vm = /* D2 방식으로 생성, _deviceIndex=CameraDevice 주입 */;
      var win = new CameraTestWindow { DataContext = vm, Owner = Application.Current.MainWindow };
      vm.RequestClose += () => win.Close();
      win.Closing += async (_, _) => await vm.StopAsync(); // 리소스 확실 해제
      await vm.StartAsync();
      win.ShowDialog(); // 모달(설정 위)
      // 닫힌 뒤: 설정 화면이 프리뷰를 다시 켜야 하는지는 D5(설정 진입 시 프리뷰 정책)에 종속
  }
  ```
  > ⚠️ `SettingsViewModel`이 `Window`/`Application`을 직접 참조하면 MVVM 순수성·테스트성 저하. → **결정 D2**(다이얼로그 서비스 vs 직접 생성) 참조. 순수성 우선이면 `ICameraTestDialogService` 추상화 권장.

### 2.3 리소스 라이프사이클 (누수 방지)

- `CameraFramePresenter.Attach`는 내부에서 `Detach` 선행(재구독 안전). Window `Closed`에서 `Detach` → `FrameReady` 구독 해제(누수 0).
- `_camera.StopAsync`는 캡처 스레드 join(최대 2s) — 모달 닫힘 시 확실 해제(VF 근거 `OpenCvCameraService.StopAsync`).
- 테스트 모달과 실제 촬영은 **동시 진입 불가**(설정은 오버레이, 촬영 중 설정 진입 경로 없음, VF-15) → 촬영-테스트 동시 점유는 발생하지 않음. 남는 충돌원은 **홈 프리뷰**뿐 → D3/D5로 처리.

---

## 3. C2 — 설정 저장/닫기 버튼 겹침 수정 (Step 6)

`SettingsView.xaml:254-264`의 sticky Grid를 **2열 분리**. 나머지(Border sticky, 색 분기 VF-9)는 불변.

```xml
<Grid MaxWidth="1000" HorizontalAlignment="Center">
    <Grid.ColumnDefinitions>
        <ColumnDefinition Width="*" />     <!-- 좌: 저장 + 토스트 -->
        <ColumnDefinition Width="Auto" />  <!-- 우: 닫기 -->
    </Grid.ColumnDefinitions>
    <StackPanel Grid.Column="0" Orientation="Horizontal" HorizontalAlignment="Left">
        <Button Content="저장" Style="{StaticResource Button.Primary}"
                Command="{Binding SaveSettingsCommand}" Margin="0,0,12,0" />
        <TextBlock Text="{Binding SavedNotice}" Style="{StaticResource Text.Body}"
                   VerticalAlignment="Center" TextTrimming="CharacterEllipsis"
                   Foreground="{Binding SavedNoticeIsError, Converter={StaticResource BoolToNoticeBrush}}" />
    </StackPanel>
    <Button Grid.Column="1" Content="닫기" Style="{StaticResource Button.Ghost}"
            HorizontalAlignment="Right" Command="{Binding CloseCommand}" />
</Grid>
```

- 좌 `*` 열이 남는 폭을 차지하고 우 `Auto` 닫기 버튼은 고정폭 → **어떤 폭에서도 겹치지 않음**. 토스트는 `TextTrimming=CharacterEllipsis`로 좌 열 내에서만 잘림(닫기 침범 불가).
- **비고**: 좁은 폭에서 저장+토스트가 닫기와 붙는 것을 더 확실히 막으려면 좌 StackPanel에 `Margin="0,0,12,0"` 우측 여백 추가(선택).

---

## 4. C3 — 앱 이름 외부 설정화 (브랜딩) (Step 4·5)

### 4.1 브랜딩 소스 & 서비스

**신규 파일:**
- `src/MCPhoto.Core/Branding/BrandingOptions.cs` — `public sealed class BrandingOptions { public string AppName { get; set; } = "MC포토"; }`
- `src/MCPhoto.Core/Branding/IBrandingService.cs` — `string AppName { get; }` (로드된 값)
- `src/MCPhoto.Core/Branding/IniBrandingService.cs` — `branding.ini` 로드(실패/빈 값 시 "MC포토")

**IniBrandingService (IniFile·PathResolver 인프라 재사용, VF-11):**
```csharp
public sealed class IniBrandingService : IBrandingService
{
    private const string Section = "Branding";
    private const string DefaultAppName = "MC포토";
    public string AppName { get; private set; } = DefaultAppName;

    public IniBrandingService(string? path = null, ILogger<IniBrandingService>? logger = null)
    {
        try
        {
            var p = path ?? ResolvePath(); // 실행경로\branding.ini 우선(D1)
            if (File.Exists(p))
            {
                var ini = IniFile.Parse(File.ReadAllText(p));
                var name = ini.GetString(Section, "AppName", DefaultAppName);
                if (!string.IsNullOrWhiteSpace(name)) AppName = name.Trim();
            }
        }
        catch (Exception ex) { logger?.LogWarning(ex, "브랜딩 로드 실패, 기본값 사용"); }
    }
    // ResolvePath: AppContext.BaseDirectory\branding.ini (D1 결정에 따름)
}
```

**branding.ini 예시(고객 편집용):**
```ini
[Branding]
AppName=우리동네 포토부스
```

### 4.2 UI 바인딩 (하드코딩 치환, VF-10)

브랜딩 값을 XAML에 노출하는 방식은 **결정 D4** 참조. 아래는 **권장안(시작 시 Application 리소스 주입)** 기준:

- `App.OnStartup`에서 서비스 로드 후 `Application.Current.Resources["Branding.AppName"] = branding.AppName;` 주입(창 생성 전).
- `MainWindow.xaml:8`: `Title="{DynamicResource Branding.AppName}"` (또는 code-behind에서 `Title = branding.AppName`).
- `HomeView.xaml:15`: `<TextBlock Text="{DynamicResource Branding.AppName}" Style="{StaticResource Text.Display}" ... />`.
- `DynamicResource`는 리소스가 시작 시 주입되므로 정적 해석 문제 없음(VF-13 방식으로 검증).

> **주의**: 홈 타이틀은 로고성 텍스트(`Text.Display` 64px Bold). 브랜딩 이름이 매우 길면 레이아웃 깨질 수 있음 → `TextTrimming`/`TextWrapping` 또는 최대 길이 가이드(문서 주석). 완료 기준 non-goal에 명시.

### 4.3 DI 등록

```csharp
// ServiceRegistration.Register 상단(설정보다 먼저 로드해도 무방)
services.AddSingleton<IBrandingService, IniBrandingService>();
```
- App.OnStartup에서 `_host.Services.GetRequiredService<IBrandingService>()`로 해결 후 리소스 주입(D4가 리소스 주입 방식일 때).

---

## 5. 파일별 변경/신규 요약

### 변경 파일
| 파일 | 변경 | 요구 |
|---|---|---|
| `src/MCPhoto.App/Views/SettingsView.xaml` | 카메라 행 TextBox→ComboBox+테스트 버튼+없음 안내(§2.1); sticky Grid 2열 분리(§3) | C1, C2 |
| `src/MCPhoto.App/ViewModels/SettingsViewModel.cs` | `ICameraService` 주입, `CameraDevices`/`HasCamera`/`IsEnumeratingCameras`, 열거 로직, `OpenCameraTestCommand` | C1 |
| `src/MCPhoto.App/ServiceRegistration.cs` | `IBrandingService` 등록; (D2가 다이얼로그 서비스면 그 등록) | C1, C3 |
| `src/MCPhoto.App/App.xaml.cs` | 브랜딩 로드 + 리소스 주입(D4 방식) | C3 |
| `src/MCPhoto.App/MainWindow.xaml` | `Title="MC포토"`→브랜딩 바인딩 | C3 |
| `src/MCPhoto.App/Views/HomeView.xaml` | 홈 타이틀 `"MC포토"`→브랜딩 바인딩 | C3 |

### 신규 파일
| 파일 | 역할 |
|---|---|
| `src/MCPhoto.App/Views/CameraTestWindow.xaml` (+`.cs`) | 실촬영 동일 테스트 모달 View(프리뷰·노티·셔터·닫기) |
| `src/MCPhoto.App/ViewModels/CameraTestViewModel.cs` | 테스트 모달 카메라 라이프사이클·셔터·플래시(저장 없음) |
| `src/MCPhoto.Core/Branding/BrandingOptions.cs` | 브랜딩 값 홀더(기본 "MC포토") |
| `src/MCPhoto.Core/Branding/IBrandingService.cs` | 브랜딩 서비스 계약 |
| `src/MCPhoto.Core/Branding/IniBrandingService.cs` | branding.ini 로드(폴백 "MC포토") |
| `branding.ini`(선택 배포 샘플) | 고객 편집용 샘플(주석 포함) |
| `src/MCPhoto.App/Converters` (필요 시) | `InverseBoolToVis`(없을 때만) |
| (D2가 서비스면) `ICameraTestDialogService`/구현 | 모달 오픈 추상화 |

### DI/인터페이스 영향
- `ICameraService`(Core) **시그니처 변경 없음** — `EnumerateDevices()` 기존 사용.
- `SettingsViewModel` 생성자에 `ICameraService` 추가(Transient VM, Singleton 카메라 주입 — 소유·Dispose 안 함, `PreviewViewModel` 관례 동일).
- 신규 `IBrandingService` Singleton.

---

## 6. 스레딩·안전 고려

- **UI 블로킹 금지**: `EnumerateDevices()`는 장치 0~7 open/close(수백 ms~초 가능) → `Task.Run` 백그라운드, `IsEnumeratingCameras` 로딩 표시(OA-5, D5).
- **카메라 스레드 안전**: 프리뷰/스틸/녹화는 `OpenCvCameraService` 내부 백그라운드 스레드·`Interlocked`·lock으로 이미 보호. 테스트 모달은 서비스 API(`StartAsync`/`StopAsync`/`CaptureStillAsync`)만 사용 → 신규 스레딩 로직 없음.
- **이벤트 구독 해제**: `CameraFramePresenter.Attach`↔`Detach`(Window Closed). `CameraTestViewModel.RequestClose`(Action) — Window Close 후 참조 해제. 누수 경로 0.
- **전역 예외**: 모달 내 예외도 `App`의 `DispatcherUnhandledException`(Home 복귀)로 포착되나, 모달은 별도 Window라 Home 복귀 시 모달이 남을 수 있음 → 모달 오픈 시 try/catch로 자체 실패 처리(카메라 못 열면 로딩 실패 문구), 크래시 금지.
- **인코딩 보존**: 기존 XAML/C# 수정 시 현재 파일 인코딩(UTF-8) 유지. 신규 파일도 UTF-8. `branding.ini`는 한글 값 가능 → **UTF-8** 저장(`IniFile.Parse`는 `File.ReadAllText` 기본 인코딩 사용 — **결정 D6 참조**: BOM/인코딩 명시 필요 여부).

---

## 7. 결정 필요 사항 (임의 확정 금지 — 상위 코디네이터 판단)

> 아래는 **여러 타당한 선택지가 있어 architect가 임의 확정하지 않은** 지점이다. 각 선택지의 장단점과 **권장안**을 제시하되, 최종 결정은 상위에서 내린다.

### D1. 브랜딩 설정 파일 위치
| 선택지 | 장점 | 단점 |
|---|---|---|
| **(A) 실행파일 옆 `branding.ini`** *(권장)* | 배포 폴더에 동봉·교체 최단순, 기존 INI 1순위 관례(VF-11)와 일치, 고객이 파일 하나만 편집 | Program Files 설치 시 쓰기 권한 필요할 수 있으나 **읽기 전용이라 무관**(브랜딩은 읽기만) |
| (B) `%ProgramData%\MCPhoto\branding.ini` | 설치 계정 무관 공유, 쓰기 자유 | 고객이 경로를 찾기 어려움, 실행 폴더와 분리돼 배포 시 누락 위험 |
| (C) `%AppData%(사용자)\MCPhoto\branding.ini` | 사용자별 커스터마이즈 | 키오스크(단일 계정) 시나리오에 과함, 고객 편집 위치 불명확 |

**권장: (A)**. 브랜딩은 읽기 전용이라 실행경로 권한 문제 없음. 필요 시 (A)→(B) 폴백 체인(`SettingsPathResolver` 재사용)도 저비용.

### D2. 테스트 모달 오픈 방식 (MVVM 순수성 vs 단순성)
| 선택지 | 장점 | 단점 |
|---|---|---|
| **(A) `ICameraTestDialogService` 추상화** *(권장)* | `SettingsViewModel`이 `Window`/`Application` 미참조 → 단위 테스트 가능, MVVM 순수, 프로젝트의 서비스 추상화 관례와 일치 | 인터페이스+구현 1쌍 추가(소폭 보일러플레이트) |
| (B) `SettingsViewModel`에서 `new CameraTestWindow()` 직접 | 파일 최소, 즉시 | VM이 UI 타입(`Window`) 참조 → 테스트성·순수성 저하, architect 안전규칙 5 위반 |

**권장: (A)**. `IDialogService` 취지(설계 원칙 "뷰모델에서 다이얼로그 직접 열지 않음")와 정합. 단, 프로젝트에 유사 다이얼로그가 code-behind로 처리된 관례가 있으면 (B) 허용.

### D3. 카메라 리소스 충돌 처리 (핵심)
`ICameraService`는 Singleton(VF-3), `StartAsync`는 running이면 파라미터 무시(VF-4). 설정 진입 시 홈 프리뷰가 카메라를 켜뒀을 수 있음(OA-1).
| 선택지 | 장점 | 단점 |
|---|---|---|
| **(A) 모달 오픈=`StopAsync`→`StartAsync(선택인덱스)`, 닫기=`StopAsync`** *(권장)* | 선택 인덱스 확실 반영, 단일 인스턴스 정합, 구현 단순 | 모달 닫은 뒤 설정 화면에 프리뷰가 있었다면 재시작 필요(D5와 연동) |
| (B) 테스트 전용 **별도 카메라 인스턴스**(모달만 `new OpenCvCameraService()`) | 기존 점유와 완전 격리 | 동일 물리 장치를 2 인스턴스가 열려 하면 **OS 레벨 충돌**(UVC 단일 점유) 가능 → 오히려 실패 위험, DI 관례 이탈 |
| (C) `StartAsync`에 "deviceIndex 변경 시 자동 재시작" 로직 추가 | 호출부 단순 | 서비스 계층 동작 변경 → 촬영 경로 회귀 위험(범위 확대) |

**권장: (A)**. 물리 장치 단일 점유 특성상 인스턴스 격리(B)는 위험. 서비스 변경(C)은 회귀 리스크. D5(설정 진입 프리뷰 정책)와 함께 결정.

### D4. 브랜딩 값의 XAML 노출 방식
| 선택지 | 장점 | 단점 |
|---|---|---|
| **(A) 시작 시 `Application.Resources["Branding.AppName"]` 주입 + `{DynamicResource}`** *(권장)* | XAML 선언적, 정적 해석 문제 없음(시작 시 주입), `XamlResourceTests` 방식 검증 가능 | App.OnStartup에 주입 코드 1줄 |
| (B) code-behind에서 `Title = branding.AppName`, 홈은 VM 프로퍼티 바인딩 | 리소스 미사용, 명시적 | 지점마다 주입 코드 산재, 홈 VM에 브랜딩 프로퍼티 추가 필요 |
| (C) `HomeViewModel`/`MainWindow`에 `IBrandingService` 주입해 프로퍼티 노출 | 테스트성 | 창 제목은 VM 없음(Window code-behind 필요) → 혼합 방식 |

**권장: (A)**. 두 지점(창 제목·홈 타이틀)을 동일 메커니즘으로 커버, 향후 브랜딩 항목 확장 시 리소스 키만 추가.

### D5. 설정 진입 시 홈 프리뷰 카메라 점유 처리 — **해소됨(결정 불필요)**
**VF-16으로 확인 완료**: 설정 진입 직전 화면(홈 등)은 카메라를 켜지 않는다. 라이브 프리뷰로 카메라를 여는 화면은 촬영(`CaptureView`)뿐이고, 촬영 중에는 설정 진입 경로가 없다(상단 바 숨김). `PreviewView`/`PreviewViewModel`은 어떤 `AppState`에도 매핑되지 않은 데드코드다.
- **⇒ 설정 진입/테스트 모달/`EnumerateDevices` 시점에 카메라 충돌 원천 없음.** 별도 정책 불필요.
- 잔여 방어책은 D3(A)(모달 여닫을 때 `StopAsync`/`StartAsync` 순서)로 충분. **상위 결정 불필요 — 정보 제공용.**

### D6. 카메라 이름 포맷 & 브랜딩 ini 인코딩
| 항목 | 선택지 | 권장 |
|---|---|---|
| 카메라 이름 | (A) 현행 `"Camera {index}"` 유지 / (B) OS FriendlyName 조회(DirectShow `System.Management` 또는 P/Invoke `DsDevice`) | **(A) 유지** — (B)는 신규 의존성·범위 확대(비범위). 여러 대 구분은 인덱스로 충분. 향후 별도 이터레이션 |
| branding.ini 인코딩 | (A) UTF-8(BOM 여부) / (B) 시스템 기본 | **(A) UTF-8** — 한글 AppName 필수. `File.ReadAllText` 기본이 UTF-8 감지지만 **BOM 없는 UTF-8도 안전하게 읽도록 `Encoding.UTF8` 명시** 권장(D6-A). 고객 편집기(메모장) 저장 인코딩 편차 대비 |

---

## 8. 품질 자체 점검 (완결성 게이트)

- [x] 모든 View에 대응 ViewModel·연결 방식 명확(CameraTestWindow↔CameraTestViewModel, SettingsView↔SettingsViewModel)
- [x] 바인딩·명령에 누락된 VM 멤버 없음(CameraDevices/HasCamera/OpenCameraTestCommand/ShootTestCommand/CloseCommand 명세)
- [x] 이벤트 구독 해제 경로 명시(Presenter Attach/Detach@Closed, RequestClose)
- [x] UI/백그라운드 경계 명확(EnumerateDevices=Task.Run, 카메라 스레드는 서비스 내부)
- [x] 리소스 키 충돌 없음(신규 키 `Branding.AppName`만, 기존 미충돌)
- [x] 전역 예외/실패 표시 경로(모달 로딩 실패 문구, 크래시 금지)
- [x] VM UI 타입 의존 최소화(D2 권장안=서비스 추상화)
- [x] 인코딩 보존 명시(§6, D6)
- [x] **미해결 결정 6건(D1~D6)을 §7에 분리** — developer 착수 전 상위 확정 필요
- [ ] **결정 확정 전에는 developer 전달 금지** — D1~D6 확정 후 WBS의 해당 Step 파라미터 확정
