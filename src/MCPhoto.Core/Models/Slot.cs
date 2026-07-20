namespace MCPhoto.Core.Models;

/// <summary>
/// 프레임 내 사진 슬롯(칸). 좌표계는 프레임 원본 픽셀 기준. (architecture §3.1)
/// </summary>
public sealed class Slot
{
    public int Index { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }

    /// <summary>슬롯 종횡비(가로/세로). 캡처 중앙 크롭 ROI 계산 기준.</summary>
    public double AspectRatio => Height == 0 ? 0 : (double)Width / Height;
}
