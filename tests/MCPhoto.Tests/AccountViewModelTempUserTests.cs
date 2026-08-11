using MCPhoto.App;
using MCPhoto.App.ViewModels;
using MCPhoto.Core.Accounts;
using MCPhoto.Core.Models;
using MCPhoto.Core.Navigation;
using MCPhoto.Core.Settings;

namespace MCPhoto.Tests;

/// <summary>
/// it13 §7.7: 계정 페이지의 Admin 전역 TempUser 한도 로드/저장.
/// it15: 계정 생성 UI가 폐지되어 생성 콤보 케이스는 삭제되고, 백엔드 전용화로 "레거시 모드" 케이스도 사라졌다.
/// 순수 라벨 매핑은 RoleManagementTests.ToLabel_Korean.
/// </summary>
public class AccountViewModelTempUserTests
{
    private sealed class StubSettingsService : ISettingsService
    {
        private readonly AppSettings _settings;
        public StubSettingsService(AppSettings settings) => _settings = settings;
        public AppSettings Current => _settings;
        public string IniPath => System.IO.Path.Combine(System.IO.Path.GetTempPath(), "MCPhoto.ini");
        public AppSettings Load() => _settings;
        public bool Save() => true;
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    /// <summary>최소 계정 서비스(it15 축소 계약 7메서드 — 이 테스트는 호출하지 않는다).</summary>
    private sealed class StubAccountService : IAccountService
    {
        public Task<User?> LoginWithGoogleAsync(string code, string codeVerifier, string redirectUri, string? nonce = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<User>> GetAllAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<User>>(Array.Empty<User>());
        public Task DeleteAsync(string id, CancellationToken ct = default) => Task.CompletedTask;
        public Task SetRoleAsync(string id, UserRole role, CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> VerifyPinAsync(string id, string pin, CancellationToken ct = default) => Task.FromResult(true);
        public Task SetOwnPinAsync(string id, string? currentPin, string newPin, CancellationToken ct = default) => Task.CompletedTask;
        public Task ResetPinAsync(string targetId, string newPin, CancellationToken ct = default) => Task.CompletedTask;
    }

    /// <summary>전역 한도 서비스 fake — 조회값 반환 + 저장 호출 기록.</summary>
    private sealed class RecordingLimitsService : ITempUserLimitsService
    {
        public TempUserLimits Stored;
        public TempUserLimits? Saved { get; private set; }
        public bool ThrowUnauthorizedOnSet { get; set; }
        public RecordingLimitsService(TempUserLimits initial) => Stored = initial;

        public Task<TempUserLimits> GetLimitsAsync(CancellationToken ct = default) => Task.FromResult(Stored);
        public Task SetLimitsAsync(TempUserLimits limits, CancellationToken ct = default)
        {
            if (ThrowUnauthorizedOnSet) throw new UnauthorizedAccessException("no");
            Saved = limits;
            Stored = limits;
            return Task.CompletedTask;
        }
    }

    private static AppSettings Backend()
    {
        var s = new AppSettings { BackendBaseUrl = "https://backend.test/api", BackendApiKey = "key" };
        s.Clamp();
        return s;
    }

    private static (AccountViewModel vm, StubAccountService accounts, RecordingLimitsService limits) MakeVm(
        UserRole? loginRole, AccountMode mode, RecordingLimitsService? limits = null)
    {
        var settings = new StubSettingsService(Backend());
        var session = new SessionContext();
        // HasPin=true: 이 테스트의 관심사는 전역 한도이므로 진입 PIN 게이트(it15 §6.3)를 우회한다.
        if (loginRole is { } r) session.Login(new User { Id = "actor", Role = r, HasPin = true });
        var shell = new AppShellViewModel(new IdleWatchdog(), settings, new EmptyServiceProvider(), session);
        var accounts = new StubAccountService();
        limits ??= new RecordingLimitsService(new TempUserLimits(48, 30));
        var vm = new AccountViewModel(shell, accounts, limits) { Mode = mode };
        return (vm, accounts, limits);
    }

    // ── §7.7: Admin 전역 한도 로드/저장 ──

    [Fact]
    public async Task Admin_Enter_Loads_Current_Limits()
    {
        var limits = new RecordingLimitsService(new TempUserLimits(72, 50));
        var (vm, _, _) = MakeVm(UserRole.Admin, AccountMode.Admin, limits);
        await vm.OnEnterAsync();

        Assert.True(vm.CanEditTempUserLimits);
        Assert.Equal(72, vm.TempUserQrHours);
        Assert.Equal(50, vm.TempUserQrCount);
    }

    [Fact]
    public async Task Admin_Save_Limits_Persists()
    {
        var limits = new RecordingLimitsService(new TempUserLimits(48, 30));
        var (vm, _, _) = MakeVm(UserRole.Admin, AccountMode.Admin, limits);
        await vm.OnEnterAsync();

        vm.TempUserQrHours = 24;
        vm.TempUserQrCount = 10;
        await vm.SaveTempUserLimitsCommand.ExecuteAsync(null);

        Assert.NotNull(limits.Saved);
        Assert.Equal(24, limits.Saved!.QrHours);
        Assert.Equal(10, limits.Saved!.QrCount);
        Assert.False(vm.TempUserLimitsMessageIsError);
    }

    [Fact]
    public async Task Admin_Save_Invalid_Range_Rejected_Client_Side()
    {
        var limits = new RecordingLimitsService(new TempUserLimits(48, 30));
        var (vm, _, _) = MakeVm(UserRole.Admin, AccountMode.Admin, limits);
        await vm.OnEnterAsync();

        vm.TempUserQrHours = 0;   // 1 미만 → 클라 거부(서버 왕복 전)
        await vm.SaveTempUserLimitsCommand.ExecuteAsync(null);

        Assert.Null(limits.Saved);
        Assert.True(vm.TempUserLimitsMessageIsError);
    }

    [Fact]
    public async Task NonAdmin_Cannot_Edit_Limits()
    {
        // Manager(power지만 Admin 아님) → 한도 섹션 미노출.
        var (vm, _, _) = MakeVm(UserRole.Manager, AccountMode.Admin);
        await vm.OnEnterAsync();
        Assert.False(vm.CanEditTempUserLimits);
    }
}
