namespace MCPhoto.Http.Session;

using MCPhoto.Core.Models;

/// <summary>
/// <see cref="IBackendSession"/> 기본 구현. 싱글턴으로 등록되어 여러 HTTP 서비스가 공유한다.
/// UI 스레드 외 호출 가능성(백그라운드 업로드 등)에 대비해 락으로 필드 접근을 보호한다.
/// </summary>
public sealed class BackendSession : IBackendSession
{
    private readonly object _gate = new();
    private string? _token;
    private User? _user;

    public string? Token
    {
        get { lock (_gate) return _token; }
    }

    public User? CurrentUser
    {
        get { lock (_gate) return _user; }
    }

    public void SignIn(string token, User user)
    {
        lock (_gate)
        {
            _token = token;
            _user = user;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _token = null;
            _user = null;
        }
    }
}
