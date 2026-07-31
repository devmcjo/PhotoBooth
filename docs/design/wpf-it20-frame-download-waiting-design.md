# it20 설계 — 기본 프레임 다운로드 대기 UI (프레임 선택 진입 Waiting 표시)

> 프로젝트 루트: `C:\STUDY\PROJECT\PhotoBooth`
> 선행 커밋: `4499f6c` (chore: 빌드 버전 변경 ver 1.1.9) — 테스트 베이스라인 **857 통과 / 0 실패** (실측)
> 입력: 사용자 요구사항(§0.1 원문), 현행 코드(§1 file:line 전수 확인)
> 산출물 소비자: `wpf-developer` — §11 WBS를 Step 순서대로 실행한다. `wpf-code-reviewer`가 §10 테스트 계획으로 검증한다.
> 서버(`web/functions`) 변경 **없음**. 클라 단독 이터레이션.
>
> **개정 이력**
> - rev1(초안): 화면 로컬 오버레이 + wall-clock 20초 예산 + 취소 정직성(OCE 재던짐) + 세마포어 게이트 유지
> - **rev2(현재)**: 설계 리뷰 P2(🔴1·🟠4·🟡10) 반영. 주요 변경 4건 —
>   ① `Loading` 탈출을 `finally`에서 **무조건** 확정(C1) ② 예산을 wall-clock → **무진행 + 총 상한 2단**(M1)
>   ③ 세마포어 게이트 → **단일 비행(single-flight) + 진행 중계**로 교체(M1·m10). 이에 따라 rev1의
>   취소 정직성(OCE 재던짐) 변경은 **폐기** ④ 스피너를 `x:Name` + `Storyboard.TargetName`(CaptureView 검증
>   패턴)으로 변경 + 인스턴스화 테스트(M2). §14에 리뷰 항목별 반영 대조표를 둔다.

---

## §0 개요

### 0.1 요구사항 원문 (축약 금지)

> 응용앱을 최초 실행 후 촬영 버튼을 누르면, 프레임 선택 페이지에서 다운받아져있는 Default Frame이 없는 경우
> 웹에서 다운받을 때까지 기다리게 되는데, 그때 UI상에 노출되는 Waiting 표시가 있으면 좋겠어.
> 이부분 개선 파이프라인 진행해줘

### 0.2 문제와 취지

`FrameSelectViewModel`은 이미 `IsLoading` 플래그를 갖고 있으나(`FrameSelectViewModel.cs:25`, `:72`, `:93`)
**`FrameSelectView.xaml`이 이 값을 한 번도 바인딩하지 않는다**(VF-2). 그래서 최초 실행(로컬 기본 프레임 0개)에
촬영을 누르면 사용자는 다음을 본다:

```
제목 "프레임 선택" · 빈 ListBox · 하단 [다음][취소] 버튼        ← 수 초 ~ 수십 초 동안 이 상태 그대로
```

빈 목록 + 활성 버튼은 "프레임이 없는 앱"으로 읽힌다. 진행 중이라는 신호가 0이므로 사용자는 [다음]을
누르거나(선택 없음 → 무반응, `FrameSelectViewModel.cs:208`) [취소]로 이탈한다.

게다가 대기 시간에 **상한이 없다**. 백엔드 `HttpClient` 타임아웃은 100초(VF-6)이고 프레임 이미지 다운로드용
정적 `HttpClient`는 기본값 100초(VF-7)다. 프레임 3개면 최악 `100 + 100×3 = 400초` 동안 화면이 그대로다.
이 설계는 **① 대기 신호 노출**과 **② 대기 시간 상한 + 탈출 경로**를 함께 해결한다.

### 0.3 판정 요약

| # | 쟁점 | 판정 | 근거 절 |
|---|------|------|---------|
| 1 | 대기 UI를 전역(MainWindow)에 둘까, 화면 로컬에 둘까 | **화면 로컬**(`FrameSelectView.xaml` 오버레이). 대기는 이 화면 고유 관심사이고, 프로젝트 관례가 화면별 오버레이(CaptureView·CameraTestWindow·삭제 확인 팝업)다. 전역화하면 셸에 화면별 busy 상태가 생겨 상태 소유권이 흐려진다 | §3 |
| 2 | 흰 배경 화면 위 오버레이의 대비 | CaptureView의 "scrim + 흰 글자"를 **그대로 복사하면 안 된다**. FrameSelect 배경은 `Brush.Bg`=흰색이라 40% scrim 위 흰 글자는 읽히지 않는다. 같은 파일의 삭제 확인 오버레이 관례(**scrim + `Card` + 어두운 글자**)를 따른다 | §4.2 |
| 3 | 상태 표현 | `bool IsLoading` 단일 플래그로는 "상한 초과 후 축소 진행"을 표현할 수 없다. Core에 순수 enum `FrameLoadPhase`(Loading/Ready/Degraded/Failed) + 순수 판정 함수(`Classify`/`Finalize`/`NoticeFor`)를 두고, VM은 파생 bool 4개만 노출한다 → **신규 컨버터 0** | §5 |
| 4 | 무한 대기 방지 | 4중 장치: ① **무진행 30초** ② **총 60초** 상한(둘을 `NextDeadline` 한 함수로 합성) ③ 오버레이 [기다리지 않고 시작] 즉시 탈출 ④ 화면 이탈 취소. 그리고 **`finally`가 `Phase`를 무조건 확정**하므로 로컬 폴백까지 실패해도 `Loading`에 고착되지 않는다 | §6.2·§6.6 |
| 5 | 예산을 wall-clock으로 둘까 | ❌ **둬서는 안 된다.** 최초 실행의 지배 경로는 "시작 prefetch가 다운로드 중일 때 진입"이고, wall-clock 예산은 전부 대기에 소모되어 **정상 다운로드를 Degraded로 오진**한다. 예산은 **무진행(inactivity)** 으로 정의하고 총 상한만 wall-clock으로 둔다 | §6.3 |
| 6 | prefetch와 화면 대기의 관계 | 세마포어 **줄 세우기**(현행)에서 **단일 비행 + 진행 중계**로 바꾼다. 화면은 진행 중인 prefetch에 **합류**해 실시간 진행을 받는다 → 문구 정체·오진·예산 소모가 원인 단계에서 사라진다. `Task.WaitAsync(ct)`가 호출자별 취소를 담당하므로 rev1의 **취소 정직성(OCE 재던짐) 변경은 폐기**한다 | §7.1 |
| 7 | 진행 문구를 어디서 만드나 | `IProgress<FrameCatalogProgress>`로 서비스가 국면을 보고하고 **문구는 Core의 순수 `ToLabel()`**이 만든다(`UserRole.ToLabel()` 관례). VM은 받은 문자열을 그대로 대입 → 문구가 UI 없이 단위 테스트된다 | §5.2 |
| 8 | 오프라인 부스의 조용한 폴백 | **보존한다.** 서버 조회가 즉시 실패하면 종전처럼 조용히 진행한다(안내 없음). Degraded 안내는 **대기가 실제로 중단된 경우에만**(상한 초과·건너뛰기·예외) 띄운다 | §6.4 |
| 9 | 로컬 스캔·fallback 생성 | 단일 비행 본체를 `Task.Run`으로 시작해 로컬 해석 전체를 UI 스레드에서 분리한다. `EnsureFallbackFrame`은 **파일 쓰기**를 수행하므로(리뷰 M3) 전용 lock + 임시파일 원자 교체로 경합을 없앤다 | §8.1·§7.2 |

### 0.4 설계의 핵 — `finally`가 `Phase`를 무조건 확정한다

```
FrameSelect 진입
      │
      ▼
Phase = Loading  ─────────────────────────────── 대기 오버레이 노출(스피너 + 진행 문구)
      │
      │  try { 단일 비행 합류 → 진행 보고마다 무진행 타이머 재무장 → 목록 채우기 }
      │       ├─ OperationCanceledException(상한 초과·건너뛰기) ─┐
      │       └─ 그 밖의 예외 ─────────────────────────────────┤→ SafeLocalFramesAsync()
      │                                                         │   (이 호출까지 실패하면 빈 목록으로 축퇴)
      ▼
   finally
      │  stale이 아니면 **무조건**:
      │     Phase = FrameLoadPolicy.Finalize(Phase, Frames.Count, interrupted || !completed, quiet)
      │     LoadNotice = FrameLoadPolicy.NoticeFor(Phase)
      ▼
   Ready | Degraded | Failed          ← Loading으로 남는 경로가 **존재하지 않는다**
```

rev1은 `Phase` 확정을 happy-path 말미의 명시적 대입에 두었다. 그러면 catch 블록 안의 로컬 폴백
(`EnsureFallbackFrame` → `Cv2.ImWrite`, 디스크 꽉 참·권한으로 실패 가능)이 던질 때 예외가 `OnEnterAsync`
밖으로 나가고 `src/MCPhoto.App/AppShellViewModel.cs:217-221`이 **조용히 삼켜** 전면 오버레이가
영구 고착된다(리뷰 C1). scrim이 3행 전체를 덮으므로 상단 바 [홈]만 남는 사실상 기능 정지다.

rev2는 확정을 `finally`로 옮긴다. 이로써 §4.3의 `Failed` 카드가 **실제로 도달 가능**해지고
("fallback PNG 생성 실패"가 그 카드의 존재 이유였다), "`Loading` 수명 상한"이 코드 구조로 보장된다 —
`try`에서 무엇이 터져도 `finally`는 실행된다.

---

## §1 검증된 사실 (verified facts — 전부 코드 직접 확인)

> 경로 표기 규칙: `src/MCPhoto.App/` 기준 상대 경로를 **끝까지** 적는다. 특히 `AppShellViewModel.cs`·
> `App.xaml.cs`·`ServiceRegistration.cs`·`SessionContext.cs`는 `ViewModels/` 아래가 **아니라**
> `src/MCPhoto.App/` 직하다(rev1 표기 오류 정정, 리뷰 m3).

| VF | 사실 | 근거 |
|----|------|------|
| VF-1 | 촬영 진입 경로: `HomeViewModel.Start()` → `Session.Reset(clearUser:false)` → `NavigateAsync(AppState.FrameSelect)`. 중간 화면 없음 | `src/MCPhoto.App/ViewModels/HomeViewModel.cs:17-22` |
| VF-2 | `FrameSelectViewModel.IsLoading`은 선언·대입만 있고 **`FrameSelectView.xaml`이 바인딩하지 않는다**. 전 솔루션 grep 결과 `IsLoading` 바인딩은 `Views/CameraTestWindow.xaml:66`, `Views/FrameEditorView.xaml:163`·`:175`뿐 | `src/MCPhoto.App/ViewModels/FrameSelectViewModel.cs:25`·`:72`·`:93`, `src/MCPhoto.App/Views/FrameSelectView.xaml` 전문(104줄) |
| VF-3 | 저장소에 **`Frame/` 폴더가 없다**(`ls Frame/` → 없음). csproj가 `..\..\Frame\**\*.*`를 출력 `Frame\`로 복사하지만 원본이 비어 있어 **신규 설치본에 번들 기본 프레임이 0개**다 → 최초 실행은 서버 다운로드가 유일한 경로 | `ls` 출력, `src/MCPhoto.App/MCPhoto.App.csproj:74-78` |
| VF-4 | `AppShellViewModel.NavigateInternalAsync`는 `CurrentViewModel` 대입(→ View 생성·바인딩) **후에** `OnEnterAsync()`를 await한다. 즉 로딩이 시작될 때 View는 이미 시각 트리에 있다 → 오버레이가 그려질 수 있다 | `src/MCPhoto.App/AppShellViewModel.cs:213-222` |
| VF-5 | `OnEnterAsync()`에서 나온 예외는 `try { await next.OnEnterAsync(); } catch { _logger?.LogError(...) }`가 **조용히 삼킨다** → 화면 상태가 진입 시점 그대로 굳는다 | `src/MCPhoto.App/AppShellViewModel.cs:217-221` |
| VF-6 | 앱 시작 직후 `App.OnStartup`이 기본 프레임 prefetch를 fire-and-forget한다(`_ = PrefetchDefaultFramesAsync()`). ct 없음(`GetDefaultFramesAsync()` 무인자) | `src/MCPhoto.App/App.xaml.cs:78`, `:89-101` |
| VF-7 | 백엔드 명명 `HttpClient`("backend") 타임아웃 = **100초** | `src/MCPhoto.App/ServiceRegistration.cs:110-116` |
| VF-8 | 프레임 이미지 다운로드는 `FrameCatalogService`의 정적 `HttpClient`(`new()`) — 타임아웃 기본값 **100초**, 재시도 없음 | `src/MCPhoto.App/Services/FrameCatalogService.cs:146-153` |
| VF-9 | `GetDefaultFramesAsync`는 `SemaphoreSlim(1,1)` 게이트로 직렬화된다(it10 S3-2). 게이트는 **DB 조회 + 전 이미지 다운로드 전 구간**을 감싼다 → 나중 호출자는 앞 호출이 전부 끝날 때까지 **줄 서서 대기**하며 앞 호출의 진행 상황을 알 수 없다 | `Services/FrameCatalogService.cs:24`, `:53`, `:96` |
| VF-10 | 게이트 통과 후 `_localStore.LoadPublic()`·`LoadBundleFrames()`·`EnsureFallbackFrame()`이 **동기 실행**된다. `LoadBundleFrames`는 프레임마다 `Cv2.ImRead`로 이미지를 전체 디코드한다 | `Services/FrameCatalogService.cs:57`, `:83`, `:92`, `:239-243` |
| VF-11 | `EnsureFallbackFrame()`은 `File.Exists` 검사 후 `FallbackFrameRenderer.Create()`를 호출하며, 그 안에서 `Directory.CreateDirectory` + `Cv2.ImWrite`로 **파일을 쓴다**(1200×1600 PNG). 즉 "로컬 해석"은 읽기 전용이 아니다 | `Services/FrameCatalogService.cs:229-236`, `src/MCPhoto.Capture/FallbackFrameRenderer.cs:15-38`, `src/MCPhoto.Core/Frames/DefaultFrameProvider.cs:12-13` |
| VF-12 | 로컬 폴백 체인은 **정상 동작 시 최소 1개**를 돌려준다: 로컬 공용 → 번들 → `EnsureFallbackFrame()`. 즉 프레임 0개는 VF-11의 쓰기가 실패할 때만 발생한다 | `Services/FrameCatalogService.cs:76-92` |
| VF-13 | `LocalFrameStore.WriteFrame`은 **png 먼저, `.slots` 나중에** 쓴다. `EnumerateFrames`는 `.slots`가 없으면 항목을 건너뛴다 → 다른 스레드가 쓰는 중에 로컬 스캔을 해도 반쪽 프레임이 노출되지 않는다 | `src/MCPhoto.Core/Frames/LocalFrameStore.cs:46-48`, `:108-109` |
| VF-14 | `ViewModelBase.OnLeaveAsync()`가 존재하고 `NavigateInternalAsync`가 화면 전환 시 이를 await한다 → 취소 훅으로 쓸 수 있다 | `src/MCPhoto.App/ViewModels/ViewModelBase.cs:12`, `src/MCPhoto.App/AppShellViewModel.cs:207-211` |
| VF-15 | `FrameSelect`는 **session-active** 상태 → 진입 시 유휴 감시가 시작된다(경고 `IdleWarningSeconds`=120초 + 카운트다운 10초 → 홈 복귀). 유휴 경고 오버레이는 `MainWindow.xaml`의 `ContentControl` **뒤**에 선언되어 화면 오버레이보다 위에 그려진다 | `src/MCPhoto.Core/Navigation/SessionStateMachine.cs:46-52`, `src/MCPhoto.App/AppShellViewModel.cs:30-33`·`:318-326`, `src/MCPhoto.App/MainWindow.xaml:15-17`·`:87-109` |
| VF-16 | `Progress<T>`를 UI 스레드에서 생성하면 콜백이 UI 스레드로 마샬링된다는 관례가 이미 문서화·사용 중 | `src/MCPhoto.App/ViewModels/QrPopupViewModel.cs:88-91` |
| VF-17 | stale 비동기 응답 폐기 관례가 이미 존재(`ReferenceEquals`로 진행 중 작업 동일성 확인 후에만 상태 기록) | `src/MCPhoto.App/AppShellViewModel.cs:174-180` |
| VF-18 | 화면 로컬 오버레이 관례: `Grid Grid.RowSpan="3" Background="{StaticResource Brush.Scrim}"` + `Visibility` 바인딩 + 중앙 `Border Style="{StaticResource Card}" Background="{StaticResource Brush.Bg}"` | `Views/FrameSelectView.xaml:57-80`(삭제 확인 팝업) |
| VF-19 | `Brush.Scrim` = `#66241F2B`(잉크 40% 알파). `FrameSelectView`의 최하단 배경은 `Brush.Bg`(=`#FFFFFF`) → scrim 위에 흰 글자(`Brush.OnAccent`)를 쓰면 대비가 무너진다. CaptureView가 흰 글자를 쓸 수 있는 이유는 그 화면 배경이 카메라 프리뷰·`Brush.CaptureBg`라서다 | `Themes/Colors.xaml:31`·`:53`, `Themes/Brushes.xaml:38`·`:59`, `Views/CaptureView.xaml:70-96`, `Views/FrameSelectView.xaml:7` |
| VF-20 | 회전 스피너 마크업이 `Views/CaptureView.xaml:74-93`에만 인라인 존재(공유 리소스 아님). 형태는 `RotateTransform x:Name="SpinnerRotate"` + `EventTrigger RoutedEvent="Loaded"` + `Storyboard.TargetName="SpinnerRotate"` `TargetProperty="Angle"`. **이 프로젝트에서 실제 동작이 확인된 유일한 스피너 형태다** | `Views/CaptureView.xaml:74-93` |
| VF-21 | `Themes/Controls.xaml`에 `Spinner.*` 접두 리소스 키가 **0개**(전체 x:Key 18개 목록 확인) → 키 충돌 없음. `ControlTemplate` 안에서 이름으로 요소를 겨냥하는 관례는 이미 존재(`Setter TargetName="Bd"`) | `grep -n "x:Key" Themes/Controls.xaml` 출력, `Themes/Controls.xaml:309`·`:317` |
| VF-22 | `XamlResourceTests`가 `FrameSelectView.xaml`의 테마 StaticResource 해석을 headless로 이미 검증한다(`[InlineData("FrameSelectView.xaml")]`). 다만 **바인딩 Path는 검사하지 않는다** — 별도로 "VM 멤버 이름이 XAML 텍스트에 존재하는지" 확인하는 관례가 있다 | `tests/MCPhoto.Tests/XamlResourceTests.cs:245-265`, `:297-322`(`FrameEditor_Popup_Bindings_Resolve_On_Editor_Vm`) |
| VF-23 | 순수 정책 클래스 + 전용 테스트 파일 관례 존재: `CutCountPolicy`·`QrDeliveryPolicy`·`DisplayApplyPolicy`·`FrameEditPolicy`. 한글 표시 라벨을 Core 순수 함수로 두는 관례도 존재(`UserRole.ToLabel()`) | `src/MCPhoto.Core/Settings/`, `src/MCPhoto.Core/Models/UserRole.cs`, `tests/MCPhoto.Tests/CutCountPolicyTests.cs` |
| VF-24 | `FramePickerViewModel.LoadAsync`는 `_catalog.GetDefaultFramesAsync(ct)`를 호출하며 이미 `catch (OperationCanceledException)`을 갖는다. **다만 관측 동작은 바뀔 수 있다** — 종전에는 취소가 서비스 내부에서 삼켜져 완전/부분 목록이 반환됐고, 취소가 경계에서 전파되면 목록 없이 조용히 종료된다. 취소 계기가 모달 종료·재오픈뿐이고 그때 화면이 사라지므로 수용 가능(rev1의 "영향받지 않는다"는 과장 — 리뷰 m4 정정) | `src/MCPhoto.App/ViewModels/FramePickerViewModel.cs:46-80` |
| VF-25 | `MCPhoto.App`은 테스트 어셈블리에 internal을 공개한다 → `internal` 테스트 이음새(seam)를 쓸 수 있다 | `src/MCPhoto.App/MCPhoto.App.csproj:40` (`<InternalsVisibleTo Include="MCPhoto.Tests" />`) |
| VF-26 | 대상 파일 전부 **UTF-8 no BOM**(`head -c3` = `<Use`/`<Res`/`using`/`namespace`) | `Views/FrameSelectView.xaml`, `Themes/Controls.xaml`, `ViewModels/FrameSelectViewModel.cs`, `src/MCPhoto.Core/Frames/FrameNaming.cs` |
| VF-27 | 대상 프레임워크 `net8.0-windows`, `LangVersion 12.0`, `ImplicitUsings`·`Nullable` enable → `Task.WaitAsync(CancellationToken)`(.NET 6+)·`File.Move(src,dst,overwrite)`(.NET Core 3+)·컬렉션 식 사용 가능 | `Directory.Build.props`, `src/MCPhoto.App/MCPhoto.App.csproj:5` |
| VF-28 | MS.DI는 **기본값 있는 미등록 생성자 파라미터를 허용**한다 — `FrameCatalogService`가 `ILogger<...>? logger = null, Func<...>? downloadImage = null`을 갖고 `AddSingleton<FrameCatalogService>()`로 등록되어 앱이 동작 중이다. 같은 형태의 테스트 이음새를 VM에도 쓸 수 있다 | `Services/FrameCatalogService.cs:32-44`, `ServiceRegistration.cs:98` |
| VF-29 | 테스트 베이스라인 **857 통과 / 0 실패**(`dotnet test MCPhoto.sln -c Debug --nologo`, 실측). HEAD = `4499f6c` | 명령 출력 |

---

## §2 미검증 가정 (open assumptions) — 검증 단계 매핑

