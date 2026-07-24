using MCPhoto.Core.Settings;

namespace MCPhoto.Tests.Http;

/// <summary>P3: AppSettings 백엔드 필드(Clamp·Clone·NormalizeBackend) 검증.</summary>
public class BackendSettingsTests
{
    [Fact]
    public void Default_UseBackend_Is_Off()
    {
        var s = new AppSettings();
        Assert.False(s.UseBackend);
        Assert.Equal(string.Empty, s.BackendBaseUrl);
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
    public void Default_GoogleClientId_Is_Empty()
    {
        var s = new AppSettings();
        Assert.Equal(string.Empty, s.GoogleClientId); // 기본 SSO opt-out
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
