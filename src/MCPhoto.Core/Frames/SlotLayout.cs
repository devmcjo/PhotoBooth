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
        => AutoArrange(slotCount, frameW, frameH, targetAspect: null);

    /// <summary>
    /// 종횡비를 지정해 자동 배치(it4 §3). 격자 셀을 산출한 뒤 <b>각 셀 안에서 targetAspect를 유지하는
    /// 최대 사각형</b>을 셀 중앙에 배치한다. targetAspect=null이면 셀 크기 그대로(기존 동작).
    /// 캡처 크롭이 <see cref="Slot.AspectRatio"/>를 따르므로 이 비율이 결과물에 직결.
    /// </summary>
    public static List<Slot> AutoArrange(int slotCount, int frameW, int frameH, double? targetAspect)
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
            int cellX = marginX + c * (cellW + gapX);
            int cellY = marginY + r * (cellH + gapY);

            var (w, h, offX, offY) = FitInCell(cellW, cellH, targetAspect);
            slots.Add(new Slot
            {
                Index = i,
                X = cellX + offX,
                Y = cellY + offY,
                Width = w,
                Height = h
            });
        }
        return slots;
    }

    /// <summary>
    /// 셀(cellW×cellH) 안에서 targetAspect(=w/h)를 유지하는 최대 사각형과 중앙 정렬 오프셋을 산출.
    /// targetAspect=null이면 셀 크기 그대로(오프셋 0).
    /// </summary>
    private static (int w, int h, int offX, int offY) FitInCell(int cellW, int cellH, double? targetAspect)
    {
        if (targetAspect is not { } aspect || aspect <= 0)
            return (cellW, cellH, 0, 0);

        double cellAspect = (double)cellW / cellH;
        int w, h;
        if (cellAspect > aspect)
        {
            // 셀이 목표보다 가로로 넓음 → 높이를 셀에 맞추고 폭을 비율로.
            h = cellH;
            w = (int)Math.Round(h * aspect);
        }
        else
        {
            // 셀이 목표보다 세로로 김 → 폭을 셀에 맞추고 높이를 비율로.
            w = cellW;
            h = (int)Math.Round(w / aspect);
        }
        w = Math.Clamp(w, 1, cellW);
        h = Math.Clamp(h, 1, cellH);
        int offX = (cellW - w) / 2;
        int offY = (cellH - h) / 2;
        return (w, h, offX, offY);
    }

    /// <summary>
    /// 슬롯 폭을 기준으로 targetAspect를 유지하도록 높이를 재계산(비율 유지 리사이즈). (it4 §3.2 선택)
    /// 경계·중앙 정렬은 호출측에서 <see cref="ClampToFrame"/>로 마무리.
    /// </summary>
    public static Slot ResizeKeepingAspect(Slot slot, int newWidth, double targetAspect)
    {
        int w = Math.Max(1, newWidth);
        int h = targetAspect <= 0 ? slot.Height : Math.Max(1, (int)Math.Round(w / targetAspect));
        return new Slot { Index = slot.Index, X = slot.X, Y = slot.Y, Width = w, Height = h };
    }

    /// <summary>
    /// 모든 슬롯을 동일 배율로 일괄 스케일(it5 §8 F1). 각 슬롯 중심 유지·종횡비 유지(w·h 동일 배율)·경계 클램프.
    /// 누적 오차 방지를 위해 항상 <b>기준(원본) 슬롯</b>에서 계산해야 한다(호출측이 baseSlots 전달).
    /// </summary>
    public static List<Slot> ScaleSlots(IReadOnlyList<Slot> baseSlots, double factor, int frameW, int frameH)
    {
        var result = new List<Slot>(baseSlots.Count);
        foreach (var s in baseSlots)
        {
            int newW = Math.Max(1, (int)Math.Round(s.Width * factor));
            int newH = Math.Max(1, (int)Math.Round(s.Height * factor));
            double cx = s.X + s.Width / 2.0;
            double cy = s.Y + s.Height / 2.0;
            int newX = (int)Math.Round(cx - newW / 2.0);
            int newY = (int)Math.Round(cy - newH / 2.0);
            result.Add(ClampToFrame(
                new Slot { Index = s.Index, X = newX, Y = newY, Width = newW, Height = newH },
                frameW, frameH));
        }
        return result;
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
