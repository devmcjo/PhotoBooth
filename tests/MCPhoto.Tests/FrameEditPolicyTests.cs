using MCPhoto.Core.Frames;
using MCPhoto.Core.Models;
using Xunit;

namespace MCPhoto.Tests;

/// <summary>
/// item2 Step 2 → it16 §4: 편집·삭제 권한 규칙(역할×출처 매트릭스) 회귀.
/// it16: 프레임 쓰기 권한은 AdvancedUser 이상 — advanced_user=본인 로컬만, power=로컬+DB 기본,
/// user·temp_user=사용만(읽기 전용, E4).
/// </summary>
public class FrameEditPolicyTests
{
    private static FrameTemplate UserLocal(string userId) => new()
        { Id = $"local:{userId}_myframe", UserId = userId, IsDefault = false };
    private static FrameTemplate DbDefault() => new()
        { Id = "a1b2c3-guid", UserId = null, IsDefault = true };
    private static FrameTemplate Bundle() => new() { Id = "bundle:classic", IsDefault = true };
    private static FrameTemplate Fallback() => new() { Id = "fallback", IsDefault = true };
    /// <summary>power가 fork·저장한 **공용** 로컬 프레임: 디스크에서 다시 읽으면 UserId=null이다(F18).</summary>
    private static FrameTemplate PublicLocal() => new()
        { Id = "local:공용프레임", UserId = null, IsDefault = true };

    // ── 게스트(role=null): 전부 불가 ──
    [Fact]
    public void Guest_Cannot_Edit_Anything()
    {
        Assert.False(FrameEditPolicy.CanEdit(UserLocal("u1"), role: null, userId: null));
        Assert.False(FrameEditPolicy.CanEdit(DbDefault(), role: null, userId: null));
        Assert.False(FrameEditPolicy.CanEdit(Bundle(), role: null, userId: null));
        Assert.False(FrameEditPolicy.CanEdit(Fallback(), role: null, userId: null));
    }

    // ── it16 E4(핵심 반전): user·temp_user는 **본인 로컬도** 편집 불가(사용만) ──

    /// <summary>
    /// it16 §8.2-9: it15까지 `User_Can_Edit_Own_Local`이 true였다(=이 단정의 반전이 이번 변경의 핵심).
    /// 프레임 저작 권한이 AdvancedUser로 이동했으므로 user·temp_user는 본인이 만든 프레임도 편집할 수 없다.
    /// </summary>
    [Theory]
    [InlineData(UserRole.User)]
    [InlineData(UserRole.TempUser)]
    public void NonWriter_Cannot_Edit_Own_Local(UserRole role)
        => Assert.False(FrameEditPolicy.CanEdit(UserLocal("u1"), role, "u1"));

    [Theory]
    [InlineData(UserRole.User)]
    [InlineData(UserRole.TempUser)]
    public void NonWriter_Cannot_Edit_Anything(UserRole role)
    {
        Assert.False(FrameEditPolicy.CanEdit(UserLocal("u1"), role, "u2"));   // 타인 로컬
        Assert.False(FrameEditPolicy.CanEdit(DbDefault(), role, "u1"));
        Assert.False(FrameEditPolicy.CanEdit(Bundle(), role, "u1"));
        Assert.False(FrameEditPolicy.CanEdit(Fallback(), role, "u1"));
    }

    // ── it16 §8.2-10: advanced_user = it15 User 케이스 전량 이관(본인 로컬만) ──

    [Fact]
    public void AdvancedUser_Can_Edit_Own_Local()
        => Assert.True(FrameEditPolicy.CanEdit(UserLocal("u1"), UserRole.AdvancedUser, "u1"));

    [Fact]
    public void AdvancedUser_Cannot_Edit_Other_Users_Local()
        => Assert.False(FrameEditPolicy.CanEdit(UserLocal("u1"), UserRole.AdvancedUser, "u2"));

