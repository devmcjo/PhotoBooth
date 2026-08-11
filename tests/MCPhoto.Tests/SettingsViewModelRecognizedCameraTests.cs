using System.IO;
using System.Linq;
using System.Reflection;
using MCPhoto.App;
using MCPhoto.App.Services;
using MCPhoto.App.ViewModels;
using MCPhoto.Core.Accounts;
using MCPhoto.Core.Capture;
using MCPhoto.Core.Devices;
using MCPhoto.Core.Models;
using MCPhoto.Core.Navigation;
using MCPhoto.Core.Settings;
using MCPhoto.Devices.Nikon;
using MCPhoto.Tests.Fakes;

namespace MCPhoto.Tests;

/// <summary>
/// it25 Step 4~6: 인식된 카메라 콤보(§6)·지원 카메라 오버레이(§7)·시뮬레이션 봉인(§3.2 TS1~TS4) 검증.
/// 설계 §12.2 T-C1~T-C5 · T-B5~T-B8.
/// <para>
/// ⚠️ 이 스위트가 지키는 것은 <b>두 가지 방향</b>이다: ① 시뮬레이션이 켜져야 할 자리에서 켜지고
/// ② <b>꺼져야 할 자리에서 꺼진다</b>(실계정 세션·촬영·테스트 모달). ②가 봉인의 관측 가능한 형태이며
/// 그것이 없으면 "가짜 사진이 실제 서버에 올라간다"는 사고를 회귀 테스트가 막지 못한다.
/// </para>
/// </summary>
public class SettingsViewModelRecognizedCameraTests
{
    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    private sealed class StubCameraService : ICameraService
    {
        public event EventHandler<CameraFrame>? FrameReady { add { } remove { } }
        public double CurrentFps => 0;
        public bool IsRunning => false;
        public Task<bool> StartAsync(int deviceIndex, double targetAspect, bool mirror, CancellationToken ct = default) => Task.FromResult(true);
        public Task StopAsync() => Task.CompletedTask;
        public void SetMirror(bool mirror) { }
        public void SetTargetAspect(double aspect) { }
        public Task<CapturedStill> CaptureStillAsync(CancellationToken ct = default) => Task.FromResult(new CapturedStill());
        public void StartRecording(string outputPath) { }
        public Task StopRecordingAsync() => Task.CompletedTask;
        public IReadOnlyList<CameraDevice> EnumerateDevices() => new[] { new CameraDevice(0, "Camera 0") };
        public void Dispose() { }
    }

    private sealed class StubCameraTestDialog : ICameraTestDialogService
    {
        public Task ShowAsync(int deviceIndex) => Task.CompletedTask;
        public Task ShowAsync(CameraTestTarget target) => Task.CompletedTask;
    }

    private sealed class StubDiagnosticsDialog : IDiagnosticsDialogService
    {
        public Task ShowAsync() => Task.CompletedTask;
    }

    private static string TempIni() => Path.Combine(Path.GetTempPath(), $"svm_it25_{Guid.NewGuid():N}.ini");

    private static IniSettingsService SettingsWith(Action<AppSettings> configure, string? path = null)
    {
        var svc = new IniSettingsService(iniPath: path ?? TempIni());
        configure(svc.Load());
        return svc;
    }

    /// <summary>
    /// WMI 프로브는 <b>반드시 주입</b>한다(기본값은 이 머신의 실제 장치를 돌려준다).
    /// <paramref name="loginUser"/>로 세션 사용자 인스턴스를 지정할 수 있어야 참조 동일성 게이트를 검증할 수 있다.
    /// </summary>
    private static SettingsViewModel MakeVm(IniSettingsService settings, UserRole? role,
        IExternalCamera? external = null,
        Func<IReadOnlyList<string>>? probe = null,
        ITestModeService? testMode = null,
        User? loginUser = null)
    {
        var session = new SessionContext();
        if (loginUser is not null) session.Login(loginUser);
        else if (role is { } r) session.Login(new User { Id = "u", Role = r });
        _ = settings.Current;
        var shell = new AppShellViewModel(new IdleWatchdog(), settings, new EmptyServiceProvider(), session);
        return new SettingsViewModel(shell, settings, new StubCameraService(), new StubCameraTestDialog(),
            new StubDiagnosticsDialog(), new FakeFirebaseClient { IsInitialized = false },
            external ?? new NullExternalCamera(),
            logger: null, licenseNotice: null,
            probePortableDevices: probe ?? (() => Array.Empty<string>()),
            testMode: testMode);
    }

