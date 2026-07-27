using MCPhoto.App;
using MCPhoto.Core.Accounts;
using MCPhoto.Core.Frames;
using MCPhoto.Core.Settings;
using MCPhoto.Core.Upload;
using MCPhoto.Firebase;
using MCPhoto.Http;
using Microsoft.Extensions.DependencyInjection;

namespace MCPhoto.Tests.Http;

/// <summary>
/// P3: DI feature flag(AppSettings.UseBackend) 분기 검증. 실제 ServiceRegistration.RegisterBackendOrFirebase를 호출.
/// 안전 불변식: 기본 OFF면 현행 Firebase 구현, ON이면 Http* 구현으로 해석되어야 한다.
/// </summary>
public class BackendDiFlagTests
{
    /// <summary>고정 AppSettings를 돌려주는 테스트용 설정 서비스.</summary>
    private sealed class StubSettingsService : ISettingsService
    {
        private readonly AppSettings _settings;
        public StubSettingsService(AppSettings settings) => _settings = settings;
        public AppSettings Current => _settings;
        public AppSettings Load() => _settings;
        public bool Save() => true;
    }

    private static ServiceProvider Build(AppSettings settings)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ISettingsService>(new StubSettingsService(settings));
        ServiceRegistration.RegisterBackendOrFirebase(services);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Default_Resolves_Http_Implementations()
    {
        // 기본 UseBackend=true + 운영 BaseUrl 내장 → 기본이 백엔드(Http*) 경로. (키 폐기 후 전용)
        // Firebase 폴백은 URL을 명시적으로 비웠을 때만(아래 On_But_Empty_Url 테스트가 커버).
        var settings = new AppSettings();
        settings.Clamp();
        Assert.True(settings.UseBackend);

        using var sp = Build(settings);

        Assert.IsType<HttpFirebaseClient>(sp.GetRequiredService<IFirebaseClient>());
        Assert.IsType<HttpFrameRepository>(sp.GetRequiredService<IFrameRepository>());
        Assert.IsType<HttpAccountService>(sp.GetRequiredService<IAccountService>());
    }

    [Fact]
    public void On_With_BaseUrl_Resolves_Http_Implementations()
    {
        var settings = new AppSettings
        {
            UseBackend = true,
            BackendBaseUrl = "https://backend.test/api",
            BackendApiKey = "key",
        };
        settings.Clamp(); // 유효 URL → UseBackend 유지, 슬래시 보정
        Assert.True(settings.UseBackend);

        using var sp = Build(settings);

        Assert.IsType<HttpFirebaseClient>(sp.GetRequiredService<IFirebaseClient>());
        Assert.IsType<HttpFrameRepository>(sp.GetRequiredService<IFrameRepository>());
        Assert.IsType<HttpAccountService>(sp.GetRequiredService<IAccountService>());
    }

    [Fact]
    public void On_But_Empty_Url_Falls_Back_To_Firebase()
    {
        // 안전 불변식: UseBackend=true여도 base URL이 비면 Clamp가 off로 되돌려 현행 경로 유지.
        var settings = new AppSettings { UseBackend = true, BackendBaseUrl = "   " };
        settings.Clamp();
        Assert.False(settings.UseBackend);

        using var sp = Build(settings);
        Assert.IsType<FirebaseClient>(sp.GetRequiredService<IFirebaseClient>());
        Assert.IsType<AccountService>(sp.GetRequiredService<IAccountService>());
    }

    [Fact]
    public void Backend_Singletons_Are_Registered_Regardless_Of_Flag()
    {
        var settings = new AppSettings();
        settings.Clamp();
        using var sp = Build(settings);

        // IHttpClientFactory·IBackendSession은 항상 등록(무해). Http 경로가 이들을 공유.
        Assert.NotNull(sp.GetService<System.Net.Http.IHttpClientFactory>());
        Assert.NotNull(sp.GetService<MCPhoto.Http.Session.IBackendSession>());
    }
}
