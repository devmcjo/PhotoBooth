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

    // ── 뷰티 개선: 피부 영역만 스무딩 + 톤업·채도 (효과가 눈에 보이는 수준인지 수치로 고정) ──

    /// <summary>왼쪽 절반 = 피부톤(YCrCb 범위 안), 오른쪽 절반 = 청록(범위 밖). 양쪽에 같은 격자 노이즈.</summary>
    private static Mat MakeSkinAndNonSkin()
    {
        const int size = 160;
        var m = new Mat(size, size, MatType.CV_8UC3);
        var idx = m.GetGenericIndexer<Vec3b>();
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                // 체커 노이즈(±28) — 스무딩이 걸리면 표준편차가 내려간다.
                int n = ((x / 2 + y / 2) % 2 == 0) ? 28 : -28;
                Vec3b p = x < size / 2
                    ? new Vec3b(Clamp(140 + n), Clamp(170 + n), Clamp(210 + n))   // 피부톤 BGR
                    : new Vec3b(Clamp(200 + n), Clamp(180 + n), Clamp(60 + n));   // 비피부(청록)
                idx[y, x] = p;
            }
        return m;
    }

    private static byte Clamp(int v) => (byte)Math.Clamp(v, 0, 255);

    /// <summary>ROI의 그레이 표준편차(디테일·노이즈 양의 지표).</summary>
    private static double StdDevIn(Mat bgr, Rect roi)
    {
        using var sub = new Mat(bgr, roi);
        using var gray = new Mat();
        Cv2.CvtColor(sub, gray, ColorConversionCodes.BGR2GRAY);
        Cv2.MeanStdDev(gray, out _, out Scalar sd);
        return sd.Val0;
    }

    [Fact]
    public void Beauty_Smooths_Skin_But_Keeps_NonSkin_Detail()
    {
        using var src = MakeSkinAndNonSkin();
        using var dst = Filters.Apply(src, FilterKind.Beauty);

        // 마스크 소프트 에지가 경계를 넘지 않도록 각 영역 안쪽만 측정.
        var skinRoi = new Rect(10, 10, 60, 140);
        var otherRoi = new Rect(90, 10, 60, 140);

        double skinBefore = StdDevIn(src, skinRoi), skinAfter = StdDevIn(dst, skinRoi);
        double otherBefore = StdDevIn(src, otherRoi), otherAfter = StdDevIn(dst, otherRoi);

        // 피부 영역은 확실히 매끄러워진다(종전 구현은 이 감소폭이 미미해 "구분이 안 된다"는 평가를 받았다).
        Assert.True(skinAfter < skinBefore * 0.5,
            $"피부 영역 스무딩 부족: {skinBefore:F1} → {skinAfter:F1}");

        // 비피부(배경·머리카락에 해당)는 피부만큼 뭉개지지 않는다 — 마스크가 실제로 작동하는지의 증거.
        double skinDrop = 1 - skinAfter / skinBefore;
        double otherDrop = 1 - otherAfter / otherBefore;
        Assert.True(skinDrop > otherDrop,
            $"피부 감소율({skinDrop:P0})이 비피부({otherDrop:P0})보다 커야 마스크가 작동하는 것");
    }

    [Fact]
    public void Beauty_Tones_Up_Skin_Region()
    {
        using var src = MakeSkinAndNonSkin();
        using var dst = Filters.Apply(src, FilterKind.Beauty);

        var skinRoi = new Rect(10, 10, 60, 140);
        using var before = new Mat(src, skinRoi);
        using var after = new Mat(dst, skinRoi);

        // 감마 톤업(0.88)이 중간톤을 올린다 → 피부가 밝아진다.
        Assert.True(MeanBrightness(after) > MeanBrightness(before) + 2,
            $"톤업 부족: {MeanBrightness(before):F1} → {MeanBrightness(after):F1}");
    }

    [Fact]
    public void Beauty_Is_Distinguishable_From_Brightness_And_Grayscale()
    {
        // 세 필터가 서로 충분히 다른 결과를 내야 사용자가 구분할 수 있다(요청의 핵심).
        using var src = MakeSkinAndNonSkin();
        using var beauty = Filters.Apply(src, FilterKind.Beauty);
        using var bright = Filters.Apply(src, FilterKind.Brightness);
        using var gray = Filters.Apply(src, FilterKind.Grayscale);

        Assert.True(MeanAbsDiff(beauty, bright) > 5, "뷰티와 밝게가 사실상 같은 결과다");
        Assert.True(MeanAbsDiff(beauty, gray) > 5, "뷰티와 흑백이 사실상 같은 결과다");
    }

    private static double MeanAbsDiff(Mat a, Mat b)
    {
        using var diff = new Mat();
        Cv2.Absdiff(a, b, diff);
        return Cv2.Mean(diff).Val0;
    }

    [Fact]
    public void Beauty_Handles_Tiny_Images_Without_Throwing()
    {
        // 커널·다운스케일이 해상도 비례라 1px 이미지에서도 커널 크기 규칙(홀수·최소 3)이 깨지지 않아야 한다.
        foreach (int size in new[] { 1, 2, 3, 7 })
        {
            using var tiny = new Mat(size, size, MatType.CV_8UC3, new Scalar(140, 170, 210));
            using var dst = Filters.Apply(tiny, FilterKind.Beauty);
            Assert.Equal(size, dst.Width);
            Assert.Equal(size, dst.Height);
            Assert.Equal(MatType.CV_8UC3, dst.Type());
        }
    }

    [Fact]
    public void Beauty_Preserves_Size_And_Type()
    {
        using var src = MakeSkinAndNonSkin();
        using var dst = Filters.Apply(src, FilterKind.Beauty);
        Assert.Equal(src.Size(), dst.Size());
        Assert.Equal(MatType.CV_8UC3, dst.Type());   // 합성이 BGR 3채널을 전제한다
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