| A | 가정 | 검증 단계 |
|---|------|-----------|
| A-1 | 대기 오버레이가 **첫 await 이전에 페인트된다**. VF-4로 시각 트리 생성은 확인했으나, 단일 비행 시작이 동기 구간을 갖는다면 첫 렌더 전에 UI 스레드가 점유될 수 있다 | Step 2에서 단일 비행 본체를 `Task.Run`으로 시작(동기 구간 = lock 진입·리스트 추가뿐) + Step 8 M1 수동 관측 |
| A-2 | **무진행 30초**가 실사용 회선에서 정상 진행을 잘라내지 않는다. 진행 보고는 단계 전환 단위이므로, 단일 프레임 이미지 다운로드가 30초를 넘기면 잘린다 | Step 8 M2에서 실서버 진입 로그(`기본 프레임 캐시: …` 간격)를 관측. 상한은 `FrameLoadPolicy` 상수 2개이므로 관측 후 조정 가능(코드 1줄 + Step 1 테스트 기대값) |
| A-3 | `Task.Run` 오프로드 후에도 `LocalFrameStore`/`FallbackFrameRenderer`가 스레드 안전하다(파일 I/O + OpenCV 디코드만, UI 타입 미참조). fallback **쓰기** 경합은 §7.2의 lock + 원자 교체로 제거 | Step 4의 fallback 동시 호출 테스트 + 기존 `FallbackFrameTests` 통과 |
| A-4 | 오버레이의 scrim + `Card` 조합에서 `Button.Ghost`(`Brush.Text.Secondary` 글자)가 읽힌다 — `Card`가 `Brush.Bg` 불투명 배경을 깔기 때문 | Step 7 XAML 작성 시 카드 배경 명시 + Step 8 M1 수동 관측 |
| A-5 | 총 상한 60초가 유휴 경고(120초, VF-15)보다 짧아 정상적으로는 두 오버레이가 겹치지 않는다. 단 `Failed`/`Degraded` 상태로 방치하면 종전 안전망대로 유휴 경고가 위에 겹치고 홈으로 복귀한다(의도된 최종 탈출, §4.6) | Step 1의 `MaxTotalWait_Is_Below_Idle_Warning` 테스트 + Step 8 M6 |
| A-6 | `ControlTemplate` 안에서 `RotateTransform`에 `x:Name`을 달고 `Storyboard.TargetName`으로 겨냥하면 Freezable이 템플릿 namescope에 등록되어 **동결되지 않고** 애니메이션이 가능하다(리뷰 M2가 지적한 `Cannot animate … on an immutable object instance` 회피) | Step 5의 `Spinner_Ring_Transform_Is_Animatable` 테스트가 템플릿을 실제 인스턴스화해 `IsFrozen == false` + `Storyboard.Begin()` 무예외를 확인 |
| A-7 | 단일 비행 교체 후에도 "프레임당 다운로드 1회" 불변이 유지된다(합류한 두 호출이 한 번의 다운로드 패스를 공유) | Step 2에서 기존 `Concurrent_Calls_Download_Each_Frame_Once`·`Cache_Miss_Downloads_And_Dedups`를 **수정 없이** 통과시켜 확인 |

---

## §3 쟁점 1 판정 — 대기 UI의 소유 위치

세 후보를 검토했다.

| 후보 | 판정 |
|------|------|
| (a) `MainWindow.xaml`에 전역 busy 오버레이 + `AppShellViewModel.IsBusy` | ❌ 셸이 화면별 로딩 사정을 알아야 한다. 유휴 경고 오버레이(`AppShellViewModel.cs:36`)는 **셸 소유 관심사**라 전역이 맞지만, 프레임 다운로드는 FrameSelect 고유 사정이다. 전역화하면 어느 화면이 busy를 켰는지 추적이 필요해지고, 해제 누락 시 앱 전체가 잠긴다 |
| (b) `FrameSelectView.xaml` 로컬 오버레이 ✅ | 관례 일치(`Views/CaptureView.xaml:70-97` 카메라 초기화 오버레이, `Views/CameraTestWindow.xaml:64-69`, 같은 파일의 삭제 확인 팝업 VF-18). 상태 소유자(`FrameSelectViewModel`)와 표시 위치가 같아 해제 누락이 구조적으로 불가능하다(VM 생명주기 = 화면 생명주기, Transient 등록 `ServiceRegistration.cs:196`) |
| (c) 별도 모달 `Window` | ❌ 키오스크 전체화면에서 모달 창은 관례가 아니다(모달은 카메라 테스트·PIN 프롬프트처럼 **관리자 조작**에만 씀). 손님 흐름은 전부 인-화면 오버레이 |

**(b) 채택.** 부수 효과로 상단 바(`MainWindow`의 `TopBar`)는 계속 보인다 — 로딩 중에도 [홈]·[설정] 접근이
유지되므로 탈출 경로가 하나 더 확보된다(`IsTopBarVisible`은 FrameSelect에서 true).

**로딩 중 [설정] 진입 → 복귀 경로**(리뷰 m10): 복귀는 `ReturnFromOverlay()` → `NavigateInternalAsync` →
VM **재생성**이므로 로딩이 처음부터 다시 시작된다. rev1(세마포어)에서는 이것이 "또 한 번 줄 서서 대기"였다.
rev2의 단일 비행에서는 **진행 중인 작업에 즉시 합류하고 최근 국면을 replay** 받으므로(§7.1) 재진입 비용이
"현재 국면 문구를 즉시 표시"로 줄어든다. 무진행 타이머도 replay 보고로 즉시 재무장된다.

---

## §4 시각 설계

### 4.1 대기 오버레이 구성

```
┌──────────────────────────── scrim(40% 잉크) ────────────────────────────┐
│                                                                          │
│                    ┌──────── Card (Brush.Bg) ────────┐                   │
│                    │            ◌  (Spinner.Ring)     │                  │
│                    │   기본 프레임 내려받는 중… (1/3)  │ ← LoadingMessage │
│                    │   처음 실행할 때는 기본 프레임을  │ ← 고정 보조 문구 │
│                    │   서버에서 한 번 내려받습니다.    │                  │
│                    │      [ 기다리지 않고 시작 ]       │ ← Button.Ghost   │
│                    └──────────────────────────────────┘                  │
└──────────────────────────────────────────────────────────────────────────┘
```

- **위치**: `Grid.RowSpan="3"`로 3행 전체를 덮는다(삭제 확인 팝업과 동일). XAML 선언 순서상 **삭제 확인
  오버레이보다 뒤(= 시각적으로 위)** 에 둔다.
- **hit test**: scrim `Grid`가 `Background`를 가지므로 아래 `ListBox`·[다음]·[취소]로의 클릭이 차단된다.
  VM 가드는 §5.4에서 이중으로 둔다.
- **보조 문구**는 정적 텍스트다. "왜 기다리는가"를 설명해 최초 실행 1회성 대기임을 알린다.
- **스피너 `Control`에는 `Focusable="False" IsTabStop="False"`를 명시**한다(리뷰 m7) — 키오스크 탭 순서에
  장식 요소가 끼지 않게 한다.

### 4.2 대비 판정 — CaptureView 패턴을 복사하지 않는 이유

`Views/CaptureView.xaml:94`는 scrim 위에 `Foreground="{StaticResource Brush.OnAccent}"`(흰색) 글자를 쓴다.
그 화면의 하위 배경은 카메라 프리뷰/`Brush.CaptureBg`(어두움)라서 성립한다.

`FrameSelectView`의 하위 배경은 `Brush.Bg`=`#FFFFFF`다(`Views/FrameSelectView.xaml:7`). scrim `#66241F2B`를
흰 배경에 합성하면 밝은 회자색이 되어 **흰 글자는 읽히지 않는다**(VF-19). 따라서 같은 파일의 삭제 확인
오버레이와 동일하게 **불투명 `Card`를 깔고 그 안에 기본 글자색을 쓴다.**

> 테마 토큰만 참조 — 카드 배경 `Brush.Bg`, 본문 `Text.Body`, 보조 `Brush.Text.Muted`, 스피너 `Brush.Accent`.
> 신규 색 토큰 **0개**.

### 4.3 실패 카드(Failed)

```
┌──────────────────────────── scrim ────────────────────────────┐
│              ┌────────── Card (Brush.Bg) ──────────┐          │
│              │  프레임을 준비하지 못했습니다        │ Text.H2  │
│              │  {LoadNotice}                        │ Text.Body│
│              │     [ 다시 시도 ]   [ 메인으로 ]     │          │
│              └──────────────────────────────────────┘          │
└────────────────────────────────────────────────────────────────┘
```

도달 조건은 **프레임 0개**다. VF-12에 따라 정상 동작에서는 fallback 1개가 항상 생기므로, 실제로는
VF-11의 쓰기가 실패할 때(디스크 꽉 참·권한·경로 잠김) 도달한다. rev1은 이 상태를 정의만 하고
**구조적으로 도달 불가능**하게 만들어 두었다(리뷰 C1) — rev2의 `finally` 확정 + `SafeLocalFramesAsync`
축퇴로 실제 도달 가능해졌고, Step 6에 그 도달을 고정하는 테스트(T-26)를 둔다.

### 4.4 축소 진행 안내(Degraded)

목록은 정상 표시하고, 하단 버튼 줄 **위**에 인라인 한 줄을 띄운다(기존 `DeleteNotice` 줄과 같은 자리 규약).

```
서버 프레임을 모두 가져오지 못해 지금 준비된 프레임으로 진행합니다.   [ 다시 시도 ]
```

사용자가 "프레임이 원래 이것뿐"으로 오인하지 않게 하고, 단일 비행 본체는 계속 진행하므로(§6.3)
잠시 뒤 [다시 시도]가 성공할 가능성이 높다는 점을 행동으로 유도한다.

### 4.5 공유 스피너 리소스 `Spinner.Ring` (rev2에서 형태 변경)

`Themes/Controls.xaml`에 `ControlTemplate x:Key="Spinner.Ring" TargetType="Control"`을 신설한다.
사용: `<Control Template="{StaticResource Spinner.Ring}" Width="56" Height="56" Focusable="False" IsTabStop="False" />`

**rev1의 판정을 철회한다.** rev1은 "`ControlTemplate` namescope에서 Freezable 이름 해석이 취약하다"는
근거 없는 이유로 `x:Name`을 피하고 속성 경로(`(UIElement.RenderTransform).(RotateTransform.Angle)`)
애니메이션을 택했다. 실제 WPF 제약은 반대 방향이다(리뷰 M2):

- `ResourceDictionary`의 `ControlTemplate`에 인라인 선언된 Freezable은 템플릿 Seal 시 **동결되어 공유**될 수
  있고, 그 상태에서 속성 경로 애니메이션은 런타임에
  `InvalidOperationException: Cannot animate '(0).(1)' on an immutable object instance`를 던진다.
- 이 예외는 UI 스레드에서 발생 → `App.xaml.cs`의 `DispatcherUnhandledException` → `TryReturnHome()` →
  **손님이 촬영을 누르면 홈으로 튕긴다.** 대기 UI를 넣으려다 진입 자체를 깨뜨리는 최악의 실패 모드다.
- `x:Name` + `Storyboard.TargetName`은 Freezable을 템플릿 namescope에 등록해 mutable하게 유지하는 표준
  회피책이며, **이 프로젝트에 동작 사례가 있다**(VF-20 `CaptureView.xaml:74-93`). `ControlTemplate` 안에서
  이름으로 요소를 겨냥하는 관례도 이미 있다(VF-21 `Setter TargetName="Bd"`).

```xml
<!-- ══════════════ Spinner.Ring (대기 스피너 공유 리소스, it20) ══════════════ -->
<!-- 사용: <Control Template="{StaticResource Spinner.Ring}" Width="56" Height="56"
                   Focusable="False" IsTabStop="False" />
     ⚠️ RotateTransform에 x:Name을 달고 Storyboard.TargetName으로 겨냥한다 —
        속성 경로((UIElement.RenderTransform).(RotateTransform.Angle)) 방식은 템플릿 Seal로 동결된
        Freezable에서 "Cannot animate on an immutable object instance"를 던진다(설계 §4.5).
        이 형태는 CaptureView.xaml:74-93에서 동작이 확인된 패턴과 동일하다.
     ⚠️ 시작은 Loaded(발화 보장), 정지/재개는 IsVisible 트리거가 담당한다 — 오버레이가 Collapsed되면
        Forever 애니메이션이 계속 도는 낭비를 막는다. -->
<ControlTemplate x:Key="Spinner.Ring" TargetType="Control">
    <Ellipse x:Name="SpinnerRing"
             Stroke="{StaticResource Brush.Accent}" StrokeThickness="5"
             StrokeDashArray="4 2" RenderTransformOrigin="0.5,0.5">
        <Ellipse.RenderTransform>
            <RotateTransform x:Name="SpinnerRotate" Angle="0" />
        </Ellipse.RenderTransform>
    </Ellipse>
    <ControlTemplate.Triggers>
        <EventTrigger RoutedEvent="FrameworkElement.Loaded">
            <BeginStoryboard x:Name="SpinnerSpin">
                <Storyboard>
                    <DoubleAnimation Storyboard.TargetName="SpinnerRotate"
                                     Storyboard.TargetProperty="Angle"
                                     From="0" To="360" Duration="0:0:1"
                                     RepeatBehavior="Forever" />
                </Storyboard>
            </BeginStoryboard>
        </EventTrigger>
        <Trigger Property="IsVisible" Value="False">
            <Trigger.EnterActions>
                <PauseStoryboard BeginStoryboardName="SpinnerSpin" />
            </Trigger.EnterActions>
            <Trigger.ExitActions>
                <ResumeStoryboard BeginStoryboardName="SpinnerSpin" />
            </Trigger.ExitActions>
        </Trigger>
    </ControlTemplate.Triggers>
</ControlTemplate>
```

시작 트리거를 `Loaded`로 두는 이유: rev1은 `Trigger Property="IsVisible" Value="True"`의 `EnterActions`로
시작하려 했으나, 템플릿이 적용되는 시점에 이미 `IsVisible=true`면 **전이가 관측되지 않아 발화하지 않을
수 있다**(리뷰 M2 부수 지적). `Phase` 초기값이 `Loading`이라 오버레이는 처음부터 Visible이므로 정확히 그
상황이다. `Loaded`는 발화가 보장되고, 정지 요구는 `IsVisible=False` 트리거의 `PauseStoryboard`가 담당한다.

**비목표**: `Views/CaptureView.xaml`의 인라인 스피너를 이 리소스로 마이그레이션하지 **않는다**. 촬영 화면은
이번 변경의 리스크 경계 밖이다(단일 리스크 원칙).

### 4.6 유휴 경고와의 시각 중첩 (리뷰 m8)

유휴 경고 오버레이는 `MainWindow.xaml`의 `ContentControl`(화면 스왑) **뒤**에 선언되어 있어 화면 로컬
오버레이보다 **위**에 그려진다(VF-15). 따라서:

| 동시 상태 | 시각 결과 | 판정 |
|-----------|-----------|------|
| `Loading` + 유휴 경고 | 정상 경로에서는 발생하지 않는다(총 상한 60초 < 경고 120초, A-5) | — |
| `Degraded`/`Failed` + 유휴 경고 | scrim이 두 겹(≈64% 잉크)이 되고 유휴 경고 카드가 최상단에 선명히 읽힌다. 10초 후 홈 복귀 | **의도된 동작.** `Failed`에서 사용자가 아무 조작도 하지 않을 때의 최종 탈출 경로다. 워치독을 건드리지 않는다(비목표) |

`Loading` 중에는 `NotifyUserActivity`를 인위적으로 호출하지 **않는다** — 그렇게 하면 로딩이 실제로 멎었을 때
워치독까지 무력화되어 최종 안전망이 사라진다.

---

## §5 상태 모델

### 5.1 Core 순수 정책 (신규 파일 전문 — 그대로 작성 가능)

```csharp
// src/MCPhoto.Core/Frames/FrameLoadPolicy.cs (신규) — UTF-8 no BOM
namespace MCPhoto.Core.Frames;

/// <summary>
/// 프레임 선택 화면의 목록 로딩 국면. UI 없이 판정·테스트되도록 Core에 둔다. (it20)
/// 0번 값이 <see cref="Loading"/>인 것은 의도다 — ViewModel 초기 상태가 안전하게 대기로 시작한다.
/// </summary>
public enum FrameLoadPhase
{
    /// <summary>서버·로컬에서 목록을 준비하는 중(대기 오버레이 노출).</summary>
    Loading,
    /// <summary>정상 완료. 목록 표시, 안내 없음.</summary>
    Ready,
    /// <summary>대기가 중단되어 로컬 프레임만으로 진행. 목록 표시 + 인라인 안내 + [다시 시도].</summary>
    Degraded,
    /// <summary>쓸 수 있는 프레임이 0개. 전면 실패 카드 + [다시 시도]/[메인으로].</summary>
    Failed
}

/// <summary>
/// 기본 프레임 로딩 대기 정책(순수 함수 — UI·서비스 인스턴스 무의존). (it20)
/// 최초 실행은 로컬에 기본 프레임이 없어 서버 다운로드를 기다린다(설계 §0.2). 그 대기에
/// **상한**과 **결과 판정**과 **안내 문구**를 부여하는 것이 이 클래스의 책임이다.
/// </summary>
public static class FrameLoadPolicy
{
    /// <summary>
    /// 무진행(inactivity) 상한(초). 진행 보고가 이 시간 동안 한 번도 없으면 대기를 포기한다.
    /// wall-clock 예산을 쓰지 않는 이유(설계 §6.3): 최초 실행의 지배 경로는 시작 prefetch가 이미
    /// 다운로드 중일 때 진입하는 것이라, wall-clock 예산은 정상 진행 중인 다운로드를 잘라
    /// "실패했다"는 거짓 안내를 띄운다. 단계 전환이 곧 진행의 증거이므로 무진행으로 정의한다.
    /// </summary>
    public const int NoProgressTimeoutSeconds = 30;

    /// <summary>
    /// 총 대기 상한(초). 아무리 진행 중이어도 손님을 이보다 길게 세워두지 않는다.
    /// 유휴 경고(AppShellViewModel.IdleWarningSeconds 기본 120초)보다 짧아야 한다 — 대기 중에
    /// "잠시 자리를 비우셨나요?" 팝업이 겹치지 않게 한다(설계 §4.6).
    /// </summary>
    public const int MaxTotalWaitSeconds = 60;

    /// <summary>유휴 경고 기본값(초). 상한 불변식을 Core 테스트에서 확인하기 위한 참조 상수 —
    /// 진실원은 <c>AppShellViewModel.IdleWarningSeconds</c>이며 이 값은 그 기본값의 사본이다.</summary>
    public const int IdleWarningReferenceSeconds = 120;

    public static TimeSpan NoProgressTimeout => TimeSpan.FromSeconds(NoProgressTimeoutSeconds);
    public static TimeSpan MaxTotalWait => TimeSpan.FromSeconds(MaxTotalWaitSeconds);

    /// <summary>
    /// 지금부터 취소까지 남겨 둘 시간. 무진행 상한과 총 상한 중 **먼저 오는 쪽**을 돌려준다.
    /// 진행 보고마다 호출해 <c>CancellationTokenSource.CancelAfter</c>를 재무장한다.
    /// 0 이하를 돌려주면 즉시 취소해야 한다(총 상한 도달).
    /// </summary>
    /// <param name="elapsed">이 로딩이 시작된 뒤 흐른 시간.</param>
    public static TimeSpan NextDeadline(TimeSpan elapsed)
    {
        var remainingTotal = MaxTotalWait - elapsed;
        if (remainingTotal <= TimeSpan.Zero) return TimeSpan.Zero;
        return remainingTotal < NoProgressTimeout ? remainingTotal : NoProgressTimeout;
    }

    /// <summary>
    /// 로딩 결과 판정.
    /// frameCount=0 → Failed(쓸 프레임이 없다).
    /// waitInterrupted=true(상한 초과·사용자 건너뛰기·예외) → Degraded.
    /// 그 외 → Ready. **서버 조회 실패 자체는 Degraded가 아니다** — 오프라인 부스는 로컬 캐시로
    /// 조용히 운영되는 것이 종전 동작이며(it10 폴백), 안내를 띄우면 매 진입 노이즈가 된다(설계 §6.4).
    /// </summary>
    public static FrameLoadPhase Classify(int frameCount, bool waitInterrupted)
        => frameCount <= 0 ? FrameLoadPhase.Failed
         : waitInterrupted ? FrameLoadPhase.Degraded
         : FrameLoadPhase.Ready;

    /// <summary>
    /// 로딩 종료 시 확정할 국면. ViewModel의 <c>finally</c>가 **무조건** 이 함수로 국면을 닫는다
    /// (설계 §0.4·§6.6 — Loading 고착 방지).
    /// quiet=true(삭제 후 조용한 재스캔)면 종전 국면을 유지한다. 단 두 경우는 예외 없이 갱신한다:
    /// 프레임이 0개면 Failed(빈 목록 + 활성 [다음]은 이 설계가 없애려는 상태),
    /// 종전이 Failed였는데 프레임이 생겼으면 Ready로 회복.
    /// </summary>
    /// <param name="current">종료 직전 국면.</param>
    /// <param name="frameCount">최종 목록 개수.</param>
    /// <param name="waitInterrupted">대기가 중단됐거나 정상 완료에 도달하지 못했는지.</param>
    /// <param name="quiet">조용한 재스캔(오버레이·안내를 띄우지 않는 계기)인지.</param>
    public static FrameLoadPhase Finalize(
        FrameLoadPhase current, int frameCount, bool waitInterrupted, bool quiet)
    {
        if (frameCount <= 0) return FrameLoadPhase.Failed;
        if (!quiet) return Classify(frameCount, waitInterrupted);
        return current == FrameLoadPhase.Failed ? FrameLoadPhase.Ready : current;
    }

    /// <summary>국면별 사용자 안내 문구(Ready는 빈 문자열). UI 없이 테스트 가능하도록 Core에 둔다.</summary>
    public static string NoticeFor(FrameLoadPhase phase) => phase switch
    {
        // 구현 시 정정(리뷰 N9): 총 상한 초과는 진행 중인 정상 다운로드도 자르므로 일부는 이미 받았을 수 있다.
        // "가져오지 못해"는 전부 실패한 것처럼 읽혀 부정확 → "모두 가져오지 못해 / 지금 준비된"으로 다듬었다.
        FrameLoadPhase.Degraded => "서버 프레임을 모두 가져오지 못해 지금 준비된 프레임으로 진행합니다.",
        FrameLoadPhase.Failed => "사용할 수 있는 프레임이 없습니다. 네트워크를 확인하고 다시 시도해 주세요.",
        _ => string.Empty
    };
}
```

### 5.2 진행 보고 표현 (신규 파일 전문)

```csharp
// src/MCPhoto.Core/Frames/FrameCatalogProgress.cs (신규) — UTF-8 no BOM
namespace MCPhoto.Core.Frames;

/// <summary>기본 프레임 준비 단계. 사용자에게 보이는 문구의 유일한 분기 축. (it20)</summary>
public enum FrameCatalogPhase
{
    /// <summary>설치·캐시된 로컬 프레임을 확인한다.</summary>
    ResolvingLocal,
    /// <summary>서버에서 기본 프레임 목록을 조회한다.</summary>
    QueryingServer,
    /// <summary>프레임 이미지를 내려받는다(<see cref="FrameCatalogProgress.Index"/>/<see cref="FrameCatalogProgress.Total"/>).</summary>
    DownloadingImage,
    /// <summary>모든 준비가 끝났다(마지막 보고 — 늦게 합류한 구독자의 replay용).</summary>
    Completed
}

/// <summary>
/// 기본 프레임 준비 진행 상황. <c>IProgress&lt;FrameCatalogProgress&gt;</c>로 보고된다. (it20)
/// 표시 문구를 <see cref="ToLabel"/> 순수 함수로 함께 제공한다 — ViewModel이 문자열을 조립하지 않으므로
/// 문구가 UI 없이 단위 테스트된다(<c>UserRole.ToLabel()</c> 관례와 동형).
/// </summary>
public readonly record struct FrameCatalogProgress(
    FrameCatalogPhase Phase,
    int Index = 0,
    int Total = 0)
{
    /// <summary>로딩 시작 직후(아직 어떤 보고도 없을 때) 보여줄 기본 문구.</summary>
    public const string StartLabel = "기본 프레임을 준비하고 있어요…";

    /// <summary>이 진행 상황의 한국어 표시 문구. Total&gt;0이면 "(n/m)" 카운터를 덧붙인다.</summary>
    public string ToLabel() => Phase switch
    {
        FrameCatalogPhase.ResolvingLocal => "설치된 프레임을 확인하는 중…",
        FrameCatalogPhase.QueryingServer => "서버에서 기본 프레임 목록을 확인하는 중…",
        FrameCatalogPhase.DownloadingImage => Total > 0
            ? $"기본 프레임 내려받는 중… ({Index}/{Total})"
            : "기본 프레임 내려받는 중…",
        FrameCatalogPhase.Completed => "프레임 목록을 정리하는 중…",
        _ => StartLabel
    };
}
```

