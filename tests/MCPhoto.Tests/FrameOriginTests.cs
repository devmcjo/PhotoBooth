using MCPhoto.Core.Frames;
using MCPhoto.Core.Models;
using Xunit;

namespace MCPhoto.Tests;

/// <summary>item2 Step 1: 프레임 출처 판정 순수 함수(Id 접두·IsDefault·UserId 경계) 회귀.</summary>
public class FrameOriginTests
{
    private static FrameTemplate F(string id, bool isDefault = false, string? userId = null)
        => new() { Id = id, IsDefault = isDefault, UserId = userId };

    [Theory]
    [InlineData("bundle:classic", FrameOriginKind.Bundle)]
    [InlineData("fallback", FrameOriginKind.Fallback)]
    [InlineData("fallback_frame", FrameOriginKind.Fallback)]
    [InlineData("", FrameOriginKind.Fallback)]                 // 빈 Id → Fallback
    [InlineData("local:u1_myframe", FrameOriginKind.UserLocal)]
    [InlineData("a1b2c3-guid", FrameOriginKind.DbDefault)]     // 접두 없는 실 DB id
    public void Classify_By_Prefix(string id, FrameOriginKind expected)
        => Assert.Equal(expected, FrameOrigin.Classify(F(id)));

    [Fact]
    public void IsOwnedLocal_True_When_Prefix_And_Owner_Match()
        => Assert.True(FrameOrigin.IsOwnedLocal(F("local:u1_x", userId: "u1"), "u1"));

    [Fact]
    public void IsOwnedLocal_False_When_Owner_Differs()
        => Assert.False(FrameOrigin.IsOwnedLocal(F("local:u1_x", userId: "u1"), "u2"));

    [Fact]
    public void IsOwnedLocal_False_When_UserId_Null_On_Frame()
        // local: 접두이나 UserId가 세팅되지 않은 경우(공용 로드 경로) → 소유 미인정.
        => Assert.False(FrameOrigin.IsOwnedLocal(F("local:x", userId: null), "u1"));

    [Fact]
    public void IsOwnedLocal_False_When_Current_User_Empty()
        => Assert.False(FrameOrigin.IsOwnedLocal(F("local:u1_x", userId: "u1"), ""));

    [Theory]
    [InlineData("bundle:classic")]
    [InlineData("fallback")]
    [InlineData("a1b2c3-guid")]  // DbDefault는 UserLocal 아님
    public void IsOwnedLocal_False_When_Not_UserLocal(string id)
        => Assert.False(FrameOrigin.IsOwnedLocal(F(id, userId: "u1"), "u1"));

    [Fact]
    public void IsDbDefault_True_For_Db_Id_With_IsDefault()
        => Assert.True(FrameOrigin.IsDbDefault(F("a1b2c3-guid", isDefault: true)));

    [Fact]
    public void IsDbDefault_False_When_Not_Default_Flag()
        // 접두 없는 id지만 isDefault=false면 DB 기본 아님(보수적).
        => Assert.False(FrameOrigin.IsDbDefault(F("a1b2c3-guid", isDefault: false)));

    [Theory]
    [InlineData("local:u1_x")]
    [InlineData("bundle:classic")]
    [InlineData("fallback")]
    [InlineData("")]
    public void IsDbDefault_False_For_NonDb_Origins(string id)
        => Assert.False(FrameOrigin.IsDbDefault(F(id, isDefault: true)));
}
