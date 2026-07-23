# it11 · 대기 기능 4종 상세 설계 (#13 재촬영 · #14 진단/상태 · #15 카메라 FriendlyName · #16 업로드 진행률/재시도)

| 항목 | 값 |
|------|-----|
| 문서 | it11 대기 기능(roadmap §2 #13~#16) 상세 설계 |
| 대상 | `wpf-developer` 구현용 근거·계약·플로우·엣지케이스·테스트 |
| 기준 코드 | 현재 `main` (빌드 0경고/0오류, 테스트 288 통과) |
| MVVM/DI | CommunityToolkit.Mvvm + Microsoft.Extensions.Hosting(Generic Host) |
| 최종 업데이트 | 2026-07-23 |

> **범위 밖(절대 건드리지 말 것)**: bldinfo(배포 시 사용자 관리), 비밀번호 해시/암호화(미진행), roadmap §2.1 장기 항목, §6 개발자 문의. `serviceAccountKey.json` 배포·publish 스크립트.

---

## 0. 검증된 사실 / 미검증 가정

### 0.1 검증된 사실 (verified facts — 코드 직접 확인)

- **재촬영 인프라 일부 존재**: `CutSelectViewModel.Retake`(RelayCommand)가 이미 `CaptureSession.ResetForRetake()` 호출 후 `NavigateAsync(AppState.Guide)`로 전이한다. (`src/MCPhoto.App/ViewModels/CutSelectViewModel.cs:79-85`)
- **전체 재촬영 전이는 이미 합법**: `SessionStateMachine.Forward[CutSelect] = { Result, Guide }` — CutSelect→Guide 합법. `AppStateTests.Retake_From_CutSelect_To_Guide_Is_Legal`가 이를 고정. (`src/MCPhoto.Core/Navigation/SessionStateMachine.cs:19`, `tests/.../AppStateTests.cs:44-49`)
- **`CaptureSession.ResetForRetake()`**: 컷·선택만 폐기, 프레임 유지. `Discard()`는 완전 폐기. 재촬영 카운터 필드는 없음. (`src/MCPhoto.Core/Capture/CaptureSession.cs:71-85`)
- **`CaptureViewModel`**: `OnEnterAsync`에서 `session.Capture.CutCount`만큼 `RunCaptureSequenceAsync` 루프로 전 컷을 순차 촬영. 컷별 재촬영/부분 촬영 개념 없음. `AddCut`은 `CutCount` 초과 시 무시. (`src/MCPhoto.App/ViewModels/CaptureViewModel.cs:128-179`, `CaptureSession.cs:44-48`)
- **`AppSettings`**: `Clamp()`가 범위 강제, `Clone()`가 전 필드 얕은 복제. INI 매핑은 `IniSettingsService.ReadInto/WriteFrom`에 `nameof(...)` 키로 1:1. `AllowedCutCounts`처럼 `static readonly int[]` 옵션 배열 관례. (`AppSettings.cs`, `IniSettingsService.cs:129-183`)
- **`SettingsViewModel`**: AppSettings 전 항목을 `[ObservableProperty]`로 미러, `LoadSettings()`/`SaveSettings()`가 필드↔`AppSettings` 왕복. QR 하위 토글 연동은 `_normalizing` 재진입 가드 + `partial void On...Changed`로 처리. 게스트 게이트는 `IsLoggedIn`/`IsGuest`(=`_shell.IsLoggedIn`). (`SettingsViewModel.cs`)
- **`SettingsView.xaml`**: 2열 그리드(`*|Auto(gap)|*`), `SettingRow`/`RowLabel`/`GroupTitle`/`GroupDivider` 스타일, `Toggle`/`ComboBox`/`Button.Secondary` 리소스, sticky 하단 바(저장/닫기), 서버 연결 상태(`ServerStatusText` + `IsServerConnected` DataTrigger 색상)가 이미 존재. (`SettingsView.xaml`)
- **카메라 열거**: `OpenCvCameraService.EnumerateDevices()`가 인덱스 0~7 `VideoCapture(i, DSHOW)` open/close 프로빙 → `new CameraDevice(i, $"Camera {i}")`. **FriendlyName 미조회**. `CameraDevice(int Index, string Name)` record, `ToString()=Name`. (`OpenCvCameraService.cs:308-329`, `ICameraService.cs:55-59`)
- **카메라 Singleton 제약**: `ICameraService`는 DI Singleton. `StartAsync`는 `if (_running) return true`로 파라미터 무시. 장치 전환은 `StopAsync`→`StartAsync` 필요. (`ServiceRegistration.cs:43`, `OpenCvCameraService.cs:56`, 메모리 `camera-singleton-constraint`)
- **ffmpeg 상태**: `FfmpegRunner.IsAvailable`(=`File.Exists(_ffmpegPath)`) / `FfmpegPath` 공개. `ResolveFfmpegPath()` = `{BaseDir}/tools/ffmpeg/ffmpeg.exe` → `{BaseDir}/ffmpeg.exe` → `"ffmpeg"`(PATH). `ITimelapseService`/`ICameraService`는 ffmpeg 상태를 노출하지 않음. `FfmpegRunner`는 DI Singleton. (`FfmpegRunner.cs:29-48`, `ServiceRegistration.cs:53`)
- **Firebase 상태**: `IFirebaseClient.IsInitialized`/`Bucket` 공개. `FirebaseClient.KeyCandidatePaths()`(static)가 키 후보 2경로 반환. (`IFirebaseClient.cs:11-14`, `FirebaseClient.cs:103-111`)
- **로그 경로**: `App.DataFolder` = `%CommonApplicationData%\MCPhoto`. 로그는 `{DataFolder}\logs\mcphoto-.log`(Serilog 일별 롤링). (`App.xaml.cs:18-19,31`)
- **업로드 경로**: `QrPopupViewModel.OnEnterAsync` → `IUploadService.UploadResultAsync(photoPath, timelapsePath, retentionHours, hostingBaseUrl, ct)` → `IFirebaseClient.UploadFileAsync(storagePath, localFilePath, contentType, ct)` → `StorageClient.UploadObjectAsync(obj, stream, ...)`. **`IProgress` 배선 없음**. `Retry`는 `OnEnterAsync` 재호출. 3상태(IsUploading/UploadSucceeded/UploadFailed)를 Visibility로 분기. (`QrPopupViewModel.cs`, `UploadService.cs`, `FirebaseClient.cs:137-155`, `QrPopupView.xaml`)
- **다이얼로그 서비스 관례**: `ICameraTestDialogService`/`IPasswordPromptDialogService`가 VM에서 Window를 직접 열지 않도록 추상화, `App/Services`에 구현, DI Singleton 등록. (`ServiceRegistration.cs:35-37`)
- **테스트 관례**: xUnit `[Fact]`/`[Theory]`. VM 단위 테스트는 `EmptyServiceProvider` + Fake/Stub 서비스 + 임시 INI 경로. 순수 로직(`SessionStateMachine`, `QrDeliveryPolicy`, `AppSettings.Clamp`)은 직접 테스트. (`SettingsViewModelTests.cs`, `QrPopupUploadTests.cs`, `AppStateTests.cs`)

### 0.2 미검증 가정 (open assumptions — 검증 단계 매핑은 WBS 문서)

| # | 가정 | 리스크 | 검증 방법(WBS) |
|---|------|--------|----------------|
| A1 | GCS `Google.Cloud.Storage.V1.UploadObjectOptions`가 진행률 콜백(`IProgress<IUploadProgress>`)을 지원한다 | 중 — 패키지 버전에 따라 API 상이 | 실제 SDK 참조 확인 + 컴파일. 미지원이면 파일 크기 기반 의사 진행률로 폴백 |
| A2 | DShow FriendlyName을 `System.Management`(WMI `Win32_PnPEntity`) 또는 DirectShow COM으로 조회 가능하나 **OpenCV 인덱스와의 정확한 매핑은 보장 불가**(best-effort) | 높음 — 매핑 오류 시 잘못된 이름 표시 | 설계상 best-effort로 확정(§3.15). 실장치 2대 이상 환경에서 육안 검증(사용자 액션) |
| A3 | `System.Diagnostics.Process.Start`로 탐색기(`explorer.exe`)를 열어 로그 폴더를 표시할 수 있다(키오스크 환경 허용) | 낮음 | 코드 확인 + 수동 실행. 키오스크 잠금 정책은 배포 시 사용자 판단(§3.14 note) |
| A4 | 컷별 재촬영 시 특정 컷 인덱스만 재촬영 후 원위치 교체하는 것이 `CaptureSession` 버퍼 모델(리스트 append)과 호환된다 | 중 | §3.13 설계대로 `RetakeCut(index)` 신설 + 단위 테스트로 검증 |

---

## 1. 공통 설계 원칙 (4개 기능 관통)

1. **INI 신규 키는 `AppSettings` 필드 + `Clamp()` + `ReadInto/WriteFrom` + `Clone()` 4곳 동시 추가** — 한 곳이라도 누락 시 저장/복원/편집취소 중 하나가 깨진다(현행 관례).
2. **#13·#14는 `SettingsView`/`SettingsViewModel`을 공유** — 재촬영 설정은 [앱 설정] "촬영" 그룹에, 진단 버튼은 [고급] 그룹 하단 서버 연결 상태 근처에 배치해 충돌 없이 병렬 개발 가능(서로 다른 XAML 영역).
3. **UI 타입 격리**: 신규 VM은 `System.Windows` 타입(Visibility/Brush)에 의존하지 않는다. 상태는 bool/enum/string으로, 색은 기존 DataTrigger 패턴(`IsServerConnected`류) 재사용.
4. **스레딩**: 장시간 작업(카메라 열거, ffmpeg 프로브, 업로드)은 `Task.Run`/`async` 백그라운드, UI 상태는 `[ObservableProperty]`(디스패처 마샬링은 CommunityToolkit이 처리하지 않으므로 **UI 스레드에서 await 재개**되도록 `ConfigureAwait` 미사용 = 기본값 유지). 진행률은 `IProgress<T>` 캡처를 UI 스레드 SynchronizationContext에서 생성.
5. **이벤트 구독 해제**: 신규 이벤트 구독(예: 진행률 핸들러, 카메라 FrameReady)은 반드시 `finally`/`OnLeaveAsync`/`Dispose`에서 해제(누수 방지, `WaitForStablePreviewAsync` 패턴 참조).
6. **성공 오인 금지**: 저장/업로드/진단 결과는 실패 시 정직하게 표시(기존 `IniSettingsService.Save()` bool, QR 실패 안내 원칙 계승).

---

## 2. 뷰↔뷰모델 매핑 (신규/변경)

| 기능 | View | ViewModel | 연결 방식 | 변경/신규 |
|------|------|-----------|-----------|-----------|
| #13 재촬영 설정 | `SettingsView.xaml` | `SettingsViewModel` | 기존 오버레이(DataContext=VM) | 변경(속성·XAML 추가) |
| #13 컷별 재촬영 | `CaptureView.xaml` | `CaptureViewModel` | 기존 | 변경(재촬영 버튼·플로우) |
| #13 전체 재촬영 | `CutSelectView.xaml` | `CutSelectViewModel` | 기존 | 변경(횟수 제한·컷별 게이트) |
| #14 진단 화면 | `DiagnosticsView.xaml`(신규) | `DiagnosticsViewModel`(신규) | 모달 다이얼로그 서비스(`IDiagnosticsDialogService`) | **신규** |
| #14 진단 진입 | `SettingsView.xaml` | `SettingsViewModel` | 버튼 → 다이얼로그 서비스 | 변경(버튼 1개) |
| #15 FriendlyName | (없음 — 서비스 계층) | — | `ICameraService.EnumerateDevices` 반환 개선 | 변경(Capture 계층) |
| #16 업로드 진행률 | `QrPopupView.xaml` | `QrPopupViewModel` | 기존 | 변경(진행률·재시도 강화) |

> **#14 진단 화면은 별도 `AppState`를 추가하지 않고 모달 다이얼로그로 구현**한다(§3.14 근거). 이유: 상태머신 전이표 확장·유휴 감시·상단바 로직에 영향 주지 않고, `ICameraTestDialogService`와 동일한 검증된 다이얼로그 패턴을 재사용하기 위함.

---

## 3. 기능별 상세 설계

---

### #13 재촬영 — 설정 옵션 + 촬영 플로우

#### 3.13.1 사용자 결정 태그

- **`[AUTONOMOUS-OK: 합리적 기본안]`** — 설정 계층·동작 규칙이 roadmap §2 #13에 명확히 확정되어 있어 스펙만으로 구현 가능. 아래는 그 스펙을 코드 구조로 옮긴 것.
- **단, 하위 항목 1건은 `[USER-DECISION-REQUIRED]`** — 아래 3.13.7 참조(전체 재촬영 버튼의 잔여 횟수 도달 시 UX).

#### 3.13.2 설정 스키마 (AppSettings 신규 필드)

```csharp
// AppSettings.cs — "촬영 옵션" 그룹에 추가
/// <summary>재촬영 사용(상위 토글). 기본 off. off면 재촬영 UI 전부 미노출.</summary>
public bool RetakeEnabled { get; set; }

/// <summary>재촬영 횟수 제한(전체+컷별 통합 카운트 상한). 기본 1, 범위 1~3. RetakeEnabled on일 때만 의미.</summary>
public int RetakeLimit { get; set; } = 1;

/// <summary>컷별 재촬영 활성화. 기본 off. RetakeEnabled on일 때만 의미.</summary>
public bool PerCutRetake { get; set; }

public static readonly int[] AllowedRetakeLimits = { 1, 2, 3 };
```

**`Clamp()` 추가**:
```csharp
if (Array.IndexOf(AllowedRetakeLimits, RetakeLimit) < 0)
    RetakeLimit = ClosestFrom(RetakeLimit, AllowedRetakeLimits, 1);
```

**`Clone()` 추가**: `RetakeEnabled`, `RetakeLimit`, `PerCutRetake` 3필드 복제.

**INI 매핑(`ReadInto`/`WriteFrom`)**: `nameof` 키 3개 추가(`GetBool`/`GetInt`, `SetBool`/`SetInt`).

#### 3.13.3 세션 재촬영 카운터 (CaptureSession)

동작 규칙("전체 재촬영을 한 번이라도 한 세션에서는 컷별 재촬영 미제공", "각 컷 1회", "횟수 제한까지")을 만족하려면 세션 단위 상태가 필요하다. `CaptureSession`에 순수 카운터를 추가한다(테스트 대상, UI 무관).

```csharp
// CaptureSession.cs 추가 필드/속성
private int _fullRetakeCount;              // 전체 재촬영 실행 횟수
private readonly HashSet<int> _perCutRetaken = new(); // 컷별 재촬영 완료한 슬롯 인덱스

/// <summary>지금까지 실행한 전체 재촬영 횟수.</summary>
public int FullRetakeCount => _fullRetakeCount;

/// <summary>전체 재촬영을 1회 이상 했는가(→ 컷별 재촬영 봉인).</summary>
public bool HasFullRetaken => _fullRetakeCount > 0;

/// <summary>특정 컷을 이미 컷별 재촬영했는가(각 컷 1회 규칙).</summary>
public bool WasCutRetaken(int cutIndex) => _perCutRetaken.Contains(cutIndex);

/// <summary>전체 재촬영 가능 여부(limit 미도달). limit는 호출측이 전달(설정 의존 제거).</summary>
public bool CanFullRetake(int limit) => _fullRetakeCount < limit;

/// <summary>전체 재촬영 실행: 컷·선택 폐기 + 카운터 증가. (기존 ResetForRetake 대체 경로)</summary>
public void BeginFullRetake()
{
    _cuts.Clear();
    _selection.Clear();
    _perCutRetaken.Clear();   // 전체 재촬영 후엔 컷별 이력도 리셋(새 촬영본)
    _fullRetakeCount++;
}

/// <summary>컷별 재촬영 실행 가능 여부: 전체 재촬영 안 했고, limit 미도달, 해당 컷 미재촬영.</summary>
public bool CanРerCutRetake(int cutIndex, int limit)
    => !HasFullRetaken && _perCutRetaken.Count < limit && !_perCutRetaken.Contains(cutIndex);

/// <summary>컷별 재촬영 완료 표시(카운트 소진).</summary>
public void MarkCutRetaken(int cutIndex) => _perCutRetaken.Add(cutIndex);
```

> **주의**: 위 `CanРerCutRetake`의 `Р`는 오타 방지 — 실제 구현은 `CanPerCutRetake`(ASCII P)로 작성할 것. (이 문서 렌더링 회피용 표기가 아니라 실제 메서드명은 `CanPerCutRetake`)

**`Discard()`**: `_fullRetakeCount = 0; _perCutRetaken.Clear();` 추가(세션 완전 폐기 시 카운터 초기화).

**기존 `ResetForRetake()`**: `CutSelectViewModel.Retake`가 호출 중이므로 **삭제하지 말고 `BeginFullRetake()`로 위임**하거나 `Retake` 커맨드를 `BeginFullRetake()` 호출로 교체(§3.13.5). 회귀 방지를 위해 `ResetForRetake()`는 `[Obsolete]` 없이 유지하되 내부에서 카운터를 건드리지 않는 기존 동작 보존 + 신규 경로는 `BeginFullRetake()` 사용.

**카운트 모델 결정**: 전체·컷별은 **통합 상한 `RetakeLimit`** 을 공유한다(스펙 "재촬영 횟수 제한"이 단일 값). 즉 전체 재촬영 `_fullRetakeCount`와 컷별 `_perCutRetaken.Count`는 각각 `RetakeLimit`을 상한으로 하되, 전체 재촬영을 하면 컷별은 봉인(`HasFullRetaken`)되므로 실질적으로 상호배타. `[AUTONOMOUS-OK]`: 전체·컷별 각각 독립적으로 limit까지 허용하는 해석을 채택(전체 3회 제한이면 전체를 최대 3회, 또는 전체를 안 했다면 컷별을 최대 limit개 슬롯까지).

#### 3.13.4 전체 재촬영 플로우 (CutSelectViewModel)

기존 `Retake`를 다음으로 교체:

```csharp
/// <summary>재촬영 UI 노출 여부(설정 on). View 바인딩.</summary>
public bool RetakeEnabled => _shell.Settings.Current.RetakeEnabled;

/// <summary>전체 재촬영 가능(설정 on AND limit 미도달). 버튼 IsEnabled.</summary>
public bool CanFullRetake =>
    RetakeEnabled && _shell.Session.Capture.CanFullRetake(_shell.Settings.Current.RetakeLimit);

[RelayCommand]
private async Task Retake()
{
    if (!CanFullRetake) return;                  // 방어(버튼 비활성이어도 이중 확인)
    _shell.Session.Capture.BeginFullRetake();    // 카운터 증가 + 컷·선택 폐기
    await _shell.NavigateAsync(AppState.Guide);
}
```

`OnEnterAsync`에서 `OnPropertyChanged(nameof(RetakeEnabled)); OnPropertyChanged(nameof(CanFullRetake));` 통지(진입마다 최신 카운터 반영).

**전이는 기존 `CutSelect→Guide` 재사용**(사실 §0.1). 상태머신 변경 불필요.

#### 3.13.5 컷별 재촬영 플로우 (CaptureViewModel — 신규 부분 촬영)

컷별 재촬영은 "특정 컷 1장만 다시 촬영"이다. 현행 `RunCaptureSequenceAsync`는 전 컷 순차 촬영만 지원하므로 **단일 컷 재촬영 경로**를 신설한다.

**진입점**: CutSelect 화면에서 컷별 재촬영 버튼(각 썸네일 위) → `CutSelectViewModel.RetakeSingleCut(cutIndex)` → `_shell.NavigateAsync(AppState.Capture)` 전이 시 세션에 "재촬영 대상 컷 인덱스"를 전달.

**세션 전달 방식**: `SessionContext`에 `int? RetakeTargetCut { get; set; }`(null=전체 촬영, 값=해당 컷만 재촬영) 추가. `Reset()`에서 null로 초기화.

```csharp
// CutSelectViewModel
public bool PerCutRetakeAvailable =>
    RetakeEnabled
    && _shell.Settings.Current.PerCutRetake
    && !_shell.Session.Capture.HasFullRetaken;   // 전체 재촬영 세션이면 미제공

/// <summary>특정 컷 재촬영 가능(각 컷 1회, limit 미도달). 썸네일 버튼 IsEnabled.</summary>
public bool CanRetakeCut(int cutIndex) =>
    PerCutRetakeAvailable
    && _shell.Session.Capture.CanPerCutRetake(cutIndex, _shell.Settings.Current.RetakeLimit);

[RelayCommand]
private async Task RetakeSingleCut(CutThumbnail? thumb)
{
    if (thumb is null || !CanRetakeCut(thumb.Index)) return;
    _shell.Session.RetakeTargetCut = thumb.Index;
    await _shell.NavigateAsync(AppState.Capture);   // Capture가 단일 컷 모드로 진입
}
```

> **전이 이슈**: `SessionStateMachine.Forward[CutSelect] = { Result, Guide }`에 `Capture`가 **없다**. 컷별 재촬영은 CutSelect→Capture 전이가 필요하므로 **전이표에 `Capture` 추가**: `[AppState.CutSelect] = new[] { AppState.Result, AppState.Guide, AppState.Capture }`. `AppStateTests`에 이 전이 합법성 테스트 추가, 기존 `Illegal_Transitions_Rejected`의 `Capture→Result` 불법성은 불변.

**`CaptureViewModel.OnEnterAsync` 분기**:
```csharp
var target = session.RetakeTargetCut;
if (target is int idx)
{
    // 단일 컷 재촬영 모드
    await RunSingleCutRetakeAsync(idx);
    session.RetakeTargetCut = null;   // 1회성 소비
    return;
}
// 기존 전체 촬영 시퀀스(변경 없음)
```

**`RunSingleCutRetakeAsync(int cutIndex)`**:
1. 카메라 Ready 대기(기존 `WaitForStablePreviewAsync` 재사용).
2. `TotalCuts = 1; CurrentCut = 1;`(UI 표시).
3. 카운트다운 1회(`CountdownAsync`) → 플래시/셔터음 → `CaptureStillAsync`.
4. **버퍼 교체**: `session.Capture.ReplaceCut(cutIndex, still)` 신설(아래).
5. `session.Capture.MarkCutRetaken(cutIndex)`.
6. **녹화 없음**(단일 컷 재촬영은 타임랩스에 반영 안 함 — 세션 원본 녹화는 이미 종료됨). `[AUTONOMOUS-OK]`: 컷별 재촬영은 타임랩스 재생성 안 함(스펙에 없고, 재녹화는 복잡도 급증). 타임랩스는 최초 전체 촬영 기준 유지.
7. `await _shell.NavigateAsync(AppState.CutSelect)` 복귀.

**`CaptureSession.ReplaceCut`**:
```csharp
/// <summary>기존 컷을 새 스틸로 교체(컷별 재촬영). 선택 상태는 유지.</summary>
public bool ReplaceCut(int cutIndex, CapturedStill still)
{
    if (cutIndex < 0 || cutIndex >= _cuts.Count) return false;
    _cuts[cutIndex] = still;
    return true;
}
```

> **`Capture→CutSelect` 복귀**: `Forward[Capture] = { CutSelect }` 이미 합법. 단일 컷 재촬영 후 복귀에 문제 없음. 단 `CutSelectViewModel.OnEnterAsync`가 `Cuts.Clear()` 후 재빌드하므로 교체된 컷이 반영됨. 선택 순서(`_selection`)는 세션에 보존되어 있으나 `OnEnterAsync`가 `SelectionOrder`를 재계산하지 않는 점 주의 — **`OnEnterAsync`에서 기존 `_selection` 기준으로 `SelectionOrder` 복원 로직 추가** 필요(현재는 append 순서만 표시하며 재진입 시 선택 표시가 사라질 수 있음). 엣지케이스 §3.13.6 참조.

#### 3.13.6 엣지케이스 (#13)

| # | 상황 | 처리 |
|---|------|------|
| E1 | 재촬영 off인데 세션 카운터 잔존 | `Reset()`/`Discard()`에서 카운터 초기화 → 다음 세션 영향 없음 |
| E2 | 전체 재촬영 limit 도달 후 버튼 | `CanFullRetake=false` → 버튼 Disable(방어로 커맨드 내 재확인) |
| E3 | 전체 재촬영 1회 후 컷별 진입 시도 | `PerCutRetakeAvailable=false`(`HasFullRetaken`) → 컷별 버튼 전부 숨김 |
| E4 | 컷별 재촬영 후 CutSelect 복귀 시 선택 표시 소실 | `CutSelectViewModel.OnEnterAsync`에서 `session.Capture.Selection` 기준 `SelectionOrder` 복원 로직 추가 |
| E5 | 컷별 재촬영 중 유휴 타임아웃 | `Capture`는 `IsSessionActive=true`이나 `IsTopBarVisible=false`. 유휴 감시 유지(무인 보호). 홈 복귀 시 `Reset`으로 카운터 정리 |
| E6 | 재촬영 대상 컷 인덱스가 범위 밖(방어) | `ReplaceCut` false 반환 → 로그 경고 후 CutSelect 복귀 |
| E7 | 컷별 재촬영을 각 컷 최대치까지 소진 | `_perCutRetaken.Count >= limit` → 모든 컷 버튼 Disable |

#### 3.13.7 [USER-DECISION-REQUIRED] 항목

**전체 재촬영 버튼을 어디에 노출할지**: 현재 `CutSelectView`에 "다시 촬영" 버튼이 이미 있고(전체 재촬영), 이를 재활용한다 — 여기까지는 자율 결정. 그러나 다음은 제품 결정:

- **컷별 재촬영 버튼의 물리적 위치**: 각 컷 썸네일 위 오버레이 버튼(↺ 아이콘) vs 별도 "이 컷 다시 찍기" 모드. `[USER-DECISION-REQUIRED]` — UI 배치·인터랙션 방식은 육안 확인이 필요한 제품 결정. **부모(오케스트레이터)는 이 결정 전까지 컷별 재촬영 UI 배치를 확정하지 말 것.** (설정 스키마·세션 카운터·전체 재촬영은 자율 진행 가능)
- 기본 제안(승인 시): 썸네일 우하단에 작은 ↺ 버튼, `CanRetakeCut` false면 숨김.

#### 3.13.8 테스트 계획 (#13)

- `CaptureSessionTests`(기존 파일 확장): `BeginFullRetake` 카운터 증가, `CanFullRetake(limit)` 경계(0/limit), `HasFullRetaken` 후 `CanPerCutRetake=false`, `MarkCutRetaken`+`WasCutRetaken`, `ReplaceCut` 범위 밖 false, `Discard` 후 카운터 0.
- `AppStateTests`: `CutSelect→Capture` 합법 추가, `Capture→Result` 여전히 불법 확인.
- `SettingsTests`/`SettingsViewModelTests`: `RetakeLimit` Clamp(0→1, 5→3), 신규 필드 INI 왕복, `RetakeEnabled` off 시 하위 무의미 확인.
- `CutSelectViewModel`(신규 테스트 파일 가능): `RetakeEnabled` off면 `CanFullRetake=false`, 전체 재촬영 세션에서 `PerCutRetakeAvailable=false`.

---

### #14 진단/상태 화면

#### 3.14.1 사용자 결정 태그

- **`[AUTONOMOUS-OK: 합리적 기본안]`** — 진입 위치(설정 화면 내 버튼)·표시 항목(카메라/ffmpeg/Firebase 헬스체크 + 로그 폴더)이 roadmap §2 #14에 확정. 모달 다이얼로그 방식은 기존 `ICameraTestDialogService` 패턴의 자연스러운 확장이므로 자율 결정.

#### 3.14.2 아키텍처 결정: 모달 다이얼로그 (AppState 미추가)

진단 화면은 **`AppState` 미추가, 모달 다이얼로그**로 구현한다.

- **근거**: (1) 상태머신 전이표/유휴 감시/상단바 로직 무변경 → 회귀 표면 최소. (2) `ICameraTestDialogService`(`CameraTestDialogService`)가 이미 "VM에서 Window 미참조 + DI Singleton + `ShowAsync`" 패턴을 확립. (3) 관리자 트러블슈팅용 일시 표시라 세션 상태 스택에 넣을 필요 없음.
- **대안 기각**: 새 `AppState.Diagnostics` 추가 시 `Forward`/`IsSessionActive`/`IsTopBarVisible`/`CreateViewModel` 4곳 + 오버레이 복귀 로직 검토 필요 → 과도.

#### 3.14.3 진단 데이터 계약

기존 인터페이스가 필요한 상태를 대부분 노출하나, **ffmpeg 상태가 `ITimelapseService`/`ICameraService`에 없다**. `FfmpegRunner`가 DI Singleton으로 등록되어 있으므로 `DiagnosticsViewModel`에 **`FfmpegRunner`를 직접 주입**(이미 컨테이너에 있음)하거나, 깔끔하게 `ITimelapseService`에 상태 노출 프로퍼티를 추가한다.

**결정 `[AUTONOMOUS-OK]`**: `FfmpegRunner`를 `DiagnosticsViewModel`에 직접 주입(추가 인터페이스 표면 없이 `IsAvailable`/`FfmpegPath` 사용). 이유: `FfmpegRunner`는 이미 public 구상 타입이며 DI Singleton, `ResultViewModel`도 `Capture.OpenCvCameraService` 구상 타입에 캐스팅해 접근하는 선례 존재(`ResultViewModel.cs:132`).

```csharp
public sealed partial class DiagnosticsViewModel : ObservableObject   // ViewModelBase 불필요(다이얼로그 전용)
{
    private readonly ICameraService _camera;
    private readonly FfmpegRunner _ffmpeg;
    private readonly IFirebaseClient _firebase;
    private readonly ILogFolderService _logFolder;   // 신규(§3.14.5)
    private readonly ILogger<DiagnosticsViewModel>? _logger;

    // ── 카메라 ──
    [ObservableProperty] private bool _isCheckingCamera;
    [ObservableProperty] private int _cameraCount;
    [ObservableProperty] private string _cameraSummary = string.Empty; // 예: "2대 연결됨" / "미연결"
    public ObservableCollection<CameraDevice> Cameras { get; } = new();

    // ── ffmpeg ──
    public bool FfmpegAvailable => _ffmpeg.IsAvailable;
    public string FfmpegPath => _ffmpeg.FfmpegPath;

    // ── Firebase ──
    public bool FirebaseInitialized => _firebase.IsInitialized;
    public string FirebaseBucket => _firebase.IsInitialized ? _firebase.Bucket : "(미초기화)";
    public IReadOnlyList<string> FirebaseKeyCandidates => FirebaseClient.KeyCandidatePaths();

    // ── 로그 ──
    public string LogFolderPath => _logFolder.LogFolderPath;

    [RelayCommand] private async Task RefreshCameras() { /* Task.Run(EnumerateDevices) — §3.14.4 */ }
    [RelayCommand] private void OpenLogFolder() => _logFolder.OpenLogFolder();
    [RelayCommand] private void Close() { /* 다이얼로그 서비스가 창 닫기 */ }
}
```

#### 3.14.4 카메라 헬스체크 스레딩 (Singleton 충돌 주의)

`EnumerateDevices()`는 장치 open/close 프로빙이라 **촬영 중이면 물리 카메라를 점유해 충돌**할 수 있다. 그러나 진단 화면은 **설정 오버레이 내부**에서만 열리고, 설정 진입은 `IsTopBarVisible` 상태(촬영/QR 아님)에서만 가능하므로 촬영과 동시 진입 불가(메모리 `camera-singleton-constraint` 전제). 그래도:

- `RefreshCameras`는 `Task.Run(() => _camera.EnumerateDevices())` 백그라운드(수백 ms~초, UI 블로킹 방지 — `SettingsViewModel.RefreshCamerasAsync`와 동일 패턴).
- `IsCheckingCamera` 로딩 표시.
- 진단 화면은 **라이브 프리뷰를 켜지 않는다**(`StartAsync` 미호출) → 카메라 점유 없음, 열거만.

#### 3.14.5 로그 폴더 서비스 (신규)

VM이 `System.Diagnostics.Process`/경로를 직접 만지지 않도록 추상화(테스트 가능성 + 관례).

```csharp
// Core 또는 App/Services — App 계층이 App.DataFolder를 알므로 App/Services 권장
public interface ILogFolderService
{
    /// <summary>로그 폴더 절대 경로(표시용).</summary>
    string LogFolderPath { get; }
    /// <summary>탐색기로 로그 폴더 열기. 실패해도 크래시 금지(로그만).</summary>
    void OpenLogFolder();
}

public sealed class LogFolderService : ILogFolderService
{
    private readonly ILogger<LogFolderService>? _logger;
    public LogFolderService(ILogger<LogFolderService>? logger = null) => _logger = logger;

    public string LogFolderPath => Path.Combine(App.DataFolder, "logs");

    public void OpenLogFolder()
    {
        try
        {
            Directory.CreateDirectory(LogFolderPath);   // 없으면 생성(폴더 열기 성공 보장)
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{LogFolderPath}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex) { _logger?.LogWarning(ex, "로그 폴더 열기 실패: {Path}", LogFolderPath); }
    }
}
```

> **키오스크 note**: `explorer.exe` 실행은 키오스크 잠금(셸 교체) 환경에선 정책상 차단될 수 있다. 진단은 관리자 전용이고 배포 정책은 사용자 판단(범위 밖) → best-effort. 폴더 열기 실패 시 **경로 텍스트는 항상 화면에 표시**(수동 탐색 가능)하도록 UI에 `LogFolderPath` 노출.

#### 3.14.6 다이얼로그 서비스 (신규)

```csharp
// App/Services
public interface IDiagnosticsDialogService { Task ShowAsync(); }

public sealed class DiagnosticsDialogService : IDiagnosticsDialogService
{
    private readonly IServiceProvider _services;
    public DiagnosticsDialogService(IServiceProvider services) => _services = services;

    public async Task ShowAsync()
    {
        var vm = _services.GetRequiredService<DiagnosticsViewModel>();
        var win = new DiagnosticsWindow { DataContext = vm, Owner = Application.Current.MainWindow };
        await vm.RefreshCamerasCommand.ExecuteAsync(null);   // 진입 시 카메라 자동 검사
        win.ShowDialog();
    }
}
```

(`CameraTestDialogService` 구현을 그대로 참조해 동일 스타일로 작성 — developer는 그 파일을 근거로 삼을 것.)

#### 3.14.7 진입 UI (SettingsView)

- **위치**: [고급] 그룹의 "서버 연결" 상태 행 **아래**에 "진단·상태" 버튼 1개 추가. `[AUTONOMOUS-OK]`.
- **가시성**: roadmap §2 #14 "로그인 상태에서" → `IsEnabled="{Binding IsLoggedIn}"` 또는 `Visibility`(관리자 트러블슈팅). `[AUTONOMOUS-OK]`: 로그인 시에만 활성(게스트 숨김) — 기존 게스트 게이트 관례(`IsLoggedIn`) 재사용.
- `SettingsViewModel`에 `IDiagnosticsDialogService` 주입 + `[RelayCommand] OpenDiagnostics()` 추가:
```csharp
[RelayCommand]
private async Task OpenDiagnostics()
{
    if (!IsLoggedIn) return;
    try { await _diagnostics.ShowAsync(); }
    catch (Exception ex) { _logger?.LogError(ex, "진단 다이얼로그 오류"); }
}
```

> **`SettingsViewModel` 생성자 시그니처 변경 주의**: 신규 의존성 `IDiagnosticsDialogService` 추가 시 기존 테스트 `SettingsViewModelTests.MakeVm`가 깨진다 → **선택적 파라미터(`IDiagnosticsDialogService? diagnostics = null`)로 추가**하거나 테스트 헬퍼를 함께 갱신. 관례상 다른 다이얼로그 서비스(`ICameraTestDialogService`)는 필수 파라미터이므로, 테스트 헬퍼 갱신(Fake 추가)이 일관적. developer는 `MakeVm`에 `FakeDiagnosticsDialog` 추가 필수.

#### 3.14.8 진단 화면 표시 항목 (UI 요약)

| 섹션 | 표시 | 색/상태 |
|------|------|---------|
| 카메라 | "N대 연결됨" + 목록(Index·Name) / "미연결" | 연결=Success, 미연결=Danger (기존 DataTrigger 패턴) |
| ffmpeg | "사용 가능" + 경로 / "미탑재" + 경로 | `FfmpegAvailable` DataTrigger |
| Firebase | "초기화됨 — {bucket}" / "미초기화" + 키 후보 경로 목록 | `FirebaseInitialized` DataTrigger |
| 로그 | 폴더 경로 텍스트(선택 복사 가능) + [폴더 열기] 버튼 | — |

#### 3.14.9 엣지케이스 (#14)

| # | 상황 | 처리 |
|---|------|------|
| E1 | 진단 중 카메라 없음 | `CameraCount=0`, "미연결" 표시(크래시 없음, 열거는 빈 목록) |
| E2 | 로그 폴더 아직 없음 | `OpenLogFolder`가 `Directory.CreateDirectory` 선행 |
| E3 | `explorer.exe` 실행 차단(키오스크) | 예외 캐치 → 로그만, 경로 텍스트는 화면 유지 |
| E4 | Firebase 키 후보 경로 표시 | `KeyCandidatePaths()` static 재사용(존재 여부까지 표시하면 QA 유용 — `File.Exists` 부기) |
| E5 | 다이얼로그 중 유휴 타임아웃 | 설정 오버레이(=`Settings` 상태)는 `IsSessionActive=false`라 유휴 감시 비대상 → 진단 다이얼로그 떠 있어도 홈 강제 복귀 없음. 안전 |

#### 3.14.10 테스트 계획 (#14)

- `DiagnosticsViewModelTests`(신규): Fake `ICameraService`(0대/2대)로 `RefreshCameras` 후 `CameraCount`/`Cameras`, ffmpeg availability(Fake 불가 → `FfmpegRunner`는 실경로 의존이므로 `FfmpegPath` non-empty만 확인 또는 주입 경로 테스트), `FirebaseInitialized`/`FirebaseBucket`(FakeFirebaseClient), `LogFolderPath`(ILogFolderService Fake).
- `LogFolderServiceTests`(신규, 선택): `LogFolderPath`가 `{DataFolder}\logs`인지. `OpenLogFolder`는 프로세스 실행이라 단위 테스트 부적합 → 예외 미발생만 스모크(또는 생략).
- `SettingsViewModelTests`: `OpenDiagnosticsCommand`가 게스트에서 no-op, 로그인 시 다이얼로그 서비스 `ShowAsync` 호출(Fake로 호출 여부 검증).

---

### #15 카메라 장치 FriendlyName

#### 3.15.1 사용자 결정 태그

- **`[AUTONOMOUS-OK: 합리적 기본안, 단 신뢰성 리스크 명시]`** — roadmap §2 #15가 "의존성 검토 + best-effort 여부 평가"를 명시적으로 요구. 아래 설계는 **best-effort FriendlyName + 인덱스 폴백**을 확정안으로 제시. 실장치 다대(多臺) 검증은 사용자 액션(가정 A2).

#### 3.15.2 문제·리스크 분석 (반드시 평가 — roadmap 요구)

- **현행**: `EnumerateDevices()`가 OpenCV `VideoCapture(i, DSHOW)` 인덱스 프로빙으로 `"Camera {i}"` 라벨만 생성. 여러 대일 때 구분 불가.
- **DShow FriendlyName 조회 경로**:
  1. **DirectShow COM 열거**(`ICreateDevEnum` → `CLSID_VideoInputDeviceCategory`) — 정확하나 C#에서 COM interop 코드가 무겁고 의존성(DirectShowLib류) 추가 위험.
  2. **WMI (`System.Management`, `Win32_PnPEntity`/`Win32_PnPSignedDriver`)** — `Caption`으로 이미지 장치명 조회. `System.Management` 참조 추가 필요(.NET 8에서 NuGet `System.Management` 패키지).
- **핵심 리스크 — 인덱스↔이름 매핑 불확실성 (A2, 높음)**: OpenCV DShow 백엔드의 장치 **인덱스 순서**와 WMI/DShow 열거 **순서**가 일치한다는 보장이 없다. OpenCV는 내부적으로 DShow 열거 순서를 쓰지만, 장치 추가/제거·USB 포트·드라이버에 따라 순서가 달라질 수 있다. → **이름을 인덱스에 잘못 붙일 위험**.
- **결론(best-effort 확정)**: FriendlyName은 **표시 개선용 best-effort**로만 제공하고, **동작(장치 선택)은 여전히 OpenCV 인덱스 기준**으로 유지한다. 이름이 틀려도 기능은 인덱스로 정확히 동작. 매핑 실패/불일치 시 `"Camera {i}"` 폴백.

#### 3.15.3 설계: 의존성 최소 WMI 조회 + 순서 매핑 + 폴백

**의존성 결정 `[AUTONOMOUS-OK]`**: `System.Management` NuGet 패키지 추가(WMI). DirectShow COM interop보다 코드량·리스크 적음. **P/Invoke DirectShow는 채택하지 않음**(interop 복잡·유지보수 부담). `MCPhoto.Capture.csproj`에만 추가(Windows 전용, 이미 `net8.0-windows`).

```csharp
// OpenCvCameraService.EnumerateDevices() 개선
public IReadOnlyList<CameraDevice> EnumerateDevices()
{
    // 1) FriendlyName 후보를 WMI로 미리 수집(best-effort, 순서 = WMI 열거 순).
    var friendlyNames = CameraNameProbe.TryGetImagingDeviceNames(_logger); // 실패 시 빈 목록

    // 2) OpenCV 인덱스 프로빙(동작 기준, 기존 로직 유지).
    var devices = new List<CameraDevice>();
    int probeOrdinal = 0;
    for (int i = 0; i < 8; i++)
    {
        try
        {
            using var cap = new VideoCapture(i, VideoCaptureAPIs.DSHOW);
            if (cap.IsOpened())
            {
                // best-effort: probeOrdinal번째 열린 장치에 WMI 이름을 순서 매핑. 없으면 폴백.
                var name = probeOrdinal < friendlyNames.Count
                    ? friendlyNames[probeOrdinal]
                    : $"Camera {i}";
                devices.Add(new CameraDevice(i, name));
                cap.Release();
                probeOrdinal++;
            }
        }
        catch { /* 장치 없음 */ }
    }
    return devices;
}
```

**`CameraNameProbe`**(신규, `MCPhoto.Capture`):
```csharp
internal static class CameraNameProbe
{
    /// <summary>WMI로 이미징/카메라 장치의 FriendlyName 조회. best-effort — 실패 시 빈 목록.</summary>
    public static IReadOnlyList<string> TryGetImagingDeviceNames(ILogger? logger = null)
    {
        try
        {
            var names = new List<string>();
            // Win32_PnPEntity 중 카메라/이미지 클래스. PNPClass='Camera' 또는 'Image'.
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name FROM Win32_PnPEntity WHERE PNPClass = 'Camera' OR PNPClass = 'Image'");
            foreach (var mo in searcher.Get())
            {
                var n = mo["Name"] as string;
                if (!string.IsNullOrWhiteSpace(n)) names.Add(n!);
            }
            return names;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "카메라 FriendlyName 조회 실패(인덱스 라벨로 폴백)");
            return Array.Empty<string>();
        }
    }
}
```

> **매핑 순서 정직성**: WMI `PNPClass='Camera'`가 UVC 웹캠을 잡지만 순서가 OpenCV와 일치한다는 보장은 없다(A2). 이 설계는 "순서 매핑 best-effort + 폴백"임을 코드 주석·문서에 명시하고, **불일치 시 기능은 인덱스로 정확**함을 보장한다. 사용자가 다대 환경에서 이름이 틀리다고 판단하면 후속으로 DShow COM 정밀 매핑을 검토(별도 항목).

#### 3.15.4 영향 범위

- **`ICameraService` 인터페이스 무변경**(`EnumerateDevices` 시그니처 동일, 반환 `CameraDevice.Name`만 개선).
- `SettingsView`/`DiagnosticsView`는 이미 `DisplayMemberPath="Name"` / `Name` 표시 → 자동 반영.
- `SettingsViewModelTests.FakeCameraService`는 이름을 생성자로 받으므로 무영향.

#### 3.15.5 엣지케이스 (#15)

| # | 상황 | 처리 |
|---|------|------|
| E1 | WMI 조회 실패/권한 없음 | 빈 목록 → 전부 `"Camera {i}"` 폴백(현행 동작 유지, 회귀 0) |
| E2 | WMI 이름 수 < OpenCV 열린 장치 수 | 부족분은 `"Camera {i}"` 폴백 |
| E3 | WMI에 가상 카메라·비디오 캡처 카드 다수 | 순서 어긋날 수 있음 → best-effort 한계 명시(A2), 기능은 인덱스로 정확 |
| E4 | 이름 중복(동일 모델 2대) | 중복 이름 허용(구분은 사용자가 테스트 버튼으로) — 필요 시 `"{name} (#{i})"` 접미 `[AUTONOMOUS-OK: 접미 추가]` |

**결정 `[AUTONOMOUS-OK]`**: 동일 이름 중복 시 인덱스 접미(`"{name} (#{index})"`) 부여해 구분성 확보(다대 환경 유용). 단일/고유 이름이면 접미 없음.

#### 3.15.6 테스트 계획 (#15)

- `CameraNameProbe`는 WMI 의존이라 단위 테스트 부적합 → 매핑 로직만 순수 함수로 분리해 테스트: `MapNamesToIndices(openIndices, friendlyNames)` 같은 순수 헬퍼로 추출 후 (인덱스 목록, 이름 목록) → `CameraDevice` 목록 매핑·폴백·중복 접미를 검증.
  ```csharp
  // 순수 매핑 헬퍼(테스트 대상) — WMI/OpenCV I/O와 분리
  internal static IReadOnlyList<CameraDevice> ComposeDevices(
      IReadOnlyList<int> openIndices, IReadOnlyList<string> friendlyNames);
  ```
- 테스트: 이름 충분/부족/빈 목록/중복 → 폴백·접미 규칙 검증.

---

### #16 업로드 진행률/재시도 UX

#### 3.16.1 사용자 결정 태그

- **`[AUTONOMOUS-OK: 합리적 기본안]`** — roadmap §2 #16이 "진행률 표시 + 재시도, IProgress 배선(UploadService→QrPopupViewModel)"으로 방향 확정. 재시도는 이미 `QrPopupViewModel.Retry`로 존재 → 진행률만 신설 + 재시도 UX 다듬기.

#### 3.16.2 IProgress 배선 (UploadService → QrPopupViewModel)

**진행률 모델**:
```csharp
// Core/Upload — 신규 (System.Windows 무의존, Core에 위치)
/// <summary>업로드 진행 단계·비율.</summary>
public sealed record UploadProgress(UploadStage Stage, double Fraction, string? Label = null);

public enum UploadStage { Photo, Timelapse, Finalizing }
```

**인터페이스 확장(하위호환)** — `IUploadService.UploadResultAsync`에 `IProgress<UploadProgress>? progress` **선택 파라미터** 추가:
```csharp
Task<ResultSession> UploadResultAsync(
    string? finalImagePath, string? timelapsePath, int retentionHours,
    string hostingBaseUrl,
    IProgress<UploadProgress>? progress = null,     // 신규(선택 — 기존 호출·테스트 무변경)
    CancellationToken ct = default);
```

> **주의**: 선택 파라미터를 `ct` **앞**에 두면 기존 위치 인자 호출(`UploadResultAsync(a,b,c,d)`)은 그대로 동작하나, `ct`를 명시하던 호출은 명명 인자 필요. 현재 `QrPopupViewModel`은 `ct` 미전달, `UploadServiceTests`/`StubUploadService`는 인터페이스 구현이므로 **시그니처 변경 시 Stub도 갱신 필수**. `QrPopupUploadTests.StubUploadService`, `UploadServiceTests`의 목 구현을 함께 수정.

**`UploadService` 구현** — 파일별 진행 보고:
```csharp
// 사진 업로드 전/후
progress?.Report(new UploadProgress(UploadStage.Photo, 0.0, "사진 업로드 중"));
var finalToken = await _client.UploadFileAsync(..., fileProgress, ct);
progress?.Report(new UploadProgress(UploadStage.Photo, 1.0));
// 타임랩스 동일(Timelapse)
// 문서 생성
progress?.Report(new UploadProgress(UploadStage.Finalizing, 1.0, "마무리 중"));
```

#### 3.16.3 파일 내부 진행률 (IFirebaseClient.UploadFileAsync)

세밀한 바이트 진행률(특히 대용량 타임랩스)을 위해 `IFirebaseClient.UploadFileAsync`에도 진행률 배선:

```csharp
Task<string> UploadFileAsync(string storagePath, string localFilePath, string contentType,
    IProgress<double>? fileProgress = null,     // 신규(선택) — 0.0~1.0
    CancellationToken ct = default);
```

**구현 (A1 가정 — GCS SDK 진행률 지원)**:
```csharp
// FirebaseClient.UploadFileAsync
var fileLen = new FileInfo(localFilePath).Length;
var options = fileProgress is null ? null : new UploadObjectOptions();
IProgress<Google.Apis.Upload.IUploadProgress>? gcsProgress = fileProgress is null ? null
    : new Progress<Google.Apis.Upload.IUploadProgress>(p =>
    {
        if (fileLen > 0) fileProgress.Report(Math.Clamp(p.BytesSent / (double)fileLen, 0, 1));
    });
await _storage!.UploadObjectAsync(obj, stream, options, ct, gcsProgress);
```

> **A1 폴백(SDK 미지원 시)**: `UploadObjectAsync`가 `IProgress<IUploadProgress>` 인자를 받지 않는 버전이면, 파일 단위 진행률은 **단계 진행률만**(사진 0→1, 타임랩스 0→1)으로 축소하고 `IFirebaseClient` 시그니처 변경을 취소한다(§3.16.2의 stage 진행률만 유지). WBS Step에서 SDK API를 먼저 확인해 분기.

#### 3.16.4 QrPopupViewModel 진행률 표시

```csharp
[ObservableProperty] private double _uploadProgress;      // 0.0~1.0(전체)
[ObservableProperty] private string _progressLabel = string.Empty; // "사진 업로드 중" 등
[ObservableProperty] private bool _isIndeterminate = true; // 세밀 진행 불가 시 무한 표시

private void OnUploadProgress(UploadProgress p)
{
    // 단계별 가중 합산(사진·타임랩스·마무리). 단순화: 3단계 균등 or 파일 크기 가중.
    IsIndeterminate = false;
    UploadProgress = ComputeOverall(p);   // 순수 함수(테스트 대상)
    ProgressLabel = p.Label ?? StageLabel(p.Stage);
}
```

`OnEnterAsync`에서:
```csharp
var progress = new Progress<UploadProgress>(OnUploadProgress);   // UI 스레드 SyncContext 캡처
var result = await _upload.UploadResultAsync(photoPath, timelapsePath,
    settings.RetentionHours, settings.HostingBaseUrl, progress);
```

> **스레딩(핵심)**: `Progress<T>`는 **생성된 스레드의 SynchronizationContext**로 콜백을 마샬링한다. `OnEnterAsync`가 UI 스레드에서 실행되므로 `new Progress<>`가 UI 컨텍스트를 캡처 → `OnUploadProgress`가 UI 스레드에서 실행되어 `[ObservableProperty]` 갱신이 안전. **백그라운드 스레드에서 `new Progress` 생성 금지**.

#### 3.16.5 QrPopupView 진행률 UI

기존 "①업로드 중" 블록을 `ProgressBar` + 라벨로 강화:
```xml
<StackPanel Visibility="{Binding IsUploading, Converter={StaticResource BoolToVis}}">
    <TextBlock Text="{Binding ProgressLabel}" Style="{StaticResource Text.Body}" HorizontalAlignment="Center" />
    <ProgressBar Height="8" Margin="0,12,0,0" Minimum="0" Maximum="1"
                 Value="{Binding UploadProgress}"
                 IsIndeterminate="{Binding IsIndeterminate}" />
</StackPanel>
```
> `ProgressBar`는 기본 컨트롤. 스타일 리소스가 없으면 기본 템플릿 사용(디자인 시스템 색과 이질감 우려 시 `Brush.Accent` Foreground 지정 — `[AUTONOMOUS-OK]`: `Foreground="{StaticResource Brush.Accent}"` + 트랙 `Background="{StaticResource Brush.Divider}"`).

#### 3.16.6 재시도 UX 강화

- 기존 `Retry`는 `OnEnterAsync` 재호출 → 진행률·상태 자동 초기화(`IsUploading=true`, `UploadProgress=0`, `IsIndeterminate=true`). 그대로 유효.
- **재시도 카운트 표시**(선택) `[AUTONOMOUS-OK: 미채택]`: 실패 안내에 재시도 횟수는 표시하지 않음(현행 비위협 안내 유지 — 스펙에 없음, 스크램블 방지).
- **취소**(선택) `[AUTONOMOUS-OK: 미채택]`: 업로드 취소 버튼은 추가하지 않음(무인 키오스크에서 취소 니즈 낮고 `ct` 배선 복잡도 대비 이득 적음). `ct`는 인터페이스에 이미 있으나 QR VM은 미전달 유지.

#### 3.16.7 엣지케이스 (#16)

| # | 상황 | 처리 |
|---|------|------|
| E1 | SDK 진행률 미지원(A1 실패) | 단계 진행률만(3단계) — `IFirebaseClient` 시그니처 변경 롤백, `UploadService`의 stage report만 유지 |
| E2 | 파일 크기 0 또는 조회 실패 | `IsIndeterminate=true` 유지(비율 계산 불가) → 무한 진행 바 |
| E3 | 사진만/타임랩스만 전송 | 존재하는 단계만 진행률 100% 기여(ComputeOverall이 실제 전송 미디어 기준 정규화) |
| E4 | 업로드 실패 중간 | 예외 → 기존 `UploadFailed` 경로(진행률 UI 숨김, 실패 안내). 진행률 상태는 다음 `OnEnterAsync`에서 리셋 |
| E5 | 진행률 콜백이 백그라운드 스레드에서 옴 | `Progress<T>`를 UI 스레드에서 생성 → 자동 마샬링(§3.16.4). 방어로 `OnUploadProgress`는 UI 상태만 변경 |
| E6 | 진행률 100% 후에도 문서 생성 지연 | `Finalizing` 단계 라벨로 "마무리 중" 표시(진행 바는 100% 또는 indeterminate) |

#### 3.16.8 테스트 계획 (#16)

- **순수 함수 `ComputeOverall(UploadProgress, 전송 미디어 구성)`**을 `QrPopupViewModel` 밖(예: `UploadProgressMath` static 또는 VM의 static)에 두어 테스트: 사진만/둘 다/타임랩스만 구성에서 단계 비율 → 전체 비율 정규화, 경계(0/1).
- `QrPopupUploadTests` 확장: `StubUploadService`가 `IProgress` 파라미터를 받아 몇 개 `Report`를 호출하도록 개선 → `OnEnterAsync` 후 `UploadProgress`가 갱신되고 `IsIndeterminate=false`인지, 성공/실패 경로에서 진행률 UI 상태 확인.
- `UploadServiceTests`: `FakeFirebaseClient`가 진행률을 시뮬레이트(옵션) → `UploadService`가 stage report를 순서대로 발행하는지 수집 검증.
- **회귀**: 기존 `UploadResultAsync(a,b,c,d)` 4인자 호출·`StubUploadService`가 컴파일되도록 시그니처 하위호환 확인.

---

## 4. 파일별 변경/신규 요약

### #13 재촬영
| 파일 | 변경 유형 | 내용 |
|------|-----------|------|
| `src/MCPhoto.Core/Settings/AppSettings.cs` | 변경 | `RetakeEnabled`/`RetakeLimit`/`PerCutRetake` 필드 + `AllowedRetakeLimits` + `Clamp` + `Clone` |
| `src/MCPhoto.Core/Settings/IniSettingsService.cs` | 변경 | `ReadInto`/`WriteFrom`에 3키 매핑 |
| `src/MCPhoto.Core/Capture/CaptureSession.cs` | 변경 | 카운터·`BeginFullRetake`/`CanFullRetake`/`CanPerCutRetake`/`MarkCutRetaken`/`WasCutRetaken`/`HasFullRetaken`/`ReplaceCut` + `Discard` 초기화 |
| `src/MCPhoto.App/SessionContext.cs` | 변경 | `int? RetakeTargetCut` + `Reset` 초기화 |
| `src/MCPhoto.Core/Navigation/SessionStateMachine.cs` | 변경 | `Forward[CutSelect]`에 `Capture` 추가 |
| `src/MCPhoto.App/ViewModels/SettingsViewModel.cs` | 변경 | `RetakeEnabled`/`RetakeLimit`/`PerCutRetake` 프로퍼티 + Load/Save + `RetakeLimitOptions` |
| `src/MCPhoto.App/Views/SettingsView.xaml` | 변경 | "촬영" 그룹에 재촬영 토글·횟수 콤보(계층 들여쓰기)·컷별 토글 |
| `src/MCPhoto.App/ViewModels/CutSelectViewModel.cs` | 변경 | `RetakeEnabled`/`CanFullRetake`/`PerCutRetakeAvailable`/`CanRetakeCut`/`RetakeSingleCut` + `Retake` 갱신 + `OnEnterAsync` 선택 복원 |
| `src/MCPhoto.App/Views/CutSelectView.xaml` | 변경 | 전체 재촬영 버튼 IsEnabled/Visibility + (USER-DECISION 승인 시)컷별 ↺ 버튼 |
| `src/MCPhoto.App/ViewModels/CaptureViewModel.cs` | 변경 | `OnEnterAsync` 단일컷 분기 + `RunSingleCutRetakeAsync` |

### #14 진단
| 파일 | 변경 유형 | 내용 |
|------|-----------|------|
| `src/MCPhoto.App/Services/ILogFolderService.cs` + `LogFolderService.cs` | 신규 | 로그 폴더 경로·열기 |
| `src/MCPhoto.App/Services/IDiagnosticsDialogService.cs` + `DiagnosticsDialogService.cs` | 신규 | 진단 다이얼로그 오픈 |
| `src/MCPhoto.App/ViewModels/DiagnosticsViewModel.cs` | 신규 | 헬스체크 상태 |
| `src/MCPhoto.App/Views/DiagnosticsWindow.xaml`(+`.cs`) | 신규 | 모달 창(디자인 시스템 리소스 사용) |
| `src/MCPhoto.App/ServiceRegistration.cs` | 변경 | `ILogFolderService`/`IDiagnosticsDialogService`/`DiagnosticsViewModel` 등록 |
| `src/MCPhoto.App/ViewModels/SettingsViewModel.cs` | 변경 | `IDiagnosticsDialogService` 주입 + `OpenDiagnostics` |
| `src/MCPhoto.App/Views/SettingsView.xaml` | 변경 | [고급] 하단 "진단·상태" 버튼 |

### #15 FriendlyName
| 파일 | 변경 유형 | 내용 |
|------|-----------|------|
| `src/MCPhoto.Capture/MCPhoto.Capture.csproj` | 변경 | `System.Management` PackageReference |
| `src/MCPhoto.Capture/CameraNameProbe.cs` | 신규 | WMI 조회 + 순수 `ComposeDevices` 매핑 |
| `src/MCPhoto.Capture/OpenCvCameraService.cs` | 변경 | `EnumerateDevices`가 `ComposeDevices` 사용 |

### #16 업로드 진행률
| 파일 | 변경 유형 | 내용 |
|------|-----------|------|
| `src/MCPhoto.Core/Upload/UploadProgress.cs` | 신규 | `UploadProgress` record + `UploadStage` |
| `src/MCPhoto.Core/Upload/IUploadService.cs` | 변경 | `IProgress<UploadProgress>?` 선택 파라미터 |
| `src/MCPhoto.Core/Upload/IFirebaseClient.cs` | 변경(조건부 A1) | `IProgress<double>?` 선택 파라미터 |
| `src/MCPhoto.Firebase/UploadService.cs` | 변경 | stage 진행 보고 |
| `src/MCPhoto.Firebase/FirebaseClient.cs` | 변경(조건부 A1) | GCS 진행률 배선 |
| `src/MCPhoto.App/ViewModels/QrPopupViewModel.cs` | 변경 | 진행률 속성 + `Progress<T>` + `ComputeOverall` |
| `src/MCPhoto.App/Views/QrPopupView.xaml` | 변경 | `ProgressBar` + 라벨 |
| 테스트 Stub(`QrPopupUploadTests`, `UploadServiceTests`) | 변경 | 시그니처 하위호환 |

---

## 5. 품질 자체 점검 (설계 확정 전)

- [x] 모든 신규 View에 대응 ViewModel·연결 방식 명확(진단=다이얼로그 서비스, 나머지=기존 오버레이/화면)
- [x] 바인딩·명령에 필요한 VM 멤버 전부 명세
- [x] 이벤트 구독 해제 경로 명시(진행률 `Progress<T>`는 GC 대상·구독 아님, 카메라 FrameReady 미사용, `WaitForStablePreviewAsync` 패턴 유지)
- [x] UI/백그라운드 경계·동기화 명확(`Task.Run` 열거·업로드, `Progress<T>` UI 스레드 캡처)
- [x] 리소스 키 충돌 없음(신규 키 최소 — `ProgressBar` 색은 기존 팔레트 재사용)
- [x] 전역 예외·성공 오인 금지 계승
- [x] ViewModel UI 무의존(bool/enum/string 상태, 색은 DataTrigger)
- [x] `wpf-developer`가 추가 질문 없이 구현 가능(단 §3.13.7 컷별 UI 배치는 USER-DECISION)
- [x] 파일 인코딩: 기존 파일 수정 시 현재 인코딩(UTF-8 BOM 관례) 보존 — developer 명세에 포함

## 6. USER-DECISION 요약

| 기능 | 태그 | 결정 필요 사항 |
|------|------|----------------|
| #13 | `[USER-DECISION-REQUIRED]` | **컷별 재촬영 버튼의 UI 배치·인터랙션**(썸네일 오버레이 ↺ vs 별도 모드). 설정 스키마·세션 카운터·전체 재촬영은 자율 진행 가능 |
| #14 | `[AUTONOMOUS-OK]` | 없음 |
| #15 | `[AUTONOMOUS-OK]` | 없음(best-effort 확정, 다대 검증은 사용자 육안) |
| #16 | `[AUTONOMOUS-OK]` | 없음 |
