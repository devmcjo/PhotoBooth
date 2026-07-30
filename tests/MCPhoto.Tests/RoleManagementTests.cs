using System.Globalization;
using System.Windows;
using MCPhoto.App.Converters;
using MCPhoto.Core.Models;

namespace MCPhoto.Tests;

/// <summary>사용자 관리 권한 위계: 자기와 같거나 낮은 역할만 관리(manager는 admin 관리 불가). + 노출 컨버터.</summary>
public class RoleManagementTests
{
    [Theory]
    // 위계 TempUser < User < AdvancedUser < Manager < Admin. 자신과 같거나 낮은 위계만 관리(it13 §3.2 위계 검증표).
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
    // it16 §8.2-6: AdvancedUser(랭크 2) 확장 행 — 기존 16조합은 불변이어야 한다.
    [InlineData(UserRole.AdvancedUser, UserRole.User, true)]
    [InlineData(UserRole.AdvancedUser, UserRole.TempUser, true)]
    [InlineData(UserRole.AdvancedUser, UserRole.AdvancedUser, true)]
    [InlineData(UserRole.AdvancedUser, UserRole.Manager, false)]
    [InlineData(UserRole.AdvancedUser, UserRole.Admin, false)]
    [InlineData(UserRole.User, UserRole.AdvancedUser, false)]
    [InlineData(UserRole.TempUser, UserRole.AdvancedUser, false)]
    [InlineData(UserRole.Manager, UserRole.AdvancedUser, true)]
    [InlineData(UserRole.Admin, UserRole.AdvancedUser, true)]
    public void CanManage_Only_Equal_Or_Lower(UserRole actor, UserRole target, bool expected)
        => Assert.Equal(expected, actor.CanManage(target));

    // ── it13: 역할 문자열 매핑 라운드트립 + 생성 권한 ──

    [Theory]
    [InlineData(UserRole.TempUser, "temp_user")]
    [InlineData(UserRole.User, "user")]
    [InlineData(UserRole.AdvancedUser, "advanced_user")]   // it16 §8.2-1 (§5.1 동결표)
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
    [InlineData("advanceduser")]   // it16 §8.2-2: snake_case가 아니면 폴백 유지(조용한 승격 금지)
    public void ParseRole_Unknown_Falls_Back_To_User(string? value)
        => Assert.Equal(UserRole.User, UserRoleExtensions.ParseRole(value));

    [Fact]
    public void TempUser_Is_Not_Power()
        => Assert.False(UserRole.TempUser.IsPower());

    /// <summary>it16 §8.2-4: power 축 오염 금지 — AdvancedUser는 계정 관리·공용 DB 프레임 권한이 없다.</summary>
    [Fact]
    public void AdvancedUser_Is_Not_Power()
        => Assert.False(UserRole.AdvancedUser.IsPower());

    /// <summary>it16 §8.2-5: 프레임 쓰기 권한 축(IsPower와 별개). AdvancedUser 이상만 true.</summary>
    [Theory]
    [InlineData(UserRole.TempUser, false)]
    [InlineData(UserRole.User, false)]
    [InlineData(UserRole.AdvancedUser, true)]
    [InlineData(UserRole.Manager, true)]
    [InlineData(UserRole.Admin, true)]
    public void CanWriteFrames_AdvancedUser_And_Above(UserRole role, bool expected)
        => Assert.Equal(expected, role.CanWriteFrames());

    [Theory]
    // admin/manager는 TempUser 생성 가능(User와 동일 위계). TempUser/User는 생성 권한 없음.
    [InlineData(UserRole.Admin, UserRole.TempUser, true)]
    [InlineData(UserRole.Manager, UserRole.TempUser, true)]
    [InlineData(UserRole.User, UserRole.TempUser, false)]
    [InlineData(UserRole.TempUser, UserRole.TempUser, false)]
    [InlineData(UserRole.TempUser, UserRole.User, false)]
    // it16: AdvancedUser는 대상으로는 power가 만들 수 있고, actor로는 생성 권한이 없다.
    [InlineData(UserRole.Admin, UserRole.AdvancedUser, true)]
    [InlineData(UserRole.Manager, UserRole.AdvancedUser, true)]
    [InlineData(UserRole.AdvancedUser, UserRole.User, false)]
    public void CanCreate_TempUser(UserRole actor, UserRole target, bool expected)
        => Assert.Equal(expected, actor.CanCreate(target));

    [Fact]
    public void CreatableRoles_Includes_TempUser_For_Power()
    {
        // it16 §3.6: 목록에 AdvancedUser 추가(위계 오름차순). 프로덕션 호출자는 0이지만 매트릭스 드리프트를 막는다.
        Assert.Equal(new[] { UserRole.TempUser, UserRole.User, UserRole.AdvancedUser, UserRole.Manager },
            UserRole.Admin.CreatableRoles());
        Assert.Equal(new[] { UserRole.TempUser, UserRole.User, UserRole.AdvancedUser },
            UserRole.Manager.CreatableRoles());
        Assert.Empty(UserRole.AdvancedUser.CreatableRoles());
        Assert.Empty(UserRole.User.CreatableRoles());
        Assert.Empty(UserRole.TempUser.CreatableRoles());
    }

