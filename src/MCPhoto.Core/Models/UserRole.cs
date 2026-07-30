namespace MCPhoto.Core.Models;

/// <summary>계정 역할. (PRD §F8, it13 TempUser, it16 AdvancedUser)</summary>
public enum UserRole
{
    // ⚠️ 서수(enum 값)를 위계 비교에 쓰지 않는다(CanManage는 ManageRank switch). 배치값은 가독성용으로 위계 순 명시.
    //    저장·전송은 전부 ToFirestoreValue() 문자열이라 배치값 변경은 무해(it13 §1.1·§3.1).
    //    it16: AdvancedUser를 위계 순 위치(2)에 끼워 넣고 Manager·Admin을 3·4로 밀었다(설계 §3.1 안전 게이트 통과).

    /// <summary>임시 유저(it13): user와 동기능 + QR 전송(업로드+다운로드)만 시간·횟수 한도. 위계 최하위.</summary>
    TempUser = 0,

    /// <summary>AppSettings 관리. it16부터 프레임은 **사용만**(생성·편집·삭제 불가, E4).</summary>
    User = 1,

    /// <summary>고급 유저(it16): User 권한 + 프레임 생성·편집·삭제(개인 로컬). power 아님(계정 관리 권한 없음).</summary>
    AdvancedUser = 2,

    /// <summary>advanced_user + 사용자 관리 + 공용 기본 프레임 관리.</summary>
    Manager = 3,

    /// <summary>manager + manager 지정(최종 1인).</summary>
    Admin = 4
}

/// <summary>UserRole 문자열 매핑(Firestore 저장값과 일치).</summary>
public static class UserRoleExtensions
{
    public static string ToFirestoreValue(this UserRole role) => role switch
    {
        UserRole.TempUser => "temp_user",   // it13: C#↔TS↔Firestore 일관(snake_case)
        UserRole.User => "user",
        UserRole.AdvancedUser => "advanced_user",   // it16 §5.1 동결표
        UserRole.Manager => "manager",
        UserRole.Admin => "admin",
        _ => "user"
    };

    public static UserRole ParseRole(string? value) => value switch
    {
        "admin" => UserRole.Admin,
        "manager" => UserRole.Manager,
        "advanced_user" => UserRole.AdvancedUser,   // it16
        "temp_user" => UserRole.TempUser,   // it13
        "user" => UserRole.User,            // 명시(기존 default 폴백에서 승격)
        _ => UserRole.User                  // 미지원값 폴백 유지(오탈자 시 최소권한 — it16부터 프레임 쓰기도 없어 더 안전)
    };

    /// <summary>
    /// power 계정(사용자 관리·공용 기본 프레임 관리 권한).
    /// ⚠️ it16: AdvancedUser는 **여기에 포함되지 않는다**(계정 관리 권한 없음). 프레임 저작 권한은
    ///    별개 축인 <see cref="CanWriteFrames"/>다 — 두 판정을 서로 대체하지 않는다.
    /// </summary>
    public static bool IsPower(this UserRole role) => role is UserRole.Manager or UserRole.Admin;

    /// <summary>
    /// 프레임 쓰기 권한(생성·편집·삭제). AdvancedUser 이상. (it16 E2)
    /// ⚠️ <see cref="IsPower"/>와 **별개 축**이다: IsPower=계정 관리·공용 DB 프레임 관리, CanWriteFrames=프레임 저작.
    ///    AdvancedUser는 CanWriteFrames=true, IsPower=false다. 두 판정을 서로 대체하지 않는다.
    ///    <see cref="ManageRank"/> 부등식으로 쓰지 않는다 — 관리 위계에 역할이 끼어들 때 저작 권한이
    ///    조용히 따라 움직이는 것을 막기 위해 명시 열거를 유지한다(it13이 서수를 버린 것과 같은 이유).
    /// </summary>
    public static bool CanWriteFrames(this UserRole role)
        => role is UserRole.AdvancedUser or UserRole.Manager or UserRole.Admin;