    [Fact]
    public void AdvancedUser_Cannot_Edit_Db_Default()
        // power가 아니므로 공용 DB 프레임은 편집 불가(IsPower 축은 확장하지 않았다).
        => Assert.False(FrameEditPolicy.CanEdit(DbDefault(), UserRole.AdvancedUser, "u1"));

    [Fact]
    public void AdvancedUser_Cannot_Edit_Bundle_Or_Fallback()
    {
        Assert.False(FrameEditPolicy.CanEdit(Bundle(), UserRole.AdvancedUser, "u1"));
        Assert.False(FrameEditPolicy.CanEdit(Fallback(), UserRole.AdvancedUser, "u1"));
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

    // ── it16 §4.4: CanDelete 매트릭스(삭제 판정을 순수 함수로 승격) ──

    /// <summary>it16 §8.2-12: 게스트·쓰기 권한 없는 역할은 어떤 프레임도 삭제할 수 없다(E4).</summary>
    [Fact]
    public void Guest_Cannot_Delete_Anything()
    {
        Assert.False(FrameEditPolicy.CanDelete(UserLocal("u1"), role: null));
        Assert.False(FrameEditPolicy.CanDelete(DbDefault(), role: null));
        Assert.False(FrameEditPolicy.CanDelete(Bundle(), role: null));
        Assert.False(FrameEditPolicy.CanDelete(Fallback(), role: null));
    }

    [Theory]
    [InlineData(UserRole.User)]
    [InlineData(UserRole.TempUser)]
    public void NonWriter_Cannot_Delete_Anything(UserRole role)
    {
        Assert.False(FrameEditPolicy.CanDelete(UserLocal("u1"), role));   // 본인 로컬도 불가(it15 대비 반전)
        Assert.False(FrameEditPolicy.CanDelete(PublicLocal(), role));
        Assert.False(FrameEditPolicy.CanDelete(DbDefault(), role));
        Assert.False(FrameEditPolicy.CanDelete(Bundle(), role));
        Assert.False(FrameEditPolicy.CanDelete(Fallback(), role));
    }

    [Fact]
    public void AdvancedUser_Can_Delete_Local_But_Not_Db_Default()
    {
        Assert.True(FrameEditPolicy.CanDelete(UserLocal("u1"), UserRole.AdvancedUser));
        Assert.False(FrameEditPolicy.CanDelete(DbDefault(), UserRole.AdvancedUser));   // 공용 DB는 power만
    }

    [Theory]
    [InlineData(UserRole.Manager)]
    [InlineData(UserRole.Admin)]
    public void Power_Can_Delete_Local_And_Db_Default(UserRole role)
    {
        Assert.True(FrameEditPolicy.CanDelete(UserLocal("mgr"), role));
        Assert.True(FrameEditPolicy.CanDelete(DbDefault(), role));
    }

    /// <summary>
    /// it16 §8.2-13: power가 fork·저장한 **공용 로컬** 프레임(UserId=null, F18)도 삭제 가능해야 한다.
    /// CanDelete가 소유자 판정으로 회귀하면 이 단정이 깨진다(현행 삭제 능력 보존).
    /// </summary>
    [Theory]
    [InlineData(UserRole.Manager)]
    [InlineData(UserRole.Admin)]
    public void Power_Can_Delete_Public_Local_Without_Owner(UserRole role)
        => Assert.True(FrameEditPolicy.CanDelete(PublicLocal(), role));

    [Theory]
    [InlineData(UserRole.AdvancedUser)]
    [InlineData(UserRole.Manager)]
    [InlineData(UserRole.Admin)]
    public void Writer_Cannot_Delete_Bundle_Fallback_Or_Empty_Id(UserRole role)
    {
        Assert.False(FrameEditPolicy.CanDelete(Bundle(), role));
        Assert.False(FrameEditPolicy.CanDelete(Fallback(), role));
        Assert.False(FrameEditPolicy.CanDelete(new FrameTemplate { Id = string.Empty }, role)); // 빈 Id=Fallback
    }

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
