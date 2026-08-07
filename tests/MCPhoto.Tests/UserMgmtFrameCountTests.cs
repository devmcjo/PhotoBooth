using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MCPhoto.App;
using MCPhoto.App.Services;
using MCPhoto.App.ViewModels;
using MCPhoto.Core.Accounts;
using MCPhoto.Core.Backend;
using MCPhoto.Core.Frames;
using MCPhoto.Core.Models;
using MCPhoto.Core.Navigation;
using MCPhoto.Core.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace MCPhoto.Tests;

/// <summary>
/// 사용자 관리 화면의 <b>계정별 개인 프레임 개수</b> 표시(정보성·읽기 전용).
/// 설계: docs/design/wpf-usermgmt-frame-count-design.md
///
/// 고정하는 계약 4가지 —
///   ① 목록 로드를 막지 않는다(행이 먼저 그려지고 개수는 뒤이어 채워진다)
///   ② 조회는 순차(동시 발사 금지) + 취소 가능(이탈·새로고침)
///   ③ 개별 행 실패는 조용히(행은 "—", StatusMessage 불변) + 가망 없으면 조기 포기
///   ④ 저장소 미주입이면 기능만 꺼진다(fail-soft)
///
/// 시간 기반 대기(Task.Delay·폴링)를 쓰지 않는다 — TaskCompletionSource 게이트와
/// <see cref="UserMgmtViewModel.FrameCountLoadTask"/>로 결정적으로 제어한다.
/// </summary>
public class UserMgmtFrameCountTests
{
    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    /// <summary>계정별 프레임 개수를 흉내내는 저장소. 호출 순서·동시성·취소 토큰을 기록한다.</summary>
    private sealed class SpyFrameRepository : IFrameRepository
    {
        /// <summary>userId → 반환할 프레임 개수(미등록 계정은 0개).</summary>
        public Dictionary<string, int> Counts { get; } = new(StringComparer.Ordinal);
        /// <summary>특정 계정만 실패시킨다(산발적 실패 모사).</summary>
        public Dictionary<string, Exception> Throws { get; } = new(StringComparer.Ordinal);
        /// <summary>전 계정 실패(오프라인·인증 만료 모사).</summary>
        public Exception? ThrowsAlways { get; set; }
        /// <summary>조회 순서(행 표시 순서와 같아야 한다).</summary>
        public List<string> Queried { get; } = new();
        /// <summary>각 호출이 받은 취소 토큰(취소 전파 검증용).</summary>
        public List<CancellationToken> Tokens { get; } = new();
        /// <summary>열릴 때까지 조회를 붙잡아 둔다(진행 중 상태를 결정적으로 관측하기 위함).</summary>
        public TaskCompletionSource<bool>? Gate { get; set; }
        /// <summary>동시 진행 최대치. 순차 계약이 지켜지면 1이다.</summary>
        public int MaxConcurrent { get { lock (_gate) return _maxConcurrent; } }
        private int _inFlight;
        private int _maxConcurrent;
        private readonly object _gate = new();

        public async Task<IReadOnlyList<FrameTemplate>> GetUserFramesAsync(string userId, CancellationToken ct = default)
        {
            var now = Interlocked.Increment(ref _inFlight);
            lock (_gate) { if (now > _maxConcurrent) _maxConcurrent = now; }
            try
            {
                Queried.Add(userId);
                Tokens.Add(ct);

                // ⚠️ 이 Yield가 없으면 MaxConcurrent 단언이 **공허해진다**.
                // Gate가 없는 경우 이 메서드에는 실제 await 지점이 없어 동기적으로 완주하고,
                // 그러면 구현이 Task.WhenAll로 병렬화돼 있어도 각 호출이 다음 호출 전에 끝나
                // _inFlight가 절대 1을 넘지 않는다 — 순차가 아닌 구현도 통과한다.
                // 여기서 한 번 양보하면 병렬 구현은 N개가 모두 in-flight 상태로 겹쳐 즉시 드러난다.
                await Task.Yield();

                if (Gate is not null) await Gate.Task.WaitAsync(ct);   // 취소되면 OperationCanceledException
                ct.ThrowIfCancellationRequested();
                if (ThrowsAlways is not null) throw ThrowsAlways;
                if (Throws.TryGetValue(userId, out var ex)) throw ex;
                var n = Counts.TryGetValue(userId, out var c) ? c : 0;
                return Enumerable.Range(0, n)
                    .Select(i => new FrameTemplate { Id = $"{userId}_f{i}", Name = $"f{i}", UserId = userId })
                    .ToList();
            }
            finally { Interlocked.Decrement(ref _inFlight); }
        }

