using System.IO;
using MCPhoto.App;
using MCPhoto.App.Services;
using MCPhoto.App.ViewModels;
using MCPhoto.Core.Frames;
using MCPhoto.Core.Models;
using MCPhoto.Core.Navigation;
using MCPhoto.Core.Settings;

namespace MCPhoto.Tests;

/// <summary>
/// it8 Step 4 (A3) → it16 §4: 프레임 생성·삭제·편집 권한 게이트.
/// 게스트 미노출, user·temp_user는 사용만(E4 — 목록·촬영 유지), advanced_user=본인 로컬, 파워=서버 옵션까지.
/// </summary>
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
        public FrameTemplate SaveLocal(FrameTemplate frame, byte[] png, string? ownerName) => frame;
        public IReadOnlyList<FrameTemplate> LoadPublic() => new List<FrameTemplate>();
        public IReadOnlyList<FrameTemplate> LoadUser(string ownerName) => UserFrames;
        public FrameTemplate CacheFromDb(FrameTemplate frame, byte[] png) => frame;
        public bool DeleteLocal(FrameTemplate frame) { DeleteLocalCalls++; return true; }
        public IReadOnlySet<string> PublicFrameNames() => new HashSet<string>();
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    private static AppShellViewModel MakeShell(SessionContext session)
    {
        var settings = new IniSettingsService(iniPath: Path.Combine(Path.GetTempPath(), $"fs_{Guid.NewGuid():N}.ini"));
        settings.Load();
        return new AppShellViewModel(new IdleWatchdog(), settings, new EmptyServiceProvider(), session);
    }

    private static (FrameSelectViewModel vm, StubRepo repo, StubLocalStore local) MakeVm(UserRole? role)
    {
        var session = new SessionContext();
        if (role is { } r) session.Login(new User { Id = "u1", Role = r });
        var repo = new StubRepo();
        var local = new StubLocalStore();
        var catalog = new FrameCatalogService(repo, local);
        var vm = new FrameSelectViewModel(MakeShell(session), catalog, local, repo);
        return (vm, repo, local);
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

    [Fact]
    public async Task Guest_Cannot_Edit_Any_Frame()
    {
        var (vm, _, _) = MakeVm(role: null);
        await vm.OnEnterAsync();
        vm.SelectedFrame = OwnedLocalFrame();
        Assert.False(vm.CanEditSelected);
        vm.SelectedFrame = DbDefaultFrame();
        Assert.False(vm.CanEditSelected);
    }

    [Fact]
    public async Task AdvancedUser_Can_Edit_Own_Local_But_Not_Db_Default()
    {
        var (vm, _, _) = MakeVm(UserRole.AdvancedUser);
        await vm.OnEnterAsync();

        vm.SelectedFrame = OwnedLocalFrame();
        Assert.True(vm.CanEditSelected);       // 본인 로컬 편집 가능

        vm.SelectedFrame = DbDefaultFrame();
        Assert.False(vm.CanEditSelected);      // DB 기본은 비power 편집 불가
    }

    [Fact]
    public async Task AdvancedUser_Cannot_Edit_Other_Users_Local()
    {
        var (vm, _, _) = MakeVm(UserRole.AdvancedUser); // 세션 계정 = u1
        await vm.OnEnterAsync();
        vm.SelectedFrame = new FrameTemplate
        {
            Id = "local:u2_frame", Name = "남의것", UserId = "u2",
            ImageSize = new ImageSize { Width = 100, Height = 100 }
        };
        Assert.False(vm.CanEditSelected);
    }

    /// <summary>it16 §8.2-17(E4): user·temp_user는 본인 로컬 프레임을 선택해도 "선택 편집" 버튼이 뜨지 않는다.</summary>
    [Theory]
    [InlineData(UserRole.User)]
    [InlineData(UserRole.TempUser)]
    public async Task NonWriter_CanEditSelected_False_Even_For_Own_Local(UserRole role)
    {
        var (vm, _, _) = MakeVm(role);
        await vm.OnEnterAsync();
        vm.SelectedFrame = OwnedLocalFrame();
        Assert.False(vm.CanEditSelected);
    }

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

    [Fact]
    public async Task Power_Can_Edit_Db_Default_And_Own_Local()
    {
        var (vm, _, _) = MakeVm(UserRole.Admin); // 세션 계정 = u1
        await vm.OnEnterAsync();

        vm.SelectedFrame = DbDefaultFrame();
        Assert.True(vm.CanEditSelected);       // power는 DB 기본 편집 가능

        vm.SelectedFrame = new FrameTemplate
        {
            Id = "local:u1_myframe", Name = "myframe", UserId = "u1",
            ImageSize = new ImageSize { Width = 100, Height = 100 }
        };
        Assert.True(vm.CanEditSelected);       // 본인 로컬도 가능
    }

    [Fact]
    public async Task Bundle_And_Fallback_Not_Editable_By_Anyone()
    {
        var (vm, _, _) = MakeVm(UserRole.Admin);
        await vm.OnEnterAsync();

        vm.SelectedFrame = new FrameTemplate { Id = "bundle:classic", IsDefault = true };
        Assert.False(vm.CanEditSelected);

        vm.SelectedFrame = new FrameTemplate { Id = "fallback", IsDefault = true };
        Assert.False(vm.CanEditSelected);
    }
}
