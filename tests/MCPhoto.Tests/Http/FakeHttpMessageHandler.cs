using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace MCPhoto.Tests.Http;

/// <summary>
/// P3 HTTP 계층 단위 테스트용 가짜 핸들러. 실서버 호출 금지 — 모든 요청을 가로채 기록하고,
/// 경로별로 등록한 응답(또는 응답 함수)을 돌려준다. 요청 본문·헤더·순서를 검증할 수 있게 캡처한다.
/// </summary>
internal sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    /// <summary>수신한 모든 요청의 스냅샷(본문 문자열 포함, 순서 보존).</summary>
    public List<CapturedRequest> Requests { get; } = new();

    /// <summary>(method, 경로prefix) → 응답 팩토리. 첫 매칭을 사용.</summary>
    private readonly List<(HttpMethod Method, string PathContains, Func<CapturedRequest, HttpResponseMessage> Responder)> _routes = new();

    /// <summary>매칭 실패 시 기본 응답(없으면 500).</summary>
    public Func<CapturedRequest, HttpResponseMessage>? Fallback { get; set; }

    public void When(HttpMethod method, string pathContains, Func<CapturedRequest, HttpResponseMessage> responder)
        => _routes.Add((method, pathContains, responder));

    public void WhenJson(HttpMethod method, string pathContains, HttpStatusCode status, string json)
        => When(method, pathContains, _ => JsonResponse(status, json));

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        var captured = new CapturedRequest(request, body);
        Requests.Add(captured);

        var url = request.RequestUri?.ToString() ?? string.Empty;
        foreach (var route in _routes)
        {
            if (route.Method == request.Method && url.Contains(route.PathContains))
                return route.Responder(captured);
        }

        if (Fallback is not null) return Fallback(captured);

        return new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("{\"error\":{\"code\":\"internal\",\"message\":\"라우트 미등록\"}}"),
        };
    }

    public static HttpResponseMessage JsonResponse(HttpStatusCode status, string json)
        => new(status)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
        };

    public static HttpResponseMessage NoContent(HttpStatusCode status = HttpStatusCode.NoContent)
        => new(status);
}

/// <summary>가로챈 요청의 불변 스냅샷.</summary>
internal sealed class CapturedRequest
{
    public HttpMethod Method { get; }
    public Uri? Uri { get; }
    public string? Body { get; }
    public HttpRequestMessage Raw { get; }

    public CapturedRequest(HttpRequestMessage raw, string? body)
    {
        Raw = raw;
        Method = raw.Method;
        Uri = raw.RequestUri;
        Body = body;
    }

    public string? HeaderValue(string name)
    {
        if (Raw.Headers.TryGetValues(name, out var vals))
            foreach (var v in vals) return v;
        if (Raw.Content is not null && Raw.Content.Headers.TryGetValues(name, out var cvals))
            foreach (var v in cvals) return v;
        return null;
    }

    public string? AuthorizationScheme => Raw.Headers.Authorization?.Scheme;
    public string? AuthorizationParameter => Raw.Headers.Authorization?.Parameter;
}
