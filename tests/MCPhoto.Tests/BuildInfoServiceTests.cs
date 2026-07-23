using System.IO;
using System.Text;
using MCPhoto.Core.Build;

namespace MCPhoto.Tests;

/// <summary>빌드 정보(bldinfo.ini) 외부 설정 로드 — 부재/정상/부분키/빈값/손상 폴백 + 표기 문자열.</summary>
public class BuildInfoServiceTests
{
    private static string TempIni(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"bldinfo_{Guid.NewGuid():N}.ini");
        File.WriteAllText(path, content, new UTF8Encoding(false)); // BOM 없는 UTF-8
        return path;
    }

    [Fact]
    public void Missing_File_Uses_Default_Version()
    {
        var path = Path.Combine(Path.GetTempPath(), $"bldinfo_absent_{Guid.NewGuid():N}.ini");
        var svc = new IniBuildInfoService(path);
        Assert.Equal("0.0.0", svc.Version);
        Assert.Equal(string.Empty, svc.BuildDate);
        Assert.Equal(string.Empty, svc.Site);
    }

    [Fact]
    public void Valid_Values_Are_Loaded()
    {
        var path = TempIni("[General]\nVersion=1.0.0\nBuildDate=2026-07-23\nSite=Beta\n");
        try
        {
            var svc = new IniBuildInfoService(path);
            Assert.Equal("1.0.0", svc.Version);
            Assert.Equal("2026-07-23", svc.BuildDate);
            Assert.Equal("Beta", svc.Site);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void DisplayText_Joins_Present_Parts_Only()
    {
        var full = TempIni("[General]\nVersion=1.0.0\nBuildDate=2026-07-23\nSite=Beta\n");
        var verOnly = TempIni("[General]\nVersion=2.1.0\n");
        try
        {
            Assert.Equal("v1.0.0  ·  Beta  ·  2026-07-23", new IniBuildInfoService(full).DisplayText);
            Assert.Equal("v2.1.0", new IniBuildInfoService(verOnly).DisplayText); // Site·BuildDate 없으면 생략
        }
        finally { File.Delete(full); File.Delete(verOnly); }
    }

    [Fact]
    public void Empty_Version_Falls_Back_To_Default()
    {
        var path = TempIni("[General]\nVersion=\nSite=Beta\n");
        try
        {
            var svc = new IniBuildInfoService(path);
            Assert.Equal("0.0.0", svc.Version); // 빈 값 → 기본값
            Assert.Equal("Beta", svc.Site);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Corrupt_Content_Does_Not_Throw()
    {
        var path = TempIni("이건 INI가 아님\n===\n@@@@\n");
        try
        {
            var svc = new IniBuildInfoService(path);
            Assert.Equal("0.0.0", svc.Version); // 크래시 없이 기본값
        }
        finally { File.Delete(path); }
    }
}
