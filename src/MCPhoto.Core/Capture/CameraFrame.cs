namespace MCPhoto.Core.Capture;

/// <summary>
/// 가공된 프리뷰 프레임(거울반전·중앙크롭 반영). BGR24 픽셀 버퍼.
/// UI 스레드로 마샬링되어 재사용 WriteableBitmap에 커밋된다. (architecture §2.2)
/// </summary>
public sealed class CameraFrame
{
    public int Width { get; init; }
    public int Height { get; init; }

    /// <summary>BGR24 인터리브 픽셀 버퍼. 길이 = Width * Height * 3.</summary>
    public byte[] Pixels { get; init; } = Array.Empty<byte>();

    /// <summary>한 행의 바이트 수(패딩 포함). 기본 = Width * 3.</summary>
    public int Stride { get; init; }
}
