using MCPhoto.Core.Accounts;

namespace MCPhoto.Core.Upload;

/// <summary>
/// TempUser QR 한도 초과로 서버가 업로드를 거부(403)했을 때의 도메인 예외(it13 §5.2·§9.3).
/// 서버 에러 code(TEMP_USER_TIME_EXCEEDED/TEMP_USER_COUNT_EXCEEDED)를 <see cref="Reason"/>으로 보존해
/// QR 팝업이 사유별 정확 문구(§0)를 표시할 수 있게 한다. 그 외 업로드 실패와 구분되는 신호.
/// </summary>
public sealed class QrLimitExceededException : Exception
{
    /// <summary>초과 사유(Time/Count). 시간 우선(서버가 판정).</summary>
    public QrGateReason Reason { get; }

    public QrLimitExceededException(QrGateReason reason, string message)
        : base(message)
    {
        Reason = reason;
    }
}
