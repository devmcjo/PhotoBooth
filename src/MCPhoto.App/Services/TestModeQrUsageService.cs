using System;
using System.Threading;
using System.Threading.Tasks;
using MCPhoto.Core.Accounts;
using MCPhoto.Core.Settings;

namespace MCPhoto.App.Services;

/// <summary>
/// 테스트 모드에서 TempUser QR 한도 상태를 <b>주입</b>하는 <see cref="IQrUsageService"/> 데코레이터(it23 §B7.4).
/// <para>
/// 왜 필요한가: 한도 조회는 서버 권위(Bearer 필수)라 토큰 없는 테스트 모드에서는 실패하고 fail-open으로 흐른다
/// → <b>TempUser 역할의 가장 특징적인 UI</b>(설정 화면 QR 토글 차단 + 사유 문구)가 절대 재현되지 않는다.
/// 화면에 아무 변화가 없어 QA가 "TempUser인데 QR이 그냥 되네?"로 오판한다.
/// </para>
/// <para>
/// 왜 셸에 <c>if (testMode)</c>를 넣지 않는가: 셸은 이미 테스트 모드 코드를 2곳(Startup·PIN 게이트) 갖는다.
/// 세 번째를 넣으면 셸이 테스트 모드의 저수지가 된다. 이 데코레이터 덕에
/// <c>AppShellViewModel.LoadTempUserQrStatusAsync</c>는 한 줄도 바뀌지 않는다.
/// </para>
/// <para>
/// ⚠️ 이것이 불변식 TM2("서버 응답을 위조하지 않는다")의 <b>유일한 예외</b>다. 근거: 이 값은 서버 판정이 아니라
/// 표시용 상태이고, 과금 안전은 서버가 업로드를 거부함으로써 담보된다(테스트 모드는 게스트 업로드만 가능하다).
/// </para>
/// </summary>
public sealed class TestModeQrUsageService : IQrUsageService
{
    private readonly ITestModeService _testMode;
    private readonly SessionContext _session;
    private readonly IQrUsageService _inner;

    public TestModeQrUsageService(ITestModeService testMode, SessionContext session, IQrUsageService inner)
    {
        _testMode = testMode;
        _session = session;
        _inner = inner;
    }

    /// <summary>
    /// 현재 세션 사용자가 <b>그</b> 테스트 계정이면 주입값, 아니면 <c>inner</c>에 그대로 위임한다.
    /// ⚠️ 판정이 <see cref="ITestModeService.IsTestUser"/>(참조 동일성)인 덕에, 테스트 모드가 켜진 채 실제 SSO
    /// 로그인이 일어나도 그 계정의 <b>실제 서버 한도</b>가 조회된다(주입값이 실계정에 적용되지 않는다).
    /// </summary>
    public Task<QrUsageStatus?> GetStatusAsync(CancellationToken ct = default)
    {
        if (!_testMode.IsTestUser(_session.CurrentUser))
            return _inner.GetStatusAsync(ct);   // 위임 경로는 inner의 계약(null=fail-open)을 그대로 승계한다

        var options = _testMode.Options;
        // RemainingTime·RemainingCount를 0으로 두는 이유: 읽는 화면이 하나도 없고(문구는 Reason만 본다)
        // 0이 "소진"의 자연스러운 표현이다.
        return Task.FromResult<QrUsageStatus?>(options.QrBlocked
            ? new QrUsageStatus(true, options.QrBlockReason, TimeSpan.Zero, 0)
            : QrUsageStatus.Unlimited);
    }
}
