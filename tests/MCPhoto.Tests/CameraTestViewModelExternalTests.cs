using System.IO;
using System.Linq;
using MCPhoto.App.Services;
using MCPhoto.App.ViewModels;
using MCPhoto.Core.Capture;
using MCPhoto.Core.Devices;
using MCPhoto.Core.Settings;
using MCPhoto.Tests.Fakes;

namespace MCPhoto.Tests;

/// <summary>
/// it23 Step 9: 카메라 테스트 모달의 장치 목록·외부 테스트 모드 검증(설계 §14.4 T-V6·T-V7).
/// <para>
/// ★ 순서 계약이 핵이다: 외부 항목을 고르면 <b>웹캠을 먼저 반납(StopAsync)한 뒤</b> 연결한다.
/// 순서가 뒤바뀌면 두 장치를 동시에 열려는 시도가 되어(설계 A8 미검증) 실패 원인을 특정할 수 없다.
/// </para>
/// </summary>
public class CameraTestViewModelExternalTests
{
    /// <summary>
    /// 호출 순서를 기록하는 카메라 페이크(Stop→Connect 순서 검증용) + 프레임 펌핑.
    /// <para>
    /// 프레임을 흘려야 웹캠 항목의 Ready 게이트(연속 8프레임 + 500ms)가 닫힌다. 펌핑이 없으면
    /// 매 웹캠 전환마다 8초 타임아웃을 기다려 이 클래스만 수십 초가 된다.
    /// </para>
    /// </summary>
    private sealed class RecordingCameraService : ICameraService
    {
        private readonly List<string> _log;
        private readonly IReadOnlyList<CameraDevice> _devices;
        private CancellationTokenSource? _pumpCts;
        private Task? _pump;

        public RecordingCameraService(List<string> log, params CameraDevice[] devices)
        {
            _log = log;
            _devices = devices.Length > 0 ? devices : new[] { new CameraDevice(0, "Camera 0") };
        }

        /// <summary>false면 프레임이 오지 않아 Ready 게이트가 타임아웃한다(로딩 상태 검증용).</summary>
        public bool PumpFrames { get; set; } = true;

        public event EventHandler<CameraFrame>? FrameReady;

        public double CurrentFps => IsRunning && PumpFrames ? 30 : 0;
        public bool IsRunning { get; private set; }

        public Task<bool> StartAsync(int deviceIndex, double targetAspect, bool mirror, CancellationToken ct = default)
        {
            _log.Add($"start:{deviceIndex}");
            IsRunning = true;
            if (PumpFrames && _pump is null)
            {
                _pumpCts = new CancellationTokenSource();
                _pump = PumpAsync(_pumpCts.Token);
            }
            return Task.FromResult(true);
        }