**rev1의 `WaitingForOther` 국면을 삭제했다.** 단일 비행(§7.1)에서는 "다른 호출을 기다리는" 상태가
존재하지 않는다 — 합류하는 즉시 진행 중인 실제 국면이 replay된다. rev1의 문구 정체 문제(리뷰 M1)가
표현 층에서부터 사라진다.

> **프레임 이름을 문구에 넣지 않는 결정**: DB 기본 프레임 이름은 운영자가 자유 입력하며 길이 제한이 없다
> (`FrameNaming.IsFileNameSafe`는 파일시스템 금지문자만 검사). 이름을 넣으면 카드 폭을 넘기거나 줄바꿈으로
> 오버레이 높이가 요동친다. 진행은 `(n/m)` 카운터로만 표현한다.

### 5.3 ViewModel 표면

```csharp
// FrameSelectViewModel — 신규/변경 멤버만
[ObservableProperty]
[NotifyPropertyChangedFor(nameof(IsLoading))]
[NotifyPropertyChangedFor(nameof(IsLoadFailed))]
[NotifyPropertyChangedFor(nameof(IsDegraded))]
[NotifyPropertyChangedFor(nameof(IsInteractive))]
private FrameLoadPhase _phase = FrameLoadPhase.Loading;   // 진입 즉시 로딩(빈 목록 깜빡임 방지)

/// <summary>대기 오버레이 노출 조건. 종전 IsLoading 플래그를 Phase 파생으로 대체한다(단일 진실 원천).</summary>
public bool IsLoading => Phase == FrameLoadPhase.Loading;
public bool IsLoadFailed => Phase == FrameLoadPhase.Failed;
public bool IsDegraded => Phase == FrameLoadPhase.Degraded;

/// <summary>목록·버튼 조작을 허용하는 국면인지. 커맨드 가드의 단일 기준(설계 §5.4).</summary>
public bool IsInteractive => Phase is FrameLoadPhase.Ready or FrameLoadPhase.Degraded;

/// <summary>오버레이의 진행 문구. FrameCatalogProgress.ToLabel() 결과를 그대로 담는다.</summary>
[ObservableProperty] private string _loadingMessage = FrameCatalogProgress.StartLabel;

/// <summary>로딩 결과 안내(Degraded·Failed에서만 비어 있지 않다). FrameLoadPolicy.NoticeFor 결과.</summary>
[ObservableProperty] private string _loadNotice = string.Empty;
```

`IsLoading`을 **필드에서 파생 프로퍼티로 바꾼다**(종전 `[ObservableProperty] private bool _isLoading` 제거).
두 개를 병존시키면 "로딩 중인데 Phase는 Ready" 같은 모순 상태가 만들어질 수 있다.
외부 대입 지점은 `ReloadFramesAsync` 내부뿐이고 XAML 바인딩이 0개다(VF-2) → 컴파일 오류로 즉시 드러난다.

> ⚠️ `FramePickerViewModel.IsLoading`(`ViewModels/FramePickerViewModel.cs:27`)은 **건드리지 않는다** —
> 편집기 모달의 별개 상태이며 `Views/FrameEditorView.xaml:175`가 바인딩 중이다.

**테스트 이음새**(리뷰 M4): 상한을 줄여 타임아웃 경로를 단위 테스트할 수 있어야 한다. `FrameCatalogService`가
`downloadImage`를 선택 생성자 인자로 노출하는 관례(VF-28)를 그대로 따른다.

```csharp
// 생성자 마지막 선택 인자로 추가(DI는 기본값을 그대로 쓴다 — VF-28)
public FrameSelectViewModel(AppShellViewModel shell, FrameCatalogService catalog,
    ILocalFrameStore localStore, IFrameRepository repository,
    ILogger<FrameSelectViewModel>? logger = null,
    Func<TimeSpan, TimeSpan>? loadDeadline = null)
{
    …
    _loadDeadline = loadDeadline ?? FrameLoadPolicy.NextDeadline;
}

private readonly Func<TimeSpan, TimeSpan> _loadDeadline;
```

### 5.4 국면별 UI 게이트 매트릭스

| Phase | 대기 오버레이 | 실패 카드 | 인라인 안내 | 목록 | [다음] | [만들기]/[선택 편집]/삭제 ✕ |
|-------|:-------------:|:---------:|:-----------:|:----:|:------:|:---------------------------:|
| `Loading` | **표시** | – | – | (scrim 아래) | scrim + VM 가드 | scrim + VM 가드 |
| `Ready` | – | – | – | 표시 | 활성 | 권한대로 |
| `Degraded` | – | – | **표시** + [다시 시도] | 표시 | 활성 | 권한대로 |
| `Failed` | – | **표시** | (카드 안 문구로 대체) | (scrim 아래, 비어 있음) | scrim + VM 가드 | scrim + VM 가드 |

`Next`/`CreateFrame`/`EditFrame`/`RequestDelete` 선두에 **`if (!IsInteractive) return;`** 가드를 넣는다.
rev1은 `if (IsLoading) return;`이라 `Failed`에서 `CreateFrame`/`EditFrame`이 VM 층에서 막히지 않아
매트릭스보다 느슨했다(리뷰 m5). `IsInteractive` 한 기준으로 통일하면 매트릭스와 코드가 1:1이 된다.
이는 it16의 "커맨드 가드를 컨버터보다 느슨하게 두지 않는다" 규약과 같은 방향이다
(`ViewModels/FrameSelectViewModel.cs:98-113` 주석 참조).

---

## §6 무한 대기 방지 — 상태 전이 정의 (요구사항 3)

### 6.1 재로드 계기와 CTS 수명

```csharp
/// <summary>목록 재로드 계기. 대기 오버레이·안내 문구 정책이 달라진다. (it20 §6.5)</summary>
private enum ReloadReason
{
    /// <summary>화면 진입·[다시 시도]: 오버레이 노출, 중단 시 Degraded 안내.</summary>
    Enter,
    /// <summary>삭제 후 재스캔: 목록이 이미 보이므로 오버레이·안내 없이 조용히 갱신.</summary>
    Refresh
}

private CancellationTokenSource? _loadCts;   // 진행 중 로딩의 취소원. Dispose 소유자는 "그 로딩 자신".

public override Task OnEnterAsync() => ReloadFramesAsync(ReloadReason.Enter);

/// <summary>화면 이탈 시 진행 중 로딩 취소 — 뒤늦은 완료가 폐기된 VM 상태를 건드리지 않게 한다.</summary>
public override Task OnLeaveAsync()
{
    CancelLoad();
    return Task.CompletedTask;
}

/// <summary>신호만 보낸다. Dispose는 로딩 본체의 finally가 수행(이중 해제 불가).</summary>
private void CancelLoad()
{
    var cts = _loadCts;
    _loadCts = null;
    try { cts?.Cancel(); }
    catch (ObjectDisposedException) { /* 이미 완료·해제된 로딩 — 무해 */ }
}
```

**Dispose 소유권을 "취소자"가 아니라 "생성자"에게 둔다.** 취소자가 Dispose하면 진행 중 본체의 `finally`가
다시 Dispose해 `ObjectDisposedException`이 나거나, 반대 순서에서 `Cancel`이 같은 예외를 던진다.
위 구조에서는 `Cancel`만 예외 방어를 하고 `Dispose`는 **정확히 한 번**(본체 finally) 일어난다.

### 6.2 로딩 본체 전문 (그대로 컴파일 가능)

```csharp
private async Task ReloadFramesAsync(ReloadReason reason)
{
    CancelLoad();                                    // 이전 로딩(재시도 연타 등) 정리
    var cts = new CancellationTokenSource();
    _loadCts = cts;
    var clock = System.Diagnostics.Stopwatch.StartNew();
    bool quiet = reason == ReloadReason.Refresh;
    bool interrupted = false;
    bool completed = false;

    if (!quiet)
    {
        Phase = FrameLoadPhase.Loading;
        LoadingMessage = FrameCatalogProgress.StartLabel;
        LoadNotice = string.Empty;
    }

    try
    {
        ArmDeadline(cts, clock);

        // UI 스레드에서 생성 → 콜백이 UI 스레드로 마샬링된다(VF-16, QrPopupViewModel.cs:88-91 관례).
        var progress = new Progress<FrameCatalogProgress>(p =>
        {
            if (!ReferenceEquals(cts, _loadCts)) return;   // stale 보고 차단(늦은 L1 보고가 L2 문구를 덮지 않게)
            LoadingMessage = p.ToLabel();
            ArmDeadline(cts, clock);                       // 진행이 관측됐으니 무진행 타이머 재무장
        });

        Frames.Clear();

        var user = _shell.Session.CurrentUser;
        IsLoggedIn = user is not null;
        // it16 E4 로직 그대로: 생성·삭제 UI는 프레임 쓰기 권한(AdvancedUser 이상)에만 열린다.
        CanCreateFrame = user?.Role.CanWriteFrames() == true;
        CanDeleteFrames = user?.Role.CanWriteFrames() == true;
        IsPower = user?.Role.IsPower() == true;

        IReadOnlyList<FrameTemplate> defaults;
        try
        {
            defaults = await _catalog.GetDefaultFramesAsync(cts.Token, quiet ? null : progress);
        }
        catch (OperationCanceledException)
        {
            if (!ReferenceEquals(cts, _loadCts)) return;   // 화면 이탈 취소 → finally가 아무것도 건드리지 않는다
            interrupted = true;
            _logger?.LogWarning(
                "기본 프레임 대기 중단(무진행 {NoProgress}초/총 {Total}초 상한 또는 사용자 건너뛰기) — 로컬 전용 폴백",
                FrameLoadPolicy.NoProgressTimeoutSeconds, FrameLoadPolicy.MaxTotalWaitSeconds);
            defaults = await SafeLocalFramesAsync();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "기본 프레임 로딩 실패 — 로컬 전용 폴백");
            interrupted = true;
            defaults = await SafeLocalFramesAsync();
        }

        if (!ReferenceEquals(cts, _loadCts)) return;

        foreach (var f in defaults)
            Frames.Add(f);

        if (user is not null)
        {
            // 개인 프레임 로드 실패가 공용 목록까지 무너뜨리지 않게 개별 방어(로컬 파일 스캔).
            try
            {
                foreach (var f in await _catalog.GetUserFramesAsync(user.Id, CancellationToken.None))
                    Frames.Add(f);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "개인 프레임 로드 실패(공용 목록은 유지)");
            }
        }

        if (!ReferenceEquals(cts, _loadCts)) return;
        SelectedFrame = Frames.FirstOrDefault();
        completed = true;
    }
    finally
    {
        // C1: 어떤 예외·어떤 경로에서도 Loading에 고착되지 않는다. try 안에서 무엇이 터져도 여기는 실행된다.
        if (ReferenceEquals(cts, _loadCts))
        {
            Phase = FrameLoadPolicy.Finalize(Phase, Frames.Count, interrupted || !completed, quiet);
            LoadNotice = FrameLoadPolicy.NoticeFor(Phase);
            _loadCts = null;
        }
        clock.Stop();
        cts.Dispose();                                    // 자기 것만 해제 — 항상 1회
    }
}

/// <summary>
/// 무진행·총 상한 중 먼저 오는 시점으로 취소 예약을 재무장한다. 0 이하면 즉시 취소(총 상한 도달).
/// </summary>
private void ArmDeadline(CancellationTokenSource cts, System.Diagnostics.Stopwatch clock)
{
    try
    {
        var due = _loadDeadline(clock.Elapsed);
        if (due <= TimeSpan.Zero) cts.Cancel();
        else cts.CancelAfter(due);
    }
    catch (ObjectDisposedException) { /* 이미 완료·해제된 로딩 — 무해 */ }
}

/// <summary>
/// 로컬 전용 폴백. 이 호출까지 실패하면(fallback PNG 생성 불가 등) 빈 목록으로 축퇴시켜
/// Failed 카드가 실제로 도달 가능하게 한다(설계 §0.4·§4.3 — 리뷰 C1의 핵심 수정).
/// </summary>
private async Task<IReadOnlyList<FrameTemplate>> SafeLocalFramesAsync()
{
    try
    {
        return await _catalog.GetLocalDefaultFramesAsync(CancellationToken.None);
    }
    catch (Exception ex)
    {
        _logger?.LogError(ex, "로컬 전용 프레임 해석까지 실패 — 사용 가능한 프레임 0개");
        return Array.Empty<FrameTemplate>();
    }
}

/// <summary>[기다리지 않고 시작]: 서버 대기를 즉시 포기한다(진행 중 로딩이 로컬 폴백으로 마감).</summary>
[RelayCommand]
private void SkipServerWait()
{
    if (Phase != FrameLoadPhase.Loading) return;
    try { _loadCts?.Cancel(); }
    catch (ObjectDisposedException) { }
}

/// <summary>[다시 시도]: 대기 상한을 새로 부여해 처음부터 재시도.</summary>
[RelayCommand]
private Task RetryLoad() => ReloadFramesAsync(ReloadReason.Enter);
```

`ConfirmDelete()` 말미(`ViewModels/FrameSelectViewModel.cs:145`)의 호출은
`await ReloadFramesAsync(ReloadReason.Refresh);`로 바꾼다.

**네 탈출 경로**

| # | 트리거 | 메커니즘 | 결과 |
|---|--------|----------|------|
| 1 | 무진행 30초 | `ArmDeadline` → `CancelAfter(NoProgressTimeout)` | `OperationCanceledException` → 로컬 폴백 → `Degraded` |
| 2 | 총 60초 | `NextDeadline`이 남은 총 시간을 돌려주고 0이면 `Cancel()` | 동일 |
| 3 | [기다리지 않고 시작] | `SkipServerWaitCommand` → `_loadCts?.Cancel()` | 동일 경로. **새 로딩을 시작하지 않는다** — 진행 중 본체가 스스로 폴백을 수행한다 |
| 4 | 화면 이탈([취소]·[홈]·유휴 복귀·설정 진입) | `OnLeaveAsync` → `CancelLoad()`(`_loadCts=null`) | stale 가드가 모든 상태 기록을 차단 → 폐기된 VM에 쓰기 없음 |

그리고 **다섯 번째 안전망이 `finally`**다: 위 네 경로 중 어느 것도 아닌 미지의 예외(로컬 폴백 자체의 실패,
`Frames.Add` 중 예외 등)에서도 `Finalize`가 국면을 닫는다.

### 6.3 wall-clock 예산을 쓰지 않는 이유 (리뷰 M1)

rev1은 `cts.CancelAfter(20초)`를 로딩 시작 시 1회만 걸었다. 그 결과 최초 실행의 **지배 경로**에서 다음이 벌어진다.

```
t=0     App.OnStartup → prefetch 시작 → 세마포어 획득 → DB 조회 → 이미지 3개 다운로드 중…
t=2s    사용자가 [촬영하기] → FrameSelect 진입 → GetDefaultFramesAsync(ct)
        → SemaphoreSlim.WaitAsync(ct)에서 **줄 서서 대기**
t=2~22s 예산 20초가 전부 게이트 대기에 소모. 문구는 "이미 시작된 준비를 기다리는 중…"에 고정
        (prefetch는 progress:null로 호출되어 실제 다운로드 진행이 UI에 도달할 경로가 없다)
t=22s   취소 → Degraded + "서버 프레임을 모두 가져오지 못해…"
        ← **서버 프레임은 정상적으로 받아지고 있었다.** 안내는 거짓이고 목록엔 fallback 흰 프레임만 남는다.
```

포토부스에서 이것은 "느려서 아쉽다"가 아니라 **손님이 빈 흰 프레임으로 촬영한다**는 결과다.
rev1의 §10.6 M1 기대 관측("문구가 `(1/N)`으로 변화")도 이 경로에서는 성립하지 않아 QA가 정상 동작을
실패로 보고하게 된다.

**rev2의 두 축 수정**

1. **원인 제거** — 세마포어 줄 세우기를 단일 비행 + 진행 중계로 바꾼다(§7.1). 화면은 대기하지 않고
   **합류**하며, prefetch의 실제 진행(`DownloadingImage (2/3)`)을 그대로 받는다. 게이트 대기 구간 자체가 없어진다.
2. **의미 변경** — 남은 상한을 wall-clock에서 **무진행**으로 바꾼다. 진행 보고가 오면 타이머를 재무장하므로
   "정상 진행 중인데 자름"이 원리적으로 불가능하다. 총 상한 60초는 별도로 유지해 손님을 무한정 세우지 않는다.

두 축을 합치면 A-2(예산 적정성)와 R-1(느린 회선 오진) 모두 해소된다 —
"진행이 관측되는 한 자르지 않고, 진행이 멎으면 30초 안에 자르며, 어떤 경우에도 60초를 넘기지 않는다."

### 6.4 오프라인 부스의 조용한 폴백 보존 (회귀 방지)

| 상황 | 서버 조회 | 로컬 프레임 | 종전 동작 | it20 동작 |
|------|:---------:|:-----------:|-----------|-----------|
| 정상 온라인 | 성공 | 있음/없음 | 목록 표시 | `Ready` — 동일 ✅ |
| 오프라인 부스(캐시 있음) | 실패(즉시) | 있음 | 조용히 목록 표시, warning 로그만 | `Ready` — **동일** ✅ |
| 오프라인 + 캐시 없음 | 실패(즉시) | 없음 → fallback 생성 | fallback 1개 표시 | `Ready` — 동일 ✅ |
| 최초 실행 + 느린 서버(진행 중) | 진행 중 | 없음 | **빈 화면 무한 대기** ❌ | `Loading` 유지 + 실시간 `(n/m)` → 완료 시 `Ready` ✅ |
| 최초 실행 + 서버 멎음 | 무응답 | 없음 | **빈 화면 무한 대기** ❌ | 30초 후 `Degraded` + 안내 + [다시 시도] ✅ |

`Classify`의 `waitInterrupted`가 "즉시 실패(오프라인)"와 "잘라낸 대기(상한 초과)"를 구분하는 유일한 축이다.
서버 조회 실패는 `GetDefaultFramesAsync` **내부에서 삼켜지므로**(`Services/FrameCatalogService.cs:71-74`,
rev2는 이 catch를 **변경하지 않는다** — §7.1) 예외로 빠져나오지 않고 `Ready`가 유지된다.
이것이 위 표 2·3행의 근거다.

### 6.5 재로드 계기 분리 — 삭제 후 재스캔에 오버레이를 띄우지 않는다

`ConfirmDelete`는 삭제 직후 재스캔을 호출해 목록을 디스크 기준으로 갱신한다
(`ViewModels/FrameSelectViewModel.cs:145`, "보완#3"). 이 경로에 그대로 `Phase = Loading`을 걸면 **삭제할
때마다 대기 오버레이가 수백 ms 번쩍인다** — 목록이 이미 보이는 상태에서 전면 오버레이가 스치는 것은 깜빡임 결함이다.

| 호출 지점 | reason | 근거 |
|-----------|--------|------|
| `OnEnterAsync()` | `Enter` | 최초 진입 = 요구사항의 대상 시나리오 |
| `RetryLoadCommand` | `Enter` | 사용자가 명시적으로 다시 기다리기로 했다 |
| `ConfirmDelete()` 말미 | `Refresh` | 목록 표시 중 조용한 갱신 |

`Refresh`도 **상한을 동일하게 받는다**(무한 대기 금지는 계기와 무관)이고 `progress`는 `null`로 넘긴다.
중단되어도 `Finalize(quiet:true)`가 종전 국면을 유지하므로 네트워크 안내가 삭제 조작에 끼어들지 않는다 —
같은 화면의 `DeleteNotice`가 삭제 결과 안내를 이미 담당한다(`ViewModels/FrameSelectViewModel.cs:44`).
**예외 두 가지**는 `Finalize`가 처리한다: 목록이 0개면 `Failed`, 종전이 `Failed`였는데 프레임이 생겼으면 `Ready`.

**삭제 후 서버 재조회 동작은 종전 그대로 유지한다**(명시적 판정): `Refresh`도 `GetDefaultFramesAsync`를
호출하므로, 로컬만 지운 DB 유래 공용 프레임이 재다운로드되어 카드가 되돌아오는 현행 동작이 보존된다.
이 동작의 타당성 논의는 이번 범위 밖이다 — 대기 UI 변경이 삭제 의미론을 조용히 바꾸지 않게 한다.

### 6.6 `Loading` 고착 불가능성 논증 (리뷰 C1의 종결)

`Phase`가 `Loading`으로 남을 수 있는 경우를 전수 검토한다.

| 경로 | rev1 | rev2 |
|------|------|------|
| 정상 완료 | 말미 대입 → Ready ✅ | `finally` → `Finalize` → Ready ✅ |
| 상한 초과·건너뛰기 → 로컬 폴백 성공 | catch 후 말미 대입 → Degraded ✅ | `finally` → Degraded ✅ |
| 상한 초과 → **로컬 폴백이 예외** | 예외가 `OnEnterAsync` 밖으로 → `AppShellViewModel.cs:217-221`이 삼킴 → **Loading 영구 고착** 🔴 | `SafeLocalFramesAsync`가 빈 목록으로 축퇴 → `finally` → **Failed** ✅ |
| `Frames.Add`·`SelectedFrame` 중 예외 | 말미 대입에 도달 못 함 → **Loading 고착** 🔴 | `finally` → `Finalize(…, !completed=true, …)` → Degraded 또는 Failed ✅ |
| 화면 이탈 취소 | 상태 미기록(정상 — VM이 폐기됨) ✅ | stale 가드로 `finally`도 건너뜀 ✅ |
| `ArmDeadline`이 예외 | 방어 없음 → 고착 가능 🔴 | `ObjectDisposedException` 방어 + 그 밖은 `finally`가 확정 ✅ |

rev2에서 `Loading`으로 남는 유일한 경우는 **VM이 이미 폐기된 경우**(stale)이며, 그때는 화면이 이미
바뀌었으므로 사용자에게 보이지 않는다. Step 6의 T-26이 3행(로컬 폴백 예외 → Failed)을 고정한다.

---

## §7 `FrameCatalogService` 변경 명세

### 7.1 세마포어 → 단일 비행(single-flight) + 진행 중계

현행은 `SemaphoreSlim(1,1)`로 **줄 세우기**를 한다(VF-9). 나중 호출자는 앞 호출이 끝날 때까지 대기하며
앞 호출의 진행을 알 수 없다 — §6.3이 분석한 오진의 근원이다. rev2는 **한 번만 실행하고 결과·진행을
모두에게 나눠 주는** 구조로 바꾼다.

