namespace MCPhoto.Core.Navigation;

/// <summary>
/// 유휴 경고 팝업 카운트다운(순수 로직, 테스트 대상). (it8 §2 A1)
/// 시작값(초)에서 Tick마다 1씩 감소, 0 도달 시 Expired. UI 타이머(DispatcherTimer)는 셸이 구동하고,
/// 감소·완료·리셋 규칙만 이 클래스가 담당한다(headless 단위 테스트).
/// </summary>
public sealed class IdleCountdown
{
    private readonly int _startSeconds;

    public IdleCountdown(int startSeconds)
    {
        _startSeconds = Math.Max(1, startSeconds);
        Remaining = _startSeconds;
    }

    /// <summary>남은 초. 0이면 만료.</summary>
    public int Remaining { get; private set; }

    /// <summary>카운트다운 완료(0 도달).</summary>
    public bool IsExpired => Remaining <= 0;

    /// <summary>
    /// 1초 경과 반영. 남은 초를 1 줄이고(하한 0), 이번 Tick으로 0에 도달했으면 true(만료 전이).
    /// 이미 0이면 false(중복 완료 방지).
    /// </summary>
    public bool Tick()
    {
        if (Remaining <= 0) return false;
        Remaining--;
        return Remaining == 0;
    }

    /// <summary>시작값으로 되돌림([이어서 진행하기] 또는 경고 해제).</summary>
    public void Reset() => Remaining = _startSeconds;
}
