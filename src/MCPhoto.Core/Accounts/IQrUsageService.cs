namespace MCPhoto.Core.Accounts;

/// <summary>QR 한도 초과 사유(설정 문구·게이트용). 시간 우선(둘 다 초과면 Time). (it13 §4.3·§8.1)</summary>
public enum QrGateReason
{
    /// <summary>미초과(정상).</summary>
    Ok,

    /// <summary>시간 한도 초과(계정 createdAt + 전역 hours 경과). 회복 불가라 횟수보다 우선.</summary>
    Time,

    /// <summary>횟수 한도 소진(QR 전송 성공 세션 수).</summary>
    Count,
}

/// <summary>
/// TempUser QR 사용 게이트 상태(서버 판정 결과의 클라 표현). 비TempUser·게스트는 <see cref="Unlimited"/>.
/// RemainingTime/RemainingCount는 서버 응답값 그대로(클라 재계산 금지 — 시계 오차 회피, it13 §8.4). (it13 §7.2)
/// </summary>
public sealed record QrUsageStatus(bool Blocked, QrGateReason Reason, TimeSpan RemainingTime, int RemainingCount)
{
    /// <summary>한도 없음(비TempUser·게스트·서버 미도달 fail-open). Blocked=false.</summary>
    public static QrUsageStatus Unlimited => new(false, QrGateReason.Ok, TimeSpan.MaxValue, int.MaxValue);
}

/// <summary>
/// 현재 로그인 계정의 QR 사용 게이트 상태 조회(서버 권위, it13 §5.3·§7.2).
/// 클라 게이트는 UX·1차 방어일 뿐 — 과금 안전은 서버가 업로드를 거부함으로써 성립한다.
/// </summary>
public interface IQrUsageService
{
    /// <summary>
    /// 현재 로그인 계정의 QR 사용 게이트 상태 조회. 비TempUser·게스트는 <see cref="QrUsageStatus.Unlimited"/>.
    /// 서버 미도달(오프라인) 시 null → 호출측이 fail-open으로 처리(허용, 서버가 업로드에서 최종 거부, it13 §8.5).
    /// </summary>
    Task<QrUsageStatus?> GetStatusAsync(CancellationToken ct = default);
}

/// <summary>전역 TempUser 한도(시간·횟수). Admin 조회·수정용. (it13 §5.4)</summary>
public sealed record TempUserLimits(int QrHours, int QrCount)
{
    /// <summary>전역 기본값(서버 config 문서 부재 시 폴백). 서버 DEFAULT_TEMP_USER_LIMITS와 대칭(48h/30회, §0·§4.3).</summary>
    public static TempUserLimits Default => new(48, 30);
}

/// <summary>
/// 전역 TempUser 한도 조회·수정(it13 §5.4). GET은 모든 로그인 사용자(표시용), PATCH는 Admin 전용
/// (서버가 requireAdmin으로 이중 방어 — 비Admin은 <see cref="UnauthorizedAccessException"/>).
/// </summary>
public interface ITempUserLimitsService
{
    /// <summary>현재 전역 한도 조회. 서버 문서 부재 시 기본값(48h/30회). 서버 미도달 시 예외.</summary>
    Task<TempUserLimits> GetLimitsAsync(CancellationToken ct = default);

    /// <summary>전역 한도 수정(Admin). 비Admin은 서버 403 → <see cref="UnauthorizedAccessException"/>.</summary>
    Task SetLimitsAsync(TempUserLimits limits, CancellationToken ct = default);
}
