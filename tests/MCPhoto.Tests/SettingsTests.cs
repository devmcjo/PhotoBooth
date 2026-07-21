using System.IO;
using MCPhoto.Core.Settings;

namespace MCPhoto.Tests;

/// <summary>WBS Step 2: AppSettings 기본값·라운드트립·범위 클램프·손상 폴백 검증.</summary>
public class SettingsTests : IDisposable
{
    private readonly string _tempPath;

    public SettingsTests()
    {
        _tempPath = Path.Combine(Path.GetTempPath(), $"mcphoto_test_{Guid.NewGuid():N}.ini");
    }

    public void Dispose()
    {
        if (File.Exists(_tempPath)) File.Delete(_tempPath);
    }

    [Fact]
    public void Defaults_When_File_Missing()
    {
        var svc = new IniSettingsService(iniPath: _tempPath);
        var s = svc.Load();

        Assert.Equal(6, s.CutCount);
        Assert.Equal(6, s.CountdownSec);
        Assert.True(s.MirrorMode);
        Assert.False(s.FlashMode);
        Assert.Equal(OutputFormat.Jpg, s.OutputFormat);
        Assert.Equal(24, s.RetentionHours);
        Assert.True(s.EnableQrDelivery);
        Assert.False(s.SaveLocalCopy);
        Assert.Equal(DisplayMode.Fullscreen, s.DisplayMode);
        Assert.Equal(0, s.CameraDevice);
    }

    [Fact]
    public void Save_Then_Load_RoundTrips()
    {
        var svc = new IniSettingsService(iniPath: _tempPath);
        var s = svc.Load();
        s.CutCount = 8;
        s.CountdownSec = 10;
        s.MirrorMode = false;
        s.FlashMode = true;
        s.OutputFormat = OutputFormat.Png;
        s.RetentionHours = 48;
        s.EnableQrDelivery = false;
        s.SaveLocalCopy = true;
        s.LocalSavePath = @"D:\photos";
        s.DisplayMode = DisplayMode.Windowed;
        s.CameraDevice = 2;
        s.HostingBaseUrl = "https://mcphoto.web.app";
        s.StorageBucket = "mcphoto.firebasestorage.app";
        s.WindowBounds.Left = 100;
        s.WindowBounds.Top = 50;
        s.WindowBounds.Width = 1600;
        s.WindowBounds.Height = 900;
        Assert.True(svc.Save()); // it3: 성공 시 true 반환

        Assert.True(File.Exists(_tempPath));

        var svc2 = new IniSettingsService(iniPath: _tempPath);
        var s2 = svc2.Load();

        Assert.Equal(8, s2.CutCount);
        Assert.Equal(10, s2.CountdownSec);
        Assert.False(s2.MirrorMode);
        Assert.True(s2.FlashMode);
        Assert.Equal(OutputFormat.Png, s2.OutputFormat);
        Assert.Equal(48, s2.RetentionHours);
        Assert.False(s2.EnableQrDelivery);
        Assert.True(s2.SaveLocalCopy);
        Assert.Equal(@"D:\photos", s2.LocalSavePath);
        Assert.Equal(DisplayMode.Windowed, s2.DisplayMode);
        Assert.Equal(2, s2.CameraDevice);
        Assert.Equal("https://mcphoto.web.app", s2.HostingBaseUrl);
        Assert.Equal("mcphoto.firebasestorage.app", s2.StorageBucket);
        Assert.Equal(100, s2.WindowBounds.Left);
        Assert.Equal(1600, s2.WindowBounds.Width);
    }

    [Fact]
    public void Save_Returns_Bool_Not_Void()
    {
        // it3 §3: Save()가 성공 여부를 반환(성공 오인 방지). 정상 임시 경로 → true.
        var svc = new IniSettingsService(iniPath: _tempPath);
        svc.Load();
        bool ok = svc.Save();
        Assert.True(ok);
    }

    [Fact]
    public void Save_Invalid_Primary_Path_Falls_Back_No_Crash()
    {
        // 잘못된 명시 경로(존재하지 않는 드라이브) 주입 → 폴백 체인(실행경로/LocalAppData)으로
        // 저장 성공(true) 하거나 최소한 크래시 없이 bool 반환. 성공 오인 대신 정직한 결과. (it3 §3.2)
        var badPath = @"Z:\nonexistent_drive_mcphoto\MCPhoto.ini";
        var svc = new IniSettingsService(iniPath: badPath);
        svc.Load();
        var ex = Record.Exception(() => svc.Save());
        Assert.Null(ex); // 예외로 크래시하지 않음(폴백 또는 false)
    }

    [Fact]
    public void RetentionHours_Clamped_To_Max()
    {
        var s = new AppSettings { RetentionHours = 100 };
        s.Clamp();
        Assert.Equal(72, s.RetentionHours);
    }

    [Fact]
    public void RetentionHours_Clamped_To_Min()
    {
        var s = new AppSettings { RetentionHours = 0 };
        s.Clamp();
        Assert.Equal(1, s.RetentionHours);
    }

    [Fact]
    public void CutCount_Snapped_To_Allowed()
    {
        var s = new AppSettings { CutCount = 7 };
        s.Clamp();
        Assert.Contains(s.CutCount, AppSettings.AllowedCutCounts);
        Assert.Equal(6, s.CutCount); // 7은 6·8 중 6에 더 가까움
    }

    [Fact]
    public void CountdownSec_Snapped_To_Allowed()
    {
        var s = new AppSettings { CountdownSec = 5 };
        s.Clamp();
        Assert.Contains(s.CountdownSec, AppSettings.AllowedCountdownSecs);
    }

    [Fact]
    public void WindowBounds_Minimum_Enforced()
    {
        var s = new AppSettings();
        s.WindowBounds.Width = 800;
        s.WindowBounds.Height = 600;
        s.Clamp();
        Assert.Equal(1280, s.WindowBounds.Width);
        Assert.Equal(720, s.WindowBounds.Height);
    }

    [Fact]
    public void HostingBaseUrl_Trailing_Slash_Trimmed()
    {
        var s = new AppSettings { HostingBaseUrl = "https://mcphoto.web.app/" };
        s.Clamp();
        Assert.Equal("https://mcphoto.web.app", s.HostingBaseUrl);
    }

    [Fact]
    public void Corrupt_Ini_Falls_Back_To_Defaults_No_Crash()
    {
        File.WriteAllText(_tempPath, "this is not\n[valid ini\n===garbage\nCutCount=notanumber\n");
        var svc = new IniSettingsService(iniPath: _tempPath);
        var s = svc.Load();

        // 손상 라인 무시 + 파싱 실패 키는 기본값
        Assert.Equal(6, s.CutCount);
        Assert.Equal(24, s.RetentionHours);
    }

    [Fact]
    public void Missing_Keys_Use_Defaults()
    {
        // CutCount만 유효, 나머지는 누락
        File.WriteAllText(_tempPath, "[MCPhoto]\nCutCount=10\n");
        var svc = new IniSettingsService(iniPath: _tempPath);
        var s = svc.Load();

        Assert.Equal(10, s.CutCount);
        Assert.Equal(6, s.CountdownSec);   // 누락 → 기본
        Assert.True(s.MirrorMode);         // 누락 → 기본
    }
}
