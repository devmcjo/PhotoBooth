using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
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
using Xunit;

namespace MCPhoto.Tests;

/// <summary>
/// it23 B부: 테스트 로그인 모드의 세션 배선 · PIN 게이트 우회 봉인 · 경고 배너.
/// <para>
/// 이 파일의 핵심은 <b>우회가 실계정 경로로 새지 않는다</b>는 것을 자동으로 지키는 것이다(§B8.3).
/// 우회 코드가 실제 계정에 적용되면 그것은 인증 우회 취약점이다 — <c>T19</c>와 <c>T28</c>이 그 유일한 자동 검증이다.
/// </para>
/// ⚠️ 창을 만들지 않는다(headless). PIN 다이얼로그는 <see cref="FakePinPromptDialogService"/> 스텁으로 검증한다.
/// </summary>
public class AppShellTestModeTests
{
    // ── 최소 페이크(설정 화면 진입 시 셸이 해석하는 VM들) ──

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
        public IReadOnlyList<CameraDevice> EnumerateDevices() => Array.Empty<CameraDevice>();
        public void Dispose() { }
    }

    private sealed class FakeCameraTestDialog : ICameraTestDialogService
    {
        public Task ShowAsync(int deviceIndex) => Task.CompletedTask;
        public Task ShowAsync(CameraTestTarget target) => Task.CompletedTask;
    }

    private sealed class FakeDiagnosticsDialog : IDiagnosticsDialogService
    {
        public Task ShowAsync() => Task.CompletedTask;
    }

    /// <summary>PIN 검증 호출 횟수를 세는 스파이 — "테스트 계정 경로는 서버를 한 번도 부르지 않는다"의 근거.</summary>
    private sealed class SpyAccountService : IAccountService
    {
        public int VerifyCalls { get; private set; }
        public int SetOwnPinCalls { get; private set; }

        public Task<bool> VerifyPinAsync(string id, string pin, CancellationToken ct = default)
        {
            VerifyCalls++;
            return Task.FromResult(true);
        }

        public Task SetOwnPinAsync(string id, string? currentPin, string newPin, CancellationToken ct = default)
        {
            SetOwnPinCalls++;
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
        SessionContext Session,
        ITestModeService TestMode,
        SpyAccountService Accounts,
        FakePinPromptDialogService Pin,
        string IniPath);

    /// <summary>
    /// 임시 ini에 <c>[Test]</c> 섹션을 써서 실제 <see cref="TestModeService"/>로 셸을 조립한다
    /// (파싱·경로·참조 동일성까지 프로덕션 경로 그대로 검증한다 — 스텁으로 대체하면 봉인이 검증되지 않는다).
    /// </summary>
    private static Harness MakeShell(string? testSection, bool registerPinDialog = true,
        string pinToSubmit = "1234", bool pinDialogResult = true)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"mcphoto_shell_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var iniPath = Path.Combine(dir, "MCPhoto.ini");
        File.WriteAllText(iniPath, "[MCPhoto]\nCutCount=8\n" + (testSection ?? string.Empty));

        var settings = new IniSettingsService(iniPath: iniPath, fallbackCandidates: new[] { iniPath });
        settings.Load();
        var testMode = new TestModeService(settings);

        var session = new SessionContext();
        var accounts = new SpyAccountService();
        var pin = new FakePinPromptDialogService { Result = pinDialogResult, PinToSubmit = pinToSubmit };
        var services = new MapServiceProvider().Add<IAccountService>(accounts);
        if (registerPinDialog) services.Add<IPinPromptDialogService>(pin);

        var shell = new AppShellViewModel(new IdleWatchdog(), settings, services, session, logger: null, testMode: testMode);
        services.AddFactory<SettingsViewModel>(() => new SettingsViewModel(
            shell, settings, new FakeCameraService(), new FakeCameraTestDialog(),
            new FakeDiagnosticsDialog(), new FakeFirebaseClient { IsInitialized = true },
            new NullExternalCamera(),
            logger: null,
            licenseNotice: new LicenseNoticeService(baseDirectory: AppContext.BaseDirectory)));
        services.AddFactory<HomeViewModel>(() => new HomeViewModel(shell));

        return new Harness(shell, session, testMode, accounts, pin, iniPath);
    }

    private const string AdminSectionNoPin = "[Test]\nTestMode=1\nId=testadmin\nEmail=test@email.com\nRole=admin\n";
    private const string AdminSectionWithPin = AdminSectionNoPin + "Pin=1234\n";

    // ── B-T15/T16: 부팅 배선 ──

    /// <summary>
    /// B-T15: <c>Startup()</c>이 홈 진입 <b>직전</b>에 테스트 계정을 세션에 태운다.
    /// 참조 동일성이어야 이후 모든 우회 판정이 성립한다.
    /// </summary>
    [Fact]
    public void T15_Startup_Logs_In_Test_User_Before_Home()
    {
        var h = MakeShell(AdminSectionNoPin);

        h.Shell.Startup();

        Assert.NotNull(h.Session.CurrentUser);
        Assert.Same(h.TestMode.TestUser, h.Session.CurrentUser);   // 참조 동일
        Assert.Equal(UserRole.Admin, h.Session.CurrentUser!.Role);
        Assert.True(h.Shell.IsPower);                              // 역할 게이트가 자동으로 따라온다
        Assert.Equal(AppState.Home, h.Shell.CurrentState);
    }

    /// <summary>B-T16: 테스트 모드가 꺼져 있으면 로그인하지 않고 홈으로 간다(앱 동작 변화 0).</summary>
    [Fact]
    public void T16_Startup_Does_Not_Log_In_When_Disabled()
    {
        var h = MakeShell("[Test]\nTestMode=0\nRole=admin\n");

        h.Shell.Startup();

        Assert.Null(h.Session.CurrentUser);
        Assert.False(h.Shell.IsTestMode);
        Assert.Equal(string.Empty, h.Shell.TestModeBannerText);
        Assert.Equal(AppState.Home, h.Shell.CurrentState);
    }

    /// <summary>테스트 모드가 켜져도 <c>IBackendSession</c>에 토큰을 넣지 않는다(불변식 TM1의 표면 검증).</summary>
    [Fact]
    public void Startup_Never_Touches_Backend_Session()
    {
        var h = MakeShell(AdminSectionNoPin);

        h.Shell.Startup();

        // 셸은 IBackendSession을 해석조차 하지 않는다(MapServiceProvider에 등록돼 있지 않다 → null).
        // 토큰을 넣는 코드가 생기면 여기서 NullReferenceException으로 드러난다.
        Assert.NotNull(h.Session.CurrentUser);
    }

    // ── B-T17/T18/T19: PIN 게이트 ──

    /// <summary>
    /// B-T17: <c>Pin</c> 키가 없으면 게이트를 생략하고, <b><c>IAccountService</c>를 한 번도 호출하지 않는다</b>.
    /// 서버 호출이 0회여야 "토큰 없는 상태에서 게이트가 fail-closed로 닫힌다"는 블로커가 되살아나지 않는다.
    /// </summary>
    [Fact]
    public async Task T17_No_Pin_Skips_Gate_Without_Server_Call()
    {
        var h = MakeShell(AdminSectionNoPin);
        h.Shell.Startup();

        await h.Shell.OpenSettingsCommand.ExecuteAsync(null);

        Assert.Equal(AppState.Settings, h.Shell.CurrentState);
        Assert.Equal(0, h.Accounts.VerifyCalls);
        Assert.Equal(0, h.Accounts.SetOwnPinCalls);   // PromptSetup 분기에 도달하는 경로가 없다
        Assert.Equal(0, h.Pin.VerifyCount);
        Assert.Equal(0, h.Pin.SetupCount);
    }

    /// <summary>
    /// B-T18: <c>Pin=1234</c>면 게이트를 띄우고 <b>로컬 대조</b>한다 — 맞으면 통과, 틀리면 거부.
    /// 서버는 여전히 호출되지 않는다(로컬 대조라는 사실의 근거).
    /// </summary>
    [Theory]
    [InlineData("1234", true)]
    [InlineData("9999", false)]
    public async Task T18_Pin_Is_Verified_Locally(string submitted, bool shouldEnter)
    {
        var h = MakeShell(AdminSectionWithPin, pinToSubmit: submitted);
        h.Shell.Startup();

        await h.Shell.OpenSettingsCommand.ExecuteAsync(null);

        Assert.Equal(1, h.Pin.VerifyCount);
        Assert.Equal(0, h.Accounts.VerifyCalls);      // 서버 왕복 없음
        if (shouldEnter) Assert.Equal(AppState.Settings, h.Shell.CurrentState);
        else Assert.NotEqual(AppState.Settings, h.Shell.CurrentState);
    }

    /// <summary>
    /// B-T19 — <b>우회 봉인의 유일한 자동 검증</b>.
    /// 테스트 모드가 켜진 상태에서 <b>다른 <see cref="User"/> 인스턴스</b>(실제 SSO 로그인 상당)로 로그인하면
    /// 그 계정은 <b>정상 서버 PIN 게이트</b>를 타야 한다. 값이 전부 같아도 마찬가지다 — 판정이 참조 동일성이므로.
    /// <para>
    /// 이 테스트가 깨지면 <c>IsEnabled</c>로 분기하는 코드가 생겼다는 뜻이고, 그것은 인증 우회 취약점이다.
    /// </para>
    /// </summary>
    [Fact]
    public async Task T19_Real_Account_Still_Goes_Through_Server_Gate()
    {
        var h = MakeShell(AdminSectionNoPin);   // Pin 없음 = 테스트 계정이면 게이트 생략되는 설정
        h.Shell.Startup();

        // 테스트 계정과 Id·이메일·역할이 **전부 같은** 별 인스턴스로 로그인(위조 시도 상당).
        var twin = new User
        {
            Id = "testadmin", Email = "test@email.com", Role = UserRole.Admin,
            AuthMethod = AuthMethod.Google, HasPin = true,
        };
        h.Session.Login(twin);
        Assert.False(h.TestMode.IsTestUser(twin));

        await h.Shell.OpenSettingsCommand.ExecuteAsync(null);

        Assert.Equal(1, h.Pin.VerifyCount);      // 게이트가 실제로 떴다
        Assert.Equal(1, h.Accounts.VerifyCalls); // 서버 검증 경로를 탔다(우회가 새지 않았다)
        Assert.Equal(AppState.Settings, h.Shell.CurrentState);
    }

    /// <summary>다이얼로그 서비스 미등록은 fail-closed — 테스트 모드가 이 규약의 예외를 만들지 않는다.</summary>
    [Fact]
    public async Task Missing_Pin_Dialog_Blocks_Even_For_Test_User()
    {
        var h = MakeShell(AdminSectionWithPin, registerPinDialog: false);
        h.Shell.Startup();

        await h.Shell.OpenSettingsCommand.ExecuteAsync(null);

        Assert.NotEqual(AppState.Settings, h.Shell.CurrentState);
        Assert.Equal(0, h.Accounts.VerifyCalls);
    }

    /// <summary>계정 관리 화면도 같은 게이트를 쓴다 — 두 번째 소비자도 함께 열려야 사용자 관리에 도달한다.</summary>
    [Fact]
    public async Task Pin_Gate_Skip_Applies_To_Account_Screen_Too()
    {
        var h = MakeShell(AdminSectionNoPin);
        h.Shell.Startup();

        Assert.True(await h.Shell.EnsurePinGateAsync(h.Session.CurrentUser!));
        Assert.Equal(0, h.Accounts.VerifyCalls);
    }

    // ── B-T22/T23: 배너 ──

    /// <summary>
    /// B-T22: 배너 문구 3상태. 로그인 문구는 <b>역할 라벨과 이메일</b>을 반드시 포함한다 —
    /// <c>Role</c> 오타로 다른 역할이 섰을 때 즉시 발각되게 하는 안전망이며, 이메일은 개인 프레임 소유 키다.
    /// </summary>
    [Fact]
    public void T22_Banner_Text_Covers_Three_States()
    {
        var h = MakeShell(AdminSectionNoPin);
        Assert.True(h.Shell.IsTestMode);

        // ① 로그아웃 상태(시작 전)
        Assert.Equal(AppShellViewModel.TestModeBannerLoggedOut, h.Shell.TestModeBannerText);

        // ② 테스트 계정 로그인 중
        h.Shell.Startup();
        var banner = h.Shell.TestModeBannerText;
        Assert.Contains("관리자", banner);              // 역할 라벨
        Assert.Contains("test@email.com", banner);      // 이메일
        Assert.Contains("실제 운영에 사용하지 마세요", banner);

        // ③ 실제 계정 병행 로그인 — 우회는 비활성이지만 ini가 켜져 있다는 사실은 알린다.
        h.Session.Login(new User { Id = "real", Email = "real@example.com", Role = UserRole.User });
        Assert.Equal(AppShellViewModel.TestModeBannerRealAccount, h.Shell.TestModeBannerText);

        // 세 문구 모두 경고 문장을 포함한다(잘라낸 문구가 배포물에 남는 것을 막는다).
        foreach (var text in new[]
                 {
                     AppShellViewModel.TestModeBannerLoggedOut,
                     AppShellViewModel.TestModeBannerRealAccount,
                     AppShellViewModel.FormatTestModeBanner("관리자", "a@b.c"),
                 })
        {
            Assert.Contains("실제 운영에 사용하지 마세요", text);
        }
    }

    // ══════════ it25: 외부 카메라 시뮬레이션 배너 접미 ══════════
    //
    // 목적: 시뮬레이션이 켜지면 설정 화면은 "연결 확인됨"인데 촬영은 SDK 부재로 웹캠 강등한다.
    //       그 **의도된 간극**을 설정 화면 밖에서도 설명해 주는 표식이다.
    // ⚠️ 접미의 판정 조건은 시뮬레이션 게이트와 **같아야** 한다(IsTestUser 참조 동일성). 다르면 배너가
    //    "시뮬레이션 중"이라고 말하면서 실제로는 실관측을 하는 거짓 표시가 된다 — 이번 이터레이션 전체가
    //    없애려는 실패 유형 그 자체다. 아래 세 테스트 중 **실계정 케이스가 그 거짓을 잠그는 단정**이다.

    /// <summary>① 테스트 계정 + 시뮬레이션 on → 접미가 붙고 모델 표시명이 들어간다.</summary>
    [Fact]
    public void Banner_Appends_Simulation_Suffix_For_Test_Account()
    {
        var h = MakeShell(AdminSectionNoPin + "ExternalCamera=1\nExternalCameraType=0\n");
        h.Shell.Startup();

        var banner = h.Shell.TestModeBannerText;

        Assert.Contains(AppShellViewModel.FormatExternalCameraSimulationSuffix("Nikon D5300"), banner);
        Assert.Contains("외부 카메라 시뮬레이션", banner);
        // 기존 배너 본문(역할·이메일·경고)이 접미 때문에 잘려 나가지 않는다.
        Assert.Contains("관리자", banner);
        Assert.Contains("test@email.com", banner);
        Assert.Contains("실제 운영에 사용하지 마세요", banner);
    }

    /// <summary>
    /// 시뮬레이션이 켜졌으나 인식된 모델이 없으면(<c>Type=-1</c>) 라벨이 "인식된 장치 없음"이다.
    /// 빈 괄호("시뮬레이션()")를 남기면 무엇이 켜졌는지 말하지 못한다.
    /// </summary>
    [Fact]
    public void Banner_Suffix_Says_None_When_No_Model_Is_Mapped()
    {
        var h = MakeShell(AdminSectionNoPin + "ExternalCamera=1\nExternalCameraType=-1\n");
        h.Shell.Startup();

        Assert.Contains(
            AppShellViewModel.FormatExternalCameraSimulationSuffix(
                AppShellViewModel.ExternalCameraSimulationNoneLabel),
            h.Shell.TestModeBannerText);
        Assert.DoesNotContain("시뮬레이션()", h.Shell.TestModeBannerText);
    }

    /// <summary>② 테스트 계정 + 시뮬레이션 off(기본) → 접미 없음. 기존 배너와 문자 단위로 동일하다.</summary>
    [Fact]
    public void Banner_Has_No_Suffix_When_Simulation_Is_Off()
    {
        var h = MakeShell(AdminSectionNoPin);
        h.Shell.Startup();

        Assert.Equal(
            AppShellViewModel.FormatTestModeBanner("관리자", "test@email.com"),
            h.Shell.TestModeBannerText);
        Assert.DoesNotContain("시뮬레이션", h.Shell.TestModeBannerText);
    }

    /// <summary>
    /// ★ ③ 테스트 ini on + <b>실계정 로그인</b> → 접미 없음. 이 상태에서는 시뮬레이션이 적용되지 않으므로
    /// (게이트가 <c>IsTestUser</c>이므로) "시뮬레이션 중" 표시는 <b>거짓</b>이다.
    /// <para>
    /// 접미를 <c>IsEnabled</c>나 <c>Options</c> 값만 보고 붙이면 이 테스트가 깨진다 — 그것이 이 단정의 목적이다.
    /// </para>
    /// </summary>
    [Fact]
    public void Banner_Has_No_Suffix_For_Real_Account_Even_With_Simulation_Ini()
    {
        var h = MakeShell(AdminSectionNoPin + "ExternalCamera=1\nExternalCameraType=0\n");
        h.Shell.Startup();
        Assert.Contains("시뮬레이션", h.Shell.TestModeBannerText);   // 테스트 계정에서는 붙어 있다

        h.Session.Login(new User { Id = "real", Email = "real@example.com", Role = UserRole.User });

        Assert.Equal(AppShellViewModel.TestModeBannerRealAccount, h.Shell.TestModeBannerText);
        Assert.DoesNotContain("시뮬레이션", h.Shell.TestModeBannerText);
    }

    /// <summary>로그아웃 상태에도 접미가 붙지 않는다(시뮬레이션이 적용될 세션이 없다).</summary>
    [Fact]
    public void Banner_Has_No_Suffix_When_Logged_Out()
    {
        var h = MakeShell(AdminSectionNoPin + "ExternalCamera=1\nExternalCameraType=0\n");

        Assert.Equal(AppShellViewModel.TestModeBannerLoggedOut, h.Shell.TestModeBannerText);
        Assert.DoesNotContain("시뮬레이션", h.Shell.TestModeBannerText);
    }

    /// <summary>
    /// B-T23: 로그아웃 시 배너 문구 통지가 발행된다. 없으면 배너가 "관리자 권한으로 실행 중"이라는
    /// <b>거짓</b>을 계속 말한다.
    /// </summary>
    [Fact]
    public void T23_Banner_Text_Notifies_On_Logout()
    {
        var h = MakeShell(AdminSectionNoPin);
        h.Shell.Startup();

        var changed = new List<string>();
        h.Shell.PropertyChanged += (_, e) => { if (e.PropertyName is { } n) changed.Add(n); };

        h.Session.Logout();

        Assert.Contains(nameof(AppShellViewModel.TestModeBannerText), changed);
        Assert.Equal(AppShellViewModel.TestModeBannerLoggedOut, h.Shell.TestModeBannerText);
    }

    /// <summary>
    /// 배너는 <c>IsEnabled</c>가 참인 동안 <b>항상</b> 렌더된다(불변식 TM4) — 로그아웃해도 사라지지 않고
    /// 문구만 바뀐다. 테스트 모드는 세션 상태가 아니라 <b>설정</b>이므로 위험이 그대로 남는다.
    /// </summary>
    [Fact]
    public void Banner_Stays_Visible_After_Logout()
    {
        var h = MakeShell(AdminSectionNoPin);
        h.Shell.Startup();
        h.Session.Logout();

        Assert.True(h.Shell.IsTestMode);
        Assert.NotEqual(string.Empty, h.Shell.TestModeBannerText);
    }

    /// <summary>
    /// §B8.5: 로그아웃은 정상 동작하고(게스트 상태도 테스트 대상이다), 재로그인은 로그인 화면 버튼이 담당한다.
    /// 같은 인스턴스를 다시 태우므로 우회(PIN 생략)가 유지된다.
    /// </summary>
    [Fact]
    public async Task Test_Login_Command_Restores_Same_Instance()
    {
        var h = MakeShell(AdminSectionNoPin);
        h.Shell.Startup();
        h.Session.Logout();
        Assert.Null(h.Session.CurrentUser);

        await h.Shell.LoginAsTestUserCommand.ExecuteAsync(null);

        Assert.Same(h.TestMode.TestUser, h.Session.CurrentUser);
        Assert.True(h.TestMode.IsTestUser(h.Session.CurrentUser));
        Assert.Equal("테스트 계정으로 로그인 (관리자)", h.Shell.TestLoginLabel);
    }

    // ── B-T25: 역할 게이트 스냅샷 ──

    /// <summary>
    /// B-T25: 5역할 각각의 게이트 기대값 매트릭스. 역할 게이트가 <b>배선 없이 자동으로</b> 따라온다는
    /// 결론(§B6)을 고정한다 — 앱이 역할을 캐시하지 않고 매번 <c>CurrentUser.Role</c>을 읽기 때문이다.
    /// </summary>
    [Theory]
    [InlineData("temp_user", false, false, false)]
    [InlineData("user", false, false, true)]      // 외부 장치 편집은 User 이상(TempUser만 제외)
    [InlineData("advanced_user", false, true, true)]
    [InlineData("manager", true, true, true)]
    [InlineData("admin", true, true, true)]
    public void T25_Role_Gates_Follow_Automatically(
        string role, bool isPower, bool canWriteFrames, bool canConfigureExternal)
    {
        var h = MakeShell($"[Test]\nTestMode=1\nRole={role}\n");
        h.Shell.Startup();

        var user = h.Session.CurrentUser!;
        Assert.Equal(isPower, h.Shell.IsPower);
        Assert.Equal(canWriteFrames, user.Role.CanWriteFrames());
        Assert.Equal(canConfigureExternal, user.Role.CanConfigureExternalCamera());
        Assert.True(h.Shell.IsLoggedIn);
    }

    // ── B-T27: 테스트 모드에서 라이선스 뷰어 도달(C부 수락 기준 AC-C1의 B측 대응) ──

    /// <summary>
    /// B-T27: 테스트 모드 ON(<c>Pin</c> 없음)에서 설정 화면에 진입하고, 그 화면의
    /// <c>OpenLicenseViewerCommand</c>로 요약 카드가 채워지며 전문까지 도달한다.
    /// 즉 B부가 PIN 게이트를 바꿔도 C부의 "로그인 무관 접근"이 깨지지 않는다.
    /// <para>
    /// it24: 열기만으로는 본문을 읽지 않으므로(요약 2단 구조) 전문 도달은 카드 커맨드로 확인한다.
    /// 실제 서비스 + 빌드 출력의 <c>licenses/</c>를 쓰는 통합 경로라 매니페스트 배포 누락도 함께 잡힌다.
    /// </para>
    /// </summary>
    [Fact]
    public async Task T27_License_Viewer_Is_Reachable_Under_Test_Mode()
    {
        var h = MakeShell(AdminSectionNoPin);
        h.Shell.Startup();

        await h.Shell.OpenSettingsCommand.ExecuteAsync(null);
        Assert.Equal(AppState.Settings, h.Shell.CurrentState);

        var settingsVm = Assert.IsType<SettingsViewModel>(h.Shell.CurrentViewModel);
        await settingsVm.OpenLicenseViewerCommand.ExecuteAsync(null);

        Assert.True(settingsVm.IsLicenseViewerOpen);
        // 빌드 출력에 licenses/(고지 txt + 요약 매니페스트)가 복사되므로 카드가 실제로 채워진다.
        Assert.False(settingsVm.HasLicenseError);
        Assert.False(settingsVm.HasLicenseDegraded);
        Assert.NotEmpty(settingsVm.LicenseSelfComponents);
        Assert.NotEmpty(settingsVm.LicenseBundledComponents);

        // GPLv3 §4 이행의 실체: 전문에 실제로 도달한다.
        await settingsVm.ShowLicenseFullTextCommand.ExecuteAsync(settingsVm.LicenseBundledComponents[0]);
        Assert.False(settingsVm.HasLicenseError);
        Assert.Contains("GNU GENERAL PUBLIC LICENSE", settingsVm.LicenseText);
    }

    // ── B-T28: 우회 누출 금지 정적 검사 ──

    /// <summary>
    /// B-T28: 소스 전체에서 <c>ITestModeService.IsEnabled</c>를 조건으로 쓰는 지점이
    /// <b>배너 표시·라벨·DI 등록</b>으로 한정되는지 확인한다. 그 밖에서 <c>IsEnabled</c>로 분기하면
    /// 테스트 모드가 켜진 채 실제 계정으로 로그인한 세션에도 우회가 적용된다(불변식 TM3 위반 = 인증 우회).
    /// <para>
    /// 왜 정적 검사인가: 새 우회 지점은 <b>추가되는 코드</b>라서 기존 단위 테스트가 잡지 못한다.
    /// (<c>LicenseComplianceTests</c>의 csproj 검사와 같은 계열의 안전망)
    /// </para>
    /// </summary>
    [Fact]
    public void T28_IsEnabled_Is_Only_Used_For_Banner_And_Registration()
    {
        var srcDir = FindSrcDir();
        var allowed = new[]
        {
            Path.Combine(srcDir, "MCPhoto.App", "ServiceRegistration.cs"),        // DI 조건 데코레이션
            Path.Combine(srcDir, "MCPhoto.Core", "Settings", "ITestModeService.cs"),
            Path.Combine(srcDir, "MCPhoto.Core", "Settings", "TestModeService.cs"),
            Path.Combine(srcDir, "MCPhoto.Core", "Settings", "TestModeOptions.cs"),
        };

        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(srcDir, "*.cs", SearchOption.AllDirectories))
        {
            if (allowed.Contains(file, StringComparer.OrdinalIgnoreCase)) continue;

            var lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                // `_testMode.IsEnabled` / `_testMode?.IsEnabled` 형태만 본다(주석 줄은 제외).
                if (!Regex.IsMatch(line, @"testMode\s*\??\.\s*IsEnabled", RegexOptions.IgnoreCase)) continue;
                if (line.TrimStart().StartsWith("//", StringComparison.Ordinal)) continue;

                // 허용: 표시 전용 게이트(배너 IsTestMode · 진단 행 IsTestModeOn)와 로그인 버튼 라벨.
                // 이들은 "ini가 켜져 있다"를 사람에게 알리는 것이 목적이므로 IsEnabled가 정확한 명제다.
                if (Regex.IsMatch(line, @"IsTestMode(On)?\s*=>")) continue;
                if (Regex.IsMatch(line, @"TestLoginLabel\s*=>")) continue;
                // 허용: TestModeBannerText 안의 조기 반환(배너 문구 자체가 IsEnabled의 함수다).
                if (line.Contains("return string.Empty", StringComparison.Ordinal)) continue;

                offenders.Add($"{Path.GetFileName(file)}:{i + 1}: {line.Trim()}");
            }
        }

        Assert.True(offenders.Count == 0,
            "ITestModeService.IsEnabled 로 분기하는 지점이 배너·라벨·DI 등록 밖에서 발견됐다 — "
            + "우회가 실계정에 적용될 수 있다(불변식 TM3). IsTestUser(참조 동일성)로 판정할 것:\n"
            + string.Join("\n", offenders));
    }

    /// <summary>
    /// 반대편 고정: PIN 게이트의 테스트 분기가 <c>IsTestUser</c>를 쓰고 있다(조건이 사라지지 않았다).
    /// </summary>
    [Fact]
    public void Pin_Gate_Branch_Uses_IsTestUser()
    {
        var shellSource = File.ReadAllText(Path.Combine(FindSrcDir(), "MCPhoto.App", "AppShellViewModel.cs"));
        var gate = Regex.Match(shellSource,
            @"public Task<bool> EnsurePinGateAsync.*?\n    \}", RegexOptions.Singleline);

        Assert.True(gate.Success, "EnsurePinGateAsync 본문을 찾지 못함");
        Assert.Contains("IsTestUser(user)", gate.Value);
        Assert.DoesNotContain("PromptSetup", gate.Value[..gate.Value.IndexOf("var account", StringComparison.Ordinal)]);
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
}