    // ══════════ T-C1: S0 — 진입 직후 ══════════

    /// <summary>
    /// 설정 진입 직후 콤보는 sentinel 단독이고 저장값은 불변이다. S0은 "검색 전"이지 "없음 단정"이 아니다.
    /// </summary>
    [Fact]
    public async Task C1_Entry_Shows_Sentinel_Only_And_Keeps_Saved_Model()
    {
        var vm = MakeVm(SettingsWith(s =>
        {
            s.ExternalCameraEnabled = true;
            s.ExternalCameraModel = "NikonD5300";
        }), UserRole.Admin);

        await vm.OnEnterAsync();

        Assert.Single(vm.RecognizedCameraOptions);
        Assert.Equal(string.Empty, vm.RecognizedCameraOptions[0].Value);
        Assert.Equal("- 선택안함 -", vm.RecognizedCameraOptions[0].Display);
        Assert.Equal(SettingsViewModel.RecognizedCameraNoneDisplay, vm.RecognizedCameraOptions[0].Display);
        Assert.Equal(string.Empty, vm.RecognizedCameraSelection);
        Assert.Equal("NikonD5300", vm.ExternalCameraModel);   // ini 미러 불변
    }

    // ══════════ T-C2: S6 — 인식 1행 + 자동 선택 ══════════

    [Fact]
    public async Task C2_Connected_Adds_Recognized_Row_And_Selects_It_When_Ids_Match()
    {
        var external = new FakeExternalCamera { CanControl = true, ConnectResult = true };
        var vm = MakeVm(SettingsWith(s =>
        {
            s.ExternalCameraEnabled = true;
            s.ExternalCameraModel = "NikonD5300";
        }), UserRole.Admin, external);
        await vm.OnEnterAsync();

        await vm.DiscoverExternalCameraCommand.ExecuteAsync(null);

        Assert.Equal(2, vm.RecognizedCameraOptions.Count);
        Assert.Equal(string.Empty, vm.RecognizedCameraOptions[0].Value);   // sentinel은 항상 첫 행
        Assert.Equal("NikonD5300", vm.RecognizedCameraOptions[1].Value);
        Assert.Equal("Nikon D5300", vm.RecognizedCameraOptions[1].Display);
        // 저장 Id와 일치하므로 그 행이 선택된다 — 저장값을 "따라간" 것이 아니라 이미 그것임을 반영한다.
        Assert.Equal("NikonD5300", vm.RecognizedCameraSelection);
        Assert.Equal("NikonD5300", vm.ExternalCameraModel);
    }

    /// <summary>재검색이 인식 행을 중복 추가하지 않는다(목록은 매 검색마다 재구성된다).</summary>
    [Fact]
    public async Task C2_Repeated_Search_Does_Not_Duplicate_Rows()
    {
        var external = new FakeExternalCamera { CanControl = true, ConnectResult = true };
        var vm = MakeVm(SettingsWith(s => s.ExternalCameraEnabled = true), UserRole.Admin, external);
        await vm.OnEnterAsync();

        await vm.DiscoverExternalCameraCommand.ExecuteAsync(null);
        await vm.DiscoverExternalCameraCommand.ExecuteAsync(null);

        Assert.Equal(2, vm.RecognizedCameraOptions.Count);
    }

    // ══════════ T-C3: S2·S4·S7 — 검색 결과가 저장값을 지우지 않는다 ══════════

