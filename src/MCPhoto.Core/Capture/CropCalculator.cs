namespace MCPhoto.Core.Capture;

/// <summary>중앙 크롭 사각형(픽셀). OpenCvSharp Rect와 독립(테스트 가능한 순수 값).</summary>
public readonly record struct CropRect(int X, int Y, int Width, int Height);

/// <summary>
/// 슬롯 종횡비 중앙 크롭 ROI 계산(왜곡 없이 잘라내기). (architecture §2.2, PRD §F1)
/// 카메라 원본 종횡비와 목표(대표 슬롯) 종횡비를 비교해 좌우 또는 상하를 중앙 기준으로 잘라낸다.
/// </summary>
public static class CropCalculator
{
    /// <summary>
    /// 원본(srcWidth×srcHeight)을 targetAspect(가로/세로)에 맞춰 중앙 크롭할 Rect 산출.
    /// - 원본이 목표보다 가로로 넓으면 좌우를 잘라냄(세로 슬롯).
    /// - 원본이 목표보다 세로로 길면 상하를 잘라냄(가로 슬롯).
    /// targetAspect ≤ 0 이면 크롭 없이 전체 반환.
    /// </summary>
    public static CropRect CenterCrop(int srcWidth, int srcHeight, double targetAspect)
    {
        if (srcWidth <= 0 || srcHeight <= 0)
            return new CropRect(0, 0, Math.Max(0, srcWidth), Math.Max(0, srcHeight));

        if (targetAspect <= 0)
            return new CropRect(0, 0, srcWidth, srcHeight);

        double srcAspect = (double)srcWidth / srcHeight;

        int cropW, cropH;
        if (srcAspect > targetAspect)
        {
            // 원본이 더 넓음 → 높이 유지, 폭 축소(좌우 잘라냄)
            cropH = srcHeight;
            cropW = (int)Math.Round(srcHeight * targetAspect);
        }
        else
        {
            // 원본이 더 좁음/길음 → 폭 유지, 높이 축소(상하 잘라냄)
            cropW = srcWidth;
            cropH = (int)Math.Round(srcWidth / targetAspect);
        }

        // 경계 보정(반올림 오차로 원본 초과 방지)
        cropW = Math.Clamp(cropW, 1, srcWidth);
        cropH = Math.Clamp(cropH, 1, srcHeight);

        int x = (srcWidth - cropW) / 2;
        int y = (srcHeight - cropH) / 2;

        return new CropRect(x, y, cropW, cropH);
    }
}
