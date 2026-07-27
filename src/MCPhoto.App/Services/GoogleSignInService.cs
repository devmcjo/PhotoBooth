using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using MCPhoto.Core.Accounts;
using MCPhoto.Core.Settings;
using Microsoft.Extensions.Logging;

namespace MCPhoto.App.Services;

/// <summary>
/// <see cref="IGoogleSignInService"/>의 데스크톱 구현(item1b §7.3): 시스템 기본 브라우저 + loopback HttpListener + PKCE.
///
/// 흐름: PKCE(codeVerifier/challenge)·state·nonce 생성 → 빈 포트 loopback HttpListener 시작
/// (http://127.0.0.1:{port}/) → Process.Start(UseShellExecute)로 authorize URL을 시스템 브라우저에 오픈 →
/// code 수신 시 state 대조 후 리스너 종료 → {code, codeVerifier, redirectUri, nonce} 반환.
///
/// 취소·타임아웃(3분)·오류는 null로 신호하고, 리스너·CTS는 try-finally로 항상 정리한다(포트·핸들 누수 0, §8.5).
/// 토큰·code·verifier·state·nonce는 로그에 남기지 않는다(§8.6).
/// </summary>
public sealed class GoogleSignInService : IGoogleSignInService
{
    /// <summary>loopback 대기 최대 시간(사용자가 브라우저에서 로그인할 여유). 초과 시 취소·정리. (§3.3)</summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(3);

    private readonly ISettingsService _settings;
    private readonly ILogger<GoogleSignInService>? _logger;

    public GoogleSignInService(ISettingsService settings, ILogger<GoogleSignInService>? logger = null)
    {
        _settings = settings;
        _logger = logger;
    }

    public async Task<GoogleAuthCodeResult?> AcquireAuthorizationCodeAsync(CancellationToken ct = default)
    {
        var clientId = _settings.Current.GoogleClientId;
        if (string.IsNullOrWhiteSpace(clientId))
        {
            // 게이트(UseBackend×GoogleClientId)로 버튼이 숨겨지므로 정상 경로에선 도달하지 않으나 방어적 처리.
            _logger?.LogWarning("Google 로그인 취소: GoogleClientId 미설정(SSO opt-out)");
            return null;
        }

        // PKCE·state·nonce 생성(순수 로직은 Core로 분리 — GoogleOAuthPkce).
        var codeVerifier = GoogleOAuthPkce.CreateRandomToken();
        var codeChallenge = GoogleOAuthPkce.CreateChallengeS256(codeVerifier);
        var state = GoogleOAuthPkce.CreateRandomToken();
        var nonce = GoogleOAuthPkce.CreateRandomToken();

        var port = FindFreeLoopbackPort();
        var redirectUri = GoogleOAuthPkce.BuildLoopbackRedirectUri(port);

        // 사용자 취소 + 타임아웃 결합. 두 CTS 모두 확실히 Dispose.
        using var timeoutCts = new CancellationTokenSource(Timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
        var token = linkedCts.Token;

        var listener = new HttpListener();
        listener.Prefixes.Add(redirectUri);
        // 취소·타임아웃 시 GetContextAsync를 즉시 깨우기 위해 listener.Stop을 등록(핸들은 finally에서 Close).
        using var ctReg = token.Register(() =>
        {
            try { listener.Stop(); }
            catch { /* 이미 종료됨 — 무시 */ }
        });

        try
        {
            listener.Start();

            var authorizeUrl = GoogleOAuthPkce.BuildAuthorizeUrl(clientId, redirectUri, codeChallenge, state, nonce);
            if (!TryOpenBrowser(authorizeUrl))
                return null;

            _logger?.LogInformation("Google 로그인: 시스템 브라우저 오픈, loopback 대기(포트 {Port})", port);

            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or InvalidOperationException)
            {
                // listener.Stop()으로 취소·타임아웃되면 여기로 온다(정상 종료 경로).
                _logger?.LogInformation("Google 로그인 대기 종료(취소/타임아웃)");
                return null;
            }

            var query = context.Request.QueryString;
            var receivedState = query["state"];
            var error = query["error"];
            var code = query["code"];

            await WriteBrowserResponseAsync(context, error).ConfigureAwait(false);

            if (!string.IsNullOrEmpty(error))
            {
                // access_denied 등(사용자 동의 거부). 값 자체는 민감하지 않으나 상세는 남기지 않는다.
                _logger?.LogInformation("Google 로그인: 인가 거부/오류 응답");
                return null;
            }
            if (!string.Equals(receivedState, state, StringComparison.Ordinal))
            {
                // state 불일치 = CSRF 의심. code를 신뢰하지 않고 폐기.
                _logger?.LogWarning("Google 로그인 거부: state 불일치");
                return null;
            }
            if (string.IsNullOrEmpty(code))
            {
                _logger?.LogWarning("Google 로그인: code 없음");
                return null;
            }

            return new GoogleAuthCodeResult
            {
                Code = code,
                CodeVerifier = codeVerifier,
                RedirectUri = redirectUri,
                Nonce = nonce,
            };
        }
        catch (Exception ex)
        {
            // 예기치 않은 오류(포트 선점 경합 등)는 null로 신호(VM이 일반 안내). 토큰류는 로그에 없음.
            _logger?.LogWarning(ex, "Google 로그인 loopback 처리 실패");
            return null;
        }
        finally
        {
            // 정리 보장: 포트 점유·핸들 누수 방지(§8.5). Close는 Stop+Dispose를 포함.
            try { listener.Close(); }
            catch { /* 무시 */ }
        }
    }

