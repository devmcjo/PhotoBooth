using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MCPhoto.App;
using MCPhoto.App.Services;
using MCPhoto.Core.Accounts;
using MCPhoto.Core.Models;
using MCPhoto.Core.Navigation;
using MCPhoto.Core.Settings;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MCPhoto.Tests;

/// <summary>
/// it23 §B7.4: TempUser QR 한도 상태 주입 데코레이터.
/// <para>
/// 왜 이 데코레이터가 있는가: 한도 조회는 서버 권위라 토큰 없는 테스트 모드에서는 실패하고 fail-open으로 흐른다
/// → TempUser 역할의 가장 특징적인 UI(QR 토글 차단 + 사유 문구)가 절대 재현되지 않아 QA가
/// "TempUser인데 QR이 그냥 되네?"로 오판한다.
/// </para>
/// ⚠️ 위임 분기가 <c>IsTestUser</c>(참조 동일성)인 것이 규격이다 — 실계정에 주입값이 적용되면 표시가 거짓이 된다.
/// </summary>
public class TestModeQrUsageServiceTests
{
    /// <summary>inner 호출 횟수와 반환값을 주입하는 스텁.</summary>
    private sealed class SpyQrUsageService : IQrUsageService
    {
        public int Calls { get; private set; }
        public QrUsageStatus? Result { get; init; }

        public Task<QrUsageStatus?> GetStatusAsync(CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(Result);
        }
    }

