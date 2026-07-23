using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;

namespace MCPhoto.App;

/// <summary>
/// 앱 진입점. DI 컨테이너(Generic Host) 조립 + AppShell 부트스트랩. (architecture §1.3)
/// </summary>
public partial class App : Application
{
    private IHost? _host;

    /// <summary>런타임 갱신 데이터 폴더(쓰기 가능). Program Files 회피. (architecture §7)</summary>
    public static string DataFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "MCPhoto");

    /// <summary>DI 서비스 프로바이더(뷰에서 VM 해결).</summary>
    public IServiceProvider Services => _host!.Services;

    public static new App Current => (App)Application.Current;

    protected override void OnStartup(StartupEventArgs e)
    {
        Directory.CreateDirectory(DataFolder);

        // Serilog 파일 싱크(무인 동작 진단). architecture §1.2
        var logPath = Path.Combine(DataFolder, "logs", "mcphoto-.log");
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(logPath, rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14)
            .CreateLogger();

        // 이전 실행의 세션 임시폴더 잔재 정리(비정상 종료 등). result·logs는 제외. (it6 #3, PRD §10)
        try
        {
            var removed = Core.Capture.SessionWorkspace.CleanupOnStartup(DataFolder);
            if (removed > 0) Log.Information("시작 시 sessions 잔재 {Count}건 정리", removed);
        }
        catch (Exception ex) { Log.Warning(ex, "sessions 잔재 정리 실패(무시)"); }

        _host = Host.CreateDefaultBuilder()
            .ConfigureServices((_, services) =>
            {
                services.AddLogging(builder =>
                {
                    builder.ClearProviders();
                    builder.AddSerilog(dispose: true);
                });
                ServiceRegistration.Register(services);
            })
            .Build();

        // 전역 예외 핸들러(무인 동작: 크래시 대신 Home 복귀). architecture §4.1, R7
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        base.OnStartup(e);

        // it9 C3: 브랜딩(앱 표시명) 로드 후 리소스 주입 — 창 생성 전이어야 DynamicResource가 최신값 반영.
        try
        {
            var branding = _host.Services.GetRequiredService<MCPhoto.Core.Branding.IBrandingService>();
            Resources["Branding.AppName"] = branding.AppName;
            Resources["Branding.Subtitle"] = branding.Subtitle;
        }
        catch (Exception ex) { Log.Warning(ex, "브랜딩 리소스 주입 실패(기본값 유지)"); }

        // 시드 계정 보장(Firebase 초기화 시). 오프라인이면 로그인 시 인메모리 시드 처리.
        _ = EnsureSeedAsync();

        var shell = _host.Services.GetRequiredService<MainWindow>();
        shell.Show();
    }

    private async Task EnsureSeedAsync()
    {
        try
        {
            var accounts = _host!.Services.GetService<MCPhoto.Core.Accounts.IAccountService>();
            if (accounts is not null)
                await accounts.EnsureSeedAccountAsync();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "시드 계정 보장 실패(오프라인 가능)");
        }
    }

    private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error(e.Exception, "UI 스레드 미처리 예외 — Home 복귀 시도");
        e.Handled = true; // 크래시 방지
        TryReturnHome();
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        Log.Error(e.ExceptionObject as Exception, "도메인 미처리 예외 (IsTerminating={Terminating})", e.IsTerminating);
    }

    private void OnUnobservedTaskException(object? sender, System.Threading.Tasks.UnobservedTaskExceptionEventArgs e)
    {
        Log.Error(e.Exception, "관측되지 않은 Task 예외");
        e.SetObserved();
    }

    private void TryReturnHome()
    {
        try
        {
            var shell = _host?.Services.GetService<AppShellViewModel>();
            shell?.ReturnHome("전역 예외 복구");
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Home 복귀 실패");
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.CloseAndFlush();
        _host?.Dispose();
        base.OnExit(e);
    }
}
