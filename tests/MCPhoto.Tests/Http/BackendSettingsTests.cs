using MCPhoto.Core.Settings;

namespace MCPhoto.Tests.Http;

/// <summary>P3: AppSettings 백엔드 필드(Clamp·Clone·NormalizeBackend) 검증.</summary>
public class BackendSettingsTests
{
    [Fact]
    public void Default_UseBackend_Is_On_With_Builtin_BaseUrl()
    {
        // 키 폐기 후 백엔드 전용 운영 → 기본 ON + 운영 BaseUrl 내장(운영자 ini 입력 불요).
        // BackendApiKey는 소스/기본값에 없음(배포 시 주입).
        var s = new AppSettings();
        Assert.True(s.UseBackend);
        Assert.Equal("https://asia-northeast3-mcphoto-955fb.cloudfunctions.net/api", s.BackendBaseUrl);
        Assert.Equal(string.Empty, s.BackendApiKey);
    }

    [Fact]
    public void Clamp_Forces_Off_When_Url_Empty()
    {
        var s = new AppSettings { UseBackend = true, BackendBaseUrl = "" };
        s.Clamp();
        Assert.False(s.UseBackend);
    }

    [Fact]
    public void Clamp_Appends_Trailing_Slash_To_BaseUrl()
    {
        var s = new AppSettings { UseBackend = true, BackendBaseUrl = "https://x.test/api" };
        s.Clamp();
        Assert.True(s.UseBackend);
        Assert.Equal("https://x.test/api/", s.BackendBaseUrl);
    }

    [Fact]
    public void Clamp_Keeps_Existing_Trailing_Slash()
    {
        var s = new AppSettings { UseBackend = true, BackendBaseUrl = "https://x.test/api/" };
        s.Clamp();
        Assert.Equal("https://x.test/api/", s.BackendBaseUrl);
    }

    [Fact]
    public void Clamp_Trims_Whitespace()
    {
        var s = new AppSettings { UseBackend = true, BackendBaseUrl = "  https://x.test  ", BackendApiKey = "  k  " };
        s.Clamp();
        Assert.Equal("https://x.test/", s.BackendBaseUrl);
        Assert.Equal("k", s.BackendApiKey);
    }

    [Fact]
    public void Clone_Copies_Backend_Fields()
    {
        var s = new AppSettings { UseBackend = true, BackendBaseUrl = "https://x.test/", BackendApiKey = "abc" };
        var c = s.Clone();
        Assert.True(c.UseBackend);
        Assert.Equal("https://x.test/", c.BackendBaseUrl);
        Assert.Equal("abc", c.BackendApiKey);
    }

    // ── item1b: GoogleClientId (§7.2) ──

    [Fact]
    public void Default_GoogleClientId_Is_Builtin_Operating_Client()
    {
        // 운영 프로젝트(mcphoto-955fb) Desktop 클라이언트 ID를 기본값으로 내장 → 운영자 ini 입력 불요.
        // 공개값이라 하드코딩 무해. 다른 프로젝트는 ini의 GoogleClientId로 오버라이드.
        var s = new AppSettings();
        Assert.Equal("712395684881-l66ogdns5ppcc91ojaap4ju9ta3hc6d3.apps.googleusercontent.com", s.GoogleClientId);
    }

    [Fact]
    public void Clamp_Trims_GoogleClientId()
    {
        var s = new AppSettings { GoogleClientId = "  123-abc.apps.googleusercontent.com  " };
        s.Clamp();
        Assert.Equal("123-abc.apps.googleusercontent.com", s.GoogleClientId);
    }

    [Fact]
    public void Clone_Copies_GoogleClientId()
    {
        var s = new AppSettings { GoogleClientId = "cid-xyz" };
        var c = s.Clone();
        Assert.Equal("cid-xyz", c.GoogleClientId);
    }
}