```csharp
// ── it20: 단일 비행 + 진행 중계 (종전 SemaphoreSlim _defaultFramesGate 대체) ──
// 종전 게이트는 "줄 세우기"였다: 시작 prefetch가 잡고 있으면 화면 진입은 그 완료까지 대기하고
// 진행 상황도 알 수 없어, 대기 상한이 전부 줄 서기에 소모되고 문구가 정체됐다(설계 §6.3).
// 단일 비행은 같은 작업을 **공유**한다 — 중복 다운로드 방지(it10 S3-2의 원래 목적)는 그대로 달성하고,
// 늦게 합류한 호출자는 진행 중인 작업의 최근 국면을 즉시 replay 받는다.
private readonly object _sync = new();
private Task<IReadOnlyList<FrameTemplate>>? _inFlight;
private readonly List<IProgress<FrameCatalogProgress>> _observers = new();
private FrameCatalogProgress _lastProgress = new(FrameCatalogPhase.ResolvingLocal);

/// <summary>
/// 공용 프레임(게스트 포함). 로컬 공용(번들+파워캐시) 우선 → DB isDefault 중 로컬에 없는 이름만 캐시·병합
/// (이름 기준 dedup) → 없으면 번들 → fallback. (it8 §3 정정)
/// it20: 동시 호출은 **하나의 작업을 공유**한다(단일 비행). <paramref name="progress"/>를 주면 진행
/// 국면을 받고, 늦게 합류해도 최근 국면이 즉시 1회 replay된다.
/// <paramref name="ct"/>는 **이 호출자만** 취소한다 — 공유 작업은 계속 진행해 캐시를 완성하므로
/// 다른 호출자나 시작 prefetch가 피해를 입지 않는다.
/// </summary>
public Task<IReadOnlyList<FrameTemplate>> GetDefaultFramesAsync(
    CancellationToken ct = default,
    IProgress<FrameCatalogProgress>? progress = null)
{
    Task<IReadOnlyList<FrameTemplate>> shared;
    FrameCatalogProgress snapshot;
    lock (_sync)
    {
        if (progress is not null) _observers.Add(progress);
        snapshot = _lastProgress;
        // Task.Run으로 시작 → 호출자(UI 스레드)의 동기 구간은 이 lock 뿐이다(설계 §8.1).
        _inFlight ??= Task.Run(RunSharedLoadAsync);
        shared = _inFlight;
    }
    progress?.Report(snapshot);          // 문구 공백 구간 제거(합류 즉시 현재 국면 표시)
    return AwaitSharedAsync(shared, progress, ct);
}

private async Task<IReadOnlyList<FrameTemplate>> AwaitSharedAsync(
    Task<IReadOnlyList<FrameTemplate>> shared,
    IProgress<FrameCatalogProgress>? progress,
    CancellationToken ct)
{
    try
    {
        // 호출자별 취소: 공유 작업 자체는 취소하지 않는다(캐시 워밍 유지·다른 호출자 보호).
        return await shared.WaitAsync(ct).ConfigureAwait(true);
    }
    finally
    {
        if (progress is not null)
            lock (_sync) { _observers.Remove(progress); }
    }
}

private async Task<IReadOnlyList<FrameTemplate>> RunSharedLoadAsync()
{
    try
    {
        return await LoadDefaultFramesCoreAsync().ConfigureAwait(false);
    }
    finally
    {
        lock (_sync) { _inFlight = null; }   // 다음 호출은 새 작업을 시작한다(캐시 반영 후 재조회)
    }
}

/// <summary>구독 중인 모든 호출자에게 진행을 알리고 replay용 스냅샷을 갱신한다.</summary>
private void ReportShared(FrameCatalogProgress p)
{
    IProgress<FrameCatalogProgress>[] targets;
    lock (_sync)
    {
        _lastProgress = p;
        targets = _observers.ToArray();
    }
    foreach (var t in targets)
    {
        // 구독자(UI) 예외가 로딩을 깨지 않게 한다.
        try { t.Report(p); }
        catch (Exception ex) { _logger?.LogWarning(ex, "프레임 진행 보고 실패(무시)"); }
    }
}
```

`LoadDefaultFramesCoreAsync()`는 **현행 `GetDefaultFramesAsync` 본문에서 게이트 획득/해제만 뺀 것**이며,
`ct` 대신 `CancellationToken.None`을 쓰고 `progress?.Report(...)` 대신 `ReportShared(...)`를 호출한다.

```csharp
private async Task<IReadOnlyList<FrameTemplate>> LoadDefaultFramesCoreAsync()
{
    ReportShared(new FrameCatalogProgress(FrameCatalogPhase.ResolvingLocal));

    // ① 로컬 공용(접두 없는 파일 = 번들 + 파워 캐시)
    var local = _localStore.LoadPublic();
    var localNames = _localStore.PublicFrameNames();

    // ② DB isDefault 중 로컬에 이름이 없는 것만 다운로드·캐시(이름 기준 dedup, 중복 집계 없음)
    try
    {
        ReportShared(new FrameCatalogProgress(FrameCatalogPhase.QueryingServer));
        var dbFrames = await _repository.GetDefaultFramesAsync(CancellationToken.None)
            .ConfigureAwait(false);

        // 분모에서 캐시 히트를 제외해 (n/m)을 정직하게 만든다.
        var pending = dbFrames.Where(f => !localNames.Contains(f.Name)).ToList();
        for (int i = 0; i < pending.Count; i++)
        {
            ReportShared(new FrameCatalogProgress(
                FrameCatalogPhase.DownloadingImage, i + 1, pending.Count));
            var cached = await TryCacheAsync(pending[i], CancellationToken.None).ConfigureAwait(false);
            if (cached is not null) local = Append(local, cached);
        }
    }
    catch (Exception ex)
    {
        _logger?.LogWarning(ex, "DB 기본 프레임 조회 실패 — 로컬/번들/fallback로 폴백(오프라인 모드)");
    }

    ReportShared(new FrameCatalogProgress(FrameCatalogPhase.Completed));
    return ResolveLocalFrames(local);
}
```

**rev1의 "취소 정직성(OCE 재던짐)" 변경은 폐기한다.** 이유:

- 공유 작업은 `CancellationToken.None`으로 돌기 때문에 내부에 취소 소스가 없다 → catch 필터가 의미를 잃는다.
- 호출자별 취소는 `Task.WaitAsync(ct)`가 **경계에서** 정직하게 `OperationCanceledException`을 던진다.
  이것이 rev1이 catch 필터로 달성하려던 목표를 더 단순하게, 부작용 없이 이룬다.
- 부수 효과로 rev1의 리스크 R-3(`ct.IsCancellationRequested` 조건을 빠뜨리면 100초 HTTP 타임아웃이
  예외로 튀어 오프라인 폴백이 깨진다)이 **완전히 사라진다** — 두 catch 블록을 아예 손대지 않는다.
- 다만 `FramePickerViewModel`의 관측 동작은 바뀐다(VF-24 정정, 리뷰 m4): 모달을 닫아 취소하면 종전에는
  목록이 반환됐고 이제는 `OperationCanceledException`으로 조용히 종료된다. 취소 계기가 모달 종료·재오픈뿐이고
  그 시점에 화면이 사라지므로 수용한다. §10.3에 이를 고정하는 회귀 테스트(T-19)를 둔다.

**중복 다운로드 방지(it10 S3-2)가 유지되는 근거**: 두 호출이 동시에 오면 `_inFlight`가 하나이므로
다운로드 패스는 **한 번**만 돈다. 순차 호출은 첫 호출 완료 후 `_inFlight`가 비므로 새 패스를 시작하고,
그때는 로컬 캐시가 채워져 있어 `localNames` dedup이 다운로드를 0회로 만든다. 기존 테스트
`Concurrent_Calls_Download_Each_Frame_Once`·`Cache_Miss_Downloads_And_Dedups`·`Cache_Hit_Skips_Download`가
**수정 없이** 이 불변을 검증한다(A-7).

**누수 점검**: `_observers`에서 자기 구독을 제거하는 경로는 `AwaitSharedAsync`의 `finally` **한 곳**이며
취소·예외·정상 완료 모두 통과한다. 별도 `Clear()`를 두지 않는 이유는 제거 경로를 하나로 유지해
"이미 제거된 구독을 또 제거"하는 모호함을 없애기 위함이다.

### 7.2 로컬 전용 해석 API + fallback 쓰기 직렬화 (리뷰 M3)

```csharp
/// <summary>
/// 네트워크를 전혀 쓰지 않는 기본 프레임 해석(로컬 공용 → 번들 → fallback). (it20)
/// 대기 상한 초과·사용자 건너뛰기 후의 축소 진행 경로다. 정상 동작 시 최소 1개를 돌려준다.
/// ⚠️ 단일 비행에 합류하지 **않는다** — 합류하면 방금 상한을 넘긴 그 작업을 다시 기다려 상한이 무의미해진다.
/// 읽기 안전 근거: LocalFrameStore가 png를 먼저 쓰고 .slots를 나중에 쓰며, 로드는 .slots 없는 항목을
/// 건너뛴다(LocalFrameStore.cs:46-48, :108-109) → 반쪽 프레임이 노출되지 않는다.
/// ⚠️ 쓰기 안전 근거: 이 경로의 종단 EnsureFallbackFrame()은 **파일을 쓴다**(Cv2.ImWrite). 진행 중인
/// 공유 작업도 같은 경로를 탈 수 있어 동시 쓰기가 가능하므로 아래 두 장치로 막는다 —
/// ① 전용 lock으로 검사·생성을 직렬화 ② 임시 파일에 쓰고 File.Move(overwrite)로 원자 교체.
/// </summary>
public Task<IReadOnlyList<FrameTemplate>> GetLocalDefaultFramesAsync(CancellationToken ct = default)
    => Task.Run(() => ResolveLocalFrames(preferLoaded: null), ct);

/// <summary>
/// 로컬 우선순위 해석(공용 로컬 → 번들 → fallback). 네트워크를 쓰지 않는다. (it20)
/// preferLoaded가 비어 있지 않으면 그대로 채택 — 호출측이 이미 스캔·병합을 마친 경우다.
/// 두 경로(공유 작업 종단·로컬 전용 API)가 같은 코드를 쓰게 해 §9 #11 우선순위 규약이 갈라지지 않게 한다.
/// </summary>
private IReadOnlyList<FrameTemplate> ResolveLocalFrames(IReadOnlyList<FrameTemplate>? preferLoaded)
{
    var local = preferLoaded ?? _localStore.LoadPublic();
    if (local.Count > 0)
    {
        _logger?.LogInformation("공용 프레임 {Count}개(로컬 우선 + DB 캐시 병합)", local.Count);
        return local;
    }

    var bundled = LoadBundleFrames();
    if (bundled.Count > 0)
    {
        _logger?.LogInformation("번들 프레임 {Count}개 사용", bundled.Count);
        return bundled;
    }

    _logger?.LogInformation("fallback 프레임 생성");
    return new[] { EnsureFallbackFrame() };
}

// fallback PNG는 프로세스 내 여러 경로(공유 작업 종단 · 로컬 전용 API)에서 동시에 요구될 수 있다.
// 같은 경로에 두 스레드가 ImWrite하면 공유 위반 실패 또는 반쯤 쓰인 PNG(디코드 실패)가 남는다.
private static readonly object _fallbackWriteSync = new();

private FrameTemplate EnsureFallbackFrame()
{
    lock (_fallbackWriteSync)
    {
        if (File.Exists(FallbackImagePath))
            return DefaultFrameProvider.CreateFallbackTemplate(FallbackImagePath);

        // 임시 파일에 렌더 후 원자 교체 — 중간 상태 파일이 남지 않는다.
        var tempPath = FallbackImagePath + ".tmp";
        var template = FallbackFrameRenderer.Create(tempPath);
        Directory.CreateDirectory(Path.GetDirectoryName(FallbackImagePath)!);
        File.Move(tempPath, FallbackImagePath, overwrite: true);
        template.ImageUrl = FallbackImagePath;   // 렌더러가 temp 경로를 심어 두므로 최종 경로로 정정
        return template;
    }
}
```

`FallbackFrameRenderer.Create(outputPath)`는 `DefaultFrameProvider.CreateFallbackTemplate(outputPath)`로
템플릿을 만들어 `ImageUrl`에 그 경로를 담는다(`src/MCPhoto.Capture/FallbackFrameRenderer.cs:17`).
임시 경로로 렌더하면 `ImageUrl`이 `.tmp`가 되므로 **교체 후 최종 경로로 정정해야 한다** — 빠뜨리면
카드 이미지가 사라진 파일을 가리켜 placeholder가 뜬다.

> `lock` 안에서 파일 I/O를 하지만 이 메서드는 `Task.Run`/`Task.Run(RunSharedLoadAsync)` 경계 **안**에서만
> 호출되므로 UI 스레드가 블로킹되지 않는다(§8.1). 대기 시간도 fallback 생성 1회(수십 ms)뿐이다.

### 7.3 시그니처 — 오버로드를 만들지 않는다

```csharp
public Task<IReadOnlyList<FrameTemplate>> GetDefaultFramesAsync(
    CancellationToken ct = default,
    IProgress<FrameCatalogProgress>? progress = null)
```

**오버로드(`(IProgress<...>?, CancellationToken)` 추가)를 쓰지 않는 이유**: 두 오버로드가 모두 전 인자
기본값을 가지면 `GetDefaultFramesAsync()` 무인자 호출이 **CS0121 모호 호출**로 컴파일 실패한다.
`src/MCPhoto.App/App.xaml.cs:95`가 정확히 그 형태다. `ct`를 1번 인자로 유지한 optional 파라미터 추가는
기존 호출 3곳(`App.xaml.cs:95`, `ViewModels/FramePickerViewModel.cs:56`, 테스트들)을
**한 줄도 바꾸지 않는다**.

`'_'` 포함 이름 경고(`Services/FrameCatalogService.cs:118-122`)와 dedup 규약은 그대로 유지한다 —
`pending` 사전 계산은 `localNames.Contains` 판정 위치만 옮기며 규약을 바꾸지 않는다.

---

## §8 스레딩·안전·인코딩

### 8.1 UI 스레드 경계

| 작업 | 종전 실행 스레드 | it20 | 근거 |
|------|------------------|------|------|
| `_localStore.LoadPublic()` / `PublicFrameNames()` | **UI**(게이트 미경합 시 동기 완료) | 스레드풀(`Task.Run(RunSharedLoadAsync)` 안) | 디렉터리 열거 + 파일 읽기 |
| `LoadBundleFrames()`(내부 `Cv2.ImRead` 전체 디코드) | **UI** | 스레드풀 | VF-10 — 프레임당 수십~수백 ms |
| `EnsureFallbackFrame()`(1200×1600 PNG 생성·쓰기) | **UI** | 스레드풀 + 전용 lock | VF-11, §7.2 |
| DB 조회 / 이미지 다운로드 | 비UI(HTTP await) | 동일 | 이미 비동기 |
| `GetLocalDefaultFramesAsync` | (신규) | 스레드풀(`Task.Run`) | 같은 로컬 해석 코드 |
| `ObservableCollection<FrameTemplate>.Add` | UI | UI | VM이 await 후 UI 컨텍스트에서 수행(`AwaitSharedAsync`가 `ConfigureAwait(true)`) |
| `Progress<T>` 콜백 → `LoadingMessage` 대입 | – | UI | VF-16: UI 스레드에서 생성 → SynchronizationContext 마샬링 |
| `ReportShared` 호출 스레드 | – | 스레드풀 | `Progress<T>`가 UI로 마샬링하므로 안전. `_observers` 스냅샷은 lock으로 보호 |

호출자(UI 스레드)의 동기 구간은 `GetDefaultFramesAsync`의 `lock (_sync)` 블록 하나뿐이다 —
리스트 추가와 필드 읽기이므로 마이크로초 단위다. 따라서 대기 오버레이의 첫 페인트를 막지 않는다(A-1).

**안전 규칙 준수**: UI 스레드에서 동기 I/O·`.Result`·`.Wait()`를 **쓰지 않는다**. 백그라운드에서
`ObservableCollection`·바인딩 대상 프로퍼티를 **직접 갱신하지 않는다**.
`ResolveLocalFrames`가 만드는 `FrameTemplate`·`Slot`은 순수 모델이며 `System.Windows` 타입을 참조하지 않는다.
`BitmapImage` 생성은 XAML 컨버터(`FilePathToImage`, UI 스레드)에서만 일어나며 이미 `Freeze()`된다
(`Converters/CommonConverters.cs:32`).

### 8.2 이벤트 구독·누수 점검

| 항목 | 판정 |
|------|------|
| 신규 이벤트 구독 | **0개**. `Progress<T>`는 이벤트가 아니라 콜백 델리게이트이며 인스턴스 수명이 호출 1회로 끝난다 |
| `_observers` 리스트 | 추가 1곳(`GetDefaultFramesAsync`) : 제거 1곳(`AwaitSharedAsync` finally). 취소·예외·정상 완료 모두 finally를 통과하므로 누적되지 않는다 |
| 신규 타이머 | **0개**. 경과 시간 문구 변화를 도입하지 않는다 — `DispatcherTimer`는 구독 해제 경로를 하나 더 만들고, 진행 문구는 이미 `IProgress`로 갱신된다. `CancelAfter`의 내부 타이머는 `Dispose`가 해제한다 |
| `CancellationTokenSource` | 생성 1 : Dispose 1(§6.1 단일 소유). `Stopwatch`는 관리 리소스 없음 |
| `_inFlight` 참조 | 완료 시 `RunSharedLoadAsync`의 finally가 `null`로 되돌린다 → 완료된 목록이 서비스에 영구 상주하지 않는다 |
| VM 수명 | Transient(`ServiceRegistration.cs:196`) — 화면마다 새 인스턴스. `OnLeaveAsync`가 취소하므로 폐기된 VM을 붙잡는 실행 중 작업이 남지 않는다(공유 작업은 VM을 참조하지 않는다) |
| `IDisposable` VM 승격 | **불필요**. `OnLeaveAsync`가 항상 호출된다(VF-14). 전역 예외 시 `ReturnHome` → `NavigateAsync` → `OnLeaveAsync` 경로도 동일 |

### 8.3 리소스 키 충돌

신규 키는 `Spinner.Ring` **1개**뿐이다. `Themes/Controls.xaml`에 `Spinner.*` 접두 키가 없음을 확인했다(VF-21).
템플릿 내부 이름(`SpinnerRing`·`SpinnerRotate`·`SpinnerSpin`)은 템플릿 namescope 지역이라 전역 키와 충돌하지 않는다.
컨버터는 기존 `BoolToVis`만 사용 → **신규 컨버터 0**.

### 8.4 전역 예외·오류 표시

로딩 실패는 VM이 `catch`하고 `Phase`/`LoadNotice`로 표면화한다 → `DispatcherUnhandledException`
(`App.xaml.cs:103`)까지 올라가 홈으로 튕기는 일이 없다. 로컬 폴백조차 실패하면 `Failed` 카드가 뜬다.
스피너 애니메이션도 UI 스레드 예외 원천이므로 §4.5에서 검증된 형태로 바꾸고 Step 5에서 테스트한다.

### 8.5 DPI·테마

신규 시각 요소는 기존 토큰(`Brush.Scrim`·`Brush.Bg`·`Brush.Accent`·`Text.H2`·`Text.Body`·`Text.Caption`·
`Brush.Text.Muted`·`Card`·`Shadow.Pop`·`Button.Ghost`·`Button.Primary`)만 참조한다.
스피너는 `Ellipse`(벡터) — 고DPI에서 자동 스케일. 하드코딩 픽셀은 스피너 크기(56)와 카드 여백(32)뿐이며
기존 오버레이(`Views/FrameSelectView.xaml:60`)와 동일한 값이다.

### 8.6 파일 인코딩

수정·신규 대상 전부 **UTF-8 no BOM**(VF-26). 한글 주석·한글 UI 문구가 다수라 BOM/코드페이지 사고가
곧바로 문자열 깨짐으로 이어진다. 각 Step 검증에 한글 가독 확인(`grep`)을 포함한다.

---

## §9 파일별 역할 (변경 인벤토리)

| # | 파일 | 종류 | 변경 내용 | Step |
|---|------|------|-----------|------|
| 1 | `src/MCPhoto.Core/Frames/FrameLoadPolicy.cs` | **신규** | `FrameLoadPhase` enum + 상한 상수 3개 + `NextDeadline`/`Classify`/`Finalize`/`NoticeFor` 순수 함수 (§5.1 전문) | 1 |
| 2 | `src/MCPhoto.Core/Frames/FrameCatalogProgress.cs` | **신규** | `FrameCatalogPhase` enum + `FrameCatalogProgress` record struct + `ToLabel()`/`StartLabel` (§5.2 전문) | 1 |
| 3 | `src/MCPhoto.App/Services/FrameCatalogService.cs` | 수정 | ① 세마포어 → 단일 비행 + 진행 중계(§7.1) ② `ResolveLocalFrames` 추출 + `GetLocalDefaultFramesAsync` 공개(§7.2) ③ `progress` optional 파라미터(§7.3) ④ `EnsureFallbackFrame` lock + 원자 교체(§7.2) | 2·3·4 |
| 4 | `src/MCPhoto.App/Themes/Controls.xaml` | 수정 | `ControlTemplate x:Key="Spinner.Ring"` 신설(§4.5 전문) | 5 |
| 5 | `src/MCPhoto.App/ViewModels/FrameSelectViewModel.cs` | 수정 | `Phase`/`LoadingMessage`/`LoadNotice` + 파생 bool 4개, `_isLoading` 필드 제거, `ReloadReason`, CTS 수명(§6.1), 로딩 본체(§6.2), `ArmDeadline`/`SafeLocalFramesAsync`, `SkipServerWait`/`RetryLoad` 커맨드, `OnLeaveAsync`, `IsInteractive` 가드, `loadDeadline` 테스트 이음새 | 6 |
| 6 | `src/MCPhoto.App/Views/FrameSelectView.xaml` | 수정 | 대기 오버레이 + 실패 카드 + Degraded 인라인 안내(§4.1·4.3·4.4) | 7 |
| 7 | `tests/MCPhoto.Tests/FrameLoadPolicyTests.cs` | **신규** | `Classify`/`Finalize`/`NextDeadline`/`NoticeFor` 진리표 + 상한 불변식 | 1 |
| 8 | `tests/MCPhoto.Tests/FrameCatalogProgressTests.cs` | **신규** | `ToLabel()` 문구·카운터 표기 | 1 |
| 9 | `tests/MCPhoto.Tests/FrameCatalogServiceTests.cs` | 수정 | 단일 비행 공유·진행 중계·replay·호출자별 취소·로컬 전용·fallback 동시 생성 | 2·3·4 |
| 10 | `tests/MCPhoto.Tests/FrameSelectViewModelTests.cs` | 수정 | 국면 전이 8종 + 상한 만료 경로 + 로컬 폴백 실패 → Failed + 커맨드 가드 | 6 |
| 11 | `tests/MCPhoto.Tests/XamlResourceTests.cs` | 수정 | `Spinner.Ring` 인스턴스화·애니메이션 가능성 + `FrameSelectView` 바인딩 Path ↔ VM 멤버 대조 | 5·7 |
| 12 | `docs/analysis/11-exe-app-features.md` | 문서 | 프레임 선택 절에 최초 실행 대기 UI·상한·축소 진행 기술 | 9 |
| 13 | `docs/analysis/13-client-behavior-spec.md` | 문서 | 프레임 로딩 상태 전이표 추가 | 9 |
| 14 | `docs/design/wpf-architecture.md` | 문서 | 화면별 오버레이 목록에 프레임 대기 오버레이 추가 | 9 |

