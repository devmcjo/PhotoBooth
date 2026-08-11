using System.IO;
using MCPhoto.Core.Devices;
using MCPhoto.Core.Settings;
using MCPhoto.Devices.Nikon;
using MCPhoto.Tests.Fakes;

namespace MCPhoto.Tests;

/// <summary>
/// it23 Step 5: Nikon 어댑터 오케스트레이션 검증(설계 §14.2 T-A1~T-A8).
/// <para>
/// 실물 SDK·카메라 없이 검증되는 것: "SDK가 계약대로 응답하면 어댑터가 설계대로 동작하고,
/// 응답하지 않으면 설계대로 강등된다". 실기 동작 자체는 증명되지 않는다(설계 §14.5 — 정직 목록).
/// </para>
/// </summary>
public class NikonExternalCameraTests : IDisposable
{
    private readonly string _root;

    public NikonExternalCameraTests()
    {
        // 실행 폴더를 흉내내는 임시 루트. md3 존재/부재 두 경로를 실물 SDK 없이 만든다.
        _root = Path.Combine(Path.GetTempPath(), $"mcphoto_nikon_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch { /* 무시 */ }
    }

    /// <summary>임시 루트에 md3 더미 파일을 만들어 프로브를 통과시킨다(내용은 무의미 — 존재만 검사한다).</summary>
    private string CreateMd3()
    {
        var folder = Path.Combine(_root, SdkRuntimeProbe.SdkFolderName);
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, ExternalCameraModels.Default.Md3FileName);
        File.WriteAllBytes(path, new byte[] { 0x4D, 0x5A });
        return path;
    }

    private ISettingsService MakeSettings(Action<AppSettings>? configure = null)
    {
        var svc = new IniSettingsService(iniPath: Path.Combine(_root, "MCPhoto.ini"));
        var s = svc.Load();
        configure?.Invoke(s);
        return svc;
    }

    private NikonExternalCamera MakeCamera(FakeNikonSdkShim shim, ISettingsService? settings = null)
        => new(shim, settings ?? MakeSettings(), logger: null, probe: new SdkRuntimeProbe(_root));

    // ── T-A1: md3 부재 → shim 미호출 강등 ──

    [Fact]
    public async Task Connect_Without_Md3_File_Fails_Without_Calling_Shim()
    {
        var shim = new FakeNikonSdkShim();
        await using var cam = MakeCamera(shim);

        Assert.False(await cam.ConnectAsync());

        Assert.Equal(0, shim.OpenCalls);          // ★ 프로브 선행 — shim을 아예 부르지 않는다
        Assert.False(cam.IsAvailable);
        Assert.Equal(@"카메라 모듈 파일이 없습니다 (NikonSdk\Type0011.md3)", cam.UnavailableReason);
        Assert.Null(cam.ModelName);               // 미연결이면 모델명도 노출하지 않는다
    }

    [Fact]
    public async Task Connect_Passes_Absolute_Md3_Path_To_Shim()
    {
        var expected = CreateMd3();
        var shim = new FakeNikonSdkShim();
        await using var cam = MakeCamera(shim);

        Assert.True(await cam.ConnectAsync());
        Assert.Equal(expected, shim.LastMd3Path);   // 경로 규약은 호출측이 결정(shim은 배치를 모른다)
    }

    // ── T-A2: OpenAsync 실패 사유 전파 ──

    [Fact]
    public async Task Open_Failure_Propagates_Reason_And_Marks_Unavailable()
    {
        CreateMd3();
        var shim = new FakeNikonSdkShim { OpenResult = false, OpenReason = NikonCameraReasons.NotConnected };
        await using var cam = MakeCamera(shim);

        Assert.False(await cam.ConnectAsync());

        Assert.Equal(1, shim.OpenCalls);
        Assert.False(cam.IsAvailable);
        Assert.Equal(NikonCameraReasons.NotConnected, cam.UnavailableReason);
    }

