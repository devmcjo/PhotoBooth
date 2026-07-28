namespace MCPhoto.Http;

using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MCPhoto.Core.Accounts;
using MCPhoto.Http.Dto;
using MCPhoto.Http.Session;
using Microsoft.Extensions.Logging;

/// <summary>
/// <see cref="ITempUserLimitsService"/>의 HTTP 구현(설계 §5.4). GET/PATCH /config/temp-user-limits.
///
/// - GET(Bearer): 모든 로그인 사용자 조회 가능(표시용). 문서 부재 시 서버가 기본값(48h/30회) 반환.
/// - PATCH(requireAdmin): 비Admin은 서버 403 → <see cref="System.UnauthorizedAccessException"/>(MapToDomainException).
/// </summary>
public sealed class HttpTempUserLimitsService : HttpBackendClient, ITempUserLimitsService
{
    public HttpTempUserLimitsService(
        IHttpClientFactory httpClientFactory,
        IBackendSession session,
        string apiKey,
        ILogger<HttpTempUserLimitsService>? logger = null)
        : base(httpClientFactory, session, apiKey, logger)
    {
    }

    public async Task<TempUserLimits> GetLimitsAsync(CancellationToken ct = default)
    {
        try
        {
            var res = await GetJsonAsync<TempUserLimitsDto>("config/temp-user-limits", bearer: true, ct)
                .ConfigureAwait(false);
            return new TempUserLimits(res.QrHours, res.QrCount);
        }
        catch (BackendException ex)
        {
            throw MapToDomainException(ex);
        }
    }

    public async Task SetLimitsAsync(TempUserLimits limits, CancellationToken ct = default)
    {
        try
        {
            await SendNoContentAsync(
                HttpMethod.Patch, "config/temp-user-limits",
                new TempUserLimitsDto { QrHours = limits.QrHours, QrCount = limits.QrCount },
                bearer: true, ct).ConfigureAwait(false);
        }
        catch (BackendException ex)
        {
            throw MapToDomainException(ex);   // 403(비Admin)→UnauthorizedAccessException, 400(범위)→ArgumentException
        }
    }
}
