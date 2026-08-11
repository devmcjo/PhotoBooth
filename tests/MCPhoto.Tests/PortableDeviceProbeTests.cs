using MCPhoto.Capture;

namespace MCPhoto.Tests;

/// <summary>
/// it24 Step 3: 휴대용 장치 이름 매칭 순수 함수(설계 §5.1 ③ · §12.1 T-M1).
/// <para>
/// WMI 실 I/O는 검증하지 않는다 — 그쪽은 catch-all + 빈 목록 폴백뿐이라 실패 경로가 구조적으로 무해하고,
/// 판정에 영향을 주는 부분은 전부 이 순수 함수 뒤에 있다(§12).
/// </para>
/// </summary>
public class PortableDeviceProbeTests
{
    private static readonly string[] D5300Keywords = { "Nikon", "D5300" };

    [Fact]
    public void Match_Is_Case_Insensitive_Contains()
    {
        var names = new[] { "NIKON DSC D5300", "Generic USB Hub" };
        var matched = PortableDeviceProbe.MatchCandidates(names, D5300Keywords);

        Assert.Single(matched);
        Assert.Equal("NIKON DSC D5300", matched[0]);   // 원문을 보존한다(운영자가 육안으로 대조한다)
    }

    /// <summary>
    /// ★ U2(반증 사례) 허용 설계: Nikon 바디가 제네릭 "MTP Portable Device"로 뜨면 <b>매칭은 miss난다</b>.
    /// 이 miss가 정상 동작이며, 그래서 미매칭을 "장치 없음"의 근거로 쓰지 않는다(R3).
    /// </summary>
    [Fact]
    public void Generic_Mtp_Name_Does_Not_Match()
    {
        var matched = PortableDeviceProbe.MatchCandidates(new[] { "MTP Portable Device" }, D5300Keywords);
        Assert.Empty(matched);
    }

    [Fact]
    public void Any_Single_Keyword_Is_Enough()
    {
        // 전체 일치를 요구하면 표기가 조금만 달라도 신호가 사라진다 — 양성 신호는 느슨한 쪽이 안전하다.
        Assert.Single(PortableDeviceProbe.MatchCandidates(new[] { "Nikon Digital Camera" }, D5300Keywords));
        Assert.Single(PortableDeviceProbe.MatchCandidates(new[] { "D5300" }, D5300Keywords));
    }

    [Fact]
    public void Matched_Name_Appears_Once_Even_If_Multiple_Keywords_Hit()
    {
        var matched = PortableDeviceProbe.MatchCandidates(new[] { "Nikon D5300" }, D5300Keywords);
        Assert.Single(matched);
    }

    [Fact]
    public void Empty_Inputs_Produce_Empty_Output()
    {
        Assert.Empty(PortableDeviceProbe.MatchCandidates(Array.Empty<string>(), D5300Keywords));
        Assert.Empty(PortableDeviceProbe.MatchCandidates(new[] { "Nikon D5300" }, Array.Empty<string>()));
    }

    [Fact]
    public void Blank_Names_And_Keywords_Are_Skipped()
    {
        // 모델 표시명 Split(' ')이 빈 토큰을 만들 수 있다. 빈 키워드가 Contains에 들어가면 전부 매칭돼
        // "감지되었습니다"가 아무 장치에나 붙는다 — 그 거짓 양성을 여기서 막는다.
        var names = new[] { "  ", "Generic USB Hub" };
        Assert.Empty(PortableDeviceProbe.MatchCandidates(names, new[] { "", " " }));
        Assert.Empty(PortableDeviceProbe.MatchCandidates(names, D5300Keywords));
    }

    /// <summary>WMI 조회는 실패해도 예외를 던지지 않는다(빈 목록 폴백 — E12). 이 머신의 실제 값은 검증하지 않는다.</summary>
    [Fact]
    public void Enumeration_Never_Throws()
    {
        var names = PortableDeviceProbe.TryGetPortableDeviceNames();
        Assert.NotNull(names);
    }
}