    [Fact]
    public async Task Missing_Sdk_Shim_Always_Degrades_With_Reason()
    {
        // 현 프로덕션 기본 구현(§11 E1)의 계약: 파일이 있어도 "SDK 모듈 미설치"로 강등된다.
        CreateMd3();
        await using var cam = new NikonExternalCamera(
            new MissingNikonSdkShim(), MakeSettings(), logger: null, probe: new SdkRuntimeProbe(_root));

        Assert.False(await cam.ConnectAsync());
        Assert.False(cam.IsAvailable);
        Assert.Equal(NikonCameraReasons.SdkMissing, cam.UnavailableReason);
        Assert.Null(await cam.GetCapabilitiesAsync());
        Assert.Null(await cam.CaptureAsync());
    }

    [Fact]
    public async Task Open_Throwing_Is_Swallowed_As_Degradation()
    {
        CreateMd3();
        var shim = new FakeNikonSdkShim { OpenThrows = new InvalidOperationException("네이티브 로드 실패") };
        await using var cam = MakeCamera(shim);

        // 예외가 호출측으로 새어 나가면 촬영 진입이 크래시한다.
        Assert.False(await cam.ConnectAsync());
        Assert.False(cam.IsAvailable);
        Assert.Equal(NikonCameraReasons.SdkMissing, cam.UnavailableReason);
    }

    // ── T-A3: 수신 타임아웃 → null(예외 없음) + 토큰 취소 전파 ──

    [Fact]
    public async Task Capture_Timeout_Returns_Null_And_Cancels_Shim_Token()
    {
        CreateMd3();
        // CaptureTimeout(10s)보다 훨씬 긴 지연 — 토큰이 취소되면 Task.Delay가 즉시 끊긴다.
        var shim = new FakeNikonSdkShim { CaptureDelay = TimeSpan.FromMinutes(5) };
        await using var cam = MakeCamera(shim);
        Assert.True(await cam.ConnectAsync());

        // 타임아웃 상수를 기다리지 않도록 호출측 토큰으로 앞당겨 검증한다:
        // 어댑터가 링크 토큰을 쓰므로 호출측 취소도 같은 경로(shim 토큰 취소)를 탄다.
        using var cts = new CancellationTokenSource();
        var task = cam.CaptureAsync(cts.Token);
        cts.CancelAfter(50);

        // 호출측 취소는 전파(웹캠 CaptureStillAsync와 동형) — 컷 루프의 기존 취소 처리가 그대로 적용된다.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.Equal(1, shim.CaptureCalls);
    }

    [Fact]
    public async Task Capture_Empty_Receive_Returns_Null()
    {
        CreateMd3();
        var shim = new FakeNikonSdkShim { CaptureResult = Array.Empty<byte>() };
        await using var cam = MakeCamera(shim);
        Assert.True(await cam.ConnectAsync());

        Assert.Null(await cam.CaptureAsync());   // 빈 수신도 컷 실패(재시도 대상)
    }

    [Fact]
    public async Task Capture_Throwing_Returns_Null_Without_Propagating()
    {
        CreateMd3();
        var shim = new FakeNikonSdkShim { CaptureThrows = new IOException("USB 오류") };
        await using var cam = MakeCamera(shim);
        Assert.True(await cam.ConnectAsync());

        Assert.Null(await cam.CaptureAsync());
    }

    [Fact]
    public async Task Capture_Succeeds_And_Returns_Bytes()
    {
        CreateMd3();
        var shim = new FakeNikonSdkShim { CaptureResult = new byte[] { 9, 8, 7 } };
        await using var cam = MakeCamera(shim);
        Assert.True(await cam.ConnectAsync());

        Assert.Equal(new byte[] { 9, 8, 7 }, await cam.CaptureAsync());
    }

    // ── T-A4: DeviceLost → ConnectionChanged 재발행 ──

