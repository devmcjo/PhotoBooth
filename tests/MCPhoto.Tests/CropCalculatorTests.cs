using MCPhoto.Core.Capture;

namespace MCPhoto.Tests;

/// <summary>WBS Step 3: 중앙 크롭 ROI 수치·중앙 정렬·왜곡 없음 검증.</summary>
public class CropCalculatorTests
{
    [Fact]
    public void Portrait_Target_On_Landscape_Source_Crops_Sides()
    {
        // 1920×1080 가로 원본, 3:4(=0.75) 세로 슬롯 목표 → 좌우 크롭
        var crop = CropCalculator.CenterCrop(1920, 1080, 3.0 / 4.0);

        // 높이 유지, 폭 = 1080 * 0.75 = 810
        Assert.Equal(1080, crop.Height);
        Assert.Equal(810, crop.Width);

        // 중앙 정렬: x = (1920-810)/2 = 555, y = 0
        Assert.Equal((1920 - 810) / 2, crop.X);
        Assert.Equal(0, crop.Y);

        // 종횡비 정확(왜곡 없음)
        Assert.Equal(0.75, (double)crop.Width / crop.Height, 3);

        // 원본 경계 내
        Assert.True(crop.X + crop.Width <= 1920);
        Assert.True(crop.Y + crop.Height <= 1080);
    }

    [Fact]
    public void Landscape_Target_On_Landscape_Source_Crops_TopBottom()
    {
        // 1920×1080 원본, 16:9(≈1.778)보다 더 넓은 21:9(≈2.333) 목표 → 좌우 크롭
        // 반대로 4:3(≈1.333) 목표는 원본(1.778)보다 좁으므로 상하 크롭
        var crop = CropCalculator.CenterCrop(1920, 1080, 4.0 / 3.0);

        // 폭 유지, 높이 = 1920 / (4/3) = 1440 → 원본 1080 초과이므로 clamp
        // 실제: srcAspect(1.778) > target(1.333) → 좌우 크롭 경로
        // 높이 유지 1080, 폭 = 1080 * 1.333 = 1440
        Assert.Equal(1080, crop.Height);
        Assert.Equal(1440, crop.Width);
        Assert.Equal((1920 - 1440) / 2, crop.X);
    }

    [Fact]
    public void Tall_Target_On_Landscape_Crops_Sides_Heavily()
    {
        // 9:16(≈0.5625) 세로 목표 → 좌우 대폭 크롭
        var crop = CropCalculator.CenterCrop(1920, 1080, 9.0 / 16.0);

        Assert.Equal(1080, crop.Height);
        Assert.Equal((int)System.Math.Round(1080 * 9.0 / 16.0), crop.Width); // 608
        // 정수 픽셀 크롭이라 종횡비는 근사(608/1080 ≈ 0.563). 절대 오차 허용.
        Assert.True(System.Math.Abs((double)crop.Width / crop.Height - 0.5625) < 0.01);
    }

    [Fact]
    public void Square_Target_Centers()
    {
        var crop = CropCalculator.CenterCrop(1920, 1080, 1.0);
        Assert.Equal(1080, crop.Width);
        Assert.Equal(1080, crop.Height);
        Assert.Equal((1920 - 1080) / 2, crop.X);
        Assert.Equal(0, crop.Y);
    }

    [Fact]
    public void Portrait_Source_Landscape_Target_Crops_TopBottom()
    {
        // 1080×1920 세로 원본에 4:3 가로 목표 → 상하 크롭
        var crop = CropCalculator.CenterCrop(1080, 1920, 4.0 / 3.0);
        Assert.Equal(1080, crop.Width);
        Assert.Equal((int)System.Math.Round(1080 / (4.0 / 3.0)), crop.Height); // 810
        Assert.Equal(0, crop.X);
        Assert.Equal((1920 - 810) / 2, crop.Y);
    }

    [Fact]
    public void Zero_Or_Negative_Aspect_Returns_Full()
    {
        var crop = CropCalculator.CenterCrop(1920, 1080, 0);
        Assert.Equal(new CropRect(0, 0, 1920, 1080), crop);
    }

    [Fact]
    public void Crop_Never_Exceeds_Source()
    {
        foreach (var aspect in new[] { 0.1, 0.5, 0.75, 1.0, 1.5, 1.78, 3.0, 10.0 })
        {
            var crop = CropCalculator.CenterCrop(1920, 1080, aspect);
            Assert.True(crop.X >= 0 && crop.Y >= 0);
            Assert.True(crop.X + crop.Width <= 1920, $"width overflow at {aspect}");
            Assert.True(crop.Y + crop.Height <= 1080, $"height overflow at {aspect}");
            Assert.True(crop.Width > 0 && crop.Height > 0);
        }
    }
}
