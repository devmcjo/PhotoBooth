using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MCPhoto.App;
using MCPhoto.App.Services;
using MCPhoto.App.ViewModels;
using MCPhoto.Core.Accounts;
using MCPhoto.Core.Models;
using MCPhoto.Core.Navigation;
using MCPhoto.Core.Settings;

namespace MCPhoto.Tests;

/// <summary>
/// it13 §9.5: 역할 변경 콤보+Apply(§8.7 매트릭스). 행별 지정 가능 역할 필터, Apply의 SetRole 호출·무변경 no-op·
/// 권한 밖 차단·서버 403 우아 처리(안내+목록 원복)를 단위 검증.
/// it15 §6.5: "PW 초기화" 폐지 + PIN 설정 여부 열(PinStateLabel) 추가 + 백엔드 게이트(isBackend) 제거.
/// </summary>
public class UserMgmtViewModelTests
{
    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    /// <summary>SetRoleAsync 호출을 기록하는 계정 서비스. GetAllAsync는 주입 목록을 반환(Reload 검증용).</summary>
    private sealed class SpyAccountService : IAccountService
    {
        public IReadOnlyList<User> Accounts { get; set; } = Array.Empty<User>();
        public bool SetRoleCalled { get; private set; }
        public string? SetRoleId { get; private set; }
        public UserRole? SetRoleValue { get; private set; }
        public int ReloadCount { get; private set; }
        /// <summary>설정 시 SetRoleAsync가 이 예외를 던진다(서버 403 모사 등).</summary>
        public Exception? SetRoleThrows { get; set; }

