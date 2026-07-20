using System.Windows;
using System.Windows.Threading;
using MCPhoto.Core.Navigation;
using MCPhoto.Core.Settings;

namespace MCPhoto.App;

/// <summary>
/// 앱 셸 창. AppShellViewModel 상태머신을 ContentControl에 바인딩, 방향별 레이아웃,
/// 좌상단 3초 롱프레스 관리자 진입, 유휴 감시용 사용자 활동 통지, displayMode/windowBounds 복원.
/// </summary>
public partial class MainWindow : Window
{
    private readonly ISettingsService _settings;
    private readonly AppShellViewModel _shell;

    private DispatcherTimer? _longPressTimer;
    private const int AdminLongPressSeconds = 3;

    public MainWindow(ISettingsService settings, AppShellViewModel shell)
    {
        _settings = settings;
        _shell = shell;
        InitializeComponent();

        DataContext = _shell;
        ApplyDisplaySettings();
        Loaded += (_, _) => _shell.Startup();
    }

    // ── 사용자 활동 통지(유휴 타이머 리셋) ──

    private void OnAnyUserActivity(object sender, RoutedEventArgs e) => _shell.NotifyUserActivity();

    // ── 좌상단 3초 롱프레스 → 관리자 진입 ──

    private void OnAdminCornerDown(object sender, RoutedEventArgs e)
    {
        _longPressTimer?.Stop();
        _longPressTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(AdminLongPressSeconds) };
        _longPressTimer.Tick += (_, _) =>
        {
            _longPressTimer?.Stop();
            _longPressTimer = null;
            // 관리자 상태로 전이. 로그인 인증은 AdminViewModel의 진입 게이트가 처리한다.
            _ = _shell.NavigateAsync(AppState.Admin);
        };
        _longPressTimer.Start();
    }

    private void OnAdminCornerUp(object sender, RoutedEventArgs e)
    {
        _longPressTimer?.Stop();
        _longPressTimer = null;
    }

    // ── 표시 모드·창 복원 ──

    private void ApplyDisplaySettings()
    {
        var s = _settings.Current;

        if (s.DisplayMode == DisplayMode.Fullscreen)
        {
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Maximized;
        }
        else
        {
            WindowStyle = WindowStyle.SingleBorderWindow;
            ResizeMode = ResizeMode.CanResize;
            WindowState = WindowState.Normal;

            Width = s.WindowBounds.Width;
            Height = s.WindowBounds.Height;
            if (s.WindowBounds.HasPosition)
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = s.WindowBounds.Left;
                Top = s.WindowBounds.Top;
            }
            else
            {
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }
        }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        var s = _settings.Current;
        if (s.DisplayMode == DisplayMode.Windowed && WindowState == WindowState.Normal)
        {
            s.WindowBounds.Left = Left;
            s.WindowBounds.Top = Top;
            s.WindowBounds.Width = Width;
            s.WindowBounds.Height = Height;
            _settings.Save();
        }
        _shell.Dispose();
        base.OnClosing(e);
    }
}
