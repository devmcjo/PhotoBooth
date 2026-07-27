using System;
using System.IO;
using MCPhoto.App.Services;
using MCPhoto.Core.Settings;

namespace MCPhoto.Tests;

/// <summary>
/// 백엔드 게이트 키 보호(DPAPI) + ini 약어 키('Ct') 저장/복원 검증.
/// 진짜 보안은 서버측(폐기가능키+JWT+역할) — 여기선 저장 시 평문·구키명 노출 회피(방어 심화)만 검증.
/// </summary>
public class SecretProtectorTests
{
    // ── DpapiSecretProtector (Windows DPAPI, CurrentUser) ──

    [Fact]
    public void Dpapi_RoundTrip_Recovers_Original()
    {
        var p = new DpapiSecretProtector();
        var enc = p.Protect("d7e1914d-secret-value");
        Assert.StartsWith("dp:", enc);
        Assert.DoesNotContain("d7e1914d-secret-value", enc); // 실제 암호화 — 평문 미노출
        Assert.Equal("d7e1914d-secret-value", p.Unprotect(enc));
    }

    [Fact]
    public void Dpapi_Empty_PassesThrough()
    {
        var p = new DpapiSecretProtector();
        Assert.Equal(string.Empty, p.Protect(string.Empty));
        Assert.Equal(string.Empty, p.Unprotect(string.Empty));
    }

    [Fact]
    public void Dpapi_Plaintext_Without_Prefix_Returned_AsIs()
    {
        // publish 주입/수기 편집(접두어 없는 평문) → 그대로(첫 저장 시 재암호화됨).
        var p = new DpapiSecretProtector();
        Assert.Equal("plainkey", p.Unprotect("plainkey"));
    }

    [Fact]
    public void Dpapi_Corrupt_Prefixed_Returns_Empty()
    {
        // dp: 접두어인데 복호화 불가(다른 PC·손상) → 빈 값(키 없음 취급, 크래시 금지).
        var p = new DpapiSecretProtector();
        Assert.Equal(string.Empty, p.Unprotect("dp:!!!not-valid!!!"));
    }

    [Fact]
    public void NullProtector_PassesThrough()
    {
        var p = new NullSecretProtector();
        Assert.Equal("x", p.Protect("x"));
        Assert.Equal("x", p.Unprotect("x"));
    }

    // ── IniSettingsService: 약어 키 'Ct' + 보호 적용 + 구 평문 키 폴백 ──

    /// <summary>가시적·가역 마커 보호(테스트용). 실제 암호화 아님 — 키명/보호 적용만 검증.</summary>
    private sealed class MarkerProtector : ISecretProtector
    {
        public string Protect(string p) => p.Length == 0 ? p : "ENC(" + p + ")";
        public string Unprotect(string s) =>
            s.StartsWith("ENC(", StringComparison.Ordinal) && s.EndsWith(")", StringComparison.Ordinal)
                ? s[4..^1] : s;
    }

    [Fact]
    public void Ini_BackendApiKey_Stored_Under_Ct_And_Protected()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ct_{Guid.NewGuid():N}.ini");
        try
        {
            var svc = new IniSettingsService(iniPath: path, protector: new MarkerProtector());
            var s = svc.Load();
            s.BackendApiKey = "mykey123";
            Assert.True(svc.Save());

            var raw = File.ReadAllText(path);
            Assert.Contains("Ct=ENC(mykey123)", raw);    // 약어 키 + 보호값으로 저장
            Assert.DoesNotContain("BackendApiKey=", raw); // 구 키명 미노출

            var s2 = new IniSettingsService(iniPath: path, protector: new MarkerProtector()).Load();
            Assert.Equal("mykey123", s2.BackendApiKey);   // 복원
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Ini_Reads_Legacy_Plaintext_BackendApiKey_Key()
    {
        // 구 평문 'BackendApiKey=' 키를 폴백으로 읽어 이관.
        var path = Path.Combine(Path.GetTempPath(), $"ctlegacy_{Guid.NewGuid():N}.ini");
        try
        {
            File.WriteAllText(path, "[MCPhoto]\nBackendApiKey=legacyplain\n");
            var s = new IniSettingsService(iniPath: path, protector: new MarkerProtector()).Load();
            Assert.Equal("legacyplain", s.BackendApiKey);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
