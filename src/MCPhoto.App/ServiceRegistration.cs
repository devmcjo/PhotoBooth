using MCPhoto.App.Services;
using MCPhoto.App.ViewModels;
using MCPhoto.Capture;
using MCPhoto.Core.Accounts;
using MCPhoto.Core.Branding;
using MCPhoto.Core.Build;
using MCPhoto.Core.Capture;
using MCPhoto.Core.Devices;
using MCPhoto.Core.Frames;
using MCPhoto.Core.LocalSave;
using MCPhoto.Core.Navigation;
using MCPhoto.Core.Settings;
using MCPhoto.Core.Upload;
using MCPhoto.Http;
using MCPhoto.Http.Session;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Linq;
using System.Net.Http;
using System.Reflection;

namespace MCPhoto.App;

/// <summary>
/// DI 서비스 등록. Step이 진행되며 서비스·ViewModel·View를 여기에 추가한다. (architecture §1.3)
/// </summary>
internal static class ServiceRegistration
{
    public static void Register(IServiceCollection services)
    {
        // 셸(부트스트랩)
        services.AddSingleton<MainWindow>();

        // it9 C3: 앱 이름 브랜딩(branding.ini). 시작 시 1회 로드, 폴백 "MC Photo".
        services.AddSingleton<IBrandingService, IniBrandingService>();
        // 빌드 정보(bldinfo.ini): 버전·빌드일·사이트. 게스트 하단 상시 표기 + 설정 표기. 폴백 v0.0.0.
        services.AddSingleton<IBuildInfoService, IniBuildInfoService>();

        // it9 C1: 카메라 테스트 모달 오픈(다이얼로그 서비스 — VM이 Window 미참조).
        services.AddSingleton<ICameraTestDialogService, CameraTestDialogService>();
        // it14/it15: 설정·계정 관리 진입 전 PIN 확인/설정 모달(유일한 진입 게이트, fail-closed).
        services.AddSingleton<IPinPromptDialogService, PinPromptDialogService>();

        // item1b §7.8: Google SSO(시스템 브라우저 + loopback + PKCE). ISettingsService(client_id)·ILogger 주입.
        // VM은 System.Net·Process 미의존(이 서비스에 캡슐화). 백엔드 교환·검증은 IAccountService가 담당.
        services.AddSingleton<IGoogleSignInService, GoogleSignInService>();

        // it11 #14: 진단·상태 모달(관리자 트러블슈팅). 로그 폴더 서비스 + 다이얼로그 서비스.
        services.AddSingleton<ILogFolderService, LogFolderService>();
        services.AddSingleton<IDiagnosticsDialogService, DiagnosticsDialogService>();

        // Step 2: 설정(INI). 백엔드 게이트 키 기본값은 exe 빌드 시 내장(AssemblyMetadata, publish -p) → ini 불요.
        services.AddSingleton<ISettingsService>(sp => new IniSettingsService(
            sp.GetService<ILogger<IniSettingsService>>(),
            embeddedApiKeyDefault: EmbeddedBackendApiKey()));

        // Step 3: 캡처 파이프라인(카메라)
        services.AddSingleton<ICameraService, OpenCvCameraService>();

        // item3 스캐폴드: 외부 장치(DSLR·프린터) 추상화. 현재는 미지원(no-op) Null 구현 등록.
        // ⚠️ 실제 하드웨어 연동은 장비 확정 후 이 등록을 실 구현으로 교체한다(SDK/드라이버). USER-ACTIONS §C1.
        services.AddSingleton<IExternalCamera, NullExternalCamera>();
        services.AddSingleton<IPhotoPrinter, NullPhotoPrinter>();

        // Step 4: 셸 상태머신·유휴 감시
        services.AddSingleton<IIdleWatchdog, IdleWatchdog>();
        services.AddSingleton<AppShellViewModel>();

        // Step 5: 로컬 저장
        services.AddSingleton<ILocalSaveService, LocalSaveService>();

        // Step 6: 녹화/타임랩스(ffmpeg). CameraService·TimelapseService가 FfmpegRunner 공유.
        services.AddSingleton<FfmpegRunner>(_ => new FfmpegRunner());
        services.AddSingleton<ITimelapseService, TimelapseService>();

        // Step 7: 합성. 세션 상태는 화면 통합(Step 9)에서 스코프 생성.
        services.AddSingleton<ICompositionService, CompositionService>();

        // Step 8: 업로드·QR + 계정·프레임.
        // it15: 레거시 Admin SDK 직결 경로가 폐지되어 백엔드(HTTPS API) 전용 — feature flag 분기 없음.
        RegisterBackendServices(services);
        services.AddSingleton<IUploadService, UploadService>();
        services.AddSingleton<IQrService, QrService>();

        // it8 A2(정정): 로컬 프레임 저장소 = 실행 폴더 Frame\ (번들과 동일 폴더, 번들+파워캐시+user 공존).
        services.AddSingleton<ILocalFrameStore>(_ =>
            new LocalFrameStore(System.IO.Path.Combine(AppContext.BaseDirectory, "Frame")));

        // Step 9: 세션 컨텍스트 + 프레임 카탈로그 + 화면 VM들
        services.AddSingleton<SessionContext>();
        services.AddSingleton<FrameCatalogService>();
        RegisterScreens(services);
    }

