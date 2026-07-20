using System.IO;
using MCPhoto.Capture;
using MCPhoto.Core.Frames;
using OpenCvSharp;

namespace MCPhoto.Tests;

/// <summary>WBS Step 9/12: fallback 프레임(하양·3:4·4슬롯) 생성 검증(오프라인 게스트 모드).</summary>
public class FallbackFrameTests : IDisposable
{
    private readonly string _work;

    public FallbackFrameTests()
    {
        _work = Path.Combine(Path.GetTempPath(), $"mcphoto_fb_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_work);
    }

    public void Dispose()
    {
        try { Directory.Delete(_work, recursive: true); } catch { /* 무시 */ }
    }

    [Fact]
    public void Fallback_Template_Has_4_Slots_3by4()
    {
        var t = DefaultFrameProvider.CreateFallbackTemplate("x.png");
        Assert.Equal(4, t.Slots.Count);
        Assert.True(t.IsDefault);
        Assert.Null(t.UserId);

        // 프레임 3:4 비율
        double frameAspect = (double)t.ImageSize.Width / t.ImageSize.Height;
        Assert.Equal(0.75, frameAspect, 2);

        // 모든 슬롯이 프레임 경계 내
        foreach (var s in t.Slots)
        {
            Assert.True(s.X >= 0 && s.Y >= 0);
            Assert.True(s.X + s.Width <= t.ImageSize.Width);
            Assert.True(s.Y + s.Height <= t.ImageSize.Height);
        }
    }

    [Fact]
    public void Fallback_Renders_White_Png()
    {
        var path = Path.Combine(_work, "fallback.png");
        var template = FallbackFrameRenderer.Create(path);

        Assert.True(File.Exists(path));
        using var img = Cv2.ImRead(path, ImreadModes.Color);
        Assert.Equal(DefaultFrameProvider.FallbackWidth, img.Width);
        Assert.Equal(DefaultFrameProvider.FallbackHeight, img.Height);

        // 모서리(슬롯 밖)는 하양 배경
        var corner = img.At<Vec3b>(5, 5);
        Assert.True(corner.Item0 > 240 && corner.Item1 > 240 && corner.Item2 > 240);
        Assert.Equal(4, template.Slots.Count);
    }
}
