using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MCPhoto.App.ViewModels;
using MCPhoto.Core.Accounts;
using MCPhoto.Core.Build;
using MCPhoto.Core.Models;
using MCPhoto.Core.Navigation;
using MCPhoto.Core.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MCPhoto.App;

/// <summary>
/// 앱 셸 상태 머신·네비게이션. 상태별 화면 VM을 CurrentViewModel로 스왑한다. (architecture §4)
/// 유휴 타임아웃·전역 예외 시 Home 복귀(안정성 안전망).
/// </summary>
public sealed partial class AppShellViewModel : ObservableObject, IDisposable
{
    private readonly IIdleWatchdog _idle;
    private readonly ISettingsService _settings;
    private readonly IServiceProvider _services;
    private readonly SessionContext _session;
    private readonly ILogger<AppShellViewModel>? _logger;
    private readonly Dispatcher _dispatcher;
    private readonly IBuildInfoService? _buildInfo;

    /// <summary>
    /// 테스트 로그인 모드(it23 B부). 미주입(테스트 다수)이면 기능 전체가 비활성이다.
    /// ⚠️ 이 필드로 분기할 때는 <see cref="ITestModeService.IsTestUser"/>만 쓴다 — <c>IsEnabled</c>로 분기하면
    ///    실계정 세션에도 우회가 적용된다(불변식 TM3). 예외는 배너 표시(<see cref="IsTestMode"/>) 하나다.
    /// </summary>
    private readonly ITestModeService? _testMode;

    /// <summary>무동작 후 경고 팝업까지(초). 2분. (it8 §2 A1)</summary>
    public int IdleWarningSeconds { get; set; } = 120;

    /// <summary>경고 팝업 카운트다운(초). 0 도달 시 홈 복귀(로그아웃 없음). (it8 §2 A1)</summary>
    public int IdleCountdownSeconds { get; set; } = 10;

    // 유휴 경고 오버레이 상태(모달 오버레이 — 현재 화면 유지한 채 위에 표시).
    [ObservableProperty] private bool _isIdleWarningVisible;
    [ObservableProperty] private int _idleCountdownRemaining;

    private IdleCountdown? _idleCountdown;
    private DispatcherTimer? _idleCountdownTimer;

    // ── 세션 완료 토스트(전체화면 완료 화면 폐지의 대체물) ──

    /// <summary>세션 완료 안내 문구(단일 지점 — 완료 경로가 둘이므로 문구가 갈리지 않게 상수로 둔다).</summary>
    public const string SessionCompleteMessage = "촬영이 완료되었습니다. 홈 화면으로 돌아갑니다.";

    /// <summary>토스트 자동 소멸까지(초). 무인 키오스크라 사용자가 닫지 않아도 사라져야 한다.</summary>
    public int ToastSeconds { get; set; } = 5;

    /// <summary>토스트 문구. 빈 문자열이면 미노출.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasToast))]
    private string _toastMessage = string.Empty;

    /// <summary>토스트 노출 여부(문구가 있을 때만).</summary>
    public bool HasToast => !string.IsNullOrEmpty(ToastMessage);

    private DispatcherTimer? _toastTimer;

    /// <summary>오버레이(설정/로그인) 진입 전 상태 — 복귀 대상. (it2 §5.3)</summary>
    private AppState _returnState = AppState.Home;

