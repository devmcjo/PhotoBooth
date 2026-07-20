using System.IO;
using MCPhoto.Core.Accounts;
using MCPhoto.Core.Frames;
using MCPhoto.Core.Models;
using MCPhoto.Firebase;

namespace MCPhoto.Tests;

/// <summary>
/// WBS Step 11: 로그인·역할 권한 게이트·cascade·오프라인 시드 검증.
/// Firebase 미초기화(키 없음) 상태에서 시드 계정 로그인·권한 규칙을 검증.
/// </summary>
public class AccountTests
{
    private sealed class NoopFrameRepo : IFrameRepository
    {
        public List<string> CascadeDeleted { get; } = new();
        public Task<IReadOnlyList<FrameTemplate>> GetDefaultFramesAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<FrameTemplate>>(Array.Empty<FrameTemplate>());
        public Task<IReadOnlyList<FrameTemplate>> GetUserFramesAsync(string userId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<FrameTemplate>>(Array.Empty<FrameTemplate>());
        public Task<FrameTemplate> SaveAsync(FrameTemplate frame, byte[] imageBytes, CancellationToken ct = default)
            => Task.FromResult(frame);
        public Task DeleteAsync(string frameId, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteAllByUserAsync(string userId, CancellationToken ct = default)
        {
            CascadeDeleted.Add(userId);
            return Task.CompletedTask;
        }
    }

    // Firebase 키 없는 환경(미초기화) 클라이언트
    private static FirebaseClient OfflineClient()
        => new(serviceAccountKeyPath: Path.Combine(Path.GetTempPath(), $"no_key_{Guid.NewGuid():N}.json"));

    [Fact]
    public void Offline_Client_Is_Not_Initialized()
    {
        var client = OfflineClient();
        Assert.False(client.IsInitialized);
    }

    [Fact]
    public async Task Seed_Admin_Login_Works_Offline()
    {
        var svc = new AccountService(OfflineClient(), new NoopFrameRepo());
        var user = await svc.LoginAsync("devmcjo", "1111");
        Assert.NotNull(user);
        Assert.Equal(UserRole.Admin, user!.Role);
    }

    [Fact]
    public async Task Wrong_Password_Fails()
    {
        var svc = new AccountService(OfflineClient(), new NoopFrameRepo());
        Assert.Null(await svc.LoginAsync("devmcjo", "wrong"));
        Assert.Null(await svc.LoginAsync("unknown", "1111"));
    }

    [Fact]
    public async Task Offline_Create_Throws()
    {
        var svc = new AccountService(OfflineClient(), new NoopFrameRepo());
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.CreateAsync("newuser", "pw"));
    }

    // ── 역할 권한 규칙(순수 로직) ──

    [Fact]
    public void User_Is_Not_Power()
    {
        Assert.False(UserRole.User.IsPower());
    }

    [Fact]
    public void Manager_And_Admin_Are_Power()
    {
        Assert.True(UserRole.Manager.IsPower());
        Assert.True(UserRole.Admin.IsPower());
    }

    [Fact]
    public void Role_Firestore_Roundtrip()
    {
        Assert.Equal("admin", UserRole.Admin.ToFirestoreValue());
        Assert.Equal("manager", UserRole.Manager.ToFirestoreValue());
        Assert.Equal("user", UserRole.User.ToFirestoreValue());
        Assert.Equal(UserRole.Admin, UserRoleExtensions.ParseRole("admin"));
        Assert.Equal(UserRole.User, UserRoleExtensions.ParseRole("unknown")); // 폴백
    }
}
