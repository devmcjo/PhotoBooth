namespace MCPhoto.Core.Navigation;

/// <summary>
/// 유휴 타임아웃 감시. 촬영/선택/편집 중 무동작 시 대기화면 복귀 트리거. (architecture §4.1, PRD §10)
/// </summary>
public interface IIdleWatchdog
{
    /// <summary>유휴 만료 이벤트. 구독자는 진행 취소·임시데이터 폐기·Home 복귀.</summary>
    event EventHandler? IdleTimeout;

    /// <summary>감시 시작(타임아웃 초). 세션 진입 시.</summary>
    void Start(int timeoutSeconds);

    /// <summary>사용자 입력마다 타이머 리셋.</summary>
    void Reset();

    /// <summary>감시 정지(대기화면 복귀·세션 종료 시).</summary>
    void Stop();
}