    /// <summary>역할 한글 표시 라벨(계정 생성 콤보·사용자 관리 목록·팝오버 등, it13 §9.1). 미지원값은 "사용자".</summary>
    public static string ToLabel(this UserRole role) => role switch
    {
        UserRole.TempUser => "임시 유저",
        UserRole.User => "사용자",
        UserRole.AdvancedUser => "고급 유저",   // it16
        UserRole.Manager => "매니저",
        UserRole.Admin => "관리자",
        _ => "사용자"
    };

    /// <summary>
    /// actingRole이 생성할 수 있는 역할 목록(it2 §7, it13 §3.1, it16 §3.6):
    /// admin→[TempUser,User,AdvancedUser,Manager], manager→[TempUser,User,AdvancedUser], 그 외→[].
    /// (admin→admin 불가: 최종 1인 규칙)
    /// ⚠️ it15의 계정 생성 폐지로 프로덕션 호출자가 0이다(테스트만 참조). 삭제하지 않고 목록만 §3.3 매트릭스와
    ///    맞춰 둔다 — 훗날 되살아날 때 E3와 모순되는 규칙이 조용히 부활하는 것을 막는다.
    /// </summary>
    public static IReadOnlyList<UserRole> CreatableRoles(this UserRole actingRole) => actingRole switch
    {
        UserRole.Admin => new[] { UserRole.TempUser, UserRole.User, UserRole.AdvancedUser, UserRole.Manager },
        UserRole.Manager => new[] { UserRole.TempUser, UserRole.User, UserRole.AdvancedUser },
        _ => Array.Empty<UserRole>()
    };

    /// <summary>actingRole이 role 계정을 생성할 권한이 있는지(게이트 판정).</summary>
    public static bool CanCreate(this UserRole actingRole, UserRole role)
        => actingRole.CreatableRoles().Contains(role);

    /// <summary>
    /// 위계 랭크(관리 판정·표시 정렬 기준). 서수(enum 값)와 분리해 명시 — 역할 추가 시 여기만 갱신(서수 재배치 안전, it13 §3.2).
    /// 위계: TempUser &lt; User &lt; AdvancedUser &lt; Manager &lt; Admin. 서버 MANAGE_RANK와 동일(it16 §5.1).
    /// ⚠️ 권한 판정에 이 값을 직접 부등식으로 쓰지 않는다 — 관리 가능 여부는 <see cref="CanManage"/>,
    ///    프레임 저작은 <see cref="CanWriteFrames"/>다. 여기서 공개하는 목적은 **목록 정렬**(사용자 관리 화면)이다.
    /// </summary>
    public static int HierarchyRank(this UserRole role) => role switch
    {
        UserRole.TempUser => 0,
        UserRole.User => 1,
        UserRole.AdvancedUser => 2,   // it16
        UserRole.Manager => 3,
        UserRole.Admin => 4,
        _ => 0
    };

    /// <summary>위계 랭크(내부 별칭 — 기존 관리 판정 표현 유지).</summary>
    private static int ManageRank(UserRole role) => role.HierarchyRank();

    /// <summary>
    /// actingRole이 targetRole 계정을 관리(삭제·PIN 재설정 등)할 수 있는지: **자신과 같거나 낮은 위계만**.
    /// 위계 TempUser&lt;User&lt;AdvancedUser&lt;Manager&lt;Admin. 예) manager는 admin을 관리 불가, admin은 전부 관리 가능.
    /// ⚠️ 서수 대소 비교가 아니라 <see cref="ManageRank"/> 명시 랭크로 판정(향후 역할 추가에도 안전).
    /// ⚠️ 이 판정만으로는 비power도 통과한다 — 관리 액션 게이트는 <see cref="IsPower"/>와 **함께** 쓴다(it16 §3.5).
    /// </summary>
    public static bool CanManage(this UserRole actingRole, UserRole targetRole)
        => ManageRank(targetRole) <= ManageRank(actingRole);
}
