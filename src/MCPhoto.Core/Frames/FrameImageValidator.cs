namespace MCPhoto.Core.Frames;

/// <summary>프레임 이미지 제한 검증(장변 4000px·10MB). (PRD §F2, §9 #17)</summary>
public static class FrameImageValidator
{
    public const int MaxLongSide = 4000;
    public const long MaxBytes = 10L * 1024 * 1024; // 10MB

    /// <summary>파일 크기 검사.</summary>
    public static bool IsSizeWithinLimit(long byteLength) => byteLength <= MaxBytes;

    /// <summary>장변 4000px 초과 시 축소 배율(1.0=축소 불필요).</summary>
    public static double ResizeFactor(int width, int height)
    {
        int longSide = Math.Max(width, height);
        if (longSide <= MaxLongSide) return 1.0;
        return (double)MaxLongSide / longSide;
    }

    /// <summary>축소 후 크기 산출.</summary>
    public static (int width, int height) ScaledSize(int width, int height)
    {
        double f = ResizeFactor(width, height);
        if (f >= 1.0) return (width, height);
        return ((int)Math.Round(width * f), (int)Math.Round(height * f));
    }

    /// <summary>지원 확장자(PNG/JPG/JPEG).</summary>
    public static bool IsSupportedExtension(string path)
    {
        var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        return ext is ".png" or ".jpg" or ".jpeg";
    }
}