    /// <summary>임시 ini로 실제 <see cref="TestModeService"/>를 만든다(참조 동일성까지 프로덕션 경로 그대로).</summary>
    private static (ITestModeService testMode, string dir) MakeTestMode(string section)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"mcphoto_qr_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "MCPhoto.ini");
        File.WriteAllText(path, section);
        var settings = new IniSettingsService(iniPath: path, fallbackCandidates: new[] { path });
        return (new TestModeService(settings), dir);
    }

    private static void Cleanup(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch { }
    }

    /// <summary>B-T20: 테스트 계정 + <c>QrBlocked=1</c> → 주입값이 나오고 inner는 호출되지 않는다.</summary>
    [Theory]
    [InlineData("time", QrGateReason.Time)]
    [InlineData("count", QrGateReason.Count)]
    public async Task T20_Injects_Blocked_Status_For_Test_User(string reasonText, QrGateReason expected)
    {
        var (testMode, dir) = MakeTestMode(
            $"[Test]\nTestMode=1\nRole=temp_user\nQrBlocked=1\nQrBlockReason={reasonText}\n");
        try
        {
            var session = new SessionContext();
            session.Login(testMode.TestUser!);
            var inner = new SpyQrUsageService { Result = QrUsageStatus.Unlimited };
            var svc = new TestModeQrUsageService(testMode, session, inner);

            var status = await svc.GetStatusAsync();

            Assert.NotNull(status);
            Assert.True(status!.Blocked);
            Assert.Equal(expected, status.Reason);
            Assert.Equal(0, inner.Calls);   // 서버 조회를 하지 않는다(토큰이 없어 무의미하다)
        }
        finally { Cleanup(dir); }
    }

    /// <summary><c>QrBlocked=0</c>(기본)이면 한도 없음 — 평시 TempUser 흐름을 막지 않는다.</summary>
    [Fact]
    public async Task Unblocked_Test_User_Gets_Unlimited()
    {
        var (testMode, dir) = MakeTestMode("[Test]\nTestMode=1\nRole=temp_user\n");
        try
        {
            var session = new SessionContext();
            session.Login(testMode.TestUser!);
            var inner = new SpyQrUsageService();
            var svc = new TestModeQrUsageService(testMode, session, inner);

            var status = await svc.GetStatusAsync();

            Assert.NotNull(status);
            Assert.False(status!.Blocked);
            Assert.Equal(QrGateReason.Ok, status.Reason);
            Assert.Equal(0, inner.Calls);
        }
        finally { Cleanup(dir); }
    }

    /// <summary>
    /// B-T21: 테스트 계정이 <b>아니면</b> inner에 그대로 위임한다 —
    /// ① 게스트(CurrentUser=null) ② 실제 계정(다른 인스턴스). 주입값이 실계정에 적용되면 표시가 거짓이 된다.
    /// </summary>
    [Fact]
    public async Task T21_Delegates_To_Inner_For_Guest_And_Real_Account()
    {
        var (testMode, dir) = MakeTestMode(
            "[Test]\nTestMode=1\nId=qa\nEmail=qa@example.com\nRole=temp_user\nQrBlocked=1\n");
        try
        {
            var session = new SessionContext();
            var inner = new SpyQrUsageService { Result = QrUsageStatus.Unlimited };
            var svc = new TestModeQrUsageService(testMode, session, inner);

            // ① 게스트
            var guest = await svc.GetStatusAsync();
            Assert.Equal(1, inner.Calls);
            Assert.False(guest!.Blocked);

            // ② 값이 전부 같은 별 인스턴스(실제 SSO 로그인 상당)
            session.Login(new User { Id = "qa", Email = "qa@example.com", Role = UserRole.TempUser });
            var real = await svc.GetStatusAsync();
            Assert.Equal(2, inner.Calls);
            Assert.False(real!.Blocked);   // 주입값(Blocked=true)이 적용되지 않았다
        }
        finally { Cleanup(dir); }
    }

    /// <summary>위임 경로는 inner의 계약(null=fail-open)을 그대로 승계한다(데코레이터가 예외를 만들지 않는다).</summary>
    [Fact]
    public async Task Delegation_Preserves_Null_Fail_Open()
    {
        var (testMode, dir) = MakeTestMode("[Test]\nTestMode=1\nRole=temp_user\nQrBlocked=1\n");
        try
        {
            var session = new SessionContext();   // 게스트
            var svc = new TestModeQrUsageService(testMode, session, new SpyQrUsageService { Result = null });

            Assert.Null(await svc.GetStatusAsync());
        }
        finally { Cleanup(dir); }
    }

    // ── 합성 루트 배선(§B7.4) ──
    // ⚠️ 데코레이션은 "마지막 등록이 이긴다"에 의존한다. 등록 순서가 바뀌거나 한 줄이 빠지면 **런타임에만**
    //    조용히 깨진다(주입이 반영되지 않고 서버 조회가 실패해 fail-open) → 실제 조립으로 고정한다.

    /// <summary>테스트 모드 ON이면 <see cref="IQrUsageService"/>가 데코레이터로 해석된다.</summary>
    [Fact]
    public void Composition_Root_Decorates_When_Test_Mode_On()
        => AssertResolvedQrService("[Test]\nTestMode=1\nRole=temp_user\nQrBlocked=1\n",
            expectDecorated: true);

    /// <summary>
    /// 테스트 모드 OFF면 데코레이터를 <b>아예 만들지 않고</b> HTTP 구현을 그대로 돌려준다 —
    /// 평시 경로에 테스트 모드 코드가 한 줄도 끼지 않는다.
    /// </summary>
    [Fact]
    public void Composition_Root_Skips_Decorator_When_Test_Mode_Off()
        => AssertResolvedQrService("[MCPhoto]\nCutCount=8\n", expectDecorated: false);

    private static void AssertResolvedQrService(string iniContent, bool expectDecorated)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"mcphoto_di_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var iniPath = Path.Combine(dir, "MCPhoto.ini");
        File.WriteAllText(iniPath, iniContent);
        try
        {
            var services = new ServiceCollection();
            services.AddLogging();
            ServiceRegistration.Register(services);
            // 실제 ini 경로 해석(쓰기 가능 후보 탐색·프로브 파일)을 피하고 이 테스트의 파일을 읽게 한다.
            // ⚠️ Register **뒤**에 등록해야 이긴다.
            services.AddSingleton<ISettingsService>(new FixedPathSettingsService(iniPath));

            using var provider = services.BuildServiceProvider();

            var qr = provider.GetRequiredService<IQrUsageService>();
            if (expectDecorated) Assert.IsType<TestModeQrUsageService>(qr);
            else Assert.IsNotType<TestModeQrUsageService>(qr);

            // 새 서비스가 실제로 해석되는지 함께 확인(선택 파라미터라 미등록이 조용히 통과한다).
            Assert.Equal(expectDecorated, provider.GetRequiredService<ITestModeService>().IsEnabled);
            Assert.NotNull(provider.GetRequiredService<ILicenseNoticeService>());
        }
        finally { Cleanup(dir); }
    }

    /// <summary>ini 경로만 고정하는 설정 서비스(실제 후보 탐색·쓰기 프로브를 하지 않는다).</summary>
    private sealed class FixedPathSettingsService : ISettingsService
    {
        private readonly IniSettingsService _inner;
        public FixedPathSettingsService(string iniPath)
        {
            _inner = new IniSettingsService(iniPath: iniPath, fallbackCandidates: new[] { iniPath });
            _inner.Load();
        }
        public AppSettings Current => _inner.Current;
        public string IniPath => _inner.IniPath;
        public AppSettings Load() => _inner.Load();
        public bool Save() => _inner.Save();
    }

    /// <summary>
    /// 셸의 한도 게이트가 주입값을 그대로 반영한다(설정 화면 QR 차단 문구의 입력).
    /// ⚠️ <c>AppShellViewModel.LoadTempUserQrStatusAsync</c>는 한 줄도 바뀌지 않았다 — 데코레이터가
    /// 셸을 테스트 모드의 저수지로 만들지 않았음을 이 경로가 확인한다.
    /// </summary>
    [Fact]
    public async Task Shell_Reflects_Injected_Block_State()
    {
        var (testMode, dir) = MakeTestMode(
            "[Test]\nTestMode=1\nRole=temp_user\nQrBlocked=1\nQrBlockReason=time\n");
        try
        {
            var iniPath = Path.Combine(dir, "MCPhoto.ini");
            var settings = new IniSettingsService(iniPath: iniPath, fallbackCandidates: new[] { iniPath });
            settings.Load();
            var session = new SessionContext();
            var decorated = new TestModeQrUsageService(testMode, session, new SpyQrUsageService());
            var services = new Fakes.MapServiceProvider().Add<IQrUsageService>(decorated);
            var shell = new AppShellViewModel(new IdleWatchdog(), settings, services, session,
                logger: null, testMode: testMode);

            shell.Startup();                       // 테스트 계정 로그인 → 셸이 한도 조회를 시작한다
            await Task.Yield();                    // fire-and-forget 조회 완료 대기(동기 스텁이라 즉시 끝난다)

            Assert.True(shell.IsTempUserQrBlocked);
            Assert.Equal(QrGateReason.Time, shell.TempUserQrReason);
        }
        finally { Cleanup(dir); }
    }
}
