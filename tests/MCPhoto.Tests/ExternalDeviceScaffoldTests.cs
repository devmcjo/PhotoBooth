using MCPhoto.Core.Devices;
using MCPhoto.Core.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace MCPhoto.Tests;

/// <summary>
/// item3 스캐폴드: 외부 장치(DSLR·프린터) 추상화 + Null 구현 + 설정 placeholder 검증.
/// 실제 하드웨어 연동은 장비 확정 후 — 현재는 미지원(no-op) 골격만 검증한다.
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

    // ══════════ it24 Step 4: PhotoPrinterName(설계 §9.1 · §12.1 T-S4) ══════════

    [Fact]
    public void PrinterName_Default_Is_Empty_Meaning_Unselected()
    {
        Assert.Equal(string.Empty, new AppSettings().PhotoPrinterName);
    }

    [Fact]
    public void PrinterName_Clamp_Trims_Only()
    {
        // 목록 대조·기본값 보정을 하지 않는다 — 목록 부재를 이유로 값을 지우면 관리자 설정이 파괴된다(P5).
        var s = new AppSettings { PhotoPrinterName = "  Canon SELPHY CP1500  " };
        s.Clamp();
        Assert.Equal("Canon SELPHY CP1500", s.PhotoPrinterName);
    }

    [Fact]
    public void PrinterName_RoundTrips_Through_Ini()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"mcphoto_prname_{Guid.NewGuid():N}.ini");
        try
        {
            var svc = new IniSettingsService(iniPath: path);
            var s = svc.Load();
            s.PhotoPrinterEnabled = true;
            s.PhotoPrinterName = @"\\print01\Photo-Lab";   // 연결 프린터(UNC 표기)도 그대로 보존돼야 한다
            Assert.True(svc.Save());

            var s2 = new IniSettingsService(iniPath: path).Load();
            Assert.True(s2.PhotoPrinterEnabled);
            Assert.Equal(@"\\print01\Photo-Lab", s2.PhotoPrinterName);
        }
        finally
        {
            if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
        }
    }

    /// <summary>
    /// ★ Clone 누락 회귀 잠금: 편집 취소(설정 화면 이탈) 경로가 Clone을 지나므로,
    /// 여기서 빠지면 프린터 선택이 조용히 유실된다(it23 T-S3와 같은 형태의 사고).
    /// </summary>
    [Fact]
    public void PrinterName_Is_Copied_By_Clone()
    {
        var s = new AppSettings { PhotoPrinterName = "Canon SELPHY CP1500" };
        Assert.Equal("Canon SELPHY CP1500", s.Clone().PhotoPrinterName);
    }

    // ── it24 §7.3: 열거 결과 계약 — "확인 불가"와 "0대"는 다른 명제다(R4) ──

    [Fact]
    public void PrinterEnumerationResult_Failed_Is_Distinct_From_Empty_Success()
    {
        var failed = PrinterEnumerationResult.Failed;
        var emptySuccess = new PrinterEnumerationResult(true, Array.Empty<InstalledPrinter>());

        Assert.False(failed.Succeeded);
        Assert.Empty(failed.Printers);
        Assert.True(emptySuccess.Succeeded);
        // 목록만 보면 두 상태는 구분되지 않는다 — Succeeded가 그 구분을 담는 유일한 자리다.
        Assert.NotEqual(failed, emptySuccess);
    }

    // ══════════ it25 §12.3·§12.4: R4 구분·예외 무투과 단정의 **계약 계층 이관** ══════════
    //
    // it24는 이 두 명제를 설정 VM 수준에서 잠갔다(P2/P4 문구·E17 강등). it25에서 프린터 표면이
    // 환원되어 VM 단정은 성립하지 않지만, **단정을 지우면 회귀를 못 잡는다** — 그래서 같은 사실을
    // 열거자 계약 계층에서 잠근다. 인쇄 이터레이션이 재배선할 때 이 계약이 살아 있어야 한다.

    /// <summary>
    /// ★ 열거자 소비자는 "성공·0대"(P2)와 "실패"(P4)를 <b>결과 객체만으로</b> 구분할 수 있어야 한다.
    /// 두 상태의 조치가 다르다(프린터 재연결 vs 인쇄 스풀러 서비스 시작).
    /// </summary>
    [Fact]
    public async Task Enumerator_Contract_Distinguishes_Empty_Success_From_Failure()
    {
        var empty = await new Fakes.FakePrinterEnumerator().EnumerateAsync();
        Assert.True(empty.Succeeded);
        Assert.Empty(empty.Printers);

        var failed = await new Fakes.FakePrinterEnumerator { Succeeded = false }.EnumerateAsync();
        Assert.False(failed.Succeeded);
        Assert.Empty(failed.Printers);
    }

    /// <summary>
    /// ★ 실 구현은 <b>예외를 결과로 바꾼다</b>(계약: "실패는 예외가 아니라 결과값"). 스풀러 중지·권한
    /// 부족 등 어떤 내부 예외여도 P4로 강등되며, 빈 목록(P2)으로 뭉개지지 않는다.
    /// <para>
    /// 실 스풀러가 살아 있는 머신에서는 성공 경로가 관측되므로, 여기서 단정하는 것은 "예외가 호출측으로
    /// 새지 않는다"와 "실패 시 Succeeded=false"의 두 가지다. 후자는 페이크로만 결정적으로 재현된다.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Enumerator_Contract_Never_Lets_Exceptions_Escape()
    {
        // 계약을 어기고 던지는 구현이 있어도, 그 사실이 타입으로 드러나야 한다(소비자가 감쌀 근거).
        var rogue = new Fakes.FakePrinterEnumerator { Throws = new InvalidOperationException("스풀러 붕괴") };
        await Assert.ThrowsAsync<InvalidOperationException>(() => rogue.EnumerateAsync());

        // 실 구현(System.Printing)은 내부 예외를 삼켜 결과 객체로 끝낸다.
        IPrinterEnumerator real = new MCPhoto.App.Services.SystemPrinterEnumerator();
        var result = await real.EnumerateAsync();
        Assert.NotNull(result);
        Assert.NotNull(result.Printers);
    }
}