    /// <summary>
    /// ★ 인식 0으로 끝나는 모든 상태에서 콤보는 sentinel 단독이고 <b>ini 원값이 살아남는다</b>.
    /// 이것이 §6.3 분리 설계의 존재 이유다 — 콤보를 ini 미러에 직접 바인딩하면 목록이 비는 순간
    /// WPF가 저장값을 null로 되쓴다(it24 P5·it7 B9 계열 함정).
    /// </summary>
    [Theory]
    [InlineData(false, true)]    // S2: 스택 미비
    [InlineData(true, false)]    // S4: 스택 정상 + 연결 실패
    public async Task C3_Recognition_Zero_States_Keep_Sentinel_And_Saved_Value(bool canControl, bool _)
    {
        var path = TempIni();
        try
        {
            var settings = SettingsWith(s =>
            {
                s.ExternalCameraEnabled = true;
                s.ExternalCameraModel = "NikonD5300";
            }, path);
            Assert.True(settings.Save());

            var external = new FakeExternalCamera
            {
                CanControl = canControl,
                ConnectResult = false,
                ReadinessReason = "SDK 모듈이 설치되지 않았습니다",
                Reason = "카메라가 연결되지 않았습니다 (USB·전원 확인)",
            };
            var vm = MakeVm(settings, UserRole.Admin, external);
            await vm.OnEnterAsync();

            await vm.DiscoverExternalCameraCommand.ExecuteAsync(null);

            Assert.Single(vm.RecognizedCameraOptions);
            Assert.Equal(string.Empty, vm.RecognizedCameraSelection);
            Assert.Equal("NikonD5300", vm.ExternalCameraModel);

            vm.SaveSettingsCommand.Execute(null);
            Assert.Equal("NikonD5300", new IniSettingsService(iniPath: path).Load().ExternalCameraModel);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    /// <summary>S7(검색 예외)도 인식 0 상태다 — 직전 검색의 인식 행을 남겨 두지 않는다.</summary>
    [Fact]
    public async Task C3_Search_Failure_Clears_Recognized_Row()
    {
        var external = new FakeExternalCamera { CanControl = true, ConnectResult = true };
        bool boom = false;
        var vm = MakeVm(SettingsWith(s => s.ExternalCameraEnabled = true), UserRole.Admin, external,
            probe: () => boom ? throw new InvalidOperationException("WMI 붕괴") : Array.Empty<string>());
        await vm.OnEnterAsync();

        await vm.DiscoverExternalCameraCommand.ExecuteAsync(null);
        Assert.Equal(2, vm.RecognizedCameraOptions.Count);

        boom = true;
        await vm.DiscoverExternalCameraCommand.ExecuteAsync(null);

        Assert.Equal(SettingsViewModel.DiscoveryFailedText, vm.DiscoveryHeadline);
        Assert.Single(vm.RecognizedCameraOptions);
        Assert.Equal(string.Empty, vm.RecognizedCameraSelection);
    }

    // ══════════ T-C4: 명시 선택 → 저장 / sentinel·null 되쓰기 → 불변 ══════════

    [Fact]
    public async Task C4_Explicit_Selection_Updates_Mirror_And_Saves()
    {
        var path = TempIni();
        try
        {
            // 저장값을 미지 Id로 두면 검색 후 자동 선택이 일어나지 않는다(sentinel) — 명시 선택 경로를 만든다.
            var settings = SettingsWith(s =>
            {
                s.ExternalCameraEnabled = true;
                s.ExternalCameraModel = "NikonD5300";
            }, path);
            var external = new FakeExternalCamera { CanControl = true, ConnectResult = true };
            var vm = MakeVm(settings, UserRole.Admin, external);
            await vm.OnEnterAsync();
            await vm.DiscoverExternalCameraCommand.ExecuteAsync(null);

            // 사용자가 sentinel로 되돌려도 저장값은 그대로다.
            vm.RecognizedCameraSelection = string.Empty;
            Assert.Equal("NikonD5300", vm.ExternalCameraModel);

            // 인식 행을 명시 선택하면 ini 미러가 갱신되고 저장에 반영된다(일반 편집 규칙).
            vm.RecognizedCameraSelection = "NikonD5300";
            Assert.Equal("NikonD5300", vm.ExternalCameraModel);

            vm.SaveSettingsCommand.Execute(null);
            Assert.Equal("NikonD5300", new IniSettingsService(iniPath: path).Load().ExternalCameraModel);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    /// <summary>
    /// ★ E25 — WPF가 매칭 실패한 <c>SelectedValue</c>를 null로 되쓰는 경로. 표시상 sentinel로 정규화되고
    /// ini 미러는 <b>불변</b>이어야 한다. 정규화가 없으면 다음 저장에서 null이 그대로 흘러 값이 사라진다.
    /// </summary>
    [Fact]
    public async Task C4_Null_Writeback_Normalizes_To_Sentinel_Without_Touching_Mirror()
    {
        var vm = MakeVm(SettingsWith(s =>
        {
            s.ExternalCameraEnabled = true;
            s.ExternalCameraModel = "NikonD5300";
        }), UserRole.Admin);
        await vm.OnEnterAsync();

        vm.RecognizedCameraSelection = null!;

        Assert.Equal(string.Empty, vm.RecognizedCameraSelection);
        Assert.Equal("NikonD5300", vm.ExternalCameraModel);
    }

    /// <summary>미지 Id 선택은 ini 미러를 바꾸지 않는다(레지스트리에 없는 값을 저장하지 않는다).</summary>
    [Fact]
    public async Task C4_Unknown_Selection_Does_Not_Touch_Mirror()
    {
        var vm = MakeVm(SettingsWith(s =>
        {
            s.ExternalCameraEnabled = true;
            s.ExternalCameraModel = "NikonD5300";
        }), UserRole.Admin);
        await vm.OnEnterAsync();

        vm.RecognizedCameraSelection = "NikonD9999";

        Assert.Equal("NikonD5300", vm.ExternalCameraModel);
    }

    // ══════════ T-C5: 지원 카메라 오버레이 ══════════

    [Fact]
    public void C5_Supported_Groups_Are_Derived_From_Registry_And_Sorted()
    {
        var vm = MakeVm(SettingsWith(_ => { }), UserRole.Admin);

        var group = Assert.Single(vm.SupportedCameraGroups);
        Assert.Equal("Nikon", group.Manufacturer);
        Assert.Equal(new[] { "D5300" }, group.Models);

        // 제조사·모델명 오름차순(레지스트리 행 순서에 의존하지 않는다).
        Assert.Equal(
            vm.SupportedCameraGroups.Select(g => g.Manufacturer).OrderBy(m => m, StringComparer.OrdinalIgnoreCase),
            vm.SupportedCameraGroups.Select(g => g.Manufacturer));
        // 레지스트리 전 모델이 정확히 한 번 등장한다(빠뜨림·중복 없음).
        Assert.Equal(
            ExternalCameraModels.All.Count,
            vm.SupportedCameraGroups.Sum(g => g.Models.Count));
    }

    [Fact]
    public void C5_Overlay_Open_Close_Round_Trips_Without_Gate()
    {
        // 게이트 없음: 게스트도 열람할 수 있어야 한다(열람은 편집이 아니다).
        var vm = MakeVm(SettingsWith(_ => { }), role: null);

        Assert.False(vm.IsSupportedCameraListOpen);
        vm.OpenSupportedCameraListCommand.Execute(null);
        Assert.True(vm.IsSupportedCameraListOpen);
        vm.CloseSupportedCameraListCommand.Execute(null);
        Assert.False(vm.IsSupportedCameraListOpen);
    }

    // ══════════ T-B5: 시뮬레이션 S6 ══════════

    /// <summary>
    /// ★ TS1·TS3·TS4 — 시뮬레이션 S6: 헤드라인·인식 콤보가 채워지고 W38이 붙지만
    /// <b>관측 I/O는 0회</b>이며 ini에는 아무것도 기록되지 않는다.
    /// </summary>
    [Fact]
    public async Task B5_Simulated_S6_Fills_Combo_With_Zero_Observation_Io()
    {
        var path = TempIni();
        try
        {
            var settings = SettingsWith(s =>
            {
                s.ExternalCameraEnabled = true;
                s.ExternalCameraModel = "NikonD5300";
            }, path);
            Assert.True(settings.Save());

            var testMode = new FakeTestModeService(
                "[Test]\nTestMode=1\nRole=admin\nExternalCamera=1\nExternalCameraType=0\n");
            // 스택 미비(실경로라면 S2)로 두어 "시뮬레이션이 실관측을 대체했다"를 문구로 구분할 수 있게 한다.
            var external = new FakeExternalCamera { CanControl = false, ReadinessReason = "SDK 모듈이 설치되지 않았습니다" };
            int probeCalls = 0;
            var vm = MakeVm(settings, role: null, external,
                probe: () => { probeCalls++; return Array.Empty<string>(); },
                testMode: testMode, loginUser: testMode.TestUser);
            await vm.OnEnterAsync();

            await vm.DiscoverExternalCameraCommand.ExecuteAsync(null);

            // ① 화면: S6 헤드라인 + W21a + W38
            Assert.Equal(SettingsViewModel.DiscoveryConnectedText("Nikon D5300"), vm.DiscoveryHeadline);
            Assert.Contains(SettingsViewModel.DiscoveryTestHintText, vm.DiscoveryDetailLines);
            Assert.Contains(SettingsViewModel.DiscoverySimulatedText, vm.DiscoveryDetailLines);
            // 배터리는 표시하지 않는다 — 관측하지 않은 수치를 날조하지 않는다(§5.4).
            Assert.DoesNotContain(vm.DiscoveryDetailLines, l => l.StartsWith("배터리", StringComparison.Ordinal));

            // ② 인식 콤보에 D5300이 올라간다.
            Assert.Equal(2, vm.RecognizedCameraOptions.Count);
            Assert.Equal("NikonD5300", vm.RecognizedCameraOptions[1].Value);

            // ③ ★ 관측 I/O 0회: CheckReadiness·ConnectAsync·WMI 프로브를 한 번도 부르지 않는다.
            Assert.False(external.Touched);
            Assert.Equal(0, probeCalls);

            // ④ ★ TS3: ini에 자동 기록이 없다.
            var r = new IniSettingsService(iniPath: path).Load();
            Assert.Equal("NikonD5300", r.ExternalCameraModel);
            Assert.True(r.ExternalCameraEnabled);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    // ══════════ T-B6: 시뮬레이션 S4 ══════════

    /// <summary>
    /// 시뮬레이션 <c>Type=-1</c> → S4(W19) + W38 + 콤보 sentinel 단독. SDK 없이는 도달할 수 없던
    /// W19 문구의 유일한 QA 경로이며, 사용자가 말한 "카메라 없음" 상태의 재현 수단이다.
    /// </summary>
    [Fact]
    public async Task B6_Simulated_S4_Says_Not_Found_With_Sentinel_Only()
    {
        var testMode = new FakeTestModeService(
            "[Test]\nTestMode=1\nRole=admin\nExternalCamera=1\nExternalCameraType=-1\n");
        var external = new FakeExternalCamera { CanControl = false, ReadinessReason = "SDK 모듈이 설치되지 않았습니다" };
        var vm = MakeVm(SettingsWith(s => s.ExternalCameraEnabled = true), role: null, external,
            testMode: testMode, loginUser: testMode.TestUser);
        await vm.OnEnterAsync();

        await vm.DiscoverExternalCameraCommand.ExecuteAsync(null);

        Assert.Equal(SettingsViewModel.DiscoveryNotFoundText, vm.DiscoveryHeadline);
        Assert.Contains(SettingsViewModel.DiscoverySimulatedText, vm.DiscoveryDetailLines);
        // ★ 실장비 사유("SDK 모듈이 설치되지 않았습니다")가 시뮬레이션 결과에 섞이지 않는다 —
        //   시뮬레이션 산출물은 계획이 말한 것만이어야 한다(§5.4·TS4).
        Assert.DoesNotContain("SDK 모듈이 설치되지 않았습니다", vm.DiscoveryDetailLines);
        Assert.Single(vm.RecognizedCameraOptions);
        Assert.Equal(string.Empty, vm.RecognizedCameraSelection);
        Assert.False(external.Touched);
    }

    /// <summary>
    /// <c>ExternalCamera=0</c>(기본)이면 테스트 유저 세션이어도 실관측이 수행된다 —
    /// 신규 키가 기존 테스트 모드 흐름을 바꾸지 않는다는 회귀 잠금.
    /// </summary>
    [Fact]
    public async Task B6_Switch_Off_Keeps_Real_Observation_Path()
    {
        var testMode = new FakeTestModeService("[Test]\nTestMode=1\nRole=admin\n");
        var external = new FakeExternalCamera { CanControl = false, ReadinessReason = "SDK 모듈이 설치되지 않았습니다" };
        int probeCalls = 0;
        var vm = MakeVm(SettingsWith(s => s.ExternalCameraEnabled = true), role: null, external,
            probe: () => { probeCalls++; return Array.Empty<string>(); },
            testMode: testMode, loginUser: testMode.TestUser);
        await vm.OnEnterAsync();

        await vm.DiscoverExternalCameraCommand.ExecuteAsync(null);

        Assert.Equal(SettingsViewModel.DiscoveryUndeterminedText, vm.DiscoveryHeadline);   // 실경로 = S2
        Assert.DoesNotContain(SettingsViewModel.DiscoverySimulatedText, vm.DiscoveryDetailLines);
        Assert.Equal(1, probeCalls);
        Assert.Equal(1, external.ReadinessCalls);
    }

    // ══════════ T-B7: 봉인(촬영·모달) ══════════

    /// <summary>
    /// ★ TS1 — <c>CaptureViewModel</c>·<c>CameraTestViewModel</c>이 <see cref="ITestModeService"/>를
    /// <b>참조하지 않는다</b>. 그 참조 부재가 봉인의 증명이다: 시뮬레이션 판정 입력이 촬영 경로에
    /// 도달할 코드 그래프가 없으므로 "가짜 스틸이 만들어지는" 상태를 표현할 수 없다.
    /// <para>
    /// 생성자 인자와 필드를 모두 검사하는 이유: 인자만 막으면 서비스 로케이터로 끌어오는 우회가 남는다.
    /// </para>
    /// </summary>
    [Fact]
    public void B7_Capture_And_CameraTest_Vms_Do_Not_Reference_Test_Mode()
    {
        foreach (var type in new[] { typeof(CaptureViewModel), typeof(CameraTestViewModel) })
        {
            Assert.DoesNotContain(
                type.GetConstructors().SelectMany(c => c.GetParameters()).Select(p => p.ParameterType),
                t => t == typeof(ITestModeService));

            Assert.DoesNotContain(
                type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                    .Select(f => f.FieldType),
                t => t == typeof(ITestModeService));
        }
    }

    /// <summary>
    /// ★ TS1 — 시뮬레이션 계획을 만드는 순수 함수·계획 타입이 <b>Core.Devices 안에만</b> 있고,
    /// 어댑터·shim 계약이 그것을 참조하지 않는다. 데코레이터가 생기면 <c>ConnectAsync</c>가
    /// 오염되어 촬영 경로까지 번진다.
    /// </summary>
    [Fact]
    public void B7_External_Camera_Contracts_Do_Not_Know_About_Simulation()
    {
        foreach (var type in new[] { typeof(IExternalCamera), typeof(INikonSdkShim) })
        {
            var referenced = type.GetMethods()
                .SelectMany(m => m.GetParameters().Select(p => p.ParameterType).Append(m.ReturnType))
                .Distinct()
                .ToArray();

            Assert.DoesNotContain(referenced, t => t == typeof(ExternalDiscoverySimPlan));
            Assert.DoesNotContain(referenced, t => t == typeof(TestModeOptions));
            Assert.DoesNotContain(referenced, t => t == typeof(ITestModeService));
        }
    }

    /// <summary>
    /// ★ TS1(개정) — <c>ExternalCameraSimulation.Plan</c>의 소비자가 <b>정확히 두 곳</b>이고 둘 다
    /// <c>IsTestUser</c> 게이트를 가진 파일 안에 있다.
    /// <para>
    /// 설계 원문의 TS1은 "분기는 검색 시퀀스 1곳"이었다. 팀리드 지시로 <b>표시 전용</b> 소비자가 하나
    /// 늘었다(테스트 모드 배너 접미) — 관측을 대체하지 않고 문자열만 만든다. 늘어난 소비자가 조용히
    /// 더 늘어나는 것을 막아야 봉인이 유지되므로, 허용 목록을 정적 검사로 고정한다.
    /// </para>
    /// <para>
    /// 왜 정적 검사인가: 새 소비자는 <b>추가되는 코드</b>라서 기존 단위 테스트가 잡지 못한다
    /// (<c>T28_IsEnabled_Is_Only_Used_For_Banner_And_Registration</c>과 같은 계열의 안전망).
    /// </para>
    /// </summary>
    [Fact]
    public void B7_Simulation_Plan_Has_Exactly_Two_Gated_Consumers()
    {
        var srcDir = FindSrcDir();
        var allowed = new[]
        {
            // 관측 대체(설정 화면 검색 시퀀스) — 유일한 "적용" 지점.
            Path.Combine(srcDir, "MCPhoto.App", "ViewModels", "SettingsViewModel.cs"),
            // 표시 전용(테스트 모드 배너 접미) — 문자열만 만든다.
            Path.Combine(srcDir, "MCPhoto.App", "AppShellViewModel.cs"),
        };

        var consumers = new List<string>();
        foreach (var file in Directory.EnumerateFiles(srcDir, "*.cs", SearchOption.AllDirectories))
        {
            var lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (!line.Contains("ExternalCameraSimulation.Plan(", StringComparison.Ordinal)) continue;
                if (line.TrimStart().StartsWith("//", StringComparison.Ordinal)) continue;
                if (line.TrimStart().StartsWith("///", StringComparison.Ordinal)) continue;
                consumers.Add(file);
            }
        }

        // 정의 파일 자체(Plan 선언)는 호출이 아니므로 목록에 없어야 한다.
        Assert.Equal(2, consumers.Count);
        foreach (var file in consumers)
        {
            Assert.Contains(file, allowed, StringComparer.OrdinalIgnoreCase);
            // ★ 두 소비자 모두 IsTestUser 게이트를 같은 파일 안에 갖는다(TS2) —
            //   게이트 없는 파일에서 Plan을 부르면 실계정 세션에 시뮬레이션이 새거나 거짓 표시가 된다.
            Assert.Contains("IsTestUser", File.ReadAllText(file), StringComparison.Ordinal);
        }
    }

    private static string FindSrcDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src");
            if (Directory.Exists(Path.Combine(candidate, "MCPhoto.App"))) return candidate;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("src 디렉터리를 찾지 못함");
    }

    // ══════════ T-B8: 봉인(실계정) ══════════

    /// <summary>
    /// ★ TS2·E22 — 테스트 ini가 켜져 있어도 <b>실계정으로 로그인한 세션</b>에는 시뮬레이션이 적용되지 않는다.
    /// <para>
    /// 이 테스트가 잠그는 것은 <c>IsEnabled</c> 단독 분기의 금지다: 그렇게 쓰면 테스트 ini를 켠 채 실계정으로
    /// 일하는 운영자가 가짜 "연결 확인됨"을 보고 실장비 진단을 그르친다. 값이 전부 같은 별 인스턴스도
    /// false여야 하므로 게이트는 참조 동일성이어야 한다.
    /// </para>
    /// </summary>
    [Fact]
    public async Task B8_Real_Account_Session_Gets_Real_Observation_Not_Simulation()
    {
        var testMode = new FakeTestModeService(
            "[Test]\nTestMode=1\nId=qa\nEmail=qa@example.com\nRole=admin\nExternalCamera=1\nExternalCameraType=0\n");
        Assert.True(testMode.IsEnabled);   // ini는 분명히 켜져 있다

        // 값이 전부 같은 **별 인스턴스**(실 SSO 로그인이 만든 계정 모사).
        var impostor = new User
        {
            Id = testMode.Options.Id,
            Email = testMode.Options.Email,
            Role = testMode.Options.Role,
        };
        Assert.False(testMode.IsTestUser(impostor));

        var external = new FakeExternalCamera { CanControl = false, ReadinessReason = "SDK 모듈이 설치되지 않았습니다" };
        int probeCalls = 0;
        var vm = MakeVm(SettingsWith(s => s.ExternalCameraEnabled = true), role: null, external,
            probe: () => { probeCalls++; return Array.Empty<string>(); },
            testMode: testMode, loginUser: impostor);
        await vm.OnEnterAsync();

        await vm.DiscoverExternalCameraCommand.ExecuteAsync(null);

        // 실관측 경로: 프로브 1회 + 준비도 1회, W38 없음, 콤보 sentinel 단독.
        Assert.Equal(1, probeCalls);
        Assert.Equal(1, external.ReadinessCalls);
        Assert.Equal(SettingsViewModel.DiscoveryUndeterminedText, vm.DiscoveryHeadline);
        Assert.DoesNotContain(SettingsViewModel.DiscoverySimulatedText, vm.DiscoveryDetailLines);
        Assert.Single(vm.RecognizedCameraOptions);
    }

    /// <summary>
    /// 게스트 세션(로그인 없음)도 시뮬레이션 대상이 아니다 — <c>IsTestUser(null)</c>은 false다.
    /// (검색 커맨드 자체도 게스트에게 잠기지만, 게이트가 하나라도 남았다고 다른 하나를 느슨하게 두지 않는다)
    /// </summary>
    [Fact]
    public async Task B8_Guest_Session_Is_Not_Simulated()
    {
        var testMode = new FakeTestModeService(
            "[Test]\nTestMode=1\nRole=admin\nExternalCamera=1\nExternalCameraType=0\n");
        var external = new FakeExternalCamera { CanControl = false, ReadinessReason = "SDK 모듈이 설치되지 않았습니다" };
        var vm = MakeVm(SettingsWith(s => s.ExternalCameraEnabled = true), role: null, external,
            testMode: testMode);
        await vm.OnEnterAsync();

        Assert.False(vm.DiscoverExternalCameraCommand.CanExecute(null));

        // 커맨드 밖 호출(연타·테스트 경로)에서도 시뮬레이션이 적용되지 않는다.
        await vm.DiscoverExternalCameraCommand.ExecuteAsync(null);

        Assert.DoesNotContain(SettingsViewModel.DiscoverySimulatedText, vm.DiscoveryDetailLines);
    }

    /// <summary>
    /// 테스트 모드 서비스가 <b>미주입</b>이면 시뮬레이션이 성립하지 않는다(프로덕션 DI는 항상 주입하지만,
    /// 미주입이 조용히 다른 동작을 만들지 않는다는 것을 고정한다).
    /// </summary>
    [Fact]
    public async Task B8_Missing_Test_Mode_Service_Never_Simulates()
    {
        var external = new FakeExternalCamera { CanControl = false, ReadinessReason = "SDK 모듈이 설치되지 않았습니다" };
        var vm = MakeVm(SettingsWith(s => s.ExternalCameraEnabled = true), UserRole.Admin, external);
        await vm.OnEnterAsync();

        await vm.DiscoverExternalCameraCommand.ExecuteAsync(null);

        Assert.Equal(SettingsViewModel.DiscoveryUndeterminedText, vm.DiscoveryHeadline);
        Assert.DoesNotContain(SettingsViewModel.DiscoverySimulatedText, vm.DiscoveryDetailLines);
        Assert.Equal(1, external.ReadinessCalls);
    }
}
