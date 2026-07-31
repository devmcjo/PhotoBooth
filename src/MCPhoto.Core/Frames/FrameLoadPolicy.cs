namespace MCPhoto.Core.Frames;

/// <summary>
/// 프레임 선택 화면의 목록 로딩 국면. UI 없이 판정·테스트되도록 Core에 둔다. (it20)
/// 0번 값이 <see cref="Loading"/>인 것은 의도다 — ViewModel 초기 상태가 안전하게 대기로 시작한다.
/// </summary>
public enum FrameLoadPhase
{
    /// <summary>서버·로컬에서 목록을 준비하는 중(대기 오버레이 노출).</summary>
    Loading,
    /// <summary>정상 완료. 목록 표시, 안내 없음.</summary>
    Ready,
    /// <summary>대기가 중단되어 로컬 프레임만으로 진행. 목록 표시 + 인라인 안내 + [다시 시도].</summary>
    Degraded,
    /// <summary>쓸 수 있는 프레임이 0개. 전면 실패 카드 + [다시 시도]/[메인으로].</summary>
    Failed
}

/// <summary>
/// 기본 프레임 로딩 대기 정책(순수 함수 — UI·서비스 인스턴스 무의존). (it20)
/// 최초 실행은 로컬에 기본 프레임이 없어 서버 다운로드를 기다린다(설계 §0.2). 그 대기에
/// **상한**과 **결과 판정**과 **안내 문구**를 부여하는 것이 이 클래스의 책임이다.
/// </summary>
public static class FrameLoadPolicy
{
    /// <summary>
    /// 무진행(inactivity) 상한(초). 진행 보고가 이 시간 동안 한 번도 없으면 대기를 포기한다.
    /// wall-clock 예산을 쓰지 않는 이유(설계 §6.3): 최초 실행의 지배 경로는 시작 prefetch가 이미
    /// 다운로드 중일 때 진입하는 것이라, wall-clock 예산은 정상 진행 중인 다운로드를 잘라
    /// "실패했다"는 거짓 안내를 띄운다. 단계 전환이 곧 진행의 증거이므로 무진행으로 정의한다.
    /// </summary>
    public const int NoProgressTimeoutSeconds = 30;

    /// <summary>
    /// 총 대기 상한(초). 아무리 진행 중이어도 손님을 이보다 길게 세워두지 않는다.
    /// 유휴 경고(AppShellViewModel.IdleWarningSeconds 기본 120초)보다 짧아야 한다 — 대기 중에
    /// "잠시 자리를 비우셨나요?" 팝업이 겹치지 않게 한다(설계 §4.6).
    /// </summary>
    public const int MaxTotalWaitSeconds = 60;

    /// <summary>유휴 경고 기본값(초). 상한 불변식을 Core 테스트에서 확인하기 위한 참조 상수 —
    /// 진실원은 <c>AppShellViewModel.IdleWarningSeconds</c>이며 이 값은 그 기본값의 사본이다.</summary>
    public const int IdleWarningReferenceSeconds = 120;

    public static TimeSpan NoProgressTimeout => TimeSpan.FromSeconds(NoProgressTimeoutSeconds);
    public static TimeSpan MaxTotalWait => TimeSpan.FromSeconds(MaxTotalWaitSeconds);

    /// <summary>
    /// 지금부터 취소까지 남겨 둘 시간. 무진행 상한과 총 상한 중 **먼저 오는 쪽**을 돌려준다.
    /// 진행 보고마다 호출해 <c>CancellationTokenSource.CancelAfter</c>를 재무장한다.
    /// 0 이하를 돌려주면 즉시 취소해야 한다(총 상한 도달).
    /// </summary>
    /// <param name="elapsed">이 로딩이 시작된 뒤 흐른 시간.</param>
    public static TimeSpan NextDeadline(TimeSpan elapsed)
    {
        var remainingTotal = MaxTotalWait - elapsed;
        if (remainingTotal <= TimeSpan.Zero) return TimeSpan.Zero;
        return remainingTotal < NoProgressTimeout ? remainingTotal : NoProgressTimeout;
    }

    /// <summary>
    /// 로딩 결과 판정.
    /// frameCount=0 → Failed(쓸 프레임이 없다).
    /// waitInterrupted=true(상한 초과·사용자 건너뛰기·예외) → Degraded.
    /// 그 외 → Ready. **서버 조회 실패 자체는 Degraded가 아니다** — 오프라인 부스는 로컬 캐시로
    /// 조용히 운영되는 것이 종전 동작이며(it10 폴백), 안내를 띄우면 매 진입 노이즈가 된다(설계 §6.4).
    /// </summary>
    public static FrameLoadPhase Classify(int frameCount, bool waitInterrupted)
        => frameCount <= 0 ? FrameLoadPhase.Failed
         : waitInterrupted ? FrameLoadPhase.Degraded
         : FrameLoadPhase.Ready;

    /// <summary>
    /// 로딩 종료 시 확정할 국면. ViewModel의 <c>finally</c>가 **무조건** 이 함수로 국면을 닫는다
    /// (설계 §0.4·§6.6 — Loading 고착 방지).
    /// quiet=true(삭제 후 조용한 재스캔)면 종전 국면을 유지한다. 단 세 경우는 예외 없이 갱신한다:
    /// 프레임이 0개면 Failed(빈 목록 + 활성 [다음]은 이 설계가 없애려는 상태),
    /// 종전이 Failed였는데 프레임이 생겼으면 Ready로 회복,
    /// 종전이 Loading이면 Ready로 닫는다 — **반환값에 Loading이 없다**는 §0.4 불변식을 조건 없이 성립시킨다
    /// (그대로 유지하면 대기 오버레이가 영구 고착된다. 설계 §5.1 코드 조각은 이 갈래를 빠뜨려
    /// 스스로 명시한 §10.1 T-8 진리표와 모순됐다 — 불변식 쪽을 채택했다).
    /// </summary>
    /// <param name="current">종료 직전 국면.</param>
    /// <param name="frameCount">최종 목록 개수.</param>
    /// <param name="waitInterrupted">대기가 중단됐거나 정상 완료에 도달하지 못했는지.</param>
    /// <param name="quiet">조용한 재스캔(오버레이·안내를 띄우지 않는 계기)인지.</param>
    public static FrameLoadPhase Finalize(
        FrameLoadPhase current, int frameCount, bool waitInterrupted, bool quiet)
    {
        if (frameCount <= 0) return FrameLoadPhase.Failed;
        if (!quiet) return Classify(frameCount, waitInterrupted);
        return current is FrameLoadPhase.Failed or FrameLoadPhase.Loading
            ? FrameLoadPhase.Ready
            : current;
    }

    /// <summary>국면별 사용자 안내 문구(Ready는 빈 문자열). UI 없이 테스트 가능하도록 Core에 둔다.</summary>
    public static string NoticeFor(FrameLoadPhase phase) => phase switch
    {
        // "가져오지 못해"가 아니라 "모두 가져오지 못해"인 이유: 총 상한(60초) 초과는 진행 중인 정상
        // 다운로드도 자르므로 일부는 이미 받아 목록에 들어와 있을 수 있다. 전부 실패한 것처럼 쓰면 거짓이 된다.
        FrameLoadPhase.Degraded => "서버 프레임을 모두 가져오지 못해 지금 준비된 프레임으로 진행합니다.",
        FrameLoadPhase.Failed => "사용할 수 있는 프레임이 없습니다. 네트워크를 확인하고 다시 시도해 주세요.",
        _ => string.Empty
    };
}
