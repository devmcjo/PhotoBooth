namespace MCPhoto.Http.Session;

using MCPhoto.Core.Models;

/// <summary>
/// 로그인으로 받은 JWT와 현재 사용자를 앱 수명 동안 메모리에만 보관하는 싱글턴(설계 §1.3·§7.2).
/// HTTP 구현들이 이 홀더를 공유해 Authorization: Bearer 헤더를 조립한다. 토큰은 디스크에 저장하지 않는다.
/// </summary>
public interface IBackendSession
{
    /// <summary>현재 Bearer 토큰(JWT). 로그인 전/로그아웃 후 null.</summary>
    string? Token { get; }

    /// <summary>현재 로그인 사용자(역할 판정에 사용). 로그인 전 null. 비밀번호는 보관하지 않는다.</summary>
    User? CurrentUser { get; }

    /// <summary>로그인 성공 시 토큰·사용자 저장.</summary>
    void SignIn(string token, User user);

    /// <summary>로그아웃/토큰 만료 시 홀더 비움.</summary>
    void Clear();
}