    /// <summary>
    /// OS가 비어 있는 loopback 포트를 할당하게 한다: TcpListener로 포트 0 바인딩 → 실제 포트 조회 → 해제.
    /// 확보 후 HttpListener 시작까지의 경합 창은 매우 짧다(§7.3).
    /// </summary>
    private static int FindFreeLoopbackPort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        try
        {
            return ((IPEndPoint)probe.LocalEndpoint).Port;
        }
        finally
        {
            probe.Stop();
        }
    }

    /// <summary>시스템 기본 브라우저로 URL 오픈(UseShellExecute=true 필수 — .NET에서 URL 셸 실행). 실패 시 false.</summary>
    private bool TryOpenBrowser(string url)
    {
        try
        {
            using var proc = Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Google 로그인: 시스템 브라우저 실행 실패");
            return false;
        }
    }

    /// <summary>브라우저에 표시할 최소 완료 안내 HTML을 쓰고 응답을 닫는다(§3.3).</summary>
    private static async Task WriteBrowserResponseAsync(HttpListenerContext context, string? error)
    {
        var message = string.IsNullOrEmpty(error)
            ? "로그인이 완료되었습니다. 이 창을 닫고 앱으로 돌아가세요."
            : "로그인이 취소되었습니다. 이 창을 닫고 앱으로 돌아가세요.";
        var html =
            "<!doctype html><html lang=\"ko\"><head><meta charset=\"utf-8\">" +
            "<title>MC포토</title></head>" +
            "<body style=\"font-family:sans-serif;text-align:center;padding:48px;\">" +
            $"<h2>{message}</h2></body></html>";

        try
        {
            var buffer = System.Text.Encoding.UTF8.GetBytes(html);
            context.Response.StatusCode = 200;
            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.ContentLength64 = buffer.Length;
            context.Response.KeepAlive = false; // 응답 후 연결 종료 → 브라우저가 완결로 인식(리셋 방지).
            await context.Response.OutputStream.WriteAsync(buffer).ConfigureAwait(false);
            await context.Response.OutputStream.FlushAsync().ConfigureAwait(false); // 소켓까지 확실히 밀어냄.
        }
        catch
        {
            // 응답 쓰기 실패는 무시(브라우저 창 닫힘 등) — code 수신 자체는 이미 완료.
        }
        finally
        {
            try { context.Response.Close(); }
            catch { /* 무시 */ }
        }

        // 리스너(로컬 서버)를 닫기 전에 브라우저가 응답을 완전히 수신하도록 짧게 대기 → "연결 안됨" 방지.
        try { await Task.Delay(400).ConfigureAwait(false); }
        catch { /* 무시 */ }
    }
}
