using System.IO;
using MCPhoto.App;
using MCPhoto.App.ViewModels;
using MCPhoto.Capture;
using MCPhoto.Core.Capture;
using MCPhoto.Core.Devices;
using MCPhoto.Core.Models;
using MCPhoto.Core.Navigation;
using MCPhoto.Core.Settings;
using MCPhoto.Tests.Fakes;
using OpenCvSharp;

namespace MCPhoto.Tests;

/// <summary>
/// it23 Step 7: 촬영 세션의 외부 카메라 배선 검증(설계 §14.3 T-C1~T-C7·T-F6).
/// <para>
/// ★ 최상위 계약은 T-C1이다: <c>ExternalCameraEnabled=false</c>(기본값)에서 촬영 흐름이
/// 외부 카메라를 <b>단 한 번도 만지지 않는다</b>. 이 테스트가 회귀 0의 기계적 증거다.
/// </para>
/// </summary>
public class CaptureViewModelExternalTests : IDisposable
{
    /// <summary>
    /// 테스트가 만든 세션들. <c>OnEnterAsync</c>가 실제 데이터 폴더(<c>App.DataFolder\sessions\</c>)에
    /// 작업 폴더를 만들기 때문에, 프로덕션의 시작 시 잔재 정리에 맡기지 않고 여기서 직접 지운다.
    /// </summary>
    private readonly List<SessionContext> _sessions = new();

    public void Dispose()
    {
        foreach (var s in _sessions)
        {
            try
            {
                if (s.WorkFolder is { } folder && Directory.Exists(folder))
                    Directory.Delete(folder, recursive: true);
            }
            catch { /* 무시 */ }
        }
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    /// <summary>1슬롯 3:4 프레임(대표 슬롯 종횡비 = 0.75).</summary>
    private static FrameTemplate MakeFrame()
    {
        var f = new FrameTemplate { Id = "f1", Name = "test" };
        f.Slots.Add(new Slot { Index = 0, X = 0, Y = 0, Width = 300, Height = 400 });
        return f;
    }

    /// <summary>DSLR이 보내오는 JPEG 바이트(1200×800 — 축소 없이 크롭만 걸린다).</summary>
    private static byte[] MakeJpeg()
    {
        using var mat = new Mat(800, 1200, MatType.CV_8UC3, new Scalar(40, 80, 160));
        Cv2.ImEncode(".jpg", mat, out var buf);
        return buf;
    }

    /// <summary>
    /// 촬영 VM + 셸 조립. 카운트다운은 0초로 둬 테스트가 초 단위로 늘어지지 않게 한다
    /// (ini에 저장하지 않고 메모리 값만 바꾼다 — Clamp가 3으로 올려버리기 때문).
    /// </summary>
    private (CaptureViewModel vm, AppShellViewModel shell, SessionContext session) MakeVm(
        PumpingCameraService camera,
        FakeExternalCamera external,
        Action<AppSettings>? configure = null,
        int cutCount = 2)
    {
        var settings = new IniSettingsService(
            iniPath: Path.Combine(Path.GetTempPath(), $"cvm_{Guid.NewGuid():N}.ini"));
        var s = settings.Load();
        s.CountdownSec = 0;
        configure?.Invoke(s);

        var session = new SessionContext();
        var frame = MakeFrame();
        session.SelectedFrame = frame;
        session.Capture.Begin(frame, cutCount);

        var shell = new AppShellViewModel(new IdleWatchdog(), settings, new EmptyServiceProvider(), session);
        var vm = new CaptureViewModel(shell, camera, external, new ExternalStillDecoder());
        _sessions.Add(session);
        return (vm, shell, session);
    }

    /// <summary>컷이 기대 수만큼 모이거나 세션이 끝날 때까지 대기(시퀀스가 fire-and-forget이라 폴링).</summary>
    private static async Task<bool> WaitAsync(Func<bool> condition, int timeoutMs = 8000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (condition()) return true;
            await Task.Delay(20);
        }
        return condition();
    }

    // ── T-C1: Enabled=off → 외부 카메라 무접촉(회귀 0) ──

