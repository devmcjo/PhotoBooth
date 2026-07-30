using MCPhoto.Core.Settings;

namespace MCPhoto.Tests;

/// <summary>
/// it17 Step 1: 촬영 컷 수 정책(순수 함수) — 자동 = max(6, 슬롯+2), 고정 = max(설정, 슬롯).
/// 슬롯 6개 프레임 + 고정 6컷이면 선택 여지가 0이 되는 문제를 자동 모드가 해소한다(설계 §0.2).
/// </summary>
public class CutCountPolicyTests
{
    [Theory]
    [InlineData(1, 6)]   // 슬롯 4개 이하: 최소 6이 이미 +2를 초과 → 고정 6과 동일
    [InlineData(2, 6)]
    [InlineData(3, 6)]
    [InlineData(4, 6)]
    [InlineData(5, 7)]   // 실질 차이 구간(설계 §5.1)
    [InlineData(6, 8)]
    public void Resolve_Auto_By_SlotCount(int slots, int expected)
    {
        Assert.Equal(expected, CutCountPolicy.Resolve(CutCountPolicy.AutoCutCount, slots));
    }

    [Fact]
    public void Resolve_Auto_Respects_Minimum()
    {
        // 프레임 미확정(슬롯 0)이어도 최소 6은 보장.
        Assert.Equal(6, CutCountPolicy.Resolve(CutCountPolicy.AutoCutCount, 0));
    }

    [Fact]
    public void Resolve_Auto_Guards_Negative_SlotCount()
    {
        // 음수 슬롯은 0으로 취급 — 음수가 컷 수로 전파되지 않는다.
        Assert.Equal(6, CutCountPolicy.Resolve(CutCountPolicy.AutoCutCount, -5));
    }

    [Fact]
    public void Resolve_Auto_Handles_Oversized_Frame()
    {
        // 가정 A-1: 슬롯 7개 이상 프레임(손상 파일 등)이 와도 크래시·0컷 없이 동작이 정의된다.
        // 상한 클램프는 의도적 비목표 — 넣으면 "컷 수 ≥ 슬롯 수" 불변이 깨진다(설계 §12 R-3).
        Assert.Equal(10, CutCountPolicy.Resolve(CutCountPolicy.AutoCutCount, 8));
    }

    [Theory]
    [InlineData(6, 3, 6)]
    [InlineData(6, 6, 6)]
    [InlineData(8, 6, 8)]
    [InlineData(10, 6, 10)]
    public void Resolve_Fixed_Keeps_Legacy_Max(int configured, int slots, int expected)
    {
        // 고정 모드는 종전 Math.Max(cutCount, slots)와 비트 단위로 동일해야 한다(VF-4).
        Assert.Equal(expected, CutCountPolicy.Resolve(configured, slots));
    }

    [Fact]
    public void Resolve_Fixed_Never_Below_SlotCount()
    {
        // VF-4 불변: 컷 수 < 슬롯 수면 빈 슬롯이 생긴다 → 슬롯 수로 끌어올린다.
        Assert.Equal(8, CutCountPolicy.Resolve(6, 8));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(-1, false)]   // 음수는 sentinel이 아니다 — 종전대로 Clamp가 6으로 보정(설계 §4.1)
    [InlineData(6, false)]
    [InlineData(7, false)]
    [InlineData(8, false)]
    [InlineData(10, false)]
    public void IsAuto_Only_For_Zero(int configured, bool expected)
    {
        Assert.Equal(expected, CutCountPolicy.IsAuto(configured));
    }

    [Fact]
    public void AutoCutCount_Is_Zero()
    {
        // sentinel 고정 — 값이 바뀌면 기존 ini의 하위 호환이 깨진다(설계 §4.3).
        Assert.Equal(0, CutCountPolicy.AutoCutCount);
    }

    [Fact]
    public void Auto_Constants_Match_Requirement()
    {
        // 요구사항 원문: "최소 촬영 수 6회, 슬롯 수 + 2만큼 촬영"(설계 §0.1).
        Assert.Equal(6, CutCountPolicy.AutoMinimum);
        Assert.Equal(2, CutCountPolicy.AutoMargin);
    }
}