        public Task SetRoleAsync(string id, UserRole role, CancellationToken ct = default)
        {
            SetRoleCalled = true;
            SetRoleId = id;
            SetRoleValue = role;
            if (SetRoleThrows is not null) throw SetRoleThrows;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<User>> GetAllAsync(CancellationToken ct = default)
        {
            ReloadCount++;
            return Task.FromResult(Accounts);
        }

        public Task<User?> LoginWithGoogleAsync(string code, string codeVerifier, string redirectUri, string? nonce = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteAsync(string id, CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> VerifyPinAsync(string id, string pin, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SetOwnPinAsync(string id, string? currentPin, string newPin, CancellationToken ct = default) => Task.CompletedTask;

        public string? ResetPinId { get; private set; }
        public string? ResetPinValue { get; private set; }
        /// <summary>설정 시 ResetPinAsync가 이 예외를 던진다(서버 403 모사 등).</summary>
        public Exception? ResetPinThrows { get; set; }
        public Task ResetPinAsync(string targetId, string newPin, CancellationToken ct = default)
        {
            ResetPinId = targetId;
            ResetPinValue = newPin;
            if (ResetPinThrows is not null) throw ResetPinThrows;
            return Task.CompletedTask;
        }
    }

    private static async Task<(UserMgmtViewModel vm, SpyAccountService accounts)> MakeVmAsync(
        UserRole actorRole, string actorId, IReadOnlyList<User> accountList)
    {
        var settings = new IniSettingsService(iniPath: Path.Combine(Path.GetTempPath(), $"umvm_{Guid.NewGuid():N}.ini"));
        settings.Load();
        var session = new SessionContext();
        session.Login(new User { Id = actorId, Role = actorRole });
        var shell = new AppShellViewModel(new IdleWatchdog(), settings, new EmptyServiceProvider(), session);
        var accounts = new SpyAccountService { Accounts = accountList };
        var vm = new UserMgmtViewModel(shell, accounts);
        await vm.OnEnterAsync();
        return (vm, accounts);
    }

    private static UserRowViewModel Row(UserMgmtViewModel vm, string id) => vm.Rows.First(r => r.User.Id == id);

    // ── 목록 정렬(관리 편의): 역할 위계 내림차순 → 같은 역할은 가입 시각 오름차순 ──

    [Fact]
    public async Task Rows_Sorted_By_Role_Rank_Desc_Then_CreatedAt_Asc()
    {
        var t = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        // 서버 응답 순서를 의도적으로 뒤섞어 넣는다(GetAllAsync는 정렬을 보장하지 않는다).
        var list = new[]
        {
            new User { Id = "u_new",  Role = UserRole.User,         CreatedAt = t.AddDays(9) },
            new User { Id = "admin",  Role = UserRole.Admin,        CreatedAt = t.AddDays(5) },
            new User { Id = "t1",     Role = UserRole.TempUser,     CreatedAt = t.AddDays(1) },
            new User { Id = "u_old",  Role = UserRole.User,         CreatedAt = t.AddDays(2) },
            new User { Id = "adv",    Role = UserRole.AdvancedUser, CreatedAt = t.AddDays(7) },
            new User { Id = "mgr",    Role = UserRole.Manager,      CreatedAt = t.AddDays(3) },
        };
        var (vm, _) = await MakeVmAsync(UserRole.Admin, "admin", list);

        Assert.Equal(
            new[] { "admin", "mgr", "adv", "u_old", "u_new", "t1" },
            vm.Rows.Select(r => r.User.Id).ToArray());
    }

    /// <summary>같은 역할 안에서는 가입이 늦은 계정이 아래로 온다(가입 시각 오름차순).</summary>
    [Fact]
    public async Task Same_Role_Rows_Put_Recent_Signup_Below()
    {
        var t = new DateTime(2026, 3, 10, 12, 0, 0, DateTimeKind.Utc);
        var list = new[]
        {
            new User { Id = "third",  Role = UserRole.User, CreatedAt = t.AddHours(6) },
            new User { Id = "first",  Role = UserRole.User, CreatedAt = t },
            new User { Id = "second", Role = UserRole.User, CreatedAt = t.AddHours(3) },
        };
        var (vm, _) = await MakeVmAsync(UserRole.Admin, "admin", list);

        Assert.Equal(new[] { "first", "second", "third" }, vm.Rows.Select(r => r.User.Id).ToArray());
    }

    /// <summary>가입 일시는 로컬 시간으로 표시한다(서버 createdAt은 UTC).</summary>
    [Fact]
    public async Task CreatedAt_Text_Uses_Local_Time()
    {
        var utc = new DateTime(2026, 5, 20, 23, 30, 0, DateTimeKind.Utc);
        var list = new[] { new User { Id = "u1", Role = UserRole.User, CreatedAt = utc } };
        var (vm, _) = await MakeVmAsync(UserRole.Admin, "admin", list);

        var local = utc.ToLocalTime();
        Assert.Equal(local.ToString("yyyy-MM-dd"), Row(vm, "u1").CreatedDateText);
        Assert.Equal(local.ToString("HH:mm"), Row(vm, "u1").CreatedTimeText);
    }

    /// <summary>역할별 인원 요약은 위계 높은 역할부터 나열하고, 0명 역할은 싣지 않는다.</summary>
    [Fact]
    public async Task SummaryText_Lists_Roles_High_To_Low()
    {
        var list = new[]
        {
            new User { Id = "admin", Role = UserRole.Admin },
            new User { Id = "u1", Role = UserRole.User },
            new User { Id = "u2", Role = UserRole.User },
            new User { Id = "t1", Role = UserRole.TempUser },
        };
        var (vm, _) = await MakeVmAsync(UserRole.Admin, "admin", list);

        Assert.Equal("총 4명 · 관리자 1 · 사용자 2 · 임시 유저 1", vm.SummaryText);
        Assert.DoesNotContain("매니저", vm.SummaryText);   // 0명 역할은 생략
        Assert.False(vm.IsEmpty);
    }

    [Fact]
    public async Task Empty_List_Sets_IsEmpty_And_Clears_Summary()
    {
        var (vm, _) = await MakeVmAsync(UserRole.Admin, "admin", Array.Empty<User>());

        Assert.True(vm.IsEmpty);
        Assert.Equal(string.Empty, vm.SummaryText);
    }

    /// <summary>실패 안내는 오류 색으로, 성공 안내는 성공 색으로(StatusIsError). 초록 오류 메시지 방지.</summary>
    [Fact]
    public async Task StatusIsError_Flags_Failure_But_Not_Success()
    {
        var list = new[] { new User { Id = "u1", Role = UserRole.User } };
        var (vm, accounts) = await MakeVmAsync(UserRole.Admin, "admin", list);

        var row = Row(vm, "u1");
        row.SelectedRole = UserRole.Manager;
        await vm.ApplyRoleChangeCommand.ExecuteAsync(row);
        Assert.False(vm.StatusIsError);   // 성공

        accounts.SetRoleThrows = new UnauthorizedAccessException("forbidden");
        row = Row(vm, "u1");
        row.SelectedRole = UserRole.TempUser;
        await vm.ApplyRoleChangeCommand.ExecuteAsync(row);
        Assert.True(vm.StatusIsError);    // 403
    }

    // ── 행별 지정 가능 역할(콤보 옵션) 필터 ──

    [Fact]
    public async Task Admin_Rows_Offer_All_Except_Admin_And_Self()
    {
        var list = new[]
        {
            new User { Id = "admin", Role = UserRole.Admin },   // 자기 계정
            new User { Id = "u1", Role = UserRole.User },
            new User { Id = "m1", Role = UserRole.Manager },
            new User { Id = "t1", Role = UserRole.TempUser },
            new User { Id = "a1", Role = UserRole.AdvancedUser },   // it16
            new User { Id = "otherAdmin", Role = UserRole.Admin },
        };
        var (vm, _) = await MakeVmAsync(UserRole.Admin, "admin", list);

        Assert.False(Row(vm, "admin").CanChangeRole);        // 자기 계정 미노출
        Assert.False(Row(vm, "otherAdmin").CanChangeRole);   // admin 대상 미노출
        // it16: 콤보 목록에 AdvancedUser 추가(위계 오름차순).
        var all = new[] { UserRole.TempUser, UserRole.User, UserRole.AdvancedUser, UserRole.Manager };
        Assert.Equal(all, Row(vm, "u1").AssignableRoles);
        Assert.Equal(all, Row(vm, "m1").AssignableRoles);
        Assert.Equal(all, Row(vm, "t1").AssignableRoles);
        Assert.Equal(all, Row(vm, "a1").AssignableRoles);
    }

    /// <summary>
    /// it16 §8.2-24·25(E3): manager는 하위 3역할 대역(temp_user·user·advanced_user) 행에서 콤보가 노출되고
    /// 대역 내 자유 지정(승격 포함)이 가능하다. manager·admin 행은 여전히 미노출.
    /// (it13의 "manager는 user→temp_user 강등만" 규칙이 이 이터레이션에서 완화됐다)
    /// </summary>
    [Fact]
    public async Task Manager_Rows_Lower_Band_Offers_Free_Assign()
    {
        var list = new[]
        {
            new User { Id = "u1", Role = UserRole.User },
            new User { Id = "t1", Role = UserRole.TempUser },
            new User { Id = "a1", Role = UserRole.AdvancedUser },
            new User { Id = "m2", Role = UserRole.Manager },
            new User { Id = "ad1", Role = UserRole.Admin },
        };
        var (vm, _) = await MakeVmAsync(UserRole.Manager, "mgrSelf", list);

        var band = new[] { UserRole.TempUser, UserRole.User, UserRole.AdvancedUser };
        Assert.Equal(band, Row(vm, "u1").AssignableRoles);
        Assert.Equal(band, Row(vm, "t1").AssignableRoles);   // it16: temp_user 행도 콤보 노출(승격 허용)
        Assert.Equal(band, Row(vm, "a1").AssignableRoles);
        Assert.True(Row(vm, "t1").CanChangeRole);
        Assert.True(Row(vm, "a1").CanChangeRole);
        Assert.False(Row(vm, "m2").CanChangeRole);   // manager 대상 미노출(manager 지정·강등은 admin 전용)
        Assert.False(Row(vm, "ad1").CanChangeRole);  // admin 대상 미노출
    }

    // ── Apply 동작 ──

    [Fact]
    public async Task Apply_User_To_TempUser_Calls_SetRole()
    {
        var list = new[] { new User { Id = "u1", Role = UserRole.User } };
        var (vm, accounts) = await MakeVmAsync(UserRole.Admin, "admin", list);
        var row = Row(vm, "u1");
        row.SelectedRole = UserRole.TempUser;

        await vm.ApplyRoleChangeCommand.ExecuteAsync(row);

        Assert.True(accounts.SetRoleCalled);
        Assert.Equal("u1", accounts.SetRoleId);
        Assert.Equal(UserRole.TempUser, accounts.SetRoleValue);
    }

    [Fact]
    public async Task Apply_No_Change_Is_NoOp()
    {
        var list = new[] { new User { Id = "u1", Role = UserRole.User } };
        var (vm, accounts) = await MakeVmAsync(UserRole.Admin, "admin", list);
        var row = Row(vm, "u1");
        // SelectedRole == 현재 역할(User) → 무변경 no-op.
        await vm.ApplyRoleChangeCommand.ExecuteAsync(row);
        Assert.False(accounts.SetRoleCalled);
    }

    [Fact]
    public async Task Apply_Beyond_Matrix_Blocked_Client_Side()
    {
        // manager가 user를 manager로 승격 시도(매트릭스 밖 — manager 지정은 admin 전용) → 클라 차단, SetRole 미호출.
        // ⚠️ it16 E3로 manager의 하위 3역할 대역 내 승격(temp_user→user 등)은 **허용**으로 반전됐다.
        //    따라서 이 테스트의 "매트릭스 밖" 케이스를 manager 지정으로 교체했다(설계 §3.3 변경점 표).
        var list = new[] { new User { Id = "u1", Role = UserRole.User } };
        var (vm, accounts) = await MakeVmAsync(UserRole.Manager, "mgrSelf", list);
        var row = Row(vm, "u1");
        row.SelectedRole = UserRole.Manager;   // manager 지정(admin 전용)

        await vm.ApplyRoleChangeCommand.ExecuteAsync(row);

        Assert.False(accounts.SetRoleCalled);
        Assert.Equal("해당 역할로 변경할 권한이 없습니다.", vm.StatusMessage);
    }

    /// <summary>it16 §8.2-27: AdvancedUser 지정이 서버로 전달되고 성공 토스트에 "고급 유저" 라벨이 실린다.</summary>
    [Fact]
    public async Task Apply_To_AdvancedUser_Calls_SetRole_And_Labels_Toast()
    {
        var list = new[] { new User { Id = "u1", Role = UserRole.User } };
        var (vm, accounts) = await MakeVmAsync(UserRole.Manager, "mgrSelf", list);
        var row = Row(vm, "u1");
        row.SelectedRole = UserRole.AdvancedUser;   // it16 E3: manager도 대역 내 승격 가능

        await vm.ApplyRoleChangeCommand.ExecuteAsync(row);

        Assert.True(accounts.SetRoleCalled);
        Assert.Equal("u1", accounts.SetRoleId);
        Assert.Equal(UserRole.AdvancedUser, accounts.SetRoleValue);
        Assert.Contains("고급 유저", vm.StatusMessage);
    }

    [Fact]
    public async Task Apply_Server_403_Handled_Gracefully_And_Reloads()
    {
        var list = new[] { new User { Id = "u1", Role = UserRole.User } };
        var (vm, accounts) = await MakeVmAsync(UserRole.Admin, "admin", list);
        accounts.SetRoleThrows = new UnauthorizedAccessException("forbidden");
        var reloadsBefore = accounts.ReloadCount;

        var row = Row(vm, "u1");
        row.SelectedRole = UserRole.Manager;
        await vm.ApplyRoleChangeCommand.ExecuteAsync(row);

        Assert.True(accounts.SetRoleCalled);
        Assert.Equal("역할을 변경할 권한이 없습니다.", vm.StatusMessage);
        Assert.True(accounts.ReloadCount > reloadsBefore);   // 목록 원복(재로드)
    }

    [Fact]
    public async Task Apply_Self_Row_Blocked()
    {
        // 자기 계정은 행 래퍼가 빈 목록이라 UI 미노출이지만, 커맨드 직접 호출 시에도 이중 방어.
        var list = new[] { new User { Id = "admin", Role = UserRole.Admin } };
        var (vm, accounts) = await MakeVmAsync(UserRole.Admin, "admin", list);
        var row = Row(vm, "admin");
        row.SelectedRole = UserRole.Manager;

        await vm.ApplyRoleChangeCommand.ExecuteAsync(row);
        Assert.False(accounts.SetRoleCalled);
    }

    // ── it14 §6.2: 타 계정 PIN 재설정 ──

    /// <summary>PIN 설정 다이얼로그를 스텁: setAsync를 즉시 실행하고 지정한 결과를 반환.</summary>
    private sealed class StubPinPromptDialogService : IPinPromptDialogService
    {
        public bool SetupResult { get; set; } = true;
        public bool SetupCalled { get; private set; }
        public bool PromptVerify(Func<string, Task<bool>> verifyAsync) => throw new NotSupportedException();
        public bool PromptSetup(Func<string, Task> setAsync)
        {
            SetupCalled = true;
            // 관리자가 새 PIN "9999"를 입력한 것으로 모사(다이얼로그가 setAsync 호출).
            setAsync("9999").GetAwaiter().GetResult();
            return SetupResult;
        }
    }

    /// <summary>백엔드 ON + PIN 다이얼로그 주입 VM(PIN 재설정 검증용).</summary>
    private static async Task<(UserMgmtViewModel vm, SpyAccountService accounts, StubPinPromptDialogService pin)> MakePinVmAsync(
        UserRole actorRole, string actorId, IReadOnlyList<User> accountList, bool setupResult = true)
    {
        var settings = new IniSettingsService(iniPath: Path.Combine(Path.GetTempPath(), $"umpin_{Guid.NewGuid():N}.ini"));
        var loaded = settings.Load();
        loaded.BackendBaseUrl = "https://backend.test/api";
        loaded.BackendApiKey = "key";
        loaded.Clamp();
        var session = new SessionContext();
        session.Login(new User { Id = actorId, Role = actorRole });
        var shell = new AppShellViewModel(new IdleWatchdog(), settings, new EmptyServiceProvider(), session);
        var accounts = new SpyAccountService { Accounts = accountList };
        var pin = new StubPinPromptDialogService { SetupResult = setupResult };
        var vm = new UserMgmtViewModel(shell, accounts, logger: null, pinPrompt: pin);
        await vm.OnEnterAsync();
        return (vm, accounts, pin);
    }

    [Fact]
    public async Task CanResetPin_Excludes_Self_And_Same_Rank_But_Allows_Lower()
    {
        var list = new[]
        {
            new User { Id = "admin", Role = UserRole.Admin },   // 자기 계정 → 미노출
            new User { Id = "u1", Role = UserRole.User },
            new User { Id = "m1", Role = UserRole.Manager },
            new User { Id = "otherAdmin", Role = UserRole.Admin }, // 동급 → 미노출(CanResetPin은 엄격히 낮은 위계)
        };
        var (vm, _, _) = await MakePinVmAsync(UserRole.Admin, "admin", list);

        Assert.False(Row(vm, "admin").CanResetPin);        // 자기 계정(본인 PIN은 AccountView에서 변경)
        Assert.False(Row(vm, "otherAdmin").CanResetPin);   // 동급(admin↔admin) 차단
        Assert.True(Row(vm, "m1").CanResetPin);            // 매니저 PIN은 관리자만 재설정
        Assert.True(Row(vm, "u1").CanResetPin);            // 하위
    }

    /// <summary>
    /// 매니저는 **다른 매니저의 PIN을 재설정할 수 없다**(동급 차단 — 매니저 PIN은 관리자 전용).
    /// 종전 판정 `IsPower() && CanManage(target)`는 CanManage가 동급을 허용해 이 행에 버튼을 노출했다.
    /// </summary>
    [Fact]
    public async Task CanResetPin_False_For_Same_Rank_Manager_Target()
    {
        var list = new[]
        {
            new User { Id = "mgr", Role = UserRole.Manager },   // 자기 계정
            new User { Id = "m2", Role = UserRole.Manager },    // 동급 → 미노출
            new User { Id = "a1", Role = UserRole.AdvancedUser },
        };
        var (vm, _, _) = await MakePinVmAsync(UserRole.Manager, "mgr", list);

        Assert.False(Row(vm, "m2").CanResetPin);   // 동급 매니저
        Assert.True(Row(vm, "a1").CanResetPin);    // 하위는 여전히 가능
    }

    /// <summary>동급 매니저 행은 UI 미노출이지만, 커맨드 직접 호출도 가드로 차단한다(이중 방어).</summary>
    [Fact]
    public async Task ResetUserPin_Blocked_For_Same_Rank_Manager_Target()
    {
        var list = new[] { new User { Id = "m2", Role = UserRole.Manager } };
        var (vm, accounts, pin) = await MakePinVmAsync(UserRole.Manager, "mgr", list);
        var row = Row(vm, "m2");

        vm.ResetUserPinCommand.Execute(row);

        Assert.False(pin.SetupCalled);
        Assert.Null(accounts.ResetPinId);
        Assert.Equal("동급·상위 역할 계정의 PIN은 재설정할 수 없습니다.", vm.StatusMessage);
    }

    /// <summary>관리자는 매니저 PIN을 재설정할 수 있다(요구사항의 유일한 허용 경로).</summary>
    [Fact]
    public async Task ResetUserPin_Admin_Can_Reset_Manager_Pin()
    {
        var list = new[] { new User { Id = "m1", Role = UserRole.Manager } };
        var (vm, accounts, pin) = await MakePinVmAsync(UserRole.Admin, "admin", list);

        vm.ResetUserPinCommand.Execute(Row(vm, "m1"));

        Assert.True(pin.SetupCalled);
        Assert.Equal("m1", accounts.ResetPinId);
        Assert.Equal("9999", accounts.ResetPinValue);
    }

    [Fact]
    public async Task CanResetPin_False_For_Higher_Role_Target()
    {
        // manager는 admin을 관리 불가(CanManage(manager,admin)=false) → PIN 재설정 UI 미노출.
        var list = new[]
        {
            new User { Id = "mgr", Role = UserRole.Manager },   // 자기 계정
            new User { Id = "a1", Role = UserRole.Admin },      // 상위 → 미노출
            new User { Id = "u1", Role = UserRole.User },
        };
        var (vm, _, _) = await MakePinVmAsync(UserRole.Manager, "mgr", list);

        Assert.False(Row(vm, "a1").CanResetPin);   // 상위 역할
        Assert.True(Row(vm, "u1").CanResetPin);    // 하위 관리 가능
    }

    /// <summary>
    /// it16 §8.2-26·§3.5: 비power actor는 PIN 재설정 UI가 **전부 미노출**이다(가정 A6 검증).
    /// CanManage만 보던 종전 판정은 advanced_user가 user·temp_user의 PIN을, user가 다른 user의 PIN을
    /// 만질 수 있게 했다 — 서버 requirePower()와 대칭으로 power 항을 추가해 모집단 전체를 차단한다.
    /// </summary>
    [Theory]
    [InlineData(UserRole.AdvancedUser)]
    [InlineData(UserRole.User)]
    [InlineData(UserRole.TempUser)]
    public async Task CanResetPin_False_For_NonPower_Actor(UserRole actorRole)
    {
        var list = new[]
        {
            new User { Id = "self", Role = actorRole },
            new User { Id = "t1", Role = UserRole.TempUser },   // 위계상 하위 → 종전이면 노출됐다
            new User { Id = "u1", Role = UserRole.User },
        };
        var (vm, _, _) = await MakePinVmAsync(actorRole, "self", list);

        Assert.All(vm.Rows, r => Assert.False(r.CanResetPin));
    }

    /// <summary>it16 §3.5: 커맨드 직접 호출도 power 가드로 차단(UI 미노출·커맨드 가드·서버 3중 방어).</summary>
    [Fact]
    public async Task ResetUserPin_Blocked_For_NonPower_Actor()
    {
        var list = new[] { new User { Id = "t1", Role = UserRole.TempUser } };
        var (vm, accounts, pin) = await MakePinVmAsync(UserRole.AdvancedUser, "adv", list);
        var row = Row(vm, "t1");

        vm.ResetUserPinCommand.Execute(row);

        Assert.False(pin.SetupCalled);
        Assert.Null(accounts.ResetPinId);
        Assert.Equal("동급·상위 역할 계정의 PIN은 재설정할 수 없습니다.", vm.StatusMessage);
    }

    // ── it15 §6.5 T7: PIN 설정 여부 열 ──

    [Fact]
    public async Task T7_PinStateLabel_Reflects_HasPin()
    {
        var list = new[]
        {
            new User { Id = "withPin", Role = UserRole.User, HasPin = true },
            new User { Id = "noPin", Role = UserRole.User, HasPin = false },
        };
        var (vm, _) = await MakeVmAsync(UserRole.Admin, "admin", list);

        Assert.Equal("설정됨", Row(vm, "withPin").PinStateLabel);
        Assert.Equal("미설정", Row(vm, "noPin").PinStateLabel);
    }

    [Fact]
    public async Task ResetUserPin_Success_Calls_ResetPin_And_Sets_Message()
    {
        var list = new[] { new User { Id = "u1", Role = UserRole.User } };
        var (vm, accounts, pin) = await MakePinVmAsync(UserRole.Admin, "admin", list);
        var row = Row(vm, "u1");

        vm.ResetUserPinCommand.Execute(row);

        Assert.True(pin.SetupCalled);
        Assert.Equal("u1", accounts.ResetPinId);
        Assert.Equal("9999", accounts.ResetPinValue);
        Assert.Contains("PIN", vm.StatusMessage);
    }

    [Fact]
    public async Task ResetUserPin_Dialog_Cancelled_No_Message()
    {
        var list = new[] { new User { Id = "u1", Role = UserRole.User } };
        var (vm, accounts, _) = await MakePinVmAsync(UserRole.Admin, "admin", list, setupResult: false);
        var row = Row(vm, "u1");

        vm.ResetUserPinCommand.Execute(row);

        // 다이얼로그가 setAsync를 실행하지만 취소(false) → 상태 메시지 없음(성공 표기 안 함).
        Assert.Equal(string.Empty, vm.StatusMessage);
    }

    [Fact]
    public async Task ResetUserPin_Blocked_When_Target_Higher_Role()
    {
        // manager가 admin PIN 재설정 시도 → CanResetPin 위반으로 차단(다이얼로그 미표시).
        var list = new[] { new User { Id = "admin", Role = UserRole.Admin } };
        var (vm, accounts, pin) = await MakePinVmAsync(UserRole.Manager, "mgr", list);
        var row = Row(vm, "admin");

        vm.ResetUserPinCommand.Execute(row);

        Assert.False(pin.SetupCalled);
        Assert.Null(accounts.ResetPinId);
        Assert.Equal("동급·상위 역할 계정의 PIN은 재설정할 수 없습니다.", vm.StatusMessage);
    }
}
