using MCPhoto.Core.Frames;

namespace MCPhoto.Tests;

/// <summary>
/// it20 Step 1: 기본 프레임 로딩 대기 정책(순수 함수) — 국면 판정·확정·상한·안내 문구.
/// 최초 실행은 로컬 기본 프레임이 없어 서버 다운로드를 기다린다. 그 대기에 상한과 결과 판정을
/// 부여하는 것이 이 정책의 책임이며, **어떤 입력에서도 Loading으로 확정되지 않는 것**이 핵심 불변식이다
/// (설계 §0.4 — Loading 고착은 전면 오버레이 영구 노출을 뜻한다).
/// ⚠️ Core 순수 테스트다 — MCPhoto.App 타입을 참조하지 않는다(유휴 경고 불변식은 참조 상수로 확인).
/// </summary>
public class FrameLoadPolicyTests
{
    // ── Classify: 목록 개수 × 대기 중단 여부 진리표 ──

    /// <summary>T-1: 프레임이 0개(또는 음수 방어)면 무조건 Failed — 빈 목록 + 활성 [다음]을 없애는 것이 이 설계의 목적이다.</summary>
    [Theory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(-1, false)]
    public void Classify_Zero_Frames_Is_Failed(int count, bool interrupted)
        => Assert.Equal(FrameLoadPhase.Failed, FrameLoadPolicy.Classify(count, interrupted));

    /// <summary>T-2: 대기가 중단됐지만 로컬 프레임이 남았으면 Degraded(축소 진행 + 안내).</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void Classify_Interrupted_With_Frames_Is_Degraded(int count)
        => Assert.Equal(FrameLoadPhase.Degraded, FrameLoadPolicy.Classify(count, waitInterrupted: true));

    /// <summary>
    /// T-3: 중단 없이 프레임을 얻으면 Ready. 서버 조회가 즉시 실패한 오프라인 부스도 이 경로다
    /// (조회 실패는 서비스 내부에서 삼켜져 예외로 나오지 않으므로 interrupted=false) — 조용한 폴백 보존(§6.4).
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void Classify_Uninterrupted_With_Frames_Is_Ready(int count)
        => Assert.Equal(FrameLoadPhase.Ready, FrameLoadPolicy.Classify(count, waitInterrupted: false));

    // ── Finalize: finally에서 국면을 닫는 함수 ──

    /// <summary>T-4: 소리 내는 계기(진입·다시 시도)는 Classify 결과를 그대로 채택한다.</summary>
    [Fact]
    public void Finalize_Loud_Uses_Classify()
    {
        Assert.Equal(FrameLoadPhase.Degraded,
            FrameLoadPolicy.Finalize(FrameLoadPhase.Loading, 2, waitInterrupted: true, quiet: false));
        Assert.Equal(FrameLoadPhase.Ready,
            FrameLoadPolicy.Finalize(FrameLoadPhase.Loading, 2, waitInterrupted: false, quiet: false));
    }

    /// <summary>T-5: 조용한 재스캔(삭제 후)은 종전 국면을 유지한다 — 네트워크 안내가 삭제 조작에 끼어들지 않는다(§6.5).</summary>
    [Fact]
    public void Finalize_Quiet_Keeps_Current()
        => Assert.Equal(FrameLoadPhase.Ready,
            FrameLoadPolicy.Finalize(FrameLoadPhase.Ready, 2, waitInterrupted: true, quiet: true));

    /// <summary>T-6: 조용한 재스캔이라도 종전이 Failed였고 프레임이 생겼으면 Ready로 회복한다.</summary>
    [Fact]
    public void Finalize_Quiet_Recovers_From_Failed()
        => Assert.Equal(FrameLoadPhase.Ready,
            FrameLoadPolicy.Finalize(FrameLoadPhase.Failed, 2, waitInterrupted: false, quiet: true));

    /// <summary>T-7: 프레임 0개는 quiet 여부·종전 국면과 무관하게 항상 Failed.</summary>
    [Theory]
    [InlineData(FrameLoadPhase.Ready, true)]
    [InlineData(FrameLoadPhase.Degraded, true)]
    [InlineData(FrameLoadPhase.Loading, false)]
    public void Finalize_Zero_Frames_Always_Failed(FrameLoadPhase current, bool quiet)
        => Assert.Equal(FrameLoadPhase.Failed,
            FrameLoadPolicy.Finalize(current, 0, waitInterrupted: false, quiet: quiet));

