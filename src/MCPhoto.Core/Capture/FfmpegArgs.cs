using System.Globalization;

namespace MCPhoto.Core.Capture;

/// <summary>
/// ffmpeg 커맨드 인자 조립 + 타임랩스 배속 N 역산(순수 로직, 테스트 대상). (architecture §2.5)
/// </summary>
public static class FfmpegArgs
{
    /// <summary>타임랩스 목표 길이 범위(초).</summary>
    public const double TargetMinSeconds = 10.0;
    public const double TargetMaxSeconds = 15.0;

    /// <summary>
    /// 세션 녹화 인자: rawvideo(bgr24) stdin 파이프 → H.264 mp4.
    /// -f rawvideo -pixel_format bgr24 -video_size WxH -framerate 30 -i - -c:v libx264 -crf 20 -preset veryfast -pix_fmt yuv420p out
    /// </summary>
    public static string BuildRecordArgs(int width, int height, int framerate, string outputPath)
    {
        var inv = CultureInfo.InvariantCulture;
        return string.Join(' ',
            "-y",
            "-f", "rawvideo",
            "-pixel_format", "bgr24",
            "-video_size", $"{width.ToString(inv)}x{height.ToString(inv)}",
            "-framerate", framerate.ToString(inv),
            "-i", "-",
            "-c:v", "libx264",
            "-crf", "20",
            "-preset", "veryfast",
            "-pix_fmt", "yuv420p",
            Quote(outputPath));
    }

    /// <summary>
    /// 타임랩스 인자: setpts 배속 + fps 30, 무음(-an), H.264.
    /// -i session.mp4 -vf "setpts=(1/N)*PTS,fps=30" -an -c:v libx264 -crf 20 -pix_fmt yuv420p out
    /// </summary>
    public static string BuildTimelapseArgs(string sessionVideoPath, double speedFactor, string outputPath)
    {
        var inv = CultureInfo.InvariantCulture;
        // setpts 계수 = 1/N (N배속). N>1이면 재생 시간 단축.
        double ptsScale = speedFactor <= 0 ? 1.0 : 1.0 / speedFactor;
        string filter = $"setpts={ptsScale.ToString("0.######", inv)}*PTS,fps=30";
        return string.Join(' ',
            "-y",
            "-i", Quote(sessionVideoPath),
            "-vf", Quote(filter),
            "-an",
            "-c:v", "libx264",
            "-crf", "20",
            "-pix_fmt", "yuv420p",
            Quote(outputPath));
    }

    /// <summary>
    /// 세션 길이(초)에서 목표 10~15초가 되도록 배속 N 역산.
    /// - 세션이 목표보다 짧으면 N=1(그대로).
    /// - 목표 중앙값(12.5초)을 기준으로 N = sessionSeconds / 12.5, 최소 1.
    /// </summary>
    public static double ComputeSpeedFactor(double sessionSeconds)
    {
        if (sessionSeconds <= TargetMaxSeconds) return 1.0;
        const double targetMid = (TargetMinSeconds + TargetMaxSeconds) / 2.0; // 12.5
        double n = sessionSeconds / targetMid;
        return Math.Max(1.0, n);
    }

    /// <summary>배속 적용 후 예상 결과 길이(초). 검증용.</summary>
    public static double ExpectedOutputSeconds(double sessionSeconds, double speedFactor)
        => speedFactor <= 0 ? sessionSeconds : sessionSeconds / speedFactor;

    private static string Quote(string path) => path.Contains(' ') ? $"\"{path}\"" : path;
}
