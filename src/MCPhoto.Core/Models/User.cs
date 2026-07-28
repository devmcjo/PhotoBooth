namespace MCPhoto.Core.Models;

/// <summary>
/// 계정 인증 방식(it15 D2). DB 저장값은 소문자 provider 문자열("google"), UI 표기는 "Google SSO".
/// 추후 Kakao/Apple 추가 시 enum 값 + 매핑 1줄씩만 늘린다. (it15 설계 §5.2)
/// </summary>
public enum AuthMethod
{
    /// <summary>Google SSO. 현재 유일한 인증 수단. 서버 authMethod="google".</summary>
    Google,

    /// <summary>서버가 미지원/미설정 값을 보낸 경우의 폴백. UI는 "알 수 없음"으로 표기.</summary>
    Unknown
}

/// <summary>계정. 자격증명(비밀번호)은 보관하지 않는다 — 신원은 Google, 게이트는 PIN. (it15 설계 §5.2)</summary>
public sealed class User
{
    public string Id { get; set; } = string.Empty;

    public UserRole Role { get; set; } = UserRole.TempUser;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Google 계정 이메일(소문자 정규화). SSO 신원의 근거이므로 항상 존재한다.</summary>
    public string? Email { get; set; }

    /// <summary>인증 방식(D2). 서버 authMethod 파생, 기본 Google.</summary>
    public AuthMethod AuthMethod { get; set; } = AuthMethod.Google;

    /// <summary>진입 PIN 설정 여부(서버 pinHash!=null 파생). false면 최초 설정 강제.</summary>
    public bool HasPin { get; set; }
}

/// <summary>인증 방식 파싱·표기 단일 소스(it15 D2).</summary>
public static class AuthMethodExtensions
{
    /// <summary>서버 저장 문자열 → enum. 미지원값은 Unknown(조용한 오인 방지).</summary>
    public static AuthMethod ParseAuthMethod(string? value) =>
        value == "google" ? AuthMethod.Google : AuthMethod.Unknown;

    /// <summary>UI·진단 표기 라벨(D2: DB "google" ↔ 화면 "Google SSO").</summary>
    public static string ToLabel(this AuthMethod m) => m switch
    {
        AuthMethod.Google => "Google SSO",
        _ => "알 수 없음"
    };
}
