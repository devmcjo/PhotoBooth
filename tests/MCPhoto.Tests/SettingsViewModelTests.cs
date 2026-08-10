using System.IO;
using MCPhoto.App;
using MCPhoto.App.Services;
using MCPhoto.App.ViewModels;
using MCPhoto.Core.Accounts;
using MCPhoto.Core.Capture;
using MCPhoto.Core.Devices;
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
        /// <summary>it23: 마지막으로 요청된 초기 선택 항목(웹캠/외부 판정 관측용).</summary>
        public CameraTestTarget? LastTarget { get; private set; }

        public Task ShowAsync(int deviceIndex) { LastDeviceIndex = deviceIndex; return Task.CompletedTask; }

        public Task ShowAsync(CameraTestTarget target)
        {
            LastTarget = target;
            LastDeviceIndex = target.DeviceIndex;
            return Task.CompletedTask;
        }
    }

    /// <summary>진단 모달 페이크 — ShowAsync 호출 횟수만 관측(실제 창 미표시). (it11 #14)</summary>
    private sealed class FakeDiagnosticsDialog : IDiagnosticsDialogService
    {
        public int ShowCount { get; private set; }
        public Task ShowAsync() { ShowCount++; return Task.CompletedTask; }
    }

    private static SettingsViewModel MakeVm(ICameraService? camera = null, IniSettingsService? settings = null,
        IFirebaseClient? firebase = null, IDiagnosticsDialogService? diagnostics = null,
        IExternalCamera? external = null)
    {
        var session = new SessionContext();
        settings ??= new IniSettingsService(iniPath: Path.Combine(Path.GetTempPath(), $"svm_{Guid.NewGuid():N}.ini"));
        settings.Load();
        var shell = new AppShellViewModel(new IdleWatchdog(), settings, new EmptyServiceProvider(), session);
        camera ??= new FakeCameraService(new CameraDevice(0, "Camera 0"));
        firebase ??= new FakeFirebaseClient { IsInitialized = false };
        diagnostics ??= new FakeDiagnosticsDialog();
        // it23: 기본은 무해한 Null 구현(미구성) — 외부 카메라를 다루는 테스트만 Fake를 주입한다.
        external ??= new NullExternalCamera();
        return new SettingsViewModel(shell, settings, camera, new FakeCameraTestDialog(), diagnostics, firebase, external);
    }

    /// <summary>로그인 세션(기본 Admin) VM. 게이트 대상 필드(거울모드·재촬영·QR·필터) 편집·저장 가능. (it12 R1)</summary>
    private static SettingsViewModel MakeLoggedInVm(IniSettingsService? settings = null, ICameraService? camera = null,
        IExternalCamera? external = null, UserRole role = UserRole.Admin)
    {
        var session = new SessionContext();
        session.Login(new User { Id = "admin", Role = role });
        settings ??= new IniSettingsService(iniPath: Path.Combine(Path.GetTempPath(), $"svm_{Guid.NewGuid():N}.ini"));
        settings.Load();
        var shell = new AppShellViewModel(new IdleWatchdog(), settings, new EmptyServiceProvider(), session);
        camera ??= new FakeCameraService(new CameraDevice(0, "Camera 0"));
        return new SettingsViewModel(shell, settings, camera, new FakeCameraTestDialog(),
            new FakeDiagnosticsDialog(), new FakeFirebaseClient { IsInitialized = false },
            external ?? new NullExternalCamera());
    }

    // ── it13 §7.3: TempUser 한도 게이트 테스트 지원 ──

    /// <summary>IQrUsageService만 해석하는 ServiceProvider(셸이 TempUser 로그인 시 조회).</summary>
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

    /// <summary>TempUser 로그인 + 지정 한도상태 VM. status.Blocked=true면 IsTempUserBlocked 반영.</summary>
    private static async Task<SettingsViewModel> MakeTempUserVmAsync(QrUsageStatus status, IniSettingsService settings)
    {
        var session = new SessionContext();
        settings.Load();
        var shell = new AppShellViewModel(new IdleWatchdog(), settings,
            new QrUsageProvider(new FakeQrUsageService(status)), session);
        session.Login(new User { Id = "tmp", Role = UserRole.TempUser });
        await Task.Delay(20); // fire-and-forget 조회 완료 대기
        return new SettingsViewModel(shell, settings, new FakeCameraService(new CameraDevice(0, "Camera 0")),
            new FakeCameraTestDialog(), new FakeDiagnosticsDialog(), new FakeFirebaseClient { IsInitialized = false },
            new NullExternalCamera());
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
        session.Login(new User { Id = "u1", Role = UserRole.User }); // QR 로드값 검증은 로그인 사용자 대상(게스트는 소스단 off)
        var shell = new AppShellViewModel(new IdleWatchdog(), settings, new EmptyServiceProvider(), session);
        var vm = new SettingsViewModel(shell, settings, new FakeCameraService(new CameraDevice(0, "Camera 0")),
            new FakeCameraTestDialog(), new FakeDiagnosticsDialog(), new FakeFirebaseClient { IsInitialized = false },
            new NullExternalCamera());
        await vm.OnEnterAsync();

        Assert.True(vm.EnableQrDelivery);
        Assert.True(vm.SendPhoto);
        Assert.False(vm.SendTimelapse); // 로드값 보존(강제 on 안 됨)
    }

    // ── it11 #13 / it12 R1: 재촬영 설정 왕복(로그인 전용 편집으로 게이트 확대) ──

    [Fact]
    public async Task Retake_Settings_Save_And_Load_RoundTrip()
    {
        var settings = new IniSettingsService(iniPath: Path.Combine(Path.GetTempPath(), $"svm_{Guid.NewGuid():N}.ini"));
        var vm = MakeLoggedInVm(settings: settings);   // it12 R1: 재촬영은 로그인 전용 편집(게스트 미기록)
        await vm.OnEnterAsync();

        Assert.False(vm.RetakeEnabled);        // 기본 off
        Assert.Equal(1, vm.RetakeLimit);       // 기본 1

        vm.RetakeEnabled = true;
        vm.RetakeLimit = 3;
        vm.SaveSettingsCommand.Execute(null);

        var r = new IniSettingsService(iniPath: settings.IniPath).Load();
        Assert.True(r.RetakeEnabled);          // 로그인 사용자는 재촬영 저장 가능
        Assert.Equal(3, r.RetakeLimit);
    }

    // ── it12 R1: 거울모드·재촬영·필터 3종 편집 권한 게이트(QR과 동일 3지점 메커니즘) ──

    [Fact]
    public async Task Guest_Gated_Fields_Forced_Off_On_Load()
    {
        // ini에 관리자값(전부 on) 저장 → 게스트 로드 시 소스단 off 표시(편집 권한 게이트).
        var settings = new IniSettingsService(iniPath: Path.Combine(Path.GetTempPath(), $"svm_{Guid.NewGuid():N}.ini"));
        var s = settings.Load();
        s.MirrorMode = true;
        s.RetakeEnabled = true;
        s.FilterGrayscale = true;
        s.FilterBrightness = true;
        s.FilterBeauty = true;
        settings.Save();

        var vm = MakeVm(settings: settings); // 게스트
        await vm.OnEnterAsync();

        Assert.True(vm.IsGuest);
        Assert.False(vm.MirrorMode);       // 표시 전용 off
        Assert.False(vm.RetakeEnabled);
        Assert.False(vm.FilterGrayscale);
        Assert.False(vm.FilterBrightness);
        Assert.False(vm.FilterBeauty);
    }

    [Fact]
    public async Task Guest_Save_Preserves_Ini_Mirror_Retake_Filters()
    {
        // 관리자가 켜둔 값이 게스트 저장으로 클로버되지 않아야(ini 원값 보존). QR 보존 테스트와 동형.
        var settings = new IniSettingsService(iniPath: Path.Combine(Path.GetTempPath(), $"svm_{Guid.NewGuid():N}.ini"));
        var s = settings.Load();
        s.MirrorMode = true;
        s.RetakeEnabled = true;
        s.RetakeLimit = 3;
        s.FilterGrayscale = true;
        s.FilterBrightness = true;
        s.FilterBeauty = true;
        settings.Save();

        var vm = MakeVm(settings: settings); // 게스트
        await vm.OnEnterAsync();
        Assert.False(vm.MirrorMode);         // 표시는 off
        vm.SaveSettingsCommand.Execute(null);

        var r = new IniSettingsService(iniPath: settings.IniPath).Load();
        Assert.True(r.MirrorMode);           // ini 원값 보존(클로버 방지)
        Assert.True(r.RetakeEnabled);
        Assert.Equal(3, r.RetakeLimit);
        Assert.True(r.FilterGrayscale);
        Assert.True(r.FilterBrightness);
        Assert.True(r.FilterBeauty);
    }

    [Fact]
    public async Task LoggedIn_Saves_Mirror_Retake_Filters()
    {
        // 로그인 사용자는 게이트 대상 필드를 편집·저장할 수 있어야(라운드트립).
        var settings = new IniSettingsService(iniPath: Path.Combine(Path.GetTempPath(), $"svm_{Guid.NewGuid():N}.ini"));
        var vm = MakeLoggedInVm(settings: settings);
        await vm.OnEnterAsync();

        vm.MirrorMode = true;
        vm.RetakeEnabled = true;
        vm.RetakeLimit = 2;
        vm.FilterGrayscale = true;
        vm.FilterBrightness = false; // 필터 기본값은 true — 로그인 사용자가 끄는 값도 기록되는지 검증
        vm.FilterBeauty = true;
        vm.SaveSettingsCommand.Execute(null);

        var r = new IniSettingsService(iniPath: settings.IniPath).Load();
        Assert.True(r.MirrorMode);
        Assert.True(r.RetakeEnabled);
        Assert.Equal(2, r.RetakeLimit);
        Assert.True(r.FilterGrayscale);
        Assert.False(r.FilterBrightness); // 로그인 사용자가 끈 값이 기록됨
        Assert.True(r.FilterBeauty);
    }

    // ── item3 스캐폴드: 외부 장치 placeholder(로그인 전용 편집, 저장만·실기능 미배선) ──

    [Fact]
    public async Task LoggedIn_Saves_External_Device_Placeholders()
    {
        // 로그인 사용자는 외부 장치 placeholder 값을 저장·복원할 수 있어야(왕복). UI에선 Disable이지만 저장 경로는 게이트만 검증.
        var settings = new IniSettingsService(iniPath: Path.Combine(Path.GetTempPath(), $"svm_{Guid.NewGuid():N}.ini"));
        var vm = MakeLoggedInVm(settings: settings);
        await vm.OnEnterAsync();

        Assert.False(vm.ExternalCameraEnabled); // 기본 off
        Assert.False(vm.PhotoPrinterEnabled);

        vm.ExternalCameraEnabled = true;
        vm.PhotoPrinterEnabled = true;
        vm.SaveSettingsCommand.Execute(null);

        var r = new IniSettingsService(iniPath: settings.IniPath).Load();
        Assert.True(r.ExternalCameraEnabled);
        Assert.True(r.PhotoPrinterEnabled);
    }

    [Fact]
    public async Task Guest_Save_Preserves_Ini_External_Device_Placeholders()
    {
        // it24 §4.1: 게스트에게 섹션은 **보이되 읽기 전용**이다(구 "미노출" 정책 폐지).
        // 그래서 이 테스트의 본질은 그대로다 — 저장 시 ini 원값 보존(클로버 방지). QR/필터 게이트와 동형.
        var settings = new IniSettingsService(iniPath: Path.Combine(Path.GetTempPath(), $"svm_{Guid.NewGuid():N}.ini"));
        var s = settings.Load();
        s.ExternalCameraEnabled = true;   // 관리자가 켜둔 값
        s.PhotoPrinterEnabled = true;
        settings.Save();

        var vm = MakeVm(settings: settings); // 게스트
        await vm.OnEnterAsync();
        vm.SaveSettingsCommand.Execute(null);

        var r = new IniSettingsService(iniPath: settings.IniPath).Load();
        Assert.True(r.ExternalCameraEnabled);  // ini 원값 보존
        Assert.True(r.PhotoPrinterEnabled);
    }

    // ── it17: 촬영 컷 수 "자동" 콤보 옵션 ──

    [Fact]
    public void CutCountOptions_Auto_First_Then_Allowed()
    {
        var vm = MakeVm();

        Assert.Equal(4, vm.CutCountOptions.Count);
        Assert.Equal(CutCountPolicy.AutoCutCount, vm.CutCountOptions[0].Value);
        Assert.Equal("자동", vm.CutCountOptions[0].Label);          // 최상단 = 신규 권장 항목(설계 §6.1)
        Assert.Equal(new[] { 6, 8, 10 }, vm.CutCountOptions.Skip(1).Select(o => o.Value).ToArray());
        Assert.Equal(new[] { "6컷", "8컷", "10컷" }, vm.CutCountOptions.Skip(1).Select(o => o.Label).ToArray());
    }

    [Fact]
    public async Task Auto_CutCount_Saved_To_Ini()
    {
        // CutCount는 게이트 비대상(VF-14) → 게스트 VM에서도 저장된다. Clamp 가드가 sentinel을 살려둔다.
        var settings = new IniSettingsService(iniPath: Path.Combine(Path.GetTempPath(), $"svm_{Guid.NewGuid():N}.ini"));
        var vm = MakeVm(settings: settings);
        await vm.OnEnterAsync();

        vm.CutCount = CutCountPolicy.AutoCutCount;
        vm.SaveSettingsCommand.Execute(null);

        Assert.Equal(CutCountPolicy.AutoCutCount, settings.Current.CutCount);
        Assert.Equal(CutCountPolicy.AutoCutCount, new IniSettingsService(iniPath: settings.IniPath).Load().CutCount);
        Assert.Equal(CutCountPolicy.AutoCutCount, vm.CutCount);   // 저장 후 재로드(LoadSettings)에서도 자동 유지
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

    // ── it13 §7.3: TempUser QR 한도 게이트(게스트 3지점 패턴 확장) ──

    [Fact]
    public async Task TempUser_Blocked_Time_Forces_Qr_Off_And_Shows_Time_Notice()
    {
        var settings = new IniSettingsService(iniPath: Path.Combine(Path.GetTempPath(), $"svm_{Guid.NewGuid():N}.ini"));
        var s = settings.Load();
        s.EnableQrDelivery = true; s.SendPhoto = true; s.SendTimelapse = true;  // 운영자가 QR on
        settings.Save();

        var vm = await MakeTempUserVmAsync(new QrUsageStatus(true, QrGateReason.Time, TimeSpan.Zero, 0), settings);
        await vm.OnEnterAsync();

        Assert.True(vm.IsTempUserBlocked);
        Assert.False(vm.CanEditQr);                 // 토글 disabled
        Assert.False(vm.EnableQrDelivery);          // 표시 전용 off
        Assert.False(vm.SendPhoto);
        Assert.False(vm.SendTimelapse);
        Assert.True(vm.HasQrLimitNotice);
        Assert.Equal("무료 사용 시간이 지났습니다. 관리자에게 문의해주세요.", vm.QrLimitNotice);
    }

    [Fact]
    public async Task TempUser_Blocked_Count_Shows_Count_Notice()
    {
        var settings = new IniSettingsService(iniPath: Path.Combine(Path.GetTempPath(), $"svm_{Guid.NewGuid():N}.ini"));
        settings.Load();
        var vm = await MakeTempUserVmAsync(new QrUsageStatus(true, QrGateReason.Count, TimeSpan.FromHours(1), 0), settings);
        await vm.OnEnterAsync();

        Assert.True(vm.IsTempUserBlocked);
        Assert.Equal("무료 사용 횟수가 소진되었습니다. 관리자에게 문의해주세요.", vm.QrLimitNotice);
    }

    [Fact]
    public async Task TempUserBlocked_Save_Preserves_Ini_Qr()
    {
        // ★ 최상위 불변식: 초과 TempUser로 저장해도 관리자 원값(QR on) 유지 → 한도 해제 시 원복.
        var settings = new IniSettingsService(iniPath: Path.Combine(Path.GetTempPath(), $"svm_{Guid.NewGuid():N}.ini"));
        var s = settings.Load();
        s.EnableQrDelivery = true; s.SendPhoto = true; s.SendTimelapse = true;
        settings.Save();

        var vm = await MakeTempUserVmAsync(new QrUsageStatus(true, QrGateReason.Time, TimeSpan.Zero, 0), settings);
        await vm.OnEnterAsync();
        Assert.False(vm.EnableQrDelivery);          // 표시는 off
        vm.SaveSettingsCommand.Execute(null);

        var r = new IniSettingsService(iniPath: settings.IniPath).Load();
        Assert.True(r.EnableQrDelivery);            // ini 원값 보존(클로버 방지)
        Assert.True(r.SendPhoto);
        Assert.True(r.SendTimelapse);
    }

    [Fact]
    public async Task Normal_TempUser_Edits_Qr_Like_User()
    {
        // 정상 TempUser(미초과)는 User와 동일 — QR 편집·저장 가능.
        var settings = new IniSettingsService(iniPath: Path.Combine(Path.GetTempPath(), $"svm_{Guid.NewGuid():N}.ini"));
        var vm = await MakeTempUserVmAsync(new QrUsageStatus(false, QrGateReason.Ok, TimeSpan.FromHours(10), 5), settings);
        await vm.OnEnterAsync();

        Assert.False(vm.IsTempUserBlocked);
        Assert.True(vm.CanEditQr);
        Assert.False(vm.HasQrLimitNotice);

        vm.EnableQrDelivery = true;
        vm.SaveSettingsCommand.Execute(null);
        var r = new IniSettingsService(iniPath: settings.IniPath).Load();
        Assert.True(r.EnableQrDelivery);            // 정상 TempUser 저장 반영
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
    public void Server_Unconfigured_Shows_Missing_Backend_Url_Notice()
    {
        // it15: 레거시 직결 경로 폐기 → 미구성 사유는 "서비스 계정 키 부재"가 아니라 "백엔드 주소 미설정".
        var vm = MakeVm(firebase: new FakeFirebaseClient { IsInitialized = false });

        Assert.False(vm.IsServerConnected);
        Assert.Equal("미구성 — 백엔드 주소가 설정되지 않았습니다(로그 참조)", vm.ServerStatusText);
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
        session.Login(new User { Id = "admin", Role = UserRole.Admin });
        var shell = new AppShellViewModel(new IdleWatchdog(), settings, new EmptyServiceProvider(), session);
        var vm = new SettingsViewModel(shell, settings, new FakeCameraService(new CameraDevice(0, "Camera 0")),
            new FakeCameraTestDialog(), diag, new FakeFirebaseClient { IsInitialized = false },
            new NullExternalCamera());

        Assert.True(vm.IsLoggedIn);
        await vm.OpenDiagnosticsCommand.ExecuteAsync(null);

        Assert.Equal(1, diag.ShowCount);
    }

    // ── it16 §7.5·§8.3: 저장 직전 창 기하 캡처(셸 이벤트 발화 횟수·순서 계약) ──

    /// <summary>Save()가 항상 실패하는 설정 서비스(쓰기 불가 경로 모사 — 폴백 체인이 있어 실경로로는 재현 불가).</summary>
    private sealed class FailingSaveSettingsService : ISettingsService
    {
        private readonly AppSettings _current = new();
        public AppSettings Current => _current;
        public string IniPath => System.IO.Path.Combine(System.IO.Path.GetTempPath(), "MCPhoto.ini");
        public AppSettings Load() => _current;
        public bool Save() => false;
    }

    /// <summary>셸까지 함께 돌려주는 저장 테스트용 VM(이벤트 발화 관측).</summary>
    private static (SettingsViewModel vm, AppShellViewModel shell) MakeSaveVm(ISettingsService settings)
    {
        var session = new SessionContext();
        session.Login(new User { Id = "admin", Role = UserRole.Admin });
        settings.Load();
        var shell = new AppShellViewModel(new IdleWatchdog(), settings, new EmptyServiceProvider(), session);
        var vm = new SettingsViewModel(shell, settings, new FakeCameraService(new CameraDevice(0, "Camera 0")),
            new FakeCameraTestDialog(), new FakeDiagnosticsDialog(), new FakeFirebaseClient { IsInitialized = false },
            new NullExternalCamera());
        return (vm, shell);
    }

    /// <summary>it16 §8.3-30: 저장 성공 시 캡처 요청과 표시모드 적용 요청이 **각각 1회** 발화한다.</summary>
    [Fact]
    public async Task SaveSettings_Fires_Capture_And_Apply_Once_Each()
    {
        var settings = new IniSettingsService(iniPath: Path.Combine(Path.GetTempPath(), $"svm_{Guid.NewGuid():N}.ini"));
        var (vm, shell) = MakeSaveVm(settings);
        await vm.OnEnterAsync();

        int captures = 0, applies = 0;
        void OnCapture() => captures++;
        void OnApply() => applies++;
        shell.WindowBoundsCaptureRequested += OnCapture;
        shell.DisplayModeApplyRequested += OnApply;
        try
        {
            vm.SaveSettingsCommand.Execute(null);
        }
        finally
        {
            shell.WindowBoundsCaptureRequested -= OnCapture;
            shell.DisplayModeApplyRequested -= OnApply;
        }

        Assert.Equal(1, captures);
        Assert.Equal(1, applies);
    }

    /// <summary>
    /// it16 §8.3-31 **순서 계약**: 캡처는 VM 필드를 `AppSettings`에 복사하기 **전에** 일어난다.
    /// 캡처 핸들러 안에서 관측한 `Current.DisplayMode`가 저장 전 값이어야 한다 —
    /// 순서가 뒤바뀌면 창모드→전체화면 저장 시 창은 아직 창모드인데 설정은 전체화면이라 직전 위치를 잃는다.
    /// </summary>
    [Fact]
    public async Task SaveSettings_Captures_Bounds_Before_Copying_DisplayMode()
    {
        var settings = new IniSettingsService(iniPath: Path.Combine(Path.GetTempPath(), $"svm_{Guid.NewGuid():N}.ini"));
        settings.Load().DisplayMode = DisplayMode.Windowed;
        settings.Save();

        var (vm, shell) = MakeSaveVm(settings);
        await vm.OnEnterAsync();
        Assert.Equal(DisplayMode.Windowed, vm.DisplayMode);

        vm.DisplayMode = DisplayMode.Fullscreen;   // 반대 값으로 바꿔 두고 저장

        DisplayMode? observed = null;
        void OnCapture() => observed = settings.Current.DisplayMode;
        shell.WindowBoundsCaptureRequested += OnCapture;
        try
        {
            vm.SaveSettingsCommand.Execute(null);
        }
        finally
        {
            shell.WindowBoundsCaptureRequested -= OnCapture;
        }

        Assert.Equal(DisplayMode.Windowed, observed);                    // 캡처 시점 = 저장 전 값
        Assert.Equal(DisplayMode.Fullscreen, settings.Current.DisplayMode); // 저장 후에는 새 값
    }

    /// <summary>
    /// it16 §8.3-32: 저장 실패 시 표시모드 적용 요청은 발화하지 않는다(현행 동작 유지).
    /// 캡처는 이미 수행됐지만 메모리상 WindowBounds만 최신화되므로 무해하다(§7.5).
    /// </summary>
    [Fact]
    public async Task SaveSettings_Failure_Does_Not_Request_Apply_But_Still_Captures()
    {
        var (vm, shell) = MakeSaveVm(new FailingSaveSettingsService());
        await vm.OnEnterAsync();

        int captures = 0, applies = 0;
        void OnCapture() => captures++;
        void OnApply() => applies++;
        shell.WindowBoundsCaptureRequested += OnCapture;
        shell.DisplayModeApplyRequested += OnApply;
        try
        {
            vm.SaveSettingsCommand.Execute(null);
        }
        finally
        {
            shell.WindowBoundsCaptureRequested -= OnCapture;
            shell.DisplayModeApplyRequested -= OnApply;
        }

        Assert.Equal(1, captures);
        Assert.Equal(0, applies);
        Assert.True(vm.SavedNoticeIsError);   // 실패 토스트(성공 오인 금지)
    }
}