    [Fact]
    public async Task Disabled_Never_Touches_External_Camera()
    {
        var camera = new PumpingCameraService();
        var external = new FakeExternalCamera();
        var (vm, _, session) = MakeVm(camera, external);   // ExternalCameraEnabled 기본 false

        await vm.OnEnterAsync();
        Assert.True(await WaitAsync(() => session.Capture.Cuts.Count == 2));
        await vm.OnLeaveAsync();

        Assert.False(external.Touched);          // ★ 회귀 0: 단 한 번도 접촉하지 않는다
        Assert.False(vm.IsExternalSource);
        Assert.False(vm.PreviewAbsent);
        Assert.False(vm.HasDegradeBanner);
        Assert.Equal(CameraLoadState.Ready, vm.CameraState);
        Assert.Equal(2, camera.StillCalls);      // 전 컷 웹캠 스틸
        Assert.Equal(1, camera.StartRecordingCalls);
    }

    [Fact]
    public async Task Disabled_With_No_Webcam_Keeps_Current_Failed_Path()
    {
        // 현행 동작 그대로: 웹캠 없으면 Failed + 안내 문구, 시퀀스 미시작.
        var camera = new PumpingCameraService { StartResult = false };
        var external = new FakeExternalCamera();
        var (vm, _, session) = MakeVm(camera, external);

        await vm.OnEnterAsync();

        Assert.False(external.Touched);
        Assert.Equal(CameraLoadState.Failed, vm.CameraState);
        Assert.Equal("카메라를 찾을 수 없습니다.", vm.StatusMessage);
        Assert.Empty(session.Capture.Cuts);
    }

    // ── T-C2: Enabled=on + 연결 성공 → 전 컷 External ──

    [Fact]
    public async Task Enabled_And_Connected_Captures_All_Cuts_From_External()
    {
        var camera = new PumpingCameraService();
        var external = new FakeExternalCamera { CaptureResult = MakeJpeg() };
        var (vm, _, session) = MakeVm(camera, external, s => s.ExternalCameraEnabled = true);

        await vm.OnEnterAsync();
        Assert.True(vm.IsExternalSource);
        Assert.True(await WaitAsync(() => session.Capture.Cuts.Count == 2));
        await vm.OnLeaveAsync();

        Assert.Equal(1, external.ConnectCalls);
        Assert.Equal(2, external.CaptureCalls);
        Assert.Equal(0, camera.StillCalls);          // 웹캠 스틸은 쓰지 않는다
        Assert.Equal(1, camera.StartRecordingCalls); // 타임랩스는 웹캠 전담 — 녹화는 계속한다
        Assert.False(vm.HasDegradeBanner);

        // 수신 스틸이 웹캠과 같은 규칙으로 정규화됐는지(슬롯 3:4 크롭) — WYSIWYG 계약.
        var expected = CropCalculator.CenterCrop(1200, 800, 0.75);
        foreach (var cut in session.Capture.Cuts)
        {
            Assert.Equal(expected.Width, cut.Width);
            Assert.Equal(expected.Height, cut.Height);
        }
    }

    [Fact]
    public async Task Receiving_Overlay_Is_On_While_Waiting_And_Off_After()
    {
        var camera = new PumpingCameraService();
        bool? receivingDuringCapture = null;
        var external = new FakeExternalCamera { CaptureResult = MakeJpeg() };
        var (vm, _, session) = MakeVm(camera, external, s => s.ExternalCameraEnabled = true, cutCount: 1);
        external.OnCapture = () => receivingDuringCapture ??= vm.IsReceiving;

        await vm.OnEnterAsync();
        Assert.True(await WaitAsync(() => session.Capture.Cuts.Count == 1));
        await vm.OnLeaveAsync();

        Assert.True(receivingDuringCapture);   // W5 오버레이가 수신 중에 켜져 있다
        Assert.False(vm.IsReceiving);          // 끝나면 반드시 내려간다(영구 고착 방지)
    }

    // ── T-C3: Enabled=on + 연결 실패 → 웹캠 강등 + W7 통지 ──

