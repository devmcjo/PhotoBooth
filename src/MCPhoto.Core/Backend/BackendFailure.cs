namespace MCPhoto.Core.Backend;

using System;

/// <summary>
/// 서버에 <b>도달하지 못했다</b> — 네트워크 끊김·DNS 실패·연결 거부·타임아웃.
/// 요청이 서버에 닿지 않았으므로 <b>서버 상태는 변하지 않았다</b>(재시도 안전).
///
/// 기존 계약(네트워크/5xx → <see cref="InvalidOperationException"/>)을 깨지 않도록 파생 타입으로 둔다 —
/// 이미 있는 <c>catch (InvalidOperationException)</c>가 그대로 잡는다.
/// </summary>
public sealed class BackendUnavailableException : InvalidOperationException
{
    public BackendUnavailableException(string message, Exception? inner = null)
        : base(message, inner) { }
}

/// <summary>
/// 서버 주소 자체가 <b>설정되지 않았다</b>(설정 화면 미입력 · 오프라인 전용 구성).
///
/// 이 타입이 없으면 상대 URL + BaseAddress 없음 조합이 HttpClient의 영문 예외
/// ("An invalid request URI was provided…")로 새어 나가 그대로 사용자 화면에 찍힌다.
/// 도달불가(<see cref="BackendUnavailableException"/>)와 구분해야 하는 이유는 조치가 다르기 때문이다 —
/// 네트워크를 고치는 것이 아니라 설정에 주소를 넣어야 한다.
/// </summary>
public sealed class BackendNotConfiguredException : InvalidOperationException
{
    public BackendNotConfiguredException(string message)
        : base(message) { }
}

/// <summary>
/// 인증이 필요하다 — 토큰 없음(<see cref="Expired"/>=false) 또는 토큰 만료·무효(=true).
///
/// <see cref="UnauthorizedAccessException"/> 파생이라 기존 403 처리 코드가 그대로 잡는다.
/// <see cref="Expired"/>를 두는 이유: "로그인해 주세요"와 "다시 로그인해 주세요"는 사용자에게 다른 상황이고,
/// 이걸 예외 메시지 문자열로 되짚으면 우리가 쓴 문구에 로직이 묶인다(문구를 못 고치게 된다).
/// </summary>
public sealed class BackendLoginRequiredException : UnauthorizedAccessException
{
    public BackendLoginRequiredException(string message, bool expired)
        : base(message)
    {
        Expired = expired;
    }

    /// <summary>true=토큰이 있었으나 만료/무효(401), false=토큰이 아예 없음.</summary>
    public bool Expired { get; }
}
