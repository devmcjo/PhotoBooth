using MCPhoto.Core.Capture;

namespace MCPhoto.Tests;

/// <summary>WBS Step 6: ffmpeg 인자 조립·타임랩스 배속 N 역산 로직 검증.</summary>
public class FfmpegArgsTests
{
    [Fact]
    public void Record_Args_Contain_Required_Flags()
    {
        var args = FfmpegArgs.BuildRecordArgs(810, 1080, 30, @"C:\temp\session.mp4");

        Assert.Contains("-f rawvideo", args);
        Assert.Contains("-pixel_format bgr24", args);
        Assert.Contains("-video_size 810x1080", args);
        Assert.Contains("-framerate 30", args);
        Assert.Contains("-i -", args);          // stdin 파이프
        Assert.Contains("-c:v libx264", args);
        Assert.Contains("-pix_fmt yuv420p", args);
        Assert.Contains("session.mp4", args);
    }

    [Fact]
    public void Timelapse_Args_Are_Muted_H264()
    {
        var args = FfmpegArgs.BuildTimelapseArgs(@"C:\temp\session.mp4", 5.0, @"C:\temp\timelapse.mp4");

        Assert.Contains("-an", args);            // 무음
        Assert.Contains("-c:v libx264", args);
        Assert.Contains("-pix_fmt yuv420p", args);
        Assert.Contains("setpts=", args);
        Assert.Contains("fps=30", args);
        // 5배속 → setpts=0.2*PTS
        Assert.Contains("setpts=0.2*PTS", args);
    }

    [Fact]
    public void SpeedFactor_Short_Session_Is_One()
    {
        // 목표(≤15초)보다 짧으면 그대로(N=1)
        Assert.Equal(1.0, FfmpegArgs.ComputeSpeedFactor(8));
        Assert.Equal(1.0, FfmpegArgs.ComputeSpeedFactor(15));
    }

    [Fact]
    public void SpeedFactor_Long_Session_Compresses_To_Target()
    {
        // 60초 세션 → N = 60/12.5 = 4.8배 → 결과 12.5초(목표 범위 내)
        double n = FfmpegArgs.ComputeSpeedFactor(60);
        Assert.Equal(4.8, n, 2);

        double outSec = FfmpegArgs.ExpectedOutputSeconds(60, n);
        Assert.InRange(outSec, FfmpegArgs.TargetMinSeconds, FfmpegArgs.TargetMaxSeconds);
    }

    [Theory]
    [InlineData(30)]
    [InlineData(45)]
    [InlineData(90)]
    [InlineData(120)]
    [InlineData(300)]
    public void SpeedFactor_Keeps_Output_In_Target_Range(double sessionSeconds)
    {
        double n = FfmpegArgs.ComputeSpeedFactor(sessionSeconds);
        double outSec = FfmpegArgs.ExpectedOutputSeconds(sessionSeconds, n);
        Assert.InRange(outSec, FfmpegArgs.TargetMinSeconds, FfmpegArgs.TargetMaxSeconds + 0.001);
        Assert.True(n >= 1.0);
    }

    [Fact]
    public void Path_With_Spaces_Is_Quoted()
    {
        var args = FfmpegArgs.BuildRecordArgs(640, 480, 30, @"C:\my videos\session.mp4");
        Assert.Contains("\"C:\\my videos\\session.mp4\"", args);
    }
}