**변경 없음(명시)**: `MainWindow.xaml`/`.cs`, `AppShellViewModel.cs`, `HomeViewModel.cs`,
`App.xaml.cs`(prefetch 그대로 — §6.3), `FramePickerViewModel.cs`, `Views/CaptureView.xaml`(§4.5 비목표),
`LocalFrameStore.cs`, `FallbackFrameRenderer.cs`(호출측에서 원자 교체 — 렌더러 자체는 불변),
`HttpFrameRepository.cs`, `ServiceRegistration.cs`, `web/functions/**`.
`FrameCatalogService`의 **두 `catch (Exception ex)` 블록도 변경하지 않는다**(rev1의 OCE 필터 폐기, §7.1).

---

## §10 테스트 계획

베이스라인 **857 통과**(VF-29). 신규 **42건 이상**(§10.1 14 + §10.2 5 + §10.3 11 + §10.4 9 + §10.5 3)을
더해 **899 이상**이 목표 하한이다
(`[Theory]`는 InlineData 수만큼 전개되므로 실제 집계는 더 늘어난다 — 하한만 검증한다).

### 10.1 `FrameLoadPolicyTests.cs` (신규 · 13건)

> Core 순수 테스트다. **App 계층 타입을 참조하지 않는다**(리뷰 m9 — rev1의 T6이 `AppShellViewModel`을
> 생성했던 것을 폐기). 유휴 경고 불변식은 `IdleWarningReferenceSeconds` 상수 비교로 확인한다.

| # | 테스트 | 기대 |
|---|--------|------|
| T-1 | `Classify_Zero_Frames_Is_Failed` `[Theory]`(0,false)(0,true)(-1,false) | `Failed` |
| T-2 | `Classify_Interrupted_With_Frames_Is_Degraded` `[Theory]`(1,true)(3,true) | `Degraded` |
| T-3 | `Classify_Uninterrupted_With_Frames_Is_Ready` `[Theory]`(1,false)(3,false) | `Ready` — 오프라인 조용한 폴백 보존(§6.4) |
| T-4 | `Finalize_Loud_Uses_Classify` — `Finalize(Loading, 2, true, quiet:false)` | `Degraded`. `Finalize(Loading, 2, false, false)` → `Ready` |
| T-5 | `Finalize_Quiet_Keeps_Current` — `Finalize(Ready, 2, true, quiet:true)` | `Ready`(삭제 재스캔에 안내가 끼어들지 않음, §6.5) |
| T-6 | `Finalize_Quiet_Recovers_From_Failed` — `Finalize(Failed, 2, false, quiet:true)` | `Ready`(프레임이 생겼으므로 회복) |
| T-7 | `Finalize_Zero_Frames_Always_Failed` `[Theory]`(Ready,quiet:true)(Degraded,quiet:true)(Loading,quiet:false) with count 0 | 전부 `Failed` |
| T-8 | `Finalize_Never_Returns_Loading` `[Theory]` 4국면 × quiet 2 × count{0,2} × interrupted 2 (32조합) | 반환값에 `Loading`이 **한 번도 없다** — §0.4 불변식의 기계적 고정 |
| T-9 | `NextDeadline_Returns_NoProgress_When_Plenty_Left` — `NextDeadline(TimeSpan.Zero)` | `NoProgressTimeout`(30초) |
| T-10 | `NextDeadline_Clamps_To_Remaining_Total` — `NextDeadline(TimeSpan.FromSeconds(45))` | 15초(총 60 − 45 < 무진행 30) |
| T-11 | `NextDeadline_Zero_When_Total_Exhausted` `[Theory]`(60초)(90초) | `TimeSpan.Zero`(즉시 취소 신호) |
| T-12 | `MaxTotalWait_Is_Below_Idle_Warning` | `MaxTotalWaitSeconds < IdleWarningReferenceSeconds`이고 `NoProgressTimeoutSeconds < MaxTotalWaitSeconds` (A-5·상한 순서 불변식) |
| T-13 | `NoticeFor_Ready_Is_Empty_Others_Are_Not` `[Theory]` 4국면 | `Ready`·`Loading`은 빈 문자열, `Degraded`·`Failed`는 비어 있지 않고 서로 다르다 |

추가 1건: `Phase_Default_Is_Loading` — `default(FrameLoadPhase) == FrameLoadPhase.Loading`
(VM 초기 상태 안전 보장).

### 10.2 `FrameCatalogProgressTests.cs` (신규 · 5건)

| # | 테스트 | 기대 |
|---|--------|------|
| T-14 | `Label_For_Each_Phase_Is_Not_Empty` `[Theory]` 4개 phase | 전부 비어 있지 않고 서로 다르다 |
| T-15 | `Downloading_Label_Includes_Counter` — `(DownloadingImage, 2, 3)` | `"(2/3)"` 포함 |
| T-16 | `Downloading_Label_Omits_Counter_When_Total_Zero` — `(DownloadingImage, 0, 0)` | `"("` 미포함 |
| T-17 | `Start_Label_Is_Not_Empty` | `StartLabel.Length > 0` |
| T-18 | `Progress_Has_No_Frame_Name_Member` | `typeof(FrameCatalogProgress).GetProperty("FrameName") is null` — §5.2 판정(이름 미노출) 회귀 방지 |

### 10.3 `FrameCatalogServiceTests.cs` 확장 (11건)

기존 `CountingFrameRepository`/`_store`/`MakeService` 하네스를 재사용한다. "붙잡기"에는 기존
`downloadImage: async (_, _) => { await release.Task; … }` 패턴(`FrameCatalogServiceTests.cs:103-109`)을 쓴다.
⚠️ **붙잡기가 성립하려면 `repo.DefaultFrames`에 DB 프레임이 있어야 한다**(비어 있으면 `downloadImage`가
호출되지 않는다 — 리뷰 m2). 각 테스트에서 `DbFrame("f1")` 이상을 넣는다.

| # | 테스트 | 기대 |
|---|--------|------|
| T-19 | `Picker_Style_Cancellation_Surfaces_As_OperationCanceled` — 다운로드를 붙잡은 상태에서 `cts.Cancel()` | `await Assert.ThrowsAnyAsync<OperationCanceledException>(...)`. VF-24 정정을 명시적으로 고정(취소가 경계에서 정직하게 전파) |
| T-20 | `Caller_Cancellation_Does_Not_Kill_Shared_Work` — 호출 A(ct 취소) + 호출 B(취소 없음). A를 취소한 뒤 붙잡기 해제 | B가 정상 완료하고 프레임을 받는다. `downloadCount`는 프레임 수와 같다(A의 취소가 공유 작업을 죽이지 않음, §7.1) |
| T-21 | `Concurrent_Callers_Share_One_Pass` — 붙잡기 상태에서 2회 호출 → 해제 | `downloadCount == 프레임 수`(중복 0), 두 결과 모두 같은 개수 |
| T-22 | `Late_Joiner_Gets_Replay_Of_Last_Progress` — 호출 A(진행 보고 수집) 시작 → 다운로드 붙잡힌 시점에 호출 B(수집기 B 부착) | B의 **첫 보고**가 `DownloadingImage`(진행 중 국면)다 — rev1의 문구 정체(리뷰 M1) 해소를 고정 |
| T-23 | `Progress_Reports_Local_Then_Server_Then_Downloads` — 동기 수집 스텁 `IProgress` | 순서에 `ResolvingLocal` → `QueryingServer` → `DownloadingImage(1/2)` → `DownloadingImage(2/2)` → `Completed` 포함 |
| T-24 | `Progress_Counter_Excludes_Cache_Hits` — 로컬에 f1 존재, DB에 f1·f2 | `DownloadingImage` 보고가 1건이고 `Total == 1`(§7.1) |
| T-25 | `LocalOnly_Returns_Fallback_When_Nothing_Local` | `GetLocalDefaultFramesAsync()` → 1개, `Id`가 `"fallback"`으로 시작, **`repo.DefaultCalls == 0`** |
| T-26 | `LocalOnly_Returns_Cached_Public_Frames` — `_store.CacheFromDb(DbFrame("f1"), …)` 선행 | 1개(`Name=="f1"`), `DefaultCalls == 0`, `_downloadCalls == 0` |
| T-27 | `LocalOnly_Does_Not_Join_Shared_Work` — 다운로드 붙잡힌 상태에서 `GetLocalDefaultFramesAsync()` 호출 | 붙잡기 해제 **전에** 완료(`Task.WaitAsync(TimeSpan.FromSeconds(2))` 내) — §6.3·§7.2 근거 고정 |
| T-28 | `Fallback_Concurrent_Creation_Produces_One_Valid_File` — 서로 다른 서비스 인스턴스 2개가 같은 `FallbackImagePath`를 향해 동시에 `GetLocalDefaultFramesAsync()` | 예외 없음, 최종 파일 1개가 `Cv2.ImRead`로 **디코드 가능**, `*.tmp` 잔재 없음, 두 결과의 `ImageUrl`이 모두 최종 경로(`.tmp` 아님) — 리뷰 M3 |
| T-29 | `Completed_Work_Is_Not_Cached_In_Service` — 1회 호출 완료 후 리플렉션/재호출로 확인 | 두 번째 호출이 새 패스를 시작한다(로컬 캐시 덕에 다운로드 0). `_inFlight` 해제 확인 |

**기존 테스트 불변**: `Cache_Hit_Skips_Download`, `Cache_Miss_Downloads_And_Dedups`,
`Concurrent_Calls_Download_Each_Frame_Once`, `Underscore_Name_Default_Frame_Still_Downloaded_And_Displayed`,
`User_Frames_Loaded_From_Local_Not_Db` — **한 줄도 수정하지 않는다.** 이 5건이 §7 구조 교체의 회귀 방벽이다(A-7).

### 10.4 `FrameSelectViewModelTests.cs` 확장 (9건)

기존 `MakeVm`/`StubRepo`/`StubLocalStore`/`MakeShell` 하네스를 재사용한다. `MakeVm`을 다음으로 확장한다.

```csharp
private static (FrameSelectViewModel vm, StubRepo repo, StubLocalStore local) MakeVm(
    UserRole? role,
    Func<string, CancellationToken, Task<byte[]?>>? downloadImage = null,   // 붙잡기 하네스(m2)
    Func<TimeSpan, TimeSpan>? loadDeadline = null)                          // 상한 축소 이음새(M4)
```
`downloadImage`는 `new FrameCatalogService(repo, local, logger: null, downloadImage: downloadImage)`로,
`loadDeadline`은 `FrameSelectViewModel` 마지막 생성자 인자로 전달한다.
⚠️ 붙잡기 테스트는 **`repo.Defaults.Add(...)`로 DB 프레임을 넣어야** `downloadImage`가 호출된다(m2).

| # | 테스트 | 기대 |
|---|--------|------|
| T-30 | `Initial_Phase_Is_Loading_Before_Enter` — VM 생성 직후 | `Phase == Loading`, `IsLoading == true`, `IsInteractive == false` |
| T-31 | `Enter_Completes_To_Ready` — `await vm.OnEnterAsync()` | `Phase == Ready`, `IsLoading == false`, `LoadNotice == ""`, `Frames.Count > 0` |
| T-32 | `Skip_Server_Wait_During_Load_Yields_Degraded` — 붙잡기 하네스 + `repo.Defaults` 1개. `OnEnterAsync()`를 await하지 않은 채 `SkipServerWaitCommand.Execute(null)` → 그 뒤 await | `Phase == Degraded`, `LoadNotice == FrameLoadPolicy.NoticeFor(Degraded)`, `Frames.Count > 0`(fallback) |
| T-33 | `Deadline_Expiry_Yields_Degraded` — `loadDeadline: _ => TimeSpan.FromMilliseconds(50)` + 붙잡기 하네스 | `Phase == Degraded`, 목록 비어 있지 않음. **이 테스트가 rev1에 없던 자동 취소 경로의 회귀 방벽이다**(리뷰 M4) |
| T-34 | `Local_Fallback_Failure_Yields_Failed` — `StubLocalStore`에 `ThrowOnLoadPublic` 플래그를 추가해 `LoadPublic()`이 `IOException`을 던지게 한다(파일시스템 조작·권한 변경 불필요) | `Phase == Failed`, `LoadNotice == FrameLoadPolicy.NoticeFor(Failed)`, `Frames.Count == 0`, 예외가 테스트 밖으로 전파되지 않음. **리뷰 C1이 요구한 도달성 고정** |
| T-35 | `Retry_From_Degraded_Returns_To_Ready` — T-32 상태에서 붙잡기 해제 후 `RetryLoadCommand` | `Phase == Ready`, `LoadNotice == ""` |
| T-36 | `Leave_During_Load_Does_Not_Mutate_State` — `OnEnterAsync()` 진행 중 `await vm.OnLeaveAsync()` → 원래 로딩 await | `Phase == Loading` 유지(결과 미기록), `Frames.Count == 0` — stale 가드 |
| T-37 | `Commands_Blocked_When_Not_Interactive` `[Theory]`(Loading)(Failed) — 해당 국면에서 `NextCommand`/`CreateFrameCommand`/`EditFrameCommand`/`RequestDeleteCommand` 실행 | 화면 전이 없음(`shell.CurrentState` 불변), `IsDeleteConfirmVisible == false` — §5.4 `IsInteractive` 가드(리뷰 m5) |
| T-38 | `Delete_Refresh_Does_Not_Reenter_Loading` — `Ready`에서 advanced_user로 로컬 프레임 삭제(`ConfirmDeleteCommand`) 완료 후 | `PropertyChanged` 수집 이력에 `Phase`→`Loading` 통지가 **없다**, `Phase == Ready` 유지, `LoadNotice == ""`, `DeleteNotice`는 종전대로 — §6.5 |

> **T-34가 결정론적으로 `Failed`에 도달하는 경로**(개발자가 그대로 따라갈 수 있게 명시):
> `LoadPublic()`이 던지면 ① `LoadDefaultFramesCoreAsync`의 첫 줄(try 밖)에서 예외 → 공유 작업 fault →
> `AwaitSharedAsync`의 await가 rethrow → ② VM의 `catch (Exception ex)`가 잡고 `interrupted = true` →
> ③ `SafeLocalFramesAsync()` → `GetLocalDefaultFramesAsync()` → `ResolveLocalFrames(null)`이 다시
> `LoadPublic()`을 호출해 또 던짐 → `SafeLocalFramesAsync`의 catch가 **빈 목록으로 축퇴** →
> ④ `finally`의 `Finalize(Loading, 0, true, quiet:false)` → `Failed`.
> 파일 권한을 건드리지 않고 스텁 한 줄로 §6.6 3행을 재현한다.

**기존 테스트 불변**: 권한 게이트 6종·삭제 흐름 5종·편집 게이트 6종·`Next_With_Auto_Setting_Resolves_Session_CutCount`
등 기존 **30개 테스트 케이스를 수정 없이** 통과시킨다.

### 10.5 `XamlResourceTests.cs` 확장 (4건)

| # | 테스트 | 기대 |
|---|--------|------|
| T-39 | `Spinner_Ring_Template_Exists_In_Theme` | `Assert.IsType<ControlTemplate>(theme["Spinner.Ring"])` |
| T-40 | `Spinner_Ring_Transform_Is_Animatable` (리뷰 M2 핵심) | STA 스레드에서 템플릿을 **실제 인스턴스화**하고 애니메이션이 시작되는지 확인 — 상세 코드는 아래 |
| T-41 | `FrameSelectView_Waiting_Bindings_Exist_On_Vm` (리뷰 M4) | `Views/FrameSelectView.xaml` 텍스트에 7개 멤버 이름이 있고, 각각이 `FrameSelectViewModel`의 public 프로퍼티/커맨드로 **리플렉션 조회된다** |
| T-42 | 기존 `Item1a_View_StaticResource_Keys_Resolve_In_Theme("FrameSelectView.xaml")` | **수정 없이** 통과 |

```csharp
/// <summary>
/// it20 M2: Spinner.Ring의 RotateTransform이 동결되지 않아 애니메이션 가능한지 headless로 확인한다.
/// 속성 경로 애니메이션((UIElement.RenderTransform).(RotateTransform.Angle))을 쓰면 템플릿 Seal로 동결된
/// Freezable에서 "Cannot animate on an immutable object instance"가 던져지고, 그 예외는 런타임에
/// DispatcherUnhandledException → 홈 복귀로 이어진다. x:Name 등록 방식이 이를 막는지 여기서 고정한다.
/// </summary>
[Fact]
public void Spinner_Ring_Transform_Is_Animatable()
{
    RunSta(() =>
    {
        var theme = LoadTheme();
        var template = (System.Windows.Controls.ControlTemplate)theme["Spinner.Ring"];

        var ctl = new System.Windows.Controls.Control { Template = template, Width = 56, Height = 56 };
        var host = new System.Windows.Controls.Border { Child = ctl };
        host.Measure(new System.Windows.Size(100, 100));
        host.Arrange(new System.Windows.Rect(0, 0, 100, 100));
        ctl.ApplyTemplate();

        var ring = template.FindName("SpinnerRing", ctl) as System.Windows.Shapes.Ellipse;
        Assert.NotNull(ring);
        var rot = ring!.RenderTransform as System.Windows.Media.RotateTransform;
        Assert.NotNull(rot);
        Assert.False(rot!.IsFrozen, "RotateTransform이 동결되면 Angle 애니메이션이 런타임 예외가 된다");

        // 실제 애니메이션 시작 — immutable이면 여기서 InvalidOperationException.
        var anim = new System.Windows.Media.Animation.DoubleAnimation(0, 360, new System.Windows.Duration(TimeSpan.FromSeconds(1)))
        {
            RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever
        };
        var sb = new System.Windows.Media.Animation.Storyboard();
        System.Windows.Media.Animation.Storyboard.SetTarget(anim, rot);
        System.Windows.Media.Animation.Storyboard.SetTargetProperty(
            anim, new System.Windows.PropertyPath(System.Windows.Media.RotateTransform.AngleProperty));
        sb.Children.Add(anim);
        sb.Begin();      // 예외 없이 통과해야 한다
        sb.Stop();
    });
}
```

```csharp
/// <summary>
/// it20 M4: 대기 오버레이 바인딩이 ViewModel 멤버와 일치하는지 정적으로 고정한다.
/// 원래 결함이 "IsLoading 선언은 있는데 바인딩이 없는 조용한 실패"(VF-2)였으므로, XAML 오타
/// (IsLoadng 등)나 VM 멤버 개명이 테스트로 드러나게 한다. 테마 키 해석 테스트는 Path를 보지 않는다.
/// (FrameEditor_Popup_Bindings_Resolve_On_Editor_Vm과 같은 계열의 정적 안전망)
/// </summary>
[Fact]
public void FrameSelectView_Waiting_Bindings_Exist_On_Vm()
{
    var text = File.ReadAllText(Path.Combine(FindAppViewsDir(), "FrameSelectView.xaml"));
    var vmType = typeof(MCPhoto.App.ViewModels.FrameSelectViewModel);

    foreach (var member in new[]
             {
                 "IsLoading", "IsLoadFailed", "IsDegraded",
                 "LoadingMessage", "LoadNotice",
                 "SkipServerWaitCommand", "RetryLoadCommand",
             })
    {
        Assert.Contains(member, text);                       // XAML이 이 이름을 실제로 바인딩한다
        Assert.NotNull(vmType.GetProperty(member));          // VM에 같은 이름의 public 멤버가 있다
    }
}
```

`Each_Theme_File_Resolves_Its_Own_StaticResource_References("Controls.xaml")`도 수정 없이 통과해야 한다
(`Spinner.Ring`이 참조하는 `Brush.Accent`는 Controls.xaml이 자체 병합).

### 10.6 수동 확인 시나리오 (Step 8)

> **실행 결과: 전 항목 `blocked`.** 구현 환경에서 앱 기동(GUI 실행)이 불가하여 M1~M11을 수행하지 못했다.
> 아래 표는 **미수행 상태의 기대 관측**으로 남아 있다 — 실기 확인은 별도로 필요하다.
>
> 대신 자동 테스트가 다음을 기계적으로 고정했다(수동 관측을 대체하지는 않지만 핵심 리스크를 덮는다):
>
> | 수동 항목 | 자동 대체 근거 | 남는 공백 |
> |-----------|----------------|-----------|
> | M1(오버레이 즉시 노출·스피너 회전) | T-40(템플릿 인스턴스화 + `IsFrozen == false` + `Storyboard.Begin()` 무예외), T-41(7개 바인딩 ↔ VM 멤버 대조), T-30(진입 전 `Loading`) | 첫 페인트 타이밍(A-1)·시각 대비(A-4)는 미확인 |
> | M2(무진행 30초 적정성, A-2) | — | **미확인**. 실회선 캐시 로그 간격 실측이 필요하다 |
> | M3·M4([기다리지 않고 시작] → 재시도) | T-32(Degraded + 안내), T-35(재시도 → Ready) | 시각·조작감 미확인 |
> | M5(네트워크 차단 → 30초 내 자동 종료) | T-33(상한 만료 → Degraded, 상한 축소 이음새로 결정론화) | 실제 30초 값의 체감 미확인 |
> | M6(오프라인 + 캐시 있음 → 안내 없음) | T-3(`Classify` 미중단 → Ready), 기존 `Cache_Hit_Skips_Download` | — |
> | M7(scrim 클릭 차단) | T-37(`Loading`·`Failed`에서 4개 커맨드 차단) | scrim 히트테스트 자체는 미확인 |
> | M8(설정 진입 후 복귀 시 현재 국면 즉시 표시) | T-22(늦은 합류자의 첫 보고 = `DownloadingImage`) | 화면 왕복 실동작 미확인 |
> | M9(삭제 후 오버레이 미번쩍) | T-38(`Phase`→`Loading` 통지 이력 없음) | — |
> | M10(편집기 모달 취소) | T-19(취소가 경계에서 `OperationCanceledException`으로 전파) | 모달 실동작 미확인 |
> | M11(fallback 쓰기 실패 → `Failed` 카드) | T-34(로컬 폴백 실패 → `Failed` + 안내 + 예외 미전파) | 읽기 전용 폴더 실환경 미확인 |

