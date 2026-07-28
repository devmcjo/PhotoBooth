using MCPhoto.App;
using MCPhoto.App.Services;
using MCPhoto.App.ViewModels;
using MCPhoto.Core.Accounts;
using MCPhoto.Core.Models;
using MCPhoto.Core.Navigation;
using MCPhoto.Core.Settings;
using MCPhoto.Tests.Fakes;

namespace MCPhoto.Tests;

/// <summary>
/// it14 §6.1 / it15 §6.3: 계정 관리 화면의 본인 PIN 설정·변경 + 진입 시 PIN 강제 생성.
/// 최초 설정(현재 PIN 불요)·변경(현재 PIN 필요)·형식/일치 검증·서버 호출·진입 게이트를 단위 검증.
/// </summary>
public class AccountViewModelPinTests
{
    private sealed class StubSettingsService : ISettingsService
    {
        private readonly AppSettings _settings;
        public StubSettingsService(AppSettings settings) => _settings = settings;
        public AppSettings Current => _settings;
        public AppSettings Load() => _settings;
        public bool Save() => true;
    }

    /// <summary>SetOwnPinAsync 호출 인자를 기록하는 계정 서비스. 예외 주입으로 실패 경로도 검증.</summary>
    private sealed class RecordingAccountService : IAccountService
    {
        public (string id, string? currentPin, string newPin)? SetOwnPinCall { get; private set; }
        public Exception? SetOwnPinThrows { get; set; }

        public Task SetOwnPinAsync(string id, string? currentPin, string newPin, CancellationToken ct = default)
        {
            if (SetOwnPinThrows is not null) throw SetOwnPinThrows;
            SetOwnPinCall = (id, currentPin, newPin);
            return Task.CompletedTask;
        }

