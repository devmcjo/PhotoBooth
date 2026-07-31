using MCPhoto.Core.Frames;

namespace MCPhoto.Tests;

/// <summary>
/// it20 Step 1: 기본 프레임 준비 진행 표현. 표시 문구가 Core 순수 함수(ToLabel)에 있으므로
/// UI 없이 검증된다 — ViewModel은 받은 문자열을 그대로 대입한다(설계 §5.2).
/// </summary>
public class FrameCatalogProgressTests
{
    /// <summary>T-14: 4개 단계 문구가 모두 비어 있지 않고 서로 다르다(같으면 사용자가 진행을 구분할 수 없다).</summary>
    [Theory]
    [InlineData(FrameCatalogPhase.ResolvingLocal)]
    [InlineData(FrameCatalogPhase.QueryingServer)]
    [InlineData(FrameCatalogPhase.DownloadingImage)]
    [InlineData(FrameCatalogPhase.Completed)]
    public void Label_For_Each_Phase_Is_Not_Empty(FrameCatalogPhase phase)
    {
        var label = new FrameCatalogProgress(phase).ToLabel();
        Assert.False(string.IsNullOrWhiteSpace(label));

        var all = new[]
        {
            FrameCatalogPhase.ResolvingLocal, FrameCatalogPhase.QueryingServer,
            FrameCatalogPhase.DownloadingImage, FrameCatalogPhase.Completed,
        }.Select(p => new FrameCatalogProgress(p).ToLabel()).ToArray();
        Assert.Equal(all.Length, all.Distinct().Count());
    }

    /// <summary>T-15: 다운로드 단계에 Total이 있으면 "(n/m)" 카운터가 붙는다 — 진행이 멈춰 보이지 않게 한다.</summary>
    [Fact]
    public void Downloading_Label_Includes_Counter()
        => Assert.Contains("(2/3)", new FrameCatalogProgress(FrameCatalogPhase.DownloadingImage, 2, 3).ToLabel());

    /// <summary>T-16: Total=0이면 카운터를 생략한다("(0/0)" 같은 무의미한 표기 방지).</summary>
    [Fact]
    public void Downloading_Label_Omits_Counter_When_Total_Zero()
        => Assert.DoesNotContain("(", new FrameCatalogProgress(FrameCatalogPhase.DownloadingImage).ToLabel());

    /// <summary>T-17: 보고 전 기본 문구가 존재한다(오버레이의 빈 문구 구간 방지).</summary>
    [Fact]
    public void Start_Label_Is_Not_Empty()
        => Assert.True(FrameCatalogProgress.StartLabel.Length > 0);

    /// <summary>
    /// T-18: 프레임 이름을 문구에 넣지 않는다는 §5.2 판정의 회귀 방지.
    /// 운영자가 자유 입력하는 이름은 길이 제한이 없어 카드 폭을 넘기거나 오버레이 높이를 요동시킨다.
    /// </summary>
    [Fact]
    public void Progress_Has_No_Frame_Name_Member()
        => Assert.Null(typeof(FrameCatalogProgress).GetProperty("FrameName"));
}
