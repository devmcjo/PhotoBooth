namespace MCPhoto.Core.Models;

/// <summary>
/// 계정 인증 방식(it14 설정 진입 게이트 분기). Sso=자동생성(sentinel 비번, PIN 게이트),
/// Password=일반(비번 게이트). 서버 authMethod와 1:1(미설정/미지원값은 Password 폴백). (설계 §5.1)
/// </summary>
public enum AuthMethod
{
    /// <summary>일반 계정(id/pw). 설정 진입 시 비밀번호 재확인 게이트.</summary>
    Password,

    /// <summary>SSO 자동생성 계정(sentinel 비번 — 아무도 모름). 설정 진입 시 전용 PIN 게이트.</summary>
    Sso
}

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

    /// <summary>it14: 인증 방식. Sso=설정 진입 PIN 게이트, Password=비번 게이트. 서버 파생, 기본 Password. (설계 §5.1)</summary>
    public AuthMethod AuthMethod { get; set; } = AuthMethod.Password;

    /// <summary>it14: 설정 진입 PIN 설정 여부(서버 pinHash!=null 파생). SSO+false=최초 설정 유도. (설계 §5.1)</summary>
    public bool HasPin { get; set; }
}
