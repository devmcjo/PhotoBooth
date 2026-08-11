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

        // it15: 시드 계정 보장 삭제 — ID/PW 계정이 폐지되어 시드 개념 자체가 소멸.
        // 최초 admin은 마이그레이션 스크립트가 부트스트랩한다(설계 §5.5 P1).

        // it10 S3-1: 앱 실행 직후 기본 프레임 백그라운드 prefetch(부수효과인 로컬 캐시가 목적).
        // fire-and-forget + 예외 무시(FrameSelect 진입 시 재시도됨) — 시작 화면 표시 지연 없음.
        _ = PrefetchDefaultFramesAsync();

        var shell = _host.Services.GetRequiredService<MainWindow>();
        shell.Show();
    }

    /// <summary>
    /// it10 S3-1: 앱 시작 시 기본 프레임을 백그라운드로 확보(로컬 캐시). 결과는 무시 — 부수효과인 캐시가 목적.
    /// FrameCatalogService의 SemaphoreSlim 게이트(S3-2)가 FrameSelect 진입과의 경합·중복 다운로드를 막는다.
    /// 실패는 앱 동작에 영향 없음(FrameSelect 진입 시 재시도) → Warning 로그만.
    /// </summary>
    private async Task PrefetchDefaultFramesAsync()
    {
        try
        {
            var catalog = _host!.Services.GetService<MCPhoto.App.Services.FrameCatalogService>();
            if (catalog is not null)
                await catalog.GetDefaultFramesAsync();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "기본 프레임 prefetch 실패(FrameSelect 진입 시 재시도)");
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
        // it23 §12.2: 외부 카메라(DSLR) SDK Shutdown 보장 지점.
        // ⚠️ 컨테이너 정리를 로그 종료보다 **먼저** 한다: 싱글턴 해제(NikonExternalCamera → SDK shim)에서
        //    나오는 경고가 Log.CloseAndFlush() 뒤에 발생하면 파일에 한 줄도 남지 않는다.
        //    (벤더 SDK는 Shutdown 미호출 시 드라이버가 불안정해진다는 경고가 있어, 해제 실패를 봐야 한다.)
        // ⚠️ OnExit은 동기 메서드다 — 여기서 async를 기다리지 않는다. 해제는 각 싱글턴의 동기 Dispose가
        //    담당한다(NikonExternalCamera는 IDisposable·IAsyncDisposable을 함께 구현한다 — 컨테이너의
        //    동기 Dispose는 IAsyncDisposable만 가진 싱글턴을 만나면 InvalidOperationException을 던진다).
        //    어댑터 싱글턴은 설정·촬영 화면 진입만으로 생성되지만(그 VM들이 IExternalCamera를 주입받는다)
        //    ExternalCameraEnabled=false면 연결을 시도한 적이 없어 Dispose가 사실상 no-op이다.
        _host?.Dispose();
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}
