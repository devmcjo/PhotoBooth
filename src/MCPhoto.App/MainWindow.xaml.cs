using System.Windows;
using MCPhoto.Core.Settings;

namespace MCPhoto.App;

/// <summary>
/// 앱 셸 창. AppShellViewModel 상태머신을 ContentControl에 바인딩, 유휴 감시용 사용자 활동 통지,
/// displayMode/windowBounds 복원. 관리자 진입은 상단 바 설정→관리자 섹션으로 대체(롱프레스 폐지, it2 §3.4).
/// </summary>
public partial class MainWindow : Window
{
    private readonly ISettingsService _settings;
    private readonly AppShellViewModel _shell;

    public MainWindow(ISettingsService settings, AppShellViewModel shell)
    {
        _settings = settings;
        _shell = shell;
        InitializeComponent();

        DataContext = _shell;
        ApplyDisplaySettings();
        // 설정에서 표시 모드 변경·저장 시 재시작 없이 즉시 반영. (it9 후속)
        _shell.DisplayModeApplyRequested += ApplyDisplaySettings;
        Loaded += (_, _) => _shell.Startup();
    }

    // ── 사용자 활동 통지(유휴 타이머 리셋) ──

    private void OnAnyUserActivity(object sender, RoutedEventArgs e) => _shell.NotifyUserActivity();

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
            _ = _settings.Save(); // 창 종료 시 창 위치 저장(반환값 무시, it3 §3)
        }
        _shell.Dispose();
        base.OnClosing(e);
    }
}
