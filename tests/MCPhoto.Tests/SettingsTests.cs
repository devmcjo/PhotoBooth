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
        Assert.False(s.ShutterSound);   // 기능#7: 기본 off
        Assert.Equal(OutputFormat.Jpg, s.OutputFormat);
        Assert.Equal(24, s.RetentionHours);
        Assert.True(s.EnableQrDelivery);
        Assert.True(s.SaveLocalCopy);                        // it9 후속: 기본 on
        Assert.Equal(DisplayMode.Windowed, s.DisplayMode);   // it9 후속: 개발 기본 창모드
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EnableQrDelivery_Bool_RoundTrips(bool value)
    {
        // QR 토글 저장 → 새 인스턴스 Load 시 그대로 유지(bool 저장/로드 회귀).
        var svc = new IniSettingsService(iniPath: _tempPath);
        var s = svc.Load();
        s.EnableQrDelivery = value;
        Assert.True(svc.Save());

        var svc2 = new IniSettingsService(iniPath: _tempPath);
        Assert.Equal(value, svc2.Load().EnableQrDelivery);
    }

    [Fact]
    public void GoogleClientId_RoundTrips()
    {
        // item1b §7.2: GoogleClientId INI 영속(비밀 아님, 배포별 값).
        var svc = new IniSettingsService(iniPath: _tempPath);
        var s = svc.Load();
        s.GoogleClientId = "999-xyz.apps.googleusercontent.com";
        Assert.True(svc.Save());

        var svc2 = new IniSettingsService(iniPath: _tempPath);
        Assert.Equal("999-xyz.apps.googleusercontent.com", svc2.Load().GoogleClientId);
    }

    [Fact]
    public void SendPhoto_SendTimelapse_RoundTrip()
    {
        // it7 F2: QR 하위 토글 INI 영속. 사진만 켜고 저장 → 로드 시 유지(QR은 하나라도 on이라 유지).
        var svc = new IniSettingsService(iniPath: _tempPath);
        var s = svc.Load();
        s.EnableQrDelivery = true;
        s.SendPhoto = true;
        s.SendTimelapse = false;
        Assert.True(svc.Save());

        var s2 = new IniSettingsService(iniPath: _tempPath).Load();
        Assert.True(s2.EnableQrDelivery);
        Assert.True(s2.SendPhoto);
        Assert.False(s2.SendTimelapse);
    }

    [Fact]
    public void Both_SubToggles_Off_Normalizes_Qr_Off_On_Load()
    {
        // 둘 다 off 저장 → NormalizeQr(Clamp)로 EnableQrDelivery=false 로드.
        var svc = new IniSettingsService(iniPath: _tempPath);
        var s = svc.Load();
        s.EnableQrDelivery = true;
        s.SendPhoto = false;
        s.SendTimelapse = false;
        Assert.True(svc.Save()); // Save가 Clamp→NormalizeQr 적용

        var s2 = new IniSettingsService(iniPath: _tempPath).Load();
        Assert.False(s2.EnableQrDelivery);
    }

    [Fact]
    public void Filter_Toggles_RoundTrip()
    {
        // it8 A6: 필터 개별 on/off INI 영속. 흑백만 끄고 저장→로드 유지.
        var svc = new IniSettingsService(iniPath: _tempPath);
        var s = svc.Load();
        s.FilterGrayscale = false;
        s.FilterBrightness = true;
        s.FilterBeauty = false;
        Assert.True(svc.Save());

        var s2 = new IniSettingsService(iniPath: _tempPath).Load();
        Assert.False(s2.FilterGrayscale);
        Assert.True(s2.FilterBrightness);
        Assert.False(s2.FilterBeauty);
    }

    [Fact]
    public void Filter_Toggles_Default_All_On()
    {
        var s = new IniSettingsService(iniPath: _tempPath).Load();
        Assert.True(s.FilterGrayscale);
        Assert.True(s.FilterBrightness);
        Assert.True(s.FilterBeauty);
    }

    [Fact]
    public void StorageBucket_RoundTrips_New_Convention()
    {
        // it5 §2.3 B6: 신규 규약(*.firebasestorage.app) 버킷명 저장→로드 보존.
        // Blaze+버킷 생성 후 이 값을 넣으면 업로드 경로가 동작(외부 전제).
        var svc = new IniSettingsService(iniPath: _tempPath);
        var s = svc.Load();
        s.StorageBucket = "mcphoto-955fb.firebasestorage.app";
        Assert.True(svc.Save());

        var svc2 = new IniSettingsService(iniPath: _tempPath);
        Assert.Equal("mcphoto-955fb.firebasestorage.app", svc2.Load().StorageBucket);
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

    // ── it11 #13: 재촬영 설정 ──

    [Fact]
    public void Retake_Defaults_Off_Limit_One()
    {
        // 기본: 재촬영 off, 횟수 제한 1.
        var s = new IniSettingsService(iniPath: _tempPath).Load();
        Assert.False(s.RetakeEnabled);
        Assert.Equal(1, s.RetakeLimit);
    }

    [Fact]
    public void RetakeLimit_Clamped_Above_To_Three()
    {
        var s = new AppSettings { RetakeLimit = 5 };
        s.Clamp();
        Assert.Equal(3, s.RetakeLimit); // 5는 허용값(1,2,3) 중 3에 가장 가까움
    }

    [Fact]
    public void RetakeLimit_Clamped_Below_To_One()
    {
        var s = new AppSettings { RetakeLimit = 0 };
        s.Clamp();
        Assert.Equal(1, s.RetakeLimit); // 0은 1로 보정(하한)
    }

    [Fact]
    public void RetakeLimit_Snapped_To_Allowed()
    {
        var s = new AppSettings { RetakeLimit = 2 };
        s.Clamp();
        Assert.Contains(s.RetakeLimit, AppSettings.AllowedRetakeLimits);
        Assert.Equal(2, s.RetakeLimit); // 유효값은 그대로
    }

    [Fact]
    public void Retake_Settings_RoundTrip()
    {
        // 재촬영 on + 제한 3 저장 → 새 인스턴스 로드 시 보존.
        var svc = new IniSettingsService(iniPath: _tempPath);
        var s = svc.Load();
        s.RetakeEnabled = true;
        s.RetakeLimit = 3;
        Assert.True(svc.Save());

        var s2 = new IniSettingsService(iniPath: _tempPath).Load();
        Assert.True(s2.RetakeEnabled);
        Assert.Equal(3, s2.RetakeLimit);
    }

    [Fact]
    public void Retake_Fields_Cloned()
    {
        // 편집 취소 대비 얕은 복제에 재촬영 필드 포함.
        var s = new AppSettings { RetakeEnabled = true, RetakeLimit = 2 };
        var c = s.Clone();
        Assert.True(c.RetakeEnabled);
        Assert.Equal(2, c.RetakeLimit);
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

    /// <summary>
    /// it15 §4.3 T6(가정 A3): 폐지된 UseBackend 키가 남아 있는 기존 배포본 ini를 Load→Save해도
    /// 예외 없이 나머지 값이 보존되고, 저장 시 그 키가 사라진다(IniFile은 모르는 키를 읽지 않는다).
    /// </summary>
    [Fact]
    public void Legacy_UseBackend_Key_Is_Ignored_And_Dropped_On_Save()
    {
        File.WriteAllText(_tempPath,
            "[MCPhoto]\nUseBackend=True\nCutCount=10\nCountdownSec=8\nBackendBaseUrl=https://x.test/api\n");
        var svc = new IniSettingsService(iniPath: _tempPath);

        var s = svc.Load();

        // 레거시 키는 무시되고 나머지 값은 정상 로드된다(예외 없음).
        Assert.Equal(10, s.CutCount);
        Assert.Equal(8, s.CountdownSec);
        Assert.Equal("https://x.test/api/", s.BackendBaseUrl);   // Clamp의 슬래시 보정 유지

        Assert.True(svc.Save());

        var written = File.ReadAllText(_tempPath);
        Assert.DoesNotContain("UseBackend", written);            // 저장 시 자동 제거
        Assert.Contains("CutCount=10", written);
    }

    // ── it17: 촬영 컷 수 자동 모드(sentinel 0). 실제 컷 수 산출은 CutCountPolicy/CaptureSession 담당 ──

    /// <summary>
    /// ★ 최상위 불변식(설계 §12 R-1): 자동 sentinel은 Clamp의 최근접 보정에서 제외된다.
    /// 가드가 없으면 ClosestFrom(0, {6,8,10})이 0을 6으로 덮어써 "자동" 설정이 조용히 소멸한다.
    /// </summary>
    [Fact]
    public void CutCount_Auto_Survives_Clamp()
    {
        var s = new AppSettings { CutCount = CutCountPolicy.AutoCutCount };
        s.Clamp();
        Assert.Equal(CutCountPolicy.AutoCutCount, s.CutCount);
        Assert.DoesNotContain(s.CutCount, AppSettings.AllowedCutCounts); // 허용 집합 밖이지만 보존
    }

    [Fact]
    public void CutCount_Auto_RoundTrips_Through_Ini()
    {
        // Clamp는 로드·저장 양쪽에서 호출된다 → sentinel이 왕복 1회로 소멸하지 않아야 한다.
        var svc = new IniSettingsService(iniPath: _tempPath);
        var s = svc.Load();
        s.CutCount = CutCountPolicy.AutoCutCount;
        Assert.True(svc.Save());

        Assert.Contains("CutCount=0", File.ReadAllText(_tempPath)); // ini에 sentinel 그대로 기록
        Assert.Equal(CutCountPolicy.AutoCutCount, new IniSettingsService(iniPath: _tempPath).Load().CutCount);
    }

    [Fact]
    public void CutCount_Negative_Snaps_To_Allowed()
    {
        // -1은 sentinel이 아니다(설계 §4.1) → 종전 규칙대로 6으로 보정(오타 방어).
        var s = new AppSettings { CutCount = -1 };
        s.Clamp();
        Assert.Equal(6, s.CutCount);
    }

    [Theory]
    [InlineData(6, 6)]
    [InlineData(8, 8)]
    [InlineData(10, 10)]
    [InlineData(7, 6)]   // 기존 보정 동작 유지(VF-19와 동일 경로)
    [InlineData(3, 6)]
    public void CutCount_Legacy_Values_Unchanged(int stored, int expected)
    {
        // 가정 A-4: 기존 ini를 쓰는 운영 PC에서 이번 변경 후 동작이 종전과 100% 동일하다.
        var s = new AppSettings { CutCount = stored };
        s.Clamp();
        Assert.Equal(expected, s.CutCount);
    }
}
