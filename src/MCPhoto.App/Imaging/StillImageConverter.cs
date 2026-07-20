using System.Windows.Media;
using System.Windows.Media.Imaging;
using MCPhoto.Core.Capture;

namespace MCPhoto.App.Imaging;

/// <summary>CapturedStill(BGR24) ↔ WPF BitmapSource 변환(썸네일·미리보기용).</summary>
public static class StillImageConverter
{
    /// <summary>BGR24 스틸 → 고정 BitmapSource(썸네일 표시용). Freeze로 스레드 안전.</summary>
    public static BitmapSource ToBitmapSource(CapturedStill still)
    {
        int stride = still.Width * 3;
        var bmp = BitmapSource.Create(
            still.Width, still.Height,
            96, 96,
            PixelFormats.Bgr24, null,
            still.Pixels, stride);
        bmp.Freeze();
        return bmp;
    }

    /// <summary>PNG 바이트(QR 등) → BitmapImage.</summary>
    public static BitmapImage FromPngBytes(byte[] png)
    {
        var img = new BitmapImage();
        using var ms = new System.IO.MemoryStream(png);
        img.BeginInit();
        img.CacheOption = BitmapCacheOption.OnLoad;
        img.StreamSource = ms;
        img.EndInit();
        img.Freeze();
        return img;
    }

    /// <summary>이미지 파일 경로 → BitmapImage(합성 결과 미리보기).</summary>
    public static BitmapImage FromFile(string path)
    {
        var img = new BitmapImage();
        img.BeginInit();
        img.CacheOption = BitmapCacheOption.OnLoad;
        img.UriSource = new Uri(path, UriKind.Absolute);
        img.EndInit();
        img.Freeze();
        return img;
    }
}
