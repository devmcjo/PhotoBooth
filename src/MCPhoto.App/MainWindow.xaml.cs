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

    /// <summary>
    /// 실제로 창에 적용된 표시 모드. null = 아직 한 번도 적용하지 않음(=시작).
    /// <see cref="DisplayApplyPolicy"/>가 "모드가 실제로 바뀌었는지"를 판정하는 유일한 기준이다. (it16 §7.4)
    /// </summary>
    private DisplayMode? _appliedMode;

    public MainWindow(ISettingsService settings, AppShellViewModel shell)
    {
        _settings = settings;
        _shell = shell;
        InitializeComponent();

        DataContext = _shell;
        ApplyDisplaySettings();
        // 설정에서 표시 모드 변경·저장 시 재시작 없이 즉시 반영. (it9 후속)
        _shell.DisplayModeApplyRequested += ApplyDisplaySettings;
        // 설정 저장 직전 현재 창 기하를 설정 객체에 반영. (it16 §7.5) 해제는 OnClosing.
        _shell.WindowBoundsCaptureRequested += OnCaptureWindowBounds;
        Loaded += (_, _) => _shell.Startup();
    }

    // ── 사용자 활동 통지(유휴 타이머 리셋) ──

    private void OnAnyUserActivity(object sender, RoutedEventArgs e) => _shell.NotifyUserActivity();

    // ── 표시 모드·창 복원 ──

    /// <summary>
    /// 표시 모드 적용. 시작 시 창 복원과 런타임 모드 변경을 겸하며, 무엇을 할지는 순수 정책
    /// <see cref="DisplayApplyPolicy.Decide"/>가 결정한다. 모드가 그대로면 **완전 무동작**이라
    /// 설정 저장이 창 위치·크기를 건드리지 않는다(it16 §7.2 A안). 전체화면 ↔ 창모드 전환은 종전대로 즉시 반영.
    /// </summary>
    private void ApplyDisplaySettings()
    {
        var s = _settings.Current;
        switch (DisplayApplyPolicy.Decide(s.DisplayMode, _appliedMode))
        {
            case DisplayApplyAction.None:
                return;                                  // 창 기하·상태 불변(위치 점프 방지)

            case DisplayApplyAction.Fullscreen:
                WindowStyle = WindowStyle.None;
                ResizeMode = ResizeMode.NoResize;
                WindowState = WindowState.Maximized;
                break;

            case DisplayApplyAction.WindowedRestoreGeometry:
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
                break;
        }
        _appliedMode = s.DisplayMode;                     // 적용 성공 후에만 기록
    }

    /// <summary>셸의 저장 직전 캡처 요청 핸들러(구독 해제를 위해 메서드 그룹으로 유지 — 람다 금지).</summary>
    private void OnCaptureWindowBounds() => CaptureWindowBounds(_settings.Current);

    /// <summary>
    /// 현재 창 기하를 설정 객체에 반영(창모드 + Normal일 때만). 저장 직전·종료 시 공용. (it16 §7.4)
    /// 판정 기준은 설정값이 아니라 <see cref="_appliedMode"/>(=실제로 창에 적용된 모드)다 —
    /// 저장 도중 설정값이 먼저 바뀌어도 창의 실제 상태를 잘못 캡처하지 않는다.
    /// </summary>
    private void CaptureWindowBounds(AppSettings s)
    {
        if (_appliedMode != DisplayMode.Windowed || WindowState != WindowState.Normal) return;
        s.WindowBounds.Left = Left;
        s.WindowBounds.Top = Top;
        s.WindowBounds.Width = Width;
        s.WindowBounds.Height = Height;
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        CaptureWindowBounds(_settings.Current);
        _ = _settings.Save(); // 창 종료 시 창 위치 저장(반환값 무시, it3 §3)

        // 이벤트 구독 해제(누수 방지) — _shell.Dispose() **전에** 수행한다. (it16 §7.4)
        _shell.DisplayModeApplyRequested -= ApplyDisplaySettings;
        _shell.WindowBoundsCaptureRequested -= OnCaptureWindowBounds;

        _shell.Dispose();
        base.OnClosing(e);
    }
}
