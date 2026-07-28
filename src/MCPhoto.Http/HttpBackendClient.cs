namespace MCPhoto.Http;

using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using MCPhoto.Http.Dto;
using MCPhoto.Http.Session;
using Microsoft.Extensions.Logging;

/// <summary>
/// 백엔드 HTTP 호출 공통 기반. HttpClient(IHttpClientFactory 명명 클라이언트) 획득, 헤더 조립
/// (API 키·Bearer), 표준 에러 파싱→<see cref="BackendException"/> 변환, JSON 직렬화를 캡슐화한다.
///
/// - 공개(게스트) 엔드포인트: API 키 헤더(X-MCPhoto-Client).
/// - 계정 조작 엔드포인트: 로그인 JWT Bearer(+ 필요 시 API 키).
/// 시크릿/토큰은 절대 로그에 남기지 않는다(설계 §12 · 요구사항).
/// </summary>
public abstract class HttpBackendClient
{
    /// <summary>IHttpClientFactory 명명 클라이언트 이름(ServiceRegistration에서 동일 이름 등록).</summary>
    public const string HttpClientName = "backend";

    /// <summary>배포 API 키 헤더명(서버 API_KEY_HEADER와 정합, functions src/http/auth.ts).</summary>
    public const string ApiKeyHeader = "X-MCPhoto-Client";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IBackendSession _session;
    private readonly string _apiKey;
    private readonly ILogger? _logger;

    protected HttpBackendClient(
        IHttpClientFactory httpClientFactory,
        IBackendSession session,
        string apiKey,
        ILogger? logger)
    {
        _httpClientFactory = httpClientFactory;
        _session = session;
        _apiKey = apiKey ?? string.Empty;
        _logger = logger;
    }

    /// <summary>공유 세션(토큰 홀더).</summary>
    protected IBackendSession Session => _session;

    /// <summary>진단 로그(시크릿/토큰 금지).</summary>
    protected ILogger? Logger => _logger;

    private HttpClient CreateClient() => _httpClientFactory.CreateClient(HttpClientName);

    /// <summary>Bearer 부착 정책(요청 조립). None=미부착, Optional=있으면 부착·없으면 익명 통과, Required=없으면 throw.</summary>
    private enum BearerMode { None, Optional, Required }

    /// <summary>GET 후 JSON 본문을 <typeparamref name="T"/>로 역직렬화. 실패 상태는 <see cref="BackendException"/>.</summary>
    protected async Task<T> GetJsonAsync<T>(string relativeUrl, bool bearer, CancellationToken ct)
    {
        using var request = BuildRequest(HttpMethod.Get, relativeUrl, bearer ? BearerMode.Required : BearerMode.None);
        return await SendAndReadAsync<T>(request, ct).ConfigureAwait(false);
    }

