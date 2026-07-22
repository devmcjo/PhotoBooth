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
        public Task<IReadOnlyList<FrameTemplate>> GetDefaultFramesAsync(CancellationToken ct = default)
            => Task.FromResult((IReadOnlyList<FrameTemplate>)new List<FrameTemplate>());
        public Task<IReadOnlyList<FrameTemplate>> GetUserFramesAsync(string userId, CancellationToken ct = default)
            => Task.FromResult((IReadOnlyList<FrameTemplate>)new List<FrameTemplate>());
        public Task<FrameTemplate> SaveAsync(FrameTemplate frame, byte[] imageBytes, CancellationToken ct = default)
            => Task.FromResult(frame);
        public Task DeleteAsync(string frameId, CancellationToken ct = default)
        {
            DeleteCalls++; DeletedId = frameId; return Task.CompletedTask;
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
}
