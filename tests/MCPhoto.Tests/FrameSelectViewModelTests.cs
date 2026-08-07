using System.IO;
using MCPhoto.App;
using MCPhoto.App.Services;
using MCPhoto.App.ViewModels;
using MCPhoto.Core.Frames;
using MCPhoto.Core.Models;
using MCPhoto.Core.Navigation;
using MCPhoto.Core.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace MCPhoto.Tests;

/// <summary>
/// it8 Step 4 (A3) → it16 §4: 프레임 생성·삭제·편집 권한 게이트.
/// 게스트 미노출, user·temp_user는 사용만(E4 — 목록·촬영 유지), advanced_user=본인 로컬, 파워=서버 옵션까지.
/// </summary>
[Collection(FallbackCacheCollection.Name)]   // it20 N2: 공유 fallback 캐시 경로 경합 제거
public class FrameSelectViewModelTests
{
    private sealed class StubRepo : IFrameRepository
    {
        public int DeleteCalls { get; private set; }
        public string? DeletedId { get; private set; }
        /// <summary>서버에 실제로 존재하는 문서 id(있어야 DeleteAsync가 true 반환).</summary>
        public HashSet<string> ExistingServerIds { get; } = new();
        /// <summary>GetDefaultFramesAsync가 돌려줄 서버 기본 프레임(이름 매칭 폴백 테스트용).</summary>
        public List<FrameTemplate> Defaults { get; } = new();

