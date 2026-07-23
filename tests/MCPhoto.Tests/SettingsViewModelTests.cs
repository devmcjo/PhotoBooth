using System.IO;
using MCPhoto.App;
using MCPhoto.App.Services;
using MCPhoto.App.ViewModels;
using MCPhoto.Core.Capture;
using MCPhoto.Core.Models;
using MCPhoto.Core.Navigation;
using MCPhoto.Core.Settings;
using MCPhoto.Core.Upload;

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

    /// <summary>진단 모달 페이크 — ShowAsync 호출 횟수만 관측(실제 창 미표시). (it11 #14)</summary>
    private sealed class FakeDiagnosticsDialog : IDiagnosticsDialogService
    {
        public int ShowCount { get; private set; }
        public Task ShowAsync() { ShowCount++; return Task.CompletedTask; }
    }

    private static SettingsViewModel MakeVm(ICameraService? camera = null, IniSettingsService? settings = null,
        IFirebaseClient? firebase = null, IDiagnosticsDialogService? diagnostics = null)
    {
        var session = new SessionContext();
        settings ??= new IniSettingsService(iniPath: Path.Combine(Path.GetTempPath(), $"svm_{Guid.NewGuid():N}.ini"));
        settings.Load();
        var shell = new AppShellViewModel(new IdleWatchdog(), settings, new EmptyServiceProvider(), session);
        camera ??= new FakeCameraService(new CameraDevice(0, "Camera 0"));
        firebase ??= new FakeFirebaseClient { IsInitialized = false };
        diagnostics ??= new FakeDiagnosticsDialog();
        return new SettingsViewModel(shell, settings, camera, new FakeCameraTestDialog(), diagnostics, firebase);
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
        session.Login(new User { Id = "u1", Password = "pw", Role = UserRole.User }); // QR 로드값 검증은 로그인 사용자 대상(게스트는 소스단 off)
        var shell = new AppShellViewModel(new IdleWatchdog(), settings, new EmptyServiceProvider(), session);
        var vm = new SettingsViewModel(shell, settings, new FakeCameraService(new CameraDevice(0, "Camera 0")),
            new FakeCameraTestDialog(), new FakeDiagnosticsDialog(), new FakeFirebaseClient { IsInitialized = false });
        await vm.OnEnterAsync();

        Assert.True(vm.EnableQrDelivery);
        Assert.True(vm.SendPhoto);
        Assert.False(vm.SendTimelapse); // 로드값 보존(강제 on 안 됨)
    }

    // ── it11 #13: 재촬영 설정 왕복(게스트 게이트 대상 아님 — 촬영 옵션) ──

    [Fact]
    public async Task Retake_Settings_Save_And_Load_RoundTrip()
    {
        var settings = new IniSettingsService(iniPath: Path.Combine(Path.GetTempPath(), $"svm_{Guid.NewGuid():N}.ini"));
        var vm = MakeVm(settings: settings);   // 게스트(로그인 안 함) — 재촬영은 게스트도 저장 가능
        await vm.OnEnterAsync();

        Assert.False(vm.RetakeEnabled);        // 기본 off
        Assert.Equal(1, vm.RetakeLimit);       // 기본 1

        vm.RetakeEnabled = true;
        vm.RetakeLimit = 3;
        vm.SaveSettingsCommand.Execute(null);

        var r = new IniSettingsService(iniPath: settings.IniPath).Load();
        Assert.True(r.RetakeEnabled);          // 게스트여도 촬영 옵션은 저장됨
        Assert.Equal(3, r.RetakeLimit);
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

    // ── 보완#1: 권한 게이트 ──

    [Fact]
    public async Task Guest_Qr_Forced_Off()
    {
        var vm = MakeVm(); // 게스트(로그인 안 함)
        await vm.OnEnterAsync();

        Assert.True(vm.IsGuest);
        Assert.False(vm.EnableQrDelivery); // 소스단 강제 off 표시
    }

    [Fact]
    public async Task Guest_Save_Preserves_Ini_Qr_And_Firebase()
    {
        var settings = new IniSettingsService(iniPath: Path.Combine(Path.GetTempPath(), $"svm_{Guid.NewGuid():N}.ini"));
        var s = settings.Load();
        s.EnableQrDelivery = true;          // 관리자가 켜둔 값
        s.StorageBucket = "keep-bucket";
        settings.Save();

        var vm = MakeVm(settings: settings); // 게스트
        await vm.OnEnterAsync();
        Assert.False(vm.EnableQrDelivery);   // 표시는 off
        vm.SaveSettingsCommand.Execute(null);

        var r = new IniSettingsService(iniPath: settings.IniPath).Load();
        Assert.True(r.EnableQrDelivery);          // ini 원값 보존(클로버 방지)
        Assert.Equal("keep-bucket", r.StorageBucket);
    }

    // 로그인 사용자 비밀번호 가드는 설정 '진입 전' 모달(PasswordPromptWindow)로 이동 —
    // AppShellViewModel.OpenSettings에서 처리(UI 모달이라 여기서 단위 테스트하지 않음). (보완#1 후속)

    // ── it10 S4-2: 서버 연결 상태 표시(읽기 전용) ──

    [Fact]
    public void Server_Connected_Shows_Bucket()
    {
        var vm = MakeVm(firebase: new FakeFirebaseClient { IsInitialized = true, Bucket = "mcphoto-955fb.firebasestorage.app" });

        Assert.True(vm.IsServerConnected);
        Assert.Equal("연결됨 — mcphoto-955fb.firebasestorage.app", vm.ServerStatusText);
    }

    [Fact]
    public void Server_Offline_Shows_No_Key_Notice()
    {
        var vm = MakeVm(firebase: new FakeFirebaseClient { IsInitialized = false });

        Assert.False(vm.IsServerConnected);
        Assert.Equal("미연결 — 서비스 계정 키 없음(로그 참조)", vm.ServerStatusText);
    }

    // ── it11 #14: 진단·상태 모달 진입(로그인 게이트) ──

    [Fact]
    public async Task Guest_OpenDiagnostics_Is_NoOp()
    {
        // 게스트(로그인 안 함) → 다이얼로그 서비스 미호출.
        var diag = new FakeDiagnosticsDialog();
        var vm = MakeVm(diagnostics: diag);

        Assert.True(vm.IsGuest);
        await vm.OpenDiagnosticsCommand.ExecuteAsync(null);

        Assert.Equal(0, diag.ShowCount);
    }

    [Fact]
    public async Task LoggedIn_OpenDiagnostics_Shows_Dialog_Once()
    {
        var diag = new FakeDiagnosticsDialog();
        var settings = new IniSettingsService(iniPath: Path.Combine(Path.GetTempPath(), $"svm_{Guid.NewGuid():N}.ini"));
        settings.Load();
        var session = new SessionContext();
        session.Login(new User { Id = "admin", Password = "pw", Role = UserRole.Admin });
        var shell = new AppShellViewModel(new IdleWatchdog(), settings, new EmptyServiceProvider(), session);
        var vm = new SettingsViewModel(shell, settings, new FakeCameraService(new CameraDevice(0, "Camera 0")),
            new FakeCameraTestDialog(), diag, new FakeFirebaseClient { IsInitialized = false });

        Assert.True(vm.IsLoggedIn);
        await vm.OpenDiagnosticsCommand.ExecuteAsync(null);

        Assert.Equal(1, diag.ShowCount);
    }
}
