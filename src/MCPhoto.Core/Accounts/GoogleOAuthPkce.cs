namespace MCPhoto.Core.Accounts;

using System.Security.Cryptography;
using System.Text;

/// <summary>
/// Google 데스크톱 OAuth의 순수 로직(item1b §3·§7.3): PKCE(codeVerifier/challenge S256)·state·nonce 난수 생성,
/// loopback redirect_uri 조립, authorize URL 조립. System.Net·Process에 의존하지 않아 단위 테스트가 가능하다
/// (부수효과 있는 HttpListener·브라우저 실행은 GoogleSignInService가 담당).
/// </summary>
public static class GoogleOAuthPkce
{
    /// <summary>Google 데스크톱 OAuth authorize 엔드포인트(RFC 8252, OAuth 2.0 for Mobile &amp; Desktop Apps).</summary>
    public const string AuthorizeEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";

    /// <summary>요청 scope(공백 구분). openid+email로 검증된 email 확보(§5.3), profile은 관례상 포함.</summary>
    public const string Scope = "openid email profile";

    /// <summary>
    /// 암호학적 난수를 base64url(패딩 제거)로 인코딩한 토큰 생성. 32바이트 → 43자(PKCE verifier·state·nonce 공용).
    /// RFC 7636 unreserved 문자([A-Za-z0-9-_])만 산출 → codeVerifier 형식(43~128자)·서버 nonce 형식 모두 충족.
    /// </summary>
    public static string CreateRandomToken(int byteLength = 32)
    {
        if (byteLength < 32) byteLength = 32; // 43자 이상 보장(codeVerifier 최소 길이·엔트로피).
        var bytes = RandomNumberGenerator.GetBytes(byteLength);
        return Base64Url(bytes);
    }

    /// <summary>PKCE code_challenge = BASE64URL(SHA256(codeVerifier)) (S256). 항상 43자.</summary>
    public static string CreateChallengeS256(string codeVerifier)
    {
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier));
        return Base64Url(hash);
    }

    /// <summary>
    /// loopback redirect_uri 조립: http://127.0.0.1:{port}/ (127.0.0.1 고정 — 일부 환경의 localhost→::1 회피, §3.3).
    /// 경로는 "/"만, 쿼리·프래그먼트 없음(서버 validateLoopbackRedirectUri 형식과 정합).
    /// </summary>
    public static string BuildLoopbackRedirectUri(int port)
    {
        if (port < 1 || port > 65535)
            throw new ArgumentOutOfRangeException(nameof(port), "포트는 1~65535 범위여야 합니다.");
        return $"http://127.0.0.1:{port}/";
    }

    /// <summary>
    /// Google authorize URL 조립(§3.2 (4)). 모든 파라미터는 <see cref="Uri.EscapeDataString"/>로 인코딩한다.
    /// response_type=code, code_challenge_method=S256, access_type/prompt는 최소 구성(refresh 미사용).
    /// </summary>
    /// <param name="clientId">OAuth 클라이언트 ID(비밀 아님, INI GoogleClientId).</param>
    /// <param name="redirectUri"><see cref="BuildLoopbackRedirectUri"/> 결과.</param>
    /// <param name="codeChallenge"><see cref="CreateChallengeS256"/> 결과.</param>
    /// <param name="state">CSRF 방어용 난수(콜백에서 대조).</param>
    /// <param name="nonce">id_token replay 방어용 난수.</param>
    public static string BuildAuthorizeUrl(
        string clientId, string redirectUri, string codeChallenge, string state, string nonce)
    {
        if (string.IsNullOrWhiteSpace(clientId))
            throw new ArgumentException("clientId가 비어 있습니다.", nameof(clientId));

        var sb = new StringBuilder(AuthorizeEndpoint);
        sb.Append("?client_id=").Append(Uri.EscapeDataString(clientId));
        sb.Append("&redirect_uri=").Append(Uri.EscapeDataString(redirectUri));
        sb.Append("&response_type=code");
        sb.Append("&scope=").Append(Uri.EscapeDataString(Scope));
        sb.Append("&code_challenge=").Append(Uri.EscapeDataString(codeChallenge));
        sb.Append("&code_challenge_method=S256");
        sb.Append("&state=").Append(Uri.EscapeDataString(state));
        sb.Append("&nonce=").Append(Uri.EscapeDataString(nonce));
        return sb.ToString();
    }

    /// <summary>표준 base64url(패딩 '=' 제거, '+'→'-', '/'→'_'). RFC 7636 / RFC 4648 §5.</summary>
    private static string Base64Url(byte[] bytes)
        => Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
