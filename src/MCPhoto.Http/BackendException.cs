namespace MCPhoto.Http;

using System;
using System.Net;

/// <summary>
/// 백엔드가 반환한 표준 에러(`{ error: { code, message } }`)를 담는 예외(설계 §6.1).
/// 상태코드·서버 코드를 보존해 상위 매핑이 도메인 예외로 변환할 수 있게 한다.
/// 시크릿/토큰은 담지 않는다(메시지는 서버가 준 사용자 노출용 텍스트).
/// </summary>
public sealed class BackendException : Exception
{
    /// <summary>HTTP 상태 코드.</summary>
    public HttpStatusCode StatusCode { get; }

    /// <summary>서버 표준 에러 코드(unauthorized/forbidden/conflict/invalid_argument/not_found/internal 등). 없으면 빈 문자열.</summary>
    public string ServerCode { get; }

    public BackendException(HttpStatusCode statusCode, string serverCode, string message)
        : base(message)
    {
        StatusCode = statusCode;
        ServerCode = serverCode;
    }
}
