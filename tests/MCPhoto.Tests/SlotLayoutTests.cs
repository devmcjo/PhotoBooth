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

    // ── it4 Step 2 (B4): 종횡비 지정 배치 ──

    [Theory]
    [InlineData(SlotAspect.Ratio4x3)]
    [InlineData(SlotAspect.Ratio3x4)]
    [InlineData(SlotAspect.Ratio1x1)]
    public void AutoArrange_With_Aspect_Keeps_Ratio_And_Valid(SlotAspect aspect)
    {
        double target = aspect.ToRatio();
        var slots = SlotLayout.AutoArrange(4, 1200, 1600, target);

        Assert.Equal(4, slots.Count);
        Assert.True(SlotLayout.IsValid(slots, 1200, 1600), $"{aspect} 배치가 유효해야 함");
        foreach (var s in slots)
        {
            double ratio = (double)s.Width / s.Height;
            // 정수 반올림·클램프 여유로 소폭 오차 허용(±2%).
            Assert.True(Math.Abs(ratio - target) / target < 0.02,
                $"{aspect}: 슬롯 비율 {ratio:F3} 이 목표 {target:F3} 에 근접해야 함");
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void AutoArrange_With_Aspect_Any_Count_Valid(int count)
    {
        var slots = SlotLayout.AutoArrange(count, 1200, 1600, SlotAspect.Ratio1x1.ToRatio());
        Assert.Equal(count, slots.Count);
        Assert.True(SlotLayout.IsValid(slots, 1200, 1600), $"{count}개 정사각 배치가 유효해야 함");
    }

    [Fact]
    public void AutoArrange_Null_Aspect_Matches_Legacy_Overload()
    {
        // targetAspect=null 오버로드는 기존 무인자 동작과 동일해야(하위호환).
        var legacy = SlotLayout.AutoArrange(4, 1200, 1600);
        var explicitNull = SlotLayout.AutoArrange(4, 1200, 1600, targetAspect: null);
        Assert.Equal(legacy.Count, explicitNull.Count);
        for (int i = 0; i < legacy.Count; i++)
        {
            Assert.Equal(legacy[i].X, explicitNull[i].X);
            Assert.Equal(legacy[i].Y, explicitNull[i].Y);
            Assert.Equal(legacy[i].Width, explicitNull[i].Width);
            Assert.Equal(legacy[i].Height, explicitNull[i].Height);
        }
    }

    [Fact]
    public void AutoArrange_Square_Aspect_Produces_Square_Slots()
    {
        var slots = SlotLayout.AutoArrange(4, 1200, 1600, SlotAspect.Ratio1x1.ToRatio());
        foreach (var s in slots)
            Assert.True(Math.Abs(s.Width - s.Height) <= 1, $"정사각이어야: {s.Width}×{s.Height}");
    }

    [Fact]
    public void ResizeKeepingAspect_Derives_Height_From_Width()
    {
        var slot = new Slot { Index = 0, X = 10, Y = 10, Width = 100, Height = 100 };
        var resized = SlotLayout.ResizeKeepingAspect(slot, 400, 4.0 / 3.0);
        Assert.Equal(400, resized.Width);
        Assert.Equal(300, resized.Height); // 400 / (4/3)
        Assert.Equal(10, resized.X);        // 위치 불변
    }

    // ── it5 Step 7 (F1): 슬롯 일괄 스케일 ──

    [Theory]
    [InlineData(0.7)]  // 축소 — 경계 클램프 없음(중심 유지 엄격 검증)
    [InlineData(1.0)]  // 등배
    public void ScaleSlots_Shrink_Scales_Size_And_Keeps_Center(double factor)
    {
        var baseSlots = SlotLayout.AutoArrange(4, 1200, 1600, SlotAspect.Ratio3x4.ToRatio());
        var scaled = SlotLayout.ScaleSlots(baseSlots, factor, 1200, 1600);

        Assert.Equal(baseSlots.Count, scaled.Count);
        for (int i = 0; i < baseSlots.Count; i++)
        {
            var b = baseSlots[i];
            var s = scaled[i];
            // 크기 factor배(±1px 반올림 여유).
            Assert.True(Math.Abs(s.Width - b.Width * factor) <= 1.5, $"폭 {s.Width} ≈ {b.Width * factor}");
            Assert.True(Math.Abs(s.Height - b.Height * factor) <= 1.5, $"높이 {s.Height} ≈ {b.Height * factor}");
            // 중심 유지(축소는 경계를 안 넘으므로 엄격, ±1.5px 반올림 여유).
            double bc = b.X + b.Width / 2.0, sc = s.X + s.Width / 2.0;
            double bcy = b.Y + b.Height / 2.0, scy = s.Y + s.Height / 2.0;
            Assert.True(Math.Abs(bc - sc) <= 1.5, $"중심 X {sc} ≈ {bc}");
            Assert.True(Math.Abs(bcy - scy) <= 1.5, $"중심 Y {scy} ≈ {bcy}");
        }
    }

    [Fact]
    public void ScaleSlots_Enlarge_Scales_Size_Within_Bounds()
    {
        // 확대(1.3)는 가장자리 슬롯이 경계에 닿으면 클램프 우선(중심 이동 허용) — 크기 배율·경계 내만 보장.
        var baseSlots = SlotLayout.AutoArrange(4, 1200, 1600, SlotAspect.Ratio3x4.ToRatio());
        var scaled = SlotLayout.ScaleSlots(baseSlots, 1.3, 1200, 1600);

        Assert.Equal(baseSlots.Count, scaled.Count);
        for (int i = 0; i < baseSlots.Count; i++)
        {
            var s = scaled[i];
            // 경계 내(클램프 보장)
            Assert.True(s.X >= 0 && s.Y >= 0);
            Assert.True(s.X + s.Width <= 1200 && s.Y + s.Height <= 1600);
            // 크기는 최소한 원본 이상(확대이므로), 프레임 한도 내
            Assert.True(s.Width >= baseSlots[i].Width);
            Assert.True(s.Height >= baseSlots[i].Height);
        }
    }

    [Fact]
    public void ScaleSlots_Keeps_Aspect_Ratio()
    {
        var baseSlots = SlotLayout.AutoArrange(4, 1200, 1600, SlotAspect.Ratio4x3.ToRatio());
        var scaled = SlotLayout.ScaleSlots(baseSlots, 1.25, 1200, 1600);
        for (int i = 0; i < baseSlots.Count; i++)
        {
            double baseRatio = (double)baseSlots[i].Width / baseSlots[i].Height;
            double scaledRatio = (double)scaled[i].Width / scaled[i].Height;
            Assert.True(Math.Abs(baseRatio - scaledRatio) / baseRatio < 0.03,
                $"종횡비 유지: {scaledRatio:F3} ≈ {baseRatio:F3}");
        }
    }

    [Fact]
    public void ScaleSlots_Clamps_Within_Frame()
    {
        // 프레임을 꽉 채운 단일 슬롯을 확대 → 경계 클램프로 프레임 안에 머무름.
        var baseSlots = new List<Slot> { new() { Index = 0, X = 100, Y = 100, Width = 1000, Height = 1400 } };
        var scaled = SlotLayout.ScaleSlots(baseSlots, 1.3, 1200, 1600);
        var s = scaled[0];
        Assert.True(s.X >= 0 && s.Y >= 0);
        Assert.True(s.X + s.Width <= 1200);
        Assert.True(s.Y + s.Height <= 1600);
    }

    [Fact]
    public void ScaleSlots_Does_Not_Mutate_Base()
    {
        var baseSlots = SlotLayout.AutoArrange(2, 1200, 1600, SlotAspect.Ratio1x1.ToRatio());
        var w0 = baseSlots[0].Width;
        _ = SlotLayout.ScaleSlots(baseSlots, 1.3, 1200, 1600);
        Assert.Equal(w0, baseSlots[0].Width); // 원본 불변(새 리스트 반환)
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