    [Fact]
    public async Task DeviceLost_Reraises_ConnectionChanged_And_Marks_Unavailable()
    {
        CreateMd3();
        var shim = new FakeNikonSdkShim();
        await using var cam = MakeCamera(shim);
        Assert.True(await cam.ConnectAsync());
        Assert.True(cam.IsAvailable);

        var changes = new List<ExternalCameraConnectionChange>();
        void OnChanged(object? s, ExternalCameraConnectionChange e) => changes.Add(e);
        cam.ConnectionChanged += OnChanged;
        try
        {
            shim.RaiseDeviceLost("USB 연결이 끊겼습니다");
        }
        finally { cam.ConnectionChanged -= OnChanged; }

        Assert.False(cam.IsAvailable);
        Assert.Equal("USB 연결이 끊겼습니다", cam.UnavailableReason);
        var change = Assert.Single(changes);
        Assert.False(change.IsConnected);
        Assert.Equal("USB 연결이 끊겼습니다", change.Reason);
    }

    [Fact]
    public async Task DeviceLost_Before_Connect_Does_Not_Raise()
    {
        // 연결 전 탈락 통지는 상태 변화가 아니다 — 배너가 중복으로 뜨는 것을 막는다.
        CreateMd3();
        var shim = new FakeNikonSdkShim();
        await using var cam = MakeCamera(shim);

        int raised = 0;
        void OnChanged(object? s, ExternalCameraConnectionChange e) => raised++;
        cam.ConnectionChanged += OnChanged;
        try { shim.RaiseDeviceLost(null); }
        finally { cam.ConnectionChanged -= OnChanged; }

        Assert.Equal(0, raised);
        Assert.Equal(NikonCameraReasons.NotConnected, cam.UnavailableReason);   // 사유 폴백은 적용
    }

    [Fact]
    public async Task Connect_Raises_ConnectionChanged_Connected()
    {
        CreateMd3();
        var shim = new FakeNikonSdkShim();
        await using var cam = MakeCamera(shim);

        var changes = new List<ExternalCameraConnectionChange>();
        void OnChanged(object? s, ExternalCameraConnectionChange e) => changes.Add(e);
        cam.ConnectionChanged += OnChanged;
        try { Assert.True(await cam.ConnectAsync()); }
        finally { cam.ConnectionChanged -= OnChanged; }

        var change = Assert.Single(changes);
        Assert.True(change.IsConnected);
        Assert.Null(change.Reason);
    }

    [Fact]
    public async Task ConnectionChanged_Subscriber_Exception_Does_Not_Kill_Device_Layer()
    {
        CreateMd3();
        var shim = new FakeNikonSdkShim();
        await using var cam = MakeCamera(shim);
        Assert.True(await cam.ConnectAsync());

        void Throwing(object? s, ExternalCameraConnectionChange e) => throw new InvalidOperationException("VM 버그");
        cam.ConnectionChanged += Throwing;
        try
        {
            // 임의 스레드에서 올라오는 구독자 예외는 잡을 곳이 없다 — 장치 계층이 격리한다.
            var ex = Record.Exception(() => shim.RaiseDeviceLost("뽑힘"));
            Assert.Null(ex);
        }
        finally { cam.ConnectionChanged -= Throwing; }

        Assert.False(cam.IsAvailable);
    }

    // ── T-A5: 동시 2호출 → OpenAsync 1회(단일 비행) ──

    [Fact]
    public async Task Concurrent_Connect_Calls_Open_Shim_Once()
    {
        CreateMd3();
        var shim = new FakeNikonSdkShim { OpenDelay = TimeSpan.FromMilliseconds(150) };
        await using var cam = MakeCamera(shim);

        var a = cam.ConnectAsync();
        var b = cam.ConnectAsync();
        var results = await Task.WhenAll(a, b);

        Assert.True(results[0]);
        Assert.True(results[1]);
        Assert.Equal(1, shim.OpenCalls);   // ★ 모듈 로드 중복 방지
    }