        // 나머지 멤버는 이 화면이 쓰지 않는다 — 호출되면 설계 위반이므로 즉시 실패시킨다.
        public Task<IReadOnlyList<FrameTemplate>> GetDefaultFramesAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<FrameTemplate> SaveAsync(FrameTemplate f, byte[] png, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<FrameTemplate> SaveMineAsync(FrameTemplate f, byte[] png, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(string frameId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteAllByUserAsync(string userId, CancellationToken ct = default) => throw new NotSupportedException();
    }

    /// <summary>목록만 돌려주는 최소 계정 서비스(이 화면의 개수 기능은 계정 변경을 하지 않는다).</summary>
    private sealed class StubAccountService : IAccountService
    {
        private readonly IReadOnlyList<User> _accounts;
        public StubAccountService(IReadOnlyList<User> accounts) => _accounts = accounts;

        public Task<IReadOnlyList<User>> GetAllAsync(CancellationToken ct = default) => Task.FromResult(_accounts);

        public Task<User?> LoginWithGoogleAsync(string code, string codeVerifier, string redirectUri, string? nonce = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteAsync(string id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SetRoleAsync(string id, UserRole role, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> VerifyPinAsync(string id, string pin, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SetOwnPinAsync(string id, string? currentPin, string newPin, CancellationToken ct = default) => throw new NotSupportedException();
        public Task ResetPinAsync(string targetId, string newPin, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private static AppShellViewModel MakeShell()
    {
        var settings = new IniSettingsService(iniPath: Path.Combine(Path.GetTempPath(), $"umfc_{Guid.NewGuid():N}.ini"));
        settings.Load();
        var session = new SessionContext();
        session.Login(new User { Id = "admin", Role = UserRole.Admin });
        return new AppShellViewModel(new IdleWatchdog(), settings, new EmptyServiceProvider(), session);
    }

    private static async Task<UserMgmtViewModel> MakeVmAsync(
        IReadOnlyList<User> accounts, SpyFrameRepository? frames, bool enter = true)
    {
        var vm = new UserMgmtViewModel(MakeShell(), new StubAccountService(accounts),
            logger: null, pinPrompt: null, frames: frames);
        if (enter) await vm.OnEnterAsync();
        return vm;
    }

    /// <summary>같은 역할 + 가입 시각 오름차순 → 표시 순서가 u1, u2, … 로 고정된다(정렬 규칙은 기존 그대로).</summary>
    private static User[] Users(int count)
    {
        var t = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);
        return Enumerable.Range(1, count)
            .Select(i => new User { Id = $"u{i}", Role = UserRole.User, CreatedAt = t.AddMinutes(i) })
            .ToArray();
    }

    private static string[] Texts(UserMgmtViewModel vm) => vm.Rows.Select(r => r.FrameCountText).ToArray();

    // ── T1: 요구 1 — 목록 로드가 개수 조회를 기다리지 않는다 ──

    /// <summary>
    /// 행은 즉시 그려지고 개수는 뒤이어 채워진다. 개수 조회를 <c>OnEnterAsync</c> 안에서 await하면
    /// N회 HTTP 동안 [사용자 관리] 버튼이 잠긴다 — "행 채워짐 + 조회 미완료"를 같은 시점에 단언해 고정한다.
    /// </summary>
    [Fact]
    public async Task Rows_Are_Populated_Before_Frame_Counts_Complete()
    {
        var frames = new SpyFrameRepository
        {
            Gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously),
        };
        frames.Counts["u1"] = 1;
        frames.Counts["u2"] = 2;
        frames.Counts["u3"] = 3;

        var vm = await MakeVmAsync(Users(3), frames);

        Assert.Equal(3, vm.Rows.Count);                       // 목록은 이미 완성됐고
        Assert.Equal(new[] { "—", "—", "—" }, Texts(vm));     // 개수는 아직 모른다
        Assert.False(vm.FrameCountLoadTask.IsCompleted);      // 조회는 진행 중(목록을 막지 않았다)

        frames.Gate!.SetResult(true);
        await vm.FrameCountLoadTask;

        Assert.Equal(new[] { "1", "2", "3" }, Texts(vm));
    }

    // ── T2: 요구 2 — 성공 시 개수가 그대로 반영된다 ──

    /// <summary>0개는 반드시 "0"이다 — 미조회("—")와 명확히 구분돼야 관리자가 잘못 읽지 않는다.</summary>
    [Fact]
    public async Task Frame_Counts_Are_Applied_On_Success()
    {
        var frames = new SpyFrameRepository();
        frames.Counts["u1"] = 3;
        frames.Counts["u2"] = 0;
        frames.Counts["u3"] = 10;

        var vm = await MakeVmAsync(Users(3), frames);
        await vm.FrameCountLoadTask;

        Assert.Equal(new[] { "3", "0", "10" }, Texts(vm));
        Assert.Equal(new int?[] { 3, 0, 10 }, vm.Rows.Select(r => r.FrameCount).ToArray());
        Assert.Equal(3, frames.Queried.Count);
    }

    // ── T3: 요구 3 — 개별 행 실패는 조용히, 화면은 멀쩡하다 ──

    /// <summary>
    /// 한 계정 조회가 실패해도 그 행만 "—"로 남고 나머지는 정상 표시된다.
    /// 사용자에게는 아무것도 알리지 않는다(StatusMessage·StatusIsError 불변) — 관리 화면이 막히면 안 된다.
    /// </summary>
    [Fact]
    public async Task One_Account_Failure_Keeps_Other_Rows_And_Screen_Intact()
    {
        var frames = new SpyFrameRepository();
        frames.Counts["u1"] = 2;
        frames.Counts["u3"] = 1;
        frames.Throws["u2"] = new BackendUnavailableException("offline");

        var vm = await MakeVmAsync(Users(3), frames);
        await vm.FrameCountLoadTask;

        Assert.Equal(new[] { "2", "—", "1" }, Texts(vm));
        Assert.Equal(3, vm.Rows.Count);
        Assert.Equal(string.Empty, vm.StatusMessage);
        Assert.False(vm.StatusIsError);
        Assert.False(vm.IsEmpty);
    }

    // ── T4: 요구 4 — 재진입 안전(새로고침이 이전 조회를 취소하고 다시 시작) ──

    [Fact]
    public async Task Refresh_Cancels_Previous_Frame_Count_Load()
    {
        var frames = new SpyFrameRepository
        {
            Gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously),
        };

        var vm = await MakeVmAsync(Users(3), frames);
        var first = vm.FrameCountLoadTask;
        Assert.False(first.IsCompleted);

        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.True(frames.Tokens[0].IsCancellationRequested);   // 이전 조회에 취소가 전파됐다
        Assert.NotSame(first, vm.FrameCountLoadTask);            // 새 조회가 시작됐다
        await first;                                             // 이전 조회는 예외 없이 끝난다

        // 정리: 두 번째 조회도 게이트에 걸려 있으므로 열어서 마무리한다.
        frames.Gate!.SetResult(true);
        await vm.FrameCountLoadTask;
    }

    // ── T5: 제약 2 — 순차 조회(동시 발사 금지) ──

    /// <summary>계정 수만큼 요청이 나가므로 병렬은 서버·토큰 갱신에 부하 스파이크를 만든다.</summary>
    [Fact]
    public async Task Frame_Count_Queries_Are_Sequential()
    {
        var frames = new SpyFrameRepository();
        var vm = await MakeVmAsync(Users(5), frames);
        await vm.FrameCountLoadTask;

        Assert.Equal(1, frames.MaxConcurrent);
        Assert.Equal(vm.Rows.Select(r => r.User.Id).ToArray(), frames.Queried.ToArray());
    }

    // ── T6: 제약 2 — 화면 이탈이 진행 중 조회를 취소한다 ──

    /// <summary>이탈 후에도 루프가 계속 돌면 폐기된 VM을 붙잡고 서버 요청을 이어 간다.</summary>
    [Fact]
    public async Task Leaving_Screen_Cancels_Frame_Count_Load()
    {
        var frames = new SpyFrameRepository
        {
            Gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously),
        };

        var vm = await MakeVmAsync(Users(3), frames);
        await vm.OnLeaveAsync();
        await vm.FrameCountLoadTask;      // 예외 없이 끝난다(취소는 정상 종료다)

        Assert.True(frames.Tokens[0].IsCancellationRequested);
        Assert.Single(frames.Queried);    // 남은 계정은 조회하지 않았다
    }

    // ── T7·T8: D-5 — 가망 없는 실패는 조기 포기(백그라운드 루프 수십 분 방지) ──

    /// <summary>
    /// 서버가 죽은 채로 전 계정을 돌면 HttpClient.Timeout(100초) × 계정 수만큼 루프가 살아 있게 된다.
    /// 결과는 어차피 전부 "—"이므로 연속 실패 3회에서 남은 계정을 포기한다.
    /// </summary>
    [Fact]
    public async Task Offline_Leaves_All_Dashes_And_Stops_Early()
    {
        var frames = new SpyFrameRepository { ThrowsAlways = new BackendUnavailableException("offline") };

        var vm = await MakeVmAsync(Users(5), frames);
        await vm.FrameCountLoadTask;

        Assert.Equal(new[] { "—", "—", "—", "—", "—" }, Texts(vm));
        Assert.Equal(3, frames.Queried.Count);            // 연속 실패 상한에서 중단
        Assert.Equal(string.Empty, vm.StatusMessage);     // 사용자에게 알리지 않는다
    }

    /// <summary>인증 없음/만료는 남은 계정도 반드시 같은 이유로 실패한다 — 첫 실패에서 접는다.</summary>
    [Fact]
    public async Task Login_Required_Aborts_Remaining_Rows()
    {
        var frames = new SpyFrameRepository { ThrowsAlways = new BackendLoginRequiredException("expired", true) };

        var vm = await MakeVmAsync(Users(3), frames);
        await vm.FrameCountLoadTask;

        Assert.Single(frames.Queried);
        Assert.Equal(new[] { "—", "—", "—" }, Texts(vm));
        Assert.Equal(string.Empty, vm.StatusMessage);
    }

    // ── T9: D-7 — 저장소 미주입이면 기능만 꺼진다(fail-soft) ──

    /// <summary>DI에 IFrameRepository가 없어도 관리 화면은 완전히 동작해야 한다(개수 열만 "—").</summary>
    [Fact]
    public async Task Null_Repository_Leaves_All_Dashes_Without_Crash()
    {
        var vm = await MakeVmAsync(Users(3), frames: null);

        Assert.Equal(new[] { "—", "—", "—" }, Texts(vm));
        Assert.True(vm.FrameCountLoadTask.IsCompleted);
        Assert.Equal(3, vm.Rows.Count);
        Assert.False(vm.IsEmpty);
        Assert.Contains("총 3명", vm.SummaryText);
    }

    // ── T10: 가정 A1 — MS.DI가 생성자 **선택 파라미터**에 등록 서비스를 주입한다 ──

    /// <summary>
    /// 이 가정이 깨지면 컴파일도 기존 테스트도 통과하는데 화면만 전부 "—"가 된다
    /// (프로덕션에서만 조용히 꺼지는 배선 결함). 실제 컨테이너로 조립해 확인한다 —
    /// 등록 형태는 ServiceRegistration.cs의 <c>AddTransient&lt;UserMgmtViewModel&gt;()</c>와 같다.
    /// </summary>
    [Fact]
    public async Task Frame_Repository_Is_Injected_Through_Optional_Ctor_Parameter()
    {
        var frames = new SpyFrameRepository();
        frames.Counts["u1"] = 4;
        frames.Counts["u2"] = 7;

        var services = new ServiceCollection();
        services.AddSingleton<IFrameRepository>(frames);
        services.AddSingleton<IAccountService>(new StubAccountService(Users(2)));
        services.AddSingleton(MakeShell());
        services.AddTransient<UserMgmtViewModel>();   // ServiceRegistration.cs와 같은 등록 형태

        using var provider = services.BuildServiceProvider();
        var vm = provider.GetRequiredService<UserMgmtViewModel>();

        await vm.OnEnterAsync();
        await vm.FrameCountLoadTask;

        Assert.Equal(new[] { "4", "7" }, Texts(vm));
    }
}
