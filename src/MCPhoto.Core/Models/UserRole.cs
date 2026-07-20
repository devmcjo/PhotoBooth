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
}
