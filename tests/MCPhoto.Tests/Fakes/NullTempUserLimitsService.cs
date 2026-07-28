using MCPhoto.Core.Accounts;

namespace MCPhoto.Tests.Fakes;

/// <summary>
/// 전역 TempUser 한도 서비스 테스트 스텁 — 조회는 기본값(48h/30회), 수정은 무시.
/// it15 D-B: 레거시 어셈블리의 프로덕션 no-op 구현을 삭제하고
/// 테스트 전용으로 이관했다. 프로덕션 DI에 "조용히 무제한 허용" 폴백을 남기지 않기 위함.
/// </summary>
public sealed class NullTempUserLimitsService : ITempUserLimitsService
{
    public Task<TempUserLimits> GetLimitsAsync(CancellationToken ct = default)
        => Task.FromResult(TempUserLimits.Default);

    public Task SetLimitsAsync(TempUserLimits limits, CancellationToken ct = default)
        => Task.CompletedTask;
}