    /// <summary>
    /// 백엔드 HTTPS API 서비스 등록(it15 §7.1: feature flag 분기 폐지 — 백엔드 전용).
    /// IHttpClientFactory("backend") + IBackendSession(JWT 홀더) + Http* 구현.
    /// 팩토리 람다는 첫 해석 시점에 ISettingsService.Current를 읽는다(설정 로드 후라 안전).
    /// </summary>
    internal static void RegisterBackendServices(IServiceCollection services)
    {
        // IHttpClientFactory 명명 클라이언트: base URL·타임아웃을 설정에서 주입.
        services.AddHttpClient(HttpBackendClient.HttpClientName, (sp, client) =>
        {
            var s = sp.GetRequiredService<ISettingsService>().Current;
            if (!string.IsNullOrWhiteSpace(s.BackendBaseUrl))
                client.BaseAddress = new Uri(s.BackendBaseUrl);
            client.Timeout = TimeSpan.FromSeconds(100);
        });
        // JWT 홀더 + 로그아웃 동기화. 홀더를 동기화기가 소유·노출하도록 등록해, "토큰이 존재할 수 있는
        // 모든 시점"에 SessionContext.CurrentUserChanged 구독이 반드시 살아 있게 한다
        // (홀더 없이는 토큰도 없으므로, 별도의 eager 해석 없이 구독 누락이 원천 차단된다).
        services.AddSingleton<BackendSessionSynchronizer>(sp =>
            new BackendSessionSynchronizer(sp.GetRequiredService<SessionContext>(), new BackendSession()));
        services.AddSingleton<IBackendSession>(sp =>
            sp.GetRequiredService<BackendSessionSynchronizer>().Session);

        services.AddSingleton<IFirebaseClient>(sp =>
        {
            var s = sp.GetRequiredService<ISettingsService>().Current;
            return new HttpFirebaseClient(
                sp.GetRequiredService<IHttpClientFactory>(),
                sp.GetRequiredService<IBackendSession>(),
                s.BackendApiKey,
                s.StorageBucket,
                configured: !string.IsNullOrWhiteSpace(s.BackendBaseUrl),
                sp.GetService<ILogger<HttpFirebaseClient>>());
        });

        services.AddSingleton<IFrameRepository>(sp =>
        {
            var s = sp.GetRequiredService<ISettingsService>().Current;
            return new HttpFrameRepository(
                sp.GetRequiredService<IHttpClientFactory>(),
                sp.GetRequiredService<IBackendSession>(),
                s.BackendApiKey,
                sp.GetService<ILogger<HttpFrameRepository>>());
        });

        services.AddSingleton<IAccountService>(sp =>
        {
            var s = sp.GetRequiredService<ISettingsService>().Current;
            return new HttpAccountService(
                sp.GetRequiredService<IHttpClientFactory>(),
                sp.GetRequiredService<IBackendSession>(),
                s.BackendApiKey,
                sp.GetService<ILogger<HttpAccountService>>());
        });

        // it13: TempUser QR 사용량·전역 한도. 서버가 진실원(계정별 강제).
        services.AddSingleton<IQrUsageService>(sp =>
        {
            var s = sp.GetRequiredService<ISettingsService>().Current;
            return new HttpQrUsageService(
                sp.GetRequiredService<IHttpClientFactory>(),
                sp.GetRequiredService<IBackendSession>(),
                s.BackendApiKey,
                sp.GetService<ILogger<HttpQrUsageService>>());
        });

        services.AddSingleton<ITempUserLimitsService>(sp =>
        {
            var s = sp.GetRequiredService<ISettingsService>().Current;
            return new HttpTempUserLimitsService(
                sp.GetRequiredService<IHttpClientFactory>(),
                sp.GetRequiredService<IBackendSession>(),
                s.BackendApiKey,
                sp.GetService<ILogger<HttpTempUserLimitsService>>());
        });
    }

    /// <summary>상태별 화면 ViewModel 등록(Transient — 진입마다 새 인스턴스).</summary>
    private static void RegisterScreens(IServiceCollection services)
    {
        services.AddTransient<HomeViewModel>();
        services.AddTransient<LoginGuestViewModel>();
        services.AddTransient<FrameSelectViewModel>();
        services.AddTransient<GuideViewModel>();
        services.AddTransient<CaptureViewModel>();
        services.AddTransient<CutSelectViewModel>();
        services.AddTransient<ResultViewModel>();
        services.AddTransient<QrPopupViewModel>();
        services.AddTransient<DoneViewModel>();
        services.AddTransient<FrameEditorViewModel>();
        // it15 F2: 편집기의 "기존 프레임 불러오기" 선택 모달 목록 VM.
        // 편집기와 같은 Transient — 진입마다 새 인스턴스라 재진입 잔존 없음.
        services.AddTransient<FramePickerViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<UserMgmtViewModel>();
        services.AddTransient<AccountViewModel>();
        // it11 #14: 진단 VM(모달 진입마다 새 인스턴스 — 최신 카메라·상태 반영).
        services.AddTransient<DiagnosticsViewModel>();
    }

    /// <summary>
    /// publish 시 <c>-p:BackendApiKeyDefault</c>로 exe에 내장된 백엔드 게이트 키(AssemblyMetadata "MCPhoto.BackendApiKey").
    /// 일반 빌드(속성 미지정)에선 속성이 없어 빈 문자열 → ini 오버라이드가 없으면 백엔드 미인증(오프라인 부스로 동작).
    /// </summary>
    private static string EmbeddedBackendApiKey() =>
        Assembly.GetExecutingAssembly()
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "MCPhoto.BackendApiKey")?.Value ?? string.Empty;
}
