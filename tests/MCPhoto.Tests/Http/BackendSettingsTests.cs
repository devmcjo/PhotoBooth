using MCPhoto.Core.Settings;

namespace MCPhoto.Tests.Http;

/// <summary>
/// P3: AppSettings 백엔드 필드(Clamp·Clone·NormalizeBackend) 검증.
/// it15 §4.3: UseBackend feature flag가 폐지되어 "강제 off" 케이스가 사라지고
/// 트림·슬래시 보정만 남는다(빈 base URL은 다른 설정을 되돌리지 않는다).
/// </summary>
public class BackendSettingsTests
{
    [Fact]
    public void Default_BaseUrl_Is_Builtin_And_ApiKey_Is_Empty()
    {
        // 백엔드 전용 운영 → 운영 BaseUrl 내장(운영자 ini 입력 불요).
        // BackendApiKey는 소스/기본값에 없음(배포 시 주입).
        var s = new AppSettings();
        Assert.Equal("https://asia-northeast3-mcphoto-955fb.cloudfunctions.net/api", s.BackendBaseUrl);
        Assert.Equal(string.Empty, s.BackendApiKey);
    }

    [Fact]
    public void Clamp_Appends_Trailing_Slash_To_BaseUrl()
    {
        var s = new AppSettings { BackendBaseUrl = "https://x.test/api" };
        s.Clamp();
        Assert.Equal("https://x.test/api/", s.BackendBaseUrl);
    }

    [Fact]
    public void Clamp_Keeps_Existing_Trailing_Slash()
    {
        var s = new AppSettings { BackendBaseUrl = "https://x.test/api/" };
        s.Clamp();
        Assert.Equal("https://x.test/api/", s.BackendBaseUrl);
    }

    [Fact]
    public void Clamp_Trims_Whitespace()
    {
        var s = new AppSettings { BackendBaseUrl = "  https://x.test  ", BackendApiKey = "  k  " };
        s.Clamp();
        Assert.Equal("https://x.test/", s.BackendBaseUrl);
        Assert.Equal("k", s.BackendApiKey);
    }

    [Fact]
    public void Clamp_Leaves_Empty_BaseUrl_Empty_Without_Touching_Other_Keys()
    {
        // it15: 빈 base URL이면 슬래시 보정만 스킵한다 — 다른 설정을 되돌리지 않는다(플래그 없음).
        var s = new AppSettings { BackendBaseUrl = "  ", BackendApiKey = " k ", GoogleClientId = " cid " };
        s.Clamp();
        Assert.Equal(string.Empty, s.BackendBaseUrl);
        Assert.Equal("k", s.BackendApiKey);
        Assert.Equal("cid", s.GoogleClientId);
    }

    [Fact]
    public void Clone_Copies_Backend_Fields()
    {
        var s = new AppSettings { BackendBaseUrl = "https://x.test/", BackendApiKey = "abc" };
        var c = s.Clone();
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
