namespace MCPhoto.Core.Models;

/// <summary>계정 역할. (PRD §F8, it13 TempUser)</summary>
public enum UserRole
{
    // ⚠️ 서수(enum 값)를 위계 비교에 쓰지 않는다(CanManage는 ManageRank switch). 배치값은 가독성용으로 위계 순 명시.
    //    저장·전송은 전부 ToFirestoreValue() 문자열이라 배치값 변경은 무해(it13 §1.1·§3.1).

    /// <summary>임시 유저(it13): user와 동기능 + QR 전송(업로드+다운로드)만 시간·횟수 한도. 위계 최하위.</summary>
    TempUser = 0,

    /// <summary>자기 프레임(최대 10) + AppSettings 관리.</summary>
    User = 1,

    /// <summary>user + 사용자 관리 + 공용 기본 프레임 관리.</summary>
    Manager = 2,

    /// <summary>manager + manager 지정(최종 1인).</summary>
    Admin = 3
}

/// <summary>UserRole 문자열 매핑(Firestore 저장값과 일치).</summary>
public static class UserRoleExtensions
{
    public static string ToFirestoreValue(this UserRole role) => role switch
    {
        UserRole.TempUser => "temp_user",   // it13: C#↔TS↔Firestore 일관(snake_case)
        UserRole.User => "user",
        UserRole.Manager => "manager",
        UserRole.Admin => "admin",
        _ => "user"
    };

    public static UserRole ParseRole(string? value) => value switch
    {
        "admin" => UserRole.Admin,
        "manager" => UserRole.Manager,
        "temp_user" => UserRole.TempUser,   // it13
        "user" => UserRole.User,            // 명시(기존 default 폴백에서 승격)
        _ => UserRole.User                  // 미지원값 폴백 유지(오탈자 시 최소권한)
    };

    /// <summary>power 계정(사용자 관리·공용 기본 프레임 관리 권한).</summary>
    public static bool IsPower(this UserRole role) => role is UserRole.Manager or UserRole.Admin;

    /// <summary>역할 한글 표시 라벨(계정 생성 콤보·사용자 관리 목록·팝오버 등, it13 §9.1). 미지원값은 "사용자".</summary>
    public static string ToLabel(this UserRole role) => role switch
    {
        UserRole.TempUser => "임시 유저",
        UserRole.User => "사용자",
        UserRole.Manager => "매니저",
        UserRole.Admin => "관리자",
        _ => "사용자"
    };

    /// <summary>
    /// actingRole이 생성할 수 있는 역할 목록(it2 §7, it13 §3.1): admin→[TempUser,User,Manager], manager→[TempUser,User], 그 외→[].
    /// (admin→admin 불가: 최종 1인 규칙. User를 만들 수 있으면 TempUser도 만들 수 있다 — 동일 위계에 TempUser 추가)
    /// </summary>
    public static IReadOnlyList<UserRole> CreatableRoles(this UserRole actingRole) => actingRole switch
    {
        UserRole.Admin => new[] { UserRole.TempUser, UserRole.User, UserRole.Manager },
        UserRole.Manager => new[] { UserRole.TempUser, UserRole.User },
        _ => Array.Empty<UserRole>()
    };

    /// <summary>actingRole이 role 계정을 생성할 권한이 있는지(게이트 판정).</summary>
    public static bool CanCreate(this UserRole actingRole, UserRole role)
        => actingRole.CreatableRoles().Contains(role);

    /// <summary>
    /// 위계 랭크(관리 판정 기준). 서수(enum 값)와 분리해 명시 — 역할 추가 시 여기만 갱신(서수 재배치 안전, it13 §3.2).
    /// 위계: TempUser &lt; User &lt; Manager &lt; Admin.
    /// </summary>
    private static int ManageRank(UserRole role) => role switch
    {
        UserRole.TempUser => 0,
        UserRole.User => 1,
        UserRole.Manager => 2,
        UserRole.Admin => 3,
        _ => 0
    };

    /// <summary>
    /// actingRole이 targetRole 계정을 관리(삭제·비밀번호 초기화 등)할 수 있는지: **자신과 같거나 낮은 위계만**.
    /// 위계 TempUser&lt;User&lt;Manager&lt;Admin. 예) manager는 admin을 관리 불가, admin은 전부 관리 가능.
    /// ⚠️ 서수 대소 비교가 아니라 <see cref="ManageRank"/> 명시 랭크로 판정(향후 역할 추가에도 안전).
    /// </summary>
    public static bool CanManage(this UserRole actingRole, UserRole targetRole)
        => ManageRank(targetRole) <= ManageRank(actingRole);
}
