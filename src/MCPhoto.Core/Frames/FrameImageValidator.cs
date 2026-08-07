namespace MCPhoto.Core.Frames;

/// <summary>
/// 프레임 이미지 제한 검증(장변 4000px·<b>8MB</b>). (PRD §F2, §9 #17)
/// <para>
/// ⚠️ <b>8MB는 서버 서명 조건과 같은 값이어야 한다</b>(설계 D-11). 서버는 서명 URL에
/// <c>x-goog-content-length-range: 0,8388608</c>을 넣어 GCS가 초과 업로드를 거부하게 하고,
/// 여기 클라 검증은 <b>즉시 피드백</b>용이다. 두 값이 어긋나면 사용자가 업로드 직전까지 갔다가 실패한다.
/// </para>
/// <para>
/// 개수 상한을 폐지했으므로(D-10) <b>이 크기 상한이 유일한 총량 방어선</b>이다. 프레임은 TTL 비대상
/// (영구 보관)이라 한 번 올라간 용량은 계속 비용이 된다.
/// </para>
/// </summary>
public static class FrameImageValidator
{
    public const int MaxLongSide = 4000;

    /// <summary>업로드 허용 최대 바이트(8MB). 서버 서명 조건 `x-goog-content-length-range`와 동일 값.</summary>
    public const long MaxBytes = 8L * 1024 * 1024; // 8MB

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
