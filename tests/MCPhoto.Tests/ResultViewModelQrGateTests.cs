using System.IO;
using MCPhoto.App;
using MCPhoto.App.ViewModels;
using MCPhoto.Core.Accounts;
using MCPhoto.Core.Capture;
using MCPhoto.Core.Models;
using MCPhoto.Core.Navigation;
using MCPhoto.Core.Settings;

namespace MCPhoto.Tests;

/// <summary>
/// it13 §7.4/§11: ResultViewModel.Next의 런타임 QR 게이트(QrEffectivePolicy 단일 지점) 실행 검증.
/// ★ 핵심 불변식: TempUser 초과여도 Next 실행 전후로 ini `EnableQrDelivery`가 불변(오버라이드만, write 없음).
/// Next의 네비게이션은 최소 하네스에서 실패할 수 있으나(catch가 삼킴), QR 분기 판정·ini 불변은 그 전에 확정된다.
/// </summary>
public class ResultViewModelQrGateTests
{
    private sealed class QrUsageProvider : IServiceProvider
    {
        private readonly IQrUsageService _svc;
        public QrUsageProvider(IQrUsageService svc) => _svc = svc;
        public object? GetService(Type serviceType)
            => serviceType == typeof(IQrUsageService) ? _svc : null;
    }

    private sealed class FakeQrUsageService : IQrUsageService
    {
        private readonly QrUsageStatus? _status;
        public FakeQrUsageService(QrUsageStatus? status) => _status = status;
        public Task<QrUsageStatus?> GetStatusAsync(CancellationToken ct = default) => Task.FromResult(_status);
    }

    // ── Next의 QR 분기 경로에서 호출되지 않는(video 없음·saveLocalCopy off) no-op 스텁들 ──
    private sealed class StubComposition : ICompositionService
    {
        public Task<string> ComposeAsync(FrameTemplate frame, IReadOnlyList<CapturedStill> cuts, FilterKind filter, string outputPath, CancellationToken ct = default)
            => Task.FromResult(outputPath);
    }
    private sealed class StubTimelapse : ITimelapseService
    {
        public Task<string?> CreateTimelapseAsync(string sessionVideoPath, string outputPath, CancellationToken ct = default)
            => Task.FromResult<string?>(null);
    }
    private sealed class StubLocalSave : MCPhoto.Core.LocalSave.ILocalSaveService
    {
        public Task<string?> SaveAsync(string localSavePath, string finalImagePath, string? timelapsePath, DateTime sessionTime, CancellationToken ct = default)
            => Task.FromResult<string?>(null);
    }
    private sealed class StubCamera : ICameraService
    {
        public event EventHandler<CameraFrame>? FrameReady { add { } remove { } }
        public double CurrentFps => 30;
        public bool IsRunning => false;
        public Task<bool> StartAsync(int deviceIndex, double targetAspect, bool mirror, CancellationToken ct = default) => Task.FromResult(true);
        public Task StopAsync() => Task.CompletedTask;
        public void SetMirror(bool mirror) { }
        public void SetTargetAspect(double aspect) { }
        public Task<CapturedStill> CaptureStillAsync(CancellationToken ct = default) => Task.FromResult(new CapturedStill());
        public void StartRecording(string outputPath) { }
        public Task StopRecordingAsync() => Task.CompletedTask;
        public IReadOnlyList<CameraDevice> EnumerateDevices() => Array.Empty<CameraDevice>();
        public void Dispose() { }
    }

    private static (ResultViewModel vm, IniSettingsService settings, SessionContext session) MakeVm(
        QrUsageStatus status, UserRole role, bool rawQrOn)
    {
        var settings = new IniSettingsService(iniPath: Path.Combine(Path.GetTempPath(), $"rvm_{Guid.NewGuid():N}.ini"));
        settings.Load();
        settings.Current.EnableQrDelivery = rawQrOn;
        settings.Current.SaveLocalCopy = false;   // 로컬 저장 스킵(QR 분기까지 바로)

        var session = new SessionContext { FinalImagePath = "final.jpg" }; // video 없음 → 타임랩스 스킵
        var shell = new AppShellViewModel(new IdleWatchdog(), settings,
            new QrUsageProvider(new FakeQrUsageService(status)), session);
        session.Login(new User { Id = "u", Role = role });

        var vm = new ResultViewModel(shell, new StubComposition(), new StubTimelapse(),
            new StubLocalSave(), new StubCamera());
        return (vm, settings, session);
    }

    [Fact]
    public async Task Blocked_TempUser_Next_Does_Not_Mutate_Ini_Qr()
    {
        // 운영자 QR on + TempUser 시간 초과 → effective off(Qr 미진입)지만 ini는 불변.
        var (vm, settings, _) = MakeVm(
            new QrUsageStatus(true, QrGateReason.Time, TimeSpan.Zero, 0), UserRole.TempUser, rawQrOn: true);
        await Task.Delay(20); // 셸 사용량 조회 완료

        Assert.True(settings.Current.EnableQrDelivery);   // 실행 전
        await vm.NextCommand.ExecuteAsync(null);          // 네비게이션은 최소 하네스에서 실패할 수 있으나 catch가 삼킴
        Assert.True(settings.Current.EnableQrDelivery);   // ★ 실행 후에도 ini 원값 불변(오버라이드만)

        // 디스크에 재로드해도 원값(어떤 경로에서도 write 안 함).
        var reloaded = new IniSettingsService(iniPath: settings.IniPath).Load();
        Assert.True(reloaded.EnableQrDelivery);
    }

    [Fact]
    public async Task Normal_TempUser_Next_Does_Not_Mutate_Ini_Qr()
    {
        // 정상 TempUser → effective on(Qr 진입 시도)이어도 ini는 여전히 불변.
        var (vm, settings, _) = MakeVm(
            new QrUsageStatus(false, QrGateReason.Ok, TimeSpan.FromHours(10), 5), UserRole.TempUser, rawQrOn: true);
        await Task.Delay(20);

        await vm.NextCommand.ExecuteAsync(null);
        Assert.True(settings.Current.EnableQrDelivery);   // ini 불변
    }
}
