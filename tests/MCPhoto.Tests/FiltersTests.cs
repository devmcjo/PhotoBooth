using MCPhoto.App.ViewModels;
using MCPhoto.Capture;
using MCPhoto.Core.Capture;
using MCPhoto.Core.Settings;
using OpenCvSharp;

namespace MCPhoto.Tests;

/// <summary>it8 Step 6 (A6): 필터 실처리 검증 — 흑백/밝기/뷰티가 원본과 다른 결과를 낸다.</summary>
public class FiltersTests
{
    /// <summary>색·밝기 편차가 있는 작은 합성 이미지(그라디언트+노이즈 느낌).</summary>
    private static Mat MakeSource()
    {
        var m = new Mat(40, 40, MatType.CV_8UC3);
        var indexer = m.GetGenericIndexer<Vec3b>();
        for (int y = 0; y < 40; y++)
            for (int x = 0; x < 40; x++)
                indexer[y, x] = new Vec3b((byte)(x * 3 % 256), (byte)(y * 3 % 256), (byte)((x + y) * 2 % 256));
        return m;
    }

    private static double MeanBrightness(Mat bgr)
    {
        using var gray = new Mat();
        Cv2.CvtColor(bgr, gray, ColorConversionCodes.BGR2GRAY);
        return Cv2.Mean(gray).Val0;
    }

    [Fact]
    public void None_Returns_Equivalent_Image()
    {
        using var src = MakeSource();
        using var dst = Filters.Apply(src, FilterKind.None);
        // None은 원본 클론 — 픽셀 동일.
        using var diff = new Mat();
        Cv2.Absdiff(src, dst, diff);
        Assert.Equal(0, Cv2.Sum(diff).Val0, 3);
    }

    [Fact]
    public void Grayscale_Makes_Channels_Equal()
    {
        using var src = MakeSource();
        using var dst = Filters.Apply(src, FilterKind.Grayscale);
        var idx = dst.GetGenericIndexer<Vec3b>();
        // 그레이 → 3채널 복제라 B=G=R.
        for (int y = 0; y < dst.Rows; y += 7)
            for (int x = 0; x < dst.Cols; x += 7)
            {
                var p = idx[y, x];
                Assert.Equal(p.Item0, p.Item1);
                Assert.Equal(p.Item1, p.Item2);
            }
    }

    [Fact]
    public void Brightness_Increases_Mean()
    {
        using var src = MakeSource();
        using var dst = Filters.Apply(src, FilterKind.Brightness);
        Assert.True(MeanBrightness(dst) > MeanBrightness(src),
            "밝기 필터는 평균 밝기를 높여야 함");
    }

    [Fact]
    public void Beauty_Applies_Real_Processing()
    {
        using var src = MakeSource();
        using var dst = Filters.Apply(src, FilterKind.Beauty);
        // 뷰티는 스무딩+약한 톤 보정을 실제 적용 → 원본과 픽셀이 달라지고 밝기가 소폭 증가(beta 6).
        using var diff = new Mat();
        Cv2.Absdiff(src, dst, diff);
        Assert.True(Cv2.Sum(diff).Val0 > 0, "뷰티 필터는 원본을 실제로 변형해야 함");
        Assert.True(MeanBrightness(dst) >= MeanBrightness(src), "뷰티 필터의 톤 보정으로 밝기가 낮아지지 않아야 함");
    }

    // ── A6: 설정 → 노출 필터 목록(항상 None + 켜진 것) ──

    [Fact]
    public void AvailableFilters_All_On_Has_Four()
    {
        var s = new AppSettings { FilterGrayscale = true, FilterBrightness = true, FilterBeauty = true };
        var opts = ResultViewModel.BuildFilterOptions(s);
        Assert.Equal(4, opts.Count); // None + 3
        Assert.Equal(FilterKind.None, opts[0].Kind);
    }

    [Fact]
    public void AvailableFilters_Excludes_Disabled()
    {
        var s = new AppSettings { FilterGrayscale = false, FilterBrightness = true, FilterBeauty = false };
        var opts = ResultViewModel.BuildFilterOptions(s);
        Assert.Contains(opts, o => o.Kind == FilterKind.None);       // 원본 항상
        Assert.Contains(opts, o => o.Kind == FilterKind.Brightness);
        Assert.DoesNotContain(opts, o => o.Kind == FilterKind.Grayscale);
        Assert.DoesNotContain(opts, o => o.Kind == FilterKind.Beauty);
    }

    [Fact]
    public void AvailableFilters_All_Off_Still_Has_None()
    {
        var s = new AppSettings { FilterGrayscale = false, FilterBrightness = false, FilterBeauty = false };
        var opts = ResultViewModel.BuildFilterOptions(s);
        Assert.Single(opts);
        Assert.Equal(FilterKind.None, opts[0].Kind); // 원본은 절대 사라지지 않음
    }
}
