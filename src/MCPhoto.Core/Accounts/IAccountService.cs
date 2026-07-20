namespace MCPhoto.Core.Accounts;

using MCPhoto.Core.Models;

/// <summary>
/// 계정 로그인/CRUD/역할. Firestore users. ⚠️ MVP 평문 비교. (PRD §F8, firebase-contract §2.1)
/// </summary>
public interface IAccountService
{
    /// <summary>id/pw 로그인. 성공 시 User, 실패 시 null(평문 비교, MVP).</summary>
    Task<User?> LoginAsync(string id, string password, CancellationToken ct = default);

    /// <summary>계정 생성(id/pw만). 중복 id면 예외.</summary>
    Task<User> CreateAsync(string id, string password, UserRole role = UserRole.User, CancellationToken ct = default);

    /// <summary>비밀번호 변경.</summary>
    Task ChangePasswordAsync(string id, string newPassword, CancellationToken ct = default);

    /// <summary>전체 계정 목록(power 전용 사용자 관리).</summary>
    Task<IReadOnlyList<User>> GetAllAsync(CancellationToken ct = default);

    /// <summary>계정 삭제 + 소유 프레임 cascade 삭제(§F8).</summary>
    Task DeleteAsync(string id, CancellationToken ct = default);

    /// <summary>역할 변경(admin만 manager 지정).</summary>
    Task SetRoleAsync(string id, UserRole role, CancellationToken ct = default);

    /// <summary>시드 계정(devmcjo/1111/admin) 없으면 생성.</summary>
    Task EnsureSeedAccountAsync(CancellationToken ct = default);
}