    [Theory]
    [InlineData(UserRole.TempUser, "임시 유저")]
    [InlineData(UserRole.User, "사용자")]
    [InlineData(UserRole.AdvancedUser, "고급 유저")]   // it16 §8.2-3
    [InlineData(UserRole.Manager, "매니저")]
    [InlineData(UserRole.Admin, "관리자")]
    public void ToLabel_Korean(UserRole role, string expected)
        => Assert.Equal(expected, role.ToLabel());

    // ── it13 §9.5·§8.7 → it16 §3.3: 역할 변경 매트릭스(클라 필터 = 서버 setRole 매트릭스 1:1) ──

    /// <summary>
    /// it16 §8.2-7: 설계 §3.3 전수 표를 그대로 옮긴 25행(actor × 대상의 현재 역할).
    /// 기대값은 지정 가능한 새 역할 목록(위계 오름차순)이며, **배열 동등성**이라 표의 5개 열(T·U·A·M·D)이
    /// 전부 기계 검증된다(admin 열이 항상 ✕인 것도 포함) + 콤보 표시 순서까지 고정한다.
    /// </summary>
    [Theory]
    // actor=admin: 대상이 admin이 아니면 admin 제외 전부(승격·강등).
    [InlineData(UserRole.Admin, UserRole.TempUser, "T,U,A,M")]
    [InlineData(UserRole.Admin, UserRole.User, "T,U,A,M")]
    [InlineData(UserRole.Admin, UserRole.AdvancedUser, "T,U,A,M")]
    [InlineData(UserRole.Admin, UserRole.Manager, "T,U,A,M")]
    [InlineData(UserRole.Admin, UserRole.Admin, "")]
    // actor=manager: 하위 3역할 대역 안에서만 자유 지정(E3). manager·admin 지정·대상은 불가.
    [InlineData(UserRole.Manager, UserRole.TempUser, "T,U,A")]
    [InlineData(UserRole.Manager, UserRole.User, "T,U,A")]
    [InlineData(UserRole.Manager, UserRole.AdvancedUser, "T,U,A")]
    [InlineData(UserRole.Manager, UserRole.Manager, "")]
    [InlineData(UserRole.Manager, UserRole.Admin, "")]
    // actor=advanced_user: 계정 관리 권한 없음 → 전부 빈 목록.
    [InlineData(UserRole.AdvancedUser, UserRole.TempUser, "")]
    [InlineData(UserRole.AdvancedUser, UserRole.User, "")]
    [InlineData(UserRole.AdvancedUser, UserRole.AdvancedUser, "")]
    [InlineData(UserRole.AdvancedUser, UserRole.Manager, "")]
    [InlineData(UserRole.AdvancedUser, UserRole.Admin, "")]
    // actor=user
    [InlineData(UserRole.User, UserRole.TempUser, "")]
    [InlineData(UserRole.User, UserRole.User, "")]
    [InlineData(UserRole.User, UserRole.AdvancedUser, "")]
    [InlineData(UserRole.User, UserRole.Manager, "")]
    [InlineData(UserRole.User, UserRole.Admin, "")]
    // actor=temp_user
    [InlineData(UserRole.TempUser, UserRole.TempUser, "")]
    [InlineData(UserRole.TempUser, UserRole.User, "")]
    [InlineData(UserRole.TempUser, UserRole.AdvancedUser, "")]
    [InlineData(UserRole.TempUser, UserRole.Manager, "")]
    [InlineData(UserRole.TempUser, UserRole.Admin, "")]
    public void AssignableRoles_Matrix_it16(UserRole actor, UserRole currentRole, string expectedCsv)
        => Assert.Equal(ParseRoleCsv(expectedCsv), RoleChangePolicy.AssignableRoles(actor, currentRole));

    [Fact]
    public void AssignableRoles_Admin_Any_NonAdmin_Target_All_Except_Admin()
    {
        // admin은 admin 대상 제외 전부(승격·강등). it16: 목록에 AdvancedUser 추가(위계 오름차순).
        var all = new[] { UserRole.TempUser, UserRole.User, UserRole.AdvancedUser, UserRole.Manager };
        Assert.Equal(all, RoleChangePolicy.AssignableRoles(UserRole.Admin, UserRole.User));
        Assert.Equal(all, RoleChangePolicy.AssignableRoles(UserRole.Admin, UserRole.TempUser));
        Assert.Equal(all, RoleChangePolicy.AssignableRoles(UserRole.Admin, UserRole.AdvancedUser));
        Assert.Equal(all, RoleChangePolicy.AssignableRoles(UserRole.Admin, UserRole.Manager));
    }

