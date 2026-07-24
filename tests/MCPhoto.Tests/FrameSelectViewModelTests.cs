using System.IO;
using MCPhoto.App;
using MCPhoto.App.Services;
using MCPhoto.App.ViewModels;
using MCPhoto.Core.Frames;
using MCPhoto.Core.Models;
using MCPhoto.Core.Navigation;
using MCPhoto.Core.Settings;

namespace MCPhoto.Tests;

/// <summary>it8 Step 4 (A3): 프레임 삭제 권한·정책 — 게스트 미노출, user 로컬만, 파워 서버 옵션.</summary>
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
        public bool SupportsUpdateById => true;
        public Task<FrameTemplate> UpdateAsync(FrameTemplate frame, byte[] imageBytes, bool replaceImage, CancellationToken ct = default)
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
        public FrameTemplate SaveLocal(FrameTemplate frame, byte[] png, string? ownerName) => frame;
        public IReadOnlyList<FrameTemplate> LoadPublic() => new List<FrameTemplate>();
        public IReadOnlyList<FrameTemplate> LoadUser(string ownerName) => new List<FrameTemplate>();
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
        if (role is { } r) session.Login(new User { Id = "u1", Password = "pw", Role = r });
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

    [Fact]
    public async Task LoggedIn_Can_Delete()
    {
        var (vm, _, _) = MakeVm(UserRole.User);
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
    public async Task User_Delete_Local_Only_No_Server()
    {
        var (vm, repo, local) = MakeVm(UserRole.User);
        await vm.OnEnterAsync();
        var frame = LocalFrame();
        vm.Frames.Add(frame);

        vm.RequestDeleteCommand.Execute(frame);
        Assert.True(vm.IsDeleteConfirmVisible);
        Assert.False(vm.IsPower); // user는 서버 옵션 없음

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
    public async Task User_Can_Edit_Own_Local_But_Not_Db_Default()
    {
        var (vm, _, _) = MakeVm(UserRole.User);
        await vm.OnEnterAsync();

        vm.SelectedFrame = OwnedLocalFrame();
        Assert.True(vm.CanEditSelected);       // 본인 로컬 편집 가능

        vm.SelectedFrame = DbDefaultFrame();
        Assert.False(vm.CanEditSelected);      // DB 기본은 user 편집 불가
    }

    [Fact]
    public async Task User_Cannot_Edit_Other_Users_Local()
    {
        var (vm, _, _) = MakeVm(UserRole.User); // 세션 계정 = u1
        await vm.OnEnterAsync();
        vm.SelectedFrame = new FrameTemplate
        {
            Id = "local:u2_frame", Name = "남의것", UserId = "u2",
            ImageSize = new ImageSize { Width = 100, Height = 100 }
        };
        Assert.False(vm.CanEditSelected);
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
