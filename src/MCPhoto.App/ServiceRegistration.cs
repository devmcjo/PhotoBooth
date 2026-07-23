using MCPhoto.App.Services;
using MCPhoto.App.ViewModels;
using MCPhoto.Capture;
using MCPhoto.Core.Accounts;
using MCPhoto.Core.Branding;
using MCPhoto.Core.Capture;
using MCPhoto.Core.Frames;
using MCPhoto.Core.LocalSave;
using MCPhoto.Core.Navigation;
using MCPhoto.Core.Settings;
using MCPhoto.Core.Upload;
using MCPhoto.Firebase;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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

        // it9 C1: 카메라 테스트 모달 오픈(다이얼로그 서비스 — VM이 Window 미참조).
        services.AddSingleton<ICameraTestDialogService, CameraTestDialogService>();

        // Step 2: 설정(INI)
        services.AddSingleton<ISettingsService, IniSettingsService>();

        // Step 3: 캡처 파이프라인(프리뷰)
        services.AddSingleton<ICameraService, OpenCvCameraService>();
        services.AddTransient<PreviewViewModel>();

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

        // Step 8: Firebase 업로드·QR. 키 없으면 IsInitialized=false로 안전(완화 경로).
        // FirebaseClient 구상 인스턴스 하나를 IFirebaseClient·FirebaseClient 둘로 공유.
        // 버킷은 AppSettings.StorageBucket로 주입(빈 값이면 project_id에서 유도 + 경고 로그).
        services.AddSingleton<FirebaseClient>(sp =>
        {
            var bucket = sp.GetRequiredService<ISettingsService>().Current.StorageBucket;
            return new FirebaseClient(
                sp.GetService<ILogger<FirebaseClient>>(),
                bucket: string.IsNullOrWhiteSpace(bucket) ? null : bucket);
        });
        services.AddSingleton<IFirebaseClient>(sp => sp.GetRequiredService<FirebaseClient>());
        services.AddSingleton<IUploadService, UploadService>();
        services.AddSingleton<IQrService, QrService>();

        // Step 10/11 서비스(화면 통합에 필요): 프레임·계정. Firebase 미초기화 시 오프라인 안전.
        services.AddSingleton<IFrameRepository, FrameRepository>();
        services.AddSingleton<IAccountService, AccountService>();

        // it8 A2(정정): 로컬 프레임 저장소 = 실행 폴더 Frame\ (번들과 동일 폴더, 번들+파워캐시+user 공존).
        services.AddSingleton<ILocalFrameStore>(_ =>
            new LocalFrameStore(System.IO.Path.Combine(AppContext.BaseDirectory, "Frame")));

        // Step 9: 세션 컨텍스트 + 프레임 카탈로그 + 화면 VM들
        services.AddSingleton<SessionContext>();
        services.AddSingleton<FrameCatalogService>();
        RegisterScreens(services);
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
    }
}
