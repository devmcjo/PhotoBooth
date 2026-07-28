using System.IO;
using MCPhoto.App;
using MCPhoto.Core.Accounts;
using MCPhoto.Core.Models;
using MCPhoto.Core.Navigation;
using MCPhoto.Core.Settings;

namespace MCPhoto.Tests;

/// <summary>
/// it13 §7.5: 셸이 역할+한도를 IsTempUserQrBlocked/TempUserQrReason으로 합성.
/// TempUser 로그인 시 IQrUsageService 1회 조회, 로그아웃·비TempUser는 항상 false(fail-open 포함).
/// </summary>
public class AppShellQrUsageTests
{
    /// <summary>IQrUsageService만 해석하는 최소 ServiceProvider(나머지 null → 셸이 방어).</summary>
    private sealed class QrUsageProvider : IServiceProvider
    {
        private readonly IQrUsageService _svc;
        public QrUsageProvider(IQrUsageService svc) => _svc = svc;
        public object? GetService(Type serviceType)
            => serviceType == typeof(IQrUsageService) ? _svc : null;
    }

    /// <summary>고정 상태를 반환하는 페이크(호출 횟수 관측).</summary>
    private sealed class FakeQrUsageService : IQrUsageService
    {
        private readonly QrUsageStatus? _status;
        public int Calls { get; private set; }
        public FakeQrUsageService(QrUsageStatus? status) => _status = status;
        public Task<QrUsageStatus?> GetStatusAsync(CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(_status);
        }
    }

    private static AppShellViewModel MakeShell(SessionContext session, IQrUsageService svc)
    {
        var settings = new IniSettingsService(iniPath: Path.Combine(Path.GetTempPath(), $"shell_{Guid.NewGuid():N}.ini"));
        settings.Load();
        return new AppShellViewModel(new IdleWatchdog(), settings, new QrUsageProvider(svc), session);
    }

    [Fact]
    public async Task TempUser_Blocked_Time_Sets_Flag_And_Reason()
    {
        var svc = new FakeQrUsageService(new QrUsageStatus(true, QrGateReason.Time, TimeSpan.Zero, 10));
        var session = new SessionContext();
        var shell = MakeShell(session, svc);

        session.Login(new User { Id = "t1", Role = UserRole.TempUser });
        // fire-and-forget 조회가 완료되도록 UI 컨텍스트 없는 테스트에서는 짧게 양보.
        await Task.Yield();
        await Task.Delay(20);

        Assert.Equal(1, svc.Calls);
        Assert.True(shell.IsTempUserQrBlocked);
        Assert.Equal(QrGateReason.Time, shell.TempUserQrReason);
    }

    [Fact]
    public async Task TempUser_Not_Blocked_Flag_False()
    {
        var svc = new FakeQrUsageService(new QrUsageStatus(false, QrGateReason.Ok, TimeSpan.FromHours(10), 5));
        var session = new SessionContext();
        var shell = MakeShell(session, svc);

        session.Login(new User { Id = "t2", Role = UserRole.TempUser });
        await Task.Delay(20);

        Assert.False(shell.IsTempUserQrBlocked);
        Assert.Equal(QrGateReason.Ok, shell.TempUserQrReason);
    }

    [Fact]
    public async Task NonTempUser_Does_Not_Query_And_Never_Blocked()
    {
        // 초과 상태를 반환하는 서비스여도, 비TempUser면 조회조차 안 하고 항상 false(역할 게이트).
        var svc = new FakeQrUsageService(new QrUsageStatus(true, QrGateReason.Time, TimeSpan.Zero, 0));
        var session = new SessionContext();
        var shell = MakeShell(session, svc);

        session.Login(new User { Id = "u1", Role = UserRole.User });
        await Task.Delay(20);

        Assert.Equal(0, svc.Calls);
        Assert.False(shell.IsTempUserQrBlocked);
        Assert.Equal(QrGateReason.Ok, shell.TempUserQrReason);
    }

