using MCPhoto.Core.LocalSave;

namespace MCPhoto.Tests;

/// <summary>
/// it26 §4.6 T2 — 유휴 팝업 [결과물 폴더 열기] 링크 노출 진리표(2조건 AND).
/// ★ 핵심 불변식: 옵션이 꺼져 있으면 어떤 경우에도 링크가 없다(손님 앞 무인 팝업의 fail-safe).
/// </summary>
public class ResultFolderLinkPolicyTests
{
    [Theory]
    // sessionFolder, 옵션, 기대
    [InlineData(null, false, false)]
    [InlineData(null, true, false)]          // 열 폴더가 없으면 링크도 없다
    [InlineData("", false, false)]
    [InlineData("", true, false)]
    [InlineData(@"C:\ProgramData\MCPhoto\result\mcphoto_260812_1445", false, false)] // 기본값(off)
    [InlineData(@"C:\ProgramData\MCPhoto\result\mcphoto_260812_1445", true, true)]
    public void Truth_Table(string? sessionFolder, bool enabled, bool expected)
    {
        Assert.Equal(expected, ResultFolderLinkPolicy.ShouldShow(sessionFolder, enabled));
    }
}
