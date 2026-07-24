namespace MCPhoto.Http.Dto;

/// <summary>서버 표준 에러 봉투: `{ error: { code, message } }` (설계 §6.1, functions src/http/errors.ts).</summary>
internal sealed class ErrorEnvelope
{
    public ErrorBody? Error { get; set; }
}

/// <summary>에러 본문(code=서버 표준 코드, message=사용자 노출용 한국어 메시지).</summary>
internal sealed class ErrorBody
{
    public string? Code { get; set; }
    public string? Message { get; set; }
}