    [Fact]
    public async Task Logout_Clears_Blocked_State()
    {
        var svc = new FakeQrUsageService(new QrUsageStatus(true, QrGateReason.Count, TimeSpan.FromHours(1), 0));
        var session = new SessionContext();
        var shell = MakeShell(session, svc);

        session.Login(new User { Id = "t3", Role = UserRole.TempUser });
        await Task.Delay(20);
        Assert.True(shell.IsTempUserQrBlocked);

        session.Logout();
        Assert.False(shell.IsTempUserQrBlocked);      // 로그아웃 시 즉시 클리어
        Assert.Equal(QrGateReason.Ok, shell.TempUserQrReason);
    }

    [Fact]
    public async Task TempUser_Server_Unreachable_Fails_Open()
    {
        // 서버 미도달 → 서비스가 null 반환 → 셸은 false(fail-open, 서버가 업로드에서 최종 거부).
        var svc = new FakeQrUsageService(status: null);
        var session = new SessionContext();
        var shell = MakeShell(session, svc);

        session.Login(new User { Id = "t4", Role = UserRole.TempUser });
        await Task.Delay(20);

        Assert.Equal(1, svc.Calls);
        Assert.False(shell.IsTempUserQrBlocked);       // fail-open
    }

    // ── it13 §7.4/§11: ResultViewModel.Next가 조합하는 effective 게이트 = QrEffectivePolicy(raw + 로그인 + 한도) ──
    // 셸 상태(IsLoggedIn/IsTempUserQrBlocked)를 실제로 구성해 Next의 판정을 재현한다(네비게이션 하네스 없이 결정만 검증).

    [Fact]
    public async Task TempUser_Blocked_Effective_Qr_False_But_Raw_Setting_Unchanged()
    {
        var svc = new FakeQrUsageService(new QrUsageStatus(true, QrGateReason.Time, TimeSpan.Zero, 0));
        var session = new SessionContext();
        var settings = new IniSettingsService(iniPath: Path.Combine(Path.GetTempPath(), $"shell_{Guid.NewGuid():N}.ini"));
        settings.Load();
        settings.Current.EnableQrDelivery = true;   // 운영자가 QR on으로 설정(부스 설정)
        var shell = new AppShellViewModel(new IdleWatchdog(), settings, new QrUsageProvider(svc), session);

        session.Login(new User { Id = "t5", Role = UserRole.TempUser });
        await Task.Delay(20);

        // Next가 계산하는 것과 동일한 조합.
        bool effective = QrEffectivePolicy.IsQrEnabled(
            settings.Current.EnableQrDelivery, shell.IsLoggedIn, shell.IsTempUserQrBlocked);
        Assert.False(effective);                        // TempUser 초과 → Qr 미진입(Done)
        Assert.True(settings.Current.EnableQrDelivery); // ★ ini raw는 불변(오버라이드만) — 한도 해제 시 원복
    }

    [Fact]
    public async Task Normal_TempUser_Effective_Qr_Follows_Raw_Setting()
    {
        var svc = new FakeQrUsageService(new QrUsageStatus(false, QrGateReason.Ok, TimeSpan.FromHours(10), 5));
        var session = new SessionContext();
        var settings = new IniSettingsService(iniPath: Path.Combine(Path.GetTempPath(), $"shell_{Guid.NewGuid():N}.ini"));
        settings.Load();
        settings.Current.EnableQrDelivery = true;
        var shell = new AppShellViewModel(new IdleWatchdog(), settings, new QrUsageProvider(svc), session);

        session.Login(new User { Id = "t6", Role = UserRole.TempUser });
        await Task.Delay(20);

        bool effective = QrEffectivePolicy.IsQrEnabled(
            settings.Current.EnableQrDelivery, shell.IsLoggedIn, shell.IsTempUserQrBlocked);
        Assert.True(effective);   // 정상 TempUser는 User와 동일 — raw 그대로
    }
}
