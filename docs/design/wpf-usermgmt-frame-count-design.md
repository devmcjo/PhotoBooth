# 사용자 관리 화면 — 계정별 개인 프레임 개수 표시 설계

> 대상: `UserMgmtView` / `UserMgmtViewModel` (power 전용 관리 화면)
> 상위 결정: [프레임 소유권·바인딩 설계 §17 항목 4](./wpf-frame-ownership-binding-design.md#17-추후-개선-항목) (2026-08-07 분할 결정)
> 작성: 2026-08-07 · 파이프라인 1단계(architect) 산출물 — developer는 이 문서만으로 구현 가능해야 한다

---

## 0. 결정 요약

| # | 결정 | 근거 |
|---|------|------|
| **D-1** | 개수는 **정보성 읽기 전용**. 편집·삭제·한도 UI를 붙이지 않는다 | 강제 로직 없는 편집 UI는 "설정했는데 왜 안 막히지"를 만든다(§17 항목 4 분할 결정) |
| **D-2** | 상태 3종을 **`int?` 하나**로 표현한다. `null`=미조회·실패, 숫자=조회 성공. 표시용은 파생 속성 `FrameCountText` | 요구사항이 "실패도 —"로 못박았으므로 실패와 미조회를 구분할 소비자가 없다. enum 상태를 두면 XAML 분기만 늘고 화면은 같다. **0개는 반드시 `"0"`** 이어야 하므로 `int` 기본값 0 방식은 쓸 수 없다 |
| **D-3** | 목록 로드는 개수 조회를 **기다리지 않는다**. `ReloadAsync` 끝에서 fire-and-forget으로 시작하고, 진행 중 작업은 공개 `Task` 핸들(`FrameCountLoadTask`)로 관측한다 | `OnEnterAsync`를 await하는 주체는 `NavigateInternalAsync`(AppShellViewModel.cs:242)이고, 그 위 호출자는 `AsyncRelayCommand`다. 개수 조회를 await 안에 넣으면 **N회 HTTP 동안 [사용자 관리] 버튼이 실행 중으로 잠긴다** |
| **D-4** | 조회는 **행 순서대로 순차**. 동시 발사 금지 | 계정 수만큼 요청이 나간다. 병렬은 서버·토큰 갱신에 부하 스파이크를 만들고, 순차는 상단 행부터 채워져 체감이 낫다 |
| **D-5** | 개별 행 실패는 **Warning 로그만**. 행은 `"—"` 유지, 루프는 다음 행으로 계속. 단 `BackendNotConfiguredException`·`BackendLoginRequiredException`·**연속 3회 실패**는 남은 행을 포기한다 | 관리 화면이 프레임 조회 때문에 막히면 안 된다(요구 제약 3). 한편 `HttpClient.Timeout=100초`(ServiceRegistration.cs:117)라 서버가 죽은 채로 20계정을 돌면 **최악 33분**짜리 백그라운드 루프가 된다 — 결과(전부 `"—"`)는 같으므로 조기 포기가 손해가 없다 |
| **D-6** | 사용자에게 **어떤 실패 문구도 노출하지 않는다**. `StatusMessage`를 건드리지 않는다 | 요구 제약 3. `BackendFailureMessage.Describe`(D-26)는 "사용자에게 보여줄 때" 쓰는 도구이고, 이 기능은 보여주지 않는 쪽을 택했다 |
| **D-7** | `IFrameRepository`는 **생성자 마지막 선택 파라미터**(`IFrameRepository? frames = null`)로 받는다 | 기존 테스트 2개 헬퍼(`new UserMgmtViewModel(shell, accounts)` / `…, logger: null, pinPrompt: pin`)를 건드리지 않는다. MS.DI는 등록된 서비스가 있으면 주입하고 없으면 기본값을 쓴다(`ParameterDefaultValue`). null이면 개수 기능만 조용히 꺼진다(fail-soft) |
| **D-8** | 새 컬럼은 **PIN 뒤**에 넣고, 기존 컬럼 폭을 줄여 **합계 1146px를 그대로 유지**한다 | `ScrollViewer.HorizontalScrollBarVisibility="Disabled"`라 폭 합계가 뷰포트를 넘으면 오른쪽 컬럼(=삭제 버튼)이 **말없이 잘린다**. 창모드 하한이 800×600(it21)이므로 총폭을 늘리지 않는 것이 유일하게 안전한 선택 |
| **D-9** | 값은 배지가 아니라 **평문 숫자**. 숫자는 본문색, `"—"`는 흐린색(`Brush.Text.Muted`) | PIN 배지는 "설정됨/미설정"이라는 **경보성 이분 상태**라 색이 의미를 갖는다. 개수는 크기 비교용 수치라 색을 입히면 없는 의미를 만든다. 대신 `"0"`(진짜 0개)과 `"—"`(모름)는 명도로 구분한다 |
| **D-10** | 역할로 조회 대상을 **거르지 않는다**(전 계정 조회) | 프레임 저작 권한을 잃은 계정(강등된 advanced_user)도 **기존 프레임을 그대로 소유**한다(FrameSelectViewModel.cs:186 주석). `CanWriteFrames`로 거르면 그 계정이 0개로 보이는 **거짓 정보**가 된다 |

---

## 1. 범위 경계 — 만들지 않는 것

| 항목 | 판정 |
|------|------|
| 일일 QR 한도 **편집 UI** | **금지**. 과금 도입 시 강제 로직과 함께 만든다(§17 항목 4) |
| 프레임 **목록 열람·삭제** 진입점 | 금지. 이번 범위는 개수 하나다 |
| 개수 기준 **정렬·필터** | 금지. 정렬은 역할 위계 → 가입 시각 그대로다 |
| 개수 실패 **재시도 버튼** | 만들지 않는다. [↻ 새로고침]이 이미 전체를 다시 돈다 |
| 서버(`web/functions`) 변경 | 불필요. `GET /frames?userId=`가 이미 power에게 임의 계정 조회를 허용한다 |
| 개수 **캐시** | 하지 않는다. 화면 진입·새로고침마다 조회한다(관리 화면은 최신값이 존재 이유다) |

---

## 2. 검증된 사실 / 미검증 가정

### 2.1 검증된 사실 (verified facts)

| # | 사실 | 근거 |
|---|------|------|
| F1 | 서버가 power에게 임의 계정 프레임 조회를 허용한다. 비power는 본인만 | `web/functions/src/routes/frames.ts:47-63` — `if (idRes.value !== actor.id && !isPower(actor.role)) throw forbidden` |
| F2 | `IsPower` = Manager + Admin. UserMgmt 진입은 power 전용(`AccountViewModel.OpenUserManagement`) | `src/MCPhoto.Core/Models/UserRole.cs:54` |
| F3 | 클라 호출부가 이미 있다: `Task<IReadOnlyList<FrameTemplate>> GetUserFramesAsync(string userId, CancellationToken ct = default)` | `src/MCPhoto.Core/Frames/IFrameRepository.cs:14`, 구현 `src/MCPhoto.Http/HttpFrameRepository.cs:56-69` |
| F4 | `IFrameRepository`는 Singleton으로 등록돼 있고 `UserMgmtViewModel`은 Transient다 | `src/MCPhoto.App/ServiceRegistration.cs:151`, `:210` |
| F5 | `ViewModelBase`에 이탈 훅이 있다. 이름은 `OnLeaveAsync`(**`OnExitAsync`가 아니다**) | `src/MCPhoto.App/ViewModels/ViewModelBase.cs:12` |
| F6 | 모든 화면 전환이 이탈 훅을 부른다: `NavigateInternalAsync`가 `old.OnLeaveAsync()`를 await하고 예외를 삼킨다 | `src/MCPhoto.App/AppShellViewModel.cs:231-236` |
| F7 | 취소·재진입 관례가 이미 있다: `_loadCts` 필드 + `CancelLoad()`(취소만) + 본체 `finally`가 Dispose | `src/MCPhoto.App/ViewModels/FrameSelectViewModel.cs:126-147` |
| F8 | 백엔드 예외 3종이 타입으로 분기 가능하다: `BackendNotConfiguredException`·`BackendUnavailableException`(둘 다 `InvalidOperationException` 파생), `BackendLoginRequiredException`(`UnauthorizedAccessException` 파생) | `src/MCPhoto.Core/Backend/BackendFailure.cs` |
| F9 | HTTP 타임아웃은 100초다 | `src/MCPhoto.App/ServiceRegistration.cs:117` |
| F10 | ~~이 리포에 `InternalsVisibleTo`가 **없다**~~ → **사실 오류(2026-08-07 정정)**. `MCPhoto.App.csproj`에 실제로 있다. 그럼에도 `FrameCountLoadTask`는 **public을 유지**한다 — 진단 관측점이라는 의도가 접근 한정자에 드러나야 하고, 테스트 계약도 그쪽이 안정적이다 | `MCPhoto.App.csproj` 확인 |
| F11 | 현재 컬럼 폭 합계는 1146px(300+128+150+96+232+240)이고 `HorizontalScrollBarVisibility="Disabled"`다 | `src/MCPhoto.App/Views/UserMgmtView.xaml:198`, `:204~294` |
| F12 | 기존 `UserMgmtViewModel` 직접 생성부는 테스트 2곳뿐이다(`MakeVmAsync`, `MakePinVmAsync`). 프로덕션은 DI가 만든다 | `tests/MCPhoto.Tests/UserMgmtViewModelTests.cs:79`, `:361` / `src/MCPhoto.App/AppShellViewModel.cs`의 `CreateViewModel` |
| F13 | XAML 회귀 안전망이 이미 `UserMgmtView.xaml`을 검사한다(테마 StaticResource 해석) | `tests/MCPhoto.Tests/XamlResourceTests.cs:253` |

### 2.2 미검증 가정 (open assumptions)

| # | 가정 | 검증 단계 |
|---|------|-----------|
| A1 | MS.DI가 **선택 파라미터**(`IFrameRepository? frames = null`)에 등록된 Singleton을 주입한다 | Step 2 — `dotnet build` 후 Step 6의 스모크(DI 해석) 테스트로 확정. 실패 시 대안: 필수 파라미터로 승격 + 테스트 헬퍼 2곳 수정 |
| A2 | 컬럼 폭을 재배분해도 한글 라벨(`고급 유저`·`임시 유저`)·버튼(`PIN 재설정`)이 잘리지 않는다 | Step 4 — 폭 산술은 §6.2 표에 있으나 **실측이 아니다**. developer는 빌드 후 실행 화면(1280 창)에서 육안 확인하고, 잘리면 §6.2의 여유분 표대로 재배분한다 |
| A3 | 계정 수가 수십 규모다(순차 조회가 실용적) | 검증 불가(운영 데이터). D-5의 조기 포기 규칙이 최악 시간을 상한한다 |

---

## 3. 상태 모델 — `UserRowViewModel`

### 3.1 속성

`src/MCPhoto.App/ViewModels/UserMgmtViewModel.cs`의 `UserRowViewModel`(이미 `sealed partial`)에 추가한다.

```csharp
/// <summary>
/// 이 계정이 소유한 개인 프레임 개수. <b>null = 아직 모른다</b>(미조회 또는 조회 실패).
/// 목록 로드를 막지 않기 위해 뒤늦게 채워지며(§4), 실패해도 null로 남는다 — 실패를 사용자에게 알리지 않는다.
/// ⚠️ 0(진짜 0개)과 null(모름)은 다른 값이다. 기본값 0으로 두면 조회 전 화면이 "전원 0개"라고 거짓말한다.
/// </summary>
[ObservableProperty]
[NotifyPropertyChangedFor(nameof(FrameCountText))]
private int? _frameCount;

/// <summary>개인 프레임 개수 표시값. 모르면 "—"(다른 셀의 미해당 표기와 같은 문자).</summary>
public string FrameCountText => FrameCount?.ToString(CultureInfo.InvariantCulture) ?? "—";
```

- `CommunityToolkit.Mvvm` 8.3.2의 소스 생성기가 `FrameCount` 프로퍼티와 `OnPropertyChanged(nameof(FrameCountText))`를 함께 만든다(`AppShellViewModel.cs:52`와 같은 관례).
- `using System.Globalization;`이 파일에 없다면 추가한다. 숫자 표기는 **`InvariantCulture` 고정** — 자릿수 구분자·아랍 숫자 로캘로 표시가 흔들리지 않게 한다.
- 표시 문자는 **U+2014 EM DASH `—`**. XAML의 역할 변경 컬럼이 이미 같은 문자를 쓴다(`UserMgmtView.xaml:287`).
- setter는 `public`이어야 한다(생성기 기본). 값 주입은 VM 내부에서만 한다.

### 3.2 상태 전이

| 시점 | `FrameCount` | 화면 |
|------|--------------|------|
| 행 생성 직후 | `null` | `—` (흐림) |
| 조회 성공 | `frames.Count` | `0`·`3` … (본문색) |
| 조회 실패 | `null` 유지 | `—` (흐림) |
| 조회 취소(이탈·새로고침) | `null` 유지 | 해당 행 VM은 곧 폐기된다 |
| 새로고침 | 행 VM 자체가 새로 만들어진다 → `null`부터 다시 | `—` → 숫자 |

**로딩 스피너·"조회 중" 문구를 두지 않는다.** 미조회 표기는 `"—"` 하나다(요구 제약 1).

---

## 4. 로딩 설계 — `UserMgmtViewModel`

### 4.1 필드·공개 관측점

```csharp
private readonly IFrameRepository? _frames;

/// <summary>진행 중인 개수 조회의 취소원. Dispose 소유자는 "그 조회 자신"(FrameSelectViewModel.cs:126 관례).</summary>
private CancellationTokenSource? _frameCountCts;

/// <summary>
/// 진행 중(또는 직전) 개수 채우기 작업. <b>테스트·진단용 관측점</b>이며 절대 faulted가 되지 않는다
/// (본체가 모든 예외를 삼킨다). 목록 로드는 이 작업을 기다리지 않는다 — 기다리면 D-3이 깨진다.
/// ⚠️ 이 리포에는 InternalsVisibleTo가 없어 internal 핸들을 테스트가 볼 수 없다. 폴링 대기는
///    플래키하므로 결정적 검증을 위해 public으로 노출한다.
/// </summary>
public Task FrameCountLoadTask { get; private set; } = Task.CompletedTask;
```

### 4.2 취소·수명

```csharp
/// <summary>화면 이탈 시 진행 중 조회 취소 — 뒤늦은 완료가 폐기된 VM 상태를 건드리지 않게 한다.</summary>
public override Task OnLeaveAsync()
{
    CancelFrameCounts();
    return Task.CompletedTask;
}

/// <summary>
/// 신호만 보낸다. Dispose는 조회 본체의 finally가 수행(이중 해제 불가) —
/// 취소자가 Dispose하면 진행 중 본체의 Cancel/Token 접근이 ObjectDisposedException으로 터진다.
/// </summary>
private void CancelFrameCounts()
{
    var cts = _frameCountCts;
    _frameCountCts = null;
    try { cts?.Cancel(); }
    catch (ObjectDisposedException) { /* 이미 완료·해제된 조회 — 무해 */ }
}
```

`ViewModelBase`의 이탈 훅 이름은 **`OnLeaveAsync`**다(F5). `OnExitAsync`라는 멤버는 존재하지 않으므로 `override` 대상 이름을 틀리면 컴파일이 막힌다.

### 4.3 시작 지점 — `ReloadAsync`

`ReloadAsync`는 **두 곳을 고친다**(본문 로직은 그대로).

```csharp
private async Task ReloadAsync()
{
    CancelFrameCounts();          // ① 맨 앞: 이전 조회 취소(새로고침·삭제·역할변경 재로드 모두 통과한다)
    Rows.Clear();
    try
    {
        … 기존 그대로 …
    }
    catch (Exception ex) { … 기존 그대로 … }
    UpdateSummary();
    StartFrameCountLoad();        // ② 맨 뒤: 행이 다 채워진 뒤에 개수 조회를 띄운다(await하지 않는다)
}
```

- ①은 `Rows.Clear()` **앞**에 둔다. 뒤에 두면 이전 루프가 방금 지워진 행에 값을 쓰려다 stale 가드에 걸리는 창이 생긴다(무해하지만 추론이 어려워진다).
- ②는 `UpdateSummary()` **뒤**. 요약(`SummaryText`·`IsEmpty`)은 개수와 무관하게 즉시 확정돼야 한다.
- 목록 조회가 실패해 `Rows`가 비면 `StartFrameCountLoad`는 아무것도 하지 않는다(§4.4 가드).

### 4.4 본체

```csharp
/// <summary>
/// 행이 채워진 뒤 개인 프레임 개수를 순차로 채운다(fire-and-forget). 목록 로드를 막지 않는 것이 요점이다.
/// 저장소 미주입(_frames=null)이면 전 행이 "—"로 남고 화면은 정상 동작한다(fail-soft).
/// </summary>
private void StartFrameCountLoad()
{
    if (_frames is null || Rows.Count == 0) return;
    var cts = new CancellationTokenSource();
    _frameCountCts = cts;
    // 스냅샷: 루프 도중 Rows가 교체돼도 컬렉션을 순회하지 않는다(InvalidOperationException 방지).
    FrameCountLoadTask = LoadFrameCountsAsync(Rows.ToArray(), cts);
}

/// <summary>
/// 계정별 개인 프레임 개수 조회. <b>순차</b>(동시 발사 금지 — 계정 수만큼 요청이 나간다),
/// <b>취소 가능</b>(화면 이탈·새로고침), <b>실패는 조용히</b>(행은 "—" 유지, Warning 로그만).
/// 어떤 경로로도 예외를 던지지 않는다 — 호출자가 await하지 않으므로 던지면 관측되지 않는 예외가 된다.
/// </summary>
private async Task LoadFrameCountsAsync(IReadOnlyList<UserRowViewModel> rows, CancellationTokenSource cts)
{
    int consecutiveFailures = 0;
    try
    {
        foreach (var row in rows)
        {
            // 이 조회가 아직 "현재" 조회인지 매 회 확인 — 새 로드가 시작됐으면 즉시 손을 뗀다.
            if (!ReferenceEquals(cts, _frameCountCts) || cts.IsCancellationRequested) return;

            try
            {
                var frames = await _frames!.GetUserFramesAsync(row.User.Id, cts.Token).ConfigureAwait(true);
                if (!ReferenceEquals(cts, _frameCountCts)) return;   // stale 결과가 새 목록을 덮지 않게
                row.FrameCount = frames.Count;
                consecutiveFailures = 0;
            }
            catch (OperationCanceledException)
            {
                return;   // 이탈·새로고침에 의한 정상 종료. 로그도 남기지 않는다.
            }
            catch (Exception ex)
            {
                // 요구 제약 3: 관리 화면 전체가 프레임 조회 실패로 막히면 안 된다.
                // 사용자에게는 아무것도 알리지 않는다(StatusMessage 불변) — 행은 "—"로 남는다.
                consecutiveFailures++;
                _logger?.LogWarning(ex, "개인 프레임 개수 조회 실패: {Id}", row.User.Id);

                if (IsHopelessForRemaining(ex) || consecutiveFailures >= MaxConsecutiveFrameCountFailures)
                {
                    _logger?.LogWarning("개인 프레임 개수 조회 중단 — 남은 계정은 '—'로 둔다(연속 실패 {Count}회)",
                        consecutiveFailures);
                    return;
                }
            }
        }
    }
    finally
    {
        if (ReferenceEquals(cts, _frameCountCts)) _frameCountCts = null;
        cts.Dispose();
    }
}

/// <summary>남은 계정도 같은 이유로 반드시 실패하는 예외인가(주소 미설정·인증 없음/만료).</summary>
private static bool IsHopelessForRemaining(Exception ex)
    => ex is BackendNotConfiguredException || ex is BackendLoginRequiredException;

/// <summary>
/// 연속 실패 상한. HttpClient.Timeout=100초라(ServiceRegistration.cs:117) 서버가 죽은 채 전 계정을 돌면
/// 백그라운드 루프가 수십 분 살아 있게 된다. 결과는 어차피 전부 "—"이므로 조기에 포기한다.
/// 산발적 실패 1~2건은 상한에 닿지 않고 다음 행으로 넘어간다(성공 시 카운터 리셋).
/// </summary>
private const int MaxConsecutiveFrameCountFailures = 3;
```

**금지 사항 (리뷰 체크포인트)**

- `Task.WhenAll`·`Parallel.ForEachAsync`로 바꾸지 않는다(D-4 위반).
- `catch`에서 `SetStatus(...)`를 부르지 않는다(D-6 위반 — 개별 행 실패는 무성이다).
- `ConfigureAwait(false)`를 쓰지 않는다. 이 코드는 **UI 스레드 컨텍스트로 돌아와야** `row.FrameCount` 대입이 UI 스레드에서 일어난다. 명시적으로 `ConfigureAwait(true)`를 붙여 "일부러 UI로 돌아온다"는 의도를 남긴다(비UI 계층 관례와의 혼동 방지).
- `_frames`가 null일 때 예외를 던지지 않는다(fail-soft).

### 4.5 요구 제약 ↔ 구현 대응

| 요구 제약 | 구현 지점 |
|-----------|-----------|
| 1. 목록 로드를 막지 않는다 | `StartFrameCountLoad()`가 `ReloadAsync` 끝에서 **await 없이** 시작. `OnEnterAsync`는 행이 채워진 즉시 반환 |
| 2. 순차 + 취소 | `foreach` + 각 호출에 `cts.Token` 전달. `OnLeaveAsync` → `CancelFrameCounts()` |
| 3. 실패는 조용히 | 행별 `catch` → `LogWarning` + `FrameCount` 그대로 null. `StatusMessage`/`StatusIsError` 불변 |
| 4. 재진입 안전 | `ReloadAsync` 첫 줄 `CancelFrameCounts()` + 매 회차 `ReferenceEquals(cts, _frameCountCts)` stale 가드 |

---

## 5. DI · 생성자 변경

### 5.1 생성자

```csharp
public UserMgmtViewModel(AppShellViewModel shell, IAccountService accounts,
    ILogger<UserMgmtViewModel>? logger = null, IPinPromptDialogService? pinPrompt = null,
    IFrameRepository? frames = null)          // ← 추가. 반드시 **마지막**에 붙인다
{
    _shell = shell;
    _accounts = accounts;
    _pinPrompt = pinPrompt;
    _logger = logger;
    _frames = frames;
}
```

- **마지막 위치**여야 기존 위치 인수 호출부가 그대로 컴파일된다(F12의 두 헬퍼 중 하나는 위치 인수 2개만 쓴다).
- **선택 파라미터**로 두는 이유: 기존 테스트를 손대지 않고(요구사항), 저장소가 없어도 화면이 완전히 동작한다(D-7).
- 프로덕션에서는 DI가 채운다 — `IFrameRepository`는 Singleton으로 이미 등록돼 있다(F4). **`ServiceRegistration.cs`는 수정하지 않는다.** `services.AddTransient<UserMgmtViewModel>()`가 그대로 유효하다.
- 가정 A1(선택 파라미터 주입)이 어긋나면 화면은 전부 `"—"`가 된다. Step 6의 DI 해석 테스트가 이를 잡는다. 어긋났을 때의 대안: `IFrameRepository frames`(필수, 3번째 위치)로 승격하고 테스트 헬퍼 2곳에 `new EmptyFrameRepository()`를 넘긴다.

### 5.2 추가 using

`UserMgmtViewModel.cs` 상단에 필요한 것만 더한다.

```csharp
using System.Globalization;      // FrameCountText의 InvariantCulture
using MCPhoto.Core.Backend;      // BackendNotConfiguredException / BackendLoginRequiredException
using MCPhoto.Core.Frames;       // IFrameRepository
```

`System.Threading`·`System.Threading.Tasks`는 ImplicitUsings로 이미 들어와 있다(현재 파일이 `Task`를 using 없이 쓴다).

### 5.3 영향 범위 — 기존 호출부

| 호출부 | 위치 | 영향 |
|--------|------|------|
| DI 등록 | `src/MCPhoto.App/ServiceRegistration.cs:210` | **변경 없음** |
| 화면 생성 | `src/MCPhoto.App/AppShellViewModel.cs`(`CreateViewModel`) | **변경 없음**(서비스 프로바이더 경유) |
| 테스트 `MakeVmAsync` | `tests/MCPhoto.Tests/UserMgmtViewModelTests.cs:79` | **변경 없음**(개수 기능이 꺼진 채로 기존 검증만 수행) |
| 테스트 `MakePinVmAsync` | `tests/MCPhoto.Tests/UserMgmtViewModelTests.cs:361` | **변경 없음**(named 인수) |

`new UserMgmtViewModel(` 전수 검색 결과 위 2건 외에 없다. 새 테스트는 §8의 전용 헬퍼를 쓴다.

---

## 6. XAML — `UserMgmtView.xaml`

### 6.1 컬럼 위치

`PIN`(96) **바로 뒤**, `역할 변경` 앞. 계정 정보 계열(계정·역할·가입·PIN·프레임 수)을 왼쪽에, 조작 계열(역할 변경·작업)을 오른쪽에 모은다 — 읽는 열과 누르는 열이 섞이지 않는다.

### 6.2 폭 재배분 (합계 1146px 유지 — D-8)

| 컬럼 | 현재 | 변경 | 최소 필요폭(추정) | 판단 |
|------|------|------|-------------------|------|
| 계정 | 300 | **260** | ID 15px(≈60) + 8 + "나" 배지(≈40) + 좌우 여백 20 ≈ 128 / 이메일은 `TextTrimming`으로 흡수 | -40 |
| 역할 ▼ | 128 | **108** | 배지 텍스트 "고급 유저"(≈56) + Padding 22 + Margin 12 ≈ 90 | -20 |
| 가입 일시 ▲ | 150 | **132** | 헤더 "가입 일시 ▲"(≈85) + Padding 12 ≈ 97 | -18 |
| PIN | 96 | 96 | 그대로 | 0 |
| **개인 프레임** | — | **96** | 헤더 "개인 프레임"(≈72) + Padding 12 ≈ 84 | **+96** |
| 역할 변경 | 232 | 232 | 콤보 122 + 8 + [적용] ≈66 + 여백 20 = 216 — **줄이지 않는다** | 0 |
| 작업 | 240 | **222** | [PIN 재설정] ≈108 + 8 + [삭제] ≈56 + Margin 12 = 184 | -18 |
| **합계** | **1146** | **1146** | | **0** |

> ⚠️ **합계 1146을 넘기지 마라.** `ScrollViewer.HorizontalScrollBarVisibility="Disabled"`(:198)라서 초과분은 스크롤이 아니라 **잘림**으로 나타나고, 잘리는 쪽은 마지막 컬럼(삭제 버튼)이다. 창모드 하한은 800×600(it21)이다.
> 최소 필요폭은 산술 추정치다(가정 A2) — 실행 화면에서 라벨이 잘리면 위 표의 여유분 안에서 재배분한다.

### 6.3 리소스 (UserControl.Resources에 추가)

`PinBadge` 스타일 정의 **바로 뒤**에 둔다.

```xml
<!-- 개인 프레임 개수: 숫자는 본문색, 미조회·실패("—")는 흐리게 —
     "0개"(진짜 0)와 "모름"이 같은 명도로 보이면 관리자가 잘못 읽는다. 배지를 쓰지 않는 이유는 설계 D-9. -->
<Style x:Key="Cell.FrameCount" TargetType="TextBlock">
    <Setter Property="FontFamily" Value="{StaticResource Font.Primary}" />
    <Setter Property="FontSize" Value="14" />
    <Setter Property="VerticalAlignment" Value="Center" />
    <Setter Property="Foreground" Value="{StaticResource Brush.Text.Primary}" />
    <Style.Triggers>
        <DataTrigger Binding="{Binding FrameCount}" Value="{x:Null}">
            <Setter Property="Foreground" Value="{StaticResource Brush.Text.Muted}" />
        </DataTrigger>
    </Style.Triggers>
</Style>
```

새 테마 키를 만들지 않는다 — `Font.Primary`·`Brush.Text.Primary`·`Brush.Text.Muted`는 이 파일이 이미 쓰는 키다. 따라서 `XamlResourceTests.Item1a_View_StaticResource_Keys_Resolve_In_Theme("UserMgmtView.xaml")`은 그대로 통과해야 한다(통과하지 않으면 오타다).

### 6.4 컬럼 정의

```xml
<!-- 개인 프레임 개수(정보성·읽기 전용). 목록이 먼저 그려지고 뒤이어 순차 조회로 채운다 —
     미조회·조회 실패는 모두 "—"다(설계 §3.2). 편집·삭제 진입점을 두지 않는다(설계 D-1·§1). -->
<GridViewColumn Header="개인 프레임" Width="96">
    <GridViewColumn.CellTemplate>
        <DataTemplate>
            <TextBlock Text="{Binding FrameCountText}" Style="{StaticResource Cell.FrameCount}"
                       Margin="12,0,8,0"
                       AutomationProperties.Name="개인 프레임 개수" />
        </DataTemplate>
    </GridViewColumn.CellTemplate>
</GridViewColumn>
```

- `Margin="12,0,8,0"`은 다른 셀(`12,0,…`)과 헤더 `Padding="12,0"`에 맞춘 좌측 정렬이다.
- 헤더 텍스트는 정렬 기준이 아니므로 `▼`/`▲` 표기를 붙이지 않는다.

---

## 7. 스레딩 · 누수 안전

| 항목 | 설계 |
|------|------|
| UI 스레드 경계 | `LoadFrameCountsAsync`는 UI 스레드에서 시작하고 `ConfigureAwait(true)`로 돌아온다 → `row.FrameCount` 대입(=`PropertyChanged`)이 UI 스레드에서 일어난다. `Rows` 컬렉션 자체는 **건드리지 않는다**(행 객체의 속성만 갱신) — 백그라운드에서 `ObservableCollection`을 변경할 위험이 원천적으로 없다 |
| 블로킹 금지 | `.Result`·`.Wait()`·`GetAwaiter().GetResult()`를 쓰지 않는다. 조회는 전부 `await` |
| 이벤트 구독 | **새로 구독하는 이벤트가 없다** → 해제 경로도 없다. 누수 표면은 CTS 하나뿐이다 |
| CTS 수명 | 생성=`StartFrameCountLoad`, 취소=`CancelFrameCounts`(이탈·재로드), Dispose=**본체 `finally` 단독**. 취소자는 Dispose하지 않는다(FrameSelectViewModel.cs:138 관례와 동일) |
| VM 폐기 후 잔존 작업 | `UserMgmtViewModel`은 Transient라 화면을 벗어나면 폐기 대상이다. `OnLeaveAsync`가 취소하므로 루프는 다음 행에서 멈춘다. 취소가 늦어 값이 한 번 더 써지더라도 대상은 이미 화면에서 떨어진 행 객체이므로 무해하다 |
| 미관측 예외 | `LoadFrameCountsAsync`는 어떤 경로로도 throw하지 않는다(모든 예외를 삼킨다) → fire-and-forget이지만 `TaskScheduler.UnobservedTaskException`을 유발하지 않는다 |
| 파일 인코딩 | 수정 대상 `.cs`는 **UTF-8 BOM 없음**을 유지한다(한글 주석 다수). `.xaml`은 현재 파일 인코딩 그대로 저장한다 |

---

## 8. 테스트 계획

### 8.1 파일

**신규** `tests/MCPhoto.Tests/UserMgmtFrameCountTests.cs`.
기존 `UserMgmtViewModelTests.cs`(543줄)는 역할·PIN 정책 전용이고 헬퍼가 `IFrameRepository`를 모른다. 리포 관례상 같은 VM이라도 관심사가 다르면 파일을 나눈다(`AccountViewModelPinTests` / `AccountViewModelTempUserTests`). **기존 파일은 한 줄도 고치지 않는다.**

### 8.2 테스트 더블

```csharp
/// <summary>계정별 프레임 개수를 흉내내는 저장소. 호출 순서·동시성·취소 토큰을 기록한다.</summary>
private sealed class SpyFrameRepository : IFrameRepository
{
    public Dictionary<string, int> Counts { get; } = new(StringComparer.Ordinal);       // userId → 반환 개수
    public Dictionary<string, Exception> Throws { get; } = new(StringComparer.Ordinal); // 특정 계정만 실패
    public Exception? ThrowsAlways { get; set; }                                        // 전 계정 실패(오프라인 모사)
    public List<string> Queried { get; } = new();                                       // 조회 순서
    public List<CancellationToken> Tokens { get; } = new();                             // 각 호출이 받은 토큰
    public TaskCompletionSource<bool>? Gate { get; set; }                               // 열릴 때까지 대기
    public int MaxConcurrent { get; private set; }                                      // 동시 진행 최대치
    private int _inFlight;

    public async Task<IReadOnlyList<FrameTemplate>> GetUserFramesAsync(string userId, CancellationToken ct = default)
    {
        var now = Interlocked.Increment(ref _inFlight);
        if (now > MaxConcurrent) MaxConcurrent = now;
        try
        {
            Queried.Add(userId);
            Tokens.Add(ct);
            if (Gate is not null) await Gate.Task.WaitAsync(ct);   // 취소되면 OperationCanceledException
            ct.ThrowIfCancellationRequested();
            if (ThrowsAlways is not null) throw ThrowsAlways;
            if (Throws.TryGetValue(userId, out var ex)) throw ex;
            var n = Counts.TryGetValue(userId, out var c) ? c : 0;
            return Enumerable.Range(0, n)
                .Select(i => new FrameTemplate { Id = $"{userId}_f{i}", Name = $"f{i}", UserId = userId })
                .ToList();
        }
        finally { Interlocked.Decrement(ref _inFlight); }
    }

    // 나머지 멤버는 이 화면이 쓰지 않는다 — 호출되면 설계 위반이므로 즉시 실패시킨다.
    public Task<IReadOnlyList<FrameTemplate>> GetDefaultFramesAsync(CancellationToken ct = default) => throw new NotSupportedException();
    public Task<FrameTemplate> SaveAsync(FrameTemplate f, byte[] png, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<FrameTemplate> SaveMineAsync(FrameTemplate f, byte[] png, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<bool> DeleteAsync(string frameId, CancellationToken ct = default) => throw new NotSupportedException();
    public Task DeleteAllByUserAsync(string userId, CancellationToken ct = default) => throw new NotSupportedException();
}
```

계정 서비스는 기존 파일의 `SpyAccountService`를 **복제하지 말고**, 이 파일 전용의 최소 스텁(`GetAllAsync`만 동작, 나머지 `NotSupportedException`)을 둔다. VM 조립 헬퍼는 `UserMgmtViewModelTests.MakeVmAsync`와 같은 형태로 만들되 `frames:` 인수를 받는다.

⚠️ `EmptyServiceProvider`·`SpyAccountService`는 `UserMgmtViewModelTests`의 **private 중첩 클래스**라 다른 파일에서 보이지 않는다 — 새 파일 안에 같은 이름의 private 중첩 타입을 각각 정의한다(기존 파일을 고쳐 `internal`로 올리지 않는다). 필요한 using: `System.IO`, `System.Linq`, `MCPhoto.App`, `MCPhoto.App.Services`, `MCPhoto.App.ViewModels`, `MCPhoto.Core.Accounts`, `MCPhoto.Core.Backend`, `MCPhoto.Core.Frames`, `MCPhoto.Core.Models`, `MCPhoto.Core.Navigation`, `MCPhoto.Core.Settings`(+ T10은 `Microsoft.Extensions.DependencyInjection`).

```csharp
private static async Task<(UserMgmtViewModel vm, SpyFrameRepository frames)> MakeVmAsync(
    IReadOnlyList<User> accounts, SpyFrameRepository frames, bool enter = true)
{
    var settings = new IniSettingsService(iniPath: Path.Combine(Path.GetTempPath(), $"umfc_{Guid.NewGuid():N}.ini"));
    settings.Load();
    var session = new SessionContext();
    session.Login(new User { Id = "admin", Role = UserRole.Admin });
    var shell = new AppShellViewModel(new IdleWatchdog(), settings, new EmptyServiceProvider(), session);
    var vm = new UserMgmtViewModel(shell, new StubAccountService(accounts), logger: null, pinPrompt: null, frames: frames);
    if (enter) await vm.OnEnterAsync();
    return (vm, frames);
}
```

### 8.3 검증 항목

| # | 테스트 | 시나리오 | 단언 | 요구 |
|---|--------|----------|------|------|
| **T1** | `Rows_Are_Populated_Before_Frame_Counts_Complete` | `Gate` 닫은 채 `OnEnterAsync` | `Rows.Count==3` · 전 행 `FrameCountText=="—"` · `FrameCountLoadTask.IsCompleted==false` → Gate 개방 후 `await FrameCountLoadTask` → 숫자 반영 | **필수 1** |
| **T2** | `Frame_Counts_Are_Applied_On_Success` | `Counts = {u1:3, u2:0, u3:10}` | `"3"`·`"0"`·`"10"` · `FrameCount` 각각 3·0·10 · `Queried.Count==3` | **필수 2** |
| **T3** | `One_Account_Failure_Keeps_Other_Rows_And_Screen_Intact` | `Throws["u2"]=new BackendUnavailableException("offline")` | `u1="2"`, `u2="—"`, `u3="1"` · `Rows.Count==3` · `StatusMessage==""` · `StatusIsError==false` · `IsEmpty==false` | **필수 3** |
| **T4** | `Refresh_Cancels_Previous_Frame_Count_Load` | `Gate` 닫은 채 진입 → `RefreshCommand.ExecuteAsync(null)` | 첫 호출 토큰 `IsCancellationRequested==true` · `FrameCountLoadTask`가 이전 인스턴스와 다름 · 이전 Task는 예외 없이 완료(`await`) | **필수 4** |
| T5 | `Frame_Count_Queries_Are_Sequential` | 5계정, Gate 없음 | `MaxConcurrent==1` · `Queried`가 `Rows` 표시 순서와 동일 | 제약 2 |
| T6 | `Leaving_Screen_Cancels_Frame_Count_Load` | Gate 닫고 진입 → `await vm.OnLeaveAsync()` | 첫 토큰 취소됨 · `await FrameCountLoadTask`가 예외 없이 끝남 · 남은 계정 미조회(`Queried.Count==1`) | 제약 2 |
| T7 | `Offline_Leaves_All_Dashes_And_Stops_Early` | 5계정, `ThrowsAlways=BackendUnavailableException` | 전 행 `"—"` · `Queried.Count==3`(연속 실패 상한) · `StatusMessage==""` | D-5 |
| T8 | `Login_Required_Aborts_Remaining_Rows` | `ThrowsAlways=new BackendLoginRequiredException("expired", true)` | `Queried.Count==1` · 전 행 `"—"` | D-5 |
| T9 | `Null_Repository_Leaves_All_Dashes_Without_Crash` | `frames: null`로 VM 생성 | 전 행 `"—"` · `FrameCountLoadTask.IsCompleted==true` · 목록·요약 정상 | D-7 fail-soft |
| T10 | `Frame_Repository_Is_Injected_Through_Optional_Ctor_Parameter` | `ServiceCollection`에 `IFrameRepository`(Spy)+의존성 등록 → `AddTransient<UserMgmtViewModel>()` → `GetRequiredService` → `OnEnterAsync` | 개수가 채워진다(=선택 파라미터에 주입됨) | **가정 A1 검증** |
| T11 | `UserMgmtView_Binds_FrameCountText` (기존 `XamlResourceTests.cs`에 추가) | `UserMgmtView.xaml` 텍스트 검사 | `{Binding FrameCountText}` 정규식 매치 · `typeof(UserRowViewModel).GetProperty("FrameCountText")!=null` · 파일에 `일일`/`한도` 문자열 없음(§1 경계 고정) | D-1·XAML 회귀 |

- 시간 기반 대기(`Task.Delay`·폴링)를 쓰지 않는다 — 전부 `TaskCompletionSource`와 `FrameCountLoadTask`로 결정적으로 제어한다.
- T4·T6의 토큰 단언은 `Cancel()`이 동기적으로 `IsCancellationRequested`를 세우므로 경합이 없다.
- 계정 정렬은 기존 규칙(역할 위계 → 가입 시각)이 적용되므로, 순서가 중요한 T5는 같은 역할 + `CreatedAt` 차등으로 고정한다.

---

## 9. 문서 갱신 지점 (정확한 위치)

| 문서 | 위치 | 갱신 내용 | 필수 |
|------|------|-----------|------|
| `docs/analysis/13-client-behavior-spec.md` | **§10.3 사용자 관리**(현재 617행~), 마지막 불릿 "목록 조회 실패는…" 다음 | 표시 항목 불릿 추가(§9.1 문안) | **필수** |
| `docs/analysis/13-client-behavior-spec.md` | 화면 표 30행 `\| \`UserMgmt\` \| P4 \| 사용자 목록·역할·삭제·PIN 재설정 \|` | 설명에 `·개인 프레임 수(표시)` 추가 | **필수** |
| `docs/design/wpf-frame-ownership-binding-design.md` | **§17 추후 개선 항목** 표의 `~~4~~` 행(현재 534행) | 개인 프레임 수 부분만 **완료**로 표시하고 이 문서를 링크. **일일 QR 한도 편집 UI는 미착수로 남긴다** | **필수** |
| `docs/analysis/11-exe-app-features.md` | §13 사용자 관리 불릿(현재 290행) | `목록 로드, 삭제(...), PIN 재설정, 역할 변경` 나열에 `개인 프레임 개수 표시(정보성 — 목록 로드 후 순차 조회, 실패는 "—")` 추가 | 권장 |
| `docs/design/README.md` | §3.2 Windows 데스크톱 표 | 이 설계 문서 등재 | **완료**(architect가 처리) |

### 9.1 `analysis/13 §10.3`에 넣을 문안

```markdown
- **개인 프레임 개수 열**(정보성·읽기 전용): 계정 목록이 **먼저** 그려지고, 그 뒤 계정별로 **순차** 조회해 채운다(`GET /frames?userId=` — power는 임의 계정 조회 가능). 미조회·조회 실패는 모두 `"—"`이고, **실패는 사용자에게 알리지 않는다**(Warning 로그만) — 프레임 조회 실패나 오프라인이 관리 화면을 막으면 안 된다. 화면을 벗어나거나 [↻ 새로고침]을 누르면 진행 중 조회는 취소된다.
- **일일 QR 한도 편집 UI는 없다.** 강제 로직이 없는 상태에서 편집 UI를 만들면 "설정했는데 왜 안 막히지"가 되므로 과금 도입 시 함께 만든다.
```

⚠️ `analysis`는 "현재 무엇이 어떻게 동작하는가"의 진실원이다. 구현이 끝난 뒤(Step 6) 갱신한다.

---

## 10. 영향 파일

| 파일 | 변경 |
|------|------|
| `src/MCPhoto.App/ViewModels/UserMgmtViewModel.cs` | `UserRowViewModel`에 `FrameCount`/`FrameCountText` 추가. `UserMgmtViewModel`에 `_frames`·`_frameCountCts`·`FrameCountLoadTask`·`OnLeaveAsync`·`StartFrameCountLoad`·`LoadFrameCountsAsync`·`IsHopelessForRemaining`·상수 추가, 생성자 파라미터 1개 추가, `ReloadAsync` 2줄 삽입 |
| `src/MCPhoto.App/Views/UserMgmtView.xaml` | `Cell.FrameCount` 스타일 + `개인 프레임` 컬럼 추가, 기존 4개 컬럼 `Width` 조정 |
| `tests/MCPhoto.Tests/UserMgmtFrameCountTests.cs` | **신규** — T1~T10 |
| `tests/MCPhoto.Tests/XamlResourceTests.cs` | T11 추가 |
| `docs/analysis/13-client-behavior-spec.md` · `docs/analysis/11-exe-app-features.md` · `docs/design/wpf-frame-ownership-binding-design.md` | §9 |
| **변경 없음** | `ServiceRegistration.cs` · `AppShellViewModel.cs` · `IFrameRepository.cs` · `HttpFrameRepository.cs` · `web/functions/**` · 빌드 버전 |

---

## 11. 구현 단계 (WBS)

> 형식: `docs/templates/WBS_BLUEPRINT.md`. 각 단계는 self-contained — 이 문서를 처음 보는 에이전트가 그 단계만 읽고 실행할 수 있어야 한다.
> 공통 금지: **커밋 금지 · 브랜치 생성/전환 금지 · 서버(`web/functions`) 수정 금지 · 빌드 버전 변경 금지.**
> 공통 규약: 수정하는 `.cs`는 **UTF-8 BOM 없음** 유지.

### Step 1: `UserRowViewModel`에 프레임 개수 상태 추가
- **Context Brief**: `src/MCPhoto.App/ViewModels/UserMgmtViewModel.cs`에는 사용자 관리 표의 한 행을 나타내는 `UserRowViewModel`(`sealed partial`, CommunityToolkit `ObservableObject`)이 있다. 여기에 "이 계정이 소유한 개인 프레임 개수"를 담을 상태를 만든다. 아직 값을 채우는 코드는 없다(Step 2).
- **대상 파일**: `src/MCPhoto.App/ViewModels/UserMgmtViewModel.cs`
- **선행 조건**: 없음
- **구현 내용**: §3.1 그대로 — `[ObservableProperty] [NotifyPropertyChangedFor(nameof(FrameCountText))] private int? _frameCount;` 와 `public string FrameCountText => FrameCount?.ToString(CultureInfo.InvariantCulture) ?? "—";`. `using System.Globalization;` 추가. 기존 멤버(`PinStateLabel` 등) 옆에 배치하고 XML 주석은 §3.1 문안을 쓴다.
- **검증 명령**: `dotnet build MCPhoto.sln -c Debug`
- **완료 기준**:
  - [관측] 빌드 오류 0. `UserRowViewModel`의 새 인스턴스에서 `FrameCountText == "—"`, `FrameCount = 0` 대입 후 `"0"`, `= 7` 후 `"7"`
  - [non-goal] 기존 속성(`PinStateLabel`·`RoleLabel`·`CanResetPin`·`AssignableRoles`)과 생성자 시그니처 불변
  - [trigger] 값 변경은 VM 코드의 대입으로만 발생 — 생성자에서 초기값을 넣지 않는다(반드시 `null`로 시작)
- **롤백**: 추가한 두 멤버와 using 제거
- [ ] 완료

### Step 2: 순차·취소 가능한 개수 로딩 파이프라인
- **Context Brief**: 같은 파일의 `UserMgmtViewModel`은 `OnEnterAsync` → `ReloadAsync`로 계정 목록을 채운다(`Rows`). 목록이 그려진 **뒤에** 계정별 프레임 개수를 순차 조회해 Step 1의 속성에 채운다. 조회는 `IFrameRepository.GetUserFramesAsync(userId, ct)`(`src/MCPhoto.Core/Frames/IFrameRepository.cs:14`)를 쓴다. 목록 로드를 **막으면 안 되고**, 실패는 **사용자에게 노출하지 않는다**. 취소 관례는 `src/MCPhoto.App/ViewModels/FrameSelectViewModel.cs:126-147`과 동일하게(취소자는 Cancel만, Dispose는 본체 `finally`) 맞춘다.
- **대상 파일**: `src/MCPhoto.App/ViewModels/UserMgmtViewModel.cs`
- **선행 조건**: Step 1
- **구현 내용**: §4.1~§4.4와 §5.1~§5.2를 그대로 옮긴다 — 생성자 마지막에 `IFrameRepository? frames = null` 추가, `_frames`·`_frameCountCts`·`FrameCountLoadTask`·`MaxConsecutiveFrameCountFailures` 선언, `OnLeaveAsync` override, `CancelFrameCounts`·`StartFrameCountLoad`·`LoadFrameCountsAsync`·`IsHopelessForRemaining` 추가, `ReloadAsync` 첫 줄에 `CancelFrameCounts();`·마지막 줄에 `StartFrameCountLoad();` 삽입. `ServiceRegistration.cs`는 **건드리지 않는다**.
- **검증 명령**: `dotnet build MCPhoto.sln -c Debug` · `rg -n "Task.WhenAll|Parallel.ForEach|ConfigureAwait\(false\)|SetStatus\(" src/MCPhoto.App/ViewModels/UserMgmtViewModel.cs`
- **완료 기준**:
  - [관측] 빌드 오류 0. `LoadFrameCountsAsync` 안에 `await`가 있는 호출은 `GetUserFramesAsync` 하나이고 `foreach` 안에 있다(=순차). `rg` 결과에 `Task.WhenAll`·`Parallel.ForEach`·`ConfigureAwait(false)`가 없고, `SetStatus(` 히트는 **기존 커맨드 경로에만** 있다
  - [non-goal] `ReloadAsync`의 정렬·요약·예외 처리 로직, `DeleteUser`/`ResetUserPin`/`ApplyRoleChange`/`Back` 커맨드, `StatusMessage`·`StatusIsError` 사용처 불변. `ServiceRegistration.cs` diff 0
  - [trigger] 개수 조회는 `ReloadAsync` 완료 시점에만 시작한다(진입·새로고침·삭제/역할변경 후 재로드). 개별 행 실패로 `StatusMessage`가 바뀌는 경로가 **없다**
- **롤백**: 추가 멤버 제거 + `ReloadAsync`의 2줄 원복 + 생성자 파라미터 제거
- [ ] 완료

### Step 3: ViewModel 테스트 10건 신규
- **Context Brief**: Step 1·2가 만든 동작을 xUnit으로 고정한다. 기존 `tests/MCPhoto.Tests/UserMgmtViewModelTests.cs`는 역할·PIN 전용이므로 **손대지 않고** 새 파일을 만든다. 이 리포에는 `InternalsVisibleTo`가 없어 테스트는 public 멤버만 본다 — 진행 중 조회는 `UserMgmtViewModel.FrameCountLoadTask`로 기다린다. 시간 기반 대기(`Task.Delay`·폴링)는 금지다.
- **대상 파일**: `tests/MCPhoto.Tests/UserMgmtFrameCountTests.cs`(신규)
- **선행 조건**: Step 2
- **구현 내용**: §8.2의 `SpyFrameRepository`·계정 스텁·`MakeVmAsync` 헬퍼 + §8.3의 T1~T10.
- **검증 명령**: `dotnet test MCPhoto.sln -c Debug --filter "FullyQualifiedName~UserMgmtFrameCount"`
- **완료 기준**:
  - [관측] 신규 10건 전부 통과, 실패 0. T1이 "행 채워짐 + `FrameCountLoadTask` 미완료"를 같은 시점에 단언한다. T10이 MS.DI 선택 파라미터 주입(가정 A1)을 실증한다
  - [non-goal] `UserMgmtViewModelTests.cs`·`RoleManagementTests.cs` diff 0, 기존 테스트 결과 불변
  - [trigger] 취소 단언은 `RefreshCommand` 실행(T4)·`OnLeaveAsync` 호출(T6)이라는 명시적 액션 뒤에만 한다
- **롤백**: 신규 파일 삭제
- [ ] 완료

### Step 4: 표에 "개인 프레임" 컬럼 추가
- **Context Brief**: `src/MCPhoto.App/Views/UserMgmtView.xaml`은 `ListView`+`GridView` 표다. 컬럼 폭은 고정이고 `ScrollViewer.HorizontalScrollBarVisibility="Disabled"`(198행)라 **폭 합계가 뷰포트를 넘으면 오른쪽 컬럼이 잘린다**. 현재 합계는 1146px다. 개수 열을 넣되 합계를 유지한다.
- **대상 파일**: `src/MCPhoto.App/Views/UserMgmtView.xaml`
- **선행 조건**: Step 1(바인딩 대상 속성 존재)
- **구현 내용**: §6.3의 `Cell.FrameCount` 스타일을 `UserControl.Resources`의 `PinBadge.Text` 뒤에 추가. §6.4의 컬럼을 `PIN` 컬럼과 `역할 변경` 컬럼 사이에 삽입. §6.2 표대로 `계정 300→260`, `역할 ▼ 128→108`, `가입 일시 ▲ 150→132`, `작업 240→222`. 새 테마 키를 만들지 않는다.
- **검증 명령**: `dotnet build MCPhoto.sln -c Debug` · `dotnet test MCPhoto.sln -c Debug --filter "FullyQualifiedName~XamlResourceTests"` · 폭 합계 확인 `rg -n "GridViewColumn Header" -A0 src/MCPhoto.App/Views/UserMgmtView.xaml`
- **완료 기준**:
  - [관측] 빌드 오류 0 · `XamlResourceTests` 전부 통과 · `Width` 값 합계 = 1146 · 앱 실행 시 1280 창에서 "개인 프레임" 헤더와 [삭제] 버튼이 모두 보인다
  - [non-goal] 역할 배지·PIN 배지·역할 변경 콤보·작업 버튼의 스타일·바인딩·가시성 조건 불변. 편집 컨트롤(TextBox/Slider/Button)을 새 컬럼에 넣지 않는다 — 읽기 전용 `TextBlock` 하나다
  - [trigger] 셀 값은 오직 `{Binding FrameCountText}`로 표시된다. 클릭·호버로 동작하는 요소가 없다(`IsHitTestVisible` 기본, 커맨드 없음)
- **롤백**: 추가한 스타일·컬럼 제거 + 4개 `Width` 원복(300/128/150/240)
- [ ] 완료

### Step 5: XAML 바인딩 정적 안전망
- **Context Brief**: `tests/MCPhoto.Tests/XamlResourceTests.cs`에는 "XAML의 `{Binding X}`가 VM 멤버와 일치하는지" 소스 텍스트로 고정하는 관례가 있다(`FrameSelectView_Waiting_Bindings_Exist_On_Vm`, 444행). 바인딩 오타는 예외 없이 조용히 실패하므로 이 그물이 필요하다.
- **대상 파일**: `tests/MCPhoto.Tests/XamlResourceTests.cs`
- **선행 조건**: Step 4
- **구현 내용**: §8.3 T11 — `UserMgmtView.xaml`에 `\{Binding\s+FrameCountText\s*[,}]` 매치가 있고 `typeof(MCPhoto.App.ViewModels.UserRowViewModel).GetProperty("FrameCountText")`가 null이 아님을 단언. 같은 테스트에서 파일 텍스트에 `일일`·`한도`가 없음을 단언해 범위 경계(§1)를 고정한다.
- **검증 명령**: `dotnet test MCPhoto.sln -c Debug --filter "FullyQualifiedName~XamlResourceTests"`
- **완료 기준**:
  - [관측] 신규 테스트 통과. 바인딩 문자열을 일부러 오타 내면 실패한다(주석에만 있는 이름은 통과시키지 않는 정규식)
  - [non-goal] `XamlResourceTests`의 기존 케이스(테마 키·스피너·아이콘·MainWindow) 결과 불변
  - [trigger] 검사 대상은 `UserMgmtView.xaml` 파일 텍스트 하나 — 창을 띄우지 않는다(headless 유지)
- **롤백**: 추가한 `[Fact]` 제거
- [ ] 완료

### Step 6: 문서 갱신
- **Context Brief**: `docs/analysis`는 "현재 동작"의 진실원, `docs/design`은 "왜 그렇게 결정했는가"다. 구현이 끝났으므로 동작 규격에 표시 항목을 싣고, 상위 설계의 추후 개선 항목에서 이 건을 완료로 내린다. **일일 QR 한도 편집 UI는 여전히 미착수**이므로 완료로 표시하면 안 된다.
- **대상 파일**: `docs/analysis/13-client-behavior-spec.md`, `docs/analysis/11-exe-app-features.md`, `docs/design/wpf-frame-ownership-binding-design.md`
- **선행 조건**: Step 4
- **구현 내용**: §9 표의 4개 지점. §9.1 문안을 그대로 쓴다. `wpf-frame-ownership-binding-design.md` §17 `~~4~~` 행은 "개인 프레임 수 = **완료(2026-08-07)**, 설계 링크 `./wpf-usermgmt-frame-count-design.md`" + "일일 QR 한도 편집 UI는 과금 도입 때(미착수)"로 다시 쓴다.
- **검증 명령**: `rg -n "개인 프레임 개수|개인 프레임 수" docs/analysis/13-client-behavior-spec.md docs/analysis/11-exe-app-features.md docs/design/wpf-frame-ownership-binding-design.md`
- **완료 기준**:
  - [관측] 3개 문서에서 각각 히트. §17 항목 4에 `완료`와 이 설계 문서 링크가 있고, 같은 행에 한도 UI가 **미착수**로 남아 있다
  - [non-goal] 다른 절·다른 이터레이션 기록 불변. 빌드 버전·릴리스 노트 미변경
  - [trigger] 문서 갱신은 Step 4까지 끝난 뒤에만 — 구현 전에 "완료"로 쓰지 않는다
- **롤백**: 문서 3개 원복
- [ ] 완료

### Step 7: 전체 게이트
- **Context Brief**: 이 작업의 수락 조건은 솔루션 전체 빌드·테스트다. 변경 전 기준선은 **995 통과 / 0 실패**다.
- **대상 파일**: 없음(검증만)
- **선행 조건**: Step 1~6
- **구현 내용**: 전체 빌드·테스트 실행. 실패 시 원인 단계로 되돌아가 수정한다. `build-verify` 스킬이 있으면 그것을 쓴다.
- **검증 명령**: `dotnet build MCPhoto.sln -c Debug` → `dotnet test MCPhoto.sln -c Debug`
- **완료 기준**:
  - [관측] 빌드 오류 0 · 테스트 실패 0 · 통과 수 = 995 + 신규 추가분(T1~T11 = 11건 기준 1006). 신규 파일 경고 0
  - [non-goal] 기존 995건 중 어느 것도 실패·건너뜀으로 바뀌지 않는다. `git status`에 새 브랜치·커밋 없음(변경만 남긴다)
  - [trigger] 실행은 리포 루트(`E:\Study\photobooth`)에서 — 다른 경로에서 실행하면 `MCPhoto.sln`을 찾지 못한다
- **롤백**: 해당 단계 롤백 지침을 따른다
- [ ] 완료

---

## 12. 완결성 게이트 (architect 자체 검사)

- [x] 검증된 사실(F1~F13) / 미검증 가정(A1~A3) 분리
- [x] 모든 가정에 검증 단계 매핑 (A1→Step 2·6(T10), A2→Step 4, A3→D-5 상한으로 완화)
- [x] 모든 단계에 7개 필수 필드
- [x] 완료 기준 전부 관측 기반 3문 형식(UI 단계 Step 4·5는 non-goal·trigger 포함)
- [x] 검증 명령이 자동 실행 가능
- [x] 요구사항 4개 제약 ↔ 구현 지점 대응표(§4.5)
- [x] 범위 경계(만들지 않는 것) 명시 + 테스트로 고정(T11)
