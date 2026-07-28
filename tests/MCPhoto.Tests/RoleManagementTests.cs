using System.Globalization;
using System.Windows;
using MCPhoto.App.Converters;
using MCPhoto.Core.Models;

namespace MCPhoto.Tests;

/// <summary>사용자 관리 권한 위계: 자기와 같거나 낮은 역할만 관리(manager는 admin 관리 불가). + 노출 컨버터.</summary>
public class RoleManagementTests
{
    [Theory]
    // 위계 TempUser < User < Manager < Admin. 자신과 같거나 낮은 위계만 관리(it13 §3.2 위계 검증표).
    [InlineData(UserRole.Admin, UserRole.Admin, true)]
    [InlineData(UserRole.Admin, UserRole.Manager, true)]
    [InlineData(UserRole.Admin, UserRole.User, true)]
    [InlineData(UserRole.Admin, UserRole.TempUser, true)]
    [InlineData(UserRole.Manager, UserRole.Admin, false)]   // 핵심 버그: manager는 admin 관리 불가
    [InlineData(UserRole.Manager, UserRole.Manager, true)]
    [InlineData(UserRole.Manager, UserRole.User, true)]
    [InlineData(UserRole.Manager, UserRole.TempUser, true)]
    [InlineData(UserRole.User, UserRole.Admin, false)]
    [InlineData(UserRole.User, UserRole.Manager, false)]
    [InlineData(UserRole.User, UserRole.User, true)]
    [InlineData(UserRole.User, UserRole.TempUser, true)]
    [InlineData(UserRole.TempUser, UserRole.Admin, false)]
    [InlineData(UserRole.TempUser, UserRole.Manager, false)]
    [InlineData(UserRole.TempUser, UserRole.User, false)]
    [InlineData(UserRole.TempUser, UserRole.TempUser, true)]   // 이론적 대칭(실제 UI 노출 없음 — IsPower=false)
    public void CanManage_Only_Equal_Or_Lower(UserRole actor, UserRole target, bool expected)
        => Assert.Equal(expected, actor.CanManage(target));

    // ── it13: 역할 문자열 매핑 라운드트립 + 생성 권한 ──

    [Theory]
    [InlineData(UserRole.TempUser, "temp_user")]
    [InlineData(UserRole.User, "user")]
    [InlineData(UserRole.Manager, "manager")]
    [InlineData(UserRole.Admin, "admin")]
    public void ToFirestoreValue_And_ParseRole_RoundTrip(UserRole role, string expected)
    {
        Assert.Equal(expected, role.ToFirestoreValue());
        Assert.Equal(role, UserRoleExtensions.ParseRole(expected));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("tempuser")]   // 오탈자·미지원값은 최소권한(user)로 폴백
    [InlineData("bogus")]
    public void ParseRole_Unknown_Falls_Back_To_User(string? value)
        => Assert.Equal(UserRole.User, UserRoleExtensions.ParseRole(value));

    [Fact]
    public void TempUser_Is_Not_Power()
        => Assert.False(UserRole.TempUser.IsPower());

    [Theory]
    // admin/manager는 TempUser 생성 가능(User와 동일 위계). TempUser/User는 생성 권한 없음.
    [InlineData(UserRole.Admin, UserRole.TempUser, true)]
    [InlineData(UserRole.Manager, UserRole.TempUser, true)]
    [InlineData(UserRole.User, UserRole.TempUser, false)]
    [InlineData(UserRole.TempUser, UserRole.TempUser, false)]
    [InlineData(UserRole.TempUser, UserRole.User, false)]
    public void CanCreate_TempUser(UserRole actor, UserRole target, bool expected)
        => Assert.Equal(expected, actor.CanCreate(target));

    [Fact]
    public void CreatableRoles_Includes_TempUser_For_Power()
    {
        Assert.Equal(new[] { UserRole.TempUser, UserRole.User, UserRole.Manager }, UserRole.Admin.CreatableRoles());
        Assert.Equal(new[] { UserRole.TempUser, UserRole.User }, UserRole.Manager.CreatableRoles());
        Assert.Empty(UserRole.User.CreatableRoles());
        Assert.Empty(UserRole.TempUser.CreatableRoles());
    }

