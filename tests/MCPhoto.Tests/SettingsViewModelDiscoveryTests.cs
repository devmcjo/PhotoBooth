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
using MCPhoto.Tests.Fakes;

namespace MCPhoto.Tests;

/// <summary>
/// it24 Step 6: 설정 화면의 장치 검색 커맨드·게이트 배선 검증(설계 §12.3 T-D1~D5·T-V3').
/// it25 Step 1: 프린터 표면 환원 후의 보존·무접촉 단정(T-A1').
/// <para>
/// 여기서 증명되는 것은 <b>"관측이 이렇게 들어오면 화면은 이렇게 말한다"</b>까지다. 실물 D5300의 WMI 관측
/// 여부·이름(U1·U2)과 SDK 정상 상태의 실거동(U6)은 실기 단계의 몫이다(it24 §12.4 정직 목록).
/// </para>
/// </summary>
public class SettingsViewModelDiscoveryTests
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

    private static string TempIni() => Path.Combine(Path.GetTempPath(), $"svm_it24_{Guid.NewGuid():N}.ini");

    /// <summary>
    /// 검색 테스트용 VM. WMI 프로브는 <b>반드시 주입</b>한다 — 기본값은 이 머신의 실제 장치를 돌려주므로
    /// 참고 라인(W23)·매칭 결과가 머신 구성에 따라 달라져 상태표 검증이 흔들린다.
    /// </summary>
    private static SettingsViewModel MakeVm(IniSettingsService settings, UserRole? role,
        IExternalCamera? external = null,
        Func<IReadOnlyList<string>>? probe = null)
    {
        var session = new SessionContext();
        if (role is { } r) session.Login(new User { Id = "u", Role = r });
        _ = settings.Current;   // 호출측이 Current에 심어 둔 준비값을 파일에서 다시 읽어 덮지 않는다
        var shell = new AppShellViewModel(new IdleWatchdog(), settings, new EmptyServiceProvider(), session);
        return new SettingsViewModel(shell, settings, new StubCameraService(), new StubCameraTestDialog(),
            new StubDiagnosticsDialog(), new FakeFirebaseClient { IsInitialized = false },
            external ?? new NullExternalCamera(),
            logger: null, licenseNotice: null,
            probePortableDevices: probe ?? (() => Array.Empty<string>()));
    }

    private static IniSettingsService SettingsWith(Action<AppSettings> configure, string? path = null)
    {
        var svc = new IniSettingsService(iniPath: path ?? TempIni());
        configure(svc.Load());
        return svc;
    }

    // ══════════ S0: 검색 전 ══════════

    [Fact]
    public async Task S0_Before_Search_Says_Not_Searched_With_No_Details()
    {
        var vm = MakeVm(SettingsWith(s => s.ExternalCameraEnabled = true), UserRole.Admin);
        await vm.OnEnterAsync();

        Assert.Equal(SettingsViewModel.DiscoveryNotSearchedText, vm.DiscoveryHeadline);
        Assert.Empty(vm.DiscoveryDetailLines);
        Assert.False(vm.IsDiscovering);
    }

    // ══════════ S1: 검색 중 ══════════

    [Fact]
    public async Task S1_During_Search_Shows_Searching_And_Blocks_Reentry()
    {
        // 프로브를 게이트로 붙잡아 "검색 중" 상태를 결정적으로 관측한다(Task 블로킹 연산을 쓰지 않는다).
        using var gate = new ManualResetEventSlim(false);
        var external = new FakeExternalCamera { CanControl = true, ConnectResult = true };
        var vm = MakeVm(SettingsWith(s => s.ExternalCameraEnabled = true), UserRole.Admin, external,
            probe: () => { gate.Wait(); return Array.Empty<string>(); });
        await vm.OnEnterAsync();

        var inFlight = vm.DiscoverExternalCameraCommand.ExecuteAsync(null);

        Assert.True(vm.IsDiscovering);
        Assert.Equal(SettingsViewModel.DiscoverySearchingText, vm.DiscoveryHeadline);
        Assert.False(vm.DiscoverExternalCameraCommand.CanExecute(null));   // 단일 비행: 버튼 비활성

        // T-D3: 진행 중 재진입은 무시된다(연결 시도가 두 번 일어나지 않는다).
        await vm.DiscoverExternalCameraCommand.ExecuteAsync(null);

        gate.Set();
        await inFlight;

        Assert.False(vm.IsDiscovering);
        Assert.Equal(1, external.ConnectCalls);
    }

    // ══════════ S2: 제어 스택 미비 + USB 후보 없음 ══════════

    [Fact]
    public async Task S2_Stack_Missing_Says_Undetermined_Not_Absent()
    {
        var external = new FakeExternalCamera
        {
            CanControl = false,
            ReadinessReason = "SDK 모듈이 설치되지 않았습니다",
        };
        var vm = MakeVm(SettingsWith(s => s.ExternalCameraEnabled = true), UserRole.Admin, external);
        await vm.OnEnterAsync();

        await vm.DiscoverExternalCameraCommand.ExecuteAsync(null);

        // ★ 거짓말 금지: 장치 부재를 단정하지 않고 "확인할 수 없습니다"라고만 말한다.
        Assert.Equal(SettingsViewModel.DiscoveryUndeterminedText, vm.DiscoveryHeadline);
        Assert.Contains("확인할 수 없습니다", vm.DiscoveryHeadline);
        Assert.DoesNotContain("찾지 못했습니다", vm.DiscoveryHeadline);
        Assert.NotEqual(SettingsViewModel.DiscoveryNotFoundText, vm.DiscoveryHeadline);
        Assert.Equal(new[] { "SDK 모듈이 설치되지 않았습니다" }, vm.DiscoveryDetailLines);
    }

    [Fact]
    public async Task S2_Lists_Unmatched_Portable_Devices_As_Reference_Note()
    {
        // U2(제네릭 이름) 상황: 매칭은 miss나지만 운영자가 육안으로 판단할 단서를 남긴다.
        var external = new FakeExternalCamera { CanControl = false, ReadinessReason = "SDK 모듈이 설치되지 않았습니다" };
        var vm = MakeVm(SettingsWith(s => s.ExternalCameraEnabled = true), UserRole.Admin, external,
            probe: () => new[] { "MTP Portable Device", "새 볼륨" });
        await vm.OnEnterAsync();

        await vm.DiscoverExternalCameraCommand.ExecuteAsync(null);

        Assert.Equal(SettingsViewModel.DiscoveryUndeterminedText, vm.DiscoveryHeadline);
        Assert.Contains(
            SettingsViewModel.DiscoveryOtherDevicesText("MTP Portable Device, 새 볼륨"),
            vm.DiscoveryDetailLines);
    }

    // ══════════ S3: 제어 스택 미비 + USB 후보 감지 ══════════

    [Fact]
    public async Task S3_Detected_But_Uncontrollable()
    {
        var external = new FakeExternalCamera { CanControl = false, ReadinessReason = "SDK 모듈이 설치되지 않았습니다" };
        var vm = MakeVm(SettingsWith(s => s.ExternalCameraEnabled = true), UserRole.Admin, external,
            probe: () => new[] { "NIKON D5300" });
        await vm.OnEnterAsync();

        await vm.DiscoverExternalCameraCommand.ExecuteAsync(null);

        Assert.Equal(SettingsViewModel.DiscoveryDetectedText("NIKON D5300"), vm.DiscoveryHeadline);
        Assert.Equal(SettingsViewModel.DiscoveryUncontrollableText, vm.DiscoveryDetailLines[0]);
        Assert.Contains("SDK 모듈이 설치되지 않았습니다", vm.DiscoveryDetailLines);
        // 매칭이 있으면 참고 라인은 붙지 않는다(감지 라인이 이미 그 역할을 한다).
        Assert.DoesNotContain(vm.DiscoveryDetailLines, l => l.StartsWith("참고:", StringComparison.Ordinal));
        Assert.Equal(0, external.ConnectCalls);   // 스택 미비 → USB 미접촉
    }

    // ══════════ S4: 스택 정상 + 연결 실패 + USB 후보 없음 ══════════

    [Fact]
    public async Task S4_Stack_Ready_And_Connect_Failed_Says_Not_Found()
    {
        var external = new FakeExternalCamera
        {
            CanControl = true,
            ConnectResult = false,
            Reason = "카메라가 연결되지 않았습니다 (USB·전원 확인)",
        };
        var vm = MakeVm(SettingsWith(s => s.ExternalCameraEnabled = true), UserRole.Admin, external);
        await vm.OnEnterAsync();

        await vm.DiscoverExternalCameraCommand.ExecuteAsync(null);

        // ★ 부재를 말할 자격이 생기는 유일한 상태 — 그래도 단정 완화형이다.
        Assert.Equal(SettingsViewModel.DiscoveryNotFoundText, vm.DiscoveryHeadline);
        Assert.Contains("찾지 못했습니다", vm.DiscoveryHeadline);
        Assert.Equal(new[] { "카메라가 연결되지 않았습니다 (USB·전원 확인)" }, vm.DiscoveryDetailLines);
        Assert.Equal(1, external.ConnectCalls);
    }

    // ══════════ S5: 스택 정상 + 연결 실패 + USB 후보 감지 ══════════

    [Fact]
    public async Task S5_Detected_But_Connect_Failed()
    {
        var external = new FakeExternalCamera
        {
            CanControl = true,
            ConnectResult = false,
            Reason = "카메라가 연결되지 않았습니다 (USB·전원 확인)",
        };
        var vm = MakeVm(SettingsWith(s => s.ExternalCameraEnabled = true), UserRole.Admin, external,
            probe: () => new[] { "Nikon D5300" });
        await vm.OnEnterAsync();

        await vm.DiscoverExternalCameraCommand.ExecuteAsync(null);

        Assert.Equal(SettingsViewModel.DiscoveryDetectedText("Nikon D5300"), vm.DiscoveryHeadline);
        Assert.Equal(SettingsViewModel.DiscoveryConnectFailedText, vm.DiscoveryDetailLines[0]);
    }

    // ══════════ S6: 연결 확인됨 ══════════

    [Fact]
    public async Task S6_Connected_Shows_Model_Battery_And_Test_Hint()
    {
        var external = new FakeExternalCamera { CanControl = true, ConnectResult = true };
        var vm = MakeVm(SettingsWith(s => s.ExternalCameraEnabled = true), UserRole.Admin, external);
        await vm.OnEnterAsync();

        await vm.DiscoverExternalCameraCommand.ExecuteAsync(null);

        Assert.Equal(SettingsViewModel.DiscoveryConnectedText("Nikon D5300"), vm.DiscoveryHeadline);
        // "연결됨"이 아니라 "연결 확인됨" — 표시 시점엔 이미 해제되어 있다.
        Assert.Contains("연결 확인됨", vm.DiscoveryHeadline);
        Assert.Contains(SettingsViewModel.DiscoveryBatteryText(75), vm.DiscoveryDetailLines);
        Assert.Contains(SettingsViewModel.DiscoveryTestHintText, vm.DiscoveryDetailLines);
        // it25 §6.4 확장: S6은 인식 콤보를 채우는 **유일한** 상태다(sentinel + 인식 1행).
        Assert.Equal(2, vm.RecognizedCameraOptions.Count);
        Assert.Equal("Nikon D5300", vm.RecognizedCameraOptions[1].Display);
        // 실경로에서는 시뮬레이션 표식이 붙지 않는다.
        Assert.DoesNotContain(SettingsViewModel.DiscoverySimulatedText, vm.DiscoveryDetailLines);
    }

    /// <summary>★ T-D2 — 검색은 순간 관찰이다: 성공해도 연결을 잔류시키지 않는다(§5.5).</summary>
    [Fact]
    public async Task S6_Disconnects_Immediately_After_Snapshot()
    {
        var external = new FakeExternalCamera { CanControl = true, ConnectResult = true };
        var vm = MakeVm(SettingsWith(s => s.ExternalCameraEnabled = true), UserRole.Admin, external);
        await vm.OnEnterAsync();

        await vm.DiscoverExternalCameraCommand.ExecuteAsync(null);

        Assert.Equal(1, external.DisconnectCalls);
        Assert.False(external.IsAvailable);
    }

    [Fact]
    public async Task S6_Without_Battery_Still_Reports_Connected()
    {
        // E15: capability 조회 실패는 배터리 라인만 빼고 헤드라인 판정을 바꾸지 않는다.
        var external = new FakeExternalCamera { CanControl = true, ConnectResult = true, Capabilities = null };
        var vm = MakeVm(SettingsWith(s => s.ExternalCameraEnabled = true), UserRole.Admin, external);
        await vm.OnEnterAsync();

        await vm.DiscoverExternalCameraCommand.ExecuteAsync(null);

        Assert.Equal(SettingsViewModel.DiscoveryConnectedText("Nikon D5300"), vm.DiscoveryHeadline);
        Assert.DoesNotContain(vm.DiscoveryDetailLines, l => l.StartsWith("배터리", StringComparison.Ordinal));
        Assert.Contains(SettingsViewModel.DiscoveryTestHintText, vm.DiscoveryDetailLines);
    }

    // ══════════ S7: 검색 중 예외 ══════════

    [Fact]
    public async Task S7_Unexpected_Exception_Reports_Failure_And_Releases_Button()
    {
        var external = new FakeExternalCamera { CanControl = true, ConnectResult = true };
        var vm = MakeVm(SettingsWith(s => s.ExternalCameraEnabled = true), UserRole.Admin, external,
            probe: () => throw new InvalidOperationException("WMI 붕괴"));
        await vm.OnEnterAsync();

        await vm.DiscoverExternalCameraCommand.ExecuteAsync(null);

        Assert.Equal(SettingsViewModel.DiscoveryFailedText, vm.DiscoveryHeadline);
        Assert.Empty(vm.DiscoveryDetailLines);
        // ★ finally 확정: 예외 경로에서도 버튼이 영구 잠기지 않는다(it20 교훈).
        Assert.False(vm.IsDiscovering);
        Assert.True(vm.DiscoverExternalCameraCommand.CanExecute(null));
    }

    // ══════════ T-D4: 스택 미비면 USB를 아예 건드리지 않는다 ══════════

    [Fact]
    public async Task Stack_Missing_Never_Calls_Connect()
    {
        var external = new FakeExternalCamera { CanControl = false, ReadinessReason = "SDK 모듈이 설치되지 않았습니다" };
        var vm = MakeVm(SettingsWith(s => s.ExternalCameraEnabled = true), UserRole.Admin, external);
        await vm.OnEnterAsync();

        await vm.DiscoverExternalCameraCommand.ExecuteAsync(null);

        Assert.Equal(0, external.ConnectCalls);
        Assert.Equal(0, external.DisconnectCalls);
        Assert.Equal(1, external.ReadinessCalls);
    }

    /// <summary>
    /// ★ it23 F14 확장: 검색 커맨드를 부르지 않으면 설정 화면은 외부 카메라를 <b>한 번도</b> 접촉하지 않는다.
    /// (진입 부수효과로 USB 세션이 성립하지 않는다는 §5.4 결정의 회귀 잠금)
    /// </summary>
    [Fact]
    public async Task Entering_Settings_Does_Not_Touch_External_Camera()
    {
        var external = new FakeExternalCamera { CanControl = true, ConnectResult = true };
        var vm = MakeVm(SettingsWith(s => s.ExternalCameraEnabled = false), UserRole.Admin, external);

        await vm.OnEnterAsync();

        Assert.False(external.Touched);
    }

    // ══════════ T-D5: 검색 게이트(§4.3) ══════════

    [Fact]
    public async Task Guest_Cannot_Search_But_TempUser_Can()
    {
        var guest = MakeVm(SettingsWith(s => s.ExternalCameraEnabled = true), role: null);
        await guest.OnEnterAsync();
        Assert.False(guest.DiscoverExternalCameraCommand.CanExecute(null));

        // TempUser는 편집은 못 하지만 진단(검색)은 할 수 있다 — 진단·상태 모달과 같은 눈높이.
        var temp = MakeVm(SettingsWith(s => s.ExternalCameraEnabled = true), UserRole.TempUser);
        await temp.OnEnterAsync();
        Assert.False(temp.CanEditExternalCamera);
        Assert.True(temp.DiscoverExternalCameraCommand.CanExecute(null));
    }

    // ══════════ T-V3': 게스트 가시성 정책 변경(§4.1·§4.3) ══════════

    /// <summary>
    /// ★ 구 T-V3("게스트는 외부 장치 섹션이 Collapsed") 단정의 <b>대체 잠금</b>.
    /// 그 단정을 지우고 넘어가면 누군가 다시 섹션을 숨겨도 아무도 모르므로, 같은 사실을
    /// <b>반대 방향으로</b> 못박는다: 게스트에게 섹션은 <b>보이되</b> 전 편집 표면이 잠긴다.
    /// <para>
    /// 표시 값은 ini 원값이다 — 외부 장치 토글은 편집 게이트이지 <b>동작 게이트가 아니므로</b>
    /// 관리자가 켜 둔 DSLR은 게스트 세션에서도 동작한다. off로 보여 주는 것이 오히려 거짓 표시다(§4.2).
    /// </para>
    /// (XAML 쪽 짝: <c>XamlResourceTests.SettingsView_External_Device_Section_Is_Not_Hidden_From_Guests</c>)
    /// </summary>
    [Fact]
    public async Task Guest_Sees_Section_Values_But_Cannot_Edit()
    {
        var path = TempIni();
        try
        {
            var settings = SettingsWith(s =>
            {
                s.ExternalCameraEnabled = true;
                s.PhotoPrinterEnabled = true;
                s.PhotoPrinterName = "Canon SELPHY CP1500";
            }, path);
            var vm = MakeVm(settings, role: null);
            await vm.OnEnterAsync();

            Assert.True(vm.IsGuest);

            // ① 편집 표면 전부 잠김: 토글·인식 콤보·노출 3행이 이 한 값에 매달려 있다.
            //    (프린터 토글은 it25 §4.1에서 역할과 무관한 하드코딩 Disable로 환원됐다 — XAML 쪽 짝 참조)
            Assert.False(vm.CanEditExternalCamera);
            // ② 검색도 게스트에겐 불가(진단 액션이지만 익명 손님보다는 좁은 게이트).
            Assert.False(vm.DiscoverExternalCameraCommand.CanExecute(null));
            // ③ 게이트 노티는 "로그인 필요"다 — "권한 없음"이 뜨면 "로그인하면 되는가"에 답하지 못한다.
            Assert.False(vm.IsExternalEditDenied);

            // ④ ★ 그런데 값은 보인다(강제 off 없음) — 섹션이 숨겨져 있었다면 검증할 필요조차 없던 항목이다.
            Assert.True(vm.ExternalCameraEnabled);
            Assert.True(vm.PhotoPrinterEnabled);   // 편집 불가 토글이어도 ini 원값을 정직하게 보여 준다
            Assert.Equal(SettingsViewModel.DiscoveryNotSearchedText, vm.DiscoveryHeadline);

            // ⑤ 저장을 눌러도 ini 원값이 그대로다(보이는 값을 되기록해 관리자 구성을 클로버하지 않는다).
            vm.ExternalCameraEnabled = false;
            vm.PhotoPrinterEnabled = false;
            vm.SaveSettingsCommand.Execute(null);
            var r = new IniSettingsService(iniPath: path).Load();
            Assert.True(r.ExternalCameraEnabled);
            Assert.True(r.PhotoPrinterEnabled);
            Assert.Equal("Canon SELPHY CP1500", r.PhotoPrinterName);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task TempUser_Sees_Permission_Caption_Not_Login_Note()
    {
        var vm = MakeVm(SettingsWith(s => s.ExternalCameraEnabled = true), UserRole.TempUser);
        await vm.OnEnterAsync();

        Assert.False(vm.IsGuest);
        Assert.False(vm.CanEditExternalCamera);
        Assert.True(vm.IsExternalEditDenied);   // ★ 로그인했으나 편집 불가 = "권한 없음"
    }

    [Fact]
    public async Task User_Has_No_Gate_Caption()
    {
        var vm = MakeVm(SettingsWith(s => s.ExternalCameraEnabled = true), UserRole.User);
        await vm.OnEnterAsync();

        Assert.True(vm.CanEditExternalCamera);
        Assert.False(vm.IsExternalEditDenied);
        Assert.False(vm.IsGuest);
    }

    // ══════════ T-A1' (it25 §4): 프린터 환원 — VM은 열거자를 접촉하지 않고 2키를 기록하지 않는다 ══════════
    //
    // it24의 프린터 VM 테스트 12종(P3 목록·P2/P4 구분·P5 합성 행·저장 2키·토글 훅·단일 비행)은 표면이
    // 사라져 성립하지 않는다. 그 단정을 **지우지 않고** 두 갈래로 옮겼다:
    //   ① "명제 구분(R4)·예외 무투과"는 열거자 계약 계층으로 이관 —
    //      ExternalDeviceScaffoldTests(PrinterEnumerationResult 구분·SystemPrinterEnumerator 예외 무투과) +
    //      아래 Real_Printer_Enumeration_Never_Throws(§12.4 존치).
    //   ② "저장값을 지우지 않는다"·"스풀러를 건드리지 않는다"는 아래 두 테스트로 압축.

    /// <summary>
    /// ★ 전 역할에서 프린터 2키가 <b>미기록</b>으로 보존된다(§4.1·§4.3).
    /// <para>
    /// 구 <c>User_Saves_Printer_Two_Keys</c>의 <b>단정 반전</b>이다 — it24는 User가 2키를 기록하는 것을
    /// 고정했고, it25는 <b>어느 역할도 기록하지 않는</b> 것을 고정한다. 미기록이면 Clone 원값이 그대로
    /// 재기록되므로 라운드트립 보존이 자동 성립한다. 키를 <c>WriteFrom</c>에서 빼는 것과는 다르다 —
    /// 그렇게 하면 기존 ini의 값이 첫 저장에서 소멸한다.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(UserRole.Admin)]
    [InlineData(UserRole.User)]
    [InlineData(UserRole.TempUser)]
    [InlineData(null)]
    public async Task Save_Never_Writes_Printer_Keys(UserRole? role)
    {
        var path = TempIni();
        try
        {
            var settings = SettingsWith(s =>
            {
                s.PhotoPrinterEnabled = true;
                s.PhotoPrinterName = @"\\print01\Photo-Lab";
            }, path);
            Assert.True(settings.Save());

            var vm = MakeVm(settings, role);
            await vm.OnEnterAsync();

            // 토글 표시값은 ini 원값이다(강제 off 표시 금지).
            Assert.True(vm.PhotoPrinterEnabled);

            // 편집 불가 컨트롤이지만, VM 속성을 직접 뒤집어도 저장 경로가 ini를 건드리지 않아야 한다.
            vm.PhotoPrinterEnabled = false;
            vm.SaveSettingsCommand.Execute(null);

            var r = new IniSettingsService(iniPath: path).Load();
            Assert.True(r.PhotoPrinterEnabled);
            Assert.Equal(@"\\print01\Photo-Lab", r.PhotoPrinterName);   // 표면이 없는 잔존 키도 보존
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    /// <summary>
    /// ★ 설정 VM은 <b>어떤 시점에도</b> 프린터 열거자를 접촉하지 않는다(소비자 0 스캐폴드).
    /// <para>
    /// 구 트리거 테스트 5종(진입 열거·토글 훅·저장 재트리거·패널 off·단일 비행)을 하나로 압축한 형태다.
    /// 열거자를 <b>주입할 자리 자체가 사라졌으므로</b>(ctor 인자 제거) 접촉 0을 타입 수준에서 고정한다 —
    /// 페이크 호출 횟수보다 강한 단정이며, 누군가 배선을 되살리면 컴파일 단계에서 이 테스트가 깨진다.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Settings_Vm_Never_Touches_Printer_Enumerator()
    {
        // ① ctor에 열거자를 넘길 방법이 없다.
        Assert.DoesNotContain(
            typeof(SettingsViewModel).GetConstructors()
                .SelectMany(c => c.GetParameters())
                .Select(p => p.ParameterType),
            t => t == typeof(IPrinterEnumerator));

        // ② 필드로도 붙들지 않는다(생성자 밖에서 서비스 로케이터로 끌어오는 경로 차단).
        Assert.DoesNotContain(
            typeof(SettingsViewModel)
                .GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                .Select(f => f.FieldType),
            t => t == typeof(IPrinterEnumerator));

        // ③ 진입·토글·저장 어느 경로도 예외 없이 지나간다(표면이 사라졌으니 상태 문구도 없다).
        var vm = MakeVm(SettingsWith(s => s.PhotoPrinterEnabled = true), UserRole.Admin);
        await vm.OnEnterAsync();
        vm.PhotoPrinterEnabled = false;
        vm.PhotoPrinterEnabled = true;
        vm.SaveSettingsCommand.Execute(null);
    }

    // ══════════ 실 스풀러 스모크(§14 Step 5 수동 검증 ①의 자동화 형태) ══════════

    /// <summary>
    /// 실제 <c>System.Printing</c> 열거가 <b>예외를 던지지 않는다</b>. 결과 개수는 머신 구성에 따라 다르므로
    /// 값을 단정하지 않는다 — 검증 대상은 "어떤 환경에서도 결과 객체로 끝난다"는 계약이다.
    /// <para>
    /// ★ it25 §12.4: 프린터 표면이 환원된 뒤에도 <b>존치</b>한다. 열거자는 소비자 0 스캐폴드이고,
    /// 이 테스트가 그 스캐폴드의 "예외를 던지지 않는다" 계약이 인쇄 이터레이션까지 살아 있음을 잠근다.
    /// </para>
    /// <para>
    /// 스풀러 중지 상태(P4)의 실측은 서비스 중지 권한이 필요해 여기서 다루지 않는다 — 그 경로와
    /// P2≠P4 구분은 <c>ExternalDeviceScaffoldTests</c>가 <see cref="FakePrinterEnumerator"/>로 검증한다.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Real_Printer_Enumeration_Never_Throws()
    {
        IPrinterEnumerator enumerator = new SystemPrinterEnumerator();

        var result = await enumerator.EnumerateAsync();

        Assert.NotNull(result);
        Assert.NotNull(result.Printers);
        // 열거가 성공했다면 이름이 빈 행은 없어야 한다(콤보에 빈 줄이 뜨지 않는다).
        Assert.DoesNotContain(result.Printers, p => string.IsNullOrWhiteSpace(p.Name));
        // 기본 프린터는 최대 1대다(중복이면 "(기본)" 접미가 여러 줄에 붙어 오해를 만든다).
        Assert.True(result.Printers.Count(p => p.IsDefault) <= 1);
    }
}
