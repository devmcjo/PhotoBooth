using System.Globalization;
using System.IO;
using MCPhoto.App;
using MCPhoto.App.Services;
using MCPhoto.App.ViewModels;
using MCPhoto.Capture;
using MCPhoto.Core.Accounts;
using MCPhoto.Core.Build;
using MCPhoto.Core.Capture;
using MCPhoto.Core.Frames;
using MCPhoto.Core.Models;
using MCPhoto.Core.Settings;
using MCPhoto.Core.Upload;

namespace MCPhoto.Tests;

/// <summary>
/// it11 #14: 진단·상태 VM 헬스체크 조립 + LogFolderService 경로 산출.
/// 실제 explorer 실행/서버 실호출은 금지 — 페이크/주입 경계로 검증(A3 스모크 포함).
/// it15 §6.6: "서비스 계정 키 후보 경로" 항목이 사라지고 "서버 연결(백엔드)" 항목으로 재구성됐다.
/// 개발자 문의 카드: 연락처(고정)·버전·빌드 시각(exe 자신) + 웹 배포일(서버 /health) — 조회 실패는 "(확인 불가)".
/// </summary>
public class DiagnosticsViewModelTests
{
    /// <summary>빌드 정보 스텁 — 실행 파일 상태와 무관하게 값 주입(결정적). (it18: Site 폐지)</summary>
    private sealed class StubBuildInfoService : IBuildInfoService
    {
        public string Version { get; init; } = "0.0.0";
        public string BuildDate { get; init; } = string.Empty;
        public string DisplayText => $"v{Version}";
    }

    /// <summary>웹 배포일 조회 페이크 — 반환값 또는 예외를 주입(실제 HTTP 미호출).</summary>
    private sealed class FakeServerDeployInfoService : IServerDeployInfoService
    {
        public DateTimeOffset? Result { get; init; }
        public Exception? Throws { get; init; }
        public int CallCount { get; private set; }

        public Task<DateTimeOffset?> GetWebDeployedAtAsync(CancellationToken ct = default)
        {
            CallCount++;
            if (Throws is not null) return Task.FromException<DateTimeOffset?>(Throws);
            return Task.FromResult(Result);
        }
    }

    /// <summary>클립보드 페이크 — 성공/실패 주입 + 전달된 텍스트 캡처(실제 클립보드 미접근).</summary>
    private sealed class FakeClipboardService : IClipboardService
    {
        public FakeClipboardService(bool succeeds = true) => Succeeds = succeeds;
        public bool Succeeds { get; }
        public string? LastText { get; private set; }

        public bool TrySetText(string text)
        {
            LastText = text;
            return Succeeds;
        }
    }
    private sealed class StubSettingsService : ISettingsService
    {
        private readonly AppSettings _settings;
        public StubSettingsService(AppSettings settings, string? iniPath = null)
        {
            _settings = settings;
            IniPath = iniPath ?? Path.Combine(Path.GetTempPath(), "MCPhoto.ini");
        }
        public AppSettings Current => _settings;
        // it23 §B5.4: 활성 INI 경로는 진단 화면이 노출하는 값이다(스텁도 결정적 경로를 준다).
        public string IniPath { get; }
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

    /// <summary>
    /// 고지 열거·읽기 페이크(실제 파일 미접근). it23 §C8: 진단 화면은 <b>건수 상태</b>만 쓰고
    /// 경로·본문은 쓰지 않는다 — 그래서 이 페이크는 문서 목록만 주입받는다.
    /// </summary>
    private sealed class FakeLicenseNoticeService : ILicenseNoticeService
    {
        public FakeLicenseNoticeService(int documentCount, bool exists = true)
        {
            Exists = exists;
            Documents = Enumerable.Range(0, documentCount)
                .Select(i => new LicenseDocument($"doc{i}.txt", $"C:\\licenses\\doc{i}.txt", 100))
                .ToList();
        }
        public string FolderPath { get; init; } = Path.Combine(Path.GetTempPath(), "licenses");
        public bool Exists { get; }
        public IReadOnlyList<LicenseDocument> Documents { get; }
        public int ListCalls { get; private set; }
        public IReadOnlyList<LicenseDocument> ListDocuments() { ListCalls++; return Documents; }
        public LicenseTextResult ReadText(LicenseDocument document) => LicenseTextResult.Ok("body");
    }

