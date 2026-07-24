namespace MCPhoto.Core.Models;

/// <summary>
/// 계정. ⚠️ MVP는 비밀번호 평문 저장(개인 사용 전제). 웹 접근 전면 차단이 방어선.
/// (PRD §6, firebase-contract §2.1)
/// </summary>
public sealed class User
{
    public string Id { get; set; } = string.Empty;

    /// <summary>⚠️ MVP 평문. 배포 시 해싱 필요(후순위).</summary>
    public string Password { get; set; } = string.Empty;

    public UserRole Role { get; set; } = UserRole.User;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>계정 이메일(소문자 정규화). 미수집/레거시 계정은 null. (item1a 설계 §4.1)</summary>
    public string? Email { get; set; }

    /// <summary>이메일 소유 확인 여부. 생성 시 false, verify 성공 시 true. (item1a 설계 §4.1)</summary>
    public bool EmailVerified { get; set; }
}
