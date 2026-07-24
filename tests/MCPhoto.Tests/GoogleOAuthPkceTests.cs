using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using MCPhoto.Core.Accounts;

namespace MCPhoto.Tests;

/// <summary>
/// item1b S5: Google 데스크톱 OAuth 순수 로직(GoogleOAuthPkce) 단위 검증.
/// PKCE(RFC 7636)·loopback redirect_uri·authorize URL이 서버(functions domain/validation.ts) 형식과
/// 정합하는지 확인한다. 실 Google 왕복은 검증하지 않는다(배포+콘솔 설정 후 수동 스모크).
/// </summary>
public class GoogleOAuthPkceTests
{
    // 서버 domain/validation.ts와 동일한 형식 규칙(클라 산출값이 서버 검증을 통과해야 함).
    private static readonly Regex ServerCodeVerifierRe = new("^[A-Za-z0-9\\-._~]{43,128}$");
    private static readonly Regex ServerNonceRe = new("^[A-Za-z0-9\\-._~]{1,256}$");

    [Fact]
    public void CreateRandomToken_Matches_CodeVerifier_Format()
    {
        for (var i = 0; i < 50; i++)
        {
            var token = GoogleOAuthPkce.CreateRandomToken();
            // RFC 7636: 43~128자, unreserved 문자만. 32바이트→base64url=43자.
            Assert.Matches(ServerCodeVerifierRe, token);
            Assert.True(token.Length is >= 43 and <= 128);
        }
    }

    [Fact]
    public void CreateRandomToken_Satisfies_Server_Nonce_Format()
    {
        var nonce = GoogleOAuthPkce.CreateRandomToken();
        Assert.Matches(ServerNonceRe, nonce);
    }

    [Fact]
    public void CreateRandomToken_Is_Base64Url_No_Padding()
    {
        var token = GoogleOAuthPkce.CreateRandomToken();
        Assert.DoesNotContain('=', token);   // 패딩 제거
        Assert.DoesNotContain('+', token);   // base64url
        Assert.DoesNotContain('/', token);
    }

    [Fact]
    public void CreateRandomToken_Produces_Distinct_Values()
    {
        var a = GoogleOAuthPkce.CreateRandomToken();
        var b = GoogleOAuthPkce.CreateRandomToken();
        Assert.NotEqual(a, b); // 매 호출 난수(state·nonce·verifier가 서로 달라야 함)
    }

    [Fact]
    public void CreateRandomToken_Rejects_Too_Small_ByteLength_By_Clamping()
    {
        // 32 미만을 요청해도 43자 이상(엔트로피·codeVerifier 최소 길이) 보장.
        var token = GoogleOAuthPkce.CreateRandomToken(byteLength: 8);
        Assert.True(token.Length >= 43);
        Assert.Matches(ServerCodeVerifierRe, token);
    }

    [Fact]
    public void CreateChallengeS256_Is_43Char_Base64Url()
    {
        var verifier = GoogleOAuthPkce.CreateRandomToken();
        var challenge = GoogleOAuthPkce.CreateChallengeS256(verifier);
        // SHA256(32바이트)→base64url=43자, 패딩 없음.
        Assert.Equal(43, challenge.Length);
        Assert.DoesNotContain('=', challenge);
        Assert.DoesNotContain('+', challenge);
        Assert.DoesNotContain('/', challenge);
    }

    [Fact]
    public void CreateChallengeS256_Is_Deterministic_For_Same_Verifier()
    {
        const string verifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk"; // RFC 7636 예시류
        var c1 = GoogleOAuthPkce.CreateChallengeS256(verifier);
        var c2 = GoogleOAuthPkce.CreateChallengeS256(verifier);
        Assert.Equal(c1, c2); // 같은 verifier면 같은 challenge(S256 결정성)
    }