        private async Task PumpAsync(CancellationToken ct)
        {
            var frame = new CameraFrame { Width = 8, Height = 8, Pixels = new byte[8 * 8 * 3], Stride = 24 };
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    FrameReady?.Invoke(this, frame);
                    await Task.Delay(20, ct);
                }
            }
            catch (OperationCanceledException) { /* 정상 종료 */ }
        }

        public async Task StopAsync()
        {
            _log.Add("stop");
            IsRunning = false;
            _pumpCts?.Cancel();
            if (_pump is not null)
            {
                try { await _pump; } catch { /* 무시 */ }
                _pump = null;
            }
            _pumpCts?.Dispose();
            _pumpCts = null;
        }

        public void SetMirror(bool mirror) { }
        public void SetTargetAspect(double aspect) { }
        public Task<CapturedStill> CaptureStillAsync(CancellationToken ct = default)
        {
            _log.Add("webcamStill");
            return Task.FromResult(new CapturedStill());
        }
        public void StartRecording(string outputPath) { }
        public Task StopRecordingAsync() => Task.CompletedTask;
        public IReadOnlyList<CameraDevice> EnumerateDevices() => _devices;
        public void Dispose() => _pumpCts?.Cancel();
    }

    private static IniSettingsService MakeSettings(Action<AppSettings>? configure = null)
    {
        var svc = new IniSettingsService(
            iniPath: Path.Combine(Path.GetTempPath(), $"ctvm_{Guid.NewGuid():N}.ini"));
        var s = svc.Load();
        configure?.Invoke(s);
        return svc;
    }

    private static ExposureDomain SampleDomain() => new(
        new ExposureDomainEntry(new[] { "1/60", "1/125" }, 0),
        null,
        new ExposureDomainEntry(new[] { "100", "400" }, 1));

    // ── T-V6: 장치 목록 — Enabled=off면 웹캠만, on이면 +외부 1항목 ──

    [Fact]
    public async Task Target_List_Has_Only_Webcams_When_External_Disabled()
    {
        var log = new List<string>();
        var camera = new RecordingCameraService(log, new CameraDevice(0, "Cam A"), new CameraDevice(1, "Cam B"));
        var external = new FakeExternalCamera();
        var vm = new CameraTestViewModel(camera, MakeSettings(), external, CameraTestTarget.Webcam(0));

        await vm.StartAsync();

        Assert.Equal(2, vm.Targets.Count);
        Assert.All(vm.Targets, t => Assert.False(t.IsExternal));
        Assert.False(external.Touched);   // ★ 회귀 0: 목록에도 없고 접촉도 없다
    }

    [Fact]
    public async Task Target_List_Adds_External_Item_When_Enabled()
    {
        var log = new List<string>();
        var camera = new RecordingCameraService(log, new CameraDevice(0, "Cam A"));
        var external = new FakeExternalCamera();
        var settings = MakeSettings(s => s.ExternalCameraEnabled = true);
        var vm = new CameraTestViewModel(camera, settings, external, CameraTestTarget.Webcam(0));

        await vm.StartAsync();

        Assert.Equal(2, vm.Targets.Count);
        var ext = Assert.Single(vm.Targets, t => t.IsExternal);
        Assert.Equal("Nikon D5300 (외부 카메라)", ext.DisplayName);
        // 항목이 있어도 선택하지 않았으므로 연결 시도는 없다(§9.3 trigger).
        Assert.Equal(0, external.ConnectCalls);
    }

    [Fact]
    public async Task Initial_Target_Selects_Requested_Webcam_Index()
    {
        var log = new List<string>();
        var camera = new RecordingCameraService(log, new CameraDevice(0, "Cam A"), new CameraDevice(1, "Cam B"));
        var vm = new CameraTestViewModel(camera, MakeSettings(), new FakeExternalCamera(), CameraTestTarget.Webcam(1));

        await vm.StartAsync();

        Assert.NotNull(vm.SelectedTarget);
        Assert.Equal(1, vm.SelectedTarget!.DeviceIndex);
        Assert.True(vm.IsWebcamSelected);
        Assert.Contains("start:1", log);
    }

    [Fact]
    public async Task External_Initial_Target_Is_Selectable_Without_Webcam()
    {
        // 웹캠 0대 + 외부 카메라만 붙은 부스: 모달이 외부 항목으로 열려야 한다.
        var log = new List<string>();
        var camera = new RecordingCameraService(log, Array.Empty<CameraDevice>());
        var external = new FakeExternalCamera();
        var settings = MakeSettings(s => s.ExternalCameraEnabled = true);
        var vm = new CameraTestViewModel(camera, settings,
            external, CameraTestTarget.External(ExternalCameraModels.Default));

        await vm.StartAsync();

        Assert.True(vm.IsExternalSelected);
        Assert.Equal(1, external.ConnectCalls);
    }

    // ── T-V7: 외부 항목 선택 → 웹캠 StopAsync 후 ConnectAsync 순서 ──

    [Fact]
    public async Task Selecting_External_Stops_Webcam_Before_Connecting()
    {
        var log = new List<string>();
        var camera = new RecordingCameraService(log, new CameraDevice(0, "Cam A"));
        var external = new FakeExternalCamera();
        var settings = MakeSettings(s => s.ExternalCameraEnabled = true);
        var vm = new CameraTestViewModel(camera, settings, external, CameraTestTarget.Webcam(0));
        await vm.StartAsync();

        log.Clear();
        external.OnCapture = null;
        var extTarget = vm.Targets.First(t => t.IsExternal);
        await vm.SelectTargetCommand.ExecuteAsync(extTarget);

        // 순서 계약: stop이 connect보다 먼저 기록돼야 한다.
        Assert.Equal("stop", log.First());
        Assert.Equal(1, external.ConnectCalls);
        Assert.True(vm.IsExternalConnected);
        Assert.Equal("Nikon D5300", vm.ExternalModelName);
    }

    [Fact]
    public async Task External_Panel_Shows_Battery_And_Capability_Lines()
    {
        var log = new List<string>();
        var camera = new RecordingCameraService(log);
        var external = new FakeExternalCamera();   // 스틸·노출·플래시 Supported, 배터리 75
        var settings = MakeSettings(s => s.ExternalCameraEnabled = true);
        var vm = new CameraTestViewModel(camera, settings,
            external, CameraTestTarget.External(ExternalCameraModels.Default));

        await vm.StartAsync();

        Assert.Equal("75%", vm.ExternalBatteryText);
        Assert.Contains("스틸 촬영: 지원", vm.ExternalCapabilityLines);
        // 비목표 항목(LiveView·동영상)도 진단 목적으로 상태가 노출된다.
        Assert.Contains("LiveView: 이 카메라가 지원하지 않는 기능입니다", vm.ExternalCapabilityLines);
        Assert.Equal("외부 카메라 — 카메라 세팅 확인 · 셔터 동작 테스트", vm.PurposeLabel);
    }

    [Fact]
    public async Task External_Connect_Failure_Shows_Reason_And_Allows_Reconnect()
    {
        var log = new List<string>();
        var camera = new RecordingCameraService(log);
        var external = new FakeExternalCamera
        {
            ConnectResult = false,
            Reason = "카메라 모듈 파일이 없습니다 (NikonSdk\\Type0011.md3)",
        };
        var settings = MakeSettings(s => s.ExternalCameraEnabled = true);
        var vm = new CameraTestViewModel(camera, settings,
            external, CameraTestTarget.External(ExternalCameraModels.Default));

        await vm.StartAsync();

        Assert.False(vm.IsExternalConnected);
        Assert.True(vm.HasExternalStatus);
        Assert.Equal("카메라 모듈 파일이 없습니다 (NikonSdk\\Type0011.md3)", vm.ExternalStatus);
        Assert.False(vm.IsLoading);   // 오버레이를 내려 사유·[다시 연결]을 볼 수 있어야 한다

        external.ConnectResult = true;
        await vm.ReconnectExternalCommand.ExecuteAsync(null);
        Assert.True(vm.IsExternalConnected);
        Assert.Equal(2, external.ConnectCalls);
    }

    [Fact]
    public async Task Switching_External_To_Webcam_Does_Not_Disconnect()
    {
        // 재연결 비용 회피(§9.3): 전환은 끊지 않는다. 끊는 것은 닫을 때뿐이다.
        var log = new List<string>();
        var camera = new RecordingCameraService(log, new CameraDevice(0, "Cam A"));
        var external = new FakeExternalCamera();
        var settings = MakeSettings(s => s.ExternalCameraEnabled = true);
        var vm = new CameraTestViewModel(camera, settings,
            external, CameraTestTarget.External(ExternalCameraModels.Default));
        await vm.StartAsync();
        Assert.True(vm.IsExternalConnected);

        await vm.SelectTargetCommand.ExecuteAsync(vm.Targets.First(t => !t.IsExternal));

        Assert.Equal(0, external.DisconnectCalls);
        Assert.True(vm.IsWebcamSelected);
    }

    [Fact]
    public async Task Device_Lost_While_Modal_Open_Updates_Status()
    {
        // §11 E5: 모달이 열려 있는 동안 USB가 뽑히면 "연결됨"이 그대로 남아 셔터 테스트가 무한 실패한다.
        var log = new List<string>();
        var camera = new RecordingCameraService(log);
        var external = new FakeExternalCamera();
        var settings = MakeSettings(s => s.ExternalCameraEnabled = true);
        var vm = new CameraTestViewModel(camera, settings,
            external, CameraTestTarget.External(ExternalCameraModels.Default));
        await vm.StartAsync();
        Assert.True(vm.IsExternalConnected);

        external.RaiseConnectionChanged(false, "USB 연결이 끊겼습니다");

        Assert.False(vm.IsExternalConnected);
        Assert.Equal("USB 연결이 끊겼습니다", vm.ExternalStatus);
        Assert.Empty(vm.ExternalCapabilityLines);
    }

    [Fact]
    public async Task Closing_Unsubscribes_Connection_Changed()
    {
        // 구독 해제 회귀 잠금: 외부 카메라는 Singleton이라 해제하지 않으면 닫힌 모달의 VM이 계속 붙잡힌다.
        var log = new List<string>();
        var camera = new RecordingCameraService(log);
        var external = new FakeExternalCamera();
        var settings = MakeSettings(s => s.ExternalCameraEnabled = true);
        var vm = new CameraTestViewModel(camera, settings,
            external, CameraTestTarget.External(ExternalCameraModels.Default));
        await vm.StartAsync();

        await vm.StopAsync();
        external.RaiseConnectionChanged(false, "닫힌 뒤 통지");

        Assert.NotEqual("닫힌 뒤 통지", vm.ExternalStatus);
    }

    [Fact]
    public async Task Closing_Disconnects_External_And_Stops_Webcam()
    {
        var log = new List<string>();
        var camera = new RecordingCameraService(log);
        var external = new FakeExternalCamera();
        var settings = MakeSettings(s => s.ExternalCameraEnabled = true);
        var vm = new CameraTestViewModel(camera, settings,
            external, CameraTestTarget.External(ExternalCameraModels.Default));
        await vm.StartAsync();

        await vm.StopAsync();

        Assert.Equal(1, external.DisconnectCalls);
        Assert.Contains("stop", log);
    }

    // ── 셔터 테스트(§9.3) ──

    [Fact]
    public async Task External_Shutter_Test_Shows_Raw_Bytes_Then_Discards()
    {
        var log = new List<string>();
        var camera = new RecordingCameraService(log);
        var external = new FakeExternalCamera { CaptureResult = new byte[] { 1, 2, 3, 4 } };
        var settings = MakeSettings(s => s.ExternalCameraEnabled = true);
        var vm = new CameraTestViewModel(camera, settings,
            external, CameraTestTarget.External(ExternalCameraModels.Default));
        await vm.StartAsync();

        await vm.ShootTestCommand.ExecuteAsync(null);

        Assert.Equal(1, external.CaptureCalls);
        Assert.Equal(0, log.Count(l => l == "webcamStill"));   // 웹캠 스틸을 쓰지 않는다
        // 3초 노출 후 폐기 — 저장하지 않는다(현행 모달 원칙).
        Assert.Null(vm.ShotImageBytes);
        Assert.False(vm.HasShotImage);
        Assert.Equal(string.Empty, vm.ShotNotice);
    }

    [Fact]
    public async Task External_Shutter_Test_Reproduces_Physical_Flash_When_Flash_On()
    {
        var log = new List<string>();
        var camera = new RecordingCameraService(log);
        var external = new FakeExternalCamera { CaptureResult = new byte[] { 9 } };
        var settings = MakeSettings(s =>
        {
            s.ExternalCameraEnabled = true;
            s.FlashMode = true;
        });
        var vm = new CameraTestViewModel(camera, settings,
            external, CameraTestTarget.External(ExternalCameraModels.Default));
        await vm.StartAsync();

        await vm.ShootTestCommand.ExecuteAsync(null);

        Assert.Equal(1, external.PhysicalFlashCalls);   // 실촬영과 동일 경로 재현(§4.3)
        Assert.False(vm.FlashActive);                   // 화면 플래시는 반드시 꺼진다
    }

    [Fact]
    public async Task External_Shutter_Failure_Shows_Notice_And_No_Image()
    {
        var log = new List<string>();
        var camera = new RecordingCameraService(log);
        var external = new FakeExternalCamera { CaptureResult = null };
        var settings = MakeSettings(s => s.ExternalCameraEnabled = true);
        var vm = new CameraTestViewModel(camera, settings,
            external, CameraTestTarget.External(ExternalCameraModels.Default));
        await vm.StartAsync();

        await vm.ShootTestCommand.ExecuteAsync(null);

        Assert.Null(vm.ShotImageBytes);
        Assert.False(vm.FlashActive);
        Assert.Equal(string.Empty, vm.ShotNotice);   // 안내 후 정리된다(고착 금지)
    }

    [Fact]
    public async Task Webcam_Shutter_Test_Keeps_Current_Behaviour()
    {
        // 회귀 0: 웹캠 항목의 셔터 테스트는 종전 그대로(웹캠 스틸 1회, 외부 카메라 무접촉).
        var log = new List<string>();
        var camera = new RecordingCameraService(log, new CameraDevice(0, "Cam A"));
        var external = new FakeExternalCamera();
        var vm = new CameraTestViewModel(camera, MakeSettings(), external, CameraTestTarget.Webcam(0));
        await vm.StartAsync();
        Assert.False(vm.IsLoading);   // 프리뷰 Ready

        await vm.ShootTestCommand.ExecuteAsync(null);

        Assert.Equal(1, log.Count(l => l == "webcamStill"));
        Assert.False(external.Touched);   // ★ 외부 카메라 무접촉
        Assert.Null(vm.ShotImageBytes);   // 웹캠 경로는 결과 이미지를 남기지 않는다(현행 그대로)
    }

    [Fact]
    public async Task Webcam_Without_Frames_Stays_In_Loading_Overlay()
    {
        // 현행 동작 유지: 프레임이 오지 않으면 Ready 게이트에서 타임아웃하고 오버레이에 사유가 남는다.
        var log = new List<string>();
        var camera = new RecordingCameraService(log, new CameraDevice(0, "Cam A")) { PumpFrames = false };
        var vm = new CameraTestViewModel(camera, MakeSettings(), new FakeExternalCamera(), CameraTestTarget.Webcam(0));

        await vm.StartAsync();

        Assert.True(vm.IsLoading);
        Assert.Equal("카메라 준비에 실패했습니다.", vm.LoadingMessage);
    }

    // ── 노출 조정(§9.3) ──

    [Fact]
    public async Task External_Exposure_Domain_Is_Loaded_On_Connect()
    {
        var log = new List<string>();
        var camera = new RecordingCameraService(log);
        var external = new FakeExternalCamera { Domain = SampleDomain() };
        var settings = MakeSettings(s =>
        {
            s.ExternalCameraEnabled = true;
            s.ExternalIso = "400";
        });
        var vm = new CameraTestViewModel(camera, settings,
            external, CameraTestTarget.External(ExternalCameraModels.Default));

        await vm.StartAsync();

        Assert.True(vm.ExposureParameters[0].IsDomainAvailable);
        Assert.False(vm.ExposureParameters[1].IsDomainAvailable);   // 조리개는 미지원(모드에 따라 잠김)
        Assert.Equal(1, vm.ExposureParameters[2].SelectedIndex);    // 저장값 "400"의 인덱스
    }

    [Fact]
    public async Task Apply_Exposure_Writes_Only_Specified_Values()
    {
        var log = new List<string>();
        var camera = new RecordingCameraService(log);
        var external = new FakeExternalCamera { Domain = SampleDomain() };
        var settings = MakeSettings(s =>
        {
            s.ExternalCameraEnabled = true;
            s.ExternalShutterSpeed = "1/125";
            // 조리개·ISO는 미지정 → 왕복하지 않는다
        });
        var vm = new CameraTestViewModel(camera, settings,
            external, CameraTestTarget.External(ExternalCameraModels.Default));
        await vm.StartAsync();

        await vm.ApplyExposureCommand.ExecuteAsync(null);

        Assert.Single(external.ExposureWrites);
        Assert.Equal((ExposureParameter.ShutterSpeed, "1/125"), external.ExposureWrites[0]);
    }

    [Fact]
    public async Task Apply_Exposure_Failure_Shows_Row_Hint_And_Continues()
    {
        var log = new List<string>();
        var camera = new RecordingCameraService(log);
        var external = new FakeExternalCamera { Domain = SampleDomain(), SetExposureResult = false };
        var settings = MakeSettings(s =>
        {
            s.ExternalCameraEnabled = true;
            s.ExternalShutterSpeed = "1/125";
            s.ExternalIso = "100";
        });
        var vm = new CameraTestViewModel(camera, settings,
            external, CameraTestTarget.External(ExternalCameraModels.Default));
        await vm.StartAsync();

        await vm.ApplyExposureCommand.ExecuteAsync(null);

        // 실패해도 예외 없이 두 행 모두 시도되고, 힌트로만 알린다(테스트는 계속된다).
        Assert.Equal(2, external.ExposureWrites.Count);
        Assert.True(vm.ExposureParameters[0].HasHint || vm.ExposureParameters[2].HasHint);
    }

    [Fact]
    public async Task Apply_Exposure_Is_NoOp_When_Not_Connected()
    {
        var log = new List<string>();
        var camera = new RecordingCameraService(log);
        var external = new FakeExternalCamera { ConnectResult = false, Reason = "미연결" };
        var settings = MakeSettings(s =>
        {
            s.ExternalCameraEnabled = true;
            s.ExternalIso = "400";
        });
        var vm = new CameraTestViewModel(camera, settings,
            external, CameraTestTarget.External(ExternalCameraModels.Default));
        await vm.StartAsync();

        await vm.ApplyExposureCommand.ExecuteAsync(null);

        Assert.Empty(external.ExposureWrites);
    }

    // ── CameraTestTarget 값 계약 ──

    [Fact]
    public void Webcam_Target_Falls_Back_To_Index_Label()
    {
        Assert.Equal("카메라 3", CameraTestTarget.Webcam(3).DisplayName);
        Assert.Equal("Cam A", CameraTestTarget.Webcam(new CameraDevice(0, "Cam A")).DisplayName);
        Assert.Equal("카메라 0", CameraTestTarget.Webcam(0, "  ").DisplayName);
    }

    [Fact]
    public void External_Target_Marks_Itself_And_Has_No_Device_Index()
    {
        var t = CameraTestTarget.External(ExternalCameraModels.Default);
        Assert.True(t.IsExternal);
        Assert.Equal(-1, t.DeviceIndex);
        Assert.Equal(t.DisplayName, t.ToString());   // 닫힌 콤보 폴백
    }
}