    [Fact]
    public async Task Connect_When_Already_Connected_Skips_Shim()
    {
        CreateMd3();
        var shim = new FakeNikonSdkShim();
        await using var cam = MakeCamera(shim);

        Assert.True(await cam.ConnectAsync());
        Assert.True(await cam.ConnectAsync());

        Assert.Equal(1, shim.OpenCalls);
    }

    [Fact]
    public async Task Reconnect_After_Disconnect_Reopens_Shim()
    {
        // ★ DisconnectAsync가 Dispose를 부르면 이 테스트가 깨진다 — 다음 세션이 영구 강등되는 회귀 잠금.
        CreateMd3();
        var shim = new FakeNikonSdkShim();
        await using var cam = MakeCamera(shim);

        Assert.True(await cam.ConnectAsync());
        await cam.DisconnectAsync();
        Assert.False(cam.IsAvailable);
        Assert.Equal(1, shim.CloseCalls);
        Assert.Equal(0, shim.DisposeCalls);   // Close만 — Dispose는 앱 종료 1회

        Assert.True(await cam.ConnectAsync());
        Assert.Equal(2, shim.OpenCalls);
        Assert.True(cam.IsAvailable);
    }

    [Fact]
    public async Task Disconnect_When_Never_Connected_Is_NoOp()
    {
        var shim = new FakeNikonSdkShim();
        await using var cam = MakeCamera(shim);

        await cam.DisconnectAsync();
        Assert.Equal(0, shim.CloseCalls);
    }

    // ── T-A6: 연결 직후 저장 노출값 재적용(도메인 불일치는 스킵) ──

    [Fact]
    public async Task Connect_Reapplies_Stored_Exposure_Values()
    {
        CreateMd3();
        var settings = MakeSettings(s =>
        {
            s.ExternalShutterSpeed = "1/125";
            s.ExternalAperture = "f/5.6";
            s.ExternalIso = "400";
        });
        var shim = new FakeNikonSdkShim
        {
            Domain = new ExposureDomain(
                new ExposureDomainEntry(new[] { "1/60", "1/125" }, 0),
                new ExposureDomainEntry(new[] { "f/4", "f/5.6" }, 0),
                new ExposureDomainEntry(new[] { "100", "400" }, 0)),
        };
        await using var cam = MakeCamera(shim, settings);

        Assert.True(await cam.ConnectAsync());

        Assert.Equal(3, shim.ExposureWrites.Count);
        Assert.Contains((ExposureParameter.ShutterSpeed, "1/125"), shim.ExposureWrites);
        Assert.Contains((ExposureParameter.Aperture, "f/5.6"), shim.ExposureWrites);
        Assert.Contains((ExposureParameter.Iso, "400"), shim.ExposureWrites);
    }

    [Fact]
    public async Task Connect_Skips_Exposure_Values_Absent_From_Domain()
    {
        CreateMd3();
        var settings = MakeSettings(s =>
        {
            s.ExternalShutterSpeed = "1/125";   // 도메인에 있음 → 적용
            s.ExternalIso = "12800";            // 도메인에 없음 → 스킵(shim 미호출)
        });
        var shim = new FakeNikonSdkShim
        {
            Domain = new ExposureDomain(
                new ExposureDomainEntry(new[] { "1/60", "1/125" }, 0),
                null,
                new ExposureDomainEntry(new[] { "100", "400" }, 0)),
        };
        await using var cam = MakeCamera(shim, settings);

        Assert.True(await cam.ConnectAsync());

        Assert.Single(shim.ExposureWrites);
        Assert.Equal((ExposureParameter.ShutterSpeed, "1/125"), shim.ExposureWrites[0]);
    }