    [Fact]
    public void AssignableRoles_Manager_Lower_Band_Free_Assign()
    {
        // it16 E3: manager는 하위 3역할 대역(temp_user·user·advanced_user)을 자유 지정(승격 허용).
        var band = new[] { UserRole.TempUser, UserRole.User, UserRole.AdvancedUser };
        Assert.Equal(band, RoleChangePolicy.AssignableRoles(UserRole.Manager, UserRole.TempUser));
        Assert.Equal(band, RoleChangePolicy.AssignableRoles(UserRole.Manager, UserRole.User));
        Assert.Equal(band, RoleChangePolicy.AssignableRoles(UserRole.Manager, UserRole.AdvancedUser));
        // manager 지정은 admin 전용 → 목록에 Manager가 없다.
        Assert.DoesNotContain(UserRole.Manager, RoleChangePolicy.AssignableRoles(UserRole.Manager, UserRole.User));
    }

    [Theory]
    // manager는 manager 대상 미노출(manager 강등은 admin 전용), 비파워(advanced_user 포함)는 전부 미노출.
    [InlineData(UserRole.Manager, UserRole.Manager)]
    [InlineData(UserRole.AdvancedUser, UserRole.User)]
    [InlineData(UserRole.AdvancedUser, UserRole.AdvancedUser)]
    [InlineData(UserRole.User, UserRole.User)]
    [InlineData(UserRole.TempUser, UserRole.User)]
    public void AssignableRoles_Empty_Cases(UserRole actor, UserRole target)
        => Assert.Empty(RoleChangePolicy.AssignableRoles(actor, target));

    [Theory]
    // admin 대상은 누구도 변경 불가.
    [InlineData(UserRole.Admin)]
    [InlineData(UserRole.Manager)]
    [InlineData(UserRole.AdvancedUser)]
    [InlineData(UserRole.User)]
    public void AssignableRoles_Admin_Target_Always_Empty(UserRole actor)
        => Assert.Empty(RoleChangePolicy.AssignableRoles(actor, UserRole.Admin));

    /// <summary>"T,U,A,M" 표기 → UserRole 배열(설계 §3.3 표 열 기호). 빈 문자열은 빈 배열.</summary>
    private static UserRole[] ParseRoleCsv(string csv)
        => string.IsNullOrEmpty(csv)
            ? Array.Empty<UserRole>()
            : csv.Split(',').Select(t => t switch
            {
                "T" => UserRole.TempUser,
                "U" => UserRole.User,
                "A" => UserRole.AdvancedUser,
                "M" => UserRole.Manager,
                "D" => UserRole.Admin,
                _ => throw new ArgumentOutOfRangeException(nameof(csv), t, "알 수 없는 역할 기호")
            }).ToArray();

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
    // it16 §8.2-8: AdvancedUser 행 — 컨버터는 CanManage 위임이라 랭크 2로 정합(실제 화면 도달은 power 전용).
    [InlineData(UserRole.Admin, UserRole.AdvancedUser, true)]
    [InlineData(UserRole.Manager, UserRole.AdvancedUser, true)]
    [InlineData(UserRole.AdvancedUser, UserRole.User, true)]
    [InlineData(UserRole.AdvancedUser, UserRole.Manager, false)]
    [InlineData(UserRole.User, UserRole.AdvancedUser, false)]
    public void RoleActionVis_Manage(UserRole actor, UserRole target, bool visible)
        => AssertVis(actor, target, "Manage", visible);

    [Fact]
    public void RoleActionVis_Malformed_Input_Collapsed()
    {
        var conv = new RoleActionVisibilityConverter();
        var r = conv.Convert(new object[] { "x" }, typeof(Visibility), "Manage", CultureInfo.InvariantCulture);
        Assert.Equal(Visibility.Collapsed, r);
    }

    /// <summary>
    /// 자기 계정 행(3번째 값 isSelf=true)은 관리 액션을 노출하지 않는다 — 위계상 관리 가능(admin↔admin)해도 마찬가지.
    /// 자기 계정 삭제는 명령이 거부하므로 버튼을 보일 이유가 없다(사용자 관리 목록 UX).
    /// </summary>
    [Theory]
    [InlineData(true, Visibility.Collapsed)]
    [InlineData(false, Visibility.Visible)]
    public void RoleActionVis_Self_Row_Hidden(bool isSelf, Visibility expected)
    {
        var conv = new RoleActionVisibilityConverter();
        var r = conv.Convert(new object[] { UserRole.Admin, UserRole.Admin, isSelf },
            typeof(Visibility), "Manage", CultureInfo.InvariantCulture);
        Assert.Equal(expected, r);
    }

    private static void AssertVis(UserRole actor, UserRole target, string mode, bool visible)
    {
        var conv = new RoleActionVisibilityConverter();
        var r = conv.Convert(new object[] { actor, target }, typeof(Visibility), mode, CultureInfo.InvariantCulture);
        Assert.Equal(visible ? Visibility.Visible : Visibility.Collapsed, r);
    }
}
