using System.IO;
using MCPhoto.Core.Devices;
using MCPhoto.Core.Models;
using MCPhoto.Core.Settings;

namespace MCPhoto.Tests;

/// <summary>
/// it23 Step 2·3: 외부 카메라 설정 스키마(모델 Id + 노출 3종 문자열)와 편집 권한 판정.
/// 설계 §14.1 T-S1·T-S2·T-S3·T-U1.
/// </summary>
public class ExternalCameraSettingsTests
{
    private static string TempIni() => Path.Combine(Path.GetTempPath(), $"mcphoto_extcam_{Guid.NewGuid():N}.ini");

    // ── T-S1: Clamp — 모델 Id 보정, 노출 3키 Trim, 빈 값 보존 ──

    [Fact]
    public void Defaults_Are_Off_Default_Model_And_Unspecified_Exposure()
    {
        var s = new AppSettings();

        Assert.False(s.ExternalCameraEnabled);
        Assert.Equal("NikonD5300", s.ExternalCameraModel);
        // 빈 값 = "미지정"(카메라 현재값 유지) — 기본값으로 특정 노출을 강제하지 않는다.
        Assert.Equal(string.Empty, s.ExternalShutterSpeed);
        Assert.Equal(string.Empty, s.ExternalAperture);
        Assert.Equal(string.Empty, s.ExternalIso);
    }

    [Fact]
    public void Clamp_Unknown_Model_Id_Falls_Back_To_Default()
    {
        var s = new AppSettings { ExternalCameraModel = "NikonD5500" };
        s.Clamp();
        Assert.Equal(ExternalCameraModels.Default.Id, s.ExternalCameraModel);
    }

    [Fact]
    public void Clamp_Normalizes_Model_Id_Casing()
    {
        // ini 손입력 대비: 대소문자가 달라도 레지스트리 정본 표기로 통일된다.
        var s = new AppSettings { ExternalCameraModel = " nikond5300 " };
        s.Clamp();
        Assert.Equal("NikonD5300", s.ExternalCameraModel);
    }

    [Fact]
    public void Clamp_Trims_Exposure_Values_But_Keeps_Empty_As_Unspecified()
    {
        var s = new AppSettings
        {
            ExternalShutterSpeed = "  1/125 ",
            ExternalAperture = "\tf/5.6\t",
            ExternalIso = "   ",
        };
        s.Clamp();

        Assert.Equal("1/125", s.ExternalShutterSpeed);
        Assert.Equal("f/5.6", s.ExternalAperture);
        Assert.Equal(string.Empty, s.ExternalIso);   // 공백만 → 미지정
    }

    [Fact]
    public void Clamp_Does_Not_Validate_Exposure_Against_Any_Domain()
    {
        // 허용 목록은 카메라에 물어봐야 안다 — Clamp는 형태만 정리하고 값을 버리지 않는다(§10.3 자유 입력).
        var s = new AppSettings { ExternalShutterSpeed = "1/9999", ExternalIso = "banana" };
        s.Clamp();

        Assert.Equal("1/9999", s.ExternalShutterSpeed);
        Assert.Equal("banana", s.ExternalIso);
    }

    // ── T-S2: INI 라운드트립(신설 4키) ──

