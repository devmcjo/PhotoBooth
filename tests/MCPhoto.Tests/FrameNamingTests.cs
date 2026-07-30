using MCPhoto.Core.Frames;

namespace MCPhoto.Tests;

/// <summary>
/// it15 F1-D4: 사본 이름 생성 규칙(순수). 원본 보존 + 이름 기준 dedup 유지의 전제.
/// '_'를 새로 도입하지 않는다(LocalFrameStore 공용/user 구분자, §1.5 함정).
/// </summary>
public class FrameNamingTests
{
    private static readonly string[] None = Array.Empty<string>();

    [Fact]
    public void NextCopyName_First_Copy_Has_No_Index()
        => Assert.Equal("기본프레임 사본", FrameNaming.NextCopyName("기본프레임", None));

    [Fact]
    public void NextCopyName_Second_Copy_Starts_At_Two()
        => Assert.Equal("기본프레임 사본 2",
            FrameNaming.NextCopyName("기본프레임", new[] { "기본프레임 사본" }));

    [Fact]
    public void NextCopyName_Skips_All_Taken_Indexes()
        => Assert.Equal("기본프레임 사본 3",
            FrameNaming.NextCopyName("기본프레임", new[] { "기본프레임 사본", "기본프레임 사본 2" }));

    [Fact]
    public void NextCopyName_Does_Not_Accumulate_Suffix()
    {
        // 이미 사본인 이름을 다시 복사해도 "사본 사본"이 되지 않는다(base로 되돌림).
        Assert.Equal("기본프레임 사본 2",
            FrameNaming.NextCopyName("기본프레임 사본", new[] { "기본프레임 사본" }));
    }

    [Fact]
    public void NextCopyName_Strips_Numbered_Suffix_Before_Copying()
        => Assert.Equal("기본프레임 사본", FrameNaming.NextCopyName("기본프레임 사본 5", None));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void NextCopyName_Blank_Base_Uses_Default(string? baseName)
        => Assert.Equal("새 프레임 사본", FrameNaming.NextCopyName(baseName, None));

    [Fact]
    public void NextCopyName_All_Indexes_Taken_Falls_Back_To_Guid()
    {
        var taken = new List<string> { "f 사본" };
        for (int n = 2; n <= 99; n++) taken.Add($"f 사본 {n}");

        var result = FrameNaming.NextCopyName("f", taken);

        Assert.DoesNotContain(result, taken);           // 충돌하지 않는 이름
        Assert.Matches(@"^f 사본 [0-9a-f]{8}$", result); // 8자리 GUID 접미
    }

    [Fact]
    public void NextCopyName_Result_Has_No_Underscore()
    {
        // '_'는 LocalFrameStore의 공용/user 구분자 — 사본 규칙이 새로 도입하지 않는다.
        Assert.DoesNotContain("_", FrameNaming.NextCopyName("기본프레임", None));
        Assert.DoesNotContain("_", FrameNaming.NextCopyName("기본프레임", new[] { "기본프레임 사본" }));
    }

    [Theory]
    [InlineData("기본프레임")]
    [InlineData("my frame 2024")]
    [InlineData("A")]
    public void StripCopySuffix_Round_Trips_With_NextCopyName(string name)
        => Assert.Equal(name, FrameNaming.StripCopySuffix(FrameNaming.NextCopyName(name, None)));

    [Fact]
    public void StripCopySuffix_Keeps_Names_Without_Suffix()
        => Assert.Equal("기본프레임", FrameNaming.StripCopySuffix("기본프레임"));

    [Fact]
    public void StripCopySuffix_Keeps_Original_When_Base_Would_Be_Empty()
        => Assert.Equal("사본", FrameNaming.StripCopySuffix("사본")); // 빈 이름을 만들지 않는다

    // ── 저장 전 이름 안전성 선검증(LocalFrameStore.EnsureFileNameSafe와 동일 판정) ──

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsFileNameSafe_Rejects_Blank(string? name)
        => Assert.False(FrameNaming.IsFileNameSafe(name));

    [Theory]
    [InlineData("a/b")]
    [InlineData("a:b")]
    [InlineData("a?b")]
    [InlineData("a\\b")]
    [InlineData("a*b")]
    [InlineData("a\"b")]
    [InlineData("a<b")]
    [InlineData("a>b")]
    [InlineData("a|b")]
    public void IsFileNameSafe_Rejects_Invalid_FileName_Chars(string name)
        => Assert.False(FrameNaming.IsFileNameSafe(name));

    [Theory]
    [InlineData("기본프레임")]
    [InlineData("내_프레임")]      // '_'는 파일시스템 금지문자가 아니다(공용 목록 노출 문제는 별도 비차단 경고)
    [InlineData("기본프레임 사본 2")]
    [InlineData("my frame 2024")]
    public void IsFileNameSafe_Accepts_Usable_Names(string name)
        => Assert.True(FrameNaming.IsFileNameSafe(name));

    [Fact]
    public void IsFileNameSafe_Accepts_Every_NextCopyName_Result()
    {
        // 사본 이름은 항상 저장 가능해야 한다(선검증이 fork 정상 흐름을 막지 않는다는 보장).
        Assert.True(FrameNaming.IsFileNameSafe(FrameNaming.NextCopyName("기본프레임", None)));
        Assert.True(FrameNaming.IsFileNameSafe(
            FrameNaming.NextCopyName("기본프레임", new[] { "기본프레임 사본" })));
    }
}
