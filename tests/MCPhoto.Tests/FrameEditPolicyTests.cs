using MCPhoto.Core.Frames;
using MCPhoto.Core.Models;
using Xunit;

namespace MCPhoto.Tests;

/// <summary>item2 Step 2: 편집 권한 규칙(역할×출처 매트릭스) 회귀. user=본인 로컬만, power=로컬+DB 기본.</summary>
public class FrameEditPolicyTests
{
    private static FrameTemplate UserLocal(string userId) => new()
        { Id = $"local:{userId}_myframe", UserId = userId, IsDefault = false };
    private static FrameTemplate DbDefault() => new()
        { Id = "a1b2c3-guid", UserId = null, IsDefault = true };
    private static FrameTemplate Bundle() => new() { Id = "bundle:classic", IsDefault = true };
    private static FrameTemplate Fallback() => new() { Id = "fallback", IsDefault = true };

    // ── 게스트(role=null): 전부 불가 ──
    [Fact]
    public void Guest_Cannot_Edit_Anything()
    {
        Assert.False(FrameEditPolicy.CanEdit(UserLocal("u1"), role: null, userId: null));
        Assert.False(FrameEditPolicy.CanEdit(DbDefault(), role: null, userId: null));
        Assert.False(FrameEditPolicy.CanEdit(Bundle(), role: null, userId: null));
        Assert.False(FrameEditPolicy.CanEdit(Fallback(), role: null, userId: null));
    }

    // ── user: 본인 로컬만 ──
    [Fact]
    public void User_Can_Edit_Own_Local()
        => Assert.True(FrameEditPolicy.CanEdit(UserLocal("u1"), UserRole.User, "u1"));

    [Fact]
    public void User_Cannot_Edit_Other_Users_Local()
        => Assert.False(FrameEditPolicy.CanEdit(UserLocal("u1"), UserRole.User, "u2"));

    [Fact]
    public void User_Cannot_Edit_Db_Default()
        => Assert.False(FrameEditPolicy.CanEdit(DbDefault(), UserRole.User, "u1"));

    [Fact]
    public void User_Cannot_Edit_Bundle_Or_Fallback()
    {
        Assert.False(FrameEditPolicy.CanEdit(Bundle(), UserRole.User, "u1"));
        Assert.False(FrameEditPolicy.CanEdit(Fallback(), UserRole.User, "u1"));
    }

    // ── power(manager/admin): 본인 로컬 + DB 기본 ──
    [Theory]
    [InlineData(UserRole.Manager)]
    [InlineData(UserRole.Admin)]
    public void Power_Can_Edit_Db_Default(UserRole role)
        => Assert.True(FrameEditPolicy.CanEdit(DbDefault(), role, "admin1"));

    [Theory]
    [InlineData(UserRole.Manager)]
    [InlineData(UserRole.Admin)]
    public void Power_Can_Edit_Own_Local(UserRole role)
        => Assert.True(FrameEditPolicy.CanEdit(UserLocal("mgr"), role, "mgr"));

    [Theory]
    [InlineData(UserRole.Manager)]
    [InlineData(UserRole.Admin)]
    public void Power_Cannot_Edit_Bundle_Or_Fallback(UserRole role)
    {
        Assert.False(FrameEditPolicy.CanEdit(Bundle(), role, "admin1"));
        Assert.False(FrameEditPolicy.CanEdit(Fallback(), role, "admin1"));
    }

    [Fact]
    public void Power_Cannot_Edit_Other_Users_Local()
        // power라도 남의 로컬 생성분은 편집 불가(UserLocal은 소유 기준).
        => Assert.False(FrameEditPolicy.CanEdit(UserLocal("u1"), UserRole.Admin, "admin1"));

    // ── it15 F1-D4: RequiresFork — 카탈로그 유래(DB/번들/fallback)면 원본 보존 + 새 이름 분기 ──
    [Fact]
    public void RequiresFork_True_For_Db_Default()
        => Assert.True(FrameEditPolicy.RequiresFork(DbDefault()));

    [Fact]
    public void RequiresFork_True_For_Bundle()
        => Assert.True(FrameEditPolicy.RequiresFork(Bundle()));

    [Fact]
    public void RequiresFork_True_For_Fallback_And_Empty_Id()
    {
        Assert.True(FrameEditPolicy.RequiresFork(Fallback()));
        Assert.True(FrameEditPolicy.RequiresFork(new FrameTemplate { Id = string.Empty }));
    }

    [Fact]
    public void RequiresFork_False_For_Local_Regardless_Of_Owner()
    {
        // 소유자 무관 — 출처(local:)만 본다(역할 인자 없음).
        Assert.False(FrameEditPolicy.RequiresFork(UserLocal("u1")));
        Assert.False(FrameEditPolicy.RequiresFork(UserLocal("someone-else")));
    }
}
