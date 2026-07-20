using MCPhoto.Core.Models;

namespace MCPhoto.Core.Frames;

/// <summary>
/// 기본 프레임 소스 우선순위: ①DB isDefault → ②설치 Frame/ 번들 → ③fallback(코드 생성). (PRD §F2, §9 #11)
/// 이 클래스는 우선순위 결정과 fallback 스펙 생성을 담당(실제 이미지 렌더는 Capture/App).
/// </summary>
public static class DefaultFrameProvider
{
    /// <summary>fallback 프레임 스펙: 하양 배경·3:4 비율·슬롯 4개(2×2). (§9 #11)</summary>
    public const int FallbackWidth = 1200;
    public const int FallbackHeight = 1600; // 3:4
    public const int FallbackSlotCount = 4;

    /// <summary>기본 프레임 소스(우선순위 결정용).</summary>
    public enum FrameSource
    {
        /// <summary>①DB 등록 기본 프레임.</summary>
        Database,
        /// <summary>②설치 폴더 Frame/ 번들.</summary>
        Bundle,
        /// <summary>③fallback(코드 생성).</summary>
        Fallback
    }

    /// <summary>
    /// 우선순위 결정(§9 #11): DB 있으면 Database, 없고 번들 있으면 Bundle, 둘 다 없으면 Fallback.
    /// </summary>
    public static FrameSource SelectSource(bool hasDbFrames, bool hasBundleFrames)
    {
        if (hasDbFrames) return FrameSource.Database;
        if (hasBundleFrames) return FrameSource.Bundle;
        return FrameSource.Fallback;
    }

    /// <summary>
    /// fallback 프레임 템플릿(슬롯 좌표 포함). 이미지 자체는 App/Capture가 하양 배경으로 생성.
    /// </summary>
    public static FrameTemplate CreateFallbackTemplate(string imagePath)
    {
        var frame = new FrameTemplate
        {
            Id = "fallback",
            Name = "기본 프레임",
            IsDefault = true,
            UserId = null,
            ImageUrl = imagePath,
            ImageSize = new ImageSize { Width = FallbackWidth, Height = FallbackHeight }
        };

        // 2×2 격자, 여백/간격 균등. 슬롯 종횡비 3:4 유지.
        int margin = 80;
        int gap = 60;
        int cellW = (FallbackWidth - margin * 2 - gap) / 2;
        int cellH = (int)(cellW * 4.0 / 3.0);
        // 세로 중앙 정렬
        int totalH = cellH * 2 + gap;
        int top = (FallbackHeight - totalH) / 2;

        int[,] origin =
        {
            { margin, top },
            { margin + cellW + gap, top },
            { margin, top + cellH + gap },
            { margin + cellW + gap, top + cellH + gap }
        };

        for (int i = 0; i < FallbackSlotCount; i++)
            frame.Slots.Add(new Slot { Index = i, X = origin[i, 0], Y = origin[i, 1], Width = cellW, Height = cellH });

        return frame;
    }
}