    /// <summary>본문 있는 POST/PATCH/DELETE 후 JSON 본문을 <typeparamref name="T"/>로 역직렬화.</summary>
    protected async Task<T> SendJsonAsync<T>(
        HttpMethod method, string relativeUrl, object? body, bool bearer, CancellationToken ct)
    {
        using var request = BuildRequest(method, relativeUrl, bearer ? BearerMode.Required : BearerMode.None);
        if (body is not null)
            request.Content = JsonContent.Create(body, options: BackendJson.Options);
        return await SendAndReadAsync<T>(request, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 본문 있는 호출에 <b>선택적 Bearer</b>를 부착한다(it13 §5.1 업로드 신원화). 토큰이 있으면 붙이고,
    /// 없으면 게스트로 익명 통과(throw 없음 — <see cref="BearerMode.Required"/>와 다름). 서버 optionalBearer와 대칭.
    /// </summary>
    protected async Task<T> SendJsonOptionalBearerAsync<T>(
        HttpMethod method, string relativeUrl, object? body, CancellationToken ct)
    {
        using var request = BuildRequest(method, relativeUrl, BearerMode.Optional);
        if (body is not null)
            request.Content = JsonContent.Create(body, options: BackendJson.Options);
        return await SendAndReadAsync<T>(request, ct).ConfigureAwait(false);
    }

    /// <summary>본문 없는(204 등) 호출. 실패 상태는 <see cref="BackendException"/>.</summary>
    protected async Task SendNoContentAsync(
        HttpMethod method, string relativeUrl, object? body, bool bearer, CancellationToken ct)
    {
        using var request = BuildRequest(method, relativeUrl, bearer ? BearerMode.Required : BearerMode.None);
        if (body is not null)
            request.Content = JsonContent.Create(body, options: BackendJson.Options);
        using var response = await SendCoreAsync(request, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
    }

    /// <summary>요청 조립: 상대 URL + API 키(항상) + Bearer(모드별). 상대 URL은 BaseAddress에 결합된다.</summary>
    private HttpRequestMessage BuildRequest(HttpMethod method, string relativeUrl, BearerMode bearer)
    {
        var request = new HttpRequestMessage(method, relativeUrl);
        // API 키는 모든 호출에 부착(게스트 엔드포인트 게이트). 계정 엔드포인트는 Bearer가 추가로 필요.
        if (!string.IsNullOrEmpty(_apiKey))
            request.Headers.TryAddWithoutValidation(ApiKeyHeader, _apiKey);

        if (bearer != BearerMode.None)
        {
            var token = _session.Token;
            if (string.IsNullOrEmpty(token))
            {
                // Required: 토큰 없으면 즉시 거부(로그인 필요). Optional: 토큰 없으면 익명 통과(게스트 업로드, it13 §5.1).
                if (bearer == BearerMode.Required)
                    throw new UnauthorizedAccessException("로그인이 필요합니다(토큰 없음).");
            }
            else
            {
                request.Headers.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            }
        }
        return request;
    }

    private async Task<HttpResponseMessage> SendCoreAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var client = CreateClient();
        try
        {
            return await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // 네트워크/타임아웃 = 백엔드 도달 불가. 현행 계약(5xx/네트워크→InvalidOperationException)과 정합.
            _logger?.LogWarning(ex, "백엔드 요청 실패(네트워크/타임아웃): {Method} {Url}", request.Method, request.RequestUri);
            throw new InvalidOperationException("백엔드에 연결할 수 없습니다.", ex);
        }
    }

    private async Task<T> SendAndReadAsync<T>(HttpRequestMessage request, CancellationToken ct)
    {
        using var response = await SendCoreAsync(request, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(response, ct).ConfigureAwait(false);

        var value = await response.Content
            .ReadFromJsonAsync<T>(BackendJson.Options, ct)
            .ConfigureAwait(false);
        if (value is null)
            throw new InvalidOperationException("백엔드 응답 본문이 비어 있습니다.");
        return value;
    }

    /// <summary>2xx가 아니면 표준 에러 봉투를 파싱해 <see cref="BackendException"/>으로 던진다.</summary>
    private async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;

        string code = string.Empty;
        string message = $"백엔드 오류({(int)response.StatusCode}).";
        try
        {
            var envelope = await response.Content
                .ReadFromJsonAsync<ErrorEnvelope>(BackendJson.Options, ct)
                .ConfigureAwait(false);
            if (envelope?.Error is { } err)
            {
                code = err.Code ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(err.Message))
                    message = err.Message!;
            }
        }
        catch
        {
            // 표준 봉투가 아니면 상태코드 기반 기본 메시지 유지(파싱 실패는 무시).
        }

        throw new BackendException(response.StatusCode, code, message);
    }

    /// <summary>
    /// <see cref="BackendException"/>을 현행 UI 계약 예외로 변환(설계 §6.1):
    /// 403→UnauthorizedAccessException, 409→InvalidOperationException(중복), 404→InvalidOperationException,
    /// 400→ArgumentException, 그 외→InvalidOperationException. 401 처리는 호출부가 결정(로그인만 null).
    /// </summary>
    protected static Exception MapToDomainException(BackendException ex) => ex.StatusCode switch
    {
        HttpStatusCode.Forbidden => new UnauthorizedAccessException(ex.Message),
        HttpStatusCode.Conflict => new InvalidOperationException(ex.Message),
        HttpStatusCode.NotFound => new InvalidOperationException(ex.Message),
        HttpStatusCode.BadRequest => new ArgumentException(ex.Message),
        _ => new InvalidOperationException(ex.Message),
    };
}
