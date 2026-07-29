namespace MCPhoto.Http;

using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MCPhoto.Core.Build;
using MCPhoto.Http.Dto;
using MCPhoto.Http.Session;
using Microsoft.Extensions.Logging;

/// <summary>
/// <see cref="IServerDeployInfoService"/>의 HTTP 구현. GET /health의 deployedAt을 읽어 최종 웹 배포 시각을 얻는다.
///
/// 서버는 이 필드를 유효 API 키 제시 시에만 응답에 넣으므로(무인증 스캐너 비노출), API 키가 항상 부착되는
/// <see cref="HttpBackendClient"/> 경로를 그대로 쓴다. Bearer는 불필요(로그인 전에도 조회 가능).
/// 진단 표기 하나를 위한 호출이므로 어떤 실패도 예외로 올리지 않고 null을 반환한다.
/// </summary>
public sealed class HttpServerDeployInfoService : HttpBackendClient, IServerDeployInfoService
{
    private readonly bool _configured;

    public HttpServerDeployInfoService(
        IHttpClientFactory httpClientFactory,
        IBackendSession session,
        string apiKey,
        bool configured,
        ILogger<HttpServerDeployInfoService>? logger = null)
        : base(httpClientFactory, session, apiKey, logger)
    {
        _configured = configured;
    }

    public async Task<DateTimeOffset?> GetWebDeployedAtAsync(CancellationToken ct = default)
    {
        // 미구성(base URL 없음)이면 호출 자체를 하지 않는다 — BaseAddress 없는 상대 URL은 예외가 된다.
        if (!_configured) return null;
        try
        {
            var health = await GetJsonAsync<HealthResponse>("health", bearer: false, ct).ConfigureAwait(false);
            return health.DeployedAt;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // 미도달·오류 응답·본문 파싱 실패 모두 "확인 불가" 표기로 폴백(진단 화면은 항상 열려야 한다).
            Logger?.LogWarning(ex, "웹 배포일 조회 실패 — 진단 표기 생략");
            return null;
        }
    }
}
