using MCPhoto.Core.Capture;
using OpenCvSharp;

namespace MCPhoto.Capture;

/// <summary>
/// 기본 필터(전체 컷 일괄, 얼굴 인식 無). 흑백·밝기·간단 뷰티. (PRD §F4)
/// 입력/출력 모두 BGR 3채널 Mat. None이면 원본 그대로.
/// </summary>
public static class Filters
{
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

    /// <summary>간단 뷰티: 경량 소프트닝(bilateral, 엣지 보존) + 약한 톤 보정.</summary>
    private static Mat Beauty(Mat src)
    {
        using var smooth = new Mat();
        // bilateral: 피부 스무딩하되 윤곽 보존
        Cv2.BilateralFilter(src, smooth, d: 7, sigmaColor: 40, sigmaSpace: 7);
        // 원본과 블렌드해 과하지 않게(소프트닝 60%)
        using var blended = new Mat();
        Cv2.AddWeighted(smooth, 0.6, src, 0.4, 0, blended);
        // 약한 톤 보정(밝기 소폭)
        var dst = new Mat();
        Cv2.ConvertScaleAbs(blended, dst, alpha: 1.03, beta: 6);
        return dst;
    }
}
