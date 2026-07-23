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
        public Task<bool> DeleteAsync(string frameId, CancellationToken ct = default) => Task.FromResult(true);
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
    public async Task Offline_Create_Throws_When_Gate_Passes()
    {
        // 게이트는 통과(admin→user)하지만 미초기화라 InvalidOperationException
        var svc = new AccountService(OfflineClient(), new NoopFrameRepo());
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.CreateAsync("newuser", "pw", UserRole.User, actingRole: UserRole.Admin));
    }

    // ── it2 §7: 계정 생성 역할 게이트(서비스 강제) ──

    [Fact]
    public async Task Gate_Rejects_Manager_Creating_Manager()
    {
        var svc = new AccountService(OfflineClient(), new NoopFrameRepo());
        // manager는 manager를 만들 수 없음 → 미초기화보다 권한 위반이 우선(UnauthorizedAccessException)
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => svc.CreateAsync("m2", "pw", UserRole.Manager, actingRole: UserRole.Manager));
    }

    [Fact]
    public async Task Gate_Rejects_Admin_Creating_Admin()
    {
        var svc = new AccountService(OfflineClient(), new NoopFrameRepo());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => svc.CreateAsync("a2", "pw", UserRole.Admin, actingRole: UserRole.Admin));
    }

    [Fact]
    public async Task Gate_Rejects_User_Creating_Anything()
    {
        var svc = new AccountService(OfflineClient(), new NoopFrameRepo());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => svc.CreateAsync("x", "pw", UserRole.User, actingRole: UserRole.User));
    }

    // ── 게이트 판정 순수 로직(CanCreate/CreatableRoles) ──

    [Fact]
    public void CreatableRoles_Admin()
    {
        var roles = UserRole.Admin.CreatableRoles();
        Assert.Contains(UserRole.User, roles);
        Assert.Contains(UserRole.Manager, roles);
        Assert.DoesNotContain(UserRole.Admin, roles); // 최종 1인
    }

    [Fact]
    public void CreatableRoles_Manager()
    {
        var roles = UserRole.Manager.CreatableRoles();
        Assert.Contains(UserRole.User, roles);
        Assert.DoesNotContain(UserRole.Manager, roles);
    }

    [Fact]
    public void CreatableRoles_User_Empty()
    {
        Assert.Empty(UserRole.User.CreatableRoles());
    }

    [Theory]
    [InlineData(UserRole.Admin, UserRole.User, true)]
    [InlineData(UserRole.Admin, UserRole.Manager, true)]
    [InlineData(UserRole.Admin, UserRole.Admin, false)]
    [InlineData(UserRole.Manager, UserRole.User, true)]
    [InlineData(UserRole.Manager, UserRole.Manager, false)]
    [InlineData(UserRole.User, UserRole.User, false)]
    public void CanCreate_Matrix(UserRole acting, UserRole target, bool expected)
    {
        Assert.Equal(expected, acting.CanCreate(target));
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
