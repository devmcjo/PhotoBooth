using System.IO;
using MCPhoto.Core.Devices;
using MCPhoto.Core.Settings;
using MCPhoto.Devices.Nikon;
using Microsoft.Extensions.DependencyInjection;

namespace MCPhoto.Tests;

/// <summary>
/// it23 Step 6: DI 배선 교체 검증(설계 §3.5).
/// <para>
/// 개별 클래스가 아니라 <b>앱과 같은 형태로 조립한 컨테이너</b>를 대상으로 한다 — 배선 결함
/// ("싱글턴을 아무도 해제하지 않는다", "동기 Dispose가 예외를 던진다")은 조립해봐야 재현된다.
/// </para>
/// </summary>
public class ExternalCameraRegistrationTests
{
    /// <summary>ServiceRegistration §3.5와 <b>같은 형태</b>의 외부 카메라 등록만 조립한다.</summary>
    private static ServiceProvider BuildContainer(string iniPath)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ISettingsService>(_ =>
        {
            var svc = new IniSettingsService(iniPath: iniPath);
            svc.Load();
            return svc;
        });
        services.AddSingleton<INikonSdkShim, MissingNikonSdkShim>();
        services.AddSingleton<IExternalCamera>(sp => new NikonExternalCamera(
            sp.GetRequiredService<INikonSdkShim>(),
            sp.GetRequiredService<ISettingsService>(),
            logger: null));
        return services.BuildServiceProvider();
    }

    private static string TempIni() => Path.Combine(Path.GetTempPath(), $"mcphoto_extreg_{Guid.NewGuid():N}.ini");

    [Fact]
    public void Di_Resolves_Nikon_Adapter_With_Missing_Shim()
    {
        using var provider = BuildContainer(TempIni());

        var cam = provider.GetRequiredService<IExternalCamera>();

        Assert.IsType<NikonExternalCamera>(cam);
        Assert.IsType<MissingNikonSdkShim>(provider.GetRequiredService<INikonSdkShim>());
        Assert.False(cam.IsAvailable);   // 등록만으로는 아무것도 열리지 않는다
    }

    [Fact]
    public void Adapter_Is_Singleton()
    {
        // 물리 장치 1대 + SDK 모듈 수명(Shutdown) 때문에 인스턴스가 하나여야 한다.
        using var provider = BuildContainer(TempIni());

        Assert.Same(provider.GetRequiredService<IExternalCamera>(), provider.GetRequiredService<IExternalCamera>());
    }

    [Fact]
    public void Resolving_Adapter_Does_Not_Touch_The_Device()
    {
        // 해석 시점에 파일 I/O·모듈 로드가 일어나면 앱 시작이 장치 상태에 묶인다(설계 Step 5 trigger).
        using var provider = BuildContainer(TempIni());

        var cam = provider.GetRequiredService<IExternalCamera>();

        Assert.Null(cam.ModelName);
        Assert.Null(cam.UnavailableReason);   // 아직 판정한 것이 없다(Connect 전)
    }

    /// <summary>
    /// ★ 종료 경로 함정 회귀 잠금: <c>App.OnExit</c>은 동기 메서드라 컨테이너 정리도 동기
    /// (<c>ServiceProvider.Dispose()</c>)다. 어댑터·shim이 <b>IAsyncDisposable만</b> 구현하면
    /// 이 호출이 InvalidOperationException을 던져 매 종료마다 예외가 난다.
    /// </summary>
    [Fact]
    public void Synchronous_Container_Dispose_Does_Not_Throw()
    {
        var provider = BuildContainer(TempIni());
        _ = provider.GetRequiredService<IExternalCamera>();   // 싱글턴을 실제로 생성(해제 대상이 된다)

        var ex = Record.Exception(() => provider.Dispose());

        Assert.Null(ex);
    }

    [Fact]
    public async Task Enabled_Setting_Does_Not_Gate_Registration()
    {
        // 게이트는 등록이 아니라 소비 지점이다 — 설정이 off여도 어댑터는 정상 해석돼야 한다
        // (그래야 설정 토글이 앱 재시작 없이 다음 세션부터 반영된다).
        var ini = TempIni();
        try
        {
            File.WriteAllText(ini, "[MCPhoto]\nExternalCameraEnabled=false\n");
            using var provider = BuildContainer(ini);

            var cam = provider.GetRequiredService<IExternalCamera>();
            Assert.NotNull(cam);
            // 연결은 SDK 부재로 강등되지만 예외는 없다.
            Assert.False(await cam.ConnectAsync());
        }
        finally { if (File.Exists(ini)) File.Delete(ini); }
    }
}
