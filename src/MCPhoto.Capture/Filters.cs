using MCPhoto.Core.Capture;
using OpenCvSharp;

namespace MCPhoto.Capture;

/// <summary>
/// 기본 필터(전체 컷 일괄, 얼굴 인식 無). 흑백·밝기·뷰티. (PRD §F4)
/// 입력/출력 모두 BGR 3채널 Mat. None이면 원본 그대로.
/// </summary>
public static class Filters
{
    // ── 뷰티 파라미터(규격: docs/analysis/14 §7 필터 표와 동기화할 것) ──

    /// <summary>피부 영역 스무딩 블렌드 최대 비율. 마스크(0~1)에 곱해 픽셀별 가중치가 된다.</summary>
    private const double BeautySmoothStrength = 0.85;

    /// <summary>톤업 감마(&lt;1이면 중간톤 상승). 선형 밝기 상승보다 하이라이트가 덜 날아간다.</summary>
    private const double BeautyGamma = 0.88;

    /// <summary>채도 배율(혈색). 과하면 피부가 붉어지므로 소폭.</summary>
    private const double BeautySaturation = 1.08;

    /// <summary>언샵 강도(눈·입 디테일 복원). 스무딩으로 뭉개진 윤곽을 되살리는 용도.</summary>
    private const double BeautyUnsharpAmount = 0.22;

    /// <summary>필터를 적용한 새 Mat 반환(원본 불변). 호출자가 Dispose 책임.</summary>
    public static Mat Apply(Mat src, FilterKind filter)
    {
        return filter switch
        {
            FilterKind.Grayscale => Grayscale(src),
            FilterKind.Brightness => Brightness(src),
            FilterKind.Beauty => Beauty(src),
            _ => src.Clone()
        };
    }

    /// <summary>흑백: 그레이스케일 후 다시 3채널 BGR로(합성 채널 일관성).</summary>
    private static Mat Grayscale(Mat src)
    {
        using var gray = new Mat();
        Cv2.CvtColor(src, gray, ColorConversionCodes.BGR2GRAY);
        var bgr = new Mat();
        Cv2.CvtColor(gray, bgr, ColorConversionCodes.GRAY2BGR);
        return bgr;
    }

    /// <summary>밝기 +약한 대비: dst = src*alpha + beta.</summary>
    private static Mat Brightness(Mat src)
    {
        var dst = new Mat();
        // alpha=1.1(약한 대비), beta=20(밝기)
        Cv2.ConvertScaleAbs(src, dst, alpha: 1.1, beta: 20);
        return dst;
    }

    /// <summary>
    /// 뷰티: ① 피부톤 마스크 → ② 엣지 보존 스무딩을 피부에만 블렌드 → ③ 톤업(감마)·채도 → ④ 미세 언샵.
    /// <para>
    /// 종전 구현은 전면 bilateral(d=7 고정) + 60% 블렌드였고, 고해상도(1080p↑) 컷에서는 커널이 상대적으로
    /// 너무 작아 효과가 눈에 보이지 않았다("흑백·밝게와 구분이 안 된다"의 원인). 개선점은 세 가지다 —
    /// 커널을 <b>해상도에 비례</b>시키고(다운스케일 처리로 비용은 오히려 낮춤), 스무딩을 <b>피부 영역에만</b>
    /// 적용해 눈·머리카락·배경 디테일을 지키고, 톤업·채도·언샵을 더해 "보정했다"가 분명히 읽히게 한다.
    /// </para>
    /// ⚠️ 형상은 건드리지 않는다(얼굴 슬리밍 등 워프는 미지원 — 오검출 시 얼굴이 왜곡되는 되돌릴 수 없는 실패).
    /// </summary>
    private static Mat Beauty(Mat src)
    {
        using Mat skin = SkinMask(src);                       // ① 0~1 소프트 마스크
        using Mat smooth = EdgePreservingSmooth(src);         // ② 피부 결 제거(엣지 보존)
        using Mat blended = BlendByMask(src, smooth, skin, BeautySmoothStrength);
        using Mat toned = ToneUp(blended);                    // ③-1 감마 톤업
        using Mat saturated = Saturate(toned, BeautySaturation); // ③-2 혈색
        return Unsharp(saturated, BeautyUnsharpAmount);       // ④ 디테일 복원
    }

    /// <summary>
    /// 피부 영역 마스크(0~1 float, 소프트 에지). YCrCb의 고전적 피부 범위(Cr 133~173, Cb 77~127)를
    /// 쓴다 — 조명·피부색 변화에 비교적 견고하고 모델 파일이 필요 없다(오프라인 단일 exe 배포 제약).
    /// 커널 크기는 해상도에 비례한다(고정값은 고해상도에서 무력해진다).
    /// </summary>
    private static Mat SkinMask(Mat bgr)
    {
        using var ycrcb = new Mat();
        Cv2.CvtColor(bgr, ycrcb, ColorConversionCodes.BGR2YCrCb);

        using var mask = new Mat();
        Cv2.InRange(ycrcb, new Scalar(0, 133, 77), new Scalar(255, 173, 127), mask);

        int k = OddAtLeast(Math.Min(bgr.Width, bgr.Height) / 160, 3);
        using var kernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(k, k));
        Cv2.MorphologyEx(mask, mask, MorphTypes.Open, kernel);   // 잡티(오검출 점) 제거
        Cv2.MorphologyEx(mask, mask, MorphTypes.Close, kernel);  // 구멍 메우기

