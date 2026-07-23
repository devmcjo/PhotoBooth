using System.IO;
using MCPhoto.App;
using MCPhoto.App.Services;
using MCPhoto.App.ViewModels;
using MCPhoto.Core.Capture;
using MCPhoto.Core.Models;
using MCPhoto.Core.Navigation;
using MCPhoto.Core.Settings;

namespace MCPhoto.Tests;

/// <summary>
/// it8 A5: QR off→on 재활성 시 하위 토글 자동 on(VM 연동).
/// it9 C1: 카메라 열거(빈/비어있지 않음/저장 인덱스 보정) 및 ComboBox Disable 판정.
/// </summary>
public class SettingsViewModelTests
{
    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    /// <summary>테스트용 카메라 서비스 — EnumerateDevices만 의미 있음(나머지 no-op).</summary>
    private sealed class FakeCameraService : ICameraService
    {
        private readonly IReadOnlyList<CameraDevice> _devices;
        public FakeCameraService(params CameraDevice[] devices) => _devices = devices;

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
        public IReadOnlyList<CameraDevice> EnumerateDevices() => _devices;
        public void Dispose() { }
    }

    private sealed class FakeCameraTestDialog : ICameraTestDialogService
    {
        public int LastDeviceIndex { get; private set; } = -1;
        public Task ShowAsync(int deviceIndex) { LastDeviceIndex = deviceIndex; return Task.CompletedTask; }
    }

    private static SettingsViewModel MakeVm(ICameraService? camera = null, IniSettingsService? settings = null)
    {
        var session = new SessionContext();
        settings ??= new IniSettingsService(iniPath: Path.Combine(Path.GetTempPath(), $"svm_{Guid.NewGuid():N}.ini"));
        settings.Load();
        var shell = new AppShellViewModel(new IdleWatchdog(), settings, new EmptyServiceProvider(), session);
        camera ??= new FakeCameraService(new CameraDevice(0, "Camera 0"));
        return new SettingsViewModel(shell, settings, camera, new FakeCameraTestDialog());
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
        var vm = new SettingsViewModel(shell, settings, new FakeCameraService(new CameraDevice(0, "Camera 0")), new FakeCameraTestDialog());
        await vm.OnEnterAsync();

        Assert.True(vm.EnableQrDelivery);
        Assert.True(vm.SendPhoto);
        Assert.False(vm.SendTimelapse); // 로드값 보존(강제 on 안 됨)
    }

    // ── it9 C1: 카메라 열거 ──

    [Fact]
    public async Task No_Cameras_Disables_Combo()
    {
        var vm = MakeVm(camera: new FakeCameraService()); // 장치 0개
        await vm.OnEnterAsync();

        Assert.False(vm.HasCamera);
        Assert.Empty(vm.CameraDevices);
        Assert.False(vm.OpenCameraTestCommand.CanExecute(null) && vm.HasCamera); // 없으면 테스트 불가
    }

    [Fact]
    public async Task Cameras_Present_Populates_And_Enables()
    {
        var vm = MakeVm(camera: new FakeCameraService(new CameraDevice(0, "Camera 0"), new CameraDevice(1, "Camera 1")));
        await vm.OnEnterAsync();

        Assert.True(vm.HasCamera);
        Assert.Equal(2, vm.CameraDevices.Count);
    }

    [Fact]
    public async Task Saved_Index_Absent_Falls_Back_To_First()
    {
        // 저장된 카메라 인덱스가 5인데 연결된 건 0,1뿐 → 첫 장치(0)로 보정.
        var settings = new IniSettingsService(iniPath: Path.Combine(Path.GetTempPath(), $"svm_{Guid.NewGuid():N}.ini"));
        var s = settings.Load();
        s.CameraDevice = 5;
        settings.Save();

        var vm = MakeVm(camera: new FakeCameraService(new CameraDevice(0, "Camera 0"), new CameraDevice(1, "Camera 1")), settings: settings);
        await vm.OnEnterAsync();

        Assert.Equal(0, vm.CameraDevice);
    }
}
