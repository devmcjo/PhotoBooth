using System.IO;
using MCPhoto.App;
using MCPhoto.App.Services;
using MCPhoto.App.ViewModels;
using MCPhoto.Capture;
using MCPhoto.Core.Capture;
using MCPhoto.Core.Models;
using MCPhoto.Core.Settings;
using MCPhoto.Core.Upload;

namespace MCPhoto.Tests;

/// <summary>
/// it11 #14: 진단·상태 VM 헬스체크 조립 + LogFolderService 경로 산출.
/// 실제 explorer 실행/서버 실호출은 금지 — 페이크/주입 경계로 검증(A3 스모크 포함).
/// it15 §6.6: "서비스 계정 키 후보 경로" 항목이 사라지고 "서버 연결(백엔드)" 항목으로 재구성됐다.
/// </summary>
public class DiagnosticsViewModelTests
{
    private sealed class StubSettingsService : ISettingsService
    {
        private readonly AppSettings _settings;
        public StubSettingsService(AppSettings settings) => _settings = settings;
        public AppSettings Current => _settings;
        public AppSettings Load() => _settings;
        public bool Save() => true;
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

    /// <summary>로그 폴더 서비스 페이크 — 경로 주입 + OpenLogFolder 호출 관측(실제 프로세스 미실행).</summary>
    private sealed class FakeLogFolderService : ILogFolderService
    {
        public FakeLogFolderService(string path) => LogFolderPath = path;
        public string LogFolderPath { get; }
        public int OpenCount { get; private set; }
        public void OpenLogFolder() => OpenCount++;
    }

    private static DiagnosticsViewModel MakeVm(
        ICameraService? camera = null,
        FfmpegRunner? ffmpeg = null,
        IFirebaseClient? firebase = null,
        ILogFolderService? logFolder = null,
        AppSettings? settings = null,
        User? loginUser = null)
    {
        camera ??= new FakeCameraService();
        // 존재하지 않는 경로를 명시 주입 → FfmpegAvailable=false로 결정적(실제 번들 유무와 무관).
        ffmpeg ??= new FfmpegRunner(ffmpegPath: Path.Combine(Path.GetTempPath(), "no-such-ffmpeg.exe"));
        firebase ??= new FakeFirebaseClient { IsInitialized = false };
        logFolder ??= new FakeLogFolderService(Path.Combine(Path.GetTempPath(), "logs"));
        settings ??= new AppSettings();
        var session = new SessionContext();
        if (loginUser is not null) session.Login(loginUser);
        return new DiagnosticsViewModel(camera, ffmpeg, firebase, logFolder, new StubSettingsService(settings), session);
    }

    [Fact]
    public async Task RefreshCameras_Two_Devices_Populates()
    {
        var vm = MakeVm(camera: new FakeCameraService(new CameraDevice(0, "Logitech"), new CameraDevice(1, "Elgato")));

        await vm.RefreshCamerasCommand.ExecuteAsync(null);

        Assert.Equal(2, vm.CameraCount);
        Assert.Equal(2, vm.Cameras.Count);
        Assert.True(vm.HasCamera);
        Assert.Equal("2대 연결됨", vm.CameraSummary);
        Assert.False(vm.IsCheckingCamera);
    }

    [Fact]
    public async Task RefreshCameras_No_Device_Shows_Disconnected()
    {
        var vm = MakeVm(camera: new FakeCameraService()); // 0대

        await vm.RefreshCamerasCommand.ExecuteAsync(null);

        Assert.Equal(0, vm.CameraCount);
        Assert.Empty(vm.Cameras);
        Assert.False(vm.HasCamera);
        Assert.Equal("미연결", vm.CameraSummary);
    }

    [Fact]
    public void Ffmpeg_Missing_Path_Reports_Unavailable()
    {
        var missing = Path.Combine(Path.GetTempPath(), "no-such-ffmpeg.exe");
        var vm = MakeVm(ffmpeg: new FfmpegRunner(ffmpegPath: missing));

        Assert.False(vm.FfmpegAvailable);
        Assert.Equal(missing, vm.FfmpegPath);
    }

