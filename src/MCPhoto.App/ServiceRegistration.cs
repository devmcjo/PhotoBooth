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
using MCPhoto.Firebase;
using MCPhoto.Http;
using MCPhoto.Http.Session;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Net.Http;

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

        // it9 C3: 앱 이름 브랜딩(branding.ini). 시작 시 1회 로드, 폴백 "MC포토".
        services.AddSingleton<IBrandingService, IniBrandingService>();
        // 빌드 정보(bldinfo.ini): 버전·빌드일·사이트. 게스트 하단 상시 표기 + 설정 표기. 폴백 v0.0.0.
        services.AddSingleton<IBuildInfoService, IniBuildInfoService>();

        // it9 C1: 카메라 테스트 모달 오픈(다이얼로그 서비스 — VM이 Window 미참조).
        services.AddSingleton<ICameraTestDialogService, CameraTestDialogService>();
        // 보완#1: 설정 진입 전 비밀번호 확인 모달.
        services.AddSingleton<IPasswordPromptDialogService, PasswordPromptDialogService>();

        // item1b §7.8: Google SSO(시스템 브라우저 + loopback + PKCE). ISettingsService(client_id)·ILogger 주입.
        // VM은 System.Net·Process 미의존(이 서비스에 캡슐화). 백엔드 교환·검증은 IAccountService가 담당.
        services.AddSingleton<IGoogleSignInService, GoogleSignInService>();

        // it11 #14: 진단·상태 모달(관리자 트러블슈팅). 로그 폴더 서비스 + 다이얼로그 서비스.
        services.AddSingleton<ILogFolderService, LogFolderService>();
        services.AddSingleton<IDiagnosticsDialogService, DiagnosticsDialogService>();

        // Step 2: 설정(INI)
        services.AddSingleton<ISettingsService, IniSettingsService>();

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
        // 안전 불변식(설계 §8.1): AppSettings.UseBackend가 기본 OFF면 현행 Firebase(Admin) 경로 유지(롤백 가능).
        // ON이면 백엔드 HTTPS API 경유(MCPhoto.Http)로 분기. 분기는 각 인터페이스 팩토리에서 설정을 읽어 결정한다.
        RegisterBackendOrFirebase(services);
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
    /// 업로드·프레임·계정 구현을 feature flag(AppSettings.UseBackend)로 분기 등록(설계 §5.5·§8.1).
    ///
    /// - OFF(기본): 현행 Firebase(Admin SDK) 경로. FirebaseClient 구상 싱글턴 1개를 IFirebaseClient·FirebaseClient로 공유.
    /// - ON: 백엔드 HTTPS API 경유. IHttpClientFactory("backend") + IBackendSession(JWT 홀더) + Http* 구현.
    ///
    /// 각 인터페이스는 팩토리 람다로 등록하고, 첫 해석 시점에 ISettingsService.Current.UseBackend를 읽어
    /// 실제 구현을 고른다(설정이 이미 로드된 뒤라 안전). 빈 URL이면 Clamp가 UseBackend를 off로 되돌린다.
    /// </summary>
    internal static void RegisterBackendOrFirebase(IServiceCollection services)
    {
        // ── 공통(백엔드 ON일 때만 사용되지만 등록은 무해) ──
        // IHttpClientFactory 명명 클라이언트: base URL·타임아웃을 설정에서 주입.
        services.AddHttpClient(HttpBackendClient.HttpClientName, (sp, client) =>
        {
            var s = sp.GetRequiredService<ISettingsService>().Current;
            if (!string.IsNullOrWhiteSpace(s.BackendBaseUrl))
                client.BaseAddress = new Uri(s.BackendBaseUrl);
            client.Timeout = TimeSpan.FromSeconds(100);
        });
        services.AddSingleton<IBackendSession, BackendSession>();

        // ── Firebase(현행) 구상 등록: OFF 경로에서 사용. FirebaseClient 싱글턴 공유. ──
        services.AddSingleton<FirebaseClient>(sp =>
        {
            var bucket = sp.GetRequiredService<ISettingsService>().Current.StorageBucket;
            return new FirebaseClient(
                sp.GetService<ILogger<FirebaseClient>>(),
                bucket: string.IsNullOrWhiteSpace(bucket) ? null : bucket);
        });

        // ── 인터페이스 분기(팩토리) ──
        services.AddSingleton<IFirebaseClient>(sp =>
        {
            var s = sp.GetRequiredService<ISettingsService>().Current;
            if (!s.UseBackend)
                return sp.GetRequiredService<FirebaseClient>();

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
            if (!s.UseBackend)
                return new FrameRepository(
                    sp.GetRequiredService<FirebaseClient>(),
                    sp.GetService<ILogger<FrameRepository>>());

            return new HttpFrameRepository(
                sp.GetRequiredService<IHttpClientFactory>(),
                sp.GetRequiredService<IBackendSession>(),
                s.BackendApiKey,
                sp.GetService<ILogger<HttpFrameRepository>>());
        });

        services.AddSingleton<IAccountService>(sp =>
        {
            var s = sp.GetRequiredService<ISettingsService>().Current;
            if (!s.UseBackend)
                return new AccountService(
                    sp.GetRequiredService<FirebaseClient>(),
                    sp.GetRequiredService<IFrameRepository>(),
                    sp.GetService<ILogger<AccountService>>());

            return new HttpAccountService(
                sp.GetRequiredService<IHttpClientFactory>(),
                sp.GetRequiredService<IBackendSession>(),
                s.BackendApiKey,
                sp.GetService<ILogger<HttpAccountService>>());
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
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<UserMgmtViewModel>();
        services.AddTransient<AccountViewModel>();
        // item1a §9.4: 비밀번호 찾기 화면(백엔드 모드 전용, 진입마다 새 인스턴스로 단계 초기화).
        services.AddTransient<PasswordResetViewModel>();
        // it11 #14: 진단 VM(모달 진입마다 새 인스턴스 — 최신 카메라·상태 반영).
        services.AddTransient<DiagnosticsViewModel>();
    }
}