    [Theory]
    [InlineData(UserRole.TempUser, "임시 유저")]
    [InlineData(UserRole.User, "사용자")]
    [InlineData(UserRole.Manager, "매니저")]
    [InlineData(UserRole.Admin, "관리자")]
    public void ToLabel_Korean(UserRole role, string expected)
        => Assert.Equal(expected, role.ToLabel());

    // ── it13 §9.5·§8.7: 역할 변경 매트릭스(클라 필터 = 서버 setRole 매트릭스 1:1) ──

    [Fact]
    public void AssignableRoles_Admin_Any_NonAdmin_Target_All_Except_Admin()
    {
        // admin은 admin 대상 제외 전부(승격·강등).
        var all = new[] { UserRole.TempUser, UserRole.User, UserRole.Manager };
        Assert.Equal(all, RoleChangePolicy.AssignableRoles(UserRole.Admin, UserRole.User));
        Assert.Equal(all, RoleChangePolicy.AssignableRoles(UserRole.Admin, UserRole.TempUser));
        Assert.Equal(all, RoleChangePolicy.AssignableRoles(UserRole.Admin, UserRole.Manager));
    }

    [Fact]
    public void AssignableRoles_Manager_User_Target_Only_Demote_To_TempUser()
    {
        // manager는 user 대상만, [user, temp_user](temp_user 강등만 유효 변경).
        Assert.Equal(new[] { UserRole.User, UserRole.TempUser },
            RoleChangePolicy.AssignableRoles(UserRole.Manager, UserRole.User));
    }

    [Theory]
    // manager는 temp_user/manager 대상 미노출(승격·manager강등 불가), 비파워는 전부 미노출.
    [InlineData(UserRole.Manager, UserRole.TempUser)]
    [InlineData(UserRole.Manager, UserRole.Manager)]
    [InlineData(UserRole.User, UserRole.User)]
    [InlineData(UserRole.TempUser, UserRole.User)]
    public void AssignableRoles_Empty_Cases(UserRole actor, UserRole target)
        => Assert.Empty(RoleChangePolicy.AssignableRoles(actor, target));

    [Theory]
    // admin 대상은 누구도 변경 불가.
    [InlineData(UserRole.Admin)]
    [InlineData(UserRole.Manager)]
    [InlineData(UserRole.User)]
    public void AssignableRoles_Admin_Target_Always_Empty(UserRole actor)
        => Assert.Empty(RoleChangePolicy.AssignableRoles(actor, UserRole.Admin));

    // ── 노출 컨버터(삭제·pw초기화=Manage) ──

    [Theory]
    [InlineData(UserRole.Manager, UserRole.Admin, false)]   // manager가 admin 행 → 삭제/초기화 미노출
    [InlineData(UserRole.Admin, UserRole.Manager, true)]
    [InlineData(UserRole.Manager, UserRole.Manager, true)]
    [InlineData(UserRole.Manager, UserRole.User, true)]
    // it13: 신 위계(TempUser 최하위) — Manage는 CanManage 위임이라 TempUser 대상도 정합.
    [InlineData(UserRole.Admin, UserRole.TempUser, true)]
    [InlineData(UserRole.Manager, UserRole.TempUser, true)]
    [InlineData(UserRole.User, UserRole.TempUser, true)]     // TempUser < User → user도 삭제/초기화 노출
    [InlineData(UserRole.TempUser, UserRole.User, false)]    // TempUser는 상위(User) 관리 불가
    public void RoleActionVis_Manage(UserRole actor, UserRole target, bool visible)
        => AssertVis(actor, target, "Manage", visible);

    [Fact]
    public void RoleActionVis_Malformed_Input_Collapsed()
    {
        var conv = new RoleActionVisibilityConverter();
        var r = conv.Convert(new object[] { "x" }, typeof(Visibility), "Manage", CultureInfo.InvariantCulture);
        Assert.Equal(Visibility.Collapsed, r);
    }

    private static void AssertVis(UserRole actor, UserRole target, string mode, bool visible)
    {
        var conv = new RoleActionVisibilityConverter();
        var r = conv.Convert(new object[] { actor, target }, typeof(Visibility), mode, CultureInfo.InvariantCulture);
        Assert.Equal(visible ? Visibility.Visible : Visibility.Collapsed, r);
    }
}
