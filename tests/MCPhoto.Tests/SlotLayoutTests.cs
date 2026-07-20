using MCPhoto.Core.Frames;
using MCPhoto.Core.Models;

namespace MCPhoto.Tests;

/// <summary>WBS Step 10: 자동 배치·경계 클램프·겹침 검사·이미지 제한 검증.</summary>
public class SlotLayoutTests
{
    [Fact]
    public void AutoArrange_4_Slots_Is_2x2_Grid()
    {
        var slots = SlotLayout.AutoArrange(4, 1200, 1600); // 3:4 표준
        Assert.Equal(4, slots.Count);
        // 겹침 없음 + 경계 내
        Assert.True(SlotLayout.IsValid(slots, 1200, 1600));
        // 2×2: 첫 두 슬롯은 같은 Y(한 행), 다른 X
        Assert.Equal(slots[0].Y, slots[1].Y);
        Assert.NotEqual(slots[0].X, slots[1].X);
        Assert.True(slots[2].Y > slots[0].Y); // 두 번째 행
    }

    [Fact]
    public void AutoArrange_Vertical_Strip_Is_Single_Column()
    {
        // 1:4 세로 긴 스트립
        var slots = SlotLayout.AutoArrange(4, 400, 1600);
        Assert.Equal(4, slots.Count);
        Assert.True(SlotLayout.IsValid(slots, 400, 1600));
        // 1열: 모든 슬롯 같은 X
        Assert.All(slots, s => Assert.Equal(slots[0].X, s.X));
        // Y는 증가
        for (int i = 1; i < slots.Count; i++)
            Assert.True(slots[i].Y > slots[i - 1].Y);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void AutoArrange_Any_Count_Is_Valid(int count)
    {
        var slots = SlotLayout.AutoArrange(count, 1200, 1600);
        Assert.Equal(count, slots.Count);
        Assert.True(SlotLayout.IsValid(slots, 1200, 1600), $"{count}개 배치가 유효해야 함");
    }

    [Fact]
    public void ClampToFrame_Keeps_Slot_Inside()
    {
        var slot = new Slot { Index = 0, X = -50, Y = -30, Width = 2000, Height = 2000 };
        var clamped = SlotLayout.ClampToFrame(slot, 1000, 800);

        Assert.True(clamped.X >= 0 && clamped.Y >= 0);
        Assert.True(clamped.X + clamped.Width <= 1000);
        Assert.True(clamped.Y + clamped.Height <= 800);
    }

    [Fact]
    public void Overlaps_Detected()
    {
        var a = new Slot { X = 0, Y = 0, Width = 100, Height = 100 };
        var b = new Slot { X = 50, Y = 50, Width = 100, Height = 100 };
        Assert.True(SlotLayout.Overlaps(a, b));
    }

    [Fact]
    public void Touching_Edges_Not_Overlap()
    {
        var a = new Slot { X = 0, Y = 0, Width = 100, Height = 100 };
        var b = new Slot { X = 100, Y = 0, Width = 100, Height = 100 }; // 접촉만
        Assert.False(SlotLayout.Overlaps(a, b));
    }

    [Fact]
    public void IsValid_Rejects_Overlap()
    {
        var slots = new List<Slot>
        {
            new() { X = 0, Y = 0, Width = 100, Height = 100 },
            new() { X = 50, Y = 50, Width = 100, Height = 100 } // 겹침
        };
        Assert.False(SlotLayout.IsValid(slots, 1000, 1000));
    }

    [Fact]
    public void IsValid_Rejects_OutOfBounds()
    {
        var slots = new List<Slot> { new() { X = 900, Y = 0, Width = 200, Height = 100 } }; // 프레임 밖
        Assert.False(SlotLayout.IsValid(slots, 1000, 1000));
    }

    [Fact]
    public void IsValid_Rejects_Zero_And_Seven_Slots()
    {
        Assert.False(SlotLayout.IsValid(new List<Slot>(), 1000, 1000));
        var seven = Enumerable.Range(0, 7).Select(i => new Slot { X = i, Y = 0, Width = 1, Height = 1 }).ToList();
        Assert.False(SlotLayout.IsValid(seven, 1000, 1000));
    }

    // ── 이미지 제한 ──

    [Fact]
    public void Image_Size_Limit_10MB()
    {
        Assert.True(FrameImageValidator.IsSizeWithinLimit(5_000_000));
        Assert.False(FrameImageValidator.IsSizeWithinLimit(11_000_000));
    }

    [Fact]
    public void Image_LongSide_Over_4000_Scales_Down()
    {
        var (w, h) = FrameImageValidator.ScaledSize(8000, 4000);
        Assert.Equal(4000, Math.Max(w, h));
        Assert.Equal(2000, Math.Min(w, h)); // 비율 유지
    }

    [Fact]
    public void Image_Within_Limit_Not_Scaled()
    {
        var (w, h) = FrameImageValidator.ScaledSize(3000, 2000);
        Assert.Equal(3000, w);
        Assert.Equal(2000, h);
    }

    [Fact]
    public void Supported_Extensions()
    {
        Assert.True(FrameImageValidator.IsSupportedExtension("a.png"));
        Assert.True(FrameImageValidator.IsSupportedExtension("a.JPG"));
        Assert.True(FrameImageValidator.IsSupportedExtension("a.jpeg"));
        Assert.False(FrameImageValidator.IsSupportedExtension("a.gif"));
        Assert.False(FrameImageValidator.IsSupportedExtension("a.bmp"));
    }
}
