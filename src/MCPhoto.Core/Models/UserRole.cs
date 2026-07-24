namespace MCPhoto.Core.Models;

/// <summary>계정 역할. (PRD §F8)</summary>
public enum UserRole
{
    /// <summary>자기 프레임(최대 10) + AppSettings 관리.</summary>
    User,

    /// <summary>user + 사용자 관리 + 공용 기본 프레임 관리.</summary>
    Manager,

    /// <summary>manager + manager 지정(최종 1인).</summary>
    Admin
}

/// <summary>UserRole 문자열 매핑(Firestore 저장값과 일치).</summary>
public static class UserRoleExtensions
{
    public static string ToFirestoreValue(this UserRole role) => role switch
    {
        UserRole.User => "user",
        UserRole.Manager => "manager",
        UserRole.Admin => "admin",
        _ => "user"
    };

    public static UserRole ParseRole(string? value) => value switch
    {
        "admin" => UserRole.Admin,
        "manager" => UserRole.Manager,
        _ => UserRole.User
    };

    /// <summary>power 계정(사용자 관리·공용 기본 프레임 관리 권한).</summary>
    public static bool IsPower(this UserRole role) => role is UserRole.Manager or UserRole.Admin;

    /// <summary>
    /// actingRole이 생성할 수 있는 역할 목록(it2 §7): admin→[User,Manager], manager→[User], 그 외→[].
    /// (admin→admin 불가: 최종 1인 규칙)
    /// </summary>
    public static IReadOnlyList<UserRole> CreatableRoles(this UserRole actingRole) => actingRole switch
    {
        UserRole.Admin => new[] { UserRole.User, UserRole.Manager },
        UserRole.Manager => new[] { UserRole.User },
        _ => Array.Empty<UserRole>()
    };

    /// <summary>actingRole이 role 계정을 생성할 권한이 있는지(게이트 판정).</summary>
    public static bool CanCreate(this UserRole actingRole, UserRole role)
        => actingRole.CreatableRoles().Contains(role);

    /// <summary>
    /// actingRole이 targetRole 계정을 관리(삭제·비밀번호 초기화 등)할 수 있는지: **자신과 같거나 낮은 역할만**.
    /// enum 순서 User&lt;Manager&lt;Admin. 예) manager는 admin을 관리 불가, admin은 전부 관리 가능.
    /// </summary>
    public static bool CanManage(this UserRole actingRole, UserRole targetRole)
        => (int)targetRole <= (int)actingRole;
}