    [Fact]
    public async Task Connect_Skips_Empty_Exposure_Values()
    {
        // 빈 값 = "미지정"(카메라 현재값 유지) — 왕복하지 않는다.
        CreateMd3();
        var shim = new FakeNikonSdkShim { Domain = new ExposureDomain(null, null, null) };
        await using var cam = MakeCamera(shim);

        Assert.True(await cam.ConnectAsync());
        Assert.Empty(shim.ExposureWrites);
    }

    [Fact]
    public async Task SetExposure_Unsupported_Capability_Does_Not_Call_Shim()
    {
        CreateMd3();
        var shim = new FakeNikonSdkShim
        {
            Capabilities = new ExternalCameraCapabilities(
                CapabilityState.Supported, CapabilityState.Unsupported, CapabilityState.Unsupported,
                CapabilityState.Unsupported, CapabilityState.Unsupported, null),
        };
        await using var cam = MakeCamera(shim);
        Assert.True(await cam.ConnectAsync());

        Assert.False(await cam.SetExposureAsync(ExposureParameter.Iso, "400"));
        Assert.Empty(shim.ExposureWrites);
    }

    [Fact]
    public async Task SetExposure_When_Disconnected_Returns_False()
    {
        var shim = new FakeNikonSdkShim();
        await using var cam = MakeCamera(shim);

        Assert.False(await cam.SetExposureAsync(ExposureParameter.Iso, "400"));
        Assert.Empty(shim.ExposureWrites);
    }

    // ── capability 캐시·프로브 실패(§4.1, E10) ──

    [Fact]
    public async Task Capabilities_Are_Probed_Once_And_Cached()
    {
        CreateMd3();
        var shim = new FakeNikonSdkShim();
        await using var cam = MakeCamera(shim);
        Assert.True(await cam.ConnectAsync());

        var a = await cam.GetCapabilitiesAsync();
        var b = await cam.GetCapabilitiesAsync();

        Assert.Equal(1, shim.ProbeCalls);   // 매 촬영마다 SDK 왕복하지 않는다
        Assert.Same(a, b);
        Assert.Equal(80, a!.BatteryLevelPercent);
    }

    [Fact]
    public async Task Probe_Failure_Yields_All_Unknown_Not_Null()
    {
        CreateMd3();
        var shim = new FakeNikonSdkShim { Capabilities = null };   // 조회 실패
        await using var cam = MakeCamera(shim);
        Assert.True(await cam.ConnectAsync());

        var caps = await cam.GetCapabilitiesAsync();

        Assert.NotNull(caps);   // 연결은 됐으므로 null("물어볼 대상 없음")이 아니다
        Assert.Equal(CapabilityState.Unknown, caps!.StillCapture);
        Assert.False(ExternalCapturePolicy.IsOpen(caps.StillCapture));   // 게이트는 닫힌다
    }

    [Fact]
    public async Task Probe_Throwing_Does_Not_Break_Connection()
    {
        CreateMd3();
        var shim = new FakeNikonSdkShim { ProbeThrows = new InvalidOperationException("capability 조회 실패") };
        await using var cam = MakeCamera(shim);

        Assert.True(await cam.ConnectAsync());   // 연결 자체는 성립
        var caps = await cam.GetCapabilitiesAsync();
        Assert.Equal(CapabilityState.Unknown, caps!.ExposureControl);
    }

    [Fact]
    public async Task Capabilities_Invalidated_On_Reconnect()
    {
        CreateMd3();
        var shim = new FakeNikonSdkShim();
        await using var cam = MakeCamera(shim);
        Assert.True(await cam.ConnectAsync());
        Assert.NotNull(await cam.GetCapabilitiesAsync());

        await cam.DisconnectAsync();
        Assert.Null(await cam.GetCapabilitiesAsync());   // 다른 바디·다른 모드일 수 있다 → 캐시 폐기

        Assert.True(await cam.ConnectAsync());
        Assert.Equal(2, shim.ProbeCalls);
    }

    // ── 물리 플래시 게이트(§4.3) ──