        public Task<User?> LoginWithGoogleAsync(string code, string codeVerifier, string redirectUri, string? nonce = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<User>> GetAllAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<User>>(Array.Empty<User>());
        public Task DeleteAsync(string id, CancellationToken ct = default) => Task.CompletedTask;
        public Task SetRoleAsync(string id, UserRole role, CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> VerifyPinAsync(string id, string pin, CancellationToken ct = default) => Task.FromResult(true);
        public Task ResetPinAsync(string targetId, string newPin, CancellationToken ct = default) => Task.CompletedTask;
    }

    private static AppSettings Backend()
    {
        var s = new AppSettings { BackendBaseUrl = "https://backend.test/api", BackendApiKey = "key" };
        s.Clamp();
        return s;
    }

    private static (AccountViewModel vm, RecordingAccountService accounts, FakePinPromptDialogService pin, AppShellViewModel shell)
        MakeVm(User? loginUser, bool pinDialogRegistered = true, bool pinDialogResult = true)
    {
        var settings = new StubSettingsService(Backend());
        var session = new SessionContext();
        if (loginUser is not null) session.Login(loginUser);

        var accounts = new RecordingAccountService();
        var pin = new FakePinPromptDialogService { Result = pinDialogResult };
        var services = new MapServiceProvider().Add<IAccountService>(accounts);
        if (pinDialogRegistered) services.Add<IPinPromptDialogService>(pin);

        var shell = new AppShellViewModel(new IdleWatchdog(), settings, services, session);
        // 진입 취소 시 ReturnFromOverlay가 Home으로 복귀하므로 HomeViewModel 해석이 필요하다(셸 순환 → 지연 생성).
        services.AddFactory<HomeViewModel>(() => new HomeViewModel(shell));

        var vm = new AccountViewModel(shell, accounts, new NullTempUserLimitsService()) { Mode = AccountMode.Account };
        return (vm, accounts, pin, shell);
    }

    private static User Google(bool hasPin) =>
        new() { Id = "g", Role = UserRole.User, AuthMethod = AuthMethod.Google, HasPin = hasPin };

    // ── 계정 정보 표기 ──

    [Fact]
    public async Task Google_Account_Shows_Sso_Label_And_HasPin()
    {
        var (vm, _, _, _) = MakeVm(Google(hasPin: true));
        await vm.OnEnterAsync();
        Assert.True(vm.HasPin);
        Assert.Equal("Google SSO", vm.AuthMethodLabel);
        Assert.Equal("g", vm.AccountId);
    }

    // ── T1·T2·T3: 진입 시 PIN 강제 생성 (it15 §6.3) ──

    [Fact]
    public async Task T1_NoPin_Account_Entry_Prompts_Setup_Exactly_Once()
    {
        var user = Google(hasPin: false);
        var (vm, accounts, pin, _) = MakeVm(user);

        await vm.OnEnterAsync();

        Assert.Equal(1, pin.SetupCount);
        Assert.Equal(0, pin.VerifyCount);          // 미설정이므로 확인 경로는 타지 않는다
        Assert.NotNull(accounts.SetOwnPinCall);
        Assert.Null(accounts.SetOwnPinCall!.Value.currentPin);   // 최초 설정 → 현재 PIN null
        Assert.True(user.HasPin);                  // 세션 로컬 반영
    }

    [Fact]
    public async Task T2_Cancelling_Pin_Setup_Returns_From_Overlay()
    {
        var (vm, accounts, pin, shell) = MakeVm(Google(hasPin: false), pinDialogResult: false);
        // 계정 화면에 진입한 상태를 만든 뒤(복귀 지점 Home) OnEnterAsync를 태운다.
        shell.CurrentState = AppState.Account;

        await vm.OnEnterAsync();

        Assert.Equal(1, pin.SetupCount);
        Assert.Null(accounts.SetOwnPinCall);       // 취소 → 서버 호출 없음
        Assert.Equal(AppState.Home, shell.CurrentState); // 화면에 머물지 않고 복귀
    }

    [Fact]
    public async Task T3_HasPin_Account_Entry_Shows_No_Dialog()
    {
        var (vm, _, pin, _) = MakeVm(Google(hasPin: true));

        await vm.OnEnterAsync();

        Assert.Equal(0, pin.SetupCount);
        Assert.Equal(0, pin.VerifyCount);          // 계정 관리 진입은 "생성 강제"만 — 재확인하지 않는다
    }

    [Fact]
    public async Task Guest_Entry_Does_Not_Prompt_Pin()
    {
        var (vm, _, pin, _) = MakeVm(loginUser: null);

        await vm.OnEnterAsync();

        Assert.Equal(0, pin.SetupCount);
        Assert.Equal(0, pin.VerifyCount);
    }

    // ── 최초 설정(HasPin=false): 현재 PIN 불요 ──

    [Fact]
    public async Task Initial_Setup_Sends_Null_CurrentPin()
    {
        // 진입 게이트를 통과시키지 않기 위해 게이트 통과 후 상태(HasPin=true)가 아닌
        // "게이트에서 설정된 뒤 섹션에서 다시 바꾸는" 경로 대신, 게이트 없이 폼만 검증한다.
        var (vm, accounts, _, _) = MakeVm(Google(hasPin: false), pinDialogRegistered: false, pinDialogResult: false);
        // 다이얼로그 미등록 = fail-closed → OnEnterAsync는 복귀하므로 폼 검증은 진입 없이 수행한다.
        vm.NewPin = "1234";
        vm.ConfirmPin = "1234";
        await vm.ChangePinCommand.ExecuteAsync(null);

        Assert.NotNull(accounts.SetOwnPinCall);
        Assert.Null(accounts.SetOwnPinCall!.Value.currentPin);   // 최초 설정 → 현재 PIN null
        Assert.Equal("1234", accounts.SetOwnPinCall!.Value.newPin);
        Assert.False(vm.PinMessageIsError);
    }

    [Fact]
    public async Task Initial_Setup_Flips_HasPin_True()
    {
        var user = Google(hasPin: false);
        var (vm, _, _, _) = MakeVm(user, pinDialogRegistered: false);
        vm.NewPin = "1234";
        vm.ConfirmPin = "1234";
        await vm.ChangePinCommand.ExecuteAsync(null);

        Assert.True(user.HasPin);   // 로컬 세션 반영(변경 모드로 전환)
        Assert.True(vm.HasPin);
    }

    // ── 변경(HasPin=true): 현재 PIN 전달 ──

    [Fact]
    public async Task Change_Sends_CurrentPin()
    {
        var (vm, accounts, _, _) = MakeVm(Google(hasPin: true));
        await vm.OnEnterAsync();
        vm.CurrentPin = "1111";
        vm.NewPin = "2222";
        vm.ConfirmPin = "2222";
        await vm.ChangePinCommand.ExecuteAsync(null);

        Assert.NotNull(accounts.SetOwnPinCall);
        Assert.Equal("1111", accounts.SetOwnPinCall!.Value.currentPin);
        Assert.Equal("2222", accounts.SetOwnPinCall!.Value.newPin);
    }

    // ── 형식/일치 검증(서버 왕복 전 차단) ──

    [Fact]
    public async Task Invalid_Format_Blocks_Service()
    {
        var (vm, accounts, _, _) = MakeVm(Google(hasPin: false), pinDialogRegistered: false);
        vm.NewPin = "12a";       // 비숫자/길이 위반
        vm.ConfirmPin = "12a";
        await vm.ChangePinCommand.ExecuteAsync(null);

        Assert.Null(accounts.SetOwnPinCall); // 서비스 미호출
        Assert.True(vm.PinMessageIsError);
    }

    [Fact]
    public async Task Mismatch_Blocks_Service()
    {
        var (vm, accounts, _, _) = MakeVm(Google(hasPin: false), pinDialogRegistered: false);
        vm.NewPin = "1234";
        vm.ConfirmPin = "5678";
        await vm.ChangePinCommand.ExecuteAsync(null);

        Assert.Null(accounts.SetOwnPinCall);
        Assert.True(vm.PinMessageIsError);
    }

    [Fact]
    public async Task Change_Invalid_CurrentPin_Blocks_Service()
    {
        var (vm, accounts, _, _) = MakeVm(Google(hasPin: true));
        await vm.OnEnterAsync();
        vm.CurrentPin = "11";    // 형식 위반
        vm.NewPin = "2222";
        vm.ConfirmPin = "2222";
        await vm.ChangePinCommand.ExecuteAsync(null);

        Assert.Null(accounts.SetOwnPinCall);
        Assert.True(vm.PinMessageIsError);
    }

    // ── 서버 거부(현재 PIN 불일치) 우아 처리 ──

    [Fact]
    public async Task Server_Rejects_WrongCurrent_Shows_Error()
    {
        var (vm, accounts, _, _) = MakeVm(Google(hasPin: true));
        await vm.OnEnterAsync();
        accounts.SetOwnPinThrows = new InvalidOperationException("현재 PIN이 올바르지 않습니다.");
        vm.CurrentPin = "0000";
        vm.NewPin = "2222";
        vm.ConfirmPin = "2222";
        await vm.ChangePinCommand.ExecuteAsync(null);

        Assert.True(vm.PinMessageIsError);
    }
}
