using System.IO;
using MCPhoto.App;
using MCPhoto.App.ViewModels;
using MCPhoto.Core.Navigation;
using MCPhoto.Core.Settings;

namespace MCPhoto.Tests;

/// <summary>it8 Step 5 (A5): QR off→on 재활성 시 하위 토글 둘 다 자동 on(VM 연동).</summary>
public class SettingsViewModelTests
{
    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    private static SettingsViewModel MakeVm()
    {
        var session = new SessionContext();
        var settings = new IniSettingsService(iniPath: Path.Combine(Path.GetTempPath(), $"svm_{Guid.NewGuid():N}.ini"));
        settings.Load();
        var shell = new AppShellViewModel(new IdleWatchdog(), settings, new EmptyServiceProvider(), session);
        return new SettingsViewModel(shell, settings);
    }

    [Fact]
    public async Task Qr_Off_Then_On_Forces_Both_Sub_Toggles_On()
    {
        var vm = MakeVm();
        await vm.OnEnterAsync();

        // QR on 상태에서 하위 둘 다 끄면 → 연동으로 QR off(it7)
        vm.SendPhoto = false;
        vm.SendTimelapse = false;
        Assert.False(vm.EnableQrDelivery);

        // 다시 QR on → 하위 둘 다 자동 on(it8 A5)
        vm.EnableQrDelivery = true;
        Assert.True(vm.SendPhoto);
        Assert.True(vm.SendTimelapse);
    }

    [Fact]
    public async Task Load_Does_Not_Trigger_ReEnable_Override()
    {
        // 저장값(QR on, 사진만 on)이 로드 시 off→on 강제로 덮이지 않아야.
        var settings = new IniSettingsService(iniPath: Path.Combine(Path.GetTempPath(), $"svm_{Guid.NewGuid():N}.ini"));
        var s = settings.Load();
        s.EnableQrDelivery = true;
        s.SendPhoto = true;
        s.SendTimelapse = false;
        settings.Save();

        var session = new SessionContext();
        var shell = new AppShellViewModel(new IdleWatchdog(), settings, new EmptyServiceProvider(), session);
        var vm = new SettingsViewModel(shell, settings);
        await vm.OnEnterAsync();

        Assert.True(vm.EnableQrDelivery);
        Assert.True(vm.SendPhoto);
        Assert.False(vm.SendTimelapse); // 로드값 보존(강제 on 안 됨)
    }
}
