using MCPhoto.Core.Models;

namespace MCPhoto.Core.Frames;

/// <summary>
/// 슬롯 자동 배치·경계 제약·겹침 검사(순수 로직, 테스트 대상). (PRD §F2, §9 #15, architecture §3.3)
/// 표준 비율=격자, 세로 스트립=1열. 슬롯이 프레임 밖으로 나가거나 겹치지 않도록 클램프.
/// </summary>
public static class SlotLayout
{
    /// <summary>
    /// 슬롯 개수(1~6)에 따라 프레임 크기에 맞춰 자동 배치. (§9 #15)
    /// 세로로 긴 프레임(aspect &lt; 0.6)은 1열 스트립, 그 외는 격자.
    /// </summary>
    public static List<Slot> AutoArrange(int slotCount, int frameW, int frameH)
    {
        slotCount = Math.Clamp(slotCount, 1, 6);
        double frameAspect = (double)frameW / frameH;

        // 세로 긴 스트립(1:3, 1:4 등) → 1열
        bool verticalStrip = frameAspect < 0.6;

        int cols, rows;
        if (verticalStrip)
        {
            cols = 1;
            rows = slotCount;
        }
        else
        {
            // 격자: 4=2×2, 6=2×3, 2=1×2 등
            cols = slotCount switch { 1 => 1, 2 => 2, 3 => 3, 4 => 2, 5 => 3, 6 => 3, _ => 2 };
            rows = (int)Math.Ceiling(slotCount / (double)cols);
        }

        int marginX = Math.Max(20, frameW / 20);
        int marginY = Math.Max(20, frameH / 20);
        int gapX = Math.Max(12, frameW / 40);
        int gapY = Math.Max(12, frameH / 40);

        int cellW = (frameW - marginX * 2 - gapX * (cols - 1)) / cols;
        int cellH = (frameH - marginY * 2 - gapY * (rows - 1)) / rows;

        var slots = new List<Slot>();
        for (int i = 0; i < slotCount; i++)
        {
            int r = i / cols;
            int c = i % cols;
            slots.Add(new Slot
            {
                Index = i,
                X = marginX + c * (cellW + gapX),
                Y = marginY + r * (cellH + gapY),
                Width = cellW,
                Height = cellH
            });
        }
        return slots;
    }

    /// <summary>슬롯을 프레임 경계 내로 클램프(프레임 밖 이탈 방지). 좌표·크기 모두 보정.</summary>
    public static Slot ClampToFrame(Slot slot, int frameW, int frameH)
    {
        int w = Math.Clamp(slot.Width, 1, frameW);
        int h = Math.Clamp(slot.Height, 1, frameH);
        int x = Math.Clamp(slot.X, 0, frameW - w);
        int y = Math.Clamp(slot.Y, 0, frameH - h);
        return new Slot { Index = slot.Index, X = x, Y = y, Width = w, Height = h };
    }

    /// <summary>두 슬롯이 겹치는지(경계 접촉은 겹침 아님).</summary>
    public static bool Overlaps(Slot a, Slot b)
    {
        return a.X < b.X + b.Width
            && a.X + a.Width > b.X
            && a.Y < b.Y + b.Height
            && a.Y + a.Height > b.Y;
    }

    /// <summary>슬롯 목록에 겹침이 있는지.</summary>
    public static bool HasAnyOverlap(IReadOnlyList<Slot> slots)
    {
        for (int i = 0; i < slots.Count; i++)
            for (int j = i + 1; j < slots.Count; j++)
                if (Overlaps(slots[i], slots[j])) return true;
        return false;
    }

    /// <summary>저장 가능 여부: 모든 슬롯이 경계 내 + 겹침 없음 + 개수 1~6.</summary>
    public static bool IsValid(IReadOnlyList<Slot> slots, int frameW, int frameH)
    {
        if (slots.Count is < 1 or > 6) return false;
        foreach (var s in slots)
        {
            if (s.X < 0 || s.Y < 0) return false;
            if (s.X + s.Width > frameW || s.Y + s.Height > frameH) return false;
            if (s.Width < 1 || s.Height < 1) return false;
        }
        return !HasAnyOverlap(slots);
    }
}
