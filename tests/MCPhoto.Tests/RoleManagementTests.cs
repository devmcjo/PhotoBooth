using System.Globalization;
using System.Windows;
using MCPhoto.App.Converters;
using MCPhoto.Core.Models;

namespace MCPhoto.Tests;

/// <summary>사용자 관리 권한 위계: 자기와 같거나 낮은 역할만 관리(manager는 admin 관리 불가). + 노출 컨버터.</summary>
public class RoleManagementTests
{
    [Theory]
    [InlineData(UserRole.Admin, UserRole.Admin, true)]
    [InlineData(UserRole.Admin, UserRole.Manager, true)]
    [InlineData(UserRole.Admin, UserRole.User, true)]
    [InlineData(UserRole.Manager, UserRole.Admin, false)]   // 핵심 버그: manager는 admin 관리 불가
    [InlineData(UserRole.Manager, UserRole.Manager, true)]
    [InlineData(UserRole.Manager, UserRole.User, true)]
    [InlineData(UserRole.User, UserRole.Admin, false)]
    [InlineData(UserRole.User, UserRole.Manager, false)]
    [InlineData(UserRole.User, UserRole.User, true)]
    public void CanManage_Only_Equal_Or_Lower(UserRole actor, UserRole target, bool expected)
        => Assert.Equal(expected, actor.CanManage(target));

    // ── 노출 컨버터(삭제·pw초기화=Manage / manager 지정=Promote) ──

    [Theory]
    [InlineData(UserRole.Manager, UserRole.Admin, false)]   // manager가 admin 행 → 삭제/초기화 미노출
    [InlineData(UserRole.Admin, UserRole.Manager, true)]
    [InlineData(UserRole.Manager, UserRole.Manager, true)]
    [InlineData(UserRole.Manager, UserRole.User, true)]
    public void RoleActionVis_Manage(UserRole actor, UserRole target, bool visible)
        => AssertVis(actor, target, "Manage", visible);

    [Theory]
    [InlineData(UserRole.Admin, UserRole.User, true)]       // admin이 user만 승격
    [InlineData(UserRole.Admin, UserRole.Manager, false)]   // 이미 manager
    [InlineData(UserRole.Admin, UserRole.Admin, false)]     // admin 강등 금지
    [InlineData(UserRole.Manager, UserRole.User, false)]    // manager는 승격 불가(admin 전용)
    public void RoleActionVis_Promote(UserRole actor, UserRole target, bool visible)
        => AssertVis(actor, target, "Promote", visible);

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
