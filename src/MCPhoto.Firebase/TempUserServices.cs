using MCPhoto.Core.Accounts;

namespace MCPhoto.Firebase;

/// <summary>
/// 레거시(UseBackend off) TempUser 사용량 서비스 no-op(설계 §12). Firebase(Admin) 경로에는 한도 강제
/// 인프라(서버 트랜잭션·config)가 없으므로 항상 <see cref="QrUsageStatus.Unlimited"/>(TempUser 한도 미적용).
/// 과금 방어는 백엔드 온라인 운영 전제(it10에서 백엔드 기본 ON 확정).
/// </summary>
public sealed class NullQrUsageService : IQrUsageService
{
    public Task<QrUsageStatus?> GetStatusAsync(CancellationToken ct = default)
        => Task.FromResult<QrUsageStatus?>(QrUsageStatus.Unlimited);
}

/// <summary>
/// 레거시(UseBackend off) 전역 한도 서비스 no-op(설계 §12). 조회는 기본값(48h/30회), 수정은 무시.
/// 레거시엔 한도 강제 인프라가 없으므로 값은 표시상 의미만 갖는다.
/// </summary>
public sealed class NullTempUserLimitsService : ITempUserLimitsService
{
    public Task<TempUserLimits> GetLimitsAsync(CancellationToken ct = default)
        => Task.FromResult(TempUserLimits.Default);   // 48h/30회 단일 소스(§4.3)

    public Task SetLimitsAsync(TempUserLimits limits, CancellationToken ct = default)
        => Task.CompletedTask;   // 레거시엔 강제 인프라 없음 — no-op
}
