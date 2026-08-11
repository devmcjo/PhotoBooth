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
using MCPhoto.Devices.Nikon;
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

        // it9 C3: 앱 이름 브랜딩(branding.ini). 시작 시 1회 로드, 폴백 "MCPhoto".
        services.AddSingleton<IBrandingService, IniBrandingService>();
        // it18: 빌드 정보는 실행 파일 자신에서 읽는다(어셈블리 버전 리소스 + exe 타임스탬프).
        //        외부 파일 bldinfo.ini 폐기 — 리소스 버전과 표기 버전의 이중 관리를 없앴다. 폴백 v0.0.0.
        services.AddSingleton<IBuildInfoService, AssemblyBuildInfoService>();

        // it9 C1: 카메라 테스트 모달 오픈(다이얼로그 서비스 — VM이 Window 미참조).
        services.AddSingleton<ICameraTestDialogService, CameraTestDialogService>();
        // it14/it15: 설정·계정 관리 진입 전 PIN 확인/설정 모달(유일한 진입 게이트, fail-closed).
        services.AddSingleton<IPinPromptDialogService, PinPromptDialogService>();

        // item1b §7.8: Google SSO(시스템 브라우저 + loopback + PKCE). ISettingsService(client_id)·ILogger 주입.
        // VM은 System.Net·Process 미의존(이 서비스에 캡슐화). 백엔드 교환·검증은 IAccountService가 담당.
        services.AddSingleton<IGoogleSignInService, GoogleSignInService>();

        // it11 #14: 진단·상태 모달(관리자 트러블슈팅). 로그 폴더 서비스 + 다이얼로그 서비스.
        services.AddSingleton<ILogFolderService, LogFolderService>();
        // 오픈소스 라이선스 고지(설치 폴더의 licenses/). GPLv3 재배포 의무 이행용. (it22 §5.1 → it23 §C7)
        // it23: 폴더 열기·경로 표시를 폐지하고 **전문을 설정 화면에서 직접 렌더링**한다(열거 + 읽기).
        services.AddSingleton<ILicenseNoticeService, LicenseNoticeService>();
        services.AddSingleton<IDiagnosticsDialogService, DiagnosticsDialogService>();
        // 진단 카드의 개발자 메일 주소 복사(best-effort — 실패해도 예외 없음).
        services.AddSingleton<IClipboardService, ClipboardService>();

        // Step 2: 설정(INI). 백엔드 게이트 키 기본값은 exe 빌드 시 내장(AssemblyMetadata, publish -p) → ini 불요.
        services.AddSingleton<ISettingsService>(sp => new IniSettingsService(
            sp.GetService<ILogger<IniSettingsService>>(),
            embeddedApiKeyDefault: EmbeddedBackendApiKey()));

        // it23 B부: [Test] 섹션 기반 테스트 로그인 모드. 최초 접근 시 1회 판정하고 앱 수명 동안 불변.
        // ⚠️ 릴리스 빌드에도 포함된다(#if DEBUG 격리 없음) — 배포 exe에서 ini 한 줄로 QA를 돌리는 것이 목적이며,
        //    그 대가로 MainWindow에 지울 수 없는 경고 배너가 상시 노출된다(설계 §B1.2·§B9).
        //    토큰은 만들지 않으므로 서버 권한은 0이다(불변식 TM1 — 설계 §B10.2).
        services.AddSingleton<ITestModeService>(sp => new TestModeService(
            sp.GetRequiredService<ISettingsService>(),
            sp.GetService<ILogger<TestModeService>>()));

        // Step 3: 캡처 파이프라인(카메라)
        services.AddSingleton<ICameraService, OpenCvCameraService>();

        // it23: DSLR 수신 스틸을 웹캠과 동일 규칙(거울→슬롯 크롭→축소 상한)으로 정규화하는 디코더.
        // 상태가 없어 Singleton으로 충분하다. 외부 카메라를 쓰지 않는 세션은 이 인스턴스를 호출하지 않는다.
        services.AddSingleton<ExternalStillDecoder>(sp =>
            new ExternalStillDecoder(sp.GetService<ILogger<ExternalStillDecoder>>()));

        // it23: 외부 카메라 = Nikon 어댑터(오케스트레이션) + SDK shim 2계층.
        // shim은 현재 MissingNikonSdkShim(항상 "모듈 없음") — SDK 실물이 도착하면 이 한 줄을
        // NikonSdkShim으로 교체하는 것이 전부다(설계 it23 §15-C4). Core·App 파일은 손대지 않는다.
        services.AddSingleton<INikonSdkShim, MissingNikonSdkShim>();
        // ⚠️ Singleton인 이유: 물리 장치는 1대이고 SDK 모듈 수명(Shutdown 필요)이 앱 수명과 일치해야 한다.
        //    웹캠 ICameraService Singleton 제약(UVC 단일 점유)과 동형이다.
        // ⚠️ 사용 여부 게이트(ExternalCameraEnabled)는 여기가 아니라 소비 지점이다 — 설정은 앱 재시작 없이
        //    바뀌므로 등록 시점에 ini를 읽으면 토글이 다음 세션에 반영되지 않는다.
        services.AddSingleton<IExternalCamera>(sp => new NikonExternalCamera(
            sp.GetRequiredService<INikonSdkShim>(),
            sp.GetRequiredService<ISettingsService>(),   // 모델 Id → 레지스트리 → md3 경로, 저장 노출값 재적용
            sp.GetService<ILogger<NikonExternalCamera>>()));
        // NullExternalCamera는 등록에서 빠지지만 삭제하지 않는다 — 라이선스 문제로
        // MCPhoto.Devices.Nikon을 제외해야 할 때(설계 §13 L1~L3) 이 줄만 되돌리면 앱이 산다.
        services.AddSingleton<IPhotoPrinter, NullPhotoPrinter>();
        // it25 §4.2: 설치 프린터 열거는 **소비자 0 스캐폴드**다(프린터 표면이 "추후 지원 예정"으로 환원됨).
        // 등록을 남기는 이유: 상태가 없어 Singleton이 무해하고 호출이 없으면 스풀러를 접촉하지 않는데,
        // 등록을 지우면 인쇄 이터레이션의 재배선에서 배선 실수가 날 표면이 늘어난다(IPhotoPrinter와 같은 지위).
        // 재개 시점·보존 근거(System.Printing 참조팩 동봉 · 스풀러 중지 강등 · 기본 프린터 null 가드)는
        // IPrinterEnumerator·SystemPrinterEnumerator의 클래스 주석에 있다. 삭제 금지.
        services.AddSingleton<IPrinterEnumerator, SystemPrinterEnumerator>();

        // Step 4: 셸 상태머신·유휴 감시
        services.AddSingleton<IIdleWatchdog, IdleWatchdog>();
        services.AddSingleton<AppShellViewModel>();

        // Step 5: 로컬 저장
        services.AddSingleton<ILocalSaveService, LocalSaveService>();

        // Step 6: 녹화/타임랩스(ffmpeg). CameraService·TimelapseService가 FfmpegRunner 공유.
        // 로거는 필수다: FfmpegRunner 는 변환 실패 시 ffmpeg stderr 꼬리를 LogError 로 남기는데,
        // 종전 등록(new FfmpegRunner())은 로거가 없어 그 로그가 한 번도 남지 않았다. 그래서
        // 홀수 해상도로 인코더가 열리지 않던 실패가 "타임랩스 생성 실패" 한 줄로만 보였고
        // 원인(width not divisible by 2)이 로그에서 사라져 진단이 오래 걸렸다.
        services.AddSingleton<FfmpegRunner>(sp => new FfmpegRunner(logger: sp.GetService<ILogger<FfmpegRunner>>()));
        services.AddSingleton<ITimelapseService, TimelapseService>();

        // Step 7: 합성. 세션 상태는 화면 통합(Step 9)에서 스코프 생성.
        services.AddSingleton<ICompositionService, CompositionService>();

        // Step 8: 업로드·QR + 계정·프레임.
        // it15: 레거시 Admin SDK 직결 경로가 폐지되어 백엔드(HTTPS API) 전용 — feature flag 분기 없음.
        RegisterBackendServices(services);
        services.AddSingleton<IUploadService, UploadService>();
        services.AddSingleton<IQrService, QrService>();

        // it23 §B7.4: 테스트 모드에서만 QR 사용량 조회를 데코레이트한다(마지막 등록이 이긴다).
        // ⚠️ 테스트 모드 OFF면 데코레이터를 **아예 만들지 않고** HTTP 구현을 그대로 돌려준다 —
        //    평시 경로에 테스트 모드 코드가 한 줄도 끼지 않게 하는 것이 요점이다.
        services.AddSingleton<IQrUsageService>(sp =>
        {
            var inner = sp.GetRequiredService<HttpQrUsageService>();
            var testMode = sp.GetRequiredService<ITestModeService>();
            return testMode.IsEnabled
                ? new TestModeQrUsageService(testMode, sp.GetRequiredService<SessionContext>(), inner)
                : inner;
        });

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

        // 진단 화면의 "Web Deploy Date" — GET /health의 deployedAt(유효 API 키 제시 시에만 응답에 포함).
        services.AddSingleton<IServerDeployInfoService>(sp =>
        {
            var s = sp.GetRequiredService<ISettingsService>().Current;
            return new HttpServerDeployInfoService(
                sp.GetRequiredService<IHttpClientFactory>(),
                sp.GetRequiredService<IBackendSession>(),
                s.BackendApiKey,
                configured: !string.IsNullOrWhiteSpace(s.BackendBaseUrl),
                sp.GetService<ILogger<HttpServerDeployInfoService>>());
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
        // 구현 타입으로도 등록하는 이유: it23 §B7.4의 테스트 모드 데코레이터가 이 인스턴스를 inner로 감싼다
        // (데코레이터 등록이 IQrUsageService를 덮어써도 실제 HTTP 구현을 그대로 재사용할 수 있게).
        services.AddSingleton<HttpQrUsageService>(sp =>
        {
            var s = sp.GetRequiredService<ISettingsService>().Current;
            return new HttpQrUsageService(
                sp.GetRequiredService<IHttpClientFactory>(),
                sp.GetRequiredService<IBackendSession>(),
                s.BackendApiKey,
                sp.GetService<ILogger<HttpQrUsageService>>());
        });
        services.AddSingleton<IQrUsageService>(sp => sp.GetRequiredService<HttpQrUsageService>());

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
        // 완료 화면(DoneViewModel)은 폐지 — 세션 완료는 셸의 홈 복귀 + 완료 토스트로 처리한다.
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
