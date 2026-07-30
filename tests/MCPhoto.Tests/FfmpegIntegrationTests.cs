using System.IO;
using MCPhoto.Capture;
using MCPhoto.Core.Capture;

namespace MCPhoto.Tests;

/// <summary>
/// WBS Step 6 통합: 실제 ffmpeg 바이너리로 녹화·타임랩스 end-to-end.
/// tools/ffmpeg/ffmpeg.exe가 있을 때만 실행(없으면 Skip).
/// </summary>
public class FfmpegIntegrationTests
{
    private static string? FindFfmpeg()
    {
        // 리포 루트의 tools/ffmpeg 탐색(테스트 실행 위치 → 상위로)
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "tools", "ffmpeg", "ffmpeg.exe");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }

    [Theory]
    [InlineData(320, 240)] // 짝수 — 종전 케이스
    [InlineData(321, 241)] // 두 변 모두 홀수. 짝수 보정이 없으면 libx264 가 yuv420p 인코더를
                           // 열지 못해(width not divisible by 2) 프로세스가 즉시 죽고, 파이프가
                           // 끊겨 session.mp4 가 아예 안 만들어진다 → 타임랩스도 실패한다.
                           // 실측 회귀: 1443x1080 세션에서 timelapseUrl 이 null 로 커밋되어
                           // 웹에 "전송 옵션 꺼짐"으로 표시됐다. 여기서는 같은 성질(홀수 변)을
                           // 작은 해상도로 재현한다 — 실측 크기는 프레임당 4.6MB 라 느리다.
    public async Task Record_Pipe_Then_Timelapse_Produces_Playable_Mp4(int w, int h)
    {
        var ffmpegPath = FindFfmpeg();
        if (ffmpegPath is null)
        {
            // ffmpeg 미탑재 환경(CI 등) — 실연동 검증 생략
            Assert.True(true, "ffmpeg 미탑재 — 통합 테스트 스킵");
            return;
        }

        var work = Path.Combine(Path.GetTempPath(), $"mcphoto_ff_{Guid.NewGuid():N}");
        Directory.CreateDirectory(work);
        var sessionPath = Path.Combine(work, "session.mp4");
        var timelapsePath = Path.Combine(work, "timelapse.mp4");

        try
        {
            var runner = new FfmpegRunner(ffmpegPath);
            Assert.True(runner.IsAvailable);

            // BGR24 더미 프레임 90장(3초 @30fps) → stdin 파이프 녹화
            const int fps = 30, frames = 90;
            var frame = new byte[w * h * 3];
            for (int i = 0; i < frame.Length; i++) frame[i] = (byte)(i % 256);

            Assert.True(runner.StartRecording(w, h, fps, sessionPath));
            for (int f = 0; f < frames; f++)
                runner.WriteFrame(frame, frame.Length);
            await runner.StopRecordingAsync();

            Assert.True(File.Exists(sessionPath), "session.mp4 생성 실패");
            Assert.True(new FileInfo(sessionPath).Length > 0, "session.mp4 크기 0");

            // 타임랩스: 3초 세션은 목표(≤15초)보다 짧아 N=1(그대로)
            double n = FfmpegArgs.ComputeSpeedFactor(frames / (double)fps);
            var args = FfmpegArgs.BuildTimelapseArgs(sessionPath, n, timelapsePath);
            Assert.True(await runner.RunAsync(args), "타임랩스 생성 실패");
            Assert.True(File.Exists(timelapsePath), "timelapse.mp4 생성 실패");
            Assert.True(new FileInfo(timelapsePath).Length > 0);
        }
        finally
        {
            try { Directory.Delete(work, recursive: true); } catch { /* 무시 */ }
        }
    }
}
