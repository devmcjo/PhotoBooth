using MCPhoto.Core.Frames;
using MCPhoto.Core.Models;
using Xunit;

namespace MCPhoto.Tests;

/// <summary>
/// item2 Step 2 → it16 §4: **삭제** 권한 규칙(역할×출처 매트릭스) 회귀.
/// ⚠️ 편집(CanEdit)·fork(RequiresFork) 테스트는 삭제했다 — 프레임 수정 기능 자체가 폐지됐다(설계 D-16).
/// it16: 프레임 쓰기 권한은 AdvancedUser 이상 — advanced_user=본인 로컬만, power=로컬+DB 기본,
/// user·temp_user=사용만(읽기 전용, E4).
/// </summary>
public class FrameEditPolicyTests
{
    private static FrameTemplate UserLocal(string userId) => new()
        { Id = $"local:{userId}_myframe", UserId = userId, IsDefault = false };
    private static FrameTemplate DbDefault() => new()
        { Id = "a1b2c3-guid", UserId = null, IsDefault = true };
    /// <summary>
    /// ⚠️ it27: <c>bundle:</c>은 <b>폐기된 출처</b>다 — 생성 경로 없음(설계 it27 §3.2).
    /// 이 헬퍼를 쓰는 단정들은 <b>fail-closed 방어 계약</b>이므로 "이제 안 쓰는 출처니까"로 지우지 않는다:
    /// 판정이 사라지면 그 id가 <c>DbDefault</c>로 떨어져 power에게 삭제가 허용된다(§4.2).
    /// </summary>
    private static FrameTemplate Bundle() => new() { Id = "bundle:classic", IsDefault = true };
    private static FrameTemplate Fallback() => new() { Id = "fallback", IsDefault = true };
    /// <summary>power가 fork·저장한 **공용** 로컬 프레임: 디스크에서 다시 읽으면 UserId=null이다(F18).</summary>
    private static FrameTemplate PublicLocal() => new()
        { Id = "local:공용프레임", UserId = null, IsDefault = true };

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

}
