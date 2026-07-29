namespace MCPhoto.Http.Dto;

using System;

/// <summary>
/// GET /health 응답(functions src/routes/health.ts). 무인증으로도 200 {status,time}이 오고,
/// <see cref="DeployedAt"/>은 유효 API 키(X-MCPhoto-Client)를 제시했을 때만 포함된다 —
/// 키가 없거나 서버에 배포 스탬프가 없으면 필드가 생략되어 null이 된다.
/// </summary>
internal sealed class HealthResponse
{
    /// <summary>상태 문자열(정상 시 "ok").</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>서버 응답 시각(UTC). 요청 시각이며 배포 시각이 아니다.</summary>
    public DateTimeOffset? Time { get; set; }

    /// <summary>최종 웹 배포 시각(UTC). 미제공 시 null.</summary>
    public DateTimeOffset? DeployedAt { get; set; }
}
