using System.IO;
using MCPhoto.App;
using MCPhoto.App.ViewModels;
using MCPhoto.Core.Capture;
using MCPhoto.Core.Models;
using MCPhoto.Core.Navigation;
using MCPhoto.Core.Settings;

namespace MCPhoto.Tests;

/// <summary>
/// it11 #13: 전체 재촬영 게이트(설정 off 미노출·횟수 제한 도달 차단). 컷별 재촬영은 후속 이터레이션(제외).
/// </summary>
public class CutSelectViewModelTests
{
    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    private static FrameTemplate MakeFrame(int slots)
    {
        var f = new FrameTemplate { Id = "f1", Name = "test" };
        for (int i = 0; i < slots; i++)
            f.Slots.Add(new Slot { Index = i, X = i * 10, Y = 0, Width = 100, Height = 133 });
        return f;
    }

    /// <summary>재촬영 설정을 지정해 세션·셸을 구성한 CutSelectViewModel 생성.</summary>
    private static (CutSelectViewModel vm, SessionContext session) MakeVm(bool retakeEnabled, int retakeLimit)
    {
        var settings = new IniSettingsService(iniPath: Path.Combine(Path.GetTempPath(), $"csvm_{Guid.NewGuid():N}.ini"));
        var s = settings.Load();
        s.RetakeEnabled = retakeEnabled;
        s.RetakeLimit = retakeLimit;

        var session = new SessionContext();
        session.Capture.Begin(MakeFrame(2), 6);

        var shell = new AppShellViewModel(new IdleWatchdog(), settings, new EmptyServiceProvider(), session);
        return (new CutSelectViewModel(shell), session);
    }

    [Fact]
    public void RetakeDisabled_Hides_And_Blocks_FullRetake()
    {
        var (vm, _) = MakeVm(retakeEnabled: false, retakeLimit: 3);
        Assert.False(vm.RetakeEnabled);   // 버튼 미노출
        Assert.False(vm.CanFullRetake);   // 방어(설정 off면 항상 불가)
    }

    [Fact]
    public void RetakeEnabled_Allows_FullRetake_Until_Limit()
    {
        var (vm, session) = MakeVm(retakeEnabled: true, retakeLimit: 1);
        Assert.True(vm.RetakeEnabled);
        Assert.True(vm.CanFullRetake);    // 0회 소진 → 가능

        session.Capture.BeginFullRetake(); // 1회 소진(limit=1)
        Assert.False(vm.CanFullRetake);    // 도달 → 초과 차단
    }

    [Fact]
    public void RetakeEnabled_Higher_Limit_Allows_Multiple()
    {
        var (vm, session) = MakeVm(retakeEnabled: true, retakeLimit: 3);
        Assert.True(vm.CanFullRetake);

        session.Capture.BeginFullRetake();
        session.Capture.BeginFullRetake();
        Assert.True(vm.CanFullRetake);     // 2/3 → 아직 여유

        session.Capture.BeginFullRetake();
        Assert.False(vm.CanFullRetake);    // 3/3 도달 → 차단
    }

    [Fact]
    public async Task Retake_At_Limit_Does_Not_Increment()
    {
        // 방어: limit 도달 후 커맨드를 눌러도 카운터 증가·전이 없음(no-op).
        var (vm, session) = MakeVm(retakeEnabled: true, retakeLimit: 1);
        session.Capture.BeginFullRetake(); // 1회 소진(limit=1 도달)
        Assert.Equal(1, session.Capture.FullRetakeCount);

        await vm.RetakeCommand.ExecuteAsync(null);

        Assert.Equal(1, session.Capture.FullRetakeCount); // 증가하지 않음
    }
}
