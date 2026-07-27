using System;
using System.IO;
using MCPhoto.Core.Settings;

namespace MCPhoto.Tests;

/// <summary>
/// 백엔드 게이트 키 exe 내장 기본값 + ini 오버라이드 동작.
/// 키는 exe 내장(publish -p)이 기본이며, ini엔 내장값을 되쓰지 않는다(평문 유출 방지). ini 오버라이드는 우선.
/// </summary>
public class BackendApiKeyEmbedTests
{
    private static string TempIni() => Path.Combine(Path.GetTempPath(), $"emb_{Guid.NewGuid():N}.ini");

    [Fact]
    public void EmbeddedDefault_Used_When_Ini_Has_No_Key()
    {
        var path = TempIni();
        try
        {
            var s = new IniSettingsService(iniPath: path, embeddedApiKeyDefault: "embedded-key").Load();
            Assert.Equal("embedded-key", s.BackendApiKey); // ini 없음 → 내장 기본값
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Ini_Override_Wins_Over_Embedded()
    {
        var path = TempIni();
        try
        {
            File.WriteAllText(path, "[MCPhoto]\nBackendApiKey=override-key\n");
            var s = new IniSettingsService(iniPath: path, embeddedApiKeyDefault: "embedded-key").Load();
            Assert.Equal("override-key", s.BackendApiKey);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Embedded_Key_Not_Written_Back_To_Ini()
    {
        // 내장 기본값과 동일한 in-memory 값은 저장 시 ini에 쓰지 않는다(평문 유출 방지).
        var path = TempIni();
        try
        {
            var svc = new IniSettingsService(iniPath: path, embeddedApiKeyDefault: "embedded-key");
            svc.Load(); // BackendApiKey = "embedded-key"(내장)
            Assert.True(svc.Save());
            var raw = File.ReadAllText(path);
            Assert.DoesNotContain("embedded-key", raw);   // ini에 내장 키 미노출
            Assert.DoesNotContain("BackendApiKey=", raw);  // 키 라인 자체가 없음
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Genuine_Override_Persisted_To_Ini()
    {
        // 내장값과 '다른' 명시 오버라이드는 저장 시 ini에 보존.
        var path = TempIni();
        try
        {
            var svc = new IniSettingsService(iniPath: path, embeddedApiKeyDefault: "embedded-key");
            var s = svc.Load();
            s.BackendApiKey = "custom-override";
            Assert.True(svc.Save());
            var reloaded = new IniSettingsService(iniPath: path, embeddedApiKeyDefault: "embedded-key").Load();
            Assert.Equal("custom-override", reloaded.BackendApiKey);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
