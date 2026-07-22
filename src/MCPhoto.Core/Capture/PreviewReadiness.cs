namespace MCPhoto.Core.Capture;

/// <summary>
/// 카메라 프리뷰 "안정적 실사용 가능" 판정(순수 로직, 테스트 대상). (it8 §7 A7)
/// 첫 프레임 1회로는 부족 — 연속 N프레임 수신 AND 최소 경과 시간을 둘 다 충족해야 Ready.
/// 시간·프레임 이벤트는 호출측(CaptureViewModel)이 주입하고, 판정 규칙만 이 클래스가 담당.
/// </summary>
public sealed class PreviewReadiness
{
    private readonly int _requiredFrames;
    private readonly double _minElapsedMs;
    private int _frameCount;
    private double _firstFrameElapsedMs = -1;

    public PreviewReadiness(int requiredFrames = 8, double minElapsedMs = 500)
    {
        _requiredFrames = Math.Max(1, requiredFrames);
        _minElapsedMs = Math.Max(0, minElapsedMs);
    }

    /// <summary>수신한 프레임 수.</summary>
    public int FrameCount => _frameCount;

    /// <summary>
    /// 프레임 1개 수신 반영. elapsedMs=대기 시작 이후 누적 경과. currentFps=현재 fps(0이면 스트림 미흐름).
    /// 반환=이 프레임으로 Ready에 도달했는지(전이 시 true 1회).
    /// </summary>
    public bool OnFrame(double elapsedMs, double currentFps)
    {
        if (IsReady) return false; // 이미 준비됨(중복 방지)
        _frameCount++;
        if (_firstFrameElapsedMs < 0) _firstFrameElapsedMs = elapsedMs;

        bool enoughFrames = _frameCount >= _requiredFrames;
        bool enoughElapsed = elapsedMs >= _minElapsedMs;
        bool streaming = currentFps > 0;
        if (enoughFrames && enoughElapsed && streaming)
        {
            IsReady = true;
            return true;
        }
        return false;
    }

    /// <summary>안정적 프리뷰 준비 완료.</summary>
    public bool IsReady { get; private set; }
}
