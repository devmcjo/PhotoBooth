using System.IO;
using MCPhoto.Core.Models;
using MCPhoto.Core.Settings;
using Microsoft.Extensions.Logging;

namespace MCPhoto.Core.Upload;

/// <summary>
/// 업로드 오케스트레이션. results/ 업로드 → 토큰 URL → ResultSession 생성 → downloadPageUrl.
/// (architecture §6, firebase-contract §4). 만료 정리 1차 담당(§6.5).
/// it15 D-A: 레거시 Admin SDK 직결 어셈블리 폐지에 따라 Core로 이관(본문 무변경 — IFirebaseClient에만 의존).
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
        string? finalImagePath,
        string? timelapsePath,
        int retentionHours,
        string hostingBaseUrl,
        IProgress<UploadProgress>? progress = null,
        CancellationToken ct = default)
    {
        if (!_client.IsInitialized)
            throw new InvalidOperationException("Firebase 미초기화 — 업로드 불가(QR off/로컬 저장 완화 경로 사용).");

        // 사진·타임랩스 각각 "옵션 on(경로 non-null) & 파일 존재"일 때만 업로드. 최소 1개 필요(it7 F2).
        bool wantPhoto = !string.IsNullOrEmpty(finalImagePath) && File.Exists(finalImagePath);
        bool wantTimelapse = !string.IsNullOrEmpty(timelapsePath) && File.Exists(timelapsePath);
        if (!wantPhoto && !wantTimelapse)
            throw new InvalidOperationException("전송할 미디어가 없습니다(사진·타임랩스 모두 off/부재). QR 연동 규칙 위반.");

        // 세션 ID = 날짜_시간(초, 로컬)_uuid. results/ 하위 폴더명이 되어 Storage에서 시각으로 찾기 쉽다(사용자 요청).
        // 이 ID가 폴더·문서ID·다운로드토큰·자동삭제 prefix를 모두 공유하므로 삭제 루틴 정합(파일명·로컬경로 무변경).
        var token = UploadContract.NewSessionId(DateTime.Now);

        // 1) 최종 이미지 업로드 + 토큰 URL. off면 null.
        // it11 #16: 파일 단위 진행률(IProgress<double>)을 해당 단계(UploadProgress)로 합성해 상위에 보고.
        string? finalUrl = null;
        if (finalImagePath is not null && wantPhoto)
        {
            var format = finalImagePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                ? OutputFormat.Png : OutputFormat.Jpg;
            var finalStoragePath = UploadContract.FinalImagePath(token, format);
            var finalContentType = format == OutputFormat.Png ? "image/png" : "image/jpeg";
            progress?.Report(new UploadProgress(UploadStage.Photo, 0.0));
            var photoFileProgress = MakeStageProgress(progress, UploadStage.Photo);
            var finalToken = await _client.UploadFileAsync(finalStoragePath, finalImagePath, finalContentType, photoFileProgress, ct);
            progress?.Report(new UploadProgress(UploadStage.Photo, 1.0));
            finalUrl = UploadContract.TokenDownloadUrl(_client.Bucket, finalStoragePath, finalToken);
        }

        // 2) 타임랩스 업로드(옵션 on & 파일 존재 시만)
        string? timelapseUrl = null;
        if (timelapsePath is not null && wantTimelapse)
        {
            var tlPath = UploadContract.TimelapsePath(token);
            progress?.Report(new UploadProgress(UploadStage.Timelapse, 0.0));
            var tlFileProgress = MakeStageProgress(progress, UploadStage.Timelapse);
            var tlToken = await _client.UploadFileAsync(tlPath, timelapsePath, "video/mp4", tlFileProgress, ct);
            progress?.Report(new UploadProgress(UploadStage.Timelapse, 1.0));
            timelapseUrl = UploadContract.TokenDownloadUrl(_client.Bucket, tlPath, tlToken);
        }

        // 3) ResultSession 문서 생성
        progress?.Report(new UploadProgress(UploadStage.Finalizing, 1.0, "마무리 중"));
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

    /// <summary>
    /// 파일 단위 진행률(0.0~1.0)을 해당 <paramref name="stage"/>의 UploadProgress로 상위에 중계하는 어댑터.
    /// upstream이 null이면 null 반환(진행 보고 없이 기존 경로). (it11 #16 §3.16.2)
    /// </summary>
    private static IProgress<double>? MakeStageProgress(IProgress<UploadProgress>? upstream, UploadStage stage)
        => upstream is null
            ? null
            : new Progress<double>(f => upstream.Report(new UploadProgress(stage, f)));

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
