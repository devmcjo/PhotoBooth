using System.IO;
using MCPhoto.Core.Models;
using MCPhoto.Core.Settings;
using MCPhoto.Core.Upload;
using Microsoft.Extensions.Logging;

namespace MCPhoto.Firebase;

/// <summary>
/// Firebase 업로드 오케스트레이션. results/ 업로드 → 토큰 URL → ResultSession 생성 → downloadPageUrl.
/// (architecture §6, firebase-contract §4). 만료 정리 1차 담당(§6.5).
/// </summary>
public sealed class UploadService : IUploadService
{
    private readonly IFirebaseClient _client;
    private readonly ILogger<UploadService>? _logger;

    public UploadService(IFirebaseClient client, ILogger<UploadService>? logger = null)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<ResultSession> UploadResultAsync(
        string finalImagePath,
        string? timelapsePath,
        int retentionHours,
        string hostingBaseUrl,
        CancellationToken ct = default)
    {
        if (!_client.IsInitialized)
            throw new InvalidOperationException("Firebase 미초기화 — 업로드 불가(QR off/로컬 저장 완화 경로 사용).");

        var token = UploadContract.NewSessionToken();
        var format = finalImagePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
            ? OutputFormat.Png : OutputFormat.Jpg;

        // 1) 최종 이미지 업로드 + 토큰 URL
        var finalStoragePath = UploadContract.FinalImagePath(token, format);
        var finalContentType = format == OutputFormat.Png ? "image/png" : "image/jpeg";
        var finalToken = await _client.UploadFileAsync(finalStoragePath, finalImagePath, finalContentType, ct);
        var finalUrl = UploadContract.TokenDownloadUrl(_client.Bucket, finalStoragePath, finalToken);

        // 2) 타임랩스 업로드(있을 때만)
        string? timelapseUrl = null;
        if (!string.IsNullOrEmpty(timelapsePath) && File.Exists(timelapsePath))
        {
            var tlPath = UploadContract.TimelapsePath(token);
            var tlToken = await _client.UploadFileAsync(tlPath, timelapsePath, "video/mp4", ct);
            timelapseUrl = UploadContract.TokenDownloadUrl(_client.Bucket, tlPath, tlToken);
        }

        // 3) ResultSession 문서 생성
        var now = DateTime.UtcNow;
        var session = new ResultSession
        {
            Id = token,
            FinalImageUrl = finalUrl,
            TimelapseUrl = timelapseUrl,
            CreatedAt = now,
            ExpiresAt = UploadContract.ComputeExpiresAt(now, retentionHours),
            DownloadPageUrl = UploadContract.DownloadPageUrl(hostingBaseUrl, token)
        };
        await _client.CreateResultSessionAsync(session, ct);

        _logger?.LogInformation("업로드 완료: session={Token}, page={Url}", token, session.DownloadPageUrl);
        return session;
    }

    public async Task<int> PurgeExpiredAsync(CancellationToken ct = default)
    {
        if (!_client.IsInitialized) return 0;

        var expired = await _client.QueryExpiredSessionsAsync(DateTime.UtcNow, ct);
        int count = 0;
        foreach (var s in expired)
        {
            try
            {
                // 불변식: 문서 + Storage 파일 함께 정리(고아 최소화). firebase-contract §6.3
                await _client.DeleteStoragePrefixAsync($"results/{s.Id}/", ct);
                await _client.DeleteResultSessionAsync(s.Id, ct);
                count++;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "만료 세션 정리 실패: {Id}", s.Id);
            }
        }
        if (count > 0) _logger?.LogInformation("만료 세션 {Count}건 정리", count);
        return count;
    }
}
