using MCPhoto.Core.Capture;
using Microsoft.Extensions.Logging;

namespace MCPhoto.Capture;

/// <summary>
/// 세션 녹화본 → 배속 타임랩스 mp4(목표 10~15초·무음·H.264). (architecture §2.5, PRD §F3)
/// 세션 길이는 녹화 측이 전달(프레임 수/fps)하거나 기본 추정치를 쓴다.
/// </summary>
public sealed class TimelapseService : ITimelapseService
{
    private readonly FfmpegRunner _ffmpeg;
    private readonly ILogger<TimelapseService>? _logger;

    /// <summary>녹화 세션 길이(초). CameraService가 녹화 종료 시 설정.</summary>
    public double LastSessionSeconds { get; set; }

    public TimelapseService(FfmpegRunner ffmpeg, ILogger<TimelapseService>? logger = null)
    {
        _ffmpeg = ffmpeg;
        _logger = logger;
    }

    public async Task<string?> CreateTimelapseAsync(string sessionVideoPath, string outputPath, CancellationToken ct = default)
    {
        if (!_ffmpeg.IsAvailable)
        {
            _logger?.LogWarning("ffmpeg 미탑재 — 타임랩스 생성 불가: {Path}", _ffmpeg.FfmpegPath);
            return null;
        }

        double sessionSeconds = LastSessionSeconds > 0 ? LastSessionSeconds : 60.0; // 폴백 추정
        double speed = FfmpegArgs.ComputeSpeedFactor(sessionSeconds);
        var args = FfmpegArgs.BuildTimelapseArgs(sessionVideoPath, speed, outputPath);

        _logger?.LogInformation("타임랩스 생성: {Sec:F1}s → {Speed:F2}x", sessionSeconds, speed);

        bool ok = await _ffmpeg.RunAsync(args, ct);
        if (!ok)
        {
            _logger?.LogWarning("타임랩스 생성 실패");
            return null;
        }
        return outputPath;
    }
}
