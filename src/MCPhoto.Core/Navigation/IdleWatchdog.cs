namespace MCPhoto.Core.Navigation;

/// <summary>
/// 유휴 타임아웃 감시(System.Threading.Timer 기반, 플랫폼 무관). (architecture §4.1, PRD §10)
/// Start 후 timeoutSeconds 동안 Reset이 없으면 IdleTimeout 발생.
/// 이벤트는 스레드풀 스레드에서 발생하므로 UI 구독자는 Dispatcher로 마샬링해야 한다.
/// </summary>
public sealed class IdleWatchdog : IIdleWatchdog, IDisposable
{
    private readonly object _lock = new();
    private Timer? _timer;
    private int _timeoutMs;
    private bool _running;

    public event EventHandler? IdleTimeout;

    public void Start(int timeoutSeconds)
    {
        lock (_lock)
        {
            _timeoutMs = Math.Max(1, timeoutSeconds) * 1000;
            _running = true;
            _timer?.Dispose();
            _timer = new Timer(OnElapsed, null, _timeoutMs, Timeout.Infinite);
        }
    }

    public void Reset()
    {
        lock (_lock)
        {
            if (!_running || _timer is null) return;
            _timer.Change(_timeoutMs, Timeout.Infinite);
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            _running = false;
            _timer?.Change(Timeout.Infinite, Timeout.Infinite);
        }
    }

    private void OnElapsed(object? state)
    {
        bool fire;
        lock (_lock)
        {
            fire = _running;
            _running = false; // 1회 발생 후 정지(구독자가 Home 복귀 처리)
        }
        if (fire)
            IdleTimeout?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _running = false;
            _timer?.Dispose();
            _timer = null;
        }
    }
}
