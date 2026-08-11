using System.IO;
using System.Linq;
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
using MCPhoto.Tests.Fakes;

namespace MCPhoto.Tests;

/// <summary>
/// it23 Step 8: 설정 화면의 외부 카메라 섹션(권한 게이트·모델 콤보·노출 슬라이더/입력) 검증.
/// 설계 §14.4 T-V1~T-V5.
/// </summary>
public class SettingsViewModelExternalCameraTests
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

    private static string TempIni() => Path.Combine(Path.GetTempPath(), $"svm_ext_{Guid.NewGuid():N}.ini");

    private static SettingsViewModel MakeVm(IniSettingsService settings, UserRole? role,
        IExternalCamera? external = null)
    {
        var session = new SessionContext();
        if (role is { } r) session.Login(new User { Id = "u", Role = r });
        // ⚠️ 여기서 Load()를 다시 부르지 않는다 — 호출측이 Current에 심어 둔 준비값(토글·노출)을
        //    파일에서 다시 읽어 덮어써 버린다. Current는 최초 접근 시 지연 로드된다.
        _ = settings.Current;
        var shell = new AppShellViewModel(new IdleWatchdog(), settings, new EmptyServiceProvider(), session);
        return new SettingsViewModel(shell, settings, new StubCameraService(), new StubCameraTestDialog(),
            new StubDiagnosticsDialog(), new FakeFirebaseClient { IsInitialized = false },
            external ?? new NullExternalCamera());
    }

    private static ExposureDomain SampleDomain() => new(
        new ExposureDomainEntry(new[] { "1/60", "1/125", "1/250" }, 1),
        new ExposureDomainEntry(new[] { "f/4", "f/5.6", "f/8" }, 0),
        new ExposureDomainEntry(new[] { "100", "200", "400" }, 2));

    // ── 권한 게이트(§8.3) ──

    [Theory]
    [InlineData(UserRole.User, true)]
    [InlineData(UserRole.AdvancedUser, true)]
    [InlineData(UserRole.Manager, true)]
    [InlineData(UserRole.Admin, true)]
    [InlineData(UserRole.TempUser, false)]
    public void CanEditExternalCamera_Follows_Role(UserRole role, bool expected)
    {
        var vm = MakeVm(new IniSettingsService(iniPath: TempIni()), role);
        Assert.Equal(expected, vm.CanEditExternalCamera);
    }

    [Fact]
    public void Guest_Cannot_Edit_External_Camera()
    {
        var vm = MakeVm(new IniSettingsService(iniPath: TempIni()), role: null);
        Assert.True(vm.IsGuest);
        Assert.False(vm.CanEditExternalCamera);
    }

    // ── T-V1: TempUser Load→Save는 ini 원값을 보존한다 ──

    [Fact]
    public async Task TempUser_Save_Preserves_All_Four_Ini_Values()
    {
        var path = TempIni();
        try
        {
            var settings = new IniSettingsService(iniPath: path);
            var s = settings.Load();
            s.ExternalCameraEnabled = true;            // 관리자가 맞춰 둔 장비 구성
            s.ExternalShutterSpeed = "1/200";
            s.ExternalAperture = "f/8";
            s.ExternalIso = "200";
            Assert.True(settings.Save());

            var vm = MakeVm(settings, UserRole.TempUser);
            await vm.OnEnterAsync();

            // §8.3-1: 편집 불가여도 강제 off 하지 않는다 — ini 원값을 그대로 보여 준다.
            Assert.False(vm.CanEditExternalCamera);
            Assert.True(vm.ExternalCameraEnabled);

            // 사용자가 UI를 못 만지지만, 저장 경로가 값을 클로버하지 않는지가 핵심이다.
            vm.ExternalCameraEnabled = false;
            vm.SaveSettingsCommand.Execute(null);

            var r = new IniSettingsService(iniPath: path).Load();
            Assert.True(r.ExternalCameraEnabled);      // ★ 원값 보존
            Assert.Equal("1/200", r.ExternalShutterSpeed);
            Assert.Equal("f/8", r.ExternalAperture);
            Assert.Equal("200", r.ExternalIso);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task Guest_Save_Preserves_All_Four_Ini_Values()
    {
        var path = TempIni();
        try
        {
            var settings = new IniSettingsService(iniPath: path);
            var s = settings.Load();
            s.ExternalCameraEnabled = true;
            s.ExternalIso = "800";
            Assert.True(settings.Save());

            var vm = MakeVm(settings, role: null);
            await vm.OnEnterAsync();
            vm.SaveSettingsCommand.Execute(null);

            var r = new IniSettingsService(iniPath: path).Load();
            Assert.True(r.ExternalCameraEnabled);
            Assert.Equal("800", r.ExternalIso);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    // ── T-V2: User 로그인 — 토글·모델·노출값 저장 반영 ──

    [Fact]
    public async Task User_Saves_Toggle_Model_And_Exposure()
    {
        var path = TempIni();
        try
        {
            var settings = new IniSettingsService(iniPath: path);
            var vm = MakeVm(settings, UserRole.User);
            await vm.OnEnterAsync();

            Assert.False(vm.ExternalCameraEnabled);
            Assert.Equal("NikonD5300", vm.ExternalCameraModel);

            vm.ExternalCameraEnabled = true;
            vm.ExposureParameters[0].Text = "1/160";   // 도메인 미확보 → 자유 입력(저장만)
            vm.ExposureParameters[1].Text = "f/2.8";
            vm.ExposureParameters[2].Text = "640";
            vm.SaveSettingsCommand.Execute(null);

            var r = new IniSettingsService(iniPath: path).Load();
            Assert.True(r.ExternalCameraEnabled);
            Assert.Equal("NikonD5300", r.ExternalCameraModel);
            Assert.Equal("1/160", r.ExternalShutterSpeed);
            Assert.Equal("f/2.8", r.ExternalAperture);
            Assert.Equal("640", r.ExternalIso);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    /// <summary>
    /// it25 §6 재작성: 구 <c>Model_Options_Come_From_Registry</c>가 고정했던 "콤보 = 레지스트리 전체"는
    /// 폐기됐다(콤보가 "인식된 카메라"가 됐다). <b>같은 사실을 두 갈래로 나눠</b> 다시 못박는다 —
    /// 지원 목록은 오버레이가 레지스트리에서 파생하고, 콤보는 인식 결과만 담는다.
    /// <para>
    /// 단정을 지우면 "레지스트리에 모델을 추가했는데 화면 어디에도 안 뜬다"는 회귀를 아무도 못 잡는다.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Registry_Feeds_Supported_Overlay_While_Combo_Holds_Recognition_Only()
    {
        var vm = MakeVm(new IniSettingsService(iniPath: TempIni()), UserRole.Admin);
        await vm.OnEnterAsync();

        // ① 지원 목록은 레지스트리 전수를 담는다(제조사·제품명 분리).
        var group = Assert.Single(vm.SupportedCameraGroups);
        Assert.Equal("Nikon", group.Manufacturer);
        Assert.Equal(new[] { "D5300" }, group.Models);
        Assert.Equal(ExternalCameraModels.All.Count, vm.SupportedCameraGroups.Sum(g => g.Models.Count));

        // ② 인식 콤보는 지원 목록이 아니다 — 검색 전에는 sentinel 단독이다.
        Assert.Single(vm.RecognizedCameraOptions);
        Assert.Equal(SettingsViewModel.RecognizedCameraNoneDisplay, vm.RecognizedCameraOptions[0].Display);
    }

    // ── T-V4: 도메인 미확보 — 슬라이더 disable + 자유 입력 저장 ──

    [Fact]
    public async Task Domain_Absent_Disables_Slider_And_Allows_Free_Text()
    {
        var settings = new IniSettingsService(iniPath: TempIni());
        var s = settings.Load();
        s.ExternalCameraEnabled = true;
        var vm = MakeVm(settings, UserRole.Admin);   // NullExternalCamera → 도메인 없음
        await vm.OnEnterAsync();

        Assert.False(vm.HasExposureDomain);
        foreach (var p in vm.ExposureParameters)
        {
            Assert.False(p.IsDomainAvailable);
            Assert.Equal(0, p.MaxIndex);
        }

        // 검증할 목록이 없으므로 아무 값이나 힌트 없이 받아들인다(적용 시점 검증이 안전망).
        vm.ExposureParameters[2].Text = "51200";
        Assert.Equal(string.Empty, vm.ExposureParameters[2].Hint);
        Assert.False(vm.ExposureParameters[2].HasHint);
    }

    [Fact]
    public async Task Disabled_External_Camera_Does_Not_Query_Domain()
    {
        // §9.1: 설정 진입이 USB 장치를 건드리지 않는다. 토글 off면 도메인 조회조차 하지 않는다.
        var external = new FakeExternalCamera { Domain = SampleDomain() };
        var settings = new IniSettingsService(iniPath: TempIni());
        settings.Load();   // ExternalCameraEnabled = false(기본)

        var vm = MakeVm(settings, UserRole.Admin, external);
        await vm.OnEnterAsync();

        Assert.Equal(0, external.ConnectCalls);   // ★ 연결 시도 없음
        Assert.False(vm.HasExposureDomain);
    }

    // ── T-V5: 도메인 확보 — 슬라이더 동기 / 불일치 입력 힌트 ──

    [Fact]
    public async Task Domain_Present_Binds_Saved_Value_To_Slider_Index()
    {
        var settings = new IniSettingsService(iniPath: TempIni());
        var s = settings.Load();
        s.ExternalCameraEnabled = true;
        s.ExternalShutterSpeed = "1/250";
        s.ExternalIso = "100";
        var external = new FakeExternalCamera { Domain = SampleDomain() };

        var vm = MakeVm(settings, UserRole.Admin, external);
        await vm.OnEnterAsync();

        Assert.True(vm.HasExposureDomain);
        var shutter = vm.ExposureParameters[0];
        Assert.True(shutter.IsDomainAvailable);
        Assert.Equal(2, shutter.MaxIndex);
        Assert.Equal(2, shutter.SelectedIndex);          // "1/250"의 인덱스
        Assert.Equal("1/250", shutter.Text);
        Assert.False(shutter.HasHint);

        var iso = vm.ExposureParameters[2];
        Assert.Equal(0, iso.SelectedIndex);              // "100"
    }

    [Fact]
    public async Task Domain_Present_Saved_Value_Absent_Falls_Back_To_Camera_Current_Index()
    {
        // 저장값이 도메인에 없으면 값을 버리지 않고 힌트를 띄우되, 슬라이더는 카메라 현재값 위치를 보여 준다.
        var settings = new IniSettingsService(iniPath: TempIni());
        var s = settings.Load();
        s.ExternalCameraEnabled = true;
        s.ExternalShutterSpeed = "1/100";                // 도메인에 없는 값
        var external = new FakeExternalCamera { Domain = SampleDomain() };

        var vm = MakeVm(settings, UserRole.Admin, external);
        await vm.OnEnterAsync();

        var shutter = vm.ExposureParameters[0];
        Assert.Equal("1/100", shutter.Text);             // 값을 버리지 않는다
        Assert.Equal(1, shutter.SelectedIndex);          // 카메라 현재값(CurrentIndex=1)
        Assert.True(shutter.HasHint);
        Assert.Equal(ExposureParameterViewModel.UnsupportedValueHint, shutter.Hint);
    }

    [Fact]
    public async Task Slider_Move_Syncs_Text_And_Clears_Hint()
    {
        var settings = new IniSettingsService(iniPath: TempIni());
        var s = settings.Load();
        s.ExternalCameraEnabled = true;
        s.ExternalAperture = "f/1.4";                     // 불일치 → 힌트 상태로 시작
        var external = new FakeExternalCamera { Domain = SampleDomain() };

        var vm = MakeVm(settings, UserRole.Admin, external);
        await vm.OnEnterAsync();

        var aperture = vm.ExposureParameters[1];
        Assert.True(aperture.HasHint);

        aperture.SelectedIndex = 2;                       // 슬라이더 이동
        Assert.Equal("f/8", aperture.Text);
        Assert.False(aperture.HasHint);
    }

    [Fact]
    public async Task Text_Matching_Domain_Syncs_Slider_Index()
    {
        var settings = new IniSettingsService(iniPath: TempIni());
        var s = settings.Load();
        s.ExternalCameraEnabled = true;
        var external = new FakeExternalCamera { Domain = SampleDomain() };

        var vm = MakeVm(settings, UserRole.Admin, external);
        await vm.OnEnterAsync();

        var iso = vm.ExposureParameters[2];
        iso.Text = "  400 ";                              // 공백 무시 정확 일치
        Assert.Equal(2, iso.SelectedIndex);
        Assert.False(iso.HasHint);
    }

    [Fact]
    public async Task Text_Not_In_Domain_Is_Not_Applied_And_Shows_Hint()
    {
        var settings = new IniSettingsService(iniPath: TempIni());
        var s = settings.Load();
        s.ExternalCameraEnabled = true;
        s.ExternalIso = "200";
        var external = new FakeExternalCamera { Domain = SampleDomain() };

        var vm = MakeVm(settings, UserRole.Admin, external);
        await vm.OnEnterAsync();

        var iso = vm.ExposureParameters[2];
        int before = iso.SelectedIndex;
        iso.Text = "51200";                               // 근사 매칭 금지 — 인덱스는 움직이지 않는다

        Assert.Equal(before, iso.SelectedIndex);
        Assert.True(iso.HasHint);
        Assert.Equal(ExposureParameterViewModel.UnsupportedValueHint, iso.Hint);
    }

    [Fact]
    public async Task Empty_Text_Is_Unspecified_Without_Hint()
    {
        var settings = new IniSettingsService(iniPath: TempIni());
        var s = settings.Load();
        s.ExternalCameraEnabled = true;
        var external = new FakeExternalCamera { Domain = SampleDomain() };

        var vm = MakeVm(settings, UserRole.Admin, external);
        await vm.OnEnterAsync();

        var shutter = vm.ExposureParameters[0];
        shutter.Text = string.Empty;
        Assert.False(shutter.HasHint);   // 미지정은 정상 상태(카메라 현재값 유지)
    }

    [Fact]
    public async Task Saved_Exposure_Survives_Save_Reload_Cycle()
    {
        // SaveSettings는 저장 후 LoadSettings로 클램프된 값을 되읽는다 —
        // 그 왕복에서 노출 문자열이 사라지면(도메인 갱신이 값을 덮으면) 편집이 불가능해진다.
        var path = TempIni();
        try
        {
            var settings = new IniSettingsService(iniPath: path);
            var s = settings.Load();
            s.ExternalCameraEnabled = true;
            var external = new FakeExternalCamera { Domain = SampleDomain() };

            var vm = MakeVm(settings, UserRole.Admin, external);
            await vm.OnEnterAsync();

            vm.ExposureParameters[0].Text = "1/60";
            vm.SaveSettingsCommand.Execute(null);

            Assert.Equal("1/60", vm.ExposureParameters[0].Text);
            Assert.Equal(0, vm.ExposureParameters[0].SelectedIndex);
            Assert.Equal("1/60", new IniSettingsService(iniPath: path).Load().ExternalShutterSpeed);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task Leaving_Unsubscribes_Domain_Notifications()
    {
        // 구독 해제 경로 회귀 잠금: OnLeaveAsync 이후 도메인 변화가 VM 알림을 일으키지 않는다.
        var settings = new IniSettingsService(iniPath: TempIni());
        var s = settings.Load();
        s.ExternalCameraEnabled = true;
        var vm = MakeVm(settings, UserRole.Admin, new FakeExternalCamera { Domain = SampleDomain() });
        await vm.OnEnterAsync();

        int notified = 0;
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.HasExposureDomain)) notified++; };

        await vm.OnLeaveAsync();
        // 이탈 후 도메인을 비워도(카메라 탈락 모사) 알림이 오지 않아야 한다.
        vm.ExposureParameters[0].SetDomain(null, string.Empty);

        Assert.Equal(0, notified);
    }
}