    /// <summary>
    /// T-8: **§0.4 불변식의 기계적 고정** — 4국면 × quiet 2 × count{0,2} × interrupted 2 = 32조합에서
    /// Finalize가 Loading을 돌려주는 경우가 한 번도 없다. 하나라도 있으면 전면 오버레이가 영구 고착된다.
    /// </summary>
    [Theory]
    [InlineData(FrameLoadPhase.Loading)]
    [InlineData(FrameLoadPhase.Ready)]
    [InlineData(FrameLoadPhase.Degraded)]
    [InlineData(FrameLoadPhase.Failed)]
    public void Finalize_Never_Returns_Loading(FrameLoadPhase current)
    {
        foreach (var quiet in new[] { false, true })
            foreach (var count in new[] { 0, 2 })
                foreach (var interrupted in new[] { false, true })
                {
                    var result = FrameLoadPolicy.Finalize(current, count, interrupted, quiet);
                    Assert.NotEqual(FrameLoadPhase.Loading, result);
                }
    }

    // ── NextDeadline: 무진행 상한과 총 상한의 합성 ──

    /// <summary>T-9: 총 상한이 넉넉하면 무진행 상한(30초)을 그대로 쓴다.</summary>
    [Fact]
    public void NextDeadline_Returns_NoProgress_When_Plenty_Left()
        => Assert.Equal(FrameLoadPolicy.NoProgressTimeout, FrameLoadPolicy.NextDeadline(TimeSpan.Zero));

    /// <summary>T-10: 총 상한 잔량이 무진행 상한보다 짧으면 잔량으로 클램프된다(60−45=15초).</summary>
    [Fact]
    public void NextDeadline_Clamps_To_Remaining_Total()
        => Assert.Equal(TimeSpan.FromSeconds(15), FrameLoadPolicy.NextDeadline(TimeSpan.FromSeconds(45)));

    /// <summary>T-11: 총 상한을 소진하면 0 — 호출측은 즉시 취소해야 한다.</summary>
    [Theory]
    [InlineData(60)]
    [InlineData(90)]
    public void NextDeadline_Zero_When_Total_Exhausted(int elapsedSeconds)
        => Assert.Equal(TimeSpan.Zero, FrameLoadPolicy.NextDeadline(TimeSpan.FromSeconds(elapsedSeconds)));

    /// <summary>
    /// T-12: 상한 순서 불변식(A-5). 총 대기 상한이 유휴 경고보다 짧아야 대기 중에 "자리를 비우셨나요?"
    /// 팝업이 겹치지 않고, 무진행 상한은 총 상한보다 짧아야 2단 구조가 의미를 갖는다.
    /// </summary>
    [Fact]
    public void MaxTotalWait_Is_Below_Idle_Warning()
    {
        Assert.True(FrameLoadPolicy.MaxTotalWaitSeconds < FrameLoadPolicy.IdleWarningReferenceSeconds,
            "총 대기 상한이 유휴 경고보다 길면 대기 중 유휴 팝업이 겹친다");
        Assert.True(FrameLoadPolicy.NoProgressTimeoutSeconds < FrameLoadPolicy.MaxTotalWaitSeconds,
            "무진행 상한이 총 상한 이상이면 2단 상한이 무의미하다");
    }

    // ── NoticeFor: 국면별 안내 문구 ──

    /// <summary>T-13: Ready·Loading은 안내 없음(빈 문자열), Degraded·Failed는 서로 다른 안내를 갖는다.</summary>
    [Theory]
    [InlineData(FrameLoadPhase.Loading)]
    [InlineData(FrameLoadPhase.Ready)]
    [InlineData(FrameLoadPhase.Degraded)]
    [InlineData(FrameLoadPhase.Failed)]
    public void NoticeFor_Ready_Is_Empty_Others_Are_Not(FrameLoadPhase phase)
    {
        var notice = FrameLoadPolicy.NoticeFor(phase);
        if (phase is FrameLoadPhase.Loading or FrameLoadPhase.Ready)
        {
            Assert.Equal(string.Empty, notice);
        }
        else
        {
            Assert.False(string.IsNullOrWhiteSpace(notice));
            Assert.NotEqual(FrameLoadPolicy.NoticeFor(FrameLoadPhase.Degraded),
                FrameLoadPolicy.NoticeFor(FrameLoadPhase.Failed));
        }
    }

    /// <summary>ViewModel 초기 상태 안전 보장: enum 기본값이 Loading이어야 필드 초기화 누락 시에도 대기로 시작한다.</summary>
    [Fact]
    public void Phase_Default_Is_Loading()
        => Assert.Equal(FrameLoadPhase.Loading, default(FrameLoadPhase));
}
