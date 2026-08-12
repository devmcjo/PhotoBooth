using MCPhoto.Core.Frames;
using static MCPhoto.Core.Frames.DefaultFrameProvider;

namespace MCPhoto.Tests;

/// <summary>
/// WBS Step 12: 기본 프레임 우선순위(DB→번들→fallback)·fallback 스펙 검증.
/// <para>
/// ⚠️ ②(번들)는 it27에서 폐기 — 번들 스캔 코드를 제거해 <c>hasBundleFrames</c>가 참이 되는 프로덕션
/// 경로가 없다(설계 it27 §3.2·§4.5). 순수 함수 열거만 이력으로 남기며, 이 단정들도 함께 남긴다.
/// </para>
/// </summary>
public class DefaultFrameTests
{
    [Fact]
    public void Priority_Db_First()
    {
        // DB 있으면 항상 DB(번들 유무 무관)
        Assert.Equal(FrameSource.Database, SelectSource(hasDbFrames: true, hasBundleFrames: true));
        Assert.Equal(FrameSource.Database, SelectSource(hasDbFrames: true, hasBundleFrames: false));
    }

    [Fact]
    public void Priority_Bundle_When_No_Db()
    {
        Assert.Equal(FrameSource.Bundle, SelectSource(hasDbFrames: false, hasBundleFrames: true));
    }

    [Fact]
    public void Priority_Fallback_When_None()
    {
        Assert.Equal(FrameSource.Fallback, SelectSource(hasDbFrames: false, hasBundleFrames: false));
    }

    [Fact]
    public void Fallback_Spec_Is_White_3by4_4slots()
    {
        // §9 #11: 하양·3:4·4슬롯
        Assert.Equal(4, FallbackSlotCount);
        double aspect = (double)FallbackWidth / FallbackHeight;
        Assert.Equal(0.75, aspect, 2);

        var t = CreateFallbackTemplate("x.png");
        Assert.Equal(4, t.Slots.Count);
        Assert.True(t.IsDefault);
        Assert.Null(t.UserId);
    }
}