    /// <summary>테스트 모드 상태만 주입하는 페이크(진단 화면은 IsEnabled·Options.Role만 읽는다).</summary>
    private sealed class FakeTestModeService : ITestModeService
    {
        public FakeTestModeService(bool enabled, UserRole role = UserRole.AdvancedUser)
        {
            IsEnabled = enabled;
            Options = enabled
                ? new TestModeOptions(true, "testuser", "test@email.com", role, null, false, QrGateReason.Count,
                    Array.Empty<string>())
                : TestModeOptions.Disabled;
        }
        public bool IsEnabled { get; }
        public TestModeOptions Options { get; }
        public string SourcePath => Path.Combine(Path.GetTempPath(), "MCPhoto.ini");
        public User? TestUser => null;
        public bool IsTestUser(User? user) => false;
    }

    private static DiagnosticsViewModel MakeVm(
        ICameraService? camera = null,
        FfmpegRunner? ffmpeg = null,
        IFirebaseClient? firebase = null,
        ILogFolderService? logFolder = null,
        AppSettings? settings = null,
        User? loginUser = null,
        IBuildInfoService? buildInfo = null,
        IServerDeployInfoService? serverDeploy = null,
        IClipboardService? clipboard = null,
        ILicenseNoticeService? licenseNotice = null,
        ITestModeService? testMode = null,
        string? iniPath = null)
    {
        camera ??= new FakeCameraService();
        // 존재하지 않는 경로를 명시 주입 → FfmpegAvailable=false로 결정적(실제 번들 유무와 무관).
        ffmpeg ??= new FfmpegRunner(ffmpegPath: Path.Combine(Path.GetTempPath(), "no-such-ffmpeg.exe"));
        firebase ??= new FakeFirebaseClient { IsInitialized = false };
        logFolder ??= new FakeLogFolderService(Path.Combine(Path.GetTempPath(), "logs"));
        settings ??= new AppSettings();
        buildInfo ??= new StubBuildInfoService();
        serverDeploy ??= new FakeServerDeployInfoService();
        clipboard ??= new FakeClipboardService();
        licenseNotice ??= new FakeLicenseNoticeService(documentCount: 3);
        var session = new SessionContext();
        if (loginUser is not null) session.Login(loginUser);
        // 프레임 검사 도구용 저장소(테스트는 임시 폴더 — 실제 파일이 없으면 빈 결과).
        var frameStore = new LocalFrameStore(Path.Combine(Path.GetTempPath(), $"diagframes_{Guid.NewGuid():N}"));
        return new DiagnosticsViewModel(camera, ffmpeg, firebase, logFolder,
            new StubSettingsService(settings, iniPath), session,
            buildInfo, serverDeploy, clipboard, licenseNotice, frameStore, logger: null, testMode: testMode);
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

    // ── 개발자 문의 카드 ──

    [Fact]
    public void DeveloperEmail_Is_The_Fixed_Contact_Address()
    {
        var vm = MakeVm();

        Assert.Equal("devmcjo@gmail.com", vm.DeveloperEmail);
        Assert.Equal(DiagnosticsViewModel.DeveloperEmailAddress, vm.DeveloperEmail);
    }

    [Fact]
    public void Version_And_BuildDate_Come_From_BuildInfo()
    {
        // it18: BuildDate는 날짜만이 아니라 시각까지 포함한다(exe 타임스탬프).
        var vm = MakeVm(buildInfo: new StubBuildInfoService { Version = "1.1.6", BuildDate = "2026-07-30 16:42" });

        Assert.Equal("1.1.6", vm.AppVersion);
        Assert.Equal("2026-07-30 16:42", vm.AppBuildDate);
    }

    [Fact]
    public void BuildDate_Missing_Shows_Unknown()
    {
        // exe 경로를 못 찾으면 빈 문자열 → 빈칸 대신 "(확인 불가)"로 표기한다.
        var vm = MakeVm(buildInfo: new StubBuildInfoService { Version = "1.1.6", BuildDate = "" });

        Assert.Equal("(확인 불가)", vm.AppBuildDate);
    }

    [Fact]
    public void WebDeployDate_Before_Probe_Is_Unknown()
    {
        var vm = MakeVm();

        Assert.Equal("(확인 불가)", vm.WebDeployDate);
        Assert.False(vm.IsCheckingWebDeploy);
    }

    [Fact]
    public async Task RefreshWebDeployDate_Formats_Server_Utc_As_Local_Minutes()
    {
        var deployedAt = new DateTimeOffset(2026, 7, 29, 4, 12, 3, TimeSpan.Zero); // 서버는 UTC로 준다
        var fake = new FakeServerDeployInfoService { Result = deployedAt };
        var vm = MakeVm(serverDeploy: fake);

        await vm.RefreshWebDeployDateCommand.ExecuteAsync(null);

        // 운영자가 읽는 로컬 시간·분 단위 표기(초 없음). ToLocalTime과 다른 API로 기대값을 계산한다.
        var expected = TimeZoneInfo.ConvertTime(deployedAt, TimeZoneInfo.Local)
            .ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        Assert.Equal(expected, vm.WebDeployDate);
        Assert.Equal(1, fake.CallCount);
        Assert.False(vm.IsCheckingWebDeploy);
    }

    [Fact]
    public async Task RefreshWebDeployDate_Null_Shows_Unknown()
    {
        // 미구성·미도달·서버가 필드를 안 준 경우 모두 null로 온다.
        var vm = MakeVm(serverDeploy: new FakeServerDeployInfoService { Result = null });

        await vm.RefreshWebDeployDateCommand.ExecuteAsync(null);

        Assert.Equal("(확인 불가)", vm.WebDeployDate);
    }

    [Fact]
    public async Task RefreshWebDeployDate_Absorbs_Exception()
    {
        // 조회 구현이 던져도 진단 화면은 열려야 한다(예외 전파 금지).
        var vm = MakeVm(serverDeploy: new FakeServerDeployInfoService
        {
            Throws = new InvalidOperationException("백엔드에 연결할 수 없습니다."),
        });

        var ex = await Record.ExceptionAsync(() => vm.RefreshWebDeployDateCommand.ExecuteAsync(null));

        Assert.Null(ex);
        Assert.Equal("(확인 불가)", vm.WebDeployDate);
        Assert.False(vm.IsCheckingWebDeploy);
    }

    [Fact]
    public void CopyDeveloperEmail_Copies_Address_And_Notifies()
    {
        var clipboard = new FakeClipboardService(succeeds: true);
        var vm = MakeVm(clipboard: clipboard);

        vm.CopyDeveloperEmailCommand.Execute(null);

        Assert.Equal("devmcjo@gmail.com", clipboard.LastText);
        Assert.Equal("메일 주소를 복사했습니다.", vm.CopyNotice);
    }

    [Fact]
    public void CopyDeveloperEmail_Failure_Guides_Manual_Copy()
    {
        // 클립보드 점유 등으로 실패해도 예외 없이 안내만 바뀐다(주소는 TextBox로 항상 선택 가능).
        var vm = MakeVm(clipboard: new FakeClipboardService(succeeds: false));

        vm.CopyDeveloperEmailCommand.Execute(null);

        Assert.Equal("복사에 실패했습니다. 위 주소를 직접 선택해 복사하세요.", vm.CopyNotice);
    }

    // ── 오픈소스 라이선스 고지 (설계 §5.1 1-6 → it23 §C8: 카드 → 1줄 상태 행) ──
    // 경로 노출·[폴더 열기] 커맨드는 폐지됐다(요구: 진단에서 열지 않는다). 남는 것은 누락 감지 그물뿐이다.

    /// <summary>
    /// C-T15: 고지 상태 행. 정상이면 건수, 없으면 누락 문구.
    /// 폴더가 있어도 <c>.txt</c>가 0건이면 고지가 없는 것이므로 **누락**으로 읽어야 한다
    /// (조용히 "정상"으로 넘어가면 라이선스 위반을 아무도 모른다).
    /// </summary>
    [Theory]
    [InlineData(3, true, "정상(3개)")]
    [InlineData(0, true, "누락 — 배포 산출물에 고지가 없습니다")]
    [InlineData(0, false, "누락 — 배포 산출물에 고지가 없습니다")]
    public void License_Notice_State_Reports_Count_Or_Missing(int count, bool exists, string expected)
    {
        var vm = MakeVm(licenseNotice: new FakeLicenseNoticeService(count, exists));

        Assert.Equal(expected, vm.LicenseNoticeState);
        Assert.Equal(count > 0, vm.HasLicenseNotice);
    }

    /// <summary>
    /// 상태 행은 진단 모달 진입 시 **1회** 평가된다(버튼이 없으므로 재평가 트리거도 없다).
    /// 바인딩이 여러 번 읽어도 폴더를 반복 열거하지 않는다.
    /// </summary>
    [Fact]
    public void License_Notice_Is_Enumerated_Once_Per_Modal()
    {
        var fake = new FakeLicenseNoticeService(documentCount: 2);
        var vm = MakeVm(licenseNotice: fake);

        _ = vm.LicenseNoticeState;
        _ = vm.HasLicenseNotice;
        _ = vm.LicenseNoticeState;

        Assert.Equal(1, fake.ListCalls);
    }

    /// <summary>진단 화면은 고지 **폴더 경로를 노출하지 않는다**(요구: 경로를 적어주지 말 것).</summary>
    [Fact]
    public void License_Folder_Path_Is_Not_Exposed_Anymore()
    {
        var t = typeof(DiagnosticsViewModel);

        Assert.Null(t.GetProperty("LicenseFolderPath"));
        Assert.Null(t.GetProperty("OpenLicenseFolderCommand"));
        Assert.Null(t.GetProperty("HasLicenseFolder"));
        Assert.Null(t.GetProperty("IsLicenseFolderMissing"));
    }

    // ── it23 §B5.4: 설정 파일 경로 · 테스트 모드 상태 ──

    /// <summary>
    /// 활성 INI 절대 경로가 노출된다. 후보 중 "쓰기 가능한 첫 곳"이 선택되므로 QA가 편집한 파일이
    /// 앱이 읽는 파일과 다를 수 있고, 이 행이 유일한 확인 창구다.
    /// </summary>
    [Fact]
    public void Settings_File_Path_Is_Exposed()
    {
        var path = Path.Combine(Path.GetTempPath(), "some", "MCPhoto.ini");
        var vm = MakeVm(iniPath: path);

        Assert.Equal(path, vm.SettingsFilePath);
    }

    /// <summary>테스트 모드 미주입(=OFF) 또는 꺼짐이면 "꺼짐"이고 색 분기도 꺼진다.</summary>
    [Fact]
    public void TestMode_State_Is_Off_By_Default()
    {
        var vm = MakeVm();

        Assert.False(vm.IsTestModeOn);
        Assert.Equal("꺼짐", vm.TestModeState);
    }

    /// <summary>켜짐이면 역할과 함께 **인증 우회 중**이라는 사실을 명시한다(§B13 동결 문구).</summary>
    [Fact]
    public void TestMode_State_Shows_Role_And_Bypass()
    {
        var vm = MakeVm(testMode: new FakeTestModeService(enabled: true, role: UserRole.Admin));

        Assert.True(vm.IsTestModeOn);
        Assert.Equal("켜짐(관리자) — 인증 우회 중", vm.TestModeState);
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