    [Fact]
    public async Task PhysicalFlash_Requires_Supported_Capability()
    {
        CreateMd3();
        var shim = new FakeNikonSdkShim();   // AllSupported → PhysicalFlash=Supported
        await using var cam = MakeCamera(shim);
        Assert.True(await cam.ConnectAsync());

        Assert.True(await cam.TrySetPhysicalFlashAsync(true));
        Assert.Equal(new[] { true }, shim.FlashWrites);
    }

    [Fact]
    public async Task PhysicalFlash_Unknown_Capability_Does_Not_Call_Shim()
    {
        CreateMd3();
        var shim = new FakeNikonSdkShim { Capabilities = null };   // 프로브 실패 → 전 항목 Unknown
        await using var cam = MakeCamera(shim);
        Assert.True(await cam.ConnectAsync());

        Assert.False(await cam.TrySetPhysicalFlashAsync(true));
        Assert.Empty(shim.FlashWrites);   // 미검증 경로를 손님 세션에서 처음 실행하지 않는다
    }

    // ── T-A7: DisposeAsync → shim DisposeAsync(Shutdown 보장) ──

    [Fact]
    public async Task DisposeAsync_Disposes_Shim_Once()
    {
        CreateMd3();
        var shim = new FakeNikonSdkShim();
        var cam = MakeCamera(shim);
        Assert.True(await cam.ConnectAsync());

        await cam.DisposeAsync();
        await cam.DisposeAsync();   // 재호출 무해(idempotent)

        Assert.Equal(1, shim.DisposeCalls);
        Assert.False(cam.IsAvailable);
    }

    [Fact]
    public async Task Connect_After_Dispose_Returns_False()
    {
        CreateMd3();
        var shim = new FakeNikonSdkShim();
        var cam = MakeCamera(shim);
        await cam.DisposeAsync();

        Assert.False(await cam.ConnectAsync());
        Assert.Equal(0, shim.OpenCalls);
    }

    // ── T-A8: 캡처 진행 중 재진입 → 즉시 null ──

    [Fact]
    public async Task Reentrant_Capture_Returns_Null_Immediately()
    {
        CreateMd3();
        var shim = new FakeNikonSdkShim { CaptureDelay = TimeSpan.FromMilliseconds(200) };
        await using var cam = MakeCamera(shim);
        Assert.True(await cam.ConnectAsync());

        var first = cam.CaptureAsync();
        // 첫 캡처가 shim에 진입할 시간을 준 뒤 재진입 시도.
        await Task.Delay(40);
        var second = await cam.CaptureAsync();

        Assert.Null(second);                       // 즉시 null(대기하지 않는다)
        Assert.NotNull(await first);
        Assert.Equal(1, shim.CaptureCalls);        // 셔터가 겹치지 않았다
        Assert.Equal(1, shim.MaxConcurrentCaptures);
    }

    [Fact]
    public async Task Capture_Slot_Is_Released_After_Failure()
    {
        // 실패 후 플래그가 남으면 그 세션의 남은 컷이 전부 즉시 null이 된다(영구 강등).
        CreateMd3();
        var shim = new FakeNikonSdkShim { CaptureResult = null };
        await using var cam = MakeCamera(shim);
        Assert.True(await cam.ConnectAsync());

        Assert.Null(await cam.CaptureAsync());

        shim.CaptureResult = new byte[] { 1 };
        Assert.NotNull(await cam.CaptureAsync());
        Assert.Equal(2, shim.CaptureCalls);
    }

    [Fact]
    public async Task Capture_When_Disconnected_Returns_Null_Without_Shim_Call()
    {
        var shim = new FakeNikonSdkShim();
        await using var cam = MakeCamera(shim);

        Assert.Null(await cam.CaptureAsync());
        Assert.Equal(0, shim.CaptureCalls);
    }

    // ── 모델 레지스트리 연동(§3.3) ──