        // 경계선이 드러나지 않도록 마스크를 흐린다 → 블렌드가 자연스럽게 이어진다.
        int blur = OddAtLeast(k * 3, 3);
        Cv2.GaussianBlur(mask, mask, new Size(blur, blur), 0);

        var soft = new Mat();
        mask.ConvertTo(soft, MatType.CV_32FC1, 1.0 / 255.0);
        return soft;
    }

    /// <summary>
    /// 엣지 보존 스무딩. <b>절반 해상도에서 bilateral 2회</b> 후 업스케일한다 —
    /// 원본 해상도에서 큰 커널을 쓰는 것과 시각적으로 같고 연산량은 1/4 수준이다(합성은 슬롯 수만큼 반복된다).
    /// </summary>
    private static Mat EdgePreservingSmooth(Mat bgr)
    {
        var halfSize = new Size(Math.Max(1, bgr.Width / 2), Math.Max(1, bgr.Height / 2));

        using var small = new Mat();
        Cv2.Resize(bgr, small, halfSize, 0, 0, InterpolationFlags.Area);

        using var pass1 = new Mat();
        Cv2.BilateralFilter(small, pass1, d: 9, sigmaColor: 60, sigmaSpace: 9);
        using var pass2 = new Mat();
        // 2회 적용: 1회로는 피부 결이 남는다(sigmaColor를 낮춰 윤곽 보존).
        Cv2.BilateralFilter(pass1, pass2, d: 9, sigmaColor: 45, sigmaSpace: 9);

        var result = new Mat();
        Cv2.Resize(pass2, result, bgr.Size(), 0, 0, InterpolationFlags.Linear);
        return result;
    }

    /// <summary>
    /// 마스크 가중 블렌드: dst = src*(1-w) + overlay*w, w = mask01 * strength(픽셀별).
    /// </summary>
    private static Mat BlendByMask(Mat src, Mat overlay, Mat mask01, double strength)
    {
        using Mat weight = mask01 * strength;                 // CV_32FC1
        using var weight3 = new Mat();
        Cv2.CvtColor(weight, weight3, ColorConversionCodes.GRAY2BGR);

        using var one = new Mat(weight3.Size(), MatType.CV_32FC3, Scalar.All(1.0));
        using var inverse = new Mat();
        Cv2.Subtract(one, weight3, inverse);

        using var srcF = new Mat();
        src.ConvertTo(srcF, MatType.CV_32FC3);
        using var overlayF = new Mat();
        overlay.ConvertTo(overlayF, MatType.CV_32FC3);

        using var keep = new Mat();
        Cv2.Multiply(srcF, inverse, keep);
        using var add = new Mat();
        Cv2.Multiply(overlayF, weight3, add);
        using var sum = new Mat();
        Cv2.Add(keep, add, sum);

        var dst = new Mat();
        sum.ConvertTo(dst, MatType.CV_8UC3);
        return dst;
    }

    /// <summary>감마 LUT 톤업(중간톤만 올려 하이라이트 보존).</summary>
    private static Mat ToneUp(Mat bgr)
    {
        using var lut = new Mat(1, 256, MatType.CV_8UC1);
        var idx = lut.GetGenericIndexer<byte>();
        for (int i = 0; i < 256; i++)
            idx[0, i] = (byte)Math.Clamp(Math.Round(Math.Pow(i / 255.0, BeautyGamma) * 255.0), 0, 255);

        var dst = new Mat();
        Cv2.LUT(bgr, lut, dst);
        return dst;
    }

    /// <summary>HSV의 S만 배율 적용(포화 연산 — 8U라 자동 클램프).</summary>
    private static Mat Saturate(Mat bgr, double factor)
    {
        using var hsv = new Mat();
        Cv2.CvtColor(bgr, hsv, ColorConversionCodes.BGR2HSV);

        var channels = Cv2.Split(hsv);
        try
        {
            using Mat boosted = channels[1] * factor;
            boosted.CopyTo(channels[1]);
            using var merged = new Mat();
            Cv2.Merge(channels, merged);

            var dst = new Mat();
            Cv2.CvtColor(merged, dst, ColorConversionCodes.HSV2BGR);
            return dst;
        }
        finally
        {
            foreach (var c in channels) c.Dispose();
        }
    }

    /// <summary>언샵 마스크(약하게): dst = src*(1+amount) - blur*amount. 스무딩으로 잃은 눈매를 되살린다.</summary>
    private static Mat Unsharp(Mat bgr, double amount)
    {
        int k = OddAtLeast(Math.Min(bgr.Width, bgr.Height) / 200, 3);
        using var blurred = new Mat();
        Cv2.GaussianBlur(bgr, blurred, new Size(k, k), 0);

        var dst = new Mat();
        Cv2.AddWeighted(bgr, 1.0 + amount, blurred, -amount, 0, dst);
        return dst;
    }

    /// <summary>커널용 홀수 크기(최소값 보장). OpenCV의 홀수 커널 요구를 한 곳에서 만족시킨다.</summary>
    private static int OddAtLeast(int value, int minimum)
    {
        int v = Math.Max(value, minimum);
        return (v % 2 == 0) ? v + 1 : v;
    }
}