| # | 조작 | 기대 관측 |
|---|------|-----------|
| M1 | 실행 폴더 `Frame\` 비우고 + `%ProgramData%\MCPhoto\cache\fallback_frame.png` 삭제 후 앱 실행 → 즉시 [촬영하기] | 프레임 선택 진입과 **동시에** 카드형 대기 오버레이. 스피너가 **실제로 회전**한다. 문구가 `설치된 프레임을 확인하는 중…` 또는 `기본 프레임 내려받는 중… (1/N)`으로 표시되며 **(n/m) 카운터가 실제로 증가**한다(단일 비행 replay 덕에 prefetch 진행이 보인다 — rev1에서는 불가능했던 관측) |
| M2 | M1 상태로 정상 완료까지 대기 | 오버레이 사라지고 목록 표시, 인라인 안내 **없음**. 로그에 `기본 프레임 캐시: {Name}` N건. **캐시 로그 간격이 30초를 넘지 않는지** 확인(A-2 판정) |
| M3 | M1 상태에서 [기다리지 않고 시작] 클릭 | 즉시 오버레이 사라짐 + 목록(fallback 또는 부분 캐시) + `서버 프레임을 모두 가져오지 못해…` 안내 + [다시 시도] 노출 |
| M4 | M3 뒤 잠시 후 [다시 시도] | 백그라운드에서 계속 진행한 공유 작업의 결과가 반영되어 `Ready`(안내 사라짐), 목록에 서버 프레임 노출(§6.3) |
| M5 | 네트워크 차단(랜 분리) 후 M1 재현 | **30초 이내** 자동으로 오버레이 종료 + Degraded 안내. 무한 대기 없음. 로그에 `기본 프레임 대기 중단(무진행 30초/총 60초 상한 또는 사용자 건너뛰기)` |
| M6 | 네트워크 차단 + 로컬 캐시 있는 상태로 진입 | 오버레이가 잠깐 스치거나 아예 안 보이고 바로 목록. 안내 **없음**(§6.4). 이 상태로 2분 방치 시 유휴 경고가 정상 동작(§4.6) |
| M7 | 로딩 중 오버레이 아래 [다음]/[취소]/카드 클릭 시도 | 반응 없음(scrim 차단). 상단 바 [홈]은 정상 동작 |
| M8 | 로딩 중 상단 바 [설정] 진입 → [닫기] 복귀 | 대기 오버레이가 다시 뜨지만 문구가 **처음 문구가 아니라 현재 진행 국면**으로 즉시 표시된다(replay, 리뷰 m10). 예외·프리즈 없음 |
| M9 | 정상 상태에서 프레임 삭제 → 재로드 | 종전과 동일. **대기 오버레이가 번쩍이지 않는다**(§6.5), `DeleteNotice` 정상 노출 |
| M10 | `Frame\` 비운 상태로 편집기 진입 → [기존 프레임 불러오기] | 종전과 동일한 `프레임 목록을 불러오는 중...` 텍스트. 모달을 즉시 닫아도 예외·프리즈 없음(VF-24 정정 확인) |
| M11 | `%ProgramData%\MCPhoto\cache\` 폴더를 읽기 전용으로 만들고 네트워크 차단 후 M1 재현 | fallback 생성 실패 → **`Failed` 카드**(전면) + [다시 시도]/[메인으로]. 홈으로 튕기지 않고, 오버레이가 고착되지 않는다 — 리뷰 C1 수동 확인 |

---

## §11 구현 WBS

> 형식: `docs/templates/WBS_BLUEPRINT.md`. 각 Step은 **self-contained** — 대화 컨텍스트 없는 에이전트가 그 Step만 읽고 실행 가능.
> 공용 검증 명령:
> `dotnet build MCPhoto.sln -c Release --no-incremental --nologo -v q` → 경고 0 · 오류 0
> `dotnet test MCPhoto.sln -c Debug --nologo` → 전량 통과(**857 이상**, 최종 899 이상)
> **모든 신규/수정 파일은 UTF-8 no BOM 유지**(VF-26). BOM이 붙으면 리뷰에서 반려된다.
> 진행 보고는 `inspected` / `changed locally` / `verified locally` / `committed` 어휘를 쓴다.

### Step 1: Core 순수 정책·진행 표현 신설
- **Context Brief**: MCPhoto(WPF 포토부스)는 최초 실행 시 로컬에 기본 프레임이 없어 프레임 선택 화면 진입 후
  서버에서 프레임을 내려받을 때까지 기다린다. 그 대기에 진행 표시와 시간 상한을 부여하려 한다.
  이 Step은 판정·문구·상한을 담는 **의존성 없는 Core 타입 2개**만 만든다(아직 어떤 코드도 호출하지 않는다).
  기존 관례: `src/MCPhoto.Core/Settings/`에 `CutCountPolicy`·`QrDeliveryPolicy` 같은 순수 static 정책 클래스가
  있고 각각 전용 테스트 파일을 가진다. 한글 표시 라벨을 Core 순수 함수로 두는 관례도 있다(`UserRole.ToLabel()`).
- **대상 파일**: `src/MCPhoto.Core/Frames/FrameLoadPolicy.cs`(신규),
  `src/MCPhoto.Core/Frames/FrameCatalogProgress.cs`(신규),
  `tests/MCPhoto.Tests/FrameLoadPolicyTests.cs`(신규), `tests/MCPhoto.Tests/FrameCatalogProgressTests.cs`(신규)
- **선행 조건**: 없음
- **구현 내용**: 설계 §5.1·§5.2의 파일 전문을 그대로 작성한다. `FrameLoadPhase`의 **0번 값은 반드시 `Loading`**.
  테스트는 §10.1 T-1~T-13(+`Phase_Default_Is_Loading`), §10.2 T-14~T-18을 xUnit `[Fact]`/`[Theory]`로 작성.
  ⚠️ **Core 테스트에서 `MCPhoto.App` 타입을 참조하지 않는다** — 유휴 경고 불변식은
  `FrameLoadPolicy.IdleWarningReferenceSeconds` 상수 비교로 확인한다(T-12).
  `MCPhoto.Core`는 `ImplicitUsings`가 켜져 있어 `using System;`을 쓰지 않는다.
- **검증 명령**:
  ```
  dotnet build MCPhoto.sln -c Release --no-incremental --nologo -v q
  dotnet test MCPhoto.sln -c Debug --nologo --filter "FullyQualifiedName~FrameLoadPolicyTests|FullyQualifiedName~FrameCatalogProgressTests"
  grep -c "기본 프레임" src/MCPhoto.Core/Frames/FrameCatalogProgress.cs
  ```
- **완료 기준**:
  - [관측] 신규 테스트 19건 이상 전부 통과. `Finalize`가 **어떤 입력 조합에서도 `Loading`을 돌려주지 않고**(T-8),
    `NextDeadline(0)==30초`·`NextDeadline(45초)==15초`·`NextDeadline(60초)==0`이 출력으로 확인된다.
    `grep`이 1 이상 → 한글 주석·문구가 깨지지 않았다.
  - [non-goal] **어떤 기존 파일도 수정하지 않는다** — `git status`에 신규 4파일만. 기존 857건 결과 불변.
    Core 프로젝트가 App/Capture를 참조하지 않는다(의존 방향 불변).
  - [trigger] 없음(순수 함수 — 호출자 없이 테스트로만 구동).
- **롤백**: 신규 4파일 삭제(다른 Step과 완전 독립).
- [x] 완료 — verified locally: 신규 4파일, 테스트 35건 통과. ⚠️ `Finalize`의 quiet 갈래를 정정(이탈 D-1, §15)

### Step 2: `FrameCatalogService` — 세마포어를 단일 비행으로 교체 + 로컬 전용 API
- **Context Brief**: `src/MCPhoto.App/Services/FrameCatalogService.cs`의 `GetDefaultFramesAsync`는
  `SemaphoreSlim(1,1)` 게이트(`:24`, `:53`, `:96`)로 동시 호출을 **줄 세운다**. 게이트는 DB 조회 + 전 이미지
  다운로드 전 구간을 감싸므로, `App.OnStartup`의 prefetch(`App.xaml.cs:78`, ct 없음)가 잡고 있으면 화면 진입은
  그 완료까지 대기하고 앞 작업의 진행 상황을 알 수 없다. 이 Step은 줄 세우기를 **단일 비행**(같은 작업을 공유)으로
  바꾸고, 호출자별 취소를 `Task.WaitAsync(ct)` 경계로 옮긴다. 또 대기를 포기했을 때 쓸 **네트워크 없는
  로컬 전용 해석**을 공개한다. 진행 보고(다음 Step)와 fallback 쓰기 직렬화(Step 4)는 여기서 다루지 않는다.
- **대상 파일**: `src/MCPhoto.App/Services/FrameCatalogService.cs`, `tests/MCPhoto.Tests/FrameCatalogServiceTests.cs`
- **선행 조건**: 없음(Step 1과 병렬 가능 — 이 Step은 `FrameCatalogProgress`를 아직 쓰지 않는다)
- **구현 내용**:
  1. `_defaultFramesGate` 필드와 `WaitAsync`/`Release` 호출을 제거하고 §7.1의 단일 비행 골격을 넣는다:
     `_sync`/`_inFlight` 필드, `GetDefaultFramesAsync`(공개, 지금은 `progress` 인자 없음),
     `AwaitSharedAsync`(`shared.WaitAsync(ct).ConfigureAwait(true)`), `RunSharedLoadAsync`(finally에서 `_inFlight=null`).
     공유 본체는 `Task.Run(RunSharedLoadAsync)`로 시작한다 — 이것이 로컬 스캔·번들 디코드의 UI 스레드 점유
     (`LoadPublic`/`LoadBundleFrames`/`EnsureFallbackFrame`)를 함께 제거한다.
  2. 현행 본문을 `private async Task<IReadOnlyList<FrameTemplate>> LoadDefaultFramesCoreAsync()`로 옮긴다.
     내부 `ct` 사용을 **전부 `CancellationToken.None`으로** 바꾼다(공유 작업은 개별 호출자가 취소하지 않는다).
     ⚠️ 두 `catch (Exception ex)` 블록(`:71-74`, `:133-137`)은 **손대지 않는다**.
  3. 로컬 우선순위 체인(`:76-92`)을 §7.2의 `ResolveLocalFrames(IReadOnlyList<FrameTemplate>? preferLoaded)`로
     추출하고 본체 말미를 `return ResolveLocalFrames(local);`로 바꾼다. 기존 로그 3줄은 문구 그대로 옮긴다.
  4. `public Task<IReadOnlyList<FrameTemplate>> GetLocalDefaultFramesAsync(CancellationToken ct = default)`
     추가 — `Task.Run(() => ResolveLocalFrames(null), ct)`. **단일 비행에 합류하지 않는다**(§6.3 근거를 XML 주석에).
  5. `FrameCatalogServiceTests.cs`에 §10.3의 T-19·T-20·T-21·T-25·T-26·T-27·T-29를 추가한다.
- **검증 명령**:
  ```
  dotnet build MCPhoto.sln -c Release --no-incremental --nologo -v q
  dotnet test MCPhoto.sln -c Debug --nologo --filter "FullyQualifiedName~FrameCatalogServiceTests"
  grep -n "SemaphoreSlim" src/MCPhoto.App/Services/FrameCatalogService.cs
  ```
- **완료 기준**:
  - [관측] T-19·T-20·T-21·T-25·T-26·T-27·T-29 통과. 취소한 ct로 호출하면 `OperationCanceledException`이
    전파되고 **다른 호출자는 정상 완료**한다(T-20). 붙잡힌 공유 작업 중에도 `GetLocalDefaultFramesAsync`가
    2초 내 완료한다(T-27). 마지막 `grep`이 **출력 0줄**(세마포어 완전 제거).
  - [non-goal] 기존 5건(`Cache_Hit_Skips_Download`·`Cache_Miss_Downloads_And_Dedups`·
    `Concurrent_Calls_Download_Each_Frame_Once`·`Underscore_Name_Default_Frame_Still_Downloaded_And_Displayed`·
    `User_Frames_Loaded_From_Local_Not_Db`)을 **한 줄도 수정하지 않고** 통과 — 중복 다운로드 방지 불변 유지(A-7).
    `GetDefaultFramesAsync`의 시그니처·반환값·로그 문구·프레임 우선순위 불변.
    `App.xaml.cs`·`FramePickerViewModel.cs`·`ServiceRegistration.cs` 무변경.
  - [trigger] 공유 작업은 첫 호출자가 시작한다(`_inFlight ??=`). 이후 호출자는 시작하지 않고 합류만 한다.
    호출자 취소는 그 호출자의 `await`만 끊고 공유 작업은 계속된다.
- **롤백**: 이 Step 커밋 revert(세마포어 복원). 다른 Step 미착수 상태에서 독립적으로 되돌릴 수 있다.
- [x] 완료 — verified locally: 세마포어 제거 확인(grep 0줄), `FrameCatalogServiceTests` 12건 통과(기존 5건 무수정)

### Step 3: `FrameCatalogService` — 진행 중계와 replay
- **Context Brief**: Step 2에서 `FrameCatalogService.GetDefaultFramesAsync`는 동시 호출이 하나의 공유 작업을
  나눠 쓰는 구조가 됐다. 이제 그 작업의 진행 국면(로컬 확인 / 서버 조회 / n번째 다운로드 / 완료)을
  **구독 중인 모든 호출자에게 방송**하고, 늦게 합류한 호출자에게는 최근 국면을 1회 replay한다.
  이것이 없으면 화면은 "무엇을 기다리는지" 알 수 없고, 무진행 상한(Step 6)이 정상 진행 중인 작업을 잘라낸다.
  진행 표현 타입은 `MCPhoto.Core.Frames.FrameCatalogProgress`(Step 1 산출물)다.
- **대상 파일**: `src/MCPhoto.App/Services/FrameCatalogService.cs`, `tests/MCPhoto.Tests/FrameCatalogServiceTests.cs`
- **선행 조건**: Step 1(`FrameCatalogProgress`), Step 2(단일 비행 골격)
- **구현 내용**:
  1. `_observers`(`List<IProgress<FrameCatalogProgress>>`)·`_lastProgress` 필드와 `ReportShared` 메서드를
     §7.1 전문대로 추가한다. 구독자 예외는 삼키고 warning 로그만 남긴다.
  2. `GetDefaultFramesAsync`에 optional 파라미터를 **뒤에** 추가한다:
     `(CancellationToken ct = default, IProgress<FrameCatalogProgress>? progress = null)`.
     ⚠️ **오버로드를 만들면 안 된다** — 두 오버로드가 모두 전 인자 기본값을 가지면 `App.xaml.cs:95`의
     무인자 호출이 CS0121(모호 호출)로 컴파일 실패한다.
     lock 안에서 구독 등록 + 스냅샷 획득, lock **밖에서** `progress?.Report(snapshot)`(replay).
     `AwaitSharedAsync`의 finally에서 구독 제거.
  3. `LoadDefaultFramesCoreAsync`에 §7.1의 보고 4지점을 삽입한다: `ResolvingLocal`(시작),
     `QueryingServer`(DB 조회 전), `DownloadingImage(i+1, pending.Count)`(다운로드 루프),
     `Completed`(반환 직전). 다운로드 대상만 `pending` 리스트로 사전 계산해 분모에서 캐시 히트를 제외한다.
     `'_'` 포함 이름 경고(`:118-122`)와 dedup 규약은 그대로 유지한다.
  4. `FrameCatalogServiceTests.cs`에 §10.3의 T-22·T-23·T-24를 추가한다.
     T-22(replay)는 붙잡기 하네스가 필요하며 `repo.DefaultFrames`에 DB 프레임 2개를 넣어야 한다.
- **검증 명령**:
  ```
  dotnet build MCPhoto.sln -c Release --no-incremental --nologo -v q
  dotnet test MCPhoto.sln -c Debug --nologo --filter "FullyQualifiedName~FrameCatalogServiceTests"
  ```
- **완료 기준**:
  - [관측] T-22·T-23·T-24 통과. 보고 순서가
    `ResolvingLocal → QueryingServer → DownloadingImage(1/2) → DownloadingImage(2/2) → Completed`이고,
    **다운로드 진행 중에 합류한 두 번째 호출자의 첫 보고가 `DownloadingImage`**다(replay 작동).
    캐시 히트가 있는 경우 `Total`이 다운로드 대상 수와 같다(분모에서 제외).
  - [non-goal] 기존 호출부 3곳(`App.xaml.cs:95`, `FramePickerViewModel.cs:56`, 기존 테스트) **무변경**으로
    컴파일·통과. Step 2의 non-goal(기존 5건 불변)도 계속 유지. `_observers`가 누적되지 않는다
    (테스트 종료 후 리플렉션으로 0개 확인 — 선택).
  - [trigger] 진행 보고는 `progress` 인자가 주어진 호출에만 전달된다(`progress?.Report` — null이면 no-op).
    replay는 구독 등록 직후 정확히 1회.
- **롤백**: `_observers`/`_lastProgress`/`ReportShared`/`progress` 파라미터·보고 4지점 제거(Step 2 상태로 복귀).
- [x] 완료 — verified locally: `FrameCatalogServiceTests` 15건 통과(replay·보고 순서·캐시 히트 분모 제외)

### Step 4: fallback PNG 생성의 쓰기 경합 제거
- **Context Brief**: `src/MCPhoto.App/Services/FrameCatalogService.cs`의 `EnsureFallbackFrame()`(`:229-236`)은
  `File.Exists` 검사 후 `FallbackFrameRenderer.Create()`를 호출하는데, 그 안에서 `Cv2.ImWrite`로
  `%ProgramData%\MCPhoto\cache\fallback_frame.png`를 **직접 쓴다**. Step 2 이후 이 경로는 두 곳에서
  도달 가능하다 — 공유 작업의 종단과 로컬 전용 API. 두 스레드가 같은 파일에 `ImWrite`하면 공유 위반으로
  실패하거나 반쯤 쓰인 PNG가 남고, 후자는 이미지 디코드 실패로 이어진다. 이 Step이 그 경합을 없앤다.
- **대상 파일**: `src/MCPhoto.App/Services/FrameCatalogService.cs`, `tests/MCPhoto.Tests/FrameCatalogServiceTests.cs`
- **선행 조건**: Step 2(`ResolveLocalFrames`·`GetLocalDefaultFramesAsync`)
- **구현 내용**:
  1. `private static readonly object _fallbackWriteSync = new();` 추가.
  2. `EnsureFallbackFrame()`을 §7.2 전문대로 바꾼다: lock 안에서 존재 검사 → 임시 파일(`경로 + ".tmp"`)에
     렌더 → `Directory.CreateDirectory` → `File.Move(temp, final, overwrite: true)` →
     **`template.ImageUrl = FallbackImagePath;`로 최종 경로 정정**.
     ⚠️ 마지막 정정을 빠뜨리면 `ImageUrl`이 사라진 `.tmp`를 가리켜 카드가 placeholder로 뜬다
     (`FallbackFrameRenderer.Create`가 인자 경로를 `ImageUrl`에 담기 때문 — `FallbackFrameRenderer.cs:17`).
  3. `FallbackFrameRenderer.cs`는 **수정하지 않는다**(호출측에서 원자 교체).
  4. `FrameCatalogServiceTests.cs`에 §10.3의 T-28을 추가한다.
- **검증 명령**:
  ```
  dotnet test MCPhoto.sln -c Debug --nologo --filter "FullyQualifiedName~FrameCatalogServiceTests|FullyQualifiedName~FallbackFrameTests|FullyQualifiedName~BundleFrameTests"
  ```
- **완료 기준**:
  - [관측] T-28 통과: 동시 2회 로컬 전용 호출에서 예외 없음, 최종 PNG가 `Cv2.ImRead`로 디코드 가능,
    `*.tmp` 잔재 0개, 두 결과의 `ImageUrl`이 모두 최종 경로(`.tmp`로 끝나지 않음).
    기존 `FallbackFrameTests`·`BundleFrameTests` 전량 통과.
  - [non-goal] `src/MCPhoto.Capture/FallbackFrameRenderer.cs` **무변경**. fallback 이미지의 시각(하양 배경·
    4슬롯 가이드)·크기(1200×1600)·슬롯 좌표 불변. 이미 파일이 있으면 **재생성하지 않는다**(기존 동작).
  - [trigger] 생성은 `File.Exists(FallbackImagePath) == false`일 때만. lock은 생성 경로에서만 경합한다.
- **롤백**: `EnsureFallbackFrame`을 원래 2줄 형태로 되돌리고 lock 필드·테스트 1건 삭제.
- [x] 완료 — verified locally: T-28 통과(디코드 가능·잔재 0·ImageUrl 최종 경로). ⚠️ 임시 경로를 `.tmp.png`로 정정(이탈 D-2, §15)

### Step 5: 공유 스피너 리소스 `Spinner.Ring` 신설 + 애니메이션 가능성 검증
- **Context Brief**: 회전 스피너 마크업이 `src/MCPhoto.App/Views/CaptureView.xaml:74-93`에만 인라인으로 있고
  공유 리소스가 아니다. 프레임 선택 화면의 대기 오버레이에서도 같은 스피너가 필요하므로
  `src/MCPhoto.App/Themes/Controls.xaml`에 재사용 가능한 `ControlTemplate`으로 넣는다.
  `Controls.xaml`은 `Brushes/Metrics/Typography`를 자체 병합하므로 `Brush.Accent`를 StaticResource로 참조할 수 있고,
  `ControlTemplate` 안에서 이름으로 요소를 겨냥하는 관례가 이미 있다(`:309`·`:317`의 `TargetName="Bd"`).
  ⚠️ **핵심 제약**: `ResourceDictionary`의 `ControlTemplate`에 인라인 선언된 Freezable은 템플릿 Seal 시
  동결될 수 있고, 그 상태에서 속성 경로 애니메이션
  (`(UIElement.RenderTransform).(RotateTransform.Angle)`)은 런타임에
  `InvalidOperationException: Cannot animate … on an immutable object instance`를 던진다. 그 예외는
  `App.xaml.cs`의 `DispatcherUnhandledException` → `TryReturnHome()`으로 이어져 **손님이 촬영을 누르면
  홈으로 튕긴다**. 따라서 `RotateTransform`에 `x:Name`을 달고 `Storyboard.TargetName`으로 겨냥한다
  (= `CaptureView.xaml:74-93`에서 동작이 확인된 형태).
- **대상 파일**: `src/MCPhoto.App/Themes/Controls.xaml`, `tests/MCPhoto.Tests/XamlResourceTests.cs`
- **선행 조건**: 없음(Step 1~4와 병렬 가능)
- **구현 내용**: `FrameCard` 공유 리소스 블록(`Themes/Controls.xaml:294-361`) 뒤에 §4.5의
  `ControlTemplate x:Key="Spinner.Ring"` 전문을 그대로 추가한다. 요점 3가지 —
  ① `RotateTransform x:Name="SpinnerRotate"` + `Storyboard.TargetName="SpinnerRotate"` `TargetProperty="Angle"`
  ② 시작은 `EventTrigger RoutedEvent="FrameworkElement.Loaded"`(발화 보장. 오버레이가 처음부터 Visible이라
  `IsVisible=True` 진입 트리거는 전이가 관측되지 않을 수 있다) ③ 정지/재개는 `Trigger Property="IsVisible"
  Value="False"`의 `PauseStoryboard`/`ResumeStoryboard`(숨은 뒤 CPU 낭비 방지).
  `XamlResourceTests.cs`에 §10.5의 T-39·T-40을 추가한다(T-40 코드는 §10.5에 전문 제시).
- **검증 명령**:
  ```
  dotnet build MCPhoto.sln -c Release --no-incremental --nologo -v q
  dotnet test MCPhoto.sln -c Debug --nologo --filter "FullyQualifiedName~XamlResourceTests"
  ```
- **완료 기준**:
  - [관측] `Spinner_Ring_Template_Exists_In_Theme`가 `ControlTemplate` 타입으로 통과.
    **`Spinner_Ring_Transform_Is_Animatable`이 통과** — 템플릿을 실제 인스턴스화해
    `RotateTransform.IsFrozen == false`이고 `Storyboard.Begin()`이 예외 없이 실행된다.
    `Each_Theme_File_Resolves_Its_Own_StaticResource_References("Controls.xaml")`·
    `Theme_Loads_And_Core_Keys_Resolve`가 **수정 없이** 통과.
  - [non-goal] `Views/CaptureView.xaml`을 **수정하지 않는다** — 촬영 화면의 인라인 스피너는 그대로 둔다.
    기존 리소스 키 18개의 내용·이름 불변. 템플릿 내부 이름(`SpinnerRing`/`SpinnerRotate`/`SpinnerSpin`)은
    템플릿 namescope 지역이므로 전역 키를 추가하지 않는다.
  - [trigger] 회전은 `Loaded` 시 시작하고 `IsVisible`이 false가 되면 일시정지, 다시 true가 되면 재개한다.
    아직 이 템플릿을 쓰는 화면이 없으므로 이 Step에서는 실행 관측이 아니라 **테스트로** 검증한다.
- **롤백**: `Controls.xaml`의 추가 블록과 테스트 2건 삭제(다른 Step과 독립).
- [x] 완료 — verified locally: T-39·T-40 통과(`IsFrozen == false` + `Storyboard.Begin()` 무예외). R-7 기계적 배제됨

### Step 6: `FrameSelectViewModel` 국면·수명·상한·커맨드
- **Context Brief**: `src/MCPhoto.App/ViewModels/FrameSelectViewModel.cs`는 화면 진입 시 `OnEnterAsync()` →
  `ReloadFramesAsync()`로 프레임 목록을 채운다. `bool IsLoading` 플래그가 있지만 **XAML이 바인딩하지 않아**
  최초 실행 다운로드 중 화면이 빈 목록으로 남는다. 이 Step은 VM에 4국면 상태(`Loading/Ready/Degraded/Failed`),
  진행 문구, 무진행·총 상한 취소, 사용자 탈출 커맨드를 넣는다. XAML은 다음 Step에서 붙인다
  (이 Step만으로는 화면 변화 없음).
  Core 타입(`FrameLoadPhase`/`FrameLoadPolicy`/`FrameCatalogProgress`)은 Step 1,
  `GetLocalDefaultFramesAsync`는 Step 2, `progress` 파라미터는 Step 3 산출물이다.
  ⚠️ **가장 중요한 불변식**: `Phase`가 `Loading`에 남는 경로가 없어야 한다. 확정은 반드시 `finally`에서
  `FrameLoadPolicy.Finalize`로 수행한다 — happy-path 말미 대입으로 두면 로컬 폴백이 던질 때
  `AppShellViewModel.cs:217-221`이 예외를 삼켜 전면 오버레이가 영구 고착된다.
- **대상 파일**: `src/MCPhoto.App/ViewModels/FrameSelectViewModel.cs`, `tests/MCPhoto.Tests/FrameSelectViewModelTests.cs`
- **선행 조건**: Step 1, Step 2, Step 3 (Step 4·5는 무관 — 병렬 가능)
- **구현 내용**:
  1. `[ObservableProperty] private bool _isLoading;`(`:25`)을 **삭제**하고 §5.3의 `Phase` +
     파생 프로퍼티 4개(`IsLoading`/`IsLoadFailed`/`IsDegraded`/`IsInteractive`) + `LoadingMessage` +
     `LoadNotice`로 교체. `Phase` 초기값은 `FrameLoadPhase.Loading`.
  2. 생성자 마지막에 선택 인자 `Func<TimeSpan, TimeSpan>? loadDeadline = null`을 추가하고
     `_loadDeadline = loadDeadline ?? FrameLoadPolicy.NextDeadline;`로 보관(§5.3 — 테스트 이음새).
     MS.DI는 기본값 있는 미등록 파라미터를 허용한다(`FrameCatalogService`가 같은 형태로 등록·동작 중).
  3. `private enum ReloadReason { Enter, Refresh }` 추가(§6.1).
  4. `ReloadFramesAsync()`를 §6.2의 **전문 그대로** 재작성한다. 포함 요소:
     `CancelLoad()`/`_loadCts`(Cancel만, Dispose는 본체 finally), `Stopwatch clock`,
     `ArmDeadline(cts, clock)`(최초 + 진행 보고마다 재무장), `Progress<FrameCatalogProgress>`(UI 스레드 생성,
     **stale 가드 포함**), 각 await 뒤 `ReferenceEquals(cts, _loadCts)` 가드,
     `SafeLocalFramesAsync()`(로컬 폴백 실패 시 `Array.Empty<FrameTemplate>()` 축퇴),
     그리고 `finally`의 `Finalize` + `NoticeFor` + `_loadCts = null` + `cts.Dispose()`.
     권한 플래그 3개·`IsLoggedIn` 계산은 **종전 로직 그대로** 유지한다(it16 E4).
  5. `OnEnterAsync()` → `ReloadFramesAsync(ReloadReason.Enter)`,
     `ConfirmDelete()` 말미(`:145`) → `await ReloadFramesAsync(ReloadReason.Refresh);`.
  6. `OnLeaveAsync()` override 추가 → `CancelLoad()`.
  7. `[RelayCommand] private void SkipServerWait()` + `[RelayCommand] private Task RetryLoad()` 추가(§6.2).
  8. `Next`/`CreateFrame`/`EditFrame`/`RequestDelete` 선두에 **`if (!IsInteractive) return;`** 가드 추가
     (`IsLoading`이 아니라 `IsInteractive` — `Failed`에서도 막아야 §5.4 매트릭스와 일치).
  9. `FrameSelectViewModelTests.cs`: `MakeVm`을 §10.4 시그니처로 확장하고 T-30~T-38을 추가한다.
     - 붙잡기 테스트(T-32·T-33·T-35·T-36)는 **`repo.Defaults`에 DB 프레임을 넣어야** `downloadImage`가 호출된다.
       DB 프레임은 `ImageUrl`이 비어 있지 않아야 한다(`TryCacheAsync`가 빈 URL을 즉시 `null` 반환하므로).
     - T-34용으로 `StubLocalStore`에 `public bool ThrowOnLoadPublic { get; set; }`을 추가하고
       `LoadPublic()`이 `true`면 `throw new IOException("테스트: 로컬 스캔 실패")`하게 한다.
       도달 경로는 §10.4의 인용 블록에 단계별로 적혀 있다.
- **검증 명령**:
  ```
  dotnet build MCPhoto.sln -c Release --no-incremental --nologo -v q
  dotnet test MCPhoto.sln -c Debug --nologo --filter "FullyQualifiedName~FrameSelectViewModelTests"
  ```
- **완료 기준**:
  - [관측] T-30~T-38 통과. 특히 **T-33(상한 만료 → Degraded)** 과 **T-34(로컬 폴백 실패 → Failed)** 가
    통과해 자동 취소 경로와 `Failed` 도달성이 회귀 방벽을 갖는다. 진입 전 `Loading`, 정상 완료 후 `Ready`,
    로딩 중 `SkipServerWait` 후 `Degraded`+안내, 이어서 `RetryLoad`로 `Ready` 복귀, 로딩 중 `OnLeaveAsync` 후 상태 미기록.
  - [non-goal] 기존 `FrameSelectViewModelTests` **30개 케이스가 수정 없이 통과**한다
    (권한 게이트·삭제 흐름·편집 게이트·`Next_With_Auto_Setting_Resolves_Session_CutCount`).
    삭제 후 재스캔이 `Phase`를 `Loading`으로 되돌리지 않는다(T-38).
    `FramePickerViewModel.IsLoading`은 건드리지 않는다. XAML 파일을 이 Step에서 열지 않는다.
  - [trigger] 상한 취소는 `ArmDeadline`이 계산한 시점에만 발동하고, 진행 보고가 오면 재무장되어
    **정상 진행 중에는 발동하지 않는다**. 사용자 탈출은 [기다리지 않고 시작] 클릭 시에만(`Loading`이 아니면 no-op).
    Degraded 안내·`Phase` 갱신은 `finally`에서 1회.
- **롤백**: 이 Step 커밋 revert(XAML 미변경이므로 화면 회귀 없음 — 오버레이 없는 종전 상태로 복귀).
- [x] 완료 — verified locally: `FrameSelectViewModelTests` 39건 통과(T-30~T-38 신규 10케이스, 기존 29케이스 무수정)

### Step 7: `FrameSelectView.xaml` 대기 오버레이·실패 카드·인라인 안내 + 바인딩 검증
- **Context Brief**: `src/MCPhoto.App/Views/FrameSelectView.xaml`은 3행 `Grid`(제목 / 프레임 `ListBox` /
  하단 버튼)이며, 삭제 확인 팝업이 `Grid.RowSpan="3"` + `Brush.Scrim` + 중앙 `Card` 오버레이로 이미 구현돼 있다
  (`:57-80`). 같은 관례로 대기 오버레이와 실패 카드를 추가한다. VM(Step 6)이
  `IsLoading`/`IsLoadFailed`/`IsDegraded`/`LoadingMessage`/`LoadNotice`/`SkipServerWaitCommand`/
  `RetryLoadCommand`를 노출한다. 스피너는 `Spinner.Ring`(Step 5).
  ⚠️ 이 화면의 하위 배경은 `Brush.Bg`(흰색)다 — `CaptureView`처럼 scrim 위에 흰 글자(`Brush.OnAccent`)를 쓰면
  대비가 무너진다. **불투명 `Card` 안에 기본 글자색**을 쓴다.
- **대상 파일**: `src/MCPhoto.App/Views/FrameSelectView.xaml`, `tests/MCPhoto.Tests/XamlResourceTests.cs`
- **선행 조건**: Step 5(`Spinner.Ring`), Step 6(VM 멤버)
- **구현 내용**:
  1. 삭제 확인 오버레이(`:57-80`) **뒤에** 대기 오버레이를 추가한다(z-order 최상단, §4.1 도식).
     `Grid Grid.RowSpan="3" Background="{StaticResource Brush.Scrim}"`
     + `Visibility="{Binding IsLoading, Converter={StaticResource BoolToVis}}"`
     + 중앙 `Border Style="{StaticResource Card}" Padding="32" Effect="{StaticResource Shadow.Pop}"`
       `Background="{StaticResource Brush.Bg}" HorizontalAlignment="Center" VerticalAlignment="Center" MinWidth="380"`.
     내부 `StackPanel`:
     `<Control Template="{StaticResource Spinner.Ring}" Width="56" Height="56" HorizontalAlignment="Center" Focusable="False" IsTabStop="False" />`
     → `TextBlock Text="{Binding LoadingMessage}"`(`Text.Body`, `HorizontalAlignment=Center`,
     `TextWrapping=Wrap`, `TextAlignment=Center`, `Margin="0,20,0,0"`)
     → 고정 보조 문구 `처음 실행할 때는 기본 프레임을 서버에서 한 번 내려받습니다.`
     (`Text.Caption`, `Brush.Text.Muted`, `TextWrapping=Wrap`, `TextAlignment=Center`, `Margin="0,8,0,0"`)
     → `Button Content="기다리지 않고 시작" Style="{StaticResource Button.Ghost}"`
     `Command="{Binding SkipServerWaitCommand}" HorizontalAlignment="Center" Margin="0,20,0,0"`
     `AutomationProperties.Name="서버 대기 건너뛰기"`.
  2. 그 뒤에 실패 카드를 추가한다(§4.3): 동일 scrim + `Card`,
     `Visibility="{Binding IsLoadFailed, Converter={StaticResource BoolToVis}}"`,
     제목 `프레임을 준비하지 못했습니다`(`Text.H2`), 본문 `{Binding LoadNotice}`(`Text.Body`,
     `Brush.Text.Muted`, `TextWrapping=Wrap`, `TextAlignment=Center`), 버튼 줄
     `[다시 시도]`(`Button.Primary`, `RetryLoadCommand`) + `[메인으로]`(`Button.Ghost`, `CancelCommand`).
  3. 하단 `StackPanel`(`:82`) 안, `DeleteNotice` `TextBlock` **위**에 Degraded 인라인 줄을 추가한다(§4.4):
     `StackPanel Orientation="Horizontal" HorizontalAlignment="Center" Margin="0,0,0,8"`
     + `Visibility="{Binding IsDegraded, Converter={StaticResource BoolToVis}}"`
     + `TextBlock Text="{Binding LoadNotice}"`(`Text.Body`, `Brush.Text.Muted`, `VerticalAlignment=Center`)
     + `Button Content="다시 시도" Style="{StaticResource Button.Ghost}" Command="{Binding RetryLoadCommand}"`
       `Margin="12,0,0,0"` `AutomationProperties.Name="프레임 목록 다시 불러오기"`.
  4. 신규 리소스 키를 **만들지 않는다** — 참조는 전부 기존 테마/App 키(`Brush.Scrim`·`Brush.Bg`·`Card`·
     `Shadow.Pop`·`Text.H2`·`Text.Body`·`Text.Caption`·`Brush.Text.Muted`·`Button.Ghost`·`Button.Primary`·
     `Spinner.Ring`·`BoolToVis`).
  5. `XamlResourceTests.cs`에 §10.5의 T-41(`FrameSelectView_Waiting_Bindings_Exist_On_Vm`)을 추가한다
     — 코드 전문은 §10.5에 있다.
- **검증 명령**:
  ```
  dotnet build MCPhoto.sln -c Release --no-incremental --nologo -v q
  dotnet test MCPhoto.sln -c Debug --nologo --filter "FullyQualifiedName~XamlResourceTests"
  grep -c "기다리지 않고 시작" src/MCPhoto.App/Views/FrameSelectView.xaml
  ```
- **완료 기준**:
  - [관측] 빌드 경고 0·오류 0. `Item1a_View_StaticResource_Keys_Resolve_In_Theme("FrameSelectView.xaml")`이
    **수정 없이** 통과. **`FrameSelectView_Waiting_Bindings_Exist_On_Vm`이 통과** — 7개 바인딩 이름이 XAML에
    존재하고 전부 VM의 public 멤버로 리플렉션 조회된다(오타 방어). `grep`이 1 → 한글 문구 정상.
  - [non-goal] 기존 요소(`ListBox`·`FrameCard` 카드·삭제 ✕ `MultiBinding`·삭제 확인 팝업·하단 4버튼·
    `DeleteNotice` 줄)의 마크업·바인딩 경로가 **불변**이다. 삭제 ✕의 `RelativeSource AncestorType=ListBox`
    경로가 유지된다(신규 오버레이를 `ListBox` 안에 넣지 않는다).
    `MainWindow.xaml`·`Views/CaptureView.xaml` 무변경. 스피너 `Control`이 탭 순서에 들어가지 않는다.
  - [trigger] 대기 오버레이는 `IsLoading==true`에서만, 실패 카드는 `IsLoadFailed==true`에서만,
    인라인 안내는 `IsDegraded==true`에서만 보인다 — `Ready`에서는 세 요소 모두 `Collapsed`이며
    화면은 종전과 픽셀 동일하다.
- **롤백**: 추가한 3개 블록과 테스트 1건 삭제(화면이 종전 상태로 복귀).
- [x] 완료 — verified locally: T-41 + `Item1a_View_StaticResource_Keys_Resolve_In_Theme("FrameSelectView.xaml")` 통과(마크업 컴파일 경고 0)

### Step 8: 전량 회귀 + 실행 관측
- **Context Brief**: Step 1~7로 프레임 선택 화면의 기본 프레임 다운로드 대기 UI가 완성됐다.
  이 Step은 코드를 바꾸지 않고 **회귀 전량 + 실제 앱 실행 관측**만 수행한다. 최초 실행 상황을 재현하려면
  실행 폴더의 `Frame\` 하위 파일(`*.png`/`*.slots`)을 비우고 `%ProgramData%\MCPhoto\cache\fallback_frame.png`도
  지운다. 로그는 `%ProgramData%\MCPhoto\logs\mcphoto-{날짜}.log`다.
- **대상 파일**: 없음(검증 전용). 관측 결과를 §10.6 표에 기록한다.
- **선행 조건**: Step 1~7 전부
- **구현 내용**: §10.6의 M1~M11을 순서대로 수행하고 각 행의 기대 관측과 실제를 대조한다.
  A-1(오버레이 즉시 페인트)·A-2(무진행 30초 적정성)·A-4(대비)·A-5(유휴 경고 미도달)·A-6(스피너 회전)을
  여기서 최종 판정한다. A-2가 어긋나면(정상 회선에서 캐시 로그 간격이 30초 초과)
  `FrameLoadPolicy.NoProgressTimeoutSeconds`(+필요 시 `MaxTotalWaitSeconds`)를 조정하고 Step 1의
  T-9~T-12 기대값을 함께 갱신한다.
  **M11(fallback 쓰기 실패 → Failed 카드)은 필수 항목이다** — 리뷰 C1의 수동 확인이다.
- **검증 명령**:
  ```
  dotnet build MCPhoto.sln -c Release --no-incremental --nologo -v q
  dotnet test MCPhoto.sln -c Debug --nologo
  dotnet run --project src/MCPhoto.App/MCPhoto.App.csproj -c Debug
  ```
- **완료 기준**:
  - [관측] 테스트 **899건 이상 통과 / 0 실패**, 빌드 경고 0·오류 0.
    M1에서 진입과 동시에 오버레이가 보이고 **스피너가 회전**하며 `(n/m)` 카운터가 증가한다.
    M5(네트워크 차단)에서 **30초 이내** 자동 종료 + Degraded. M11에서 **`Failed` 카드**가 뜬다.
  - [non-goal] M6(오프라인 + 캐시 있음)에서 **안내가 뜨지 않는다**. M9(삭제 후 재스캔)에서 오버레이가
    번쩍이지 않는다. M10(편집기 모달)은 종전 텍스트 그대로. 촬영 이후 흐름(Guide→Capture→CutSelect→Result→QR)
    에 변화가 없다. 어떤 시나리오에서도 홈으로 튕기지 않는다(스피너 애니메이션 예외 부재).
  - [trigger] Degraded 안내는 상한 초과 또는 [기다리지 않고 시작] 뒤에만 나타난다 —
    정상 완료(M2)·오프라인 즉시 실패(M6)에서는 나타나지 않는다.
- **롤백**: 없음(검증 전용). 실패 항목은 해당 Step으로 되돌려 수정한다.
- [x] 부분 완료 — 자동 회귀 verified locally(**916 통과 / 0 실패**, 동일 결과 3회 반복 = 경합 테스트 안정).
  **수동 관측 M1~M11은 blocked** — 실행 환경에서 앱 기동(GUI)이 불가하다(§10.6 참조).

### Step 9: 문서 동기화
- **Context Brief**: MCPhoto는 `docs/analysis/`에 앱 동작 명세를, `docs/design/`에 이터레이션 설계를 둔다.
  it20에서 프레임 선택 화면의 로딩 동작이 바뀌었으므로(대기 오버레이, 무진행·총 상한, 축소 진행, 단일 비행)
  분석 문서를 맞춘다. 설계 문서 자체는 이미 존재하므로 **WBS 체크 및 §10.6 관측 결과 기록**만 한다.
- **대상 파일**: `docs/analysis/11-exe-app-features.md`, `docs/analysis/13-client-behavior-spec.md`,
  `docs/design/wpf-architecture.md`, `docs/design/wpf-it20-frame-download-waiting-design.md`
- **선행 조건**: Step 8(관측 결과 확정)
- **구현 내용**:
  1. `11-exe-app-features.md`의 프레임 선택 절에 "최초 실행 시 서버 다운로드 대기 UI + 무진행 30초/총 60초 상한 +
     [기다리지 않고 시작] + 축소 진행" 3~5줄 추가.
  2. `13-client-behavior-spec.md`에 §5.4 국면별 UI 게이트 매트릭스를 옮겨 상태 전이표로 기재.
  3. `wpf-architecture.md`의 오버레이 목록에 "프레임 준비 대기(FrameSelectView, it20)" 행 추가 +
     `FrameCatalogService`의 동시성 모델을 "세마포어 직렬화 → 단일 비행 + 진행 중계"로 갱신.
  4. 이 설계 문서의 §11 체크박스를 완료 표시하고 §10.6에 실제 관측을 기록한다.
- **검증 명령**:
  ```
  grep -n "it20" docs/analysis/11-exe-app-features.md docs/analysis/13-client-behavior-spec.md docs/design/wpf-architecture.md
  git status --short
  ```
- **완료 기준**:
  - [관측] 3개 문서에 `it20` 참조가 각각 1개 이상 검색된다. 설계 문서 §11 체크박스가 모두 채워졌다.
  - [non-goal] 소스 파일(`src/**`)·테스트 파일 변경 **0건**(`git status`에 `docs/` 아래만).
    기존 문서의 다른 절(설정·촬영·업로드)을 재작성하지 않는다.
  - [trigger] 없음(문서 작업).
- **롤백**: 문서 변경 revert.
- [x] 완료 — `11-exe-app-features.md`(§3 대기 UI·동시성·라인번호 정정) · `13-client-behavior-spec.md`(§4.2 국면 전이표) ·
  `wpf-architecture.md`(§3.2 동시성 모델 + 화면별 오버레이 목록 신설) · 이 문서(§11 체크·§10.6 관측·§15 이탈 기록)

---

## §12 리스크와 명시적 비목표

### 12.1 리스크

| R | 리스크 | 완화 |
|---|--------|------|
| R-1 | 무진행 30초가 **단일 대형 이미지 다운로드**를 잘라낸다(진행 보고가 단계 단위라 한 프레임 내부 진척은 보고되지 않는다) | Step 8 M2에서 캐시 로그 간격을 실측해 판정(A-2). 상한은 상수 1개이므로 조정 비용이 낮다. 바이트 단위 진행 보고는 스트리밍 다운로드 재작성이 필요해 이번 범위 밖(§12.2) |
| R-2 | 세마포어 → 단일 비행 교체가 `it10 S3-2`(중복 다운로드 방지)의 회귀를 낳는다 | 기존 3건(`Concurrent_Calls_Download_Each_Frame_Once`·`Cache_Miss_Downloads_And_Dedups`·`Cache_Hit_Skips_Download`)을 **수정 없이** 통과시키는 것이 Step 2의 완료 기준(A-7). 신규 T-21이 공유 패스 1회를 직접 고정 |
| R-3 | 공유 작업이 `CancellationToken.None`으로 돌아 **아무도 취소할 수 없는** 백그라운드 작업이 된다 | 의도된 설계다(캐시 워밍). 상한은 `HttpClient` 타임아웃 100초 × 프레임 수이며 UI를 막지 않는다. 완료 시 `_inFlight`가 해제되어 누적되지 않는다(T-29). 앱 종료 시 프로세스와 함께 종료된다 |
| R-4 | `Task.Run` 경계로 로컬 파일 접근이 스레드풀에서 일어나 파일 잠금·예외 양상이 바뀐다 | `LocalFrameStore`/`FallbackFrameRenderer`는 UI 타입 미참조 순수 I/O(A-3). fallback 쓰기 경합은 Step 4의 lock + 원자 교체로 제거하고 T-28이 고정 |
| R-5 | `IsLoading`을 필드에서 파생 프로퍼티로 바꾸며 외부 대입 지점을 놓친다 | 대입 지점은 `ReloadFramesAsync` 내부뿐이고 XAML 바인딩이 0개다(VF-2). 컴파일 오류로 즉시 드러난다 |
| R-6 | 오버레이가 삭제 ✕ 버튼의 `RelativeSource AncestorType=ListBox` 바인딩을 깨뜨린다 | 신규 오버레이를 `ListBox` **밖**(Grid 직계 자식)에 둔다. Step 7 non-goal에 명시 |
| R-7 | `Spinner.Ring`의 Freezable 동결로 런타임 `InvalidOperationException` → 홈 복귀 | rev1의 속성 경로 방식을 폐기하고 `x:Name` + `TargetName`(CaptureView 검증 형태)로 바꿨다. **Step 5의 T-40이 템플릿을 실제 인스턴스화해 `IsFrozen == false` + `Storyboard.Begin()` 무예외를 기계적으로 확인**한다 — rev1처럼 수동 관측에만 의존하지 않는다 |
| R-8 | `finally`의 `Finalize`가 stale 판정을 잘못해 폐기된 VM 상태를 갱신한다 | 판정은 `ReferenceEquals(cts, _loadCts)` 한 조건이며 `CancelLoad()`가 `_loadCts=null`을 먼저 수행한다. T-36이 이탈 중 상태 미기록을 고정 |

### 12.2 명시적 비목표

- **`Views/CaptureView.xaml` 스피너 마이그레이션** — 촬영 화면은 리스크 경계 밖(§4.5).
- **바이트 단위 다운로드 진행률** — `FrameCatalogService`의 `DefaultDownloadAsync`가
  `HttpClient.GetByteArrayAsync` 한 줄이라(`Services/FrameCatalogService.cs:152`) 스트리밍 재작성이 필요하다.
  단계 단위 `(n/m)`으로 충분하며, 단일 프레임 장기 다운로드는 R-1로 관리한다.
- **`AppSettings`에 상한 ini 키 추가** — 운영자가 조절할 값이 아니다. 설정 항목이 늘면 게스트 편집 게이트·
  검증·문서까지 파생된다. 상수 2개로 두고 필요 시 릴리스로 조정한다(테스트 이음새는 코드에만 존재).
- **자동 재시도(지수 백오프)** — 사용자가 [다시 시도]를 누르는 명시적 트리거를 유지한다. 자동 재시도는
  키오스크에서 "언제 끝나는지 모르는 대기"를 다시 만든다.
- **`HttpClient` 타임아웃 값 변경**(`ServiceRegistration.cs:115`의 100초) — 업로드·계정 등 다른 호출과
  공유하는 값이다. 클라이언트 상한이 프레임 로딩만 유계로 만들므로 공유 값을 건드릴 이유가 없다.
- **`FrameCatalogService`의 두 `catch (Exception ex)` 수정**(rev1의 OCE 재던짐) — 단일 비행 경계의
  `Task.WaitAsync(ct)`가 같은 목적을 더 단순히 달성한다(§7.1).
- **유휴 워치독 변경** — `Failed`/`Degraded` 방치 시 유휴 경고가 겹쳐 홈 복귀하는 것은 의도된 최종 탈출(§4.6).
- **삭제 후 재로드가 DB 프레임을 재다운로드하는 현행 동작 변경** — §6.5 말미 판정.
- **`Frame/` 번들 기본 프레임 동봉**(VF-3) — 최초 실행 대기를 근본적으로 없애는 대안이지만 배포물 구성·
  프레임 저작권·설치본 크기 결정이 필요하다. 별도 이터레이션 주제로 남긴다(이 설계는 **대기가 발생한다는
  전제**에서 UX를 개선한다).
- **전역 busy 오버레이 도입** — §3 판정.
- **서버(`web/functions`) 변경** — 없음.

---

## §13 완결성 게이트 (developer 전달 전 자체 검사)

- [x] 검증된 사실(§1, VF-1~29) / 미검증 가정(§2, A-1~7) 목록이 분리되어 있다
- [x] 모든 가정에 검증 단계가 매핑되어 있다
      (A-1→Step 2·8, A-2→Step 8, A-3→Step 4, A-4→Step 7·8, A-5→Step 1·8, A-6→Step 5, A-7→Step 2)
- [x] 9개 Step 전부에 7개 필수 필드가 채워져 있다 (Context Brief / 대상 파일 / 선행 조건 / 구현 내용 / 검증 명령 / 완료 기준 / 롤백)
- [x] 모든 완료 기준이 관측 기반 3문 형식이다 (UI Step 5·7·8은 non-goal·trigger 포함)
- [x] 검증 명령이 자동 실행 가능한 형태다 (`dotnet build` / `dotnet test --filter` / `grep` / `git status`)
- [x] 단계 분할 3기준 충족: 독립 검증 가능 · 단일 리스크 · 주관 판단 없는 PASS/FAIL
- [x] 병렬 가능 Step 식별: Step 1 · 2 · 5는 상호 독립(선행 조건 없음)
- [x] **코드 조각이 컴파일 가능한 형태다** — §6.1의 `OnEnterAsync`/`RetryLoad`가 `ReloadReason` 인자를 넘기고,
      §6.2가 `ArmDeadline`/`SafeLocalFramesAsync` 정의를 포함하며, §7.1/§7.2가 필드 선언까지 제시한다(리뷰 m1)
- [x] **핵심 신규 동작 3개에 자동 테스트가 있다**: 상한 만료(T-33) · `Failed` 도달성(T-34) ·
      스피너 애니메이션 가능성(T-40) · 오버레이 바인딩 존재(T-41) (리뷰 C1·M2·M4)

---

## §14 설계 리뷰 P2 반영 대조표

| 항목 | 등급 | 반영 | 반영 위치 / 근거 |
|------|:----:|:----:|------------------|
| C1 `Loading` 고착 | 🔴 | ✅ 전면 반영 | §0.4·§6.2 `finally`의 `Finalize` 무조건 확정 + `SafeLocalFramesAsync` 빈 목록 축퇴 + §6.6 전수 논증 + T-34(자동)·M11(수동) |
| M1 wall-clock 예산 오진 | 🟠 | ✅ 반영(2·3안 결합) | 리뷰가 권한 2안(무진행)만으로는 **게이트 대기 구간이 예산을 먹는 구조가 남는다** — 3안(진행 replay)을 함께 채택해 §7.1 단일 비행으로 원인 제거 + §5.1 `NoProgressTimeout`/`MaxTotalWait` 2단 상한. 4안(문구 수정)은 `WaitingForOther` 국면 삭제로 대체. M1 기대 관측을 §10.6에서 현실에 맞게 갱신 |
| M2 스피너 미검증 | 🟠 | ✅ 전면 반영 | §4.5를 `x:Name` + `Storyboard.TargetName`(CaptureView 검증 형태)로 교체, `Loaded` 시작 + `IsVisible` Pause/Resume. T-40이 인스턴스화 + `IsFrozen==false` + `Storyboard.Begin()` 검증. R-7로 리스크 재기술 |
| M3 fallback 쓰기 경합 | 🟠 | ✅ 반영 | §7.2 `_fallbackWriteSync` lock + `.tmp` → `File.Move(overwrite)` 원자 교체 + `ImageUrl` 정정. VF-11로 "쓰기 경로가 있다"를 사실로 등재. Step 4 + T-28 |
| M4 테스트 공백 | 🟠 | ✅ 반영 | 상한 주입: §5.3 `loadDeadline` 생성자 이음새(VF-28 관례) → T-33이 자동 취소 경로 고정. 바인딩 검증: T-41이 XAML 텍스트 + VM 리플렉션 대조(§10.5 전문) |
| m1 코드 조각 컴파일 불가 | 🟡 | ✅ | §6.1·§6.2가 `ReloadReason` 인자·헬퍼 정의·필드 선언까지 포함. §13 게이트 항목 추가 |
| m2 T23/T25 하네스 불완전 | 🟡 | ✅ | §10.3·§10.4에 "`repo.Defaults`/`repo.DefaultFrames`에 DB 프레임을 넣어야 `downloadImage`가 호출된다" 명시 + `MakeVm` 확장 시그니처 제시 |
| m3 경로 오표기 | 🟡 | ✅ | §1 상단에 경로 표기 규칙 추가. `AppShellViewModel.cs`·`App.xaml.cs`·`ServiceRegistration.cs`를 `src/MCPhoto.App/` 직하로 전부 정정 |
| m4 VF-24 과장 | 🟡 | ✅ | VF-24를 "관측 동작이 바뀌지만 취소 계기가 모달 종료·재오픈뿐이라 수용 가능"으로 정정 + T-19 회귀 테스트 + M10 수동 확인 |
| m5 가드가 매트릭스보다 느슨 | 🟡 | ✅ | `IsInteractive`(Ready·Degraded) 파생 프로퍼티 도입, 4개 커맨드 가드를 이 한 기준으로 통일. T-37이 `Loading`·`Failed` 두 국면을 `[Theory]`로 검증 |
| m6 progress stale 가드 없음 | 🟡 | ✅ | §6.2 `Progress<T>` 콜백 첫 줄에 `ReferenceEquals(cts, _loadCts)` 가드 |
| m7 스피너 탭 순서 | 🟡 | ✅ | §4.1·Step 7에 `Focusable="False" IsTabStop="False"` 명시 |
| m8 Failed + 유휴 경고 중첩 미정의 | 🟡 | ✅ | §4.6 신설(VF-15의 z-order 사실 + 중첩 시각 결과 + "의도된 최종 탈출" 판정 + `NotifyUserActivity` 호출 금지 근거) |
| m9 T6이 App 계층 의존 | 🟡 | ✅ | `FrameLoadPolicy.IdleWarningReferenceSeconds` 상수 도입, T-12를 Core 순수 비교로 교체. Step 1 구현 내용에 "App 타입 참조 금지" 명시 |
| m10 설정 진입 후 복귀 시 재시작 | 🟡 | ✅ | §3 말미에 경로 기술 + 단일 비행 replay로 재진입 비용이 "현재 국면 즉시 표시"로 축소됨을 명시. M8 수동 시나리오 추가 |

**미반영 항목: 없음.** 리뷰의 15개 지적을 모두 반영했다. M1만 리뷰가 제시한 4개 선택지 중 **2안+3안을
결합**했고(단독 2안은 게이트 대기 구간을 해소하지 못한다), 그 판단 근거를 §6.3에 남겼다.

---

## §15 구현 단계 이탈 기록 (developer → 설계 피드백)

구현 중 설계 코드 조각 그대로는 성립하지 않는 지점 2건을 발견해 **설계의 명시된 의도를 채택하는 방향으로** 정정했다.
둘 다 §5.1·§7.2 코드 조각의 결함이며, 판정·상한·구조 등 설계 결정 자체는 하나도 되돌리지 않았다.

### D-1 — `FrameLoadPolicy.Finalize`의 quiet 갈래가 `Loading`을 반환한다 (§5.1 ↔ §10.1 T-8 모순)

- **증상**: §5.1의 코드 `return current == FrameLoadPhase.Failed ? Ready : current;`는
  `Finalize(Loading, 2, *, quiet: true)`에서 `Loading`을 그대로 돌려준다.
  그런데 §0.4·§6.6은 "`Loading`으로 남는 경로가 존재하지 않는다"를 불변식으로 못 박고, §10.1 **T-8**은
  4국면 × quiet 2 × count{0,2} × interrupted 2 = 32조합에서 `Loading`이 한 번도 나오지 않을 것을 요구한다.
  설계가 자기 코드 조각과 자기 테스트 진리표로 서로를 반증한다. T-8을 작성하자 **즉시 실패했다**.
- **정정**: quiet 갈래에서 `Loading`도 `Ready`로 닫는다 —
  `return current is FrameLoadPhase.Failed or FrameLoadPhase.Loading ? Ready : current;`
- **근거**: 불변식 쪽(§0.4·§6.6·T-8)이 설계의 의도다. 그대로 두면 quiet 재스캔이 `Loading` 중에 완주했을 때
  프레임이 채워졌는데도 전면 대기 오버레이가 **영구 고착**된다 — 리뷰 C1이 없애려던 바로 그 실패 모드다.
- **영향 범위**: 이 조합은 현행 UI에서 도달 불가하다(`ConfirmDelete`는 `RequestDelete`를 거치고 그 선두에
  `IsInteractive` 가드가 있으므로 `Loading` 중 quiet 재스캔이 시작되지 않는다). 즉 **동작 변화 0**,
  불변식의 무조건 성립만 확보한 방어적 정정이다. T-5·T-6·T-7·T-38 기대값은 그대로 통과한다.

### D-2 — fallback 임시 파일 경로 `FallbackImagePath + ".tmp"`는 `Cv2.ImWrite`가 거부한다

- **증상**: §7.2의 `var tempPath = FallbackImagePath + ".tmp";`는 `fallback_frame.png.tmp`가 된다.
  OpenCV는 **확장자로 인코더를 고르므로** `.tmp`에는 writer가 없다. 실측 확인:
  `OpenCvSharp.OpenCVException: could not find a writer for the specified extension`
  (`FallbackFrameRenderer.cs:35`의 `Cv2.ImWrite`에서 발생).
- **파급**: 이 예외는 `EnsureFallbackFrame` → `ResolveLocalFrames` → 공유 작업 fault → VM의
  `catch (Exception)` → `SafeLocalFramesAsync` → 같은 경로에서 재차 실패 → **빈 목록** → `Failed`.
  즉 설계대로 구현하면 **최초 실행이 항상 `Failed` 카드로 떨어진다** — fallback 프레임이 단 한 번도 생성되지 못한다.
  M11(수동)만으로는 "의도한 Failed"와 구분되지 않아 놓칠 수 있는 결함이었다.
- **정정**: 임시 경로가 `.png` 확장자를 유지하게 한다 —
  `var tempPath = Path.ChangeExtension(FallbackImagePath, ".tmp.png");` → `fallback_frame.tmp.png`.
  lock + 원자 교체(`File.Move(overwrite: true)`) + `ImageUrl` 최종 경로 정정은 설계 그대로다.
- **T-28 조정**: 잔재 검사를 `*.tmp*` glob으로 수행한다(`.tmp`로 끝나는 파일이 아니라 `.tmp`를 포함하는 파일).
  임시 파일이 `LocalFrameStore` 스캔 루트(`AppContext.BaseDirectory\Frame`)가 아닌 캐시 폴더에 생기므로
  `.png` 확장자가 공용 프레임으로 오인될 위험은 없다(스캔 루트가 다르다).

### 그 밖의 경미한 적응 (판정 변경 없음)

| # | 지점 | 적응 | 이유 |
|---|------|------|------|
| a | Step 2 완료 기준의 `grep -n "SemaphoreSlim"` 0줄 | 신규 주석에서 타입명을 빼고 "종전 세마포어 게이트(`_defaultFramesGate`) 대체"로 표기 | 게이트 명령을 만족시키면서 교체 이력을 코드에 남긴다 |
| b | §9 #12~14 문서 라인 참조 | `11-exe-app-features.md`의 `FrameSelectViewModel.cs:70-93` 등 stale 라인 번호를 신규 번호로 정정 | 구조 교체로 전부 이동했다. 문서의 근거 규율 유지 |
| c | Step 9의 "`wpf-architecture.md` 오버레이 목록" | 해당 문서에 오버레이 목록 절이 **없었다** → §3.2 뒤에 표를 신설하고 기존 오버레이 6종 + it20 신규 2종을 함께 등재 | "목록에 행 추가"의 전제가 성립하지 않아 목록 자체를 만들었다. 기존 6종은 코드에서 조건 프로퍼티명을 확인해 기재 |

### 보고 표현 정정 (리뷰 지적)

구현 1차 보고에서 DI 생성자 선택 인자를 "이번 변경의 **유일한** 런타임 전용 리스크"로 기술한 것은 **부정확**하다.
런타임 전용(자동 검증으로 닫히지 않는) 리스크는 최소 3종이었다:

1. DI 해석 — `ViewModel_Resolves_From_Di_With_Optional_Deadline_Seam`으로 닫음
2. **`Spinner.Ring`의 트리거 체인**(`Loaded` → `BeginStoryboard` 이름 해석, `Pause/ResumeStoryboard`의
   `BeginStoryboardName` 해석) — 라운드 1 Major 1. `Spinner_Ring_Trigger_Chain_Runs_Without_Exception`으로 닫음
3. **A-1(첫 페인트 타이밍)·A-2(무진행 30초 적정성)·A-4(오버레이 대비)** — 여전히 **미확인**. 실기 기동이 필요하다

§10.6의 blocked 기록이 3번을 "남는 실질 공백"으로 인정하면서 본문에서 "유일한"이라고 쓴 것이 서로 어긋났다.

### N8 — 단일 비행이 동시 호출자에게 같은 `FrameTemplate` 인스턴스를 준다 (별칭 계약 변화, 기록만)

**사실**: 종전 세마포어 구조는 호출마다 `LoadPublic()`을 다시 돌려 **호출별 새 인스턴스**를 줬다.
단일 비행은 하나의 결과 리스트를 공유하므로 **동시 in-flight 호출자들이 같은 인스턴스를 본다**.
`FrameTemplate`은 가변이다(`ImageUrl`·`Slots`가 `set`/가변 컬렉션).

**판정: 현재 실제 위험 없음.** 코드 수정하지 않고 기록만 한다. 근거 —

- 카탈로그가 돌려준 인스턴스를 **변형하는 소비자가 없다**. 유일한 유력 후보였던 편집기의
  `FrameEditorViewModel.ApplyPickedFrame`(`:383-430`)은 `src.ImageUrl`·`src.ImageSize`·`src.Slots`를 **읽기만** 하고,
  슬롯은 `new Slot { … }`으로 새로 만들어 `_baseSlots`에 담는다. `src`에 대입하는 문장이 한 줄도 없다.
- `FrameSelectViewModel`도 읽기만 한다(`Session.SelectedFrame` 대입 · `Capture.Begin` 전달 · 삭제 시 `Id`/`Name` 조회).
- 별칭이 성립하려면 두 호출이 **동시에 in-flight**여야 한다. FrameSelect와 편집기 피커는 서로 다른 화면이고,
  화면 전환 시 `OnLeaveAsync`가 진행 중 로딩을 취소해 그 목록을 채우지 않는다. 순차 호출은 `_inFlight`가
  비어 새 패스를 시작하므로 새 인스턴스가 된다.
- 시작 prefetch는 결과를 **버린다**(`App.xaml.cs`의 fire-and-forget) → 별칭 소비자가 아니다.

**향후 주의**: 카탈로그 결과를 제자리 변형(in-place mutate)하는 소비자를 추가하면 이 계약이 문제가 된다.
그때는 소비자 쪽에서 복사하거나 `ResolveLocalFrames` 반환 시점에 방어 복사를 넣어야 한다.

---

## §16 코드 리뷰 라운드 1 대응 기록

판정: 🔴 0 · 🟠 2 · 🟡 11. **13건 전부 조치**(코드 수정 10건 / 문서 기록 2건 / 확인만 1건).

| 항목 | 조치 | 근거·위치 |
|------|------|-----------|
| **Major 1** 스피너 트리거 체인 미검증 | 테스트 확장(실기 기동 불가) | `Spinner_Ring_Trigger_Chain_Runs_Without_Exception` 신설 — `ctl.RaiseEvent(Loaded)`로 `EventTrigger` 실제 발화 + `rot.HasAnimatedProperties` 단정 + 호스트 `Visibility` `Collapsed`↔`Visible` 토글로 `Pause/ResumeStoryboard` 수행. **변이 검증으로 검출력 입증**: `TargetName`을 오타로 바꾸면 `InvalidOperationException: 'SpinnerRotateWRONG' 이름이 ControlTemplate의 이름 범위 안에 없습니다`로 실패, `BeginStoryboardName` 오타도 동일하게 실패 → 두 실패 모드 모두 이 테스트가 잡는다 |
| **Major 2** quiet 재스캔의 빈 목록 + 조작 열림 | `Frames.Clear()`를 `defaults` 확정 이후로 이동 | `FrameSelectViewModel.ReloadFramesAsync` — 별도 `resolved` 리스트에 모아 마지막에 한 번 교체. Enter 경로의 목록 깜빡임도 함께 사라졌다. 기존 T-36(`Leave_During_Load_Does_Not_Mutate_State`)은 목록을 아예 건드리지 않게 되어 의미가 더 강해졌고 기대값은 그대로 통과 |
| N1 replay가 stale `Completed` 재생 | 새 패스 시작 시 `_lastProgress` 리셋 | `GetDefaultFramesAsync`의 lock 안에서 `if (_inFlight is null) _lastProgress = new(ResolvingLocal);`. 회귀 테스트 `New_Pass_Does_Not_Replay_Previous_Completed` 신설 |
| N2 T-28의 머신 전역 경로 경합 | 전용 `[Collection]` 격리 | `FallbackCacheCollection.cs` 신설 + 공유 fallback 캐시를 건드리는 4개 클래스(`FrameCatalogServiceTests`·`FrameSelectViewModelTests`·`FramePickerViewModelTests`·`FrameEditorViewModelTests`)에 부착. `FallbackFrameTests`는 자기 임시 폴더만 쓰므로 제외 |
| N3 T-19 고아 태스크 | 공유 작업 완료를 await | `release.SetResult()` 뒤 `await svc.GetDefaultFramesAsync()` |
| N4 T-22 고정 Delay 가정 | 폴링으로 결정론화 | `WaitForPhaseAsync(collector, DownloadingImage, timeoutMs: 2000)` 헬퍼 — 관측될 때까지 10ms 간격 폴링, 미관측 시 실제 보고 순서를 담아 실패 |
| N5 신규 테스트 2파일 LF 개행 | **확인만**(리뷰어 stash 왕복으로 이미 정규화) | 4개 신규 파일 전부 CRLF 확인(`lines == CRLF` 카운트 일치). 추가 조치 없음 |
| N6 T-41의 부분 문자열 검사 | 정규식으로 강화 | `\{Binding\s+MEMBER\s*[,}]` — 주석에만 이름이 있으면 실패한다 |
| N7 stale 주석 | 정정 | `FramePickerViewModel.cs` — "직렬화한다" → "**공유**한다(단일 비행) … 취소는 경계에서 전파" |
| N8 별칭 계약 변화 | **기록**(코드 수정 없음) | §15 참조. 변형 소비자가 없고 동시 in-flight 조건도 성립하지 않아 현재 위험 없음으로 판정 |
| N9 Degraded 문구가 부분 성공에 부정확 | 문구 정정 | `NoticeFor(Degraded)` → "서버 프레임을 **모두** 가져오지 못해 **지금 준비된** 프레임으로 진행합니다." 진실원 1곳(`FrameLoadPolicy.cs`)이라 파급 없음 |
| N10 `CancelAfter` 인자 클램프 부재 | 클램프 한 줄 | `ArmDeadline`에서 `due > MaxTotalWait`이면 `MaxTotalWait`으로 클램프 후 `CancelAfter` |
| N11 `IdleWarningReferenceSeconds` 수동 사본 | App 계층 테스트 1건 | `Idle_Warning_Reference_Matches_Shell_Default` — 실제 `AppShellViewModel.IdleWarningSeconds` 기본값과 상수 일치 + `MaxTotalWaitSeconds < 실제 유휴 경고` 단정 |

**미조치 항목: 없음.** 동의하지 않아 남긴 지적도 없다.

**여전히 남는 미확인 리스크(인수 대상)**: **A-2 — 무진행 30초가 실회선에서 정상 다운로드를 자르지 않는지.**
자동 테스트로 대체 불가하며 실기에서 `기본 프레임 캐시: {Name}` 로그 간격 실측이 필요하다.
어긋나면 `FrameLoadPolicy.NoProgressTimeoutSeconds` 1개 + T-9~T-12 기대값만 고치면 된다. A-1·A-4도 실기 시각 확인 대상이다.