    [Fact]
    public async Task Unknown_Model_Id_In_Settings_Falls_Back_To_Default_Module()
    {
        CreateMd3();
        // Clamp를 우회해 손상값을 직접 주입(ini 손입력·구버전 값 모사).
        var settings = MakeSettings(s => s.ExternalCameraModel = "CanonEOS");
        var shim = new FakeNikonSdkShim();
        await using var cam = MakeCamera(shim, settings);

        Assert.True(await cam.ConnectAsync());
        Assert.Equal("Nikon D5300", cam.ModelName);
        Assert.EndsWith(ExternalCameraModels.Default.Md3FileName, shim.LastMd3Path);
    }

    // ══════════ it24 Step 2: 준비도 검사(설계 §5.1 ⓐⓑⓒ · §12.2 T-R2·T-R3·T-R5) ══════════

    /// <summary>
    /// ★ T-R2 — R1의 코드 실체: shim이 부재 구현이면 <b>md3 파일이 있어도</b> CanControl=false다.
    /// 이 한 줄이 무너지면 "파일을 넣었으니 이제 장치가 없다는 뜻이겠지"라는 거짓 판정이 화면에 뜬다.
    /// </summary>
    [Fact]
    public void CheckReadiness_Shim_Not_Operational_Is_False_Even_With_Module_File()
    {
        CreateMd3();
        var shim = new FakeNikonSdkShim { IsOperational = false };
        using var cam = MakeCamera(shim);

        var readiness = cam.CheckReadiness();

        Assert.False(readiness.CanControl);
        Assert.Equal(NikonCameraReasons.SdkMissing, readiness.Reason);
        Assert.Equal(0, shim.OpenCalls);   // 준비도 검사는 SDK를 호출하지 않는다
    }

    /// <summary>T-R3 — shim 정상 + md3 부재는 파일 경로가 담긴 사유(W11)로 강등된다(그 경로가 곧 조치 안내다).</summary>
    [Fact]
    public void CheckReadiness_Operational_Shim_Without_Module_File_Reports_Missing_Path()
    {
        var shim = new FakeNikonSdkShim { IsOperational = true };
        using var cam = MakeCamera(shim);

        var readiness = cam.CheckReadiness();

        Assert.False(readiness.CanControl);
        Assert.Equal(@"카메라 모듈 파일이 없습니다 (NikonSdk\Type0011.md3)", readiness.Reason);
    }

    /// <summary>T-R3 — shim 정상 + md3 존재면 비로소 "부재를 판정할 자격"이 생긴다.</summary>
    [Fact]
    public void CheckReadiness_Operational_Shim_With_Module_File_Is_Controllable()
    {
        CreateMd3();
        var shim = new FakeNikonSdkShim { IsOperational = true };
        using var cam = MakeCamera(shim);

        var readiness = cam.CheckReadiness();

        Assert.True(readiness.CanControl);
        Assert.Null(readiness.Reason);
        Assert.Equal(0, shim.OpenCalls);   // 여전히 USB·SDK 미접촉(파일 검사만)
    }

    /// <summary>T-R5 — 프로덕션 기본 shim은 항상 미구현이다(상수 회귀 잠금).</summary>
    [Fact]
    public void MissingShim_Is_Never_Operational()
    {
        INikonSdkShim shim = new MissingNikonSdkShim();
        Assert.False(shim.IsOperational);
    }

    /// <summary>
    /// 프로덕션 조합(부재 shim + 파일 없음)의 도달점은 S2다 — 현 배포본이 정직하게 말할 수 있는 전부.
    /// </summary>
    [Fact]
    public void Production_Default_Combination_Judges_As_Undetermined()
    {
        using var cam = MakeCamera(new FakeNikonSdkShim { IsOperational = false });

        var state = ExternalDiscoveryJudge.Judge(cam.CheckReadiness(), usbCandidateSeen: false, connected: false);
        Assert.Equal(ExternalCameraDiscoveryState.UndeterminedStackMissing, state);
    }
}
