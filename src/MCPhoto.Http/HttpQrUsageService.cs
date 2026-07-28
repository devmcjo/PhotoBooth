namespace MCPhoto.Http;

using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MCPhoto.Core.Accounts;
using MCPhoto.Http.Dto;
using MCPhoto.Http.Session;
using Microsoft.Extensions.Logging;

/// <summary>
/// <see cref="IQrUsageService"/>의 HTTP 구현(설계 §5.3·§7.2). GET /accounts/me/qr-usage(Bearer) 호출.
///
/// - 서버가 principal.id로 계정을 로드해 evaluateQrGate 실행한 결과를 받는다(서버 권위, 과금 안전).
/// - 서버 미도달(네트워크/타임아웃) 시 <c>null</c> 반환 → 셸이 fail-open으로 처리(허용, 서버가 업로드에서 최종 거부, §8.5).
/// </summary>
public sealed class HttpQrUsageService : HttpBackendClient, IQrUsageService
{
    public HttpQrUsageService(
        IHttpClientFactory httpClientFactory,
        IBackendSession session,
        string apiKey,
        ILogger<HttpQrUsageService>? logger = null)
        : base(httpClientFactory, session, apiKey, logger)
    {
    }

    public async Task<QrUsageStatus?> GetStatusAsync(CancellationToken ct = default)
    {
        try
        {
            var res = await GetJsonAsync<QrUsageResponse>("accounts/me/qr-usage", bearer: true, ct)
                .ConfigureAwait(false);
            return ToStatus(res);
        }
        catch (BackendException ex)
        {
            // 서버가 응답은 했으나 오류(401 토큰 만료 등) → fail-open(null). 과금 안전은 업로드 거부가 담보(§8.5).
            Logger?.LogWarning("QR 사용량 조회 실패({Status}) — fail-open", ex.StatusCode);
            return null;
        }
        catch (InvalidOperationException ex)
        {
            // 백엔드 미도달(네트워크/타임아웃) → fail-open. HttpBackendClient가 이 예외로 래핑.
            Logger?.LogWarning(ex, "QR 사용량 조회 실패(백엔드 미도달) — fail-open");
            return null;
        }
    }

    /// <summary>서버 응답 → 도메인 상태. reason 문자열 파싱, remainingMs를 TimeSpan으로 변환.</summary>
    private static QrUsageStatus ToStatus(QrUsageResponse res)
    {
        var reason = res.Reason switch
        {
            "time" => QrGateReason.Time,
            "count" => QrGateReason.Count,
            _ => QrGateReason.Ok,
        };
        // 음수 방어(서버가 clamp하지만 이중). remainingMs가 과대해도 TimeSpan.FromMilliseconds가 처리.
        var remaining = TimeSpan.FromMilliseconds(Math.Max(0, res.RemainingMs));
        return new QrUsageStatus(res.Blocked, reason, remaining, Math.Max(0, res.RemainingCount));
    }
}