    [Fact]
    public void RoundTrip_Through_Ini_Preserves_All_Four_Keys()
    {
        var path = TempIni();
        try
        {
            var svc = new IniSettingsService(iniPath: path);
            var s = svc.Load();
            s.ExternalCameraEnabled = true;
            s.ExternalCameraModel = "NikonD5300";
            s.ExternalShutterSpeed = "1/125";
            s.ExternalAperture = "f/5.6";
            s.ExternalIso = "400";
            Assert.True(svc.Save());

            var r = new IniSettingsService(iniPath: path).Load();
            Assert.True(r.ExternalCameraEnabled);
            Assert.Equal("NikonD5300", r.ExternalCameraModel);
            Assert.Equal("1/125", r.ExternalShutterSpeed);
            Assert.Equal("f/5.6", r.ExternalAperture);
            Assert.Equal("400", r.ExternalIso);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void RoundTrip_Preserves_Empty_Exposure_As_Empty()
    {
        // "미지정"이 저장 왕복 한 번에 특정 값으로 바뀌면 안 된다.
        var path = TempIni();
        try
        {
            var svc = new IniSettingsService(iniPath: path);
            svc.Load();
            Assert.True(svc.Save());

            var r = new IniSettingsService(iniPath: path).Load();
            Assert.Equal(string.Empty, r.ExternalShutterSpeed);
            Assert.Equal(string.Empty, r.ExternalAperture);
            Assert.Equal(string.Empty, r.ExternalIso);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Missing_Keys_In_Existing_Ini_Fall_Back_To_Defaults()
    {
        // 기존 설치 ini에는 신설 키가 없다 — 마이그레이션 없이 기본값으로 열려야 한다.
        var path = TempIni();
        try
        {
            File.WriteAllText(path, "[MCPhoto]\nExternalCameraEnabled=true\nCutCount=8\n");

            var r = new IniSettingsService(iniPath: path).Load();
            Assert.True(r.ExternalCameraEnabled);      // 있던 키는 그대로
            Assert.Equal("NikonD5300", r.ExternalCameraModel);
            Assert.Equal(string.Empty, r.ExternalShutterSpeed);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Corrupt_Model_Id_In_Ini_Is_Clamped_On_Load()
    {
        var path = TempIni();
        try
        {
            File.WriteAllText(path, "[MCPhoto]\nExternalCameraModel=CanonEOS\nExternalIso=  800  \n");

            var r = new IniSettingsService(iniPath: path).Load();
            Assert.Equal("NikonD5300", r.ExternalCameraModel);   // Load가 Clamp를 호출
            Assert.Equal("800", r.ExternalIso);                  // Trim 적용
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    // ── T-S3: Clone(편집 취소 대비 — 누락은 값 유실 회귀) ──

    [Fact]
    public void Clone_Copies_All_Four_New_Fields()
    {
        var s = new AppSettings
        {
            ExternalCameraEnabled = true,
            ExternalCameraModel = "NikonD5300",
            ExternalShutterSpeed = "1/200",
            ExternalAperture = "f/8",
            ExternalIso = "200",
        };

        var c = s.Clone();

        Assert.True(c.ExternalCameraEnabled);
        Assert.Equal("NikonD5300", c.ExternalCameraModel);
        Assert.Equal("1/200", c.ExternalShutterSpeed);
        Assert.Equal("f/8", c.ExternalAperture);
        Assert.Equal("200", c.ExternalIso);
    }

    // ── T-U1: 편집 권한(§8.1 — 명시 열거 5역할 전수) ──

    [Theory]
    [InlineData(UserRole.TempUser, false)]   // TempUser는 로그인해도 장비 구성 편집 불가
    [InlineData(UserRole.User, true)]
    [InlineData(UserRole.AdvancedUser, true)]
    [InlineData(UserRole.Manager, true)]
    [InlineData(UserRole.Admin, true)]
    public void CanConfigureExternalCamera_Enumerates_Roles_Explicitly(UserRole role, bool expected)
        => Assert.Equal(expected, role.CanConfigureExternalCamera());

    [Fact]
    public void CanConfigureExternalCamera_Is_Not_A_Rank_Inequality()
    {
        // 랭크 부등식(rank >= User)으로 구현했다면 역할 추가 시 조용히 따라 움직인다.
        // 전 역할을 열거해 판정과 랭크가 우연히 일치하는 것이 아니라 명시 열거임을 고정한다.
        foreach (var role in Enum.GetValues<UserRole>())
        {
            var byEnumeration = role is UserRole.User or UserRole.AdvancedUser or UserRole.Manager or UserRole.Admin;
            Assert.Equal(byEnumeration, role.CanConfigureExternalCamera());
        }
    }
}
