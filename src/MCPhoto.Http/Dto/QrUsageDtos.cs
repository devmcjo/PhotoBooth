namespace MCPhoto.Http.Dto;

/// <summary>
/// GET /accounts/me/qr-usage 응답(설계 §5.3). 서버가 principal.id로 계정을 로드해 evaluateQrGate 실행한 결과.
/// 비TempUser(user/manager/admin)는 blocked=false, reason="ok"(한도 없음 — 클라가 무제한 처리).
/// reason은 문자열("ok"|"time"|"count") — 클라가 QrGateReason으로 파싱. (functions src/routes/accounts.ts)
/// </summary>
internal sealed class QrUsageResponse
{
    /// <summary>계정 역할 문자열("temp_user"|"user"|"manager"|"admin").</summary>
    public string Role { get; set; } = string.Empty;

    /// <summary>QR 전송 차단(한도 초과) 여부. 비TempUser는 항상 false.</summary>
    public bool Blocked { get; set; }

    /// <summary>초과 사유("ok"|"time"|"count"). 시간 우선(둘 다 초과면 "time").</summary>
    public string Reason { get; set; } = "ok";

    /// <summary>시간 잔여(ms). 초과 시 0. 클라 표시용(재계산 금지 — 서버 UTC 권위).</summary>
    public long RemainingMs { get; set; }

    /// <summary>횟수 잔여. 초과 시 0.</summary>
    public int RemainingCount { get; set; }

    /// <summary>적용된 전역 한도(표시용).</summary>
    public TempUserLimitsDto? Limits { get; set; }
}

/// <summary>
/// GET/PATCH /config/temp-user-limits 요청·응답 본문(설계 §5.4): {qrHours, qrCount}. 문서 부재 시 서버가 기본값(48/30) 반환.
/// (functions src/routes/config.ts)
/// </summary>
internal sealed class TempUserLimitsDto
{
    /// <summary>시간 한도(시간). 기본 48.</summary>
    public int QrHours { get; set; }

    /// <summary>횟수 한도(성공 세션 수). 기본 30.</summary>
    public int QrCount { get; set; }
}