    /// <summary>계정 페이지 진입 모드(비번변경/계정생성/관리자). Account VM 생성 직후 주입. (it5 §5 C2)</summary>
    private ViewModels.AccountMode _pendingAccountMode = ViewModels.AccountMode.Account;


    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTopBarVisible))]
    [NotifyPropertyChangedFor(nameof(IsHome))]
    [NotifyPropertyChangedFor(nameof(IsSettings))]
    private AppState _currentState = AppState.Home;

    [ObservableProperty]
    private ViewModelBase? _currentViewModel;

    // ── 상단 바 계정 상태 (it3 §2: 단일 소스 = SessionContext, 미러 없음) ──

    /// <summary>계정 팝오버 표시 여부(상단 바 로그인 버튼 토글).</summary>
    [ObservableProperty]
    private bool _isAccountPopupOpen;

    /// <summary>계정 상태는 세션(단일 소스)에서 직접 읽는다. CurrentUserChanged 구독으로 통지.</summary>
    public Core.Models.User? CurrentUser => _session.CurrentUser;
    public bool IsLoggedIn => CurrentUser is not null;
    public bool IsGuest => CurrentUser is null;
    public bool IsPower => CurrentUser?.Role.IsPower() == true;

    // ── it13: TempUser QR 한도 상태(역할+한도 합성). effective QR 계산·설정 게이트 입력(§7.5) ──

    /// <summary>TempUser 로그인 시 1회 조회한 서버 사용량 상태(비TempUser·게스트·미조회는 null).</summary>
    private QrUsageStatus? _tempUserQrStatus;

    /// <summary>
    /// TempUser이고 QR 한도 초과인지(역할+한도 합성). 비TempUser(User/Manager/Admin/게스트)는 항상 false.
    /// effective QR 계산(QrEffectivePolicy) 입력. 서버 미도달로 상태 미조회(null)면 false=fail-open(§7.5·§8.5).
    /// </summary>
    public bool IsTempUserQrBlocked =>
        CurrentUser?.Role == UserRole.TempUser && _tempUserQrStatus?.Blocked == true;

    /// <summary>초과 사유(설정 문구용). TempUser 아니거나 미초과·미조회면 Ok.</summary>
    public QrGateReason TempUserQrReason =>
        CurrentUser?.Role == UserRole.TempUser ? (_tempUserQrStatus?.Reason ?? QrGateReason.Ok) : QrGateReason.Ok;

    /// <summary>상단 바 계정 버튼의 접근 이름·툴팁: 비로그인="로그인", 로그인=계정 ID.
    /// it21 §6.2: 버튼 표면에서 텍스트가 사라지고 아이콘/아바타가 되므로, 이 문자열은 툴팁으로 옮겨간다.</summary>
    public string AccountLabel => CurrentUser?.Id ?? "로그인";

    /// <summary>
    /// 계정 버튼 아바타에 표시할 이니셜 1글자(대문자). 게스트는 빈 문자열. (it21 §6.2)
    /// 아이콘 전용화로 사라진 "누가 로그인했는지"를 텍스트 pill 없이 전달한다.
    /// </summary>
    public string AccountInitial =>
        CurrentUser?.Id is { Length: > 0 } id ? id[..1].ToUpperInvariant() : string.Empty;

    /// <summary>상단 바 표시 여부(촬영·QR 팝업에서 숨김).</summary>
    public bool IsTopBarVisible => SessionStateMachine.IsTopBarVisible(CurrentState);

    /// <summary>현재 홈 화면인지(홈 버튼은 홈에서 숨김). (it9 후속)</summary>
    public bool IsHome => CurrentState == AppState.Home;

    /// <summary>현재 설정 화면인지(설정 버튼은 설정 화면에서 숨김 — 자기 화면 재진입 방지).</summary>
    public bool IsSettings => CurrentState == AppState.Settings;

    /// <summary>앱 하단 버전 표기(예: "v1.1.6"). 로그인 무관 상시 노출.
    /// it18: 배포 채널(종전 " · Beta")은 표기하지 않는다 — 개발·알파 서버를 운영하지 않아 의미가 없었다.
    /// 빌드 정보 미주입 시 빈 문자열. 값은 시작 시 고정(불변) → 통지 불필요.</summary>
    public string VersionText => _buildInfo?.DisplayText ?? string.Empty;

    public SessionContext Session => _session;
    public ISettingsService Settings => _settings;

    /// <summary>표시 모드 변경 즉시 반영 요청(설정 저장 시). MainWindow가 구독해 ApplyDisplaySettings 재실행. (it9 후속)</summary>
    public event Action? DisplayModeApplyRequested;

    /// <summary>설정 저장 후 표시 모드(전체화면/창모드)를 즉시 적용하도록 셸 창에 통지.</summary>
    public void RequestApplyDisplayMode() => DisplayModeApplyRequested?.Invoke();

    /// <summary>
    /// 설정 저장 직전, 현재 창 기하를 <c>AppSettings.WindowBounds</c>에 반영하도록 셸 창에 요청. (it16 §7.5)
    /// WindowBounds는 종전에 창을 닫을 때만 갱신됐다 — 저장 시점에도 신선하게 만들어 전체화면→창모드 복귀가
    /// "사용자가 마지막에 두었던 자리"가 되게 한다. MainWindow가 구독해 CaptureWindowBounds를 실행한다.
    /// </summary>
    public event Action? WindowBoundsCaptureRequested;

    /// <summary>현재 창 기하를 설정 객체에 반영하도록 셸 창에 통지(저장 직전 호출).</summary>
    public void RequestCaptureWindowBounds() => WindowBoundsCaptureRequested?.Invoke();

    public AppShellViewModel(
        IIdleWatchdog idle,
        ISettingsService settings,
        IServiceProvider services,
        SessionContext session,
        ILogger<AppShellViewModel>? logger = null,
        ITestModeService? testMode = null)
    {
        _idle = idle;
        _settings = settings;
        _services = services;
        _session = session;
        _logger = logger;
        _testMode = testMode;
        _dispatcher = Dispatcher.CurrentDispatcher;
        // 빌드 정보는 선택적(미등록/테스트 시 null → 표기 비노출). 앱에선 DI로 항상 주입(it18: 어셈블리 출처).
        _buildInfo = services.GetService<IBuildInfoService>();

        _idle.IdleTimeout += OnIdleTimeout;
        _session.CurrentUserChanged += OnCurrentUserChanged;
    }

    /// <summary>세션 계정 변경(로그인/로그아웃) → 상단 바 바인딩 자동 갱신. (it3 §2.2)</summary>
    private void OnCurrentUserChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(CurrentUser));
        OnPropertyChanged(nameof(IsLoggedIn));
        OnPropertyChanged(nameof(IsGuest));
        OnPropertyChanged(nameof(IsPower));
        OnPropertyChanged(nameof(AccountLabel));
        OnPropertyChanged(nameof(AccountInitial));   // it21 §6.2: 계정 버튼 아바타
        // ⚠️ it23 §B9.3: 이 통지가 없으면 로그아웃 후에도 배너가 "관리자 권한으로 실행 중"이라는 거짓을 말한다.
        OnPropertyChanged(nameof(TestModeBannerText));

        // it13: 계정 변경마다 TempUser 사용량 상태를 재평가(§7.5). 로그아웃·비TempUser는 즉시 클리어,
        //        TempUser 로그인은 서버 상태를 1회 조회(fire-and-forget, 완료 시 파생 프로퍼티 통지).
        _tempUserQrStatus = null;
        OnPropertyChanged(nameof(IsTempUserQrBlocked));
        OnPropertyChanged(nameof(TempUserQrReason));
        if (CurrentUser?.Role == UserRole.TempUser)
            _ = LoadTempUserQrStatusAsync();
    }

    /// <summary>
    /// TempUser 로그인 시 서버 사용량 상태 1회 조회(§7.5). 실패(null)면 fail-open(허용, 서버가 업로드 거부).
    /// fire-and-forget으로 호출되므로 예외를 삼키지 않도록 내부에서 방어(async void 아님 — Task 반환).
    /// </summary>
    private async Task LoadTempUserQrStatusAsync()
    {
        var user = CurrentUser;
        try
        {
            var svc = _services.GetService<IQrUsageService>();
            if (svc is null) return;   // 미등록(테스트 등) — fail-open 유지
            var status = await svc.GetStatusAsync().ConfigureAwait(true);   // UI 컨텍스트 복귀(파생 통지 안전)

            // 조회 중 로그아웃·계정 전환됐으면 stale 응답 폐기(경합 방어).
            if (!ReferenceEquals(user, CurrentUser)) return;

            _tempUserQrStatus = status;
            OnPropertyChanged(nameof(IsTempUserQrBlocked));
            OnPropertyChanged(nameof(TempUserQrReason));
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "TempUser QR 사용량 조회 실패 — fail-open");
        }
    }

    // ── it23 B부: 테스트 로그인 모드 배너 · 재로그인 ──

    /// <summary>
    /// 테스트 모드 경고 배너 노출 여부. ⚠️ 판정이 <see cref="ITestModeService.IsEnabled"/> <b>단독</b>인 유일한
    /// 지점이다 — 배너는 세션 상태와 무관하게 항상 떠 있어야 한다(불변식 TM4). 릴리스 빌드에도 이 기능이
    /// 포함되므로 실운영 오투입이 즉시 발각되게 하는 대가다.
    /// </summary>
    public bool IsTestMode => _testMode?.IsEnabled == true;

    /// <summary>배너 문구(로그아웃 상태). 테스트 모드는 세션이 아니라 <b>설정</b>이므로 로그아웃해도 위험은 남는다.</summary>
    public const string TestModeBannerLoggedOut =
        "⚠ 테스트 모드가 켜져 있습니다(현재 로그아웃). 실제 운영에 사용하지 마세요.";

    /// <summary>배너 문구(실제 계정 병행 로그인). 우회는 비활성이지만 ini가 켜져 있다는 사실은 알려야 한다.</summary>
    public const string TestModeBannerRealAccount =
        "⚠ 테스트 모드 설정이 켜져 있습니다(현재는 실제 계정으로 로그인). 실제 운영에 사용하지 마세요.";

    /// <summary>
    /// 배너 문구(테스트 계정 로그인 중). <b>역할 라벨을 반드시 표시</b>한다 — <c>Role</c> 오타로 다른 역할이
    /// 섰을 때 즉시 발각되게 하는 안전망이며, 그것이 "잘못된 값 → 기본값 폴백"을 안전하게 만든다.
    /// 이메일도 표시한다(개인 프레임 소유 키라 프레임이 안 보일 때 첫 확인 대상이다).
    /// </summary>
    public static string FormatTestModeBanner(string roleLabel, string email) =>
        $"⚠ 테스트 모드 — 인증 없이 {roleLabel} 권한으로 실행 중입니다. 실제 운영에 사용하지 마세요. ({email})";

    /// <summary>현재 상태에 맞는 배너 문구. 테스트 모드가 꺼져 있으면 빈 문자열(배너 자체가 Collapsed).</summary>
    public string TestModeBannerText
    {
        get
        {
            if (_testMode?.IsEnabled != true) return string.Empty;
            var user = CurrentUser;
            if (user is null) return TestModeBannerLoggedOut;
            if (!_testMode.IsTestUser(user)) return TestModeBannerRealAccount;
            return FormatTestModeBanner(user.Role.ToLabel(), user.Email ?? string.Empty);
        }
    }

    /// <summary>로그인 화면의 [테스트 계정으로 로그인 ({역할})] 라벨. 테스트 모드가 꺼져 있으면 빈 문자열.</summary>
    public string TestLoginLabel => _testMode?.IsEnabled == true
        ? $"테스트 계정으로 로그인 ({_testMode.Options.Role.ToLabel()})"
        : string.Empty;

    /// <summary>
    /// 로그아웃한 뒤 다시 테스트 계정으로 돌아오는 경로(§B8.5). 유일한 대안이 앱 재시작인데, 역할별 UI를
    /// 비교하는 QA 작업에서 매번 재시작은 실용성을 해친다.
    /// <para>
    /// <b>같은 인스턴스</b>를 다시 태우므로 <c>IsTestUser</c>가 계속 참이다(PIN 생략·QR 주입 유지).
    /// 후처리는 <c>LoginWithGoogle</c>과 동일(오버레이 복귀).
    /// </para>
    /// </summary>
    [RelayCommand]
    private async Task LoginAsTestUser()
    {
        if (_testMode?.TestUser is not { } user) return;
        _logger?.LogWarning("테스트 계정 재로그인: id={Id} role={Role}", user.Id, user.Role.ToFirestoreValue());
        _session.Login(user);
        await ReturnFromOverlay();
    }

    /// <summary>
    /// 앱 시작 시 홈 화면 진입. 테스트 모드가 켜져 있으면 <b>홈 진입 직전</b>에 가짜 계정을 세션에 태운다.
    /// <para>
    /// 왜 여기인가(§B5.3): 셸이 이미 <c>CurrentUserChanged</c>를 구독한 뒤이므로 로그인 통지가 유실되지 않는다
    /// (그 통지가 하는 일 중 하나가 TempUser QR 상태 조회 시작이다 — <c>App.OnStartup</c>에서 로그인하면
    /// <c>QrBlocked</c> 주입이 절대 반영되지 않는다). 또 홈 화면 VM이 처음부터 올바른 역할을 본다.
    /// </para>
    /// 로그 레벨이 Warning인 이유: "이 실행은 인증을 우회했다"는 사실은 사후 조사에서 눈에 띄어야 한다.
    /// </summary>
    public void Startup()
    {
        if (_testMode?.TestUser is { } testUser)
        {
            _logger?.LogWarning("테스트 모드 로그인: id={Id} role={Role} ini={Path}",
                testUser.Id, testUser.Role.ToFirestoreValue(), _testMode.SourcePath);
            _session.Login(testUser);   // ⚠️ IBackendSession은 건드리지 않는다(토큰 없음 — 불변식 TM1)
        }
        _ = NavigateAsync(AppState.Home);
    }

    /// <summary>상태 전이 + 화면 VM 스왑. 불법 전이는 거부. </summary>
    public Task<bool> NavigateAsync(AppState target) => NavigateInternalAsync(target, bypassValidation: false);

    /// <summary>
    /// 실제 전이. bypassValidation=true면 전이표 검증을 면제(오버레이 복귀 전용, it2 §5.3).
    /// 복귀는 저장된 유효 상태(_returnState)로만 가므로 안전하다.
    /// </summary>
    private async Task<bool> NavigateInternalAsync(AppState target, bool bypassValidation)
    {
        if (!bypassValidation && target != AppState.Home && !SessionStateMachine.CanTransition(CurrentState, target))
        {
            _logger?.LogWarning("불법 전이 거부: {From} → {To}", CurrentState, target);
            return false;
        }

        // 이전 화면 이탈
        if (CurrentViewModel is { } old)
        {
            try { await old.OnLeaveAsync(); }
            catch (Exception ex) { _logger?.LogError(ex, "화면 이탈 오류: {State}", CurrentState); }
        }

        CurrentState = target;
        CurrentViewModel = CreateViewModel(target);
        UpdateIdleWatch();

        if (CurrentViewModel is { } next)
        {
            try { await next.OnEnterAsync(); }
            catch (Exception ex) { _logger?.LogError(ex, "화면 진입 오류: {State}", target); }
        }
        return true;
    }

    /// <summary>
    /// 오버레이(설정/로그인/계정) 진입: 현재 상태를 복귀 지점으로 저장 후 전이. (it2 §5.3)
    /// 오버레이 화면에서 오버레이로 전환할 때는 복귀 지점을 덮어쓰지 않는다 — 덮어쓰면 [닫기]가
    /// 자기 자신으로 복귀해 아무 일도 하지 않는다(it19: 계정관리↔관리자도구 전환 후 닫기 무반응 버그).
    /// </summary>
    public async Task NavigateToOverlayAsync(AppState target)
    {
        if (!SessionStateMachine.IsOverlayScreen(CurrentState))
            _returnState = CurrentState;
        await NavigateAsync(target);
    }

    /// <summary>
    /// 오버레이에서 복귀: 저장된 상태(세션 화면 포함)로 직접 복귀. 세션 데이터 보존(Reset 안 함). (it2 §5.3)
    /// 저장된 _returnState는 진입 시점의 유효 상태이므로 전이표 검증을 면제한다
    /// (Settings→Result 등은 전이표에 없지만 복귀는 합법 — 진입의 역방향).
    /// </summary>
    public async Task<bool> ReturnFromOverlay()
    {
        var ok = await NavigateInternalAsync(_returnState, bypassValidation: true);
        if (!ok)
            _logger?.LogWarning("오버레이 복귀 실패: {Target}", _returnState);
        return ok;
    }

    /// <summary>상태 → 화면 VM 팩토리(DI).</summary>
    private ViewModelBase? CreateViewModel(AppState state) => state switch
    {
        AppState.Home => _services.GetRequiredService<HomeViewModel>(),
        AppState.Login => _services.GetRequiredService<LoginGuestViewModel>(),
        AppState.FrameSelect => _services.GetRequiredService<FrameSelectViewModel>(),
        AppState.Guide => _services.GetRequiredService<GuideViewModel>(),
        AppState.Capture => _services.GetRequiredService<CaptureViewModel>(),
        AppState.CutSelect => _services.GetRequiredService<CutSelectViewModel>(),
        AppState.Result => _services.GetRequiredService<ResultViewModel>(),
        AppState.Qr => _services.GetRequiredService<QrPopupViewModel>(),
        AppState.FrameEditor => _services.GetRequiredService<FrameEditorViewModel>(),
        AppState.Settings => _services.GetRequiredService<SettingsViewModel>(),
        AppState.UserMgmt => _services.GetRequiredService<UserMgmtViewModel>(),
        AppState.Account => CreateAccountViewModel(),
        _ => null
    };

    private AccountViewModel CreateAccountViewModel()
    {
        var vm = _services.GetRequiredService<AccountViewModel>();
        vm.Mode = _pendingAccountMode; // 진입 모드 주입(팝오버 항목이 지정)
        return vm;
    }

    /// <summary>
    /// 프레임 편집기 진입. <b>항상 신규 생성</b>이다 — 기존 프레임 수정 기능은 폐지됐고(설계 D-16),
    /// 재활용은 편집기 안의 [기존 프레임 불러오기]가 담당한다.
    /// </summary>
    public async Task OpenFrameEditor() => await NavigateAsync(AppState.FrameEditor);

    /// <summary>
    /// 어디서든 Home으로 강제 복귀. 촬영 세션 데이터는 항상 폐기.
    /// clearUser=true(유휴·세션완료=다음 손님)일 때만 로그아웃, 사용자 취소는 로그인 보존. (it3 §2.3)
    /// </summary>
    public void ReturnHome(string reason, bool clearUser = false)
    {
        _logger?.LogInformation("Home 복귀: {Reason} (clearUser={Clear})", reason, clearUser);
        try { _session.Reset(clearUser); }
        catch (Exception ex) { _logger?.LogError(ex, "세션 데이터 폐기 실패"); }

        _idle.Stop();
        _ = NavigateAsync(AppState.Home);
    }

    /// <summary>
    /// 촬영 세션 완료 → 홈 복귀 + 완료 토스트.
    /// <para>
    /// 종전에는 전체화면 완료 화면(`Done`: "감사합니다" + [처음으로] + 6초 자동 복귀)이 이 역할을 했다.
    /// 화면을 하나 더 거치는 것보다 홈으로 바로 돌아가는 편이 키오스크 회전에 낫다는 판단으로 폐지하고,
    /// 완료 사실은 토스트로만 알린다(자동 소멸 + [확인]으로 즉시 닫기).
    /// </para>
    /// 촬영 후 로그인은 유지한다(it5 §4 B8) — 로그아웃은 계정 메뉴 수동 또는 유휴 타임아웃만.
    /// 완료 경로가 둘(QR 미사용 즉시 완료 · QR 팝업 [완료])이므로 반드시 이 지점을 지나게 한다.
    /// </summary>
    public void CompleteSession(string reason)
    {
        ReturnHome(reason, clearUser: false);
        ShowToast(SessionCompleteMessage);
    }

    /// <summary>토스트 노출(직전 토스트는 교체). <see cref="ToastSeconds"/> 후 자동 소멸.</summary>
    public void ShowToast(string message)
    {
        StopToastTimer();
        ToastMessage = message;
        if (string.IsNullOrEmpty(message)) return;

        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(Math.Max(1, ToastSeconds)) };
        _toastTimer.Tick += (_, _) => DismissToast();
        _toastTimer.Start();
    }

    /// <summary>토스트 닫기([확인] 또는 자동 소멸). 이미 닫혀 있으면 무해한 no-op.</summary>
    [RelayCommand]
    private void DismissToast()
    {
        StopToastTimer();
        ToastMessage = string.Empty;
    }

    private void StopToastTimer()
    {
        _toastTimer?.Stop();
        _toastTimer = null;
    }

    /// <summary>
    /// 사용자 활동 통지. 경고 팝업 표시 중에는 무시(버튼으로만 해제, 설계 §2.2) —
    /// 경고 전 단계에서만 warning 타이머를 리셋한다.
    /// </summary>
    public void NotifyUserActivity()
    {
        if (IsIdleWarningVisible) return;
        _idle.Reset();
    }

    private void UpdateIdleWatch()
    {
        // 화면 전환 시 경고 오버레이가 떠 있으면 내린다(예: 촬영 진입 등).
        HideIdleWarning();
        if (SessionStateMachine.IsSessionActive(CurrentState))
            _idle.Start(IdleWarningSeconds);
        else
            _idle.Stop();
    }

    /// <summary>
    /// 2분 무동작 → 경고 팝업 표시 + 10초 카운트다운 시작. 즉시 홈 복귀·로그아웃 없음. (it8 §2 A1)
    /// 카운트다운 0 → 홈 복귀(clearUser:false). [이어서]/활동은 취소, [메인]은 즉시 홈.
    /// </summary>
    private void OnIdleTimeout(object? sender, EventArgs e)
        => _dispatcher.BeginInvoke(ShowIdleWarning);

    private void ShowIdleWarning()
    {
        if (IsIdleWarningVisible) return;
        _idle.Stop(); // 경고 단계에선 warning 타이머 정지(카운트다운이 이어받음)
        _idleCountdown = new IdleCountdown(IdleCountdownSeconds);
        IdleCountdownRemaining = _idleCountdown.Remaining;
        IsIdleWarningVisible = true;

        _idleCountdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _idleCountdownTimer.Tick += OnIdleCountdownTick;
        _idleCountdownTimer.Start();
    }

    private void OnIdleCountdownTick(object? sender, EventArgs e)
    {
        if (_idleCountdown is null) return;
        bool expired = _idleCountdown.Tick();
        IdleCountdownRemaining = _idleCountdown.Remaining;
        if (expired)
        {
            HideIdleWarning();
            ReturnHome("유휴 타임아웃", clearUser: false); // 로그아웃 절대 금지(it8 A1)
        }
    }

    private void HideIdleWarning()
    {
        _idleCountdownTimer?.Stop();
        if (_idleCountdownTimer is not null)
            _idleCountdownTimer.Tick -= OnIdleCountdownTick;
        _idleCountdownTimer = null;
        _idleCountdown = null;
        IsIdleWarningVisible = false;
    }

    // ── 공통 네비게이션 커맨드 ──

    [RelayCommand]
    private void GoHome() => ReturnHome("사용자 취소");

    // ── 상단 바 커맨드 (it2 §3.2) ──

    /// <summary>우상단 설정 버튼 → 설정 페이지(오버레이 진입). 로그인 사용자는 PIN 게이트 통과 필수.</summary>
    [RelayCommand]
    private async Task OpenSettings()
    {
        IsAccountPopupOpen = false;
        // 게스트는 무가드(현행 유지). 로그인 사용자는 PIN 게이트 — 취소/불일치면 진입하지 않음.
        if (_session.CurrentUser is { } user && !await EnsurePinGateAsync(user))
            return;
        await NavigateToOverlayAsync(AppState.Settings);
    }

    /// <summary>
    /// PIN 게이트 공통(it15 §6.2). HasPin이면 확인, 아니면 최초 설정 강제(데드락 방지).
    /// 계정 서비스·다이얼로그 서비스 미등록은 fail-closed(진입 거부) — it14 규약 승계.
    /// 설정 진입·계정 관리 진입 두 곳이 이 메서드를 공유한다(동일 PIN·동일 다이얼로그).
    /// </summary>
    /// <remarks>
    /// 내부가 동기인데 <see cref="Task{TResult}"/>인 이유: IPinPromptDialogService는 ShowDialog() 기반이라
    /// 동기 반환이다. 호출부가 await 문맥이므로 시그니처를 Task로 두어 향후 비동기 다이얼로그 전환 시
    /// 호출부 변경이 없게 한다.
    /// </remarks>
    public Task<bool> EnsurePinGateAsync(MCPhoto.Core.Models.User user)
    {
        // ── 테스트 계정 전용 경로(it23 §B8.4): 서버를 한 번도 호출하지 않는다. ──
        // 왜 필요한가: 이 게이트는 IAccountService(Bearer 필수)를 호출하는데 테스트 모드에는 토큰이 없다 →
        //   예외 → PinPromptWindow가 "확인할 수 없습니다"로 끝나 게이트가 열리지 않는다(fail-closed).
        //   그러면 설정·계정 관리·사용자 관리에 도달할 수 없어 테스트 모드가 "역할 배지만 바뀌는 기능"이 된다.
        // ⚠️ 조건이 IsTestUser(참조 동일성) **한 줄**뿐인 것이 규격이다(불변식 TM3). IsEnabled로 분기하면
        //   테스트 모드가 켜진 채 실제 SSO 로그인한 계정의 PIN 게이트까지 우회되어 인증 우회 취약점이 된다.
        if (_testMode?.IsTestUser(user) == true)
        {
            var testPin = _testMode.Options.Pin;
            // Pin 미설정 → 게이트 생략. 목적이 "빠르게 역할 UI를 본다"이므로 기본 흐름을 막지 않는다.
            if (testPin is null) return Task.FromResult(true);

            var dialog = _services.GetService<Services.IPinPromptDialogService>();
            if (dialog is null) return Task.FromResult(false);   // fail-closed 규약 승계(예외를 만들지 않는다)

            // Pin 설정 → 게이트를 띄우고 **로컬 대조**. PIN 게이트 UI 자체(입력 형식 검증·5회 실패 자동 닫힘·
            // 쿨다운)를 서버 없이 검증할 수 있다. Setup 분기는 쓰지 않는다 — Pin 존재 = HasPin이라 도달 경로가 없다.
            return Task.FromResult(dialog.PromptVerify(p =>
                Task.FromResult(string.Equals(p, testPin, StringComparison.Ordinal))));
        }

        var account = _services.GetService<MCPhoto.Core.Accounts.IAccountService>();
        var pin = _services.GetService<Services.IPinPromptDialogService>();
        if (account is null || pin is null) return Task.FromResult(false); // fail-closed

        var uid = user.Id;
        bool ok = user.HasPin
            ? pin.PromptVerify(p => account.VerifyPinAsync(uid, p))
            : pin.PromptSetup(async p =>   // PIN 미설정 = 최초 진입 → 강제 설정(현재 PIN 확인 없음, 데드락 방지).
              {
                  await account.SetOwnPinAsync(uid, null, p);
                  user.HasPin = true;      // 세션 로컬 반영(재진입 시 확인 경로로 전환).
              });
        return Task.FromResult(ok);
    }

    /// <summary>좌상단 계정 버튼: 비로그인→로그인 페이지, 로그인→계정 팝오버 토글.</summary>
    [RelayCommand]
    private async Task OpenAccount()
    {
        if (IsLoggedIn)
            IsAccountPopupOpen = !IsAccountPopupOpen;
        else
            await NavigateToOverlayAsync(AppState.Login);
    }

    /// <summary>계정 페이지(오버레이) 진입 + 모드 저장. 복귀는 진입 전 화면으로. (it5 §5 C2)</summary>
    private async Task NavigateToAccountAsync(ViewModels.AccountMode mode)
    {
        IsAccountPopupOpen = false;
        _pendingAccountMode = mode;
        await NavigateToOverlayAsync(AppState.Account);
    }

    /// <summary>계정 팝오버 → 계정 관리 페이지(내 정보 + PIN 변경). (it15 §6.3)</summary>
    [RelayCommand]
    private Task OpenAccountManage() => NavigateToAccountAsync(ViewModels.AccountMode.Account);

    /// <summary>계정 팝오버(power) → 관리자 도구(사용자 관리·앱 종료) 페이지.</summary>
    [RelayCommand]
    private Task OpenAdminTools() => NavigateToAccountAsync(ViewModels.AccountMode.Admin);

    /// <summary>사용자 관리 화면에서 관리자 도구(Account/Admin)로 복귀. (it5 §5 C2)</summary>
    public async Task ReturnToAdminToolsAsync()
    {
        _pendingAccountMode = ViewModels.AccountMode.Admin;
        await NavigateAsync(AppState.Account);
    }

    /// <summary>로그아웃: 세션 계정 해제(이벤트 통지) + 홈 복귀. 세션 이미 로그아웃되므로 clearUser 불필요.</summary>
    [RelayCommand]
    private void Logout()
    {
        IsAccountPopupOpen = false;
        _session.Logout();            // CurrentUserChanged 발행 → 상단 바 자동 갱신
        ReturnHome("로그아웃");        // 촬영 데이터 폐기(로그인은 이미 해제됨)
    }

    // ── 유휴 경고 팝업 커맨드 (it8 §2 A1) ──

    /// <summary>[이어서 진행하기]: 경고 해제 + 유휴 타이머 재시작. 현재 화면·로그인 유지.</summary>
    [RelayCommand]
    private void ContinueSession()
    {
        HideIdleWarning();
        if (SessionStateMachine.IsSessionActive(CurrentState))
            _idle.Start(IdleWarningSeconds); // warning 타이머 재시작
    }

    /// <summary>[메인 화면으로]: 즉시 홈 복귀(로그아웃 없음).</summary>
    [RelayCommand]
    private void GoHomeFromIdle()
    {
        HideIdleWarning();
        ReturnHome("유휴 경고 — 메인으로", clearUser: false);
    }

    public void Dispose()
    {
        _idle.IdleTimeout -= OnIdleTimeout;
        _session.CurrentUserChanged -= OnCurrentUserChanged;
        HideIdleWarning(); // 카운트다운 타이머 정리
        StopToastTimer();  // 완료 토스트 타이머 정리
        (_idle as IDisposable)?.Dispose();
    }
}
