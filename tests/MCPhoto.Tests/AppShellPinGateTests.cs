using System.IO;
using MCPhoto.App;
using MCPhoto.App.Services;
using MCPhoto.App.ViewModels;
using MCPhoto.Core.Accounts;
using MCPhoto.Core.Capture;
using MCPhoto.Core.Models;
using MCPhoto.Core.Navigation;
using MCPhoto.Core.Settings;
using MCPhoto.Core.Upload;
using MCPhoto.Tests.Fakes;

namespace MCPhoto.Tests;

/// <summary>
/// it15 §6.2: 설정 진입 게이트가 PIN 단일 경로가 되었음을 검증한다.
/// 게스트는 무가드, 로그인 사용자는 PIN 확인/최초 설정, 서비스 미등록은 fail-closed(진입 차단).
/// 실제 다이얼로그 창은 띄우지 않는다(headless — <see cref="FakePinPromptDialogService"/>).
/// </summary>
public class AppShellPinGateTests
{
    /// <summary>테스트용 카메라 서비스 — EnumerateDevices만 의미 있음(나머지 no-op).</summary>
    private sealed class FakeCameraService : ICameraService
    {
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
        public IReadOnlyList<CameraDevice> EnumerateDevices() => new[] { new CameraDevice(0, "Camera 0") };
        public void Dispose() { }
    }

    private sealed class FakeCameraTestDialog : ICameraTestDialogService
    {
        public Task ShowAsync(int deviceIndex) => Task.CompletedTask;
    }

    private sealed class FakeDiagnosticsDialog : IDiagnosticsDialogService
    {
        public Task ShowAsync() => Task.CompletedTask;
    }

    /// <summary>PIN 검증 결과를 주입 가능한 계정 서비스(다른 메서드는 no-op).</summary>
    private sealed class StubAccountService : IAccountService
    {
        public bool VerifyResult { get; set; } = true;
        public int VerifyCalls { get; private set; }
        public (string id, string? currentPin, string newPin)? SetOwnPinCall { get; private set; }

        public Task<bool> VerifyPinAsync(string id, string pin, CancellationToken ct = default)
        {
            VerifyCalls++;
            return Task.FromResult(VerifyResult);
        }

        public Task SetOwnPinAsync(string id, string? currentPin, string newPin, CancellationToken ct = default)
        {
            SetOwnPinCall = (id, currentPin, newPin);
            return Task.CompletedTask;
        }

        public Task<User?> LoginWithGoogleAsync(string code, string codeVerifier, string redirectUri, string? nonce = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<User>> GetAllAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<User>>(Array.Empty<User>());
        public Task DeleteAsync(string id, CancellationToken ct = default) => Task.CompletedTask;
        public Task SetRoleAsync(string id, UserRole role, CancellationToken ct = default) => Task.CompletedTask;
        public Task ResetPinAsync(string targetId, string newPin, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed record Harness(
        AppShellViewModel Shell,
        StubAccountService Accounts,
        FakePinPromptDialogService Pin);

    private static Harness MakeShell(User? loginUser, bool registerPinDialog = true, bool pinDialogResult = true)
    {
        var settings = new IniSettingsService(iniPath: Path.Combine(Path.GetTempPath(), $"shell_{Guid.NewGuid():N}.ini"));
        settings.Load();

        var session = new SessionContext();
        if (loginUser is not null) session.Login(loginUser);

        var accounts = new StubAccountService();
        var pin = new FakePinPromptDialogService { Result = pinDialogResult };
        var services = new MapServiceProvider().Add<IAccountService>(accounts);
        if (registerPinDialog) services.Add<IPinPromptDialogService>(pin);

        var shell = new AppShellViewModel(new IdleWatchdog(), settings, services, session);
        // 설정 화면 진입 시 셸이 해석하는 VM들(셸 순환 → 지연 생성).
        services.AddFactory<SettingsViewModel>(() => new SettingsViewModel(
            shell, settings, new FakeCameraService(), new FakeCameraTestDialog(),
            new FakeDiagnosticsDialog(), new FakeFirebaseClient { IsInitialized = true }));
        services.AddFactory<HomeViewModel>(() => new HomeViewModel(shell));

        return new Harness(shell, accounts, pin);
    }

    private static User Google(bool hasPin) =>
        new() { Id = "g", Role = UserRole.User, AuthMethod = AuthMethod.Google, HasPin = hasPin };

    // ── T5: 게스트 무가드 (현행 보존) ──

    [Fact]
    public async Task T5_Guest_Enters_Settings_Without_Pin()
    {
        var h = MakeShell(loginUser: null);

        await h.Shell.OpenSettingsCommand.ExecuteAsync(null);

        Assert.Equal(AppState.Settings, h.Shell.CurrentState);
        Assert.Equal(0, h.Pin.VerifyCount);
        Assert.Equal(0, h.Pin.SetupCount);
    }

    // ── T4: fail-closed (다이얼로그 서비스 미등록) ──

    [Fact]
    public async Task T4_Missing_PinDialog_Service_Blocks_Settings()
    {
        var h = MakeShell(Google(hasPin: true), registerPinDialog: false);

        await h.Shell.OpenSettingsCommand.ExecuteAsync(null);

        Assert.NotEqual(AppState.Settings, h.Shell.CurrentState); // 진입하지 않음
        Assert.Equal(0, h.Accounts.VerifyCalls);                  // 검증 시도조차 없음
    }

    // ── PIN 단일 경로: 확인 / 최초 설정 / 취소 ──

    [Fact]
    public async Task HasPin_Verifies_And_Enters()
    {
        var h = MakeShell(Google(hasPin: true));

        await h.Shell.OpenSettingsCommand.ExecuteAsync(null);

        Assert.Equal(1, h.Pin.VerifyCount);
        Assert.Equal(1, h.Accounts.VerifyCalls);
        Assert.Equal(AppState.Settings, h.Shell.CurrentState);
    }

    [Fact]
    public async Task NoPin_Forces_Setup_Then_Enters()
    {
        var user = Google(hasPin: false);
        var h = MakeShell(user);

        await h.Shell.OpenSettingsCommand.ExecuteAsync(null);

        Assert.Equal(1, h.Pin.SetupCount);
        Assert.NotNull(h.Accounts.SetOwnPinCall);
        Assert.Null(h.Accounts.SetOwnPinCall!.Value.currentPin);  // 최초 설정 → 현재 PIN 불요
        Assert.True(user.HasPin);                                 // 세션 로컬 반영
        Assert.Equal(AppState.Settings, h.Shell.CurrentState);
    }

    [Fact]
    public async Task Cancelled_Pin_Prompt_Blocks_Settings()
    {
        var h = MakeShell(Google(hasPin: true), pinDialogResult: false);

        await h.Shell.OpenSettingsCommand.ExecuteAsync(null);

        Assert.NotEqual(AppState.Settings, h.Shell.CurrentState);
    }

    [Fact]
    public async Task Wrong_Pin_Blocks_Settings()
    {
        var h = MakeShell(Google(hasPin: true));
        h.Accounts.VerifyResult = false;   // 서버가 불일치 판정

        await h.Shell.OpenSettingsCommand.ExecuteAsync(null);

        Assert.Equal(1, h.Accounts.VerifyCalls);
        Assert.NotEqual(AppState.Settings, h.Shell.CurrentState);
    }
}
