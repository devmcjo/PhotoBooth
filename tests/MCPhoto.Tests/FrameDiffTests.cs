using MCPhoto.Core.Frames;
using MCPhoto.Core.Models;
using Xunit;

namespace MCPhoto.Tests;

/// <summary>item2 Step 3: 이미지·슬롯·이름 diff 판정(경계·보수 판정 포함) 회귀.</summary>
public class FrameDiffTests
{
    private static List<Slot> Slots(params (int i, int x, int y, int w, int h)[] items)
        => items.Select(t => new Slot { Index = t.i, X = t.x, Y = t.y, Width = t.w, Height = t.h }).ToList();

    private static readonly byte[] ImgA = { 1, 2, 3, 4, 5 };
    private static readonly byte[] ImgA2 = { 1, 2, 3, 4, 5 }; // 동일 바이트, 다른 인스턴스
    private static readonly byte[] ImgB = { 1, 2, 3, 4, 9 };  // 마지막 바이트만 다름
    private static readonly byte[] ImgC = { 1, 2, 3 };        // 길이 다름

    [Fact]
    public void No_Change_When_All_Same()
    {
        var slots = Slots((0, 10, 10, 100, 100), (1, 200, 10, 100, 100));
        var change = FrameDiff.Compare(ImgA, ImgA2, slots, slots, "name", "name");
        Assert.False(change.HasAnyChange);
        Assert.False(change.ImageChanged);
        Assert.False(change.SlotsChanged);
        Assert.False(change.NameChanged);
    }

    [Fact]
    public void ImageChanged_When_Bytes_Differ()
    {
        var slots = Slots((0, 10, 10, 100, 100));
        var change = FrameDiff.Compare(ImgA, ImgB, slots, slots, "n", "n");
        Assert.True(change.ImageChanged);
        Assert.True(change.HasAnyChange);
        Assert.False(change.SlotsChanged);
    }

    [Fact]
    public void ImageChanged_When_Length_Differs()
        => Assert.True(FrameDiff.ImageEqual(ImgA, ImgC) == false);

    [Fact]
    public void ImageChanged_When_Original_Null_Conservative()
    {
        // 원본 확보 실패(null) → 변경으로 간주(C3 보수).
        var slots = Slots((0, 0, 0, 10, 10));
        var change = FrameDiff.Compare(null, ImgA, slots, slots, "n", "n");
        Assert.True(change.ImageChanged);
    }

    [Fact]
    public void ImageEqual_Both_Null_Is_Equal()
        => Assert.True(FrameDiff.ImageEqual(null, null));

    [Fact]
    public void SlotsChanged_When_One_Pixel_Moved()
    {
        var original = Slots((0, 10, 10, 100, 100));
        var edited = Slots((0, 11, 10, 100, 100)); // X +1px
        var change = FrameDiff.Compare(ImgA, ImgA2, original, edited, "n", "n");
        Assert.True(change.SlotsChanged);
        Assert.False(change.ImageChanged);
    }

    [Fact]
    public void SlotsChanged_When_Count_Differs()
    {
        var original = Slots((0, 10, 10, 100, 100));
        var edited = Slots((0, 10, 10, 100, 100), (1, 200, 10, 100, 100));
        Assert.False(FrameDiff.SlotsEqual(original, edited));
    }

    [Fact]
    public void SlotsEqual_Ignores_Order_By_Index()
    {
        var a = Slots((0, 10, 10, 50, 50), (1, 100, 10, 50, 50));
        var b = Slots((1, 100, 10, 50, 50), (0, 10, 10, 50, 50)); // 순서만 다름
        Assert.True(FrameDiff.SlotsEqual(a, b));
    }

    [Fact]
    public void NameChanged_When_Name_Differs()
    {
        var slots = Slots((0, 0, 0, 10, 10));
        var change = FrameDiff.Compare(ImgA, ImgA2, slots, slots, "old", "new");
        Assert.True(change.NameChanged);
        Assert.True(change.HasAnyChange);
        Assert.False(change.ImageChanged);
        Assert.False(change.SlotsChanged);
    }
}