        public Task<IReadOnlyList<FrameTemplate>> GetDefaultFramesAsync(CancellationToken ct = default)
            => Task.FromResult((IReadOnlyList<FrameTemplate>)Defaults);
        public Task<IReadOnlyList<FrameTemplate>> GetUserFramesAsync(string userId, CancellationToken ct = default)
            => Task.FromResult((IReadOnlyList<FrameTemplate>)new List<FrameTemplate>());
        public Task<FrameTemplate> SaveMineAsync(FrameTemplate frame, byte[] imageBytes, CancellationToken ct = default)
            => Task.FromResult(frame);
        public Task<FrameTemplate> SaveAsync(FrameTemplate frame, byte[] imageBytes, CancellationToken ct = default)
            => Task.FromResult(frame);
        public Task<bool> DeleteAsync(string frameId, CancellationToken ct = default)
        {
            DeleteCalls++; DeletedId = frameId; return Task.FromResult(ExistingServerIds.Remove(frameId));
        }
        public Task DeleteAllByUserAsync(string userId, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class StubLocalStore : ILocalFrameStore
    {
        public int DeleteLocalCalls { get; private set; }
        /// <summary>LoadUser가 돌려줄 개인 로컬 프레임(it16 E4 목록 노출 검증용).</summary>
        public List<FrameTemplate> UserFrames { get; } = new();
        /// <summary>
        /// it20 T-34: 로컬 스캔 자체를 실패시킨다(디스크 꽉 참·권한 등의 대역). 파일시스템 조작 없이
        /// "로컬 폴백까지 실패 → Failed 카드" 경로를 결정론적으로 재현하기 위한 이음새.
        /// </summary>
        public bool ThrowOnLoadPublic { get; set; }
        public FrameTemplate SaveDefaultFrame(FrameTemplate frame, byte[] png, string? dbId) => frame;
        public FrameTemplate SaveUserFrame(FrameTemplate frame, byte[] png, string ownerEmail, string? dbId) => frame;
        public IReadOnlyList<FrameTemplate> LoadPublic()
            => ThrowOnLoadPublic
                ? throw new IOException("테스트: 로컬 스캔 실패")
                : new List<FrameTemplate>();
        public IReadOnlyList<FrameTemplate> LoadUser(string ownerEmail) => UserFrames;
        public bool DeleteLocal(FrameTemplate frame) { DeleteLocalCalls++; return true; }
        public IReadOnlySet<string> PublicFrameNames() => new HashSet<string>();
        public IReadOnlySet<string> UserFrameNames(string ownerEmail)
            => new HashSet<string>(UserFrames.Select(f => f.Name), StringComparer.OrdinalIgnoreCase);
        public IReadOnlyList<LocalFrameEntry> Inspect(string? ownerEmail) => Array.Empty<LocalFrameEntry>();
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    private static AppShellViewModel MakeShell(SessionContext session, IniSettingsService? settings = null)
    {
        settings ??= new IniSettingsService(iniPath: Path.Combine(Path.GetTempPath(), $"fs_{Guid.NewGuid():N}.ini"));
        settings.Load();
        return new AppShellViewModel(new IdleWatchdog(), settings, new EmptyServiceProvider(), session);
    }

    /// <param name="downloadImage">it20 붙잡기 하네스. ⚠️ repo.Defaults에 DB 프레임을 넣어야 호출된다.</param>
    /// <param name="loadDeadline">it20 상한 축소 이음새(자동 취소 경로 테스트용).</param>
    private static (FrameSelectViewModel vm, StubRepo repo, StubLocalStore local) MakeVm(
        UserRole? role,
        Func<string, CancellationToken, Task<byte[]?>>? downloadImage = null,
        Func<TimeSpan, TimeSpan>? loadDeadline = null)
    {
        var session = new SessionContext();
        if (role is { } r) session.Login(new User { Id = "u1", Role = r });
        var repo = new StubRepo();
        var local = new StubLocalStore();
        var catalog = new FrameCatalogService(repo, local, logger: null, downloadImage: downloadImage);
        var vm = new FrameSelectViewModel(MakeShell(session), catalog, local, repo,
            logger: null, loadDeadline: loadDeadline);
        return (vm, repo, local);
    }

    /// <summary>DB 기본 프레임(ImageUrl이 비어 있으면 TryCacheAsync가 즉시 null → 다운로드가 호출되지 않는다).</summary>
    private static FrameTemplate DbDefault(string name) => new()
    {
        Id = "doc-" + name, Name = name, IsDefault = true,
        ImageUrl = "https://example/frame.png",
        ImageSize = new ImageSize { Width = 1200, Height = 1600 },
        Slots = { new Slot { Index = 0, X = 0, Y = 0, Width = 100, Height = 100 } }
    };

    /// <summary>다운로드를 붙잡아 로딩을 진행 중 상태로 고정하는 하네스.</summary>
    private static (Func<string, CancellationToken, Task<byte[]?>> download, TaskCompletionSource release) HeldDownload()
    {
        var release = new TaskCompletionSource();
        return (async (_, _) => { await release.Task; return new byte[] { 1, 2, 3 }; }, release);
    }

    private static FrameTemplate LocalFrame() => new()
    {
        Id = "local:u1_myframe", Name = "myframe",
        ImageSize = new ImageSize { Width = 100, Height = 100 }
    };

    [Fact]
    public async Task Guest_Cannot_Delete()
    {
        var (vm, _, _) = MakeVm(role: null); // 게스트
        await vm.OnEnterAsync();
        Assert.False(vm.CanDeleteFrames);
    }

    /// <summary>
    /// it16 §8.2-15·16(E4): 생성·삭제 UI 노출은 **프레임 쓰기 권한**(AdvancedUser 이상)에 걸린다.
    /// it15까지는 로그인 여부만 봤으므로 user·temp_user 행이 true → false로 반전된 것이 이번 변경의 핵심이다.
    /// </summary>
    [Theory]
    [InlineData(null, false)]                     // 게스트
    [InlineData(UserRole.TempUser, false)]
    [InlineData(UserRole.User, false)]
    [InlineData(UserRole.AdvancedUser, true)]
    [InlineData(UserRole.Manager, true)]
    [InlineData(UserRole.Admin, true)]
    public async Task CanCreate_And_CanDelete_Follow_Frame_Write_Permission(UserRole? role, bool expected)
    {
        var (vm, _, _) = MakeVm(role);
        await vm.OnEnterAsync();
        Assert.Equal(expected, vm.CanCreateFrame);
        Assert.Equal(expected, vm.CanDeleteFrames);
    }

    [Fact]
    public async Task AdvancedUser_Can_Delete()
    {
        var (vm, _, _) = MakeVm(UserRole.AdvancedUser);
        await vm.OnEnterAsync();
        Assert.True(vm.CanDeleteFrames);
    }

    [Fact]
    public void IsDeletable_Only_Local_Frames()
    {
        Assert.True(FrameSelectViewModel.IsDeletable(new FrameTemplate { Id = "local:x" }));
        Assert.False(FrameSelectViewModel.IsDeletable(new FrameTemplate { Id = "bundle:x" }));
        Assert.False(FrameSelectViewModel.IsDeletable(new FrameTemplate { Id = "fallback" }));
    }

    [Fact]
    public async Task AdvancedUser_Delete_Local_Only_No_Server()
    {
        // it16: 이 흐름의 주체가 user → advanced_user로 이동했다(비power라 서버 옵션은 여전히 없다).
        var (vm, repo, local) = MakeVm(UserRole.AdvancedUser);
        await vm.OnEnterAsync();
        var frame = LocalFrame();
        vm.Frames.Add(frame);

        vm.RequestDeleteCommand.Execute(frame);
        Assert.True(vm.IsDeleteConfirmVisible);
        Assert.False(vm.IsPower); // advanced_user는 서버 옵션 없음

        await vm.ConfirmDeleteCommand.ExecuteAsync(null);

        Assert.Equal(1, local.DeleteLocalCalls); // 로컬 삭제
        Assert.Equal(0, repo.DeleteCalls);        // DB 미삭제
        Assert.DoesNotContain(frame, vm.Frames);
        Assert.False(vm.IsDeleteConfirmVisible);
    }

    [Fact]
    public async Task Power_Delete_With_Server_Option_Strips_Local_Prefix()
    {
        var (vm, repo, local) = MakeVm(UserRole.Admin);
        await vm.OnEnterAsync();
        var frame = LocalFrame();
        vm.Frames.Add(frame);

        vm.RequestDeleteCommand.Execute(frame);
        Assert.True(vm.IsPower);
        vm.DeleteAlsoServer = true;

        await vm.ConfirmDeleteCommand.ExecuteAsync(null);

        Assert.Equal(1, local.DeleteLocalCalls);
        Assert.Equal(1, repo.DeleteCalls);
        Assert.Equal("u1_myframe", repo.DeletedId); // "local:" 접두 제거
    }

    [Fact]
    public async Task Power_Delete_Without_Server_Option_Local_Only()
    {
        var (vm, repo, local) = MakeVm(UserRole.Admin);
        await vm.OnEnterAsync();
        var frame = LocalFrame();
        vm.Frames.Add(frame);

        vm.RequestDeleteCommand.Execute(frame);
        // DeleteAlsoServer 기본 false 유지
        await vm.ConfirmDeleteCommand.ExecuteAsync(null);

        Assert.Equal(1, local.DeleteLocalCalls);
        Assert.Equal(0, repo.DeleteCalls); // 체크 안 하면 DB 미삭제
    }

    [Fact]
    public async Task Power_Server_Delete_Succeeds_When_Id_Matches()
    {
        var (vm, repo, _) = MakeVm(UserRole.Admin);
        await vm.OnEnterAsync();
        // 실 DB id(GUID, 접두 없음)를 담은 공용 프레임 — 서버에 존재.
        var frame = new FrameTemplate { Id = "GUID-abc", Name = "공용프레임", ImageSize = new ImageSize { Width = 100, Height = 100 } };
        repo.ExistingServerIds.Add("GUID-abc");
        vm.Frames.Add(frame);

        vm.RequestDeleteCommand.Execute(frame);
        vm.DeleteAlsoServer = true;
        await vm.ConfirmDeleteCommand.ExecuteAsync(null);

        Assert.Equal(1, repo.DeleteCalls);
        Assert.Equal("GUID-abc", repo.DeletedId);
        Assert.False(vm.DeleteNoticeIsError);           // 성공 안내
    }

    [Fact]
    public async Task Power_Server_Delete_Falls_Back_To_Name_When_Id_Mismatched()
    {
        var (vm, repo, _) = MakeVm(UserRole.Admin);
        await vm.OnEnterAsync();
        // 로컬 id에 #dbid가 없어 local:name 으로 로드된 상황 — 직접 id로는 서버에서 못 찾음.
        var frame = new FrameTemplate { Id = "local:myframe", Name = "myframe", ImageSize = new ImageSize { Width = 100, Height = 100 } };
        // 서버에는 이름이 같은 실제 문서(GUID)가 존재.
        repo.Defaults.Add(new FrameTemplate { Id = "GUID-xyz", Name = "myframe" });
        repo.ExistingServerIds.Add("GUID-xyz");
        vm.Frames.Add(frame);

        vm.RequestDeleteCommand.Execute(frame);
        vm.DeleteAlsoServer = true;
        await vm.ConfirmDeleteCommand.ExecuteAsync(null);

        Assert.Equal(2, repo.DeleteCalls);              // 1) local id 실패 → 2) 이름 매칭 재삭제
        Assert.Equal("GUID-xyz", repo.DeletedId);       // 이름으로 찾은 실제 문서 삭제
        Assert.False(vm.DeleteNoticeIsError);           // 최종 성공
    }

    [Fact]
    public async Task Power_Server_Delete_Reports_Error_When_Not_Found()
    {
        var (vm, repo, _) = MakeVm(UserRole.Admin);
        await vm.OnEnterAsync();
        var frame = LocalFrame(); // 서버에도 없고 이름 매칭도 없음
        vm.Frames.Add(frame);

        vm.RequestDeleteCommand.Execute(frame);
        vm.DeleteAlsoServer = true;
        await vm.ConfirmDeleteCommand.ExecuteAsync(null);

        Assert.True(vm.DeleteNoticeIsError);            // 성공 오인 금지: 실패 안내
    }

    // ── item2 Step 2: 편집 게이트(FrameEditPolicy 위임) ──

    private static FrameTemplate OwnedLocalFrame() => new()
    {
        Id = "local:u1_myframe", Name = "myframe", UserId = "u1",
        ImageSize = new ImageSize { Width = 100, Height = 100 }
    };

    private static FrameTemplate DbDefaultFrame() => new()
    {
        Id = "GUID-abc", Name = "공용", UserId = null, IsDefault = true,
        ImageSize = new ImageSize { Width = 100, Height = 100 }
    };

    /// <summary>it16 §8.2-18(E4): 커맨드 직접 호출(키보드·자동화)도 정책 가드로 차단 — 확인 팝업이 열리지 않는다.</summary>
    [Theory]
    [InlineData(UserRole.User)]
    [InlineData(UserRole.TempUser)]
    public async Task NonWriter_RequestDelete_Command_Blocked(UserRole role)
    {
        var (vm, _, local) = MakeVm(role);
        await vm.OnEnterAsync();
        var frame = OwnedLocalFrame();
        vm.Frames.Add(frame);

        vm.RequestDeleteCommand.Execute(frame);

        Assert.False(vm.IsDeleteConfirmVisible);
        Assert.Null(vm.FrameToDelete);
        Assert.Equal(0, local.DeleteLocalCalls);
        Assert.Contains(frame, vm.Frames);      // 목록에서 사라지지 않는다
    }

    /// <summary>it16 §8.2-19(E4): CreateFrame을 직접 실행해도 편집기로 전이하지 않는다.</summary>
    [Theory]
    [InlineData(UserRole.User)]
    [InlineData(UserRole.TempUser)]
    public async Task NonWriter_CreateFrame_Command_Does_Not_Navigate(UserRole role)
    {
        var session = new SessionContext();
        session.Login(new User { Id = "u1", Role = role });
        var repo = new StubRepo();
        var local = new StubLocalStore();
        var shell = MakeShell(session);
        var vm = new FrameSelectViewModel(shell, new FrameCatalogService(repo, local), local, repo);
        await vm.OnEnterAsync();
        var before = shell.CurrentState;

        await vm.CreateFrameCommand.ExecuteAsync(null);

        Assert.Equal(before, shell.CurrentState);   // 화면 전이 없음(편집기 미진입)
    }

    /// <summary>
    /// it16 §8.2-20(E4): 권한을 잃은 계정의 **기존 로컬 프레임은 목록에 그대로 노출**된다(숨기지 않는다).
    /// 촬영에 계속 쓸 수 있어야 하므로 목록 로딩 코드는 이번 변경 대상이 아니다.
    /// </summary>
    [Theory]
    [InlineData(UserRole.User)]
    [InlineData(UserRole.TempUser)]
    public async Task NonWriter_Existing_Local_Frames_Still_Listed(UserRole role)
    {
        var session = new SessionContext();
        session.Login(new User { Id = "u1", Role = role });
        var repo = new StubRepo();
        var local = new StubLocalStore();
        local.UserFrames.Add(OwnedLocalFrame());
        var vm = new FrameSelectViewModel(MakeShell(session), new FrameCatalogService(repo, local), local, repo);

        await vm.OnEnterAsync();

        Assert.Contains(vm.Frames, f => f.Id == "local:u1_myframe");
        Assert.False(vm.CanDeleteFrames);   // 노출은 유지하되 쓰기 UI만 사라진다
        Assert.False(vm.CanCreateFrame);
    }

    // ── it17: 자동 컷 수 엔드투엔드 배선(설정 → 프레임 선택 → 세션) ──

    /// <summary>
    /// 설계 §0.4 핵심 주장 고정: ini의 자동 sentinel(0)이 **호출측 무변경**으로 세션 실효값까지 전달된다.
    /// `Next()`는 `Settings.Current.CutCount`를 그대로 `Begin`에 넘기고, 해석은 `Begin` 내부에서만 일어난다
    /// — 슬롯 5개 프레임이면 5+2=7컷. 허용 집합({6,8,10})에 없는 7이 실효값 경로에서 정상 동작함도 함께 확인한다.
    /// </summary>
    [Fact]
    public async Task Next_With_Auto_Setting_Resolves_Session_CutCount()
    {
        var iniPath = Path.Combine(Path.GetTempPath(), $"fs_{Guid.NewGuid():N}.ini");
        File.WriteAllText(iniPath, "[MCPhoto]\nCutCount=0\n");
        try
        {
            var settings = new IniSettingsService(iniPath: iniPath);
            Assert.Equal(CutCountPolicy.AutoCutCount, settings.Load().CutCount); // Clamp 가드가 sentinel 보존

            var session = new SessionContext();
            session.Login(new User { Id = "u1", Role = UserRole.User });
            var repo = new StubRepo();
            var local = new StubLocalStore();
            var shell = MakeShell(session, settings);
            var vm = new FrameSelectViewModel(shell, new FrameCatalogService(repo, local), local, repo);
            await vm.OnEnterAsync();

            var frame = FiveSlotFrame();
            vm.Frames.Add(frame);
            vm.SelectedFrame = frame;

            await vm.NextCommand.ExecuteAsync(null);

            Assert.Same(frame, shell.Session.SelectedFrame);
            Assert.Equal(7, shell.Session.Capture.CutCount);
            Assert.True(shell.Session.Capture.IsAutoCutCount);
        }
        finally
        {
            if (File.Exists(iniPath)) File.Delete(iniPath);
        }
    }

    // ── it20: 기본 프레임 다운로드 대기 UI — 국면 전이·상한·탈출 경로 ──

    /// <summary>T-30: VM 생성 직후는 대기 국면이다(진입과 동시에 오버레이가 보이고 빈 목록이 깜빡이지 않는다).</summary>
    [Fact]
    public void Initial_Phase_Is_Loading_Before_Enter()
    {
        var (vm, _, _) = MakeVm(UserRole.User);
        Assert.Equal(FrameLoadPhase.Loading, vm.Phase);
        Assert.True(vm.IsLoading);
        Assert.False(vm.IsInteractive);
        Assert.False(vm.IsDegraded);
        Assert.False(vm.IsLoadFailed);
    }

    /// <summary>T-31: 정상 완료는 Ready — 오버레이·안내가 사라지고 목록이 채워진다.</summary>
    [Fact]
    public async Task Enter_Completes_To_Ready()
    {
        var (vm, _, _) = MakeVm(UserRole.User);
        await vm.OnEnterAsync();

        Assert.Equal(FrameLoadPhase.Ready, vm.Phase);
        Assert.False(vm.IsLoading);
        Assert.True(vm.IsInteractive);
        Assert.Equal(string.Empty, vm.LoadNotice);
        Assert.NotEmpty(vm.Frames);
    }

    /// <summary>
    /// T-32: [기다리지 않고 시작]은 진행 중 로딩을 즉시 로컬 폴백으로 마감한다 →
    /// Degraded + 축소 진행 안내. **새 로딩을 시작하지 않는다**(진행 중 본체가 스스로 폴백한다).
    /// </summary>
    [Fact]
    public async Task Skip_Server_Wait_During_Load_Yields_Degraded()
    {
        var (download, release) = HeldDownload();
        var (vm, repo, _) = MakeVm(UserRole.User, downloadImage: download);
        repo.Defaults.Add(DbDefault("f1"));

        var enter = vm.OnEnterAsync();
        Assert.Equal(FrameLoadPhase.Loading, vm.Phase);

        vm.SkipServerWaitCommand.Execute(null);
        await enter;

        Assert.Equal(FrameLoadPhase.Degraded, vm.Phase);
        Assert.Equal(FrameLoadPolicy.NoticeFor(FrameLoadPhase.Degraded), vm.LoadNotice);
        Assert.NotEmpty(vm.Frames);              // fallback으로 촬영은 계속 가능하다
        Assert.True(vm.IsInteractive);

        release.SetResult();                     // 공유 작업 정리(백그라운드 캐시 워밍은 계속 진행)
    }

    /// <summary>
    /// T-33: 상한 만료 → 자동 취소 → Degraded. 진행이 멎었을 때 손님을 무한정 세워두지 않는 경로의
    /// 회귀 방벽이다(상한은 생성자 이음새로 축소해 결정론적으로 만든다).
    /// </summary>
    [Fact]
    public async Task Deadline_Expiry_Yields_Degraded()
    {
        var (download, release) = HeldDownload();
        var (vm, repo, _) = MakeVm(UserRole.User,
            downloadImage: download,
            loadDeadline: _ => TimeSpan.FromMilliseconds(50));
        repo.Defaults.Add(DbDefault("f1"));

        await vm.OnEnterAsync();

        Assert.Equal(FrameLoadPhase.Degraded, vm.Phase);
        Assert.NotEmpty(vm.Frames);
        Assert.Equal(FrameLoadPolicy.NoticeFor(FrameLoadPhase.Degraded), vm.LoadNotice);

        release.SetResult();
    }

    /// <summary>
    /// T-34: 로컬 폴백까지 실패하면 Failed 카드에 도달한다 — 예외가 OnEnterAsync 밖으로 나가
    /// AppShellViewModel이 조용히 삼키고 전면 오버레이가 영구 고착되는 경로를 봉쇄한다(설계 §6.6 3행).
    /// </summary>
    [Fact]
    public async Task Local_Fallback_Failure_Yields_Failed()
    {
        var (vm, _, local) = MakeVm(UserRole.User);
        local.ThrowOnLoadPublic = true;

        await vm.OnEnterAsync();                 // 예외가 테스트 밖으로 전파되지 않아야 한다

        Assert.Equal(FrameLoadPhase.Failed, vm.Phase);
        Assert.Equal(FrameLoadPolicy.NoticeFor(FrameLoadPhase.Failed), vm.LoadNotice);
        Assert.Empty(vm.Frames);
        Assert.False(vm.IsInteractive);
    }

    /// <summary>T-35: Degraded에서 [다시 시도]는 새 상한을 부여해 Ready로 회복한다(안내도 사라진다).</summary>
    [Fact]
    public async Task Retry_From_Degraded_Returns_To_Ready()
    {
        var (download, release) = HeldDownload();
        var (vm, repo, _) = MakeVm(UserRole.User, downloadImage: download);
        repo.Defaults.Add(DbDefault("f1"));

        var enter = vm.OnEnterAsync();
        vm.SkipServerWaitCommand.Execute(null);
        await enter;
        Assert.Equal(FrameLoadPhase.Degraded, vm.Phase);

        release.SetResult();                     // 서버 응답이 도착한 상황
        await vm.RetryLoadCommand.ExecuteAsync(null);

        Assert.Equal(FrameLoadPhase.Ready, vm.Phase);
        Assert.Equal(string.Empty, vm.LoadNotice);
        Assert.NotEmpty(vm.Frames);
    }

    /// <summary>
    /// T-36: 로딩 중 화면 이탈은 뒤늦은 완료가 폐기된 VM 상태를 건드리지 못하게 한다(stale 가드).
    /// finally도 건너뛰므로 Phase가 Loading으로 남지만, 그때 화면은 이미 바뀌어 사용자에게 보이지 않는다.
    /// </summary>
    [Fact]
    public async Task Leave_During_Load_Does_Not_Mutate_State()
    {
        var (download, release) = HeldDownload();
        var (vm, repo, _) = MakeVm(UserRole.User, downloadImage: download);
        repo.Defaults.Add(DbDefault("f1"));

        var enter = vm.OnEnterAsync();
        await vm.OnLeaveAsync();
        await enter;

        Assert.Equal(FrameLoadPhase.Loading, vm.Phase);   // 결과 미기록
        Assert.Empty(vm.Frames);
        Assert.Equal(string.Empty, vm.LoadNotice);

        release.SetResult();
    }

    /// <summary>
    /// T-38: 삭제 후 재스캔은 조용한 갱신이다 — Phase가 Loading으로 되돌아가지 않으므로
    /// 삭제할 때마다 전면 대기 오버레이가 번쩍이지 않는다(§6.5).
    /// </summary>
    [Fact]
    public async Task Delete_Refresh_Does_Not_Reenter_Loading()
    {
        var (vm, _, local) = MakeVm(UserRole.AdvancedUser);
        await vm.OnEnterAsync();
        Assert.Equal(FrameLoadPhase.Ready, vm.Phase);

        var phaseNotices = new List<FrameLoadPhase>();
        void OnChanged(object? _, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(FrameSelectViewModel.Phase)) phaseNotices.Add(vm.Phase);
        }
        vm.PropertyChanged += OnChanged;
        try
        {
            var frame = LocalFrame();
            vm.Frames.Add(frame);
            vm.RequestDeleteCommand.Execute(frame);
            await vm.ConfirmDeleteCommand.ExecuteAsync(null);
        }
        finally { vm.PropertyChanged -= OnChanged; }

        Assert.DoesNotContain(FrameLoadPhase.Loading, phaseNotices);
        Assert.Equal(FrameLoadPhase.Ready, vm.Phase);
        Assert.Equal(string.Empty, vm.LoadNotice);       // 네트워크 안내가 삭제 조작에 끼어들지 않는다
        Assert.Equal(string.Empty, vm.DeleteNotice);     // 삭제 결과 안내는 종전대로(성공 시 무음)
        Assert.Equal(1, local.DeleteLocalCalls);
    }

    /// <summary>
    /// it20: 생성자에 추가한 선택 인자(`Func&lt;TimeSpan, TimeSpan&gt;? loadDeadline`)가 DI 해석을 깨지 않는지 고정한다.
    /// 이 타입은 컨테이너에 등록되지 않으므로, MS.DI가 기본값 있는 미등록 파라미터를 허용하지 않으면
    /// `AddTransient&lt;FrameSelectViewModel&gt;()` 해석이 런타임에만 실패해 **프레임 선택 화면이 열리지 않는다**
    /// (컴파일·기존 단위 테스트로는 잡히지 않는 배선 결함). 실제 DI 컨테이너로 해석해 확인한다.
    /// </summary>
    [Fact]
    public void ViewModel_Resolves_From_Di_With_Optional_Deadline_Seam()
    {
        var session = new SessionContext();
        var repo = new StubRepo();
        var local = new StubLocalStore();

        var services = new ServiceCollection();
        services.AddSingleton<IFrameRepository>(repo);
        services.AddSingleton<ILocalFrameStore>(local);
        services.AddSingleton(new FrameCatalogService(repo, local));
        services.AddSingleton(MakeShell(session));
        services.AddTransient<FrameSelectViewModel>();   // ServiceRegistration.cs:196과 같은 등록 형태

        using var provider = services.BuildServiceProvider();
        var vm = provider.GetRequiredService<FrameSelectViewModel>();

        Assert.NotNull(vm);
        Assert.Equal(FrameLoadPhase.Loading, vm.Phase);   // 기본 상한(FrameLoadPolicy.NextDeadline)으로 조립됨
    }

    /// <summary>
    /// it20 N11: `FrameLoadPolicy.IdleWarningReferenceSeconds`는 `AppShellViewModel.IdleWarningSeconds`
    /// 기본값의 **수동 사본**이다. Core 테스트(T-12)는 App을 참조하지 않으므로 사본이 어긋나도 못 잡는다 —
    /// 어긋나면 "총 대기 상한 &lt; 유휴 경고" 불변식(설계 A-5)이 거짓 안심이 된다. 여기서 App 계층으로 단정한다.
    /// </summary>
    [Fact]
    public void Idle_Warning_Reference_Matches_Shell_Default()
    {
        var shell = MakeShell(new SessionContext());
        // xUnit2000 규칙에 따라 상수를 expected 위치에 둔다. 진실원은 shell 쪽이며 사본이 상수다.
        Assert.Equal(FrameLoadPolicy.IdleWarningReferenceSeconds, shell.IdleWarningSeconds);
        Assert.True(FrameLoadPolicy.MaxTotalWaitSeconds < shell.IdleWarningSeconds,
            "총 대기 상한이 실제 유휴 경고 기본값보다 길면 대기 중 유휴 팝업이 겹친다");
    }

    private static FrameTemplate FiveSlotFrame()
    {
        var f = new FrameTemplate
        {
            Id = "local:u1_five", Name = "five", UserId = "u1",
            ImageSize = new ImageSize { Width = 600, Height = 800 }
        };
        for (int i = 0; i < 5; i++)
            f.Slots.Add(new Slot { Index = i, X = 0, Y = i * 150, Width = 100, Height = 133 });
        return f;
    }
}
