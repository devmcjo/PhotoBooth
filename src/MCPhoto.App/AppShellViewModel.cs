using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MCPhoto.App.ViewModels;
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

    /// <summary>무동작 후 경고 팝업까지(초). 2분. (it8 §2 A1)</summary>
    public int IdleWarningSeconds { get; set; } = 120;

    /// <summary>경고 팝업 카운트다운(초). 0 도달 시 홈 복귀(로그아웃 없음). (it8 §2 A1)</summary>
    public int IdleCountdownSeconds { get; set; } = 10;

    // 유휴 경고 오버레이 상태(모달 오버레이 — 현재 화면 유지한 채 위에 표시).
    [ObservableProperty] private bool _isIdleWarningVisible;
    [ObservableProperty] private int _idleCountdownRemaining;

    private IdleCountdown? _idleCountdown;
    private DispatcherTimer? _idleCountdownTimer;

    /// <summary>오버레이(설정/로그인) 진입 전 상태 — 복귀 대상. (it2 §5.3)</summary>
    private AppState _returnState = AppState.Home;

    /// <summary>계정 페이지 진입 모드(비번변경/계정생성/관리자). Account VM 생성 직후 주입. (it5 §5 C2)</summary>
    private ViewModels.AccountMode _pendingAccountMode = ViewModels.AccountMode.PasswordChange;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTopBarVisible))]
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

    /// <summary>상단 바 좌측 라벨: 비로그인="로그인", 로그인=계정 ID.</summary>
    public string AccountLabel => CurrentUser?.Id ?? "로그인";

    /// <summary>상단 바 표시 여부(촬영·QR 팝업에서 숨김).</summary>
    public bool IsTopBarVisible => SessionStateMachine.IsTopBarVisible(CurrentState);

    public SessionContext Session => _session;
    public ISettingsService Settings => _settings;

    public AppShellViewModel(
        IIdleWatchdog idle,
        ISettingsService settings,
        IServiceProvider services,
        SessionContext session,
        ILogger<AppShellViewModel>? logger = null)
    {
        _idle = idle;
        _settings = settings;
        _services = services;
        _session = session;
        _logger = logger;
        _dispatcher = Dispatcher.CurrentDispatcher;

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
    }

    /// <summary>앱 시작 시 홈 화면 진입.</summary>
    public void Startup() => _ = NavigateAsync(AppState.Home);

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
    /// 오버레이(설정/로그인) 진입: 현재 상태를 복귀 지점으로 저장 후 전이. (it2 §5.3)
    /// 설정/로그인 자기 자신 재진입 시엔 복귀 지점을 덮어쓰지 않는다.
    /// </summary>
    public async Task NavigateToOverlayAsync(AppState target)
    {
        if (CurrentState is not (AppState.Settings or AppState.Login))
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
        AppState.Done => _services.GetRequiredService<DoneViewModel>(),
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

    /// <summary>우상단 설정 버튼 → 설정 페이지(오버레이 진입).</summary>
    [RelayCommand]
    private async Task OpenSettings()
    {
        IsAccountPopupOpen = false;
        await NavigateToOverlayAsync(AppState.Settings);
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

    /// <summary>계정 팝오버 → 비밀번호 변경 전용 페이지.</summary>
    [RelayCommand]
    private Task OpenPasswordChange() => NavigateToAccountAsync(ViewModels.AccountMode.PasswordChange);

    /// <summary>계정 팝오버(power) → 계정 생성 전용 페이지.</summary>
    [RelayCommand]
    private Task OpenAccountCreate() => NavigateToAccountAsync(ViewModels.AccountMode.AccountCreate);

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
        (_idle as IDisposable)?.Dispose();
    }
}