    [Fact]
    public void CreateChallengeS256_RFC7636_KnownVector()
    {
        // RFC 7636 Appendix B 검증 벡터: verifier → challenge.
        const string verifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";
        const string expected = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM";
        Assert.Equal(expected, GoogleOAuthPkce.CreateChallengeS256(verifier));
    }

    [Fact]
    public void BuildLoopbackRedirectUri_Matches_Server_Loopback_Rules()
    {
        var uri = GoogleOAuthPkce.BuildLoopbackRedirectUri(54321);
        Assert.Equal("http://127.0.0.1:54321/", uri);

        // 서버 validateLoopbackRedirectUri 규칙 재현: http·127.0.0.1·경로 "/"·쿼리/프래그먼트 없음.
        var parsed = new Uri(uri);
        Assert.Equal("http", parsed.Scheme);
        Assert.Equal("127.0.0.1", parsed.Host);
        Assert.Equal("/", parsed.AbsolutePath);
        Assert.Equal(string.Empty, parsed.Query);
        Assert.Equal(string.Empty, parsed.Fragment);
        Assert.Equal(54321, parsed.Port);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65536)]
    public void BuildLoopbackRedirectUri_Rejects_Invalid_Port(int port)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => GoogleOAuthPkce.BuildLoopbackRedirectUri(port));
    }

    [Fact]
    public void BuildAuthorizeUrl_Contains_Required_Params_Correctly_Encoded()
    {
        const string clientId = "123-abc.apps.googleusercontent.com";
        var redirect = GoogleOAuthPkce.BuildLoopbackRedirectUri(50000);
        const string challenge = "test-challenge";
        const string state = "state-xyz";
        const string nonce = "nonce-xyz";

        var url = GoogleOAuthPkce.BuildAuthorizeUrl(clientId, redirect, challenge, state, nonce);

        Assert.StartsWith(GoogleOAuthPkce.AuthorizeEndpoint + "?", url);

        var query = ParseQuery(url);
        Assert.Equal(clientId, query["client_id"]);
        Assert.Equal(redirect, query["redirect_uri"]);       // 디코딩 시 원 loopback 주소 복원
        Assert.Equal("code", query["response_type"]);
        Assert.Equal(GoogleOAuthPkce.Scope, query["scope"]);  // "openid email profile"
        Assert.Equal(challenge, query["code_challenge"]);
        Assert.Equal("S256", query["code_challenge_method"]);
        Assert.Equal(state, query["state"]);
        Assert.Equal(nonce, query["nonce"]);
    }

    /// <summary>쿼리스트링을 key→디코딩된 value로 파싱(System.Web 비의존).</summary>
    private static Dictionary<string, string> ParseQuery(string url)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var q = new Uri(url).Query.TrimStart('?');
        foreach (var pair in q.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            if (eq < 0)
            {
                result[Uri.UnescapeDataString(pair)] = string.Empty;
                continue;
            }
            var key = Uri.UnescapeDataString(pair[..eq]);
            var val = Uri.UnescapeDataString(pair[(eq + 1)..].Replace('+', ' '));
            result[key] = val;
        }
        return result;
    }

    [Fact]
    public void BuildAuthorizeUrl_Escapes_RedirectUri_And_Scope()
    {
        var redirect = GoogleOAuthPkce.BuildLoopbackRedirectUri(50000);
        var url = GoogleOAuthPkce.BuildAuthorizeUrl("cid", redirect, "ch", "st", "no");
        // 원문에는 escape된 형태로 들어가야 함(공백·슬래시가 raw로 노출되지 않음).
        Assert.Contains("scope=openid%20email%20profile", url);
        Assert.Contains("redirect_uri=http%3A%2F%2F127.0.0.1%3A50000%2F", url);
    }

    [Fact]
    public void BuildAuthorizeUrl_Throws_On_Empty_ClientId()
    {
        Assert.Throws<ArgumentException>(() =>
            GoogleOAuthPkce.BuildAuthorizeUrl("", "http://127.0.0.1:1/", "ch", "st", "no"));
    }
}