    [Fact]
    public void Backend_Configured_Reports_Bucket()
    {
        var vm = MakeVm(firebase: new FakeFirebaseClient { IsInitialized = true, Bucket = "b.appspot.com" });

        Assert.True(vm.IsBackendConfigured);
        Assert.Equal("b.appspot.com", vm.FirebaseBucket);
    }

    [Fact]
    public void Backend_Unconfigured_Shows_Placeholder()
    {
        var vm = MakeVm(firebase: new FakeFirebaseClient { IsInitialized = false });

        Assert.False(vm.IsBackendConfigured);
        Assert.Equal("(미구성)", vm.FirebaseBucket);
    }

    [Fact]
    public void BackendBaseUrl_Is_Exposed_From_Settings()
    {
        var vm = MakeVm(settings: new AppSettings { BackendBaseUrl = "https://x.test/api/" });

        Assert.Equal("https://x.test/api/", vm.BackendBaseUrl);
    }

    [Fact]
    public void BackendBaseUrl_Empty_Shows_Placeholder()
    {
        var vm = MakeVm(settings: new AppSettings { BackendBaseUrl = string.Empty });

        Assert.Equal("(미설정)", vm.BackendBaseUrl);
    }

    [Theory]
    [InlineData("", "미설정")]
    [InlineData("secret-key", "설정됨")]
    public void BackendApiKeyState_Never_Leaks_The_Key(string key, string expected)
    {
        var vm = MakeVm(settings: new AppSettings { BackendApiKey = key });

        Assert.Equal(expected, vm.BackendApiKeyState);
        Assert.DoesNotContain("secret-key", vm.BackendApiKeyState);
    }

    [Fact]
    public void SignedInAccount_Guest_Shows_Guest()
    {
        var vm = MakeVm();

        Assert.Equal("게스트", vm.SignedInAccount);
    }

    [Fact]
    public void SignedInAccount_Shows_Id_Method_Role_And_Pin_State()
    {
        var vm = MakeVm(loginUser: new User
        {
            Id = "devmcjo", Role = UserRole.Admin, AuthMethod = AuthMethod.Google, HasPin = true
        });

        Assert.Equal("devmcjo · Google SSO · 관리자 · PIN 설정됨", vm.SignedInAccount);
    }

    [Fact]
    public void LogFolderPath_Is_Exposed_From_Service()
    {
        var expected = Path.Combine(Path.GetTempPath(), "logs");
        var vm = MakeVm(logFolder: new FakeLogFolderService(expected));

        Assert.Equal(expected, vm.LogFolderPath);
    }

    [Fact]
    public void OpenLogFolder_Delegates_To_Service_Without_Exception()
    {
        var fake = new FakeLogFolderService(Path.Combine(Path.GetTempPath(), "logs"));
        var vm = MakeVm(logFolder: fake);

        vm.OpenLogFolderCommand.Execute(null); // A3 스모크: 예외 미발생

        Assert.Equal(1, fake.OpenCount);
    }
}

/// <summary>
/// it11 #14: LogFolderService 경로 산출 + OpenLogFolder 스모크(실제 explorer 실행 결과와 무관하게 예외 미발생). (A3)
/// </summary>
public class LogFolderServiceTests
{
    [Fact]
    public void LogFolderPath_Is_DataFolder_Logs()
    {
        var svc = new LogFolderService();
        var expected = Path.Combine(MCPhoto.App.App.DataFolder, "logs");

        Assert.Equal(expected, svc.LogFolderPath);
    }

    [Fact]
    public void OpenLogFolder_Does_Not_Throw()
    {
        // 실제 explorer 실행 부작용 없이 검증: 열기 동작을 no-op opener로 주입.
        var opened = false;
        var svc = new LogFolderService(opener: _ => opened = true);

        var ex = Record.Exception(() => svc.OpenLogFolder());

        Assert.Null(ex);
        Assert.True(opened); // 폴더 생성 후 주입된 opener가 호출됨(explorer 미실행)
    }
}