    [Fact]
    public async Task Enabled_But_Connect_Fails_Degrades_To_Webcam_With_Toast()
    {
        var camera = new PumpingCameraService();
        var external = new FakeExternalCamera
        {
            ConnectResult = false,
            Reason = "카메라가 연결되지 않았습니다 (USB·전원 확인)",
        };
        var (vm, shell, session) = MakeVm(camera, external, s => s.ExternalCameraEnabled = true);

        await vm.OnEnterAsync();
        Assert.True(await WaitAsync(() => session.Capture.Cuts.Count == 2));
        await vm.OnLeaveAsync();

        Assert.False(vm.IsExternalSource);
        Assert.Equal(2, camera.StillCalls);      // 웹캠 단독으로 완주
        Assert.Equal(0, external.CaptureCalls);
        Assert.Equal(
            "외부 카메라를 사용할 수 없어 웹캠으로 촬영합니다 (카메라가 연결되지 않았습니다 (USB·전원 확인))",
            shell.ToastMessage);
    }

    [Fact]
    public async Task Enabled_But_Still_Capture_Unsupported_Degrades_With_Capability_Reason()
    {
        // 연결은 되지만 스틸 캡처 capability가 미지원 — 게이트가 닫히고 사유 문구가 달라진다(§4.2).
        var camera = new PumpingCameraService();
        var external = new FakeExternalCamera
        {
            Capabilities = new ExternalCameraCapabilities(
                CapabilityState.Unsupported, CapabilityState.Supported, CapabilityState.Supported,
                CapabilityState.Unsupported, CapabilityState.Unsupported, null),
        };
        var (vm, shell, session) = MakeVm(camera, external, s => s.ExternalCameraEnabled = true);

        await vm.OnEnterAsync();
        Assert.True(await WaitAsync(() => session.Capture.Cuts.Count == 2));
        await vm.OnLeaveAsync();

        Assert.False(vm.IsExternalSource);
        Assert.Equal(2, camera.StillCalls);
        Assert.Contains("이 카메라가 지원하지 않는 기능입니다", shell.ToastMessage);
    }

    [Fact]
    public async Task Probe_Failure_Closes_Gate_With_Unknown_Reason()
    {
        var camera = new PumpingCameraService();
        var external = new FakeExternalCamera { Capabilities = ExternalCameraCapabilities.AllUnknown };
        var (vm, shell, session) = MakeVm(camera, external, s => s.ExternalCameraEnabled = true);

        await vm.OnEnterAsync();
        Assert.True(await WaitAsync(() => session.Capture.Cuts.Count == 2));
        await vm.OnLeaveAsync();

        Assert.False(vm.IsExternalSource);
        Assert.Contains("기능 지원 여부를 확인하지 못했습니다", shell.ToastMessage);
    }

    // ── T-C4: 컷 중간 실패 → 재시도 → 웹캠 강등(앞 컷 유지) ──

    [Fact]
    public async Task Cut_Failure_Retries_Once_Then_Degrades_And_Keeps_Earlier_Cuts()
    {
        var camera = new PumpingCameraService();
        var external = new FakeExternalCamera { CaptureResult = MakeJpeg() };
        var (vm, _, session) = MakeVm(camera, external, s => s.ExternalCameraEnabled = true, cutCount: 3);

        await vm.OnEnterAsync();
        // 컷1이 성공한 뒤 컷2부터 실패시킨다(재시도 포함 2회 실패 → 강등).
        Assert.True(await WaitAsync(() => session.Capture.Cuts.Count == 1));
        external.FailFirstCaptures = 2;

        Assert.True(await WaitAsync(() => session.Capture.Cuts.Count == 3));
        await vm.OnLeaveAsync();

        Assert.Equal(3, session.Capture.Cuts.Count);   // 앞 컷을 폐기하지 않는다
        Assert.False(vm.IsExternalSource);
        Assert.True(vm.HasDegradeBanner);
        Assert.Equal("외부 카메라 연결이 끊겨 웹캠으로 촬영합니다", vm.DegradeBanner);
        Assert.True(external.ConnectCalls >= 2);       // 재시도에 재연결이 포함된다
        Assert.Equal(2, camera.StillCalls);            // 강등된 컷2·3은 웹캠
    }

