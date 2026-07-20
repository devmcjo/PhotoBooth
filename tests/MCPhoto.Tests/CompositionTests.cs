using System.IO;
using MCPhoto.Capture;
using MCPhoto.Core.Capture;
using MCPhoto.Core.Models;
using OpenCvSharp;

namespace MCPhoto.Tests;

/// <summary>WBS Step 7: 슬롯 배치 좌표·왜곡 없음·출력 크기·필터·배경형 합성 검증.</summary>
public class CompositionTests : IDisposable
{
    private readonly string _work;

    public CompositionTests()
    {
        _work = Path.Combine(Path.GetTempPath(), $"mcphoto_comp_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_work);
    }

    public void Dispose()
    {
        try { Directory.Delete(_work, recursive: true); } catch { /* 무시 */ }
    }

    // ── SlotPlacement 순수 로직 ──

    [Fact]
    public void SourceCrop_Matching_Aspect_Uses_Full()
    {
        // 소스가 이미 슬롯 종횡비(300x400 = 3:4, 슬롯 150x200 = 3:4) → 크롭 없이 전체
        var crop = SlotPlacement.SourceCropForSlot(300, 400, 150, 200);
        Assert.Equal(300, crop.Width);
        Assert.Equal(400, crop.Height);
        Assert.Equal(0, crop.X);
        Assert.Equal(0, crop.Y);
    }

    [Fact]
    public void SourceCrop_Different_Aspect_Center_Crops()
    {
        // 소스 400x400(1:1), 슬롯 3:4(0.75) → 좌우 크롭
        var crop = SlotPlacement.SourceCropForSlot(400, 400, 150, 200);
        Assert.Equal(400, crop.Height);
        Assert.Equal(300, crop.Width); // 400*0.75
        Assert.Equal(50, crop.X);      // 중앙
    }

    [Fact]
    public void ClampSlot_Keeps_Inside_Frame()
    {
        var slot = new Slot { X = -10, Y = -5, Width = 5000, Height = 5000 };
        var r = SlotPlacement.ClampSlotToFrame(slot, 1000, 800);
        Assert.True(r.X >= 0 && r.Y >= 0);
        Assert.True(r.X + r.Width <= 1000);
        Assert.True(r.Y + r.Height <= 800);
    }

    // ── 실제 배경형 합성 통합 ──

    private FrameTemplate MakeFrameWithImage(int frameW, int frameH, int slots)
    {
        // 흰 배경 프레임 이미지 생성
        var framePath = Path.Combine(_work, "frame.png");
        using (var bg = new Mat(frameH, frameW, MatType.CV_8UC3, new Scalar(255, 255, 255)))
            Cv2.ImWrite(framePath, bg);

        var f = new FrameTemplate
        {
            Id = "f1",
            Name = "grid",
            ImageUrl = framePath,
            ImageSize = new ImageSize { Width = frameW, Height = frameH }
        };

        // 2x2 격자(슬롯 종횡비 3:4)
        int sw = frameW / 2 - 30;
        int sh = (int)(sw * 4.0 / 3.0);
        int[,] pos = { { 20, 20 }, { frameW / 2 + 10, 20 }, { 20, frameH / 2 + 10 }, { frameW / 2 + 10, frameH / 2 + 10 } };
        for (int i = 0; i < slots; i++)
            f.Slots.Add(new Slot { Index = i, X = pos[i, 0], Y = pos[i, 1], Width = sw, Height = sh });
        return f;
    }

    private static CapturedStill MakeColorStill(int w, int h, byte b, byte g, byte r)
    {
        var px = new byte[w * h * 3];
        for (int i = 0; i < px.Length; i += 3) { px[i] = b; px[i + 1] = g; px[i + 2] = r; }
        return new CapturedStill { Width = w, Height = h, Pixels = px };
    }

    [Fact]
    public async Task Compose_Produces_Image_At_Frame_Resolution()
    {
        var frame = MakeFrameWithImage(800, 1000, 4);
        // 컷은 슬롯 종횡비(3:4) 300x400
        var cuts = new[]
        {
            MakeColorStill(300, 400, 255, 0, 0),   // 파랑
            MakeColorStill(300, 400, 0, 255, 0),   // 초록
            MakeColorStill(300, 400, 0, 0, 255),   // 빨강
            MakeColorStill(300, 400, 0, 255, 255)  // 노랑
        };
        var outPath = Path.Combine(_work, "final.jpg");

        var svc = new CompositionService();
        var result = await svc.ComposeAsync(frame, cuts, FilterKind.None, outPath);

        Assert.True(File.Exists(result));
        using var composed = Cv2.ImRead(result, ImreadModes.Color);
        // 출력 해상도 = 프레임 원본
        Assert.Equal(800, composed.Width);
        Assert.Equal(1000, composed.Height);

        // 첫 슬롯 중앙 픽셀이 파랑(컷1)인지 → 슬롯에 컷이 배치됨
        var slot0 = frame.Slots[0];
        var center = composed.At<Vec3b>(slot0.Y + slot0.Height / 2, slot0.X + slot0.Width / 2);
        Assert.True(center.Item0 > 200, "슬롯0 파랑 채널이 높아야 함(컷 배치 확인)");
        Assert.True(center.Item2 < 60, "슬롯0 빨강 채널이 낮아야 함");
    }

    [Fact]
    public async Task Compose_Rejects_Wrong_Cut_Count()
    {
        var frame = MakeFrameWithImage(800, 1000, 4);
        var cuts = new[] { MakeColorStill(300, 400, 255, 0, 0) }; // 슬롯 4개인데 컷 1개
        var svc = new CompositionService();

        await Assert.ThrowsAsync<ArgumentException>(
            () => svc.ComposeAsync(frame, cuts, FilterKind.None, Path.Combine(_work, "x.jpg")));
    }

    [Fact]
    public async Task Compose_Grayscale_Filter_Desaturates()
    {
        var frame = MakeFrameWithImage(400, 600, 1);
        // 순수 파랑 컷(3:4)
        var cuts = new[] { MakeColorStill(300, 400, 255, 0, 0) };
        var outPath = Path.Combine(_work, "gray.jpg");

        var svc = new CompositionService();
        await svc.ComposeAsync(frame, cuts, FilterKind.Grayscale, outPath);

        using var composed = Cv2.ImRead(outPath, ImreadModes.Color);
        var slot = frame.Slots[0];
        var center = composed.At<Vec3b>(slot.Y + slot.Height / 2, slot.X + slot.Width / 2);
        // 그레이스케일이면 B≈G≈R (채널 차이 작음)
        int maxDiff = Math.Max(Math.Abs(center.Item0 - center.Item1),
                      Math.Max(Math.Abs(center.Item1 - center.Item2), Math.Abs(center.Item0 - center.Item2)));
        Assert.True(maxDiff < 25, $"흑백이면 채널 차이가 작아야 함(실제 {maxDiff})");
    }
}
