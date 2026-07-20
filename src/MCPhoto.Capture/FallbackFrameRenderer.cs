using System.IO;
using MCPhoto.Core.Frames;
using MCPhoto.Core.Models;
using OpenCvSharp;

namespace MCPhoto.Capture;

/// <summary>
/// fallback 기본 프레임 이미지(하양 배경·3:4·4슬롯)를 코드로 렌더링. (PRD §9 #11, architecture §3.2)
/// DB/번들 프레임이 모두 없을 때 사용.
/// </summary>
public static class FallbackFrameRenderer
{
    /// <summary>fallback 프레임 이미지를 outputPath에 생성하고 템플릿 반환.</summary>
    public static FrameTemplate Create(string outputPath)
    {
        var template = DefaultFrameProvider.CreateFallbackTemplate(outputPath);

        using var canvas = new Mat(
            DefaultFrameProvider.FallbackHeight,
            DefaultFrameProvider.FallbackWidth,
            MatType.CV_8UC3,
            new Scalar(255, 255, 255)); // 하양 배경

        // 슬롯 자리(연회색 사각형 + 테두리)로 시각적 가이드
        foreach (var slot in template.Slots)
        {
            var rect = new Rect(slot.X, slot.Y, slot.Width, slot.Height);
            Cv2.Rectangle(canvas, rect, new Scalar(235, 235, 235), thickness: -1);
            Cv2.Rectangle(canvas, rect, new Scalar(200, 200, 200), thickness: 2);
        }

        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        Cv2.ImWrite(outputPath, canvas);

        return template;
    }
}
