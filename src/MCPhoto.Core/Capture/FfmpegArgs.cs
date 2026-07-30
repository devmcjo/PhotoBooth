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
    /// 홀수 변을 1px 잘라 짝수로 맞추는 crop 필터. yuv420p(4:2:0)는 두 변이 모두 짝수여야
    /// 인코더가 열린다 — 홀수면 libx264 가 "width not divisible by 2"로 인코더 개시 자체를
    /// 거부하고 프로세스가 즉시 죽는다(exit=-542398533). 그러면 파이프가 끊겨 이후 모든
    /// WriteFrame 이 "프레임 write 실패(드롭)"로 흐르고, session.mp4 가 못 만들어지므로
    /// 타임랩스도 생성되지 않는다. 웹에서는 timelapseUrl=null 이 "전송 옵션 꺼짐"으로 보인다.
    ///
    /// 녹화 크기는 카메라 프레임을 프레임(배경) 종횡비로 center crop 한 결과라 임의의 홀수가
    /// 될 수 있다(실측: 1443x1080 → 실패, 810x1080 → 성공). 실패는 특정 프레임을 고른
    /// 세션에서만 재현되므로 눈에 잘 띄지 않았다.
    ///
    /// scale 이 아니라 crop 인 이유: 1px 을 버리는 것은 리샘플이 없어 화질 손실이 0 이다.
    /// 또한 -video_size 는 원본 그대로 두어 파이프로 보내는 프레임 바이트 수(stride)를
    /// 바꾸지 않는다 — 호출부(WriteFrame)는 무변경이다. rawvideo/bgr24 입력 자체는
    /// 서브샘플링이 없어 홀수 폭도 정상 처리한다. 제약은 출력 인코딩에만 있다.
    /// </summary>
    private const string EvenDimensionCrop = "crop=trunc(iw/2)*2:trunc(ih/2)*2";

    /// <summary>
    /// 세션 녹화 인자: rawvideo(bgr24) stdin 파이프 → H.264 mp4.
    /// -f rawvideo -pixel_format bgr24 -video_size WxH -framerate 30 -i - -vf crop=... -c:v libx264 -crf 20 -preset veryfast -pix_fmt yuv420p out
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
            // 두 변이 이미 짝수면 no-op 이다. 공백이 없어 인자 1개로 파싱된다
            // (UseShellExecute=false 라 괄호도 셸을 거치지 않는다).
            "-vf", EvenDimensionCrop,
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
        // crop 은 안전벨트다: 입력 session.mp4 는 BuildRecordArgs 가 만든 짝수 영상이라
        // 통상 no-op 이지만, 같은 yuv420p 제약이 여기에도 걸리므로 한쪽만 막아두지 않는다.
        string filter = $"{EvenDimensionCrop},setpts={ptsScale.ToString("0.######", inv)}*PTS,fps=30";
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
