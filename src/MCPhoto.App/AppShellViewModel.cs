using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MCPhoto.App.ViewModels;
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

    /// <summary>유휴 타임아웃(초). PRD §10 권장 60~90초.</summary>
    public int IdleTimeoutSeconds { get; set; } = 75;

    [ObservableProperty]
    private AppState _currentState = AppState.Home;

    [ObservableProperty]
    private ViewModelBase? _currentViewModel;

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
    }

    /// <summary>앱 시작 시 홈 화면 진입.</summary>
    public void Startup() => _ = NavigateAsync(AppState.Home);

    /// <summary>상태 전이 + 화면 VM 스왑. 불법 전이는 거부. </summary>
    public async Task<bool> NavigateAsync(AppState target)
    {
        if (target != AppState.Home && !SessionStateMachine.CanTransition(CurrentState, target))
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
        AppState.Admin => _services.GetRequiredService<AdminViewModel>(),
        AppState.UserMgmt => _services.GetRequiredService<UserMgmtViewModel>(),
        _ => null
    };

    /// <summary>어디서든 Home으로 강제 복귀(취소·완료·예외·유휴). 세션 데이터 폐기.</summary>
    public void ReturnHome(string reason)
    {
        _logger?.LogInformation("Home 복귀: {Reason}", reason);
        try { _session.Reset(); }
        catch (Exception ex) { _logger?.LogError(ex, "세션 데이터 폐기 실패"); }

        _idle.Stop();
        _ = NavigateAsync(AppState.Home);
    }

    public void NotifyUserActivity() => _idle.Reset();

    private void UpdateIdleWatch()
    {
        if (SessionStateMachine.IsSessionActive(CurrentState))
            _idle.Start(IdleTimeoutSeconds);
        else
            _idle.Stop();
    }

    private void OnIdleTimeout(object? sender, EventArgs e)
        => _dispatcher.BeginInvoke(() => ReturnHome("유휴 타임아웃"));

    // ── 공통 네비게이션 커맨드 ──

    [RelayCommand]
    private void GoHome() => ReturnHome("사용자 취소");

    public void Dispose()
    {
        _idle.IdleTimeout -= OnIdleTimeout;
        (_idle as IDisposable)?.Dispose();
    }
}
