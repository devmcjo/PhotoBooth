using System;
using System.Security.Cryptography;
using System.Text;
using MCPhoto.Core.Settings;

namespace MCPhoto.App.Services;

/// <summary>
/// Windows DPAPI(CurrentUser) 기반 보호. 암호문은 **그 PC/사용자에서만 복호화**된다
/// (ini를 다른 PC로 복사해도 못 읽음 → 유출 완화). 저위험 게이트 키의 방어 심화용.
/// 진짜 보안은 서버측(폐기 가능 키 + JWT + 역할)이 담당한다.
///
/// 형식: 보호값은 <c>dp:</c> 접두어 + base64. 접두어 없으면 평문(publish 주입/수기)으로 간주(느슨 복원 → 첫 저장 시 재암호화).
/// </summary>
public sealed class DpapiSecretProtector : ISecretProtector
{
    private const string Prefix = "dp:";
    // 부가 엔트로피(고정) — DPAPI 오용 방지 관용. 비밀은 아니며 형식 태깅 수준.
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("MCPhoto.secret.v1");

    public string Protect(string plaintext)
    {
        if (string.IsNullOrEmpty(plaintext)) return plaintext;
        var data = Encoding.UTF8.GetBytes(plaintext);
        var enc = ProtectedData.Protect(data, Entropy, DataProtectionScope.CurrentUser);
        return Prefix + Convert.ToBase64String(enc);
    }

    public string Unprotect(string stored)
    {
        if (string.IsNullOrEmpty(stored)) return string.Empty;
        // 평문(접두어 없음): publish 주입/수기 편집 → 그대로 사용(첫 저장 시 dp:로 재암호화됨).
        if (!stored.StartsWith(Prefix, StringComparison.Ordinal)) return stored;
        try
        {
            var enc = Convert.FromBase64String(stored.Substring(Prefix.Length));
            var data = ProtectedData.Unprotect(enc, Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(data);
        }
        catch
        {
            // 다른 PC/사용자 등에서 복호화 불가 → 키 없음 취급(빈 값). 크래시 금지.
            return string.Empty;
        }
    }
}
