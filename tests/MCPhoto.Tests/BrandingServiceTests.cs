using System.IO;
using System.Text;
using MCPhoto.Core.Branding;

namespace MCPhoto.Tests;

/// <summary>it9 C3: 브랜딩(앱 이름) 외부 설정 로드 — 부재/빈값/정상/한글/손상 폴백.</summary>
public class BrandingServiceTests
{
    private static string TempIni(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"branding_{Guid.NewGuid():N}.ini");
        File.WriteAllText(path, content, new UTF8Encoding(false)); // BOM 없는 UTF-8(메모장 기본 편차 모사)
        return path;
    }

    [Fact]
    public void Missing_File_Uses_Default()
    {
        var path = Path.Combine(Path.GetTempPath(), $"branding_absent_{Guid.NewGuid():N}.ini");
        var svc = new IniBrandingService(path);
        Assert.Equal("MC Photo", svc.AppName);
        Assert.Equal("self custom photobooth", svc.Subtitle); // 소제목 기본값
    }

    [Fact]
    public void Valid_Subtitle_Is_Loaded()
    {
        var path = TempIni("[Branding]\nAppName=철이네 사진관\nSubtitle=추억을 남기는 순간\n");
        try
        {
            var svc = new IniBrandingService(path);
            Assert.Equal("철이네 사진관", svc.AppName);
            Assert.Equal("추억을 남기는 순간", svc.Subtitle);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Empty_Or_Missing_Subtitle_Falls_Back_To_Default()
    {
        // Subtitle 키 자체가 없을 때
        var path1 = TempIni("[Branding]\nAppName=우리동네 포토부스\n");
        // Subtitle 이 빈 값일 때
        var path2 = TempIni("[Branding]\nSubtitle=\n");
        try
        {
            Assert.Equal("self custom photobooth", new IniBrandingService(path1).Subtitle);
            Assert.Equal("self custom photobooth", new IniBrandingService(path2).Subtitle);
        }
        finally { File.Delete(path1); File.Delete(path2); }
    }

    [Fact]
    public void Valid_AppName_Is_Loaded()
    {
        var path = TempIni("[Branding]\nAppName=우리동네 포토부스\n");
        try
        {
            var svc = new IniBrandingService(path);
            Assert.Equal("우리동네 포토부스", svc.AppName);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Empty_AppName_Falls_Back_To_Default()
    {
        var path = TempIni("[Branding]\nAppName=\n");
        try
        {
            var svc = new IniBrandingService(path);
            Assert.Equal("MC Photo", svc.AppName);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Whitespace_AppName_Falls_Back_To_Default()
    {
        var path = TempIni("[Branding]\nAppName=    \n");
        try
        {
            var svc = new IniBrandingService(path);
            Assert.Equal("MC Photo", svc.AppName);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Korean_Utf8_Value_Preserved()
    {
        var path = TempIni("[Branding]\nAppName=철이네 사진관 📸\n");
        try
        {
            var svc = new IniBrandingService(path);
            Assert.Equal("철이네 사진관 📸", svc.AppName);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Corrupt_Content_Does_Not_Throw()
    {
        var path = TempIni("이건 INI가 아님\n===\n@@@@\n");
        try
        {
            var svc = new IniBrandingService(path);
            Assert.Equal("MC Photo", svc.AppName); // 크래시 없이 기본값
        }
        finally { File.Delete(path); }
    }
}
