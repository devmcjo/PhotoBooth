using MCPhoto.Core.Devices;
using MCPhoto.Core.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace MCPhoto.Tests;

/// <summary>
/// item3 스캐폴드: 외부 장치(DSLR·프린터) 추상화 + Null 구현 + 설정 placeholder 검증.
/// 실제 하드웨어 연동은 장비 확정 후(USER-ACTIONS §C1) — 현재는 미지원(no-op) 골격만 검증한다.
/// </summary>
public class ExternalDeviceScaffoldTests
{
    // ── Null 구현: 항상 미지원(false/null) · 예외 금지 ──

    [Fact]
    public async Task NullExternalCamera_Is_Unavailable_And_NoOp()
    {
        IExternalCamera cam = new NullExternalCamera();

        Assert.False(cam.IsAvailable);
        Assert.False(await cam.ConnectAsync());
        Assert.Null(await cam.CaptureAsync());

        // Disconnect는 미연결이어도 예외 없이 완료(no-op).
        var ex = await Record.ExceptionAsync(() => cam.DisconnectAsync());
        Assert.Null(ex);
    }

    [Fact]
    public async Task NullPhotoPrinter_Is_Unavailable_And_NoOp()
    {
        IPhotoPrinter printer = new NullPhotoPrinter();

        Assert.False(printer.IsAvailable);
        // 미지원이라 인쇄는 false 반환(예외 금지) — 빈 바이트여도 크래시 없음.
        Assert.False(await printer.PrintAsync(Array.Empty<byte>()));
        Assert.False(await printer.PrintAsync(new byte[] { 1, 2, 3 }));
    }

    // ── DI: 인터페이스가 Null 구현으로 해결된다(추후 실 구현 교체 지점) ──

    [Fact]
    public void Di_Resolves_Null_Implementations_For_Interfaces()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IExternalCamera, NullExternalCamera>();
        services.AddSingleton<IPhotoPrinter, NullPhotoPrinter>();
        using var provider = services.BuildServiceProvider();

        var cam = provider.GetRequiredService<IExternalCamera>();
        var printer = provider.GetRequiredService<IPhotoPrinter>();

        Assert.IsType<NullExternalCamera>(cam);
        Assert.IsType<NullPhotoPrinter>(printer);
        // 미지원 상태 재확인(등록된 구현이 실제 no-op).
        Assert.False(cam.IsAvailable);
        Assert.False(printer.IsAvailable);
    }

    // ── AppSettings placeholder: 기본 false · INI 왕복 · Clone 포함 ──

    [Fact]
    public void ExternalDevice_Settings_Default_Off()
    {
        var s = new AppSettings();
        Assert.False(s.ExternalCameraEnabled);
        Assert.False(s.PhotoPrinterEnabled);
    }

    [Fact]
    public void ExternalDevice_Settings_RoundTrip_Through_Ini()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"mcphoto_extdev_{Guid.NewGuid():N}.ini");
        try
        {
            var svc = new IniSettingsService(iniPath: path);
            var s = svc.Load();
            s.ExternalCameraEnabled = true;
            s.PhotoPrinterEnabled = true;
            Assert.True(svc.Save());

            var s2 = new IniSettingsService(iniPath: path).Load();
            Assert.True(s2.ExternalCameraEnabled);
            Assert.True(s2.PhotoPrinterEnabled);
        }
        finally
        {
            if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
        }
    }

    [Fact]
    public void ExternalDevice_Settings_Cloned()
    {
        var s = new AppSettings { ExternalCameraEnabled = true, PhotoPrinterEnabled = true };
        var c = s.Clone();
        Assert.True(c.ExternalCameraEnabled);
        Assert.True(c.PhotoPrinterEnabled);
    }
}
