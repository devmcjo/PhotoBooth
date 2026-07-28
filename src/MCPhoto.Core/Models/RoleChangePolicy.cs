namespace MCPhoto.Core.Models;

/// <summary>
/// 역할 변경(setRole) 권한 매트릭스(순수 로직, 테스트 대상). 서버 §8.7 setRole 매트릭스와 **1:1 대칭**
/// (계약 드리프트 방지). UserMgmt 역할 변경 UI가 콤보에 넣을 역할 목록을 필터한다(클라 1차 방어, 서버가 최종 강제).
/// (it13 §9.5)
/// </summary>
public static class RoleChangePolicy
{
    /// <summary>
    /// actor가 target(현재 <paramref name="currentRole"/>)에게 지정 가능한 역할 목록(콤보 필터).
    /// 빈 목록이면 역할 변경 UI 미노출. 규칙(§8.7):
    ///   - target==Admin → 빈 목록(admin 대상 변경 불가, 누구도).
    ///   - actor==Admin → admin 제외 전부(승격·강등). 무변경(current==target)은 UI가 처리.
    ///   - actor==Manager && current==User → [User, TempUser](user→temp_user 강등만 유효).
    ///   - 그 외(manager의 다른 대상·비파워) → 빈 목록.
    /// </summary>
    public static IReadOnlyList<UserRole> AssignableRoles(UserRole actorRole, UserRole currentRole)
    {
        if (currentRole == UserRole.Admin) return Array.Empty<UserRole>();       // admin 대상 불가
        if (actorRole == UserRole.Admin)
            // admin: admin 제외 전부(승격·강등). currentRole 자신 포함 여부는 UI에서 무변경 처리.
            return new[] { UserRole.TempUser, UserRole.User, UserRole.Manager };
        if (actorRole == UserRole.Manager && currentRole == UserRole.User)
            return new[] { UserRole.User, UserRole.TempUser };                    // user→temp_user 강등만 유효
        return Array.Empty<UserRole>();                                           // 그 외 manager·비파워 미노출
    }
}