    [Fact]
    public async Task Degraded_Session_Does_Not_Retry_External_Per_Cut()
    {
        // 강등은 "이 컷부터 끝까지"다 — 컷마다 재시도하면 손님이 타임아웃을 반복 대기한다.
        var camera = new PumpingCameraService();
        var external = new FakeExternalCamera { CaptureResult = MakeJpeg(), FailFirstCaptures = 100 };
        var (vm, _, session) = MakeVm(camera, external, s => s.ExternalCameraEnabled = true, cutCount: 4);

        await vm.OnEnterAsync();
        Assert.True(await WaitAsync(() => session.Capture.Cuts.Count == 4));
        await vm.OnLeaveAsync();

        // 컷1에서 2회(최초+재시도) 실패한 뒤로는 외부 카메라를 다시 부르지 않는다.
        Assert.Equal(2, external.CaptureCalls);
        Assert.Equal(4, camera.StillCalls);
        Assert.True(vm.HasDegradeBanner);
    }

    // ── T-C5: 캡처 실패 + 웹캠 부재 → 세션 중단(E7) ──

    [Fact]
    public async Task Cut_Failure_Without_Webcam_Aborts_Session()
    {
        var camera = new PumpingCameraService { StartResult = false };   // 웹캠 없음
        var external = new FakeExternalCamera { FailFirstCaptures = 100 };
        var (vm, shell, session) = MakeVm(camera, external, s => s.ExternalCameraEnabled = true);

        await vm.OnEnterAsync();
        Assert.True(await WaitAsync(() => shell.ToastMessage.Length > 0));
        await vm.OnLeaveAsync();

        Assert.Equal("촬영을 계속할 수 없습니다 — 카메라를 확인해 주세요", shell.ToastMessage);
        Assert.Empty(session.Capture.Cuts);   // 완성 불가 세션을 컷선택으로 보내지 않는다
        Assert.False(vm.IsReceiving);         // 오버레이가 고착되지 않는다
    }

    // ── T-C6: 웹캠 부재 + External 정상 → PreviewAbsent, 녹화 미시작, 완주 ──

    [Fact]
    public async Task External_Without_Webcam_Completes_Without_Recording()
    {
        var camera = new PumpingCameraService { StartResult = false };
        var external = new FakeExternalCamera { CaptureResult = MakeJpeg() };
        var (vm, _, session) = MakeVm(camera, external, s => s.ExternalCameraEnabled = true);

        await vm.OnEnterAsync();
        Assert.True(vm.PreviewAbsent);
        Assert.Equal(CameraLoadState.Ready, vm.CameraState);   // 촬영 가능 상태(Failed 아님)
        Assert.True(await WaitAsync(() => session.Capture.Cuts.Count == 2));
        await vm.OnLeaveAsync();

        Assert.Equal(0, camera.StartRecordingCalls);
        Assert.Null(session.SessionVideoPath);   // 존재하지 않는 경로를 남기지 않는다(타임랩스 시도 방지)
        Assert.Equal(2, session.Capture.Cuts.Count);
    }

    [Fact]
    public async Task External_With_Stalled_Webcam_Still_Shoots()
    {
        // 웹캠은 열렸지만 프레임이 오지 않는다 → 프리뷰만 없고 촬영은 DSLR이 한다.
        var camera = new PumpingCameraService { PumpFrames = false };
        var external = new FakeExternalCamera { CaptureResult = MakeJpeg() };
        var (vm, _, session) = MakeVm(camera, external, s => s.ExternalCameraEnabled = true, cutCount: 1);

        await vm.OnEnterAsync();
        Assert.True(vm.PreviewAbsent);
        Assert.Equal(CameraLoadState.Ready, vm.CameraState);
        Assert.True(await WaitAsync(() => session.Capture.Cuts.Count == 1));
        await vm.OnLeaveAsync();

        Assert.Single(session.Capture.Cuts);
    }

    // ── T-C7: 수신 대기 중 이탈 → 취소 전파, 후속 AddCut 없음 ──

