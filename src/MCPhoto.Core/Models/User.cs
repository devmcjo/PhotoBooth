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
}