    [Fact]
    public async Task Leaving_While_Receiving_Cancels_And_Adds_No_More_Cuts()
    {
        var camera = new PumpingCameraService();
        var external = new FakeExternalCamera
        {
            CaptureResult = MakeJpeg(),
            CaptureDelay = TimeSpan.FromSeconds(3),   // 수신 중에 이탈한다
        };
        var (vm, _, session) = MakeVm(camera, external, s => s.ExternalCameraEnabled = true, cutCount: 3);

        await vm.OnEnterAsync();
        Assert.True(await WaitAsync(() => vm.IsReceiving));

        await vm.OnLeaveAsync();
        var atLeave = session.Capture.Cuts.Count;

        await Task.Delay(400);   // 취소가 전파되지 않았다면 이 사이에 컷이 늘어난다
        Assert.Equal(atLeave, session.Capture.Cuts.Count);
        Assert.False(vm.IsReceiving);
    }

    // ── T-F6: 물리 플래시 이중 발광 게이트(§4.3) ──

    [Fact]
    public async Task Flash_On_With_Supported_Physical_Flash_Calls_TrySet_Once_Per_Cut()
    {
        var camera = new PumpingCameraService();
        var external = new FakeExternalCamera { CaptureResult = MakeJpeg() };
        var (vm, _, session) = MakeVm(camera, external, s =>
        {
            s.ExternalCameraEnabled = true;
            s.FlashMode = true;
        }, cutCount: 2);

        await vm.OnEnterAsync();
        Assert.True(await WaitAsync(() => session.Capture.Cuts.Count == 2));
        await vm.OnLeaveAsync();

        Assert.Equal(2, external.PhysicalFlashCalls);          // 컷당 1회, 셔터 직전
        Assert.Equal(new[] { true, true }, external.FlashValues);
    }

    [Fact]
    public async Task Flash_On_With_Unsupported_Physical_Flash_Never_Calls_TrySet()
    {
        var camera = new PumpingCameraService();
        var external = new FakeExternalCamera
        {
            CaptureResult = MakeJpeg(),
            Capabilities = new ExternalCameraCapabilities(
                CapabilityState.Supported, CapabilityState.Supported, CapabilityState.Unsupported,
                CapabilityState.Unsupported, CapabilityState.Unsupported, null),
        };
        var (vm, _, session) = MakeVm(camera, external, s =>
        {
            s.ExternalCameraEnabled = true;
            s.FlashMode = true;
        }, cutCount: 2);

        await vm.OnEnterAsync();
        Assert.True(await WaitAsync(() => session.Capture.Cuts.Count == 2));
        await vm.OnLeaveAsync();

        Assert.Equal(0, external.PhysicalFlashCalls);   // 화면 플래시가 유일 활성 경로
    }

    [Fact]
    public async Task Flash_Off_Never_Calls_Physical_Flash()
    {
        var camera = new PumpingCameraService();
        var external = new FakeExternalCamera { CaptureResult = MakeJpeg() };
        var (vm, _, session) = MakeVm(camera, external, s => s.ExternalCameraEnabled = true, cutCount: 1);

        await vm.OnEnterAsync();
        Assert.True(await WaitAsync(() => session.Capture.Cuts.Count == 1));
        await vm.OnLeaveAsync();

        Assert.Equal(0, external.PhysicalFlashCalls);
    }

    // ── E11: 수신 바이트 디코드 실패 → 컷 실패로 편입 ──

    [Fact]
    public async Task Corrupt_Received_Bytes_Are_Treated_As_Cut_Failure()
    {
        var camera = new PumpingCameraService();
        var garbage = new byte[256];
        new Random(7).NextBytes(garbage);
        var external = new FakeExternalCamera { CaptureResult = garbage };
        var (vm, _, session) = MakeVm(camera, external, s => s.ExternalCameraEnabled = true, cutCount: 2);

        await vm.OnEnterAsync();
        Assert.True(await WaitAsync(() => session.Capture.Cuts.Count == 2));
        await vm.OnLeaveAsync();

        // 디코드 실패 → 재시도 → 강등. 세션은 웹캠으로 완주한다(크래시·중단 없음).
        Assert.True(vm.HasDegradeBanner);
        Assert.Equal(2, camera.StillCalls);
    }
}
